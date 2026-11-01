using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #437 - the four places where one game's rule was being applied to another game.
    ///
    /// <para>
    /// All four came out of the 2026-11-01 rules audit, and they are one mistake in four places: a value
    /// the engine held FLAT across all six systems where the systems' own books print different numbers.
    /// `CLAUDE.md` calls that the single easiest thing here to get catastrophically wrong, and the reason
    /// it hid this long is that every one of the four fails SILENTLY - a too-generous cap never refuses
    /// anything, a prompt that should not exist still has a sensible default, and a coherency band that
    /// is too loose just lets a pack spread out.
    /// </para>
    ///
    /// <para>
    /// <b>The framing that found them</b> (Kai, 2026-11-01): each book is its own game, not a flavouring
    /// of one engine. A difference between two books is the DESIGN, not a defect. So the only valid test
    /// is "does the engine match THIS book, for THIS system?" - never "do two books disagree?" Every
    /// assertion below is written against one named book.
    /// </para>
    /// </summary>
    [TestFixture]
    public class PerSystemRuleSplitTests
    {
        private static readonly EGameSystem[] FullScale =
            { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy };

        private static readonly EGameSystem[] Skirmish =
            { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish };

        private static readonly EGameSystem[] Quest =
            { EGameSystem.GFStarQuest, EGameSystem.AoFQuest };

        private static readonly EGameSystem[] Everything =
            { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight,
              EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest, EGameSystem.AoFQuest };

        // -- Shooting targets --------------------------------------------------------------------------

        /// <summary>
        /// The four standard books: <i>"may fire at up to two different targets with different weapon
        /// types."</i> Both Quest books cap a unit at one - AoFQ v2.1.0 says so outright (<i>"a unit must
        /// pick ONE valid target and all models in the unit may shoot at it"</i>) and GFSQ v2.1.0 never
        /// grants a split (<i>"Models in range and line of sight may fire all ranged weapons."</i>).
        /// </summary>
        [Test]
        public void ShootingTargetCap_IsTwoInTheStandardGames_AndOneInBothQuestSystems()
        {
            foreach (EGameSystem system in FullScale.Concat(Skirmish))
                Assert.That(CoreRulesProfile.For(system).MaxTargetsPerShootAction, Is.EqualTo(2),
                    $"{system}: its book prints \"up to two different targets with different weapon types\".");

            foreach (EGameSystem system in Quest)
                Assert.That(CoreRulesProfile.For(system).MaxTargetsPerShootAction, Is.EqualTo(1),
                    $"{system}: a Quest unit picks ONE target and commits.");
        }

        /// <summary>
        /// The constant stays at the standard games' value, because the Unlimited Split Fire lobby toggle
        /// and the GUI's refusal message both name a number. Pinned so a future tidy-up does not
        /// "simplify" the profile back onto it.
        /// </summary>
        [Test]
        public void TheFlatConstantIsStillTheStandardGamesValue()
        {
            Assert.That(GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION, Is.EqualTo(2));
            Assert.That(CoreRulesProfile.For(EGameSystem.GrimdarkFuture).MaxTargetsPerShootAction,
                Is.EqualTo(GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION));
        }

        // -- Strike back -------------------------------------------------------------------------------

        /// <summary>
        /// The standard books: <i>"Then defenders may choose to strike back, but don't have to."</i>
        /// GFSQ v2.1.0: <i>"Then defenders must strike back"</i>. AoFQ v2.1.0: <i>"the defending unit must
        /// strike back"</i>.
        /// </summary>
        [Test]
        public void StrikingBack_IsAChoiceInTheStandardGames_AndMandatoryInBothQuestSystems()
        {
            foreach (EGameSystem system in FullScale.Concat(Skirmish))
                Assert.That(CoreRulesProfile.For(system).StrikeBackIsMandatory, Is.False,
                    $"{system}: its book says defenders MAY choose to strike back.");

            foreach (EGameSystem system in Quest)
                Assert.That(CoreRulesProfile.For(system).StrikeBackIsMandatory, Is.True,
                    $"{system}: its book says the defender MUST strike back.");
        }

        // -- Coherency ---------------------------------------------------------------------------------

        /// <summary>
        /// Three bands, each from its own book: 9" full-scale, 6" skirmish, and <b>3" for the Quest
        /// systems' AI groups</b> - GFSQ v2.1.0 <i>"within 3" of all other members"</i>, AoFQ v2.1.0
        /// <i>"must stay within 3" of all other models"</i>. The engine ran 9" in both Quest systems
        /// because the switch only knew "skirmish or not".
        /// <para>
        /// Asserted through <c>ConfigureTable</c>, the production entry point, rather than by reading the
        /// constants - a test that only compares constants to each other would pass with the switch still
        /// broken.
        /// </para>
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 9f)]
        [TestCase(EGameSystem.AgeOfFantasy, 9f)]
        [TestCase(EGameSystem.GFFirefight, 6f)]
        [TestCase(EGameSystem.AoFSkirmish, 6f)]
        [TestCase(EGameSystem.GFStarQuest, 3f)]
        [TestCase(EGameSystem.AoFQuest, 3f)]
        public void Coherency_IsItsOwnBooksBand(EGameSystem system, float expected)
        {
            try
            {
                GameWideConstants.ConfigureTable(system);
                Assert.That(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES,
                    Is.EqualTo(expected),
                    $"{system}: its own book prints {expected}\" to all other members.");
            }
            finally
            {
                // ConfigureTable is global state; leave the default behind for whatever runs next.
                GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            }
        }

        /// <summary>The 1" chain-link distance is shared by all six books and must stay flat.</summary>
        [Test]
        public void TheOneInchChainLinkIsSharedByEveryBook()
        {
            Assert.That(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES, Is.EqualTo(1f));
        }

        // -- Horde reinforce range ---------------------------------------------------------------------

        /// <summary>
        /// All four Horde Mode v3.6.0 books print the reinforce band as <b>9"</b> - including GFF and
        /// AoFS, where buying upgrades and recruiting are both 6". The engine reused the shop range, so a
        /// skirmish Horde refused a reinforcement the book allows between 6" and 9" out.
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 9f)]
        [TestCase(EGameSystem.AgeOfFantasy, 9f)]
        [TestCase(EGameSystem.GFFirefight, 6f)]
        [TestCase(EGameSystem.AoFSkirmish, 6f)]
        public void HordeShopRange_IsPerSystem_ButReinforceIsAlwaysNine(EGameSystem system, float shopRange)
        {
            try
            {
                HordeRules.Configure(system);

                Assert.That(HordeRules.ShopRangeInches, Is.EqualTo(shopRange),
                    $"{system}: upgrades and recruiting use its own band.");
                Assert.That(HordeRules.ReinforceRangeInches, Is.EqualTo(9f),
                    $"{system}: every Horde book prints 9\" for reinforcing, whatever the shop range is.");
            }
            finally
            {
                HordeRules.Configure(EGameSystem.GrimdarkFuture);
            }
        }

        /// <summary>
        /// And the thing that actually went wrong: in the skirmish games the two bands must DIFFER. A
        /// future refactor that folds reinforce back onto the shop range fails here.
        /// </summary>
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void InTheSkirmishGames_ReinforceReachesFurtherThanTheShop(EGameSystem system)
        {
            try
            {
                HordeRules.Configure(system);
                Assert.That(HordeRules.ReinforceRangeInches, Is.GreaterThan(HordeRules.ShopRangeInches),
                    $"{system}: the book scales upgrades and recruiting to 6\" and leaves reinforcing at 9\".");
            }
            finally
            {
                HordeRules.Configure(EGameSystem.GrimdarkFuture);
            }
        }

        // -- The shape of the whole thing --------------------------------------------------------------

        /// <summary>
        /// Every system resolves to a profile, including any added later. A missing arm of
        /// <c>CoreRulesProfile.For</c> falls through to the full-scale defaults, which is exactly how
        /// three of these four bugs behaved - so this is the guard for the NEXT one.
        /// </summary>
        [Test]
        public void EverySystemHasAProfile_AndNoneFallsThroughSilently()
        {
            foreach (EGameSystem system in Everything)
            {
                CoreRulesProfile profile = CoreRulesProfile.For(system);
                Assert.That(profile, Is.Not.Null, $"{system} has no core-rules profile.");
                Assert.That(profile.SystemName, Is.Not.Empty);
                Assert.That(profile.MaxTargetsPerShootAction, Is.InRange(1, 2),
                    $"{system}: a shoot-target cap outside 1-2 is not in any book.");
            }
        }

        /// <summary>
        /// The two Quest systems share their special-rule READINGS (their numbers live in
        /// <c>QuestRulesProfile</c>), so a change that splits one from the other here is either a real
        /// book difference worth a comment or a mistake. Pinned to force that decision.
        /// </summary>
        [Test]
        public void BothQuestSystemsShareTheirReadings()
        {
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFQuest),
                Is.EqualTo(CoreRulesProfile.For(EGameSystem.GFStarQuest)),
                "GFSQ v2.1.0 and AoFQ v2.1.0 agree on every reading this profile holds; if that stops "
                + "being true, the differing one needs its own profile and a quote to justify it.");
        }
    }
}
