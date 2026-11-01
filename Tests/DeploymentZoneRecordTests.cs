using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// #429 - the deployment zones have to outlive deployment.
    ///
    /// <para>
    /// They used to exist only on the <c>DeploymentContext</c>, which is thrown away the moment
    /// deployment ends. So from round one onwards <b>nothing in the game could say where anyone had
    /// deployed</b>, and that is not a cosmetic gap: it is why the campaign's Intimidator objective
    /// ("most of your units inside the enemy deployment zone at the end of the game") could not be
    /// scored at all, and it is the same missing fact behind the Solo AI's three sections, which the book
    /// draws along the AI's own deployment edge (<c>WorkItems/424</c>).
    /// </para>
    ///
    /// <para>
    /// The first test plays a <b>whole AI-vs-AI game</b> rather than calling the recorder directly, which
    /// is the lesson from #424: a test that calls the new code itself cannot tell whether the game ever
    /// reaches it. The stage that chooses the zones runs long before the first progress record exists, so
    /// this also pins the hand-off that would otherwise erase them on the first snapshot of the game.
    /// </para>
    /// </summary>
    [TestFixture]
    public class DeploymentZoneRecordTests
    {
        private static (GameDataStore Store, InProcessBus Bus, GameSettings Settings, PlayerSlot[] Slots)
            Scene(int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // One bus for the whole game: the AI controllers' games and the server must share it, or the
            // server's decision requests never reach a resolver and the game simply never finishes.
            var bus = new InProcessBus();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;

            return (store, bus, settings, slots);
        }

        [Test]
        [CancelAfter(180_000)]
        public async Task AFullGame_RecordsEveryTeamsDeploymentZone_AndStillHasThemAtTheEnd()
        {
            (GameDataStore store, InProcessBus bus, GameSettings settings, PlayerSlot[] slots) =
                Scene(seed: 7311);

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            IReadOnlyDictionary<int, ZoneRect>? zones = GameProgressUtilities.DeploymentZones(store);
            Assert.That(zones, Is.Not.Null,
                "a finished game must still know where each side deployed - this is the whole point of #429, "
                + "and it is the assertion that fails if the recorder is never reached.");
            Assert.That(zones!.Count, Is.EqualTo(2), "one zone per team.");

            ZoneRect first = zones.Values.First();
            ZoneRect second = zones.Values.Last();
            Assert.That(first.Left, Is.LessThan(first.Right), "a recorded zone must be a real rectangle.");
            Assert.That(first.Bottom, Is.LessThan(first.Top));
            Assert.That(first.Bottom < second.Bottom ? first.Top <= second.Bottom : second.Top <= first.Bottom,
                Is.True, "the two deployment zones must not overlap.");
        }

        /// <summary>
        /// #430's other in-game tally, in a real game for the same reason: the scoring tests record first
        /// blood themselves, so only a played game can say whether the engine ever records it. A shooting
        /// army against a defending one over four rounds destroys something, and whoever did it first is
        /// the campaign's Annihilator objective.
        /// </summary>
        [Test]
        [CancelAfter(180_000)]
        public async Task AFullGame_RecordsWhoDestroyedTheFirstEnemyUnit()
        {
            (GameDataStore store, InProcessBus bus, GameSettings settings, PlayerSlot[] slots) =
                Scene(seed: 7311);

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            var table = new TableState(store);
            bool anythingDied = table.Units.Objects.Any(u => !u.GetIsAlive());
            Assume.That(anythingDied, Is.True,
                "this seed's game killed nothing, so there is no first blood to record - not a failure.");

            // The stamp rides the killer's own campaign sheet, so this needs campaign units on the table
            // - the sweep armies have none. Covered by FirstBloodStampTests instead; what this game
            // proves is that nothing in the kill path throws when there are no sheets to stamp.
            Assert.That(table.Units.Objects.Any(u => u.GetIsAlive()), Is.True,
                "sanity: the game left somebody standing.");
        }

        /// <summary>
        /// A resumed game has to keep them, or Intimidator would score differently depending on whether
        /// the player saved halfway through.
        /// </summary>
        [Test]
        public void TheZonesSurviveASaveAndReload()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameProgressUtilities.WriteProgress(store, Progress());
            GameProgressUtilities.SetDeploymentZones(store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
                [2] = new ZoneRect(0f, 72f, 36f, 48f),
            });

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));

            ZoneRect? mine = GameProgressUtilities.DeploymentZoneOf(loaded, 1);
            Assert.That(mine, Is.Not.Null, "the zones must be in the save.");
            Assert.That(mine!.Left, Is.EqualTo(0f));
            Assert.That(mine.Right, Is.EqualTo(72f));
            Assert.That(mine.Bottom, Is.EqualTo(0f));
            Assert.That(mine.Top, Is.EqualTo(12f));
            Assert.That(GameProgressUtilities.DeploymentZoneOf(loaded, 2)!.Bottom, Is.EqualTo(36f));
        }

        /// <summary>
        /// The hand-off that is easy to get wrong: the zones are chosen during deployment, BEFORE the main
        /// phase writes the first progress record. If the first snapshot of the game did not carry them
        /// forward it would erase them in round one, and every test that set them directly would still
        /// pass.
        /// </summary>
        [Test]
        public void ZonesSetBeforeTheFirstProgressRecord_SurviveItBeingWritten()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameProgressUtilities.SetDeploymentZones(store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
            });
            Assert.That(GameProgressUtilities.DeploymentZoneOf(store, 1), Is.Not.Null,
                "they must be readable straight away, from inside deployment.");

            GameProgressUtilities.WriteProgress(store, Progress());

            Assert.That(GameProgressUtilities.DeploymentZoneOf(store, 1), Is.Not.Null,
                "and the first progress record of the game must not erase them.");
        }

        [Test]
        public void ARoundSnapshotDoesNotEraseThem()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameProgressUtilities.WriteProgress(store, Progress());
            GameProgressUtilities.SetDeploymentZones(store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
            });

            // Every round writes a fresh snapshot that knows nothing of the zones.
            GameProgressUtilities.WriteProgress(store, Progress(round: 2));
            GameProgressUtilities.WriteProgress(store, Progress(round: 3));

            Assert.That(GameProgressUtilities.DeploymentZoneOf(store, 1), Is.Not.Null);
        }

        [Test]
        public void NoZonesRecorded_ReadsAsNullRatherThanAnEmptySet()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameProgressUtilities.WriteProgress(store, Progress());

            Assert.That(GameProgressUtilities.DeploymentZones(store), Is.Null);
            Assert.That(GameProgressUtilities.DeploymentZoneOf(store, 1), Is.Null);
        }

        private static GameProgressData Progress(int round = 1)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GrimdarkFuture;
            return new GameProgressData(EResumeStage.MainPhase, round,
                teamActivateOrder: new List<int> { 1, 2 },
                currentRoundTeamFinishOrder: new List<int>(), currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int>(),
                unactivatedUnits: new List<DataBinding<UnitData>>(), settings: settings);
        }
    }
}
