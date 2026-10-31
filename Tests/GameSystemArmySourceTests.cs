using FDG.ArmyBuilding;
using NUnit.Framework;

namespace FDG.Tests
{
    // #412 - which game system's army books each game type is played with.
    //
    // This is the single easiest thing in the project to get catastrophically wrong, because getting it
    // wrong produces a game that works. OPR publishes the same faction once per system, and the versions
    // differ in unit names, unit sizes, points and which units exist at all - Alien Hives' Grunt Veteran
    // is 40pts in Grimdark Future, 100pts in Star Quest and 55pts as a playable Star Quest hero; its
    // Assault Grunt(s) are 10 models for 90pts in Grimdark Future and 1 model for 30pts in Star Quest.
    // Serve one system's book to another and every army still compiles, loads, deploys and plays. The
    // only symptom is that the points buy the wrong amount of army, which nobody can see.
    //
    // Star Quest is the case that was actually wrong: #406 could not confirm whether it published its own
    // enemy book, so it defaulted to Grimdark Future's, and every wave and mission was priced off a book
    // whose numbers are 2.5x out. It does publish one - 54 of them - and #412 imported them.
    //
    // So each mapping gets an assertion naming WHY, and the Star Quest ones assert the negative too: a
    // fixture that only checks "Star Quest takes Star Quest armies" would still pass if someone widened
    // the lobby to accept everything.
    [TestFixture]
    public class GameSystemArmySourceTests
    {
        [TestCase(EGameSystem.GrimdarkFuture, EAllowedGameSystems.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy, EAllowedGameSystems.AgeOfFantasy)]
        [TestCase(EGameSystem.GFFirefight, EAllowedGameSystems.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish, EAllowedGameSystems.AoFSkirmish)]
        [TestCase(EGameSystem.GFStarQuest, EAllowedGameSystems.GFStarQuest)]
        public void EachGameSystem_TakesItsOwnArmies(EGameSystem system, EAllowedGameSystems expected)
        {
            Assert.That(GameSystemInfo.ArmySource(system), Is.EqualTo(expected),
                $"{GameSystemInfo.DisplayName(system)} must be played with its own system's army books - "
                + "another system's are not an approximation, they are different numbers");
        }

        [Test]
        public void StarQuest_DoesNotTakeGrimdarkFutureArmies()
        {
            // The actual bug, stated as what must not happen. A Grimdark Future army in a Star Quest game
            // is priced off a book whose Grunt Veteran costs 40 where Star Quest's costs 100, so a wave
            // budget buys about two and a half times the enemy it should.
            Assert.That(GameSystemInfo.ArmySource(EGameSystem.GFStarQuest),
                Is.Not.EqualTo(EAllowedGameSystems.GrimdarkFuture));

            Assert.That(GameSystems.IsAllowed(
                    GameSystemInfo.ArmySource(EGameSystem.GFStarQuest), GameSystems.GrimdarkFuture),
                Is.False, "a Grimdark Future army must not pass a Star Quest lobby's filter");
            Assert.That(GameSystems.IsAllowed(
                    GameSystemInfo.ArmySource(EGameSystem.GFStarQuest), GameSystems.GFStarQuest),
                Is.True, "and a Star Quest army must");
        }

        [Test]
        public void AnArmyWithNoSystemField_IsStillOnlyGrimdarkFutures()
        {
            // Absent-means-GDF (#378) is what keeps every army saved before the field existed working.
            // It must NOT make such an army acceptable everywhere: a pre-#378 list is a Grimdark Future
            // list, and letting it into a Star Quest lobby would reintroduce the mispricing above through
            // the back door.
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.GrimdarkFuture, null), Is.True);
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.GFStarQuest, null), Is.False);
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.GFFirefight, null), Is.False);
        }

        [Test]
        public void AoFQuest_StillFallsBackToAgeOfFantasy_AndIsNotSelectable()
        {
            // A known gap, pinned so it cannot be forgotten rather than left as a comment: AoF: Quest's
            // own enemy books exist at Army Forge (id 8, 48 of them) and are not imported, so it falls
            // back the way Star Quest used to. That is harmless ONLY while the lobby cannot pick it - so
            // this asserts both halves together, and making it selectable without importing its books
            // fails here.
            Assert.That(GameSystemInfo.ArmySource(EGameSystem.AoFQuest),
                Is.EqualTo(EAllowedGameSystems.AgeOfFantasy));
            Assert.That(GameSystemInfo.IsAvailable(EGameSystem.AoFQuest), Is.False,
                "AoF: Quest must not become selectable while it would be played on Age of Fantasy's "
                + "points - import its own books (Army Forge id 8) and move ArmySource with it");
        }

        [Test]
        public void EveryDisplayedSystem_HasAnArmySourceAndAName()
        {
            // The lobby lists every system in DisplayOrder, including the ones that are greyed out. A
            // member added to the enum but not to these two switches reads as its own ToString() and
            // silently falls through to Grimdark Future's armies - the exact shape of the Star Quest bug.
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                Assert.That(GameSystemInfo.DisplayName(system), Is.Not.EqualTo(system.ToString()),
                    $"{system} has no display name - add it to GameSystemInfo.DisplayName");

                EAllowedGameSystems source = GameSystemInfo.ArmySource(system);
                if (source == EAllowedGameSystems.GrimdarkFuture
                    && system != EGameSystem.GrimdarkFuture)
                {
                    Assert.That(system, Is.EqualTo(EGameSystem.GrimdarkFuture),
                        $"{system} falls through to Grimdark Future's armies. If that is deliberate "
                        + "(its own books are not imported yet) say so in ArmySource and add it to "
                        + nameof(AoFQuest_StillFallsBackToAgeOfFantasy_AndIsNotSelectable)
                        + "; otherwise it is being played on the wrong points.");
                }
            }
        }
    }
}
