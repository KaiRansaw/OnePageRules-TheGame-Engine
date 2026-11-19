using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai's bug hunt, 2026-11-19 ("Fix it in the engine"): <see cref="IGameProgress.GameSystem"/>,
    /// <see cref="IGameProgress.GameMode"/> and <see cref="IGameProgress.TotalRounds"/> read the progress record, which
    /// does not exist until round 1. All through objective placement and deployment they answered Grimdark Future /
    /// Standard / 4 rounds whatever was being played - so the official bot deployed an Age of Fantasy: Skirmish army
    /// by Grimdark Future's Solo &amp; Co-Op profile (3 sections of its table edge; the Skirmish book says 2), and every
    /// "which game is this?" asked during set-up got the wrong answer.
    /// </summary>
    [TestFixture]
    public class GameSettingsBeforeRoundOneTests
    {
        // A played Skirmish game leaves the global table at 4' x 4'; other fixtures expect the standard one.
        [TearDown]
        public void RestoreTheTable() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        /// <summary>The official bot's resolvers, noting what the table says about the game each time it is asked something.</summary>
        private sealed class Noting : IStageResolverRegistry
        {
            private readonly IStageResolverRegistry _inner;
            private readonly ITableState _table;
            private readonly List<(int? Round, EGameSystem System, EGameMode Mode, int Rounds)> _seen;

            public Noting(IStageResolverRegistry inner, ITableState table, List<(int? Round, EGameSystem System, EGameMode Mode, int Rounds)> seen)
            {
                _inner = inner;
                _table = table;
                _seen = seen;
            }

            private void Note()
            {
                IGameProgress p = _table.Progress;
                lock (_seen) _seen.Add((p.RoundCount, p.GameSystem, p.GameMode, p.TotalRounds));
            }

            public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
                where TRequest : IStageTaskRequest<TReply>
            {
                _inner.RegisterResolver(resolver);
                return this;
            }

            public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                Note();
                return _inner.ResolveRequest<TRequest, TReply>(request);
            }

            public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore store)
            {
                Note();
                return _inner.ResolveRequestAsJson(typeFullName, requestJson, store);
            }
        }

        [Test]
        public async Task DuringSetUpAndDeployment_TheTableAlreadyKnowsWhichGameIsBeingPlayed()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var seen = new List<(int? Round, EGameSystem System, EGameMode Mode, int Rounds)>();

            var slots = new PlayerSlot[2];
            for (int i = 0; i < 2; i++)
            {
                ArmyListFile army = i == 0 ? TestArmies.MakeShooterArmy() : TestArmies.MakeDefenderArmy();
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                var registry = new Noting(AiResolverRegistryFactory.BuildSoloRules(aiGame.TableState, slots[i].PlayerID, 7, slots[i].SlotID), aiGame.TableState, seen);
                slots[i].AssignPlayerController(new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame, registry));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.AoFSkirmish;
            settings.GameMode = EGameMode.Standard;
            settings.NumberOfRounds = 5;
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 7;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            List<(int? Round, EGameSystem System, EGameMode Mode, int Rounds)> before;
            lock (seen) before = seen.Where(s => s.Round == null).ToList();
            Assert.That(before, Is.Not.Empty, "the bots were asked nothing before round 1 - the test proves nothing");
            Assert.That(before.Select(s => s.System).Distinct(), Is.EqualTo(new[] { EGameSystem.AoFSkirmish }),
                "before round 1 the table must already say which game it is");
            Assert.That(before.Select(s => s.Rounds).Distinct(), Is.EqualTo(new[] { 5 }), "and how many rounds it will last");
            Assert.That(before.Select(s => s.Mode).Distinct(), Is.EqualTo(new[] { EGameMode.Standard }));
        }

        [Test]
        public void AStoreNoGameWasBuiltOn_StillAnswersTheOldDefaults()
        {
            var progress = new GameProgress(GameDataStore.GameDataStoreBuilder.GetDefault());
            Assert.That(progress.GameSystem, Is.EqualTo(EGameSystem.GrimdarkFuture));
            Assert.That(progress.GameMode, Is.EqualTo(EGameMode.Standard));
            Assert.That(progress.TotalRounds, Is.EqualTo(4));
        }

        [Test]
        public void TheSettingsAGameIsBuiltWith_AnswerUntilTheProgressRecordExists_AndFollowACopyOfTheStore()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFFirefight;
            settings.GameMode = EGameMode.Horde;
            GameProgressUtilities.SetGameSettings(store, settings);

            var progress = new GameProgress(store);
            Assert.That(progress.RoundCount, Is.Null, "no progress record yet");
            Assert.That(progress.GameSystem, Is.EqualTo(EGameSystem.GFFirefight));
            Assert.That(progress.GameMode, Is.EqualTo(EGameMode.Horde));
            Assert.That(progress.TotalRounds, Is.EqualTo(StateMachine.HordeRules.RoundLimit(settings)));

            // A bot's look-ahead works on a copy of the store: it is the same game there.
            var copy = new GameProgress(StoreClone.Clone(store));
            Assert.That(copy.GameSystem, Is.EqualTo(EGameSystem.GFFirefight));
            Assert.That(copy.GameMode, Is.EqualTo(EGameMode.Horde));
        }
    }
}
