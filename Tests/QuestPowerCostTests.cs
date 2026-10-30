using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #414 - the Star Quest power costs, pinned against the **POWER REFERENCE** in GFSQ Core Rules
    /// v2.1.0. Star Quest was built against v2.0.1, where an extra action and a revive each cost 2; the
    /// 2.1.0 book raised both to 3 and left the other three alone.
    ///
    /// <para>
    /// These are numbers the player pays every single activation, so they get their own fixture rather
    /// than riding along in an integration test. The costs also appear in the player-facing labels, and
    /// under v2.0.1 the number was written out separately in five files - so the tests below check the
    /// LABELS too. A cost that is right in the rule and stale on the button is still a bug: it tells the
    /// player a price the game does not charge.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestPowerCostTests
    {
        // "Heroes may spend 3 Power to use any one additional action."
        [Test]
        public void AnExtraAction_Costs3_Not2()
        {
            Assert.That(QuestActions.ExtraActionPower, Is.EqualTo(3),
                "v2.1.0 raised this from 2. An extra SKILL action is an additional action too, so it "
                + "shares this cost.");
        }

        // "Heroes may spend 3 Power to revive other Unconscious Heroes."
        [Test]
        public void RevivingAnUnconsciousHero_Costs3_Not2()
        {
            Assert.That(QuestActions.RevivePower, Is.EqualTo(3));
        }

        // The three the book did NOT change - pinned so a sweep of "2 -> 3" cannot take them with it.
        [Test]
        public void TheUnchangedCosts_StayWhereTheyWere()
        {
            Assert.That(QuestActions.PassStatTestPower, Is.EqualTo(2),
                "counting a failed Str/Dex/Wil test as passed is still 2.");
            Assert.That(QuestActions.IgnoreShakenPower, Is.EqualTo(2),
                "ignoring Shaken from a failed morale test is still 2.");
            Assert.That(QuestActions.FailedSkillPower, Is.EqualTo(1),
                "a failed hero-skill test is still 1 - what changed in 2.1.0 is that paying it became "
                + "OPTIONAL, and the skill now fizzles when you decline (QuestFailedSkillPaymentTests).");
        }

        // The labels the player reads. These are built from the constants now; the test is here so that
        // going back to a hand-written number fails rather than quietly lying on the button.
        [Test]
        public void TheOnScreenLabels_QuoteTheRealCost()
        {
            Assert.That(QuestActions.ExtraActionOption, Is.EqualTo("Extra Action (3 power)"));
            Assert.That(QuestActions.ExtraSkillOption, Is.EqualTo("Extra Skill (3 power)"));
            Assert.That(QuestSkill.ReviveName, Is.EqualTo("Revive a hero (3 power)"));
        }

        // The revive skill's description is what the Actions panel shows under the button, so it has to
        // agree with the button.
        [Test]
        public void TheReviveDescription_AgreesWithItsLabel()
        {
            QuestSkill? revive = QuestSkill.Find(QuestSkill.ReviveName);

            Assert.That(revive, Is.Not.Null, "the skill is looked up by name, so a renamed constant must "
                + "still resolve - this is the test that catches a half-finished rename.");
            Assert.That(revive!.Description, Does.Contain("3 power"));
            Assert.That(revive.Description, Does.Not.Contain("2 power"));
        }
    }
}
