using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Command Points (Advanced Rulebooks 3.6.0: GF p42, GFF p39, AoF p42, AoFS p39) - the framework.</summary>
    [TestFixture]
    public class CommandPointsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private void Build(int face, int playstyle, EGameSystem system = EGameSystem.GrimdarkFuture, Func<object, object>? answer = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = CommandPoints.WithPick(EAdvancedRules.CommandPoints, playstyle);
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: new Answer(answer ?? (r => ((StringSelectionRequest)r).ValidOptions[0])));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
            Army(_me, 2000);
            Army(_them, 1000);
        }

        private void Army(PlayerID owner, int points)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, "Unit", quality: 4, defense: 4, modelBindings: bindings) { PointCost = points };
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
        }

        [Test]
        public void ThePlaystyles_AreTheBooks()
        {
            Assert.That(new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight, EGameSystem.AoFSkirmish }
                .Select(CommandPoints.Step), Is.EqualTo(new[] { 1000, 750, 400, 300 }));
            Func<int> two = () => 2, three = () => 3;
            Assert.That(CommandPoints.Earned(CommandPoints.Fixed, 2000, EGameSystem.GrimdarkFuture, two), Is.EqualTo(8));
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 2000, EGameSystem.GrimdarkFuture, two), Is.EqualTo(2));
            Assert.That(CommandPoints.Earned(CommandPoints.FixedRandom, 2000, EGameSystem.GrimdarkFuture, three), Is.EqualTo(12));
            Assert.That(CommandPoints.Earned(CommandPoints.GrowingRandom, 1000, EGameSystem.GrimdarkFuture, three), Is.EqualTo(2), "D3 x 0.5, rounded up");
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 500, EGameSystem.GrimdarkFuture, two), Is.EqualTo(1), "at least one step");
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 600, EGameSystem.AoFSkirmish, two), Is.EqualTo(2));
            GameSettings s = GameSettings.GetDefault();
            s.AdvancedRules = CommandPoints.WithPick(EAdvancedRules.CommandPoints, 5);
            Assert.That(CommandPoints.Picked(s), Is.EqualTo(5));
            Assert.That(AdvancedRules.Enabled(s), Is.EqualTo(EAdvancedRules.CommandPoints), "the pick bits are not options");
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.CommandPointsChapter));
        }

        [Test]
        public async Task Fixed_GivesItsCpOnce_BeforeTheGame()
        {
            Build(face: 4, CommandPoints.Fixed);
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8));
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(4));
            await CommandPoints.RoundStart(_context, 1);
            await CommandPoints.RoundStart(_context, 2);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8), "Fixed is once only");
        }

        [Test]
        public async Task Growing_GivesCpEachRound_Temporary_LosesThemAtRoundEnd()
        {
            Build(face: 4, CommandPoints.Growing);
            await CommandPoints.RoundStart(_context, 1);
            await CommandPoints.RoundStart(_context, 2);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4));
            CommandPoints.RoundEnd(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4), "Growing keeps them");

            Build(face: 4, CommandPoints.Temporary);
            await CommandPoints.RoundStart(_context, 1);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(2));
            CommandPoints.RoundEnd(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(0));
        }

        [Test]
        public async Task TheDoctrine_IsPickedOrRolled_AndCpCanBeSpent()
        {
            Build(face: 5, CommandPoints.Fixed, answer: r => ((StringSelectionRequest)r).ValidOptions[2]);   // "Defensive Doctrine"
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.DoctrineOf(_context, _me), Is.EqualTo(2));
            Build(face: 5, CommandPoints.Fixed);   // first option = roll: a 5
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.DoctrineOf(_context, _them), Is.EqualTo(5));
            Assert.That(CommandPoints.Spend(_context, _them, 3, "test"), Is.True);
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(1));
            Assert.That(CommandPoints.Spend(_context, _them, 2, "test"), Is.False, "can't spend what it hasn't got");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(1));
        }
    }
}
