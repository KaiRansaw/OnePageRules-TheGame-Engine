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

        private static async Task<List<string>> PlayAGame(int seed, EGameSystem system, bool fastPlayShooting, bool fastPlayMelee = false, bool fastPlayMovement = false,
            EDeploymentStyle deploymentStyle = EDeploymentStyle.Core)
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
            settings.FastPlayMelee = fastPlayMelee;
            settings.FastPlayMovement = fastPlayMovement;
            settings.DeploymentStyle = deploymentStyle;

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

        /// <summary>
        /// #458: with Leader-Based Melee on, a played game still finishes, and every melee that happens has
        /// the WHOLE unit striking ("all models always count as being in range of the enemy").
        /// </summary>
        [TestCase(4560)]
        [TestCase(4581)]
        public async Task WithFastPlayMelee_EveryMeleeHasTheWholeUnitInRange(int seed)
        {
            List<string> log = await PlayAGame(seed, EGameSystem.GrimdarkFuture, fastPlayShooting: false,
                fastPlayMelee: true);
            Assert.That(LeadersWereSet(log), Is.True);

            var rx = new System.Text.RegularExpressions.Regex(
                @"Determine In Range (Attackers|Defenders): (\d+) of (\d+) living models in melee range");
            int melees = 0;
            foreach (string line in log)
            {
                var m = rx.Match(line);
                if (!m.Success || m.Groups[2].Value == "0") continue;
                melees++;
                Assert.That(m.Groups[2].Value, Is.EqualTo(m.Groups[3].Value),
                    $"Fast-Play melee: everyone in range, but the log says \"{line}\"");
            }
            Assert.That(melees, Is.GreaterThan(0), "this seed must actually produce a melee, or the test proves nothing");
        }

        /// <summary>#459: all three leader rules on together, both games' books - a whole game still plays out.</summary>
        [TestCase(EGameSystem.GrimdarkFuture, 4590)]
        [TestCase(EGameSystem.AgeOfFantasy, 4591)]
        public async Task WithEveryLeaderRuleOn_TheGameStillFinishes(EGameSystem system, int seed)
        {
            List<string> log = await PlayAGame(seed, system, fastPlayShooting: true, fastPlayMelee: true,
                fastPlayMovement: true);
            Assert.That(LeadersWereSet(log), Is.True);
        }

        /// <summary>
        /// The Advanced deployment styles (2026-11-05): a whole Firefight game deploys into every one of the 18
        /// and plays to the end - the AI places its units inside triangles, wedges and rings without faulting.
        /// </summary>
        [TestCaseSource(nameof(EveryDeploymentStyle))]
        public async Task EveryDeploymentStyle_DeploysAndPlaysToTheEnd(EDeploymentStyle style)
        {
            List<string> log = await PlayAGame(4600 + (int)style, EGameSystem.GFFirefight, fastPlayShooting: false,
                deploymentStyle: style);
            Assert.That(log.Any(l => l.Contains($"Deployment style: {AdvancedDeployment.Name(style)}", StringComparison.Ordinal)),
                Is.True, "the style was never used");
        }

        private static IEnumerable<EDeploymentStyle> EveryDeploymentStyle() =>
            AdvancedDeployment.InFamily(EDeploymentFamily.Standard)
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Strategic))
                .Concat(AdvancedDeployment.InFamily(EDeploymentFamily.Asymmetric));

        /// <summary>Grimdark Future's own long-table layouts, all 18, played out.</summary>
        [TestCaseSource(nameof(EveryDeploymentStyle))]
        public async Task EveryDeploymentStyle_OnGrimdarkFuturesLongTable_DeploysAndPlaysToTheEnd(EDeploymentStyle style)
        {
            List<string> log = await PlayAGame(4700 + (int)style, EGameSystem.GrimdarkFuture, fastPlayShooting: false,
                deploymentStyle: style);
            Assert.That(log.Any(l => l.Contains($"Deployment style: {AdvancedDeployment.Name(style)}", StringComparison.Ordinal)),
                Is.True, "the style was never used");
        }

        [Test]
        public async Task ARandomStyle_IsRolledAndAnnounced()
        {
            List<string> log = await PlayAGame(4620, EGameSystem.AoFSkirmish, fastPlayShooting: false,
                deploymentStyle: EDeploymentStyle.RandomAsymmetric);
            Assert.That(log.Any(l => l.Contains("Deployment style: rolled", StringComparison.Ordinal)), Is.True);
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
