using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Impact(X) flows through the REAL melee charge-
    // contact stage. ResolveImpactHitsStage fires the OnChargeContact "when", folds the ImpactSink,
    // rolls X dice (each 2+ a hit), and runs those hits through the real save→wound sub-pipeline against
    // the defender — BEFORE any normal swings. FixedDiceRoller(2) makes every die a 2: each impact die
    // is a hit (2+), and each save (needed 4+ vs defense 4, AP 0) fails — so Impact(3) = 3 wounds.
    [TestFixture]
    public class ImpactRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        }

        [Test]
        public async Task ImpactCharger_DealsAutoHits_KillsLethalTarget()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2); // 2 wounds total; 3 impact wounds is lethal

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(0f),
                "Impact(3) deals 3 auto-hits → 3 wounds, wiping out the 2-wound defender before any swing.");
        }

        // #197 misc - Heavy Impact ("Impact(X) with hits that have AP(1)"). AllOnFaceDiceRoller(4): each
        // impact die is a 4 (a hit, 2+), each save is a 4. Against defense 4, an AP-0 save of 4 HOLDS
        // (4 >= 4) but an AP-1 save needs 5 and FAILS - so the only thing separating these two tests is the
        // AP the impact hits now carry.
        [Test]
        public async Task HeavyImpact_HitsCarryAP_TheSaveFailsWhereCoreImpactWouldHold()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(4));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachHeavyImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // 5 wounds, survives 3 wounds

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "Heavy Impact(3): 3 hits, each save (4) fails against AP(1) -> 3 wounds.");
        }

        [Test]
        public async Task CoreImpact_AtTheSameDice_TheSaveHolds()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(4));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3); // AP 0
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(5f),
                "core Impact(3): 3 hits, but each save of 4 holds at AP 0 -> no wounds. The contrast pins " +
                "that Heavy Impact's extra wounds come from the AP, not the dice.");
        }

        [Test]
        public async Task NoImpact_DefenderUnharmed()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Impact rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "with no Impact rule the stage skips — the defender takes no charge-contact hits.");
        }

        [Test]
        public async Task Impact_SubLethal_AssignsExactWoundCount()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5); // 5 wounds → 3 impact wounds is sub-lethal

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "Impact(3) produces 3 failed saves → 3 wounds to assign across the surviving unit.");
        }

        // #409 (OPR 3.6.0): Counter no longer reduces the charger's Impact dice - the clause was deleted
        // outright, leaving only "may strike first ... unless this unit is Fatigued". A Counter defender
        // now eats the full Impact(X) it used to blunt, which is the single biggest play change in the
        // rule. Inverted from the 3.5.1 test that used to live here.
        [Test]
        public async Task CounterDefender_NoLongerReducesImpactDice()
        {
            var requester = new CapturingWoundRequester();
            var ctx = new WoundTestContext(_store, requester, new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            // 5 models so 3 wounds is sub-lethal and the volley routes to the wound requester rather than
            // wiping the unit - the same shape Impact_SubLethal_AssignsExactWoundCount uses. The 3.5.1
            // version of this test could use 2 models because Counter was cancelling 2 of the 3 dice.
            DataBinding<UnitData> defender = MakeUnit(modelCount: 5);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(requester.Captured!.TotalWoundsToAssign, Is.EqualTo(3f),
                "all 3 Impact dice land in 3.6: Counter's per-model reduction is gone.");
        }

        // #409 (OPR 3.6.0): the 3.5.1 companion to the test above asserted that 3 Counter models cancelled
        // Impact(3) outright. With the reduction deleted there is nothing left to cancel, so a full Counter
        // unit now takes the whole volley. Pinned at the size that used to zero it, so a reinstated
        // reduction fails loudly here rather than quietly changing a number.
        [Test]
        public async Task CounterDefender_FullUnit_StillTakesTheWholeImpact()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1);
            AttachImpact(attacker, x: 3);
            DataBinding<UnitData> defender = MakeUnit(modelCount: 3);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(0f),
                "Impact(3) kills all 3 models in 3.6 - the 3.5.1 cancellation is gone.");
        }

        // #409: with the reduction gone, Counter emits nothing at all at charge contact. A charger with no
        // Impact was already a no-op in 3.5.1 (the lone negative reduction skipped the stage); it stays one
        // for a simpler reason now - there is no charge-contact entry on the rule.
        [Test]
        public async Task CounterDefender_NoImpactCharger_NoEffect()
        {
            var ctx = new WoundTestContext(_store, new CapturingWoundRequester(), new AllOnFaceDiceRoller(2));
            DataBinding<UnitData> attacker = MakeUnit(modelCount: 1); // no Impact rule
            DataBinding<UnitData> defender = MakeUnit(modelCount: 2);
            AttachCounter(defender);

            await RunStage(ctx, attacker, defender);

            Assert.That(defender.RemainingWounds(), Is.EqualTo(2f),
                "no Impact and no reduction - nothing happens at charge contact.");
        }

        private static void AttachCounter(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

        private async Task RunStage(WoundTestContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new ResolveImpactHitsStage(ctx, new NoOpLayer<ICombatActionContext>());
            stage.OnImpactResolved.Bind("done");

            var context = new CombatActionContext(ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            await stage.Enter(context);
        }

        private static void AttachImpact(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        // Heavy Impact ships as supplement data; here it's built inline as its engine shape - core Impact's
        // charge-contact entry with AP(1) on the impact hits.
        private static readonly SpecialRuleDefinition HeavyImpactDefinition = new SpecialRuleDefinition(
            "Heavy Impact",
            new[]
            {
                new HookEntry(EHookID.Melee_OnChargeContact, new Condition.Always(),
                    new Effect.ChargeImpactHits(new ValueSource.Arg(0), ArmorPenetration: 1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>());

        private static void AttachHeavyImpact(DataBinding<UnitData> unit, int x) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Heavy Impact", HeavyImpactDefinition,
                new RuleArgument[] { new RuleArgument.Int(x) }));

        // Unlike FixedDiceRoller (which models a single die: TotalRolls is always 1), this returns
        // EVERY rolled die on a fixed face — so Roll(n) yields n dice there. Needed for Impact's
        // multi-die roll and the subsequent multi-save roll to scale (and stay integer/deterministic).
        private sealed class AllOnFaceDiceRoller : IDiceRoller
        {
            private readonly int _face;
            public AllOnFaceDiceRoller(int face) => _face = face;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        private DataBinding<UnitData> MakeUnit(int modelCount)
        {
            var modelBindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0),
                    gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
