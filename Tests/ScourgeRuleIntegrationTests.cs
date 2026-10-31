using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #412 Scourge: "for each unmodified result of 1 to block hits, enemies count as having taken 2 extra
    // wounds when checking who won melee, or if shooting wounds would trigger Morale Tests."
    //
    // Scourge is the first rule whose trigger and consumer sit at different hooks - the block rolls that
    // earn the wounds are only visible at the save hook, and both things that SPEND them (the
    // who-won-melee comparison, the shooting half-strength check) run later against contexts that cannot
    // see those rolls. So the mechanism is a banked tally, and these tests cover the two halves of it:
    // the banking effect turning block rolls into a tally, and the half-strength predicate honouring a
    // tally without actually wounding anyone.
    [TestFixture]
    public class ScourgeRuleIntegrationTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public void HalfStrengthCheck_CountsTheBankedTally_WithoutWoundingAnyone()
        {
            // Four 1-wound models, one dead: 3 of 4 wounds left, comfortably above half.
            UnitData unit = MakeUnit(models: 4);
            unit.Models[0].DealWounds(1f);
            float before = 4f;

            Assert.That(unit.GetIsAtHalfStrength(), Is.False, "3 of 4 wounds is above half.");
            Assert.That(MoraleUtilities.WoundsLeftUnitAtHalfStrength(before, unit), Is.False,
                "without a tally, one dead model out of four triggers no test.");

            // +1 counted wound still leaves 2 of 4 notional - exactly half, which is "half or less".
            Assert.That(MoraleUtilities.WoundsLeftUnitAtHalfStrength(before, unit, extraWoundsCounted: 1f),
                Is.True, "counted wounds push it onto the half line, so the test is due.");

            Assert.That(unit.RemainingWounds, Is.EqualTo(3f),
                "and nothing was actually wounded - the tally is a comparison figure only.");
        }

        [Test]
        public void HalfStrengthCheck_TallyWithoutRealWounds_StillTakesNoTest()
        {
            // The rule reads "if shooting WOUNDS would trigger Morale Tests" - a unit that took no real
            // wounds this activation never tests, however large the tally. Guards against a tally that
            // conjures a morale test out of a volley that missed entirely.
            UnitData unit = MakeUnit(models: 4);

            Assert.That(MoraleUtilities.WoundsLeftUnitAtHalfStrength(4f, unit, extraWoundsCounted: 99f),
                Is.False, "no real wounds this action, so no test - the tally cannot create one.");
        }

        [Test]
        public void Banking_TurnsBlockRollsIntoATally()
        {
            UnitData bearer = MakeUnit(models: 1);
            var effect = new Effect.BankTokenPerUnmodifiedSaveRoll(
                TokenType.MeleeWoundTally, OnRollValue: 1, PerMatch: 2);

            // Three unmodified 1s to block, at 2 counted wounds each.
            List<RuleOperation> operations = Fire(effect, bearer, onesRolled: 3);

            RuleOperation.GrantTokenToUnit? grant = operations
                .OfType<RuleOperation.GrantTokenToUnit>().FirstOrDefault();
            Assert.That(grant, Is.Not.Null, "three matching block rolls must bank a tally.");
            Assert.That(grant!.TokenToGrant.Type, Is.EqualTo(TokenType.MeleeWoundTally));
            Assert.That(grant.TokenToGrant.Count, Is.EqualTo(6), "3 rolls x 2 wounds each.");
        }

        [Test]
        public void Banking_NoMatchingRolls_BanksNothing()
        {
            UnitData bearer = MakeUnit(models: 1);
            var effect = new Effect.BankTokenPerUnmodifiedSaveRoll(
                TokenType.MeleeWoundTally, OnRollValue: 1, PerMatch: 2);

            List<RuleOperation> operations = Fire(effect, bearer, onesRolled: 0);

            Assert.That(operations, Is.Empty,
                "every block roll succeeded, so there is nothing to bank - and no zero-count token.");
        }

        [Test]
        public void Spending_ConvertsTheTallyIntoTheMeleeWinnerBonus()
        {
            UnitData bearer = MakeUnit(models: 1);
            for (int i = 0; i < 6; i++)
            {
                bearer.Tokens.AddToken(new Token(TokenType.MeleeWoundTally, 1,
                    new TokenClearTrigger.ManualOnly()));
            }

            var effect = new Effect.TokenScaledExtraMeleeWoundCount(TokenType.MeleeWoundTally);
            var operations = new List<RuleOperation>();
            effect.Apply(InvocationFor(bearer, new MeleeResolutionContext(bearer, bearer)), operations);

            RuleOperation.ExtraMeleeWoundCount? bonus = operations
                .OfType<RuleOperation.ExtraMeleeWoundCount>().FirstOrDefault();
            Assert.That(bonus, Is.Not.Null, "a banked tally must reach the who-won-melee comparison.");
            Assert.That(bonus!.Amount, Is.EqualTo(6),
                "the tally is spent 1:1 as Fear(X)'s existing bonus, so the comparison needs no new concept.");
        }

        private List<RuleOperation> Fire(Effect effect, UnitData bearer, int onesRolled)
        {
            IDiceResults saveRolls = new FixedFaceDiceRoller(1).Roll(6, onesRolled);
            var hook = new SaveRollCompleteContext(bearer, bearer, saveRolls, IsMelee: true);
            var operations = new List<RuleOperation>();
            effect.Apply(InvocationFor(bearer, hook), operations);
            return operations;
        }

        private static RuleInvocation InvocationFor(UnitData bearer, IHookContext hook) =>
            new RuleInvocation(hook, bearer, System.Array.Empty<RuleArgument>());

        private UnitData MakeUnit(int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: new Position(i, 0),
                    gameDataStore: _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
