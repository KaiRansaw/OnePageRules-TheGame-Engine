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
    /// <summary>
    /// #414 - the two Campaign Rules v3.6.0 hero skills that were markers doing nothing and are now real:
    /// **Explorer** (once per game, ignore Difficult and Dangerous terrain) and **Sharpshooter** (once per
    /// game, +12" range when shooting).
    ///
    /// <para>
    /// Both are offered at <see cref="EHookID.Activation_OnActivationStart"/> and grant a helper rule for
    /// the activation, rather than being authored at the terrain or range hook directly. That is not a
    /// style choice: only the ten hooks in <c>RuleFireLint.AbilityOfferingHooks</c> ever offer an
    /// <see cref="ActivatedAbility"/>, so an ability on <c>Movement_OnMoveThroughTerrain</c> or
    /// <c>Shooting_OnRangeCheck</c> would be dead data. These tests therefore check the whole chain -
    /// offer, accept, and the effect showing up in the query the game actually reads - because a rule
    /// that offers but grants nothing readable would look fine in the catalog and do nothing at the table.
    /// </para>
    ///
    /// <para>
    /// The once-per-game gate itself is <see cref="Cost.OncePerGame"/>, which the engine has had all
    /// along (an <c>"AbilityUsed:&lt;rule&gt;"</c> marker token with a ManualOnly clear trigger). The
    /// catalog comments claiming otherwise were stale; see WorkItems/414.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignOncePerGameSkillTests
    {
        private static TestRuleHarness Harness()
        {
            var harness = new TestRuleHarness();
            foreach (SpecialRuleDefinition definition in Campaign36RuleCatalog.All)
                harness.Register(definition);
            return harness;
        }

        private static AbilityOffer SingleOffer(TestRuleHarness harness, IUnit unit, string ruleName)
        {
            IReadOnlyList<AbilityOffer> offers = harness.OfferAbilities(new ActivationStartContext(unit));
            Assert.That(offers.Select(o => o.RuleName), Does.Contain(ruleName));
            return offers.Single(o => o.RuleName == ruleName);
        }

        // ── Explorer ────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Explorer_IsOfferedAtActivationStart()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Explorer.Name);

            Assert.That(SingleOffer(harness, hero, "Skill: Explorer"), Is.Not.Null);
        }

        // AllTerrain, not DifficultOnly: the book waives Dangerous too. DifficultOnly is Strider, and
        // IgnoresAllTerrain returns false for it - so asserting on this query is what distinguishes the
        // two, and getting it wrong would quietly leave heroes taking dangerous-terrain wounds.
        [Test]
        public void UsingExplorer_WaivesDifficultAndDangerousTerrain_ForTheActivation()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Explorer.Name);

            Assert.That(MovementRuleQueries.IgnoresAllTerrain(hero, harness.Evaluator), Is.False,
                "nothing is waived before the skill is used.");

            OperationApplier.ApplyTokenOperations(
                harness.Accept(SingleOffer(harness, hero, "Skill: Explorer"), hero));

            Assert.That(MovementRuleQueries.IgnoresAllTerrain(hero, harness.Evaluator), Is.True);
        }

        [Test]
        public void Explorer_IsNotOfferedTwiceInAGame()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Explorer.Name);

            OperationApplier.ApplyTokenOperations(
                harness.Accept(SingleOffer(harness, hero, "Skill: Explorer"), hero));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(hero)).Select(o => o.RuleName),
                Does.Not.Contain("Skill: Explorer"), "once per GAME - nothing clears the used marker.");
        }

        // ── Sharpshooter ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Sharpshooter_IsOfferedAtActivationStart()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Sharpshooter.Name);

            Assert.That(SingleOffer(harness, hero, "Skill: Sharpshooter"), Is.Not.Null);
        }

        [Test]
        public void UsingSharpshooter_Adds12InchesOfRange_ForTheActivation()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Sharpshooter.Name);
            IUnit target = harness.BuildUnit("P2", 1);
            // BuildUnit's models carry no weapons, and EffectiveRange is per-weapon, so the hero needs a
            // real one to measure against.
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

            float before = RangeRuleQueries.EffectiveRange(hero, rifle, target, harness.Evaluator);

            OperationApplier.ApplyTokenOperations(
                harness.Accept(SingleOffer(harness, hero, "Skill: Sharpshooter"), hero));

            float after = RangeRuleQueries.EffectiveRange(hero, rifle, target, harness.Evaluator);

            Assert.That(after - before, Is.EqualTo(12f),
                "the book says +12\"; the core rule this copies (Increased Shooting Range) is +6.");
        }

        [Test]
        public void Sharpshooter_IsNotOfferedTwiceInAGame()
        {
            TestRuleHarness harness = Harness();
            IUnit hero = harness.BuildUnit("P1", 1, Campaign36RuleCatalog.Sharpshooter.Name);

            OperationApplier.ApplyTokenOperations(
                harness.Accept(SingleOffer(harness, hero, "Skill: Sharpshooter"), hero));

            Assert.That(harness.OfferAbilities(new ActivationStartContext(hero)).Select(o => o.RuleName),
                Does.Not.Contain("Skill: Sharpshooter"));
        }

        // ── Both ────────────────────────────────────────────────────────────────────────────────────

        // A hero with no skill gets no offer, so nothing here leaks onto ordinary units.
        [Test]
        public void AUnitWithoutTheSkills_IsOfferedNeither()
        {
            TestRuleHarness harness = Harness();
            IUnit plain = harness.BuildUnit("P1", 1);

            IEnumerable<string> offered = harness.OfferAbilities(new ActivationStartContext(plain))
                .Select(o => o.RuleName);

            Assert.That(offered, Does.Not.Contain("Skill: Explorer").And.Not.Contain("Skill: Sharpshooter"));
        }

        // The helper rules are the bodies of the two abilities, not skills in their own right: they must
        // never appear on a hero skill table, or a level-up could roll "Explorer: Surefooted" as a skill.
        [Test]
        public void TheHelperRules_AreNotOnAnyHeroSkillTable()
        {
            IEnumerable<string> everySkill = FDG.Campaign.CampaignTables.SkillSets.Values.SelectMany(s => s);

            Assert.That(everySkill, Does.Not.Contain(Campaign36RuleCatalog.ExplorerBoostName)
                .And.Not.Contain(Campaign36RuleCatalog.SharpshooterBoostName));
        }
    }
}
