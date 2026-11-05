using FDG.ArmyBuilding;
using FDG.Quest;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - the party is a Star Quest army, and it reads Star Quest's books.
    //
    // This fixture exists because #412 changed one side of a boundary and not the other, and the result
    // was that no Star Quest game could be started from the GUI at all.
    //
    // #412 gave Star Quest its own army source: a Star Quest lobby takes GFStarQuest armies and refuses
    // everything else, which is what stops a mission being priced off Grimdark Future's book. But
    // QuestArmy.Build - the thing that turns the hero party into a playable army - still stamped the party
    // "grimdark-future". So the party failed its own lobby's filter, the LAUNCH button went grey, and the
    // message blamed the player's army: "Grimdark Future army, but this lobby takes GF: Star Quest armies
    // only."
    //
    // GameSystemArmySourceTests passed throughout, because it asserts GameSystemInfo and never builds a
    // party. That is the shape of the fault worth pinning: two correct halves that disagree at the seam.
    // So these tests assert the SEAM - the army QuestArmy actually produces, run through the same
    // GameSystems.IsAllowed the launch gate uses - rather than either half on its own.
    [TestFixture]
    public class QuestArmyGameSystemTests
    {
        private static QuestHero Hero(string name, string faction = "Test") => new QuestHero
        {
            Name = name, Faction = faction, BaseHeroName = "Leader", BaseCost = 60, Class = "Soldier",
            Quality = 4, Defense = 4, Tough = 6, Power = 4, Strength = 4,
        };

        private static BuiltArmyFile Party(params string[] names) =>
            QuestArmy.Build(names.Select(n => Hero(n)).ToList(), _ => null);

        [Test]
        public void AParty_IsAStarQuestArmy()
        {
            Assert.That(Party("Vex", "Mara").GameSystem, Is.EqualTo(GameSystems.GFStarQuest),
                "the party plays in Star Quest, so it is a Star Quest army");
        }

        [Test]
        public void AParty_PassesAStarQuestLobbysFilter()
        {
            // The actual regression, stated the way the launch gate asks it. Without this, every GUI
            // Star Quest game is unstartable and the only clue is a message blaming the player.
            BuiltArmyFile party = Party("Vex", "Mara");

            Assert.That(GameSystems.IsAllowed(
                    GameSystemInfo.ArmySource(EGameSystem.GFStarQuest), party.GameSystem),
                Is.True, "a Star Quest party must be allowed into a Star Quest lobby");
        }

        [Test]
        public void AParty_IsRefusedByEveryOtherLobby()
        {
            // The negative half: stamping the party with a system must not make it play anywhere. A
            // party in a Grimdark Future lobby would be 4 heroes against a 2000pt army.
            BuiltArmyFile party = Party("Vex", "Mara");

            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                if (GameSystemInfo.IsQuest(system)) continue;
                Assert.That(GameSystems.IsAllowed(GameSystemInfo.ArmySource(system), party.GameSystem),
                    Is.False, $"a Star Quest party must not be playable in a {system} lobby");
            }
        }

        // ── Which book a hero's rules come from ──────────────────────────────────────────────────────

        private static BookFile Book(string faction, string? system) =>
            new BookFile { Name = faction, Faction = faction, GameSystem = system };

        [Test]
        public void AHerosRules_ComeFromTheStarQuestEditionOfItsFaction()
        {
            // The library holds the same faction once per system and the old lookup took
            // FirstOrDefault, i.e. whichever edition the scan reached first. The same named rule can be
            // worded differently per system, so this is the rules half of the same fault that the points
            // half of #412 fixed. Grimdark Future is deliberately listed FIRST here, because that is the
            // order that made the old code wrong.
            var library = new[]
            {
                Book("Alien Hives", GameSystems.GrimdarkFuture),
                Book("Alien Hives", GameSystems.GFFirefight),
                Book("Alien Hives", GameSystems.GFStarQuest),
            };

            BookFile? picked = QuestArmy.BookForHeroFaction(library, "Alien Hives");

            Assert.That(picked, Is.Not.Null);
            Assert.That(picked!.GameSystem, Is.EqualTo(GameSystems.GFStarQuest));
        }

        [Test]
        public void AFactionStarQuestDoesNotPublish_StillFindsItsBook()
        {
            // The fallback is deliberate and is NOT the bug above: a definition from the parent system
            // beats no definition at all, and the hero's own stats come from the hero dataset either way.
            // Without this, a hero of a faction Star Quest has no edition of would silently lose every
            // faction rule it carries.
            var library = new[] { Book("Human Inquisition", GameSystems.GrimdarkFuture) };

            Assert.That(QuestArmy.BookForHeroFaction(library, "Human Inquisition")?.GameSystem,
                Is.EqualTo(GameSystems.GrimdarkFuture));
        }

        [Test]
        public void AFactionNobodyPublishes_IsNull()
        {
            Assert.That(QuestArmy.BookForHeroFaction(
                new[] { Book("Alien Hives", GameSystems.GFStarQuest) }, "Nobody At All"), Is.Null);
        }

        [Test]
        public void ARenamedFaction_StillResolves_AndStillPrefersStarQuest()
        {
            // #412 taught the lookup OPR's faction renames (BookFile.PreviousNames). Narrowing it to the
            // Star Quest edition must not quietly undo that, so both behaviours are asserted together.
            var sq = Book("Havoc Brothers", GameSystems.GFStarQuest);
            sq.PreviousNames = new List<string> { "Chaos Brothers" };
            var gf = Book("Havoc Brothers", GameSystems.GrimdarkFuture);
            gf.PreviousNames = new List<string> { "Chaos Brothers" };

            BookFile? picked = QuestArmy.BookForHeroFaction(new[] { gf, sq }, "Chaos Brothers");

            Assert.That(picked, Is.Not.Null, "a hero saved under the old faction name must still resolve");
            Assert.That(picked!.GameSystem, Is.EqualTo(GameSystems.GFStarQuest));
        }

        [Test]
        public void BothQuestSystemsParties_AreRecognisedAsParties()
        {
            Assert.That(QuestArmy.IsPartyFaction(QuestArmy.PartyFactionFor(EGameSystem.GFStarQuest)), Is.True);
            Assert.That(QuestArmy.IsPartyFaction(QuestArmy.PartyFactionFor(EGameSystem.AoFQuest)), Is.True);
            Assert.That(QuestArmy.IsPartyFaction("Orcs"), Is.False);
        }
    }
}
