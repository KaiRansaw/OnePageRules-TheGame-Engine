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
    /// #432 - the random events reaching a game that is actually being played.
    ///
    /// <para>
    /// <see cref="CampaignEventRunnerTests"/> covers the runner: 48 cases over the targeting, the 36
    /// branches and the round-end clear. What it cannot tell you is whether <b>the game ever calls it</b>
    /// - and that is the lesson already written down twice in this suite (#424, then #429/#430): a test
    /// that calls the new code itself proves the code works and nothing about the wiring. The roll was
    /// added to <c>StartOfRoundExtraActionStage</c> and the sweep to <c>ReconcileObjectivesStage</c>; if
    /// either call were deleted, every test in that other fixture would still pass.
    /// </para>
    ///
    /// <para>
    /// So this plays a whole AI-vs-AI game whose units carry campaign sheets, captures the log the way a
    /// front end receives it, and reads the event lines out of it. The armies are the ordinary test
    /// armies with a <see cref="StrikeUnit"/> stamped on each unit entry - which is all a campaign army
    /// is, since the sheet rides the unit from the file (<c>UnitFileEntry.StrikeUnitJson</c>) into play.
    /// </para>
    ///
    /// <para>
    /// <b>The negative half matters as much.</b> The same game without sheets must produce not one event
    /// line, because a random event in an ordinary game would be a bug visible to every player in the
    /// game - and the guard that prevents it is one line that nothing else tests end to end.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignEventInAPlayedGameTests
    {
        /// <summary>A copy of the army with a campaign sheet on every unit, which is what makes it a campaign army.</summary>
        private static ArmyListFile AsCampaignArmy(ArmyListFile army, string teamFile)
        {
            foreach (UnitFileEntry unit in army.Units)
                unit.StrikeUnitJson = new StrikeUnit { TeamFile = teamFile }.ToJson();
            return army;
        }

        /// <summary>
        /// The log a front end would see. The AI's own controller throws log messages away
        /// (<c>ComputerPlayerController.SendLogMessage</c> is deliberately empty), so the only way to read
        /// what the game said is to wrap it - which is also the least invasive way, since the game is
        /// otherwise untouched and genuinely driven by the official AI.
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

            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        private static (GameDataStore Store, InProcessBus Bus, GameSettings Settings, PlayerSlot[] Slots,
            List<string> Log) Scene(int seed, bool campaign,
                EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // One bus for the whole game - the AI controllers' games and the server must share it, or the
            // server's requests never reach a resolver and the game never finishes (#429's own trap).
            var bus = new InProcessBus();
            var log = new List<string>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = i == 0 ? TestArmies.MakeShooterArmy() : TestArmies.MakeDefenderArmy();
                if (campaign) army = AsCampaignArmy(army, $"camp{i}.fdgarmy");

                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID);
                slots[i].AssignPlayerController(new LogCapturingController(ai, log));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;

            return (store, bus, settings, slots, log);
        }

        private static async Task<List<string>> PlayAGame(int seed, bool campaign,
            EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            (GameDataStore store, InProcessBus bus, GameSettings settings, PlayerSlot[] slots,
                List<string> log) = Scene(seed, campaign, system);

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            lock (log) return log.ToList();
        }

        private static List<string> EventLines(IEnumerable<string> log) =>
            log.Where(l => l.StartsWith("Campaign event ", StringComparison.Ordinal)).ToList();

        /// <summary>
        /// Four rounds, two players rolling one die each round, firing on a 5+: about 2.7 events expected.
        /// Several seeds are played and the fired events pooled, because any single seed can legitimately
        /// roll eight misses - the claim being pinned is that the game reaches the roll at all, and a seed
        /// that rolled nothing would say nothing about that either way.
        /// </summary>
        [Test]
        [CancelAfter(600_000)]
        public async Task ACampaignGame_RollsItsRandomEvents_AndSaysWhatHappened()
        {
            var fired = new List<string>();
            foreach (int seed in new[] { 7311, 4242, 991, 5150 })
            {
                fired.AddRange(EventLines(await PlayAGame(seed, campaign: true)));
                if (fired.Count > 0) break;
            }

            Assert.That(fired, Is.Not.Empty,
                "four played campaign games rolled 32 dice between them and fired no event at all - the "
                + "roll is almost certainly never reached. This is the assertion that fails if the call "
                + "in StartOfRoundExtraActionStage is deleted, and nothing in CampaignEventRunnerTests "
                + "would notice.");

            // Each line is the book's own text, and the runner always follows it with a second line
            // saying what it did - including when it did nothing, which is a real outcome.
            Assert.That(fired.All(l => l.Contains(':')), Is.True,
                "an event line must name the event, not just announce that one happened.");
        }

        /// <summary>
        /// And the guard, in a real game: an ordinary army has no sheets, so nothing fires. Sabotaging
        /// <c>IsCampaignGame</c> to return true fails this and nothing else in the suite.
        /// </summary>
        /// <summary>
        /// #433, in a played game: an Age of Fantasy campaign must never be told a <i>Solar Flare</i>
        /// happened. Asserted as "no science-fiction-only name appears", rather than "a fantasy name
        /// appears", because which event fires is a dice roll - 15 of the 36 are named identically in both
        /// books, so a run could legitimately produce only shared names and that is not a failure.
        /// <para>
        /// This is the test that proves the RUNNER consults the skin. Everything in
        /// <c>CampaignEventNameTests</c> would still pass if the runner logged <c>fired.Name</c> directly.
        /// </para>
        /// </summary>
        [Test]
        [CancelAfter(600_000)]
        public async Task AnAgeOfFantasyCampaign_NeverAnnouncesAScienceFictionEventName()
        {
            // The canonical names of the 21 events the fantasy books rename - exactly the strings that
            // must never reach an Age of Fantasy log.
            var sciFiOnly = CampaignEventNames.RenamedCodes
                .Select(code => CampaignEvents.ByCode(code).Name)
                .ToList();

            var fired = new List<string>();
            foreach (int seed in new[] { 7311, 4242, 991, 5150 })
                fired.AddRange(EventLines(await PlayAGame(seed, campaign: true,
                    system: EGameSystem.AgeOfFantasy)));

            Assume.That(fired, Is.Not.Empty, "no event fired across four games, so there is nothing to check.");

            foreach (string line in fired)
                foreach (string sciFi in sciFiOnly)
                    Assert.That(line, Does.Not.Contain(sciFi),
                        $"an Age of Fantasy campaign announced '{sciFi}': {line}");
        }

        [Test]
        [CancelAfter(600_000)]
        public async Task AnOrdinaryGame_FiresNoRandomEventAtAll()
        {
            foreach (int seed in new[] { 7311, 4242, 991, 5150 })
            {
                List<string> events = EventLines(await PlayAGame(seed, campaign: false));
                Assert.That(events, Is.Empty,
                    $"seed {seed}: a non-campaign game fired {events.Count} random event(s). Random events "
                    + "are a campaign mechanic, and firing them in an ordinary game would be visible to "
                    + "every player in it.");
            }
        }
    }
}
