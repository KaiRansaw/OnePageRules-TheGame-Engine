using FDG.Ai;
using FDG.Campaign;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #466 - the campaign mission's length rules in a game that is actually played, AI against AI, so a
    /// deleted call in the round flow fails here even though every unit test in
    /// <see cref="CampaignMissionLengthTests"/> would still pass (the lesson #432 wrote down).
    /// </summary>
    [TestFixture]
    public class CampaignMissionLengthInAPlayedGameTests
    {
        /// <summary>
        /// The official bot's resolvers, except that the concession check is answered by the test: a bot
        /// on its own never concedes, so "concede at round N" has to come from outside, as Kai's button
        /// does for him.
        /// </summary>
        private sealed class ConcedingRegistry : IStageResolverRegistry
        {
            private readonly IStageResolverRegistry _inner;
            private readonly int? _concedeAtRound;
            public int Asked;

            public ConcedingRegistry(IStageResolverRegistry inner, int? concedeAtRound)
            {
                _inner = inner;
                _concedeAtRound = concedeAtRound;
            }

            private bool Answer(ConcedeCheckRequest r)
            {
                Interlocked.Increment(ref Asked);
                return _concedeAtRound is int n && r.Round >= n;
            }

            public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
                where TRequest : IStageTaskRequest<TReply>
            {
                _inner.RegisterResolver(resolver);
                return this;
            }

            public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                request is ConcedeCheckRequest check
                    ? Task.FromResult((TReply)(object)Answer(check))
                    : _inner.ResolveRequest<TRequest, TReply>(request);

            public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore store)
            {
                if (typeFullName == typeof(ConcedeCheckRequest).FullName)
                {
                    var check = JsonConvert.DeserializeObject<ConcedeCheckRequest>(requestJson)!;
                    return Task.FromResult(JsonConvert.SerializeObject(Answer(check)));
                }
                return _inner.ResolveRequestAsJson(typeFullName, requestJson, store);
            }
        }

        private sealed class LogCapture : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string> _lines;
            internal LogCapture(IPlayerController inner, List<string> lines) { _inner = inner; _lines = lines; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { lock (_lines) _lines.Add(logMessage); _inner.SendLogMessage(logMessage, color); }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        private sealed record Played(GameResult Result, List<string> Log, GameDataStore Store, PlayerSlot[] Slots, int Asked);

        private static async Task<Played> Play(int seed, bool campaign, int? slot0ConcedesAtRound)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();
            var registries = new ConcedingRegistry[2];

            var slots = new PlayerSlot[2];
            for (int i = 0; i < 2; i++)
            {
                ArmyListFile army = i == 0 ? TestArmies.MakeShooterArmy() : TestArmies.MakeDefenderArmy();
                if (campaign)
                    foreach (UnitFileEntry unit in army.Units)
                        unit.StrikeUnitJson = new StrikeUnit { TeamFile = $"camp{i}.fdgarmy" }.ToJson();

                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                registries[i] = new ConcedingRegistry(
                    AiResolverRegistryFactory.BuildSoloRules(aiGame.TableState, slots[i].PlayerID, seed, slots[i].SlotID),
                    i == 0 ? slot0ConcedesAtRound : null);
                var ai = new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame, registries[i]);
                slots[i].AssignPlayerController(new LogCapture(ai, log));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);

            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            lock (log) return new Played(completed.Task.Result, log.ToList(), store, slots, registries.Sum(r => r.Asked));
        }

        [Test]
        public async Task ConcedingAtRoundTwo_EndsTheGame_TheOtherSideWins_AndTheArmyIsNotACasualty()
        {
            Played game = await Play(seed: 466, campaign: true, slot0ConcedesAtRound: 2);

            Assert.That(game.Result.Outcome, Is.EqualTo(EGameOutcome.Win));
            Assert.That(game.Result.Winner, Is.EqualTo(game.Slots[1].PlayerID));
            Assert.That(game.Log.Any(l => l.Contains("concedes the mission")), Is.True);
            Assert.That(game.Log.Any(l => l.StartsWith("Round 3")), Is.False, "nothing is played after the concession");

            var conceded = game.Store.GetAllValues<UnitData>().Where(u => u.PlayerID.Equals(game.Slots[0].PlayerID)).ToList();
            Assert.That(conceded.Where(u => u.GetIsAlive()).All(u => u.Tokens.HasToken(Rules.Foundation.TokenType.Conceded)), Is.True);
            Assert.That(conceded.Any(u => u.GetIsAlive()), Is.True,
                "the shooter army survives round 1 - these are the units the casualty check must pass over");
        }

        [Test]
        public async Task AnOrdinaryGame_NeverAsks()
        {
            Played game = await Play(seed: 466, campaign: false, slot0ConcedesAtRound: 1);
            Assert.That(game.Asked, Is.Zero);
            Assert.That(game.Log.Any(l => l.Contains("concedes")), Is.False);
        }

        /// <summary>
        /// Mission Length, over a spread of seeds: a campaign mission never ends in a tie before the
        /// house-rule cap (both armies still standing), and every round past the fourth was announced as the
        /// book's extra round. At least one seed must actually go past round 4, or the test proves nothing.
        /// </summary>
        [Test]
        public async Task ACampaignMission_PlaysOnUntilThereIsOneWinner()
        {
            int wentLong = 0;
            foreach (int seed in new[] { 1, 2, 3, 4, 5, 6, 7, 8 })
            {
                Played game = await Play(seed, campaign: true, slot0ConcedesAtRound: null);
                if (game.Result.RoundsPlayed > 4)
                {
                    wentLong++;
                    Assert.That(game.Log.Any(l => l.Contains("continues into another round")), Is.True, $"seed {seed}");
                }
                if (game.Result.Outcome == EGameOutcome.Tie)
                    Assert.That(game.Result.RoundsPlayed, Is.GreaterThanOrEqualTo(Stages.CampaignMissionLength.LastRound),
                        $"seed {seed}: a tie only at the cap");
            }
            Assert.That(wentLong, Is.GreaterThan(0), "no seed went past round 4 - pick seeds that do");
        }
    }
}
