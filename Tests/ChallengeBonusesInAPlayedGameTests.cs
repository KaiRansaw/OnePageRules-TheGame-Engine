using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The Solo &amp; Co-Op Rules v3.6.0 Challenge Bonuses in PLAYED games, through the real server, turn order
    /// and roll stages. Seat 0 is marked as a bot (<see cref="IComputerPlayer"/>) and seat 1 is not, so seat 1
    /// stands in for the human: only seat 0 may ever get a bonus, which is the book's "give the AI" and the
    /// thing a bot-vs-bot harness would otherwise hide.
    /// </summary>
    [TestFixture]
    public class ChallengeBonusesInAPlayedGameTests
    {
        private const string BotName = "Bot";
        private const string StandInName = "Stand-in";
        private const string StandInSuffix = " (B)";

        /// <summary>A played Firefight / Skirmish game leaves the global table at 4' x 4' - put it back.</summary>
        [TearDown]
        public void RestoreTheTable() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private sealed class LogCapturingController : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string>? _lines;

            internal LogCapturingController(IPlayerController inner, List<string>? lines)
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
                // Both seats receive every log line; only the bot's seat keeps a copy.
                if (_lines != null)
                    lock (_lines) _lines.Add(logMessage);
                _inner.SendLogMessage(logMessage, color);
            }

            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType,
                string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        /// <summary>A wrapped bot that the game is told is a bot.</summary>
        private sealed class MarkedBot : IPlayerController, IComputerPlayer
        {
            private readonly LogCapturingController _inner;
            internal MarkedBot(LogCapturingController inner) => _inner = inner;
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
            public void SendLogMessage(string logMessage, TextColor color) => _inner.SendLogMessage(logMessage, color);
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        private static ArmyListFile Renamed(ArmyListFile army, string suffix)
        {
            foreach (var unit in army.Units) unit.Name += suffix;
            return army;
        }

        private static async Task<List<string>> PlayAGame(int seed, EGameSystem system, EChallengeBonuses bonuses)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = i == 0
                    ? TestArmies.MakeRichArmy("Ours")
                    : Renamed(TestArmies.MakeRichArmy("Theirs"), StandInSuffix);
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = AiResolverRegistryFactory.CreateSoloRulesController(
                    i == 0 ? BotName : StandInName, slots[i].PlayerID, aiGame, seed, slots[i].SlotID);
                var capturing = new LogCapturingController(ai, i == 0 ? log : null);
                slots[i].AssignPlayerController(i == 0 ? new MarkedBot(capturing) : capturing);
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.ChallengeBonuses = bonuses;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            lock (log) return log.ToList();
        }

        private static bool IsChallengeLine(string line) =>
            line.Contains(ChallengeBonuses.Name, StringComparison.Ordinal);

        [Test]
        public async Task WithNoBonuses_NothingIsGiven()
        {
            List<string> log = await PlayAGame(seed: 7101, EGameSystem.GrimdarkFuture, EChallengeBonuses.None);
            Assert.That(log.Where(IsChallengeLine), Is.Empty);
        }

        [TestCase(EGameSystem.GrimdarkFuture, 7101)]
        [TestCase(EGameSystem.AgeOfFantasy, 7102)]
        [TestCase(EGameSystem.GFFirefight, 7103)]
        [TestCase(EGameSystem.AoFSkirmish, 7104)]
        public async Task D3Activations_TheBotRollsEachTurn_AndOnlyTheBot(EGameSystem system, int seed)
        {
            List<string> log = await PlayAGame(seed, system, EChallengeBonuses.ExtraActivations);

            List<string> rolls = log.Where(l => l.StartsWith($"{ChallengeBonuses.Name}: ", StringComparison.Ordinal)
                && l.Contains(" rolls a ", StringComparison.Ordinal)).ToList();
            Assert.That(rolls, Is.Not.Empty, $"{system}: the bot never rolled its D3.");
            Assert.That(rolls.All(l => l.Contains(BotName + " rolls", StringComparison.Ordinal)), Is.True,
                "only the bot's seat may roll: " + string.Join(" | ", rolls.Where(l => !l.Contains(BotName + " rolls"))));
            Assert.That(log.Any(l => l.Contains(StandInName + " rolls", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// The run itself: after a roll of 2 or 3, the next activations belong to the bot - checked on the
        /// "Activating:" lines, where the stand-in's units carry a suffix. A run may end early only when the
        /// bot has nothing left to activate this round, so the check accepts a short run only at a round's end.
        /// </summary>
        [TestCase(7101)]
        [TestCase(7105)]
        [TestCase(7106)]
        public async Task D3Activations_AfterARollOfTwoOrMore_TheBotActivatesThatManyInARow(int seed)
        {
            List<string> log = await PlayAGame(seed, EGameSystem.GrimdarkFuture, EChallengeBonuses.ExtraActivations);

            int checkedRuns = 0;
            for (int i = 0; i < log.Count; i++)
            {
                string line = log[i];
                if (!line.StartsWith($"{ChallengeBonuses.Name}: {BotName} rolls a ", StringComparison.Ordinal)) continue;
                int rolled = line[$"{ChallengeBonuses.Name}: {BotName} rolls a ".Length] - '0';
                if (rolled < 2) continue;

                // The roll is made as the turn is handed over, before the turn's first "Activating:" line.
                List<string> following = log.Skip(i + 1)
                    .TakeWhile(l => !(l.StartsWith($"{ChallengeBonuses.Name}: ", StringComparison.Ordinal)
                                      && l.Contains(" rolls a ", StringComparison.Ordinal))
                        && !l.StartsWith("No players left", StringComparison.Ordinal))
                    .Where(l => l.StartsWith("Activating: ", StringComparison.Ordinal))
                    .ToList();
                List<string> runLines = following.Take(rolled).ToList();
                int firstStandIn = runLines.FindIndex(l => l.Contains(StandInSuffix, StringComparison.Ordinal));
                if (firstStandIn >= 0)
                {
                    // Cut short - legal only when the bot had nothing left to activate this round, which shows
                    // as no bot activation anywhere after the hand-over in the rest of the round.
                    List<string> afterHandOver = following.Skip(firstStandIn).ToList();
                    Assert.That(afterHandOver.Where(l => !l.Contains(StandInSuffix, StringComparison.Ordinal)), Is.Empty,
                        $"after '{line}' the run handed over while the bot still had units to activate: "
                        + string.Join(" | ", following));
                    continue;
                }
                if (following.Count < rolled) continue;

                Assert.That(runLines.Count, Is.EqualTo(rolled));
                checkedRuns++;
            }

            Assert.That(checkedRuns, Is.GreaterThan(0), "no full run of 2+ activations was played to check.");
        }

        [TestCase(EGameSystem.GrimdarkFuture, 7101)]
        [TestCase(EGameSystem.AgeOfFantasy, 7102)]
        public async Task TiedBonus_GivesTheBotPlusOneToHit_WhileTied(EGameSystem system, int seed)
        {
            List<string> log = await PlayAGame(seed, system, EChallengeBonuses.TiedHitBonus);
            List<string> bonusLines = log.Where(IsChallengeLine).ToList();
            Assert.That(bonusLines.Any(l => l.Contains("(tied) +1 to hit", StringComparison.Ordinal)), Is.True,
                $"{system}: the tied bonus never applied.");
            // Only the bot's units: the stand-in's carry the suffix.
            Assert.That(bonusLines.Where(l => l.Contains(StandInSuffix, StringComparison.Ordinal)), Is.Empty);
            // The tied bonus never gives Defense, and never fires while losing.
            Assert.That(bonusLines.Any(l => l.Contains("Defense", StringComparison.Ordinal)), Is.False);
            Assert.That(bonusLines.Any(l => l.Contains("(losing)", StringComparison.Ordinal)), Is.False);
        }

        /// <summary>
        /// The losing bonus: +1 to hit and +1 Defense, only to the bot, only while behind. Several seeds, because
        /// whether the bot falls behind at all is the dice's business - the assertion is that across them it does
        /// at least once, and that every bonus line in every game belongs to the bot.
        /// </summary>
        [Test]
        public async Task LosingBonus_GivesTheBotHitAndDefense_OnlyWhileLosing()
        {
            var all = new List<string>();
            foreach (int seed in Enumerable.Range(7101, 20))
                all.AddRange(await PlayAGame(seed, EGameSystem.GrimdarkFuture, EChallengeBonuses.LosingBonus));

            List<string> bonusLines = all.Where(IsChallengeLine).ToList();
            Assert.That(bonusLines.Any(l => l.Contains("(losing) +1 to hit", StringComparison.Ordinal)), Is.True,
                "the bot never got the losing hit bonus in twenty games.");
            Assert.That(bonusLines.Any(l => l.Contains("(losing) +1 to Defense", StringComparison.Ordinal)), Is.True,
                "the bot never got the losing Defense bonus in twenty games.");
            Assert.That(bonusLines.Where(l => l.Contains(StandInSuffix, StringComparison.Ordinal)), Is.Empty);
            Assert.That(bonusLines.Any(l => l.Contains("(tied)", StringComparison.Ordinal)), Is.False);
        }

        [TestCase(EGameSystem.GFStarQuest)]
        [TestCase(EGameSystem.AoFQuest)]
        public void QuestGames_HaveNoChallengeBonuses(EGameSystem system) =>
            Assert.That(ChallengeBonuses.Applies(system), Is.False,
                "the Quest games have no Solo & Co-Op book and print no Challenge Bonuses.");

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void TheFourSoloCoOpGames_HaveThem(EGameSystem system) =>
            Assert.That(ChallengeBonuses.Applies(system), Is.True);
    }
}
