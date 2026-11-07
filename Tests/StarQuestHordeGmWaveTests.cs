using FDG.Ai;
using FDG.Data;
using FDG.GameMasterTools;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Quest;
using FDG.SaveLoad;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// QA 5.2: Game Master sets alertness to 6 and ends the round in a Star Quest Horde game. The wave must
    /// end and the next round must put the next wave's enemies on the table.
    /// </summary>
    [TestFixture]
    public class StarQuestHordeGmWaveTests
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

        private static QuestHero Hero(string name) => new QuestHero
        {
            Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 60, Class = "Soldier",
            Quality = 4, Defense = 4, Tough = 9, Power = 4, Strength = 4,
            Weapons = new() { new QuestWeapon { Name = "CCW", Range = 0, Attacks = 2 } },
        };

        private static ArmyListFile Collection(int units)
        {
            var army = new ArmyListFile { Name = "Enemies" };
            for (int i = 0; i < units; i++)
                army.Units.Add(new UnitFileEntry
                {
                    Name = $"Raiders {i + 1}", ModelCount = 3, Quality = 5, Defense = 5, PointCost = 40,
                    Weapons = new() { new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1, Quantity = 3 } },
                });
            return army;
        }

        [TestCase(EGameMode.StarQuestHorde)]
        [TestCase(EGameMode.StarQuestMission)]
        public async Task AlertnessSixThenEndRound_DeploysTheNextWave(EGameMode mode)
        {
            List<string> lines = await Play(mode, units: 8);
            Assert.That(lines.Any(l => l.Contains("wave 2 (") || l.Contains("reinforcements 1 (")), Is.True, "the next wave picks enemies");
            Assert.That(lines.Any(l => l.StartsWith("Arrived: ") && l.EndsWith(")")), Is.True,
                "the wave says who came in and at which corner");
        }

        /// <summary>
        /// The collection is a single unit, still alive on the table when the next wave is due: the banner says
        /// nobody is left, instead of "Wave 2 incoming!" with nothing after it.
        /// </summary>
        [Test]
        public async Task AnExhaustedCollection_SaysSo()
        {
            List<string> lines = await Play(EGameMode.StarQuestHorde, units: 1);
            Assert.That(lines.Any(l => l.Contains("Wave 2: no enemies are left to send.")), Is.True);
            Assert.That(lines.Any(l => l.Contains("Wave 2 incoming!")), Is.False);
        }

        private static async Task<List<string>> Play(EGameMode mode, int units)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var lines = new List<string>();
            bool fired = false;
            var slots = new PlayerSlot[2];
            ArmyListFile party = QuestArmy.Build(new[] { Hero("Vex"), Hero("Mara") }, _ => null);
            for (int i = 0; i < 2; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), i == 0 ? party : Collection(units), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = AiProfileFactory.CreateController(EAiProfile.StarQuest, $"AI {i}",
                    slots[i].PlayerID, aiGame, 7, slots[i].SlotID);
                slots[i].AssignPlayerController(new LogTap(ai, line =>
                {
                    lock (lines) lines.Add(line);
                    if (!fired && line.StartsWith("Round 1") && GameMasterHub.Active is { } hub)
                    {
                        fired = true;
                        hub.QuestSetAlertness(6);
                        hub.EndRoundNow();
                    }
                }));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = mode;
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = 7;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += r => completed.TrySetResult(r);

            var deadline = DateTime.UtcNow.AddSeconds(120);
            while (DateTime.UtcNow < deadline && !completed.Task.IsCompleted)
            {
                lock (lines)
                    if (lines.Any(l => l.StartsWith("Round 3"))) break;
                await Task.Delay(50);
            }
            lock (lines)
            {
                foreach (string l in lines.Where(l => l.Contains("Horde") || l.Contains("Mission") || l.Contains("corner") || l.Contains("Game Master") || l.StartsWith("Round") || l.Contains("ncoming") || l.Contains("Arrived") || l.Contains("no enemies")))
                    TestContext.Progress.WriteLine(l);
            }
            server.Stop();
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            Assert.That(fired, Is.True);
            lock (lines) return lines.ToList();
        }
    }
}
