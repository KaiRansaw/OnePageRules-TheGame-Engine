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
        [TestCase(EGameSystem.AoFQuest, EAllowedGameSystems.AoFQuest)]
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
        public void AoFQuest_TakesItsOwnBooks_AndIsStillNotSelectable()
        {
            // #417 closed the half of this that was dangerous: AoF: Quest's own enemy books are imported
            // (Army Forge id 8, 46 books) and ArmySource points at them, so the data is right. What is
            // still missing is the PLAYER side - AoF: Quest heroes are Army Forge id 7, a separate
            // dataset with different stats for the same unit (Beastmen's Ndoli Beast Lord is 45pts Q6 D6
            // as a hero against the enemy book's Ndoli Warrior at 35pts Q4 D4), and there is no AoF
            // Hero Builder yet. So the system stays unselectable, and this pins WHY: without a party
            // there is nothing to play, not that the enemy points are wrong.
            Assert.That(GameSystemInfo.ArmySource(EGameSystem.AoFQuest),
                Is.EqualTo(EAllowedGameSystems.AoFQuest),
                "AoF: Quest's enemy side must come from its own books, not Age of Fantasy's - the Quest "
                + "scale is about 3x (Beastmen's Hakemba is 40pts in AoF and 110pts here)");
            Assert.That(GameSystemInfo.IsAvailable(EGameSystem.AoFQuest), Is.False,
                "AoF: Quest cannot be picked until its hero roster (Army Forge id 7) is imported and the "
                + "Hero Builder can build an AoF party - the enemy books alone are half a game");
        }

        [Test]
        public void TheTwoQuestSystems_AreNeverTheirParentSystem()
        {
            // The whole class of bug this fixture exists for, asserted as a rule rather than per system:
            // a Quest game played on its parent's book buys the wrong amount of enemy at every points
            // value, and nothing visible goes wrong.
            Assert.That(GameSystemInfo.ArmySource(EGameSystem.GFStarQuest),
                Is.Not.EqualTo(EAllowedGameSystems.GrimdarkFuture));
            Assert.That(GameSystemInfo.ArmySource(EGameSystem.AoFQuest),
                Is.Not.EqualTo(EAllowedGameSystems.AgeOfFantasy));

            // And neither Quest system's books may be let into its parent's lobby, or the mispricing
            // simply runs the other way.
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.AgeOfFantasy, GameSystems.AoFQuest),
                Is.False);
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.AoFQuest, GameSystems.AgeOfFantasy),
                Is.False);
            Assert.That(GameSystems.IsAllowed(EAllowedGameSystems.AoFQuest, null), Is.False,
                "an army with no system field is a Grimdark Future list (#378), never an AoF: Quest one");
        }

        [Test]
        public void BothQuestSystems_OfferTheQuestModes()
        {
            // The mode enum members are named for Star Quest because it got here first; they are the
            // shared Quest modes. An AoF: Quest game that offered Standard/Horde would be playing the
            // ordinary mission rules with a party of heroes.
            Assert.That(GameSystemInfo.Modes(EGameSystem.AoFQuest),
                Is.EqualTo(GameSystemInfo.Modes(EGameSystem.GFStarQuest)));
            Assert.That(GameSystemInfo.IsQuest(EGameSystem.AoFQuest), Is.True);
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
                        + nameof(AoFQuest_TakesItsOwnBooks_AndIsStillNotSelectable)
                        + "; otherwise it is being played on the wrong points.");
                }
            }
        }
    }
}
