using System;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // #409 (OPR 3.6.0) - what used to be the Quick Readjustment integration suite.
    //
    // Quick Readjustment's whole text was "this unit ignores the -1 to hit penalty for shooting after
    // moving when firing Indirect weapons". 3.6 deleted that penalty: Indirect no longer modifies hit
    // rolls at all, it simply stops working unless the unit used a Hold action. A rule whose only job was
    // to cancel the penalty therefore has nothing left to cancel - and worse, left in place unchanged it
    // would stop being a cancellation and start being a free, unsourced +1 to hit after moving. That is
    // why the shipped GdfRuleSupplement.json entry was removed along with the rules change rather than
    // being carried forward; it is on #409's list for the eventual 3.6 army-book re-import, which is when
    // we will learn what (if anything) OPR replaced it with.
    //
    // These tests now pin the two facts that removal rests on, so the rule cannot quietly come back and
    // start paying out:
    //   1. Core Indirect produces no hit-roll modifier in either direction (moved or held).
    //   2. A Quick-Readjustment-shaped rule, if one were re-added unchanged, WOULD be a phantom bonus -
    //      asserted explicitly so the reason for removal is visible rather than folklore.
    [TestFixture]
    public class QuickReadjustmentRuleIntegrationTests
    {
        // The 3.5.1 authoring, kept only as the subject of test 2 below. Not registered anywhere.
        private static SpecialRuleDefinition LegacyQuickReadjustment() =>
            new("Quick Readjustment",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollModifier,
                        new Condition.And(new Condition.WeaponHasRule("Indirect"),
                            new Condition.And(new Condition.AfterMoving(),
                                new Condition.Not(new Condition.IsMelee()))),
                        new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>(),
                ERuleScope.Weapon);

        private static int NetHit(TestRuleHarness harness, IUnit attacker, IUnit defender,
            IWeapon weapon, bool attackerMoved)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(harness.Evaluator.EvaluateAll(
                new HitRollModifierContext(attacker, defender, DistanceInches: 12f,
                    AttackerMoved: attackerMoved, IsMelee: false),
                RuleParticipant.Actor(attacker, weapon)));
            return sink.Net(ERollKind.Hit);
        }

        private static Weapon Mortar()
        {
            var mortar = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            mortar.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            return mortar;
        }

        [Test]
        public void IndirectWeapon_AfterMoving_HasNoHitPenalty()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);

            Assert.That(NetHit(harness, attacker, defender, Mortar(), attackerMoved: true), Is.EqualTo(0),
                "3.6 deleted Indirect's -1 for shooting after moving - there is no penalty to cancel.");
        }

        [Test]
        public void IndirectWeapon_Holding_HasNoHitModifierEither()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);

            Assert.That(NetHit(harness, attacker, defender, Mortar(), attackerMoved: false), Is.EqualTo(0),
                "Indirect is a sight permission in 3.6, not a hit modifier, so Holding changes nothing here "
                + "either - what Holding gates is the out-of-line-of-sight shot itself.");
        }

        // The reason the supplement entry had to go, stated as an assertion. If someone re-adds the 3.5.1
        // authoring verbatim during the 3.6 book re-import, this is what they would be shipping.
        [Test]
        public void LegacyQuickReadjustment_WouldNowBeAnUnsourcedBonus()
        {
            var harness = new TestRuleHarness();
            IUnit attacker = harness.BuildUnit("P1", 1);
            IUnit defender = harness.BuildUnit("P2", 1);

            Weapon mortar = Mortar();
            mortar.AttachRuleDefinition(
                new ResolvedRule("Quick Readjustment", LegacyQuickReadjustment()));

            Assert.That(NetHit(harness, attacker, defender, mortar, attackerMoved: true), Is.EqualTo(+1),
                "with Indirect's -1 gone, the old cancellation rule nets a straight +1 to hit after moving "
                + "- a bonus no 3.6 rulebook grants. Hence its removal from GdfRuleSupplement.json.");
        }
    }
}
