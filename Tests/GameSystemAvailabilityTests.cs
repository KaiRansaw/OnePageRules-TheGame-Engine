using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - every game system the game carries data for must be selectable in the lobby.
    ///
    /// <para>
    /// <b>AoF: Skirmish was greyed out for weeks and nothing was blocking it.</b> It had its own 46 army
    /// books, its slug, army folder, force-org profile, CLI alias and table/coherency rules, it shared
    /// <see cref="CoreRulesProfile.Skirmish"/> with Firefight (which was selectable the whole time), and
    /// it played full headless games throughout. <c>GameSystemInfo.IsAvailable</c> was a hand-written
    /// list of members that simply never got updated when its books landed.
    /// </para>
    /// <para>
    /// That is the shape of fault this fixture exists for: nothing failed, nothing was logged, and the
    /// only symptom was a menu entry a player could not click. A hand-maintained list of systems is a
    /// blind spot with a date on it - the same lesson the project already learned from the army
    /// builder's hardcoded two-system dropdown, which hid 156 imported books.
    /// </para>
    /// </summary>
    [TestFixture]
    public class GameSystemAvailabilityTests
    {
        /// <summary>
        /// The headline invariant. If a system is listed for the player to see, the player can pick it;
        /// a system that is not ready yet should not be in the display order at all.
        /// </summary>
        [Test]
        public void EverySystemTheLobbyLists_CanBeSelected()
        {
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                Assert.That(GameSystemInfo.IsAvailable(system), Is.True,
                    $"{GameSystemInfo.DisplayName(system)} is shown in the lobby but cannot be picked. "
                    + "Either finish it or take it out of DisplayOrder - a listed-but-unclickable mode "
                    + "reads as a broken game, not as a roadmap.");
            }
        }

        /// <summary>
        /// Named explicitly, because this is the one that was wrong and a regression here is invisible.
        /// </summary>
        [Test]
        public void AoFSkirmishIsSelectable()
        {
            Assert.That(GameSystemInfo.IsAvailable(EGameSystem.AoFSkirmish), Is.True,
                "AoF: Skirmish has its own books, rules profile and army folder, and plays headlessly. "
                + "It was left out of the availability list by oversight (#420).");
        }

        /// <summary>
        /// Everything a selectable system needs in order to be more than a menu entry. Each of these was
        /// already true for Skirmish - which is the point: the gate was the only thing missing, and
        /// asserting the rest keeps a FUTURE system from being unlocked before it is ready.
        /// </summary>
        [Test]
        public void EverySelectableSystemHasTheDataToPlay()
        {
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                string label = GameSystemInfo.DisplayName(system);

                Assert.That(GameSystemInfo.Slug(system), Is.Not.Null.And.Not.Empty,
                    $"{label} has no Army Forge slug, so nothing can key its data.");
                Assert.That(GameSystems.DisplayName(GameSystemInfo.Slug(system)), Is.Not.Empty,
                    $"{label}'s slug is not one GameSystems recognises.");
                Assert.That(GameSystemInfo.Modes(system), Is.Not.Empty,
                    $"{label} offers no game modes.");
                Assert.That(CoreRulesProfile.For(system), Is.Not.Null,
                    $"{label} has no core-rules profile, so it would play by another system's wording.");
                Assert.That(OfficialRuleText.Text("Tough", GameSystemInfo.Slug(system)),
                    Is.Not.Null.And.Not.Empty,
                    $"{label} has no official rule text, so every tooltip would fall back to a summary.");
            }
        }

        /// <summary>
        /// The six are distinct systems, not aliases: no two may share a slug, or a book written for one
        /// would silently load into the other. This is the invariant CLAUDE.md calls "the single easiest
        /// thing to get catastrophically wrong".
        /// </summary>
        [Test]
        public void NoTwoSystemsShareASlug()
        {
            string[] slugs = GameSystemInfo.DisplayOrder.Select(GameSystemInfo.Slug).ToArray();

            Assert.That(slugs, Is.Unique,
                "two systems resolving to one slug would serve one mode's book to another - wrong data, "
                + "not an approximation.");
        }
    }
}
