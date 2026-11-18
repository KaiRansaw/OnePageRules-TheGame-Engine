using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Campaign Rules 3.6.0, Medic skill Healer: "Once per game, when the Hero's unit is activated, it may restore
    // D3 models/wounds." A placeholder until 2026-11-18. Played through ActivationStartStage with a real offer.
    [TestFixture]
    public class HealerSkillIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private RuleResolver _resolver = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _resolver = new RuleResolver();
            _resolver.Register(Campaign36RuleCatalog.Healer);
        }

        [TestCase(1, 1)] // D3 off a d6: 1-2 -> 1
        [TestCase(4, 2)] // 3-4 -> 2
        [TestCase(6, 3)] // 5-6 -> 3
        public async Task Accepted_RevivesD3Models(int face, int expected)
        {
            DataBinding<UnitData> unit = MakeUnit(5);
            for (int i = 1; i < 5; i++) Kill(unit, i);
            var asker = new Asker { Accept = true };

            await Activate(unit, asker, face);

            Assert.That(asker.Asked, Is.EqualTo(1), "'may' - it is offered");
            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(1 + expected));
        }

        [Test]
        public async Task WoundsFirst_ThenModels()
        {
            DataBinding<UnitData> unit = MakeUnit(2, toughSecond: 3);
            Kill(unit, 0);
            ((ModelData)unit.GetValue().Models[1]).DealWounds(1f);

            await Activate(unit, new Asker { Accept = true }, face: 6); // 3 restores

            Assert.That(unit.GetValue().Models[1].WoundsDealt, Is.EqualTo(0f).Within(0.001f));
            Assert.That(unit.GetValue().Models[0].GetIsAlive(), Is.True);
        }

        [Test]
        public async Task OncePerGame_AndNotOfferedAtFullStrength()
        {
            DataBinding<UnitData> unit = MakeUnit(3);
            var full = new Asker { Accept = true };
            await Activate(unit, full, face: 6);
            Assert.That(full.Asked, Is.EqualTo(0), "nothing to restore - no offer, so the one use isn't wasted");

            Kill(unit, 2);
            await Activate(unit, new Asker { Accept = true }, face: 6);
            Assert.That(unit.GetValue().Models.All(m => m.GetIsAlive()), Is.True);

            Kill(unit, 2);
            var again = new Asker { Accept = true };
            await Activate(unit, again, face: 6);
            Assert.That(again.Asked, Is.EqualTo(0), "once per game");
            Assert.That(unit.GetValue().Models[2].GetIsAlive(), Is.False);
        }

        [Test]
        public async Task Declined_KeepsTheUse()
        {
            DataBinding<UnitData> unit = MakeUnit(3);
            Kill(unit, 2);
            await Activate(unit, new Asker { Accept = false }, face: 6);
            Assert.That(unit.GetValue().Models[2].GetIsAlive(), Is.False);

            var later = new Asker { Accept = true };
            await Activate(unit, later, face: 6);
            Assert.That(later.Asked, Is.EqualTo(1));
            Assert.That(unit.GetValue().Models[2].GetIsAlive(), Is.True);
        }

        private static void Kill(DataBinding<UnitData> unit, int index)
        {
            var model = (ModelData)unit.GetValue().Models[index];
            model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        private DataBinding<UnitData> MakeUnit(int count, int toughSecond = 0)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < count; i++)
            {
                var m = new ModelData(0.5f, new List<Weapon>(), new Position(20f + i * 1.2f, 20f), _store);
                if (i == 1 && toughSecond > 0) m.SetMaxWounds(toughSecond);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(m)));
            }
            var unit = new UnitData(_player, "Medic Squad", quality: 4, defense: 4, modelBindings: models);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Skill: Healer", Campaign36RuleCatalog.Healer));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            _store.Create(new TeamData(0, new List<PlayerID> { _player }));
            return binding;
        }

        private async Task Activate(DataBinding<UnitData> unit, IPlayerRequestByID asker, int face)
        {
            var ctx = new TriggeredMoveTestContext(_store, asker, diceRoller: new FixedDiceRoller(face),
                ruleResolver: _resolver);
            var unitContext = new UnitActionContext(ctx, unit);
            unitContext.Reset(unit);
            var stage = new ActivationStartStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("finish");
            await stage.Enter(unitContext);
        }

        private sealed class Asker : IPlayerRequestByID
        {
            public bool Accept;
            public int Asked;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is YesNoRequest) { Asked++; return Task.FromResult((TReply)(object)Accept); }
                throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
