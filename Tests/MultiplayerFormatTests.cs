using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Ai;
using FDG.GameModel;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// The Advanced Rulebooks' multiplayer formats (3.6.0, Multiplayer Games): a team's points limit is split
    /// between its players, and King of the Hill's king and combat bonus points.
    /// </summary>
    [TestFixture]
    public class MultiplayerFormatTests
    {
        private static ArmyListFile Army(int points) => new ArmyListFile
        {
            Name = "A", Faction = "A", GameSystem = GameSystems.GrimdarkFuture, PointsLimit = 99999,
            Units = new() { new UnitFileEntry { Name = "U", ModelCount = 1, Quality = 4, Defense = 4, PointCost = points } },
        };

        [Test]
        public void ATeamSplitsTheLimit_UnevenlyIfItLikes()
        {
            // 2v1 at 1000: the pair may split 1000 as 700 + 300; the lone player has 1000.
            var ok = new[] { (0, "Kai", (ArmyListFile?)Army(700)), (0, "Friend", Army(300)), (1, "Bot", Army(1000)) };
            Assert.That(LaunchGate.BlockingProblems(ok, 1000, EAllowedGameSystems.All), Is.Empty);

            var over = new[] { (0, "Kai", (ArmyListFile?)Army(700)), (0, "Friend", Army(400)), (1, "Bot", Army(1000)) };
            Assert.That(LaunchGate.BlockingProblems(over, 1000, EAllowedGameSystems.All).Single(),
                Does.Contain("1100 pts together"));
        }

        [Test]
        public void KingOfTheHill_EarnsOnTheKing_SpendsOncePerActivation()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var players = Enumerable.Range(0, 3).Select(_ => new PlayerID(Guid.NewGuid())).ToArray();
            for (int i = 0; i < 3; i++)
            {
                store.Create(new PlayerSlotInfo(players[i], i, i, $"P{i}", true));
                store.Create(new TeamData(i, new List<PlayerID> { players[i] }));
            }
            UnitData Unit(PlayerID owner, int cost)
            {
                var m = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), store)));
                var u = new UnitData(owner, "U", 4, 4, new List<DataBinding<ModelData>> { m }) { PointCost = cost };
                return store.GetDataBinding<UnitData>(store.Create(u)).GetValue();
            }
            UnitData kingsUnit = Unit(players[0], 500);
            UnitData other = Unit(players[1], 300);
            Unit(players[2], 300);

            GameSettings settings = GameSettings.GetDefault();
            settings.KingOfTheHill = true;
            GameProgressUtilities.WriteProgress(store, new GameProgressData(EResumeStage.MainPhase, 1,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(), new List<DataBinding<UnitData>>(), settings));
            var game = new TestGameContext(store, new FixedDiceRoller(4), settings: settings);

            Assert.That(KingOfTheHill.Kings(game.TableState), Is.EquivalentTo(new[] { players[0] }), "the biggest army is king");

            KingOfTheHill.OnTargetPicked(game, players[1], other);      // not the king: nothing
            Assert.That(KingOfTheHill.Status(game, players[1]).Points, Is.Zero);
            KingOfTheHill.OnTargetPicked(game, players[1], kingsUnit);  // the king's unit: +1
            KingOfTheHill.OnTargetPicked(game, players[1], kingsUnit);
            Assert.That(KingOfTheHill.Status(game, players[1]).Points, Is.EqualTo(2));

            Assert.That(KingOfTheHill.TrySpend(game, players[1]), Is.True);
            Assert.That(KingOfTheHill.TrySpend(game, players[1]), Is.False, "once per activation");
            Assert.That(KingOfTheHill.Status(game, players[1]).Points, Is.EqualTo(1));
        }

        /// <summary>Three bots, free-for-all in the corners, King of the Hill on: the game plays to the end,
        /// every side deployed inside its own corner.</summary>
        [Test]
        [CancelAfter(240_000)]
        public async Task ThreeSides_DeployInCorners_AndPlayKingOfTheHillToTheEnd()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[3];
            for (int i = 0; i < 3; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 1 ? MakeDefenderArmy() : MakeShooterArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(EAiProfile.SoloCoOp,
                    $"AI {i}", slots[i].PlayerID, aiGame, 4242, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 4242;
            settings.KingOfTheHill = true;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += r => completed.TrySetResult(r);
            Assert.That(await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(200))), Is.SameAs(completed.Task));

            IReadOnlyDictionary<int, ZoneRect>? zones = GameProgressUtilities.DeploymentZones(store);
            Assert.That(zones, Is.Not.Null);
            foreach (ZoneRect z in zones!.Values)
            {
                Assert.That(z.Right - z.Left, Is.EqualTo(24f).Within(0.01f), "2' along the edge");
                Assert.That(z.Top - z.Bottom, Is.EqualTo(12f).Within(0.01f), "1' deep");
                Assert.That(z.Left < 0.01f || z.Right > GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES - 0.01f, Is.True, "in a corner");
            }
        }
    }
}
