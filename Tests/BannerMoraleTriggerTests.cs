using System;
using System.Collections.Generic;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Audit finding 3 - <b>a Banner's +1 is for combat morale only</b>.
    ///
    /// <para>
    /// Every current book and Army Forge's own text agree, in all six systems:
    /// <i>"+1 to Morale Test rolls <b>triggered by shooting or melee</b>."</i> The bundled definition
    /// carried <c>condition: always</c>, which is indistinguishable from correct in a plain game - almost
    /// every morale test there does come from combat - and wrong as soon as a non-combat source is in
    /// play: a spell's morale test, a unit rule that forces one (Mind Control, Fatigue Debuff), or a
    /// campaign random event (#432).
    /// </para>
    ///
    /// <para>
    /// This fixture covers the ENGINE half - the new condition kind, and the trigger reaching it intact
    /// through <see cref="MoraleUtilities.TakeMoraleTest"/>. The authored-data half (every bundled book
    /// really carrying the gated condition) cannot be read from here, because the books ship with the
    /// app; it lives in <c>FdgRaylib.Tests/BannerShippedDataTests</c>. <b>Both halves are needed.</b>
    /// The bug was in data, so these behaviour tests would have passed against the broken books all
    /// along - and a data test alone would not notice the trigger being mis-threaded at a call site.
    /// </para>
    /// </summary>
    [TestFixture]
    public class BannerMoraleTriggerTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // -- The condition kind itself ------------------------------------------------------------------

        [TestCase(EMoraleTrigger.ShootingOrMelee, true)]
        [TestCase(EMoraleTrigger.OtherSource, false)]
        public void TheCondition_IsTrueOnlyForCombatMorale(EMoraleTrigger trigger, bool expected)
        {
            IUnit unit = MakeUnit(quality: 4).GetValue();
            var condition = new Condition.MoraleTriggeredByCombat();

            bool fired = condition.Evaluate(new RuleInvocation(
                new PreMoraleTestContext(unit, trigger), unit, Array.Empty<RuleArgument>()));

            Assert.That(fired, Is.EqualTo(expected),
                trigger == EMoraleTrigger.ShootingOrMelee
                    ? "shooting or melee is the book's own case."
                    : "a spell, a unit rule or a campaign event is not shooting or melee.");
        }

        // -- The trigger reaching the rule through the real morale engine -------------------------------

        /// <summary>
        /// A Banner-shaped rule (the bundled definition's exact shape: the pre-morale hook, the combat
        /// gate, +1 Morale) eases the threshold on a combat-triggered test.
        /// </summary>
        [Test]
        public async Task BannerShapedRule_EasesTheThreshold_OnCombatMorale()
        {
            DataBinding<UnitData> unit = MakeUnit(quality: 5);
            unit.GetValue().AttachRuleDefinition(BannerShaped());

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5, EMoraleTrigger.ShootingOrMelee);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                "the +1 lowers the Quality-5 threshold to 4 for a combat-triggered test.");
            Assert.That(outcome.Passed, Is.True, "the eased test passes on a 4.");
        }

        /// <summary>
        /// The regression this finding is about. Same unit, same die, same rule - but the test was forced
        /// by something that is not shooting or melee, so the threshold must stay at Quality. Before the
        /// fix this eased to 4 and passed.
        /// </summary>
        [Test]
        public async Task BannerShapedRule_DoesNothing_WhenMoraleCameFromAnywhereElse()
        {
            DataBinding<UnitData> unit = MakeUnit(quality: 5);
            unit.GetValue().AttachRuleDefinition(BannerShaped());

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5, EMoraleTrigger.OtherSource);

            Assert.That(outcome.RollNeeded, Is.EqualTo(5),
                "a spell/rule/campaign-event morale test gets no help - the books gate the bonus on "
                + "\"triggered by shooting or melee\".");
            Assert.That(outcome.Passed, Is.False, "4 does not clear an un-eased Quality-5 test.");
        }

        /// <summary>
        /// An unconditional morale rule must be unaffected by any of this. Motivated is the real one -
        /// Campaign Rules v3.6.0 and Army Forge both print a plain <i>"Gets +1 to rolls when taking
        /// Morale Tests"</i> with no trigger clause - so it still helps a spell-driven test, and a future
        /// change that gated the whole hook rather than this one rule would fail here.
        /// </summary>
        [Test]
        public async Task AnUnconditionalMoraleRule_StillHelpsANonCombatTest()
        {
            DataBinding<UnitData> unit = MakeUnit(quality: 5);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Motivated", new SpecialRuleDefinition(
                "Motivated",
                new[]
                {
                    new HookEntry(EHookID.Morale_OnPreMoraleTest, new Condition.Always(),
                        new Effect.RollModifier(ERollKind.Morale, Delta: +1), ELifetime.ThisActivation),
                },
                Array.Empty<ActivatedAbility>())));

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unit.GetValue(), baseRollNeeded: 5, EMoraleTrigger.OtherSource);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                "Motivated's text carries no trigger clause, so it applies to every morale test.");
        }

        // -- The call sites, which is where a correct rule can still be fed the wrong trigger -----------

        /// <summary>
        /// The wound-driven path - shooting, or melee wounds taking a unit to half strength - must count
        /// as combat. It is the single most common way a morale test happens, so passing the wrong
        /// trigger here would switch every Banner in the game off in ordinary play while leaving all the
        /// tests above green.
        /// </summary>
        [Test]
        public async Task TheWoundDrivenPath_CountsAsCombat()
        {
            DataBinding<UnitData> unit = MakeUnit(quality: 5, modelCount: 4);
            unit.GetValue().AttachRuleDefinition(BannerShaped());
            KillModels(unit, 2); // half strength: 2 of 4 living

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(
                Ctx(die: 4), unit, remainingWoundsBefore: 4f);

            Assert.That(result, Is.True,
                "wounds come from shooting or melee, so the bonus applies and the 4 clears the eased test.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        /// <summary>And the same unit without the rule fails that test - so the one above is the rule's doing.</summary>
        [Test]
        public async Task TheWoundDrivenPath_FailsWithoutTheRule()
        {
            DataBinding<UnitData> unit = MakeUnit(quality: 5, modelCount: 4);
            KillModels(unit, 2);

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(
                Ctx(die: 4), unit, remainingWoundsBefore: 4f);

            Assert.That(result, Is.False, "no bonus, so the threshold stays at 5 and a 4 fails.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        // -- Helpers -----------------------------------------------------------------------------------

        /// <summary>
        /// The bundled Banner's exact shape. Kept in step with <c>GdfRuleSupplement.json</c> by
        /// <c>BannerShippedDataTests</c>, which asserts the real definition has this condition - so if
        /// the data drifts, that fixture fails rather than this one quietly testing a fiction.
        /// </summary>
        private static ResolvedRule BannerShaped() => new ResolvedRule("Banner", new SpecialRuleDefinition(
            "Banner",
            new[]
            {
                new HookEntry(EHookID.Morale_OnPreMoraleTest,
                    new Condition.MoraleTriggeredByCombat(),
                    new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>()));

        private TestGameContext Ctx(int die) => new TestGameContext(_store, new FixedDiceRoller(die));

        private static void KillModels(DataBinding<UnitData> unit, int count)
        {
            for (int i = 0; i < count; i++)
            {
                ModelData model = unit.GetValue().ModelBindings[i].GetValue();
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
        }

        private DataBinding<UnitData> MakeUnit(int quality, int modelCount = 1)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(0, 0), gameDataStore: _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "U", quality: quality, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
