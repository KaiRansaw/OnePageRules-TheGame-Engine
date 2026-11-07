using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Enemy Ambush in Horde Mode (GF 3.6.0, with the Solo &amp; Co-Op AI rules the Horde book hands the enemy
    /// to): "kept in reserve, and must always deploy at the start of the second round" - at a random corner
    /// (Kai, 2026-11-06). The defenders' own Ambush does nothing at set-up: "all their units within 9\" of the
    /// table centre".
    /// </summary>
    [TestFixture]
    public class HordeEnemyAmbushTests
    {
        private sealed class LogTap : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly Action<string> _onLine;
            internal LogTap(IPlayerController inner, Action<string> onLine) { _inner = inner; _onLine = onLine; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { _onLine(logMessage); _inner.SendLogMessage(logMessage, color); }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        private static ArmyListFile Army(string name, bool ambushUnit)
        {
            var army = new ArmyListFile { Name = name };
            for (int i = 0; i < 3; i++)
                army.Units.Add(new UnitFileEntry
                {
                    Name = $"{name} {i + 1}", ModelCount = 3, Quality = 4, Defense = 4, PointCost = 60,
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1, Quantity = 3 },
                        new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1, Quantity = 3 },
                    },
                    SpecialRules = ambushUnit && i == 2 ? new() { new SpecialRuleEntry_Core("Ambush") } : new(),
                });
            return army;
        }

        [Test]
        public async Task EnemyAmbush_ArrivesAtRoundTwo_AtACorner_DefenderAmbush_DeploysAtTheCentre()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var lines = new List<string>();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < 2; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    Army(i == 0 ? "Guards" : "Raiders", ambushUnit: true), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame,
                    AiResolverRegistryFactory.BuildSoloRules(aiGame.TableState, slots[i].PlayerID, 11, slots[i].SlotID));
                slots[i].AssignPlayerController(new LogTap(ai, l => { lock (lines) lines.Add(l); }));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.Horde;
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = 11;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += r => completed.TrySetResult(r);

            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !completed.Task.IsCompleted)
            {
                lock (lines) if (lines.Any(l => l.StartsWith("Round 3"))) break;
                await Task.Delay(50);
            }
            server.Stop();

            List<string> log;
            lock (lines) log = lines.Distinct().ToList();
            foreach (string l in log.Where(l => l.Contains("Ambush") || l.Contains("Raiders 3") || l.Contains("Guards 3") || l.StartsWith("Round") || l.StartsWith("Arrived")))
                TestContext.Progress.WriteLine(l);

            int round1 = log.FindIndex(l => l.StartsWith("Round 1"));
            int round3 = log.FindIndex(l => l.StartsWith("Round 3"));
            int held = log.FindIndex(l => l.Contains("Raiders 3 has Ambush - it waits"));
            int ambush = log.FindIndex(l => l == "Ambush!");
            int arrived = log.FindIndex(l => l.StartsWith("Arrived: Raiders 3 ("));
            Assert.That(held, Is.GreaterThanOrEqualTo(0), "the enemy ambusher is held back at set-up");
            Assert.That(round1, Is.GreaterThan(held));
            Assert.That(ambush, Is.GreaterThan(round1), "not in round 1");
            Assert.That(arrived, Is.GreaterThan(ambush).And.LessThan(round3), "it arrives as round 2 starts, at a named corner");
            Assert.That(log.Any(l => l.Contains("Guards 3 has Ambush")), Is.False, "the defenders' Ambush does nothing at set-up");
            Assert.That(log.Any(l => l.Contains("from Ambush")), Is.False, "the standard Ambush prompt never runs in Horde");
        }
    }
}
