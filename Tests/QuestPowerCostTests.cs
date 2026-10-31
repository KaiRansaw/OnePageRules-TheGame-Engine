using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.Utilities;
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
            // #418 made the prices per system - Star Quest charges 3 power for an extra action and a
            // revive, AoF: Quest's 2.0.1 book charges 2 - so the labels are built from the profile and
            // asserted for BOTH systems. The claim is unchanged and now twice as strong: a player can
            // never be shown a price the spend will not take.
            // Both Quest systems are on 2.1.0 books, so both prices read 3. They were briefly 2 for AoF:
            // Quest, built from its 2.0.1 book before its 2.1.0 ones arrived - which is exactly why the
            // label is DERIVED from the profile rather than typed: the number moved and nothing had to
            // be hunted down.
            foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
            {
                QuestRulesProfile p = QuestRulesProfile.For(system);
                Assert.That(p.ExtraActionOption, Is.EqualTo("Extra Action (3 power)"), $"{system}");
                Assert.That(p.ExtraSkillOption, Is.EqualTo("Extra Skill (3 power)"), $"{system}");
                Assert.That(p.ReviveName, Is.EqualTo("Revive a hero (3 power)"), $"{system}");
            }

            // And the catalog's own labels still agree with Star Quest's profile, since the constants in
            // QuestActions are documented as that system's values.
            Assert.That(QuestActions.ExtraActionOption,
                Is.EqualTo(QuestRulesProfile.StarQuest.ExtraActionOption));
            Assert.That(QuestActions.ExtraSkillOption,
                Is.EqualTo(QuestRulesProfile.StarQuest.ExtraSkillOption));

            // The revive SKILL's name carries no price any more: it lives in a catalog built before any
            // game exists, so a priced name there could only ever be one system's. The price moved to
            // the description, which is built per game - see TheReviveDescription_QuotesThisGamesCost.
            Assert.That(QuestSkill.ReviveName, Is.EqualTo("Revive a hero"));
        }

        // The revive skill's description is what the Actions panel shows under the button, so it has to
        // agree with the button.
        [Test]
        public void TheReviveDescription_QuotesThisGamesCost()
        {
            QuestSkill? revive = QuestSkill.Find(QuestSkill.ReviveName);

            Assert.That(revive, Is.Not.Null, "the skill is looked up by name, so a renamed constant must "
                + "still resolve - this is the test that catches a half-finished rename.");

            // The stored text is a template; nothing shows it raw.
            Assert.That(revive!.Description, Does.Contain("{power}"),
                "the price is filled in per game by DescriptionIn");

            foreach ((EGameSystem system, string expected) in new[]
                     {
                         (EGameSystem.GFStarQuest, "3 power"),
                         (EGameSystem.AoFQuest, "3 power"),
                     })
            {
                string shown = revive.DescriptionIn(ContextFor(system));
                Assert.That(shown, Does.Contain(expected), $"{system}: the Actions panel would show the "
                    + "wrong price under the button");
                Assert.That(shown, Does.Not.Contain("{power}"));
            }
        }

        /// <summary>A context whose only job is to carry a game system, for the per-system prices.</summary>
        private static TestGameContext ContextFor(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.GameMode = EGameMode.StarQuestHorde;
            return new TestGameContext(GameDataStore.GameDataStoreBuilder.GetDefault(),
                new FixedDiceRoller(4), settings: settings);
        }
    }
}
