using FDG.Ai;
using FDG.Campaign;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #435 - the Underdog Bonus reaching a game that is actually being played.
    ///
    /// <para>
    /// <see cref="UnderdogBonusTests"/> covers the pool and the spend: 22 cases over the arithmetic, the
    /// sign of the modifier, who gets asked and the one-point-per-roll cap. What it could not tell you is
    /// whether <b>a game ever seeds the pool</b> - and #434 shipped in exactly that state. Every one of
    /// those 22 tests passed while the Bonus did nothing whatsoever in play, because each of them seeded
    /// the pool itself. That is the lesson this suite has now written down four times (#424, #429/#430,
    /// #433, here): a test that calls the new code proves the code works and nothing about the wiring.
    /// </para>
    ///
    /// <para>
    /// So these play whole AI-vs-AI games with two campaign armies of deliberately unequal value and read
    /// the log a front end would receive. <c>UnderdogPool.SeedFromSlots</c> is called once, from
    /// <c>FDGServer.BuildContextAndLaunch</c>; delete that line and the first test here fails and nothing
    /// else in the suite notices.
    /// </para>
    ///
    /// <para>
    /// <b>Seat 0 is wrapped and seat 1 is not</b>, and that is load-bearing rather than incidental. The
    /// house rule is that underdog points go to a human only, and the engine reads that from the
    /// controller - so the wrapped seat counts as a player and the raw
    /// <see cref="ComputerPlayerController"/> counts as a bot. Which force is the smaller one then
    /// decides whether anything is handed out at all, which is what the two tests below contrast.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UnderdogInAPlayedGameTests
    {
        /// <summary>
        /// Forces the army's value to exactly <paramref name="points"/>: every unit to zero, then the
        /// whole total onto the first. The engine reads the force brought as the army file's TotalPoints,
        /// and nothing in a game depends on a unit's own cost.
        /// <para>
        /// Applied to the ORDINARY armies too, not only the campaign ones, and that is deliberate. With
        /// the test armies left at their own costs the two sides were close enough in value that the
        /// ordinary-game test passed whether or not the campaign guard was there - a sabotage that let a
        /// non-campaign army into the pool walked straight past it. Both games are now a full mission cap
        /// apart, so the only thing keeping an ordinary game's log clean is the guard being tested.
        /// </para>
        /// </summary>
        private static ArmyListFile WithPoints(ArmyListFile army, int points)
        {
            foreach (UnitFileEntry unit in army.Units) unit.PointCost = 0;
            army.UnattributedPoints = 0;
            army.Units[0].PointCost = points;
            return army;
        }

        /// <summary>
        /// A campaign sheet on every unit and a strike-team block - which is all a campaign army is,
        /// since the sheet rides the unit from the file into play.
        /// </summary>
        private static ArmyListFile AsCampaignArmy(ArmyListFile army, string name, bool isBot)
        {
            foreach (UnitFileEntry unit in army.Units)
                unit.StrikeUnitJson = new StrikeUnit { TeamFile = name + ".fdgarmy" }.ToJson();

            army.StrikeTeam = new StrikeTeamInfo
            {
                Name = name,
                GameSystem = EGameSystem.GrimdarkFuture,
                IsBot = isBot,
            };
            return army;
        }

        /// <summary>
        /// The log a front end would see. The AI's own controller throws log messages away
        /// (<c>ComputerPlayerController.SendLogMessage</c> is deliberately empty), so the only way to
        /// read what the game said is to wrap one seat's controller - which doubles as this fixture's
        /// "this seat is a human" marker.
        /// </summary>
        private sealed class LogCapturingController : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string> _lines;

            internal LogCapturingController(IPlayerController inner, List<string> lines)
            {
                _inner = inner;
                _lines = lines;
            }

            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;

            public event Action<bool> OnReadyStateChanged
            {
                add => _inner.OnReadyStateChanged += value;
                remove => _inner.OnReadyStateChanged -= value;
            }

            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer
            {
                add => _inner.OnMessageSentByPlayer += value;
                remove => _inner.OnMessageSentByPlayer -= value;
            }

            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();

            public void SendLogMessage(string logMessage, TextColor color)
            {
                lock (_lines) _lines.Add(logMessage);
                _inner.SendLogMessage(logMessage, color);
            }

            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType,
                                          string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        /// <summary>
        /// Plays a campaign game where seat 0 (the human) brings <paramref name="yourPoints"/> and seat 1
        /// (the bot) brings <paramref name="theirPoints"/>, and returns the log.
        /// </summary>
        private static async Task<List<string>> PlayAGame(int seed, int yourPoints, int theirPoints,
                                                          bool campaign = true)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // One bus for the whole game - the AI controllers' games and the server must share it, or
            // the server's requests never reach a resolver and the game never finishes.
            var bus = new InProcessBus();
            var log = new List<string>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = WithPoints(
                    i == 0 ? TestArmies.MakeShooterArmy() : TestArmies.MakeDefenderArmy(),
                    i == 0 ? yourPoints : theirPoints);
                if (campaign) army = AsCampaignArmy(army, i == 0 ? "Yours" : "Theirs", isBot: i == 1);

                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID);
                // Seat 0 wrapped (so the engine reads it as a human), seat 1 raw (so it reads as a bot).
                slots[i].AssignPlayerController(
                    i == 0 ? new LogCapturingController(ai, log) : ai);
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = EGameSystem.GrimdarkFuture;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            lock (log) return log.ToList();
        }

        /// <summary>
        /// The two lines the Bonus itself writes: the seeding announcement and a spend. Matched on their
        /// own wording rather than on the word "underdog", because the save-roll stage prints its whole
        /// modifier breakdown - "markers +0, underdog 0, ..." - on every save in every game, campaign or
        /// not, and that line says nothing about whether a pool exists.
        /// </summary>
        private static List<string> UnderdogLines(IEnumerable<string> log) =>
            log.Where(l => l.Contains("is the underdog this mission", StringComparison.Ordinal)
                           || l.StartsWith("Underdog point spent", StringComparison.Ordinal))
               .ToList();

        /// <summary>
        /// The claim #434 could not make: a played campaign mission where the player brings less actually
        /// hands them underdog points.
        /// <para>
        /// 675 against 1,000 is a 325pt gap - Grimdark Future's whole mission cap, so the widest a legal
        /// mission can be, and exactly five steps of its 65pt rate. The expected number is arithmetic,
        /// not a dice roll. (Set these points further apart than the cap and the engine pays more than
        /// five, correctly: the Bonus rule has no ceiling of its own, and keeping the forces legal is
        /// Mission Setup's job.)
        /// </para>
        /// </summary>
        [Test]
        [CancelAfter(600_000)]
        public async Task APlayedCampaignMission_GivesTheSmallerPlayerTheirUnderdogPoints()
        {
            List<string> lines = UnderdogLines(await PlayAGame(7311, yourPoints: 675, theirPoints: 1000));

            Assert.That(lines, Is.Not.Empty,
                "a campaign mission a full mission cap apart announced no underdog bonus at all - the pool is almost "
                + "certainly never seeded. This is the assertion that fails if the SeedFromSlots call in "
                + "FDGServer.BuildContextAndLaunch is deleted, and nothing in UnderdogBonusTests would "
                + "notice.");
            Assert.That(lines.Any(l => l.Contains("Yours is the underdog", StringComparison.Ordinal)
                                       && l.Contains("5 underdog point", StringComparison.Ordinal)),
                Is.True,
                "325 pts behind at 65 pts a point is exactly 5 points: " + string.Join(" | ", lines));
        }

        /// <summary>
        /// The house rule, in a real game (Kai, 2026-10-30): the bot is the smaller force here and must
        /// be given nothing. The books would hand it five points; a bot cannot judge which roll is worth
        /// one, and five invisible free +1s would make a mission harder in a way the player could neither
        /// see nor plan around.
        /// <para>
        /// Dropping the <c>isBot</c> check in <c>UnderdogPool.Seed</c> fails this.
        /// </para>
        /// </summary>
        [Test]
        [CancelAfter(600_000)]
        public async Task APlayedCampaignMission_NeverGivesTheBotUnderdogPoints()
        {
            List<string> lines = UnderdogLines(await PlayAGame(7311, yourPoints: 1000, theirPoints: 675));

            Assert.That(lines, Is.Empty,
                "the bot was the smaller force, so nobody should have had a point: "
                + string.Join(" | ", lines));
        }

        /// <summary>
        /// And the guard: an ordinary game has no campaign sheets, so no pool and no prompt. The core
        /// rules ask for <i>"two armies of equal point cost"</i> and have no underdog rule at all, so an
        /// underdog offer in a standard game would be a bug every player in it could see.
        /// </summary>
        [Test]
        [CancelAfter(600_000)]
        public async Task AnOrdinaryGame_HasNoUnderdogBonusAtAll()
        {
            List<string> lines = UnderdogLines(
                await PlayAGame(7311, yourPoints: 675, theirPoints: 1000, campaign: false));

            Assert.That(lines, Is.Empty,
                "a standard game offered an underdog bonus: " + string.Join(" | ", lines));
        }
    }
}
