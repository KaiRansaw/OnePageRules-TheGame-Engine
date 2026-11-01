using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #422: Slayer's SHOOTING arm is gated on a Hold action, not on
    // a distance.
    //
    // The book (GF Core 3.6.0, and identically in the other five systems' core books):
    //     "When using Hold actions and shooting, or when charging, this model's weapons get AP(+2) if
    //      most models in the target have Tough(3) or higher."
    //
    // What the engine had was `DistanceGreaterThan(9)` - a flat misread that was wrong in BOTH
    // directions, which is why it needed tests rather than just a fix:
    //   * a unit that Advanced and then shot a Tough(3)+ target at 20" was GIVEN AP(+2) it should not
    //     have had (no Hold action), and
    //   * a unit that Held and shot the same target at 5" was DENIED the AP(+2) the book grants it.
    // Both of those are a case below, so a regression to the distance reading fails here loudly.
    //
    // "Hold action" is read as "has not moved this round" - the same idiom Artillery uses for the same
    // phrase (see HitRollRuleIntegrationTests). Slayer additionally needs Not(IsMelee) where Artillery
    // did not: Artillery's surviving 12" gate cannot pass in base contact, but with Slayer's distance
    // gate gone the shooting entry would otherwise fire on a held unit's STRIKE-BACK, and the book says
    // "and shooting". StrikeBack_HeldInMelee_NoAp is that guard's test.
    [TestFixture]
    public class SlayerRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position PointBlankPos = new Position(5, 5);
        private static readonly Position LongRangePos = new Position(20, 5);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(6)); // every die shows 6
        }

        // The half the old distance gate DENIED. A Hold action against a Tough(3) target at 5" is
        // exactly what the book grants AP(+2) for, and the engine gave nothing because 5 < 9.
        [Test]
        public async Task HoldAndShoot_AtPointBlank_GrantsApTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(PointBlankPos, wounds: 3); // Tough(3)
            AttachSlayer(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(-2),
                "a Hold action shooting a Tough(3) target gets AP(+2), whatever the range - the old "
                + "engine required over 9\" and so gave nothing at 5\".");

            DetermineSaveRollNeededResults saves = await RunSaveNeededStage(attacker, defender, hits);
            foreach (PendingSaveRolls pending in saves.PendingSaveRollsList)
            {
                Assert.That(pending.SaveNeeded, Is.EqualTo(6), "defense 4 + 2 from Slayer's AP(+2).");
            }
        }

        // Still granted at long range, because the range was never what mattered - the Hold was.
        [Test]
        public async Task HoldAndShoot_AtLongRange_GrantsApTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(LongRangePos, wounds: 3);
            AttachSlayer(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(-2));
        }

        // The half the old distance gate wrongly GRANTED: a unit that Advanced and then shot from 20"
        // passed `DistanceGreaterThan(9)` and got AP(+2) with no Hold action anywhere in sight.
        [Test]
        public async Task MovedThenShoot_AtLongRange_NoAp()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(LongRangePos, wounds: 3);
            AttachSlayer(attacker);
            StampMoved(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0),
                "no Hold action - the unit moved this round, so Slayer's shooting arm must not fire, "
                + "however far away the target is.");
        }

        // The Not(IsMelee) guard. A unit that Held is, by definition, still unmoved when it is charged
        // and strikes back; without the guard the shooting entry would fire on that strike-back and
        // hand out AP(+2) the book reserves for "shooting" and "when charging".
        [Test]
        public async Task StrikeBack_HeldInMelee_NoAp()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(PointBlankPos, wounds: 3);
            AttachSlayer(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender, isMelee: true, isCharging: false);
            Assert.That(hits.SaveModifier, Is.EqualTo(0),
                "striking back is neither shooting nor charging - a held unit must not carry its "
                + "shooting AP into melee.");
        }

        // The melee arm the book does grant, untouched by this fix and here so the fix is not mistaken
        // for having removed it.
        [Test]
        public async Task Charging_GrantsApTwo()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(PointBlankPos, wounds: 3);
            AttachSlayer(attacker);
            StampMoved(attacker);   // a charge IS a move; the melee arm must not care

            RollToHitResults hits = await RunHitStage(attacker, defender, isMelee: true, isCharging: true);
            Assert.That(hits.SaveModifier, Is.EqualTo(-2),
                "\"or when charging\" - and charging always involves moving, so the melee arm cannot be "
                + "gated on the Hold token.");
        }

        // The Tough(3) half of the condition, which the fix left alone. A one-wound target gets nothing.
        [Test]
        public async Task HoldAndShoot_SoftTarget_NoAp()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(PointBlankPos); // 1 wound, no Tough
            AttachSlayer(attacker);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0),
                "Slayer is anti-armour only - most models in the target must have Tough(3) or higher.");
        }

        [Test]
        public async Task NoSlayer_NoAp()
        {
            DataBinding<UnitData> attacker = MakeUnit(AttackerPos);
            DataBinding<UnitData> defender = MakeUnit(PointBlankPos, wounds: 3);

            RollToHitResults hits = await RunHitStage(attacker, defender);
            Assert.That(hits.SaveModifier, Is.EqualTo(0));
        }

        private async Task<RollToHitResults> RunHitStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            bool isMelee = false, bool isCharging = false)
        {
            var stage = new RollToHitStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender, isMelee, isCharging);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: 1)); // a 6 clears 4+
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults result), Is.True);
            return result;
        }

        private async Task<DetermineSaveRollNeededResults> RunSaveNeededStage(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender, RollToHitResults hits)
        {
            var stage = new DetermineSaveRollsNeededStage<ICombatMetadata>(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var metadata = NewMetadata(attacker, defender);
            metadata.AddResult(hits);
            metadata.AddResult(new CoverCheckResults(0)); // no cover
            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out DetermineSaveRollNeededResults result), Is.True);
            return result;
        }

        private CombatMetadata NewMetadata(DataBinding<UnitData> attacker, DataBinding<UnitData> defender,
            bool isMelee = false, bool isCharging = false)
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            return new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1,
                isMelee: isMelee, isCharging: isCharging);
        }

        private static void AttachSlayer(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Slayer", CoreRuleCatalog.Slayer));

        // The "has not moved" half of the Hold gate reads TokenType.MovedThisRound, which MovementStage
        // stamps for real and which these stage-level tests have to stamp by hand.
        private static void StampMoved(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

        private DataBinding<UnitData> MakeUnit(Position position, int wounds = 1)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            if (wounds > 1) model.SetMaxWounds(wounds);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
