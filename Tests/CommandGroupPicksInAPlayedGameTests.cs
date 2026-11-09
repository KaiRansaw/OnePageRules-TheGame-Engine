using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #439 - the Command Group picks reaching a game that is actually being played.
    ///
    /// <para>
    /// <see cref="CommandGroupPickedUnitsTests"/> covers the pieces: the per-system allowance, the grant
    /// reaching the picked unit's rules, and the revocation being tied to its own bearer. What it cannot
    /// tell you is whether <b>the game ever runs the stage</b> - the same lesson this suite has now
    /// written down three times (#424, #429/#430, then #432, which caught two real bugs no unit test
    /// could). <see cref="PickCommandGroupUnitsStage"/> is wired into
    /// <see cref="DeploymentStage"/> between the first-deployment roll-off and unit placement; delete
    /// that wiring and every test in the other fixture still passes.
    /// </para>
    ///
    /// <para>
    /// So this plays whole AI-vs-AI games with Banner-carrying armies and reads the picks out of the log.
    /// The armies are the ordinary test armies with a real Banner definition embedded
    /// (<see cref="ArmyListFile.RuleDefinitions"/>, which <c>GameBootstrap.BuildRuleResolver</c>
    /// registers) and referenced from one unit - which is exactly how a bundled army book carries it.
    /// </para>
    ///
    /// <para>
    /// <b>The negative half matters as much.</b> The same game in Grimdark Future must produce not one
    /// pick, because GF's Banner covers the bearer's own unit and nobody else - and the thing preventing
    /// it is one profile number that nothing else tests end to end.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CommandGroupPicksInAPlayedGameTests
    {
        /// <summary>A played Firefight / Skirmish game leaves the global table at 4' x 4' - put it back, or a
        /// later fixture that assumes the default 6' x 4' table (MoveStaysOnTableTests) fails depending on order.</summary>
        [TearDown]
        public void RestoreTheTable() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        /// <summary>Banner as #438 gates it: the combat-only morale bonus.</summary>
        private static SpecialRuleDefinition BannerDefinition() => new("Banner",
            new[]
            {
                new HookEntry(EHookID.Morale_OnPreMoraleTest,
                    new Condition.MoraleTriggeredByCombat(),
                    new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>(),
            Description: "The unit gets +1 to morale test rolls triggered by shooting or melee.");

        /// <summary>
        /// The army with a Banner on its first unit, carried the way a book carries it: the definition
        /// embedded on the army file, and the unit referencing it by name.
        /// </summary>
        private static ArmyListFile WithABanner(ArmyListFile army)
        {
            army.RuleDefinitions.Add(BannerDefinition());
            army.Units[0].SpecialRules.Add(new SpecialRuleEntry_Core("Banner"));
            return army;
        }

        /// <summary>
        /// The log a front end would see. The AI's own controller throws log messages away
        /// (<c>ComputerPlayerController.SendLogMessage</c> is deliberately empty), so wrapping it is the
        /// only way to read what the game said - and the least invasive, since the game is otherwise
        /// untouched and genuinely driven by the official AI.
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

        private static async Task<List<string>> PlayAGame(int seed, EGameSystem system)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            // One bus for the whole game - the AI controllers' games and the server must share it, or the
            // server's requests never reach a resolver and the game never finishes (#429's trap).
            var bus = new InProcessBus();
            var log = new List<string>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                // MakeRichArmy, not the shooter/defender pair: those are ONE unit each, so a Banner
                // there has nobody to confer on and the game correctly picks nothing. Found by this
                // very test, which is a fair summary of why it exists.
                ArmyListFile army = WithABanner(TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs"));

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

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            lock (log) return log.ToList();
        }

        /// <summary>The stage's own line, which is the only trace a pick leaves in the log.</summary>
        private static List<string> PickLines(IEnumerable<string> log) =>
            log.Where(line => line.Contains("Banner on ", StringComparison.Ordinal)
                           && line.Contains(" also covers ", StringComparison.Ordinal)).ToList();

        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        [TestCase(EGameSystem.GFStarQuest)]
        [TestCase(EGameSystem.AoFQuest)]
        public async Task InTheFourWideGames_TheBannerCoversPickedUnits(EGameSystem system)
        {
            List<string> log = await PlayAGame(seed: 5150, system);

            List<string> picks = PickLines(log);

            // Two armies, one Banner each, two picks allowed - but each test army is small, so the real
            // claim is "the stage ran and granted at least once", not an exact count.
            Assert.That(picks, Is.Not.Empty,
                $"{system}: its book gives a Banner two picked friendly units, and a played game should "
                + "show them being picked. An empty list means the stage never ran - check that "
                + "DeploymentStage still binds RollForFirstDeployment -> PickCommandGroupUnits -> "
                + "DeployAllUnits.");
        }

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        public async Task InTheTwoNarrowGames_NothingIsPicked(EGameSystem system)
        {
            List<string> log = await PlayAGame(seed: 5150, system);

            Assert.That(PickLines(log), Is.Empty,
                $"{system}: its Banner reads \"this model AND ITS UNIT\". A pick here would be a rule "
                + "from another game leaking into this one.");
        }

        /// <summary>
        /// The game still finishes. A stage that asks questions during deployment is exactly the kind of
        /// change that can deadlock a game rather than break a rule, and the other assertions here would
        /// all pass right up until the 120s timeout fired.
        /// </summary>
        [Test]
        public async Task TheGameStillPlaysToAResult_WithThePicksInThePath()
        {
            List<string> log = await PlayAGame(seed: 991, EGameSystem.GFFirefight);

            Assert.That(log, Is.Not.Empty);
            Assert.That(PickLines(log), Is.Not.Empty);
        }
    }
}
