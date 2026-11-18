using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // Campaign Rules 3.6.0 hero skills that were placeholders (name on the card, no effect) until 2026-11-18:
    //   Motivator  "+1 to Morale Test rolls triggered by shooting"
    //   Leader     "+1 to Morale Test rolls in melee"
    //   Duellist   "AP(+1) in melee when charged"
    //   Suppressor "AP(+1) when using Hold and shooting"
    //   Destroyer  "AP(+1) when using move actions and shooting"
    // Each test pairs the case the skill covers with the one it doesn't, so a skill that fires everywhere fails too.
    [TestFixture]
    public class CampaignHeroSkillTests
    {
        private static (TestRuleHarness h, IUnit hero, IUnit enemy) Setup(SpecialRuleDefinition skill)
        {
            var h = new TestRuleHarness();
            h.Register(skill);
            return (h, h.BuildUnit("P1", 1, skill.Name), h.BuildUnit("P2", 1));
        }

        private static float Ap(SpecialRuleDefinition skill, bool melee, bool charging, bool moved)
        {
            var (h, hero, enemy) = Setup(skill);
            var ctx = new HitRollCompleteContext(hero, enemy, TestDice.Faces(4), DistanceInches: melee ? 0.5f : 12f,
                IsMelee: melee, IsCharging: charging, ChargeOriginDistanceInches: charging ? 5f : 0f,
                AttackerMoved: moved);
            return -h.Evaluate(hero, ERuleSeat.Actor, ctx).OfType<RuleOperation.ApplyRollModifier>()
                .Where(m => m.Roll == ERollKind.Save).Sum(m => m.Delta);
        }

        private static float Morale(SpecialRuleDefinition skill, EMoraleTrigger trigger)
        {
            var (h, hero, _) = Setup(skill);
            return h.Evaluate(hero, ERuleSeat.Actor, new PreMoraleTestContext(hero, trigger, 12f))
                .OfType<RuleOperation.ApplyRollModifier>().Where(m => m.Roll == ERollKind.Morale).Sum(m => m.Delta);
        }

        [Test]
        public void Duellist_IsApPlusOne_WhenStrikingBack_NotWhenCharging_NorShooting()
        {
            Assert.That(Ap(Campaign36RuleCatalog.Duellist, melee: true, charging: false, moved: false), Is.EqualTo(1f));
            Assert.That(Ap(Campaign36RuleCatalog.Duellist, melee: true, charging: true, moved: true), Is.EqualTo(0f));
            Assert.That(Ap(Campaign36RuleCatalog.Duellist, melee: false, charging: false, moved: false), Is.EqualTo(0f));
        }

        [Test]
        public void Suppressor_IsApPlusOne_OnlyWhenShootingAfterHold()
        {
            Assert.That(Ap(Campaign36RuleCatalog.Suppressor, melee: false, charging: false, moved: false), Is.EqualTo(1f));
            Assert.That(Ap(Campaign36RuleCatalog.Suppressor, melee: false, charging: false, moved: true), Is.EqualTo(0f));
            Assert.That(Ap(Campaign36RuleCatalog.Suppressor, melee: true, charging: false, moved: false), Is.EqualTo(0f));
        }

        [Test]
        public void Destroyer_IsApPlusOne_OnlyWhenShootingAfterAMove()
        {
            Assert.That(Ap(Campaign36RuleCatalog.Destroyer, melee: false, charging: false, moved: true), Is.EqualTo(1f));
            Assert.That(Ap(Campaign36RuleCatalog.Destroyer, melee: false, charging: false, moved: false), Is.EqualTo(0f));
            Assert.That(Ap(Campaign36RuleCatalog.Destroyer, melee: true, charging: true, moved: true), Is.EqualTo(0f));
        }

        [Test]
        public void Motivator_IsPlusOne_ForShootingMoraleOnly()
        {
            Assert.That(Morale(Campaign36RuleCatalog.Motivator, EMoraleTrigger.Shooting), Is.EqualTo(1f));
            Assert.That(Morale(Campaign36RuleCatalog.Motivator, EMoraleTrigger.Melee), Is.EqualTo(0f));
            Assert.That(Morale(Campaign36RuleCatalog.Motivator, EMoraleTrigger.OtherSource), Is.EqualTo(0f));
        }

        [Test]
        public void Leader_IsPlusOne_ForMeleeMoraleOnly()
        {
            Assert.That(Morale(Campaign36RuleCatalog.Leader, EMoraleTrigger.Melee), Is.EqualTo(1f));
            Assert.That(Morale(Campaign36RuleCatalog.Leader, EMoraleTrigger.Shooting), Is.EqualTo(0f));
            Assert.That(Morale(Campaign36RuleCatalog.Leader, EMoraleTrigger.OtherSource), Is.EqualTo(0f));
        }

        [Test]
        public void NoneOfTheFive_IsAPlaceholderAnyMore()
        {
            foreach (SpecialRuleDefinition d in new[] { Campaign36RuleCatalog.Motivator, Campaign36RuleCatalog.Leader,
                         Campaign36RuleCatalog.Duellist, Campaign36RuleCatalog.Suppressor, Campaign36RuleCatalog.Destroyer })
            {
                Assert.That(d.Passive, Is.Not.Empty, d.Name);
                Assert.That(d.Description, Does.Not.Contain("not wired up"), d.Name);
            }
        }
    }
}
