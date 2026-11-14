using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// 2026-11-13: in GF: Firefight, AoF: Skirmish and both Quest games every AURA confers like Banner does. Army Forge's
    /// text for those four systems: <i>"This model's unit and up to 2 friendly units picked before the start of the game
    /// get X. If this model is killed, picked models that are deployed lose the rule."</i> (Grimdark Future and Age of
    /// Fantasy: <i>"This model and its unit get X."</i>). Kai: one screen per player before deployment; a Quest enemy's
    /// "up to 2 nearest" means nearest when it lands.
    /// </summary>
    [TestFixture]
    public class AuraPickedUnitsTests
    {
        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        private static SpecialRuleDefinition FuriousAura() => new("Furious Aura",
            new[] { new HookEntry(EHookID.Lifecycle_OnUnitCreated, new Condition.Always(), new Effect.Aura("Furious"), ELifetime.UntilEndOfGame) },
            Array.Empty<ActivatedAbility>());

        private static SpecialRuleDefinition Banner() => new("Banner", Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>());

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        [Test]
        public void ACarrier_ConfersItsBanner_AndWhatItsAuraHandsOut()
        {
            DataBinding<UnitData> hero = MakeUnit("Warlord", _us, 10, 10, aura: true, banner: true);

            Assert.That(AuraConferral.For(hero.GetValue()).Select(c => (c.Source, c.Granted)),
                Is.EquivalentTo(new[] { ("Banner", "Banner"), ("Furious Aura", "Furious") }));
        }

        [Test]
        public async Task InFirefight_OneRequestPerPlayer_CoversEveryCarrier_AndGrantsThePicks()
        {
            DataBinding<UnitData> hero = MakeUnit("Warlord", _us, 10, 10, aura: true, banner: true);
            DataBinding<UnitData> a = MakeUnit("Riflemen", _us, 20, 10);
            DataBinding<UnitData> b = MakeUnit("Bikers", _us, 30, 10);
            DataBinding<UnitData> enemy = MakeUnit("Raiders", _them, 40, 40);
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { hero, a, b }));
            _store.Create(new ArmyData(_them, new List<DataBinding<UnitData>> { enemy }));

            var requester = new Answering(rows => rows.Select(r => r.Source == "Furious Aura"
                ? r.Options.Select(o => o.Option).ToList()           // both units
                : r.Options.Take(1).Select(o => o.Option).ToList())  // Banner: just the first
                .ToList());
            await RunStage(requester, EGameSystem.GFFirefight);

            Assert.That(requester.Asked, Has.Count.EqualTo(1), "one screen for the player with carriers; the enemy has none");
            Assert.That(requester.Asked[0].Rows.Select(r => r.Source), Is.EquivalentTo(new[] { "Banner", "Furious Aura" }));
            Assert.That(requester.Asked[0].Rows.SelectMany(r => r.Options).All(o => o.Option.GetValue().PlayerID == _us),
                "only your own units are offered");

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(a.GetValue(), "Furious"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(b.GetValue(), "Furious"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(a.GetValue(), "Banner"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(b.GetValue(), "Banner"), Is.False);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(enemy.GetValue(), "Furious"), Is.False);
        }

        [Test]
        public async Task PickedAuraUnits_LoseItWhenTheCarrierDies()
        {
            DataBinding<UnitData> hero = MakeUnit("Warlord", _us, 10, 10, aura: true);
            DataBinding<UnitData> a = MakeUnit("Riflemen", _us, 20, 10);
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { hero, a }));
            await RunStage(new Answering(rows => rows.Select(r => r.Options.Select(o => o.Option).ToList()).ToList()), EGameSystem.AoFSkirmish);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(a.GetValue(), "Furious"), Is.True);

            foreach (DataBinding<ModelData> m in hero.GetValue().ModelBindings) m.GetValue().DealWounds(m.GetValue().TotalWounds);
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(new Answering(_ => new()), EGameSystem.AoFSkirmish), hero.GetValue(), killer: null);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(a.GetValue(), "Furious"), Is.False,
                "\"If this model is killed, picked models that are deployed lose the rule.\"");
        }

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        public async Task InTheFullSizeGames_NothingIsAsked(EGameSystem system)
        {
            DataBinding<UnitData> hero = MakeUnit("Warlord", _us, 10, 10, aura: true);
            DataBinding<UnitData> a = MakeUnit("Riflemen", _us, 20, 10);
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { hero, a }));

            var requester = new Answering(_ => throw new InvalidOperationException("nothing should be asked"));
            await RunStage(requester, system);

            Assert.That(requester.Asked, Is.Empty, "\"This model and its unit get X\" - nobody else");
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(a.GetValue(), "Furious"), Is.False);
        }

        [Test]
        public void AQuestEnemy_GivesItsAuraToTheTwoNearest_WhenItLands()
        {
            DataBinding<UnitData> boss = MakeUnit("Hive Tyrant", _them, 10, 10, aura: true);
            DataBinding<UnitData> near1 = MakeUnit("Gaunts A", _them, 13, 10);
            DataBinding<UnitData> near2 = MakeUnit("Gaunts B", _them, 10, 15);
            DataBinding<UnitData> far = MakeUnit("Gaunts C", _them, 50, 40);
            DataBinding<UnitData> hero = MakeUnit("Hero", _us, 11, 11);
            _store.Create(new ArmyData(_them, new List<DataBinding<UnitData>> { boss, near1, near2, far }));
            _store.Create(new ArmyData(_us, new List<DataBinding<UnitData>> { hero }));
            var ctx = Ctx(new Answering(_ => new()), EGameSystem.GFStarQuest);
            GameSettings settings = ctx.Settings;
            settings.GameMode = EGameMode.StarQuestHorde;
            ctx.Settings = settings;

            Assert.That(AuraConferral.IsQuestEnemy(ctx, boss.GetValue()), Is.True);
            AuraConferral.GrantToNearest(ctx, boss.GetValue(), 2);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(near1.GetValue(), "Furious"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(near2.GetValue(), "Furious"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(far.GetValue(), "Furious"), Is.False, "only the 2 nearest");
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(hero.GetValue(), "Furious"), Is.False, "never an enemy unit");
        }

        [Test]
        public async Task TheBot_FillsEveryRow_InTheOrderOffered()
        {
            DataBinding<UnitData> a = MakeUnit("A", _us, 1, 1), b = MakeUnit("B", _us, 2, 2), c = MakeUnit("C", _us, 3, 3);
            var opts = new[] { a, b, c }.Select(u => new PickCommandGroupUnitsRequest.ValidOption(u, u.GetValue().Name)).ToList();
            var request = new PickAuraUnitsRequest(_us, new[]
            {
                new PickAuraUnitsRequest.Row(a, "A", "Furious Aura", "Furious", 2, opts),
                new PickAuraUnitsRequest.Row(a, "A", "Banner", "Banner", 2, opts.Skip(2).ToList()),
            });

            List<List<DataBinding<UnitData>>> reply = await new AiPickAuraUnitsResolver().Resolve(request);

            Assert.That(reply[0].Select(u => u.GetValue().Name), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(reply[1].Select(u => u.GetValue().Name), Is.EqualTo(new[] { "C" }));
        }

        // -- helpers ------------------------------------------------------------------------------------

        private TriggeredMoveTestContext Ctx(IPlayerRequestByID requester, EGameSystem system)
        {
            var ctx = new TriggeredMoveTestContext(_store, requester);
            GameSettings settings = ctx.Settings;
            settings.GameSystem = system;
            ctx.Settings = settings;
            return ctx;
        }

        private async Task RunStage(IPlayerRequestByID requester, EGameSystem system)
        {
            var ctx = Ctx(requester, system);
            var stage = new PickCommandGroupUnitsStage(ctx, new NoOpLayer<IDeploymentContext>());
            stage.ToDeployAllUnits.Bind("next");
            await stage.Enter(new DeploymentContext(ctx));
        }

        private DataBinding<UnitData> MakeUnit(string name, PlayerID owner, float x, float z, bool aura = false, bool banner = false)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(
                    new ModelData(0.5f, new List<Weapon>(), new Position(x + i, z), _store))));
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: models);
            if (aura) unit.AttachRuleDefinition(new ResolvedRule("Furious Aura", FuriousAura()));
            if (banner) unit.AttachRuleDefinition(new ResolvedRule("Banner", Banner()));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private sealed class Answering : IPlayerRequestByID
        {
            private readonly Func<IReadOnlyList<PickAuraUnitsRequest.Row>, List<List<DataBinding<UnitData>>>> _answer;
            public readonly List<PickAuraUnitsRequest> Asked = new();
            public Answering(Func<IReadOnlyList<PickAuraUnitsRequest.Row>, List<List<DataBinding<UnitData>>>> answer) => _answer = answer;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is PickAuraUnitsRequest r)
                {
                    Asked.Add(r);
                    return Task.FromResult((TReply)(object)_answer(r.Rows));
                }
                throw new InvalidOperationException("Unexpected request: " + request.GetType().Name);
            }
        }
    }
}
