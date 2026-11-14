using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #412 Parry, mirroring ExtraHitRuleIntegrationTests: proves the
    // first hit-CANCELLING rule flows through the real RollToHitStage. The defender's rule fires at
    // Shooting_OnHitRollComplete from the Subject seat, the RuleEvaluator queues IgnoreHitsOnRoll, the
    // HitIgnoreSink pools the dice, and the stage throws them and removes hits from the successful-hit
    // list - the mirror of the injection path those tests cover.
    //
    // FixedDiceRoller(6) makes every die a 6, so every parry die cancels; a 1-roller proves the rule is
    // actually rolled rather than applied flat. Both are needed: with only the 6 case, a bug that
    // ignored the roll entirely would pass.
    [TestFixture]
    public class ParryRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position DefenderPos = new Position(3, 5);

        private GameDataStore _store = null!;

        private static SpecialRuleDefinition Parry(int dicePerModel = 1) =>
            new SpecialRuleDefinition("Parry",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete,
                        new Condition.And(new Condition.AnyModelHasThisRule(), new Condition.IsMelee()),
                        new Effect.IgnoreHitsOnRoll(new ValueSource.Literal(dicePerModel),
                            SuccessThreshold: 6),
                        ELifetime.ThisAttack,
                        Seat: ERuleSeat.Subject),
                },
                System.Array.Empty<ActivatedAbility>(),
                Valence: EValence.Positive,
                Description: "Test Parry.");

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task NoParry_HitStands()
        {
            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), MakeUnit(DefenderPos),
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(1f), "without Parry the natural 6 simply hits.");
        }

        [Test]
        public async Task Parry_EveryDieSucceeds_CancelsTheHit()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(0f),
                "one hit taken, one parry die, and it rolled a 6 - the hit is ignored.");
        }

        [Test]
        public async Task Parry_EveryDieFails_HitStands()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(1));

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "the parry die rolled a 1, so nothing is cancelled - Parry is rolled, not automatic.");
        }

        [Test]
        public async Task Parry_InShooting_DoesNothing()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6), isMelee: false);

            Assert.That(TotalHits(result), Is.EqualTo(1f),
                "Parry is a melee rule; its IsMelee gate must keep it out of shooting.");
        }

        [Test]
        public async Task Parry_PoolCappedAtHitsTaken_CannotGoNegative()
        {
            // Five dice per model against a single hit. The cap ("up to as many dice as hits taken")
            // and the clamp both have to hold, or the hit list goes negative and the save stage owes
            // the defender dice that do not exist.
            DataBinding<UnitData> defender = MakeUnit(DefenderPos);
            Attach(defender, Parry(dicePerModel: 5));

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender,
                new FixedDiceRoller(6));

            Assert.That(TotalHits(result), Is.EqualTo(0f),
                "the one hit is cancelled and the total floors at zero rather than going negative.");
        }

        // Build 2.08, the core-rules prose check: Parry is "roll X dice PER MODEL WITH THIS RULE in it", so it
        // has no every-model condition. The engine's general "a defensive rule needs every model" gate switched
        // it off for a unit whose joined hero lacked it, and made a joined hero's own Parry do nothing.

        [Test]
        public async Task EveryModelHasParry_RollsOneSetPerModel()
        {
            DataBinding<UnitData> defender = MakeUnit(DefenderPos, models: 3);
            Attach(defender, Parry());

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), defender, new FixedFaceDiceRoller(6), attacks: 6);

            Assert.That(TotalHits(result), Is.EqualTo(3f), "three models, one die each, every die a 6");
        }

        [Test]
        public async Task AHeroWithoutParry_JoiningAParryUnit_LeavesTheUnitsModelsTheirDice()
        {
            DataBinding<UnitData> host = MakeUnit(DefenderPos, models: 3);
            Attach(host, Parry());
            UnitData hero = MakeUnit(DefenderPos).GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            Join(host.GetValue(), hero);

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), host, new FixedFaceDiceRoller(6), attacks: 6);

            Assert.That(TotalHits(result), Is.EqualTo(3f), "the three models with Parry still roll; the hero has none");
        }

        [Test]
        public async Task AHeroWithParry_InAUnitWithout_RollsItsOwnDice()
        {
            DataBinding<UnitData> host = MakeUnit(DefenderPos, models: 3);
            UnitData hero = MakeUnit(DefenderPos).GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule("Parry", Parry(dicePerModel: 2), new RuleArgument[] { new RuleArgument.Int(2) }));
            Join(host.GetValue(), hero);

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), host, new FixedFaceDiceRoller(6), attacks: 6);

            Assert.That(TotalHits(result), Is.EqualTo(4f), "only the hero has Parry(2): two dice");
        }

        [Test]
        public async Task AHeroAndItsUnit_BothWithParry_RollOneSetPerModel_NeverTwice()
        {
            DataBinding<UnitData> host = MakeUnit(DefenderPos, models: 3);
            Attach(host, Parry());
            UnitData hero = MakeUnit(DefenderPos).GetValue();
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule("Parry", Parry(), new RuleArgument[] { new RuleArgument.Int(1) }));
            Join(host.GetValue(), hero);

            RollToHitResults result = await RunStage(MakeUnit(AttackerPos), host, new FixedFaceDiceRoller(6), attacks: 6);

            Assert.That(TotalHits(result), Is.EqualTo(2f), "four models, one die each - not eight");
        }

        private static void Join(UnitData host, UnitData hero) =>
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host", Name = host.Name }, host),
                (new UnitFileEntry { JoinsUnitId = "host", Name = hero.Name }, hero),
            });

        private async Task<RollToHitResults> RunStage(DataBinding<UnitData> attacker,
            DataBinding<UnitData> defender, IDiceRoller roller, bool isMelee = true, int attacks = 1)
        {
            var ctx = new TestGameContext(_store, roller);
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new RollToHitStage<ICombatMetadata>(ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1,
                attackerMoved: false, isMelee: isMelee, isCharging: false);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: attacks));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True,
                "Stage must store a RollToHitResults in metadata.");
            return result;
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList) total += hit.HitCount;
            return total;
        }

        private static void Attach(DataBinding<UnitData> unit, SpecialRuleDefinition definition) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(definition.Name, definition,
                new RuleArgument[] { new RuleArgument.Int(1) }));

        private DataBinding<UnitData> MakeUnit(Position position, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(position.x + i * 1.6f, position.z),
                    gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
