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
    /// #456 - Fast-Play unit leaders in a PLAYED game, through the real server, deployment stage and AI
    /// resolvers. The unit tests prove the rules; this proves the game reaches them - the project's
    /// recurring lesson is that a test which calls the new code cannot tell whether the game ever does.
    /// Mirrors <see cref="CommandGroupPicksInAPlayedGameTests"/>, which this harness is copied from.
    /// </summary>
    [TestFixture]
    public class FastPlayInAPlayedGameTests
    {
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

        private static async Task<List<string>> PlayAGame(int seed, EGameSystem system, bool fastPlayShooting)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
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
            settings.FastPlayShooting = fastPlayShooting;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            lock (log) return log.ToList();
        }

        private static bool LeadersWereSet(IEnumerable<string> log) =>
            log.Any(line => line.Contains("Fast-Play: every unit has a leader", StringComparison.Ordinal));

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        public async Task WithFastPlayOn_EveryUnitGetsALeader_AndTheGameStillFinishes(EGameSystem system)
        {
            List<string> log = await PlayAGame(seed: 4560, system, fastPlayShooting: true);
            Assert.That(LeadersWereSet(log), Is.True,
                $"{system}: the leader stage never ran. Check that DeploymentStage binds "
                + "PickCommandGroupUnits -> PickUnitLeaders -> DeployAllUnits.");
        }

        [Test]
        public async Task WithFastPlayOff_NoLeaders()
        {
            List<string> log = await PlayAGame(seed: 4560, EGameSystem.GrimdarkFuture, fastPlayShooting: false);
            Assert.That(LeadersWereSet(log), Is.False);
        }

        [Test]
        public async Task InFirefight_NoLeaders_EvenWithTheSwitchLeftOn()
        {
            List<string> log = await PlayAGame(seed: 4560, EGameSystem.GFFirefight, fastPlayShooting: true);
            Assert.That(LeadersWereSet(log), Is.False, "Firefight's Advanced book has no Fast-Play.");
        }
    }
}
