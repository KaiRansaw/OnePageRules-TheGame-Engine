using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Quest;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #418 - the two Quest systems' item vocabularies.
    ///
    /// <para>
    /// OPR publishes one Quest ruleset and dresses it twice. The two Campaign Builders' Hidden Treasure
    /// tables are the same table roll for roll, with fantasy names in AoF: Quest and sci-fi names in
    /// Star Quest. The engine had only Star Quest's, so until this existed an <b>AoF: Quest hero found
    /// Med Kits and credits in a fantasy dungeon</b> - wrong data in play, of the exact kind #417 and
    /// #418 exist to stamp out, and invisible unless you knew both books.
    /// </para>
    /// <para>
    /// What is pinned here is the mapping and the fact that neither system shows the other's words. The
    /// item EFFECTS are shared on purpose: neither book prints them, so each AoF name is wired to the
    /// effect of the Star Quest item in the same table slot, and that inference is written down in
    /// <see cref="QuestItemSkin"/> rather than hidden.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestItemSkinTests
    {
        // Read from AoFQ - Campaign Builder v2.1.0, Hidden Treasure, against GFSQ's same table.
        private static readonly (string Canonical, string AoF)[] Pairs =
        {
            ("Courage Injector", "Heroic Elixir"),
            ("Frenzy Injector", "Conquest Elixir"),
            ("Power Injector", "Power Elixir"),
            ("Noise Grenade", "Noise Bomb"),
            ("Grease Grenade", "Grease Bomb"),
            ("Stun Grenade", "Stunning Bomb"),
            ("Curing Kit", "Curing Potion"),
            ("Teleport Kit", "Teleport Potion"),
            ("Med Kit", "Health Potion"),
            ("Injector Pouch", "Elixir Pouch"),
            ("Grenade Bag", "Bomb Bag"),
            ("Kit Box", "Potion Box"),
            ("Credits Stash", "Gold Stash"),
        };

        [Test]
        public void EachNamePair_IsTheOneFromTheBooks()
        {
            foreach ((string canonical, string aof) in Pairs)
            {
                Assert.That(QuestItemSkin.Name(EGameSystem.AoFQuest, canonical), Is.EqualTo(aof),
                    $"AoF: Quest calls '{canonical}' '{aof}'");
                Assert.That(QuestItemSkin.Name(EGameSystem.GFStarQuest, canonical), Is.EqualTo(canonical),
                    "Star Quest's names ARE the canonical ones, so nothing is translated for it");
                Assert.That(QuestItemSkin.Canonical(aof), Is.EqualTo(canonical), "and it maps back");
            }
        }

        [Test]
        public void AnItemFoundInEitherSystem_ResolvesToOneSetOfRules()
        {
            // The point of a skin rather than a second item list: a hero holds the name it found, and
            // the rules behind it are the same object. Two definitions would be two things to maintain
            // and one of them would drift.
            foreach ((string canonical, string aof) in Pairs)
            {
                QuestItemInfo? byCanonical = QuestItems.Find(canonical);
                if (byCanonical is null) continue;   // the table entries are not items

                QuestItemInfo? bySkin = QuestItems.Find(aof);
                Assert.That(bySkin, Is.SameAs(byCanonical),
                    $"'{aof}' has to resolve to '{canonical}''s rules, or an AoF hero's item does nothing");
            }
        }

        [Test]
        public void EveryTreasureItemAnAoFPartyCanFind_HasRules()
        {
            // The discriminating version of the test above: walk what the table can actually produce.
            var produced = new List<string>();
            for (int roll = 3; roll <= 5; roll++)
                produced.AddRange(Pairs.Take(9).Select(p => QuestItemSkin.Name(EGameSystem.AoFQuest, p.Canonical)));

            var ruleless = produced.Distinct().Where(n => QuestItems.Find(n) is null).ToList();

            Assert.That(ruleless, Is.Empty,
                "an item a hero can find but the engine cannot look up is an item that does nothing: "
                + string.Join(", ", ruleless));
        }

        [Test]
        public void TheTreasureTable_SpeaksTheSystemsLanguage()
        {
            // Roll by roll, both ways round. The sub-rolls inside each entry are covered by the pairs
            // above; this is the entry a player is told they found.
            Assert.That(QuestTreasure.TreasureName(3, EGameSystem.GFStarQuest), Is.EqualTo("Injector Pouch"));
            Assert.That(QuestTreasure.TreasureName(3, EGameSystem.AoFQuest), Is.EqualTo("Elixir Pouch"));
            Assert.That(QuestTreasure.TreasureName(4, EGameSystem.AoFQuest), Is.EqualTo("Bomb Bag"));
            Assert.That(QuestTreasure.TreasureName(5, EGameSystem.AoFQuest), Is.EqualTo("Potion Box"));
            Assert.That(QuestTreasure.TreasureName(6, EGameSystem.AoFQuest), Is.EqualTo("Gold Stash"));

            // The two entries both books spell the same way.
            foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
            {
                Assert.That(QuestTreasure.TreasureName(1, system), Is.EqualTo("Hazard"));
                Assert.That(QuestTreasure.TreasureName(7, system), Is.EqualTo("Treasure Trove"));
            }
        }

        [Test]
        public void NeitherSystem_EverShowsTheOthersWords()
        {
            // The negative half, and the one that would catch a half-finished skin: no AoF name may come
            // out of a Star Quest lookup, and no canonical-only name out of an AoF one.
            foreach (string aofName in QuestItemSkin.AllAoFNames)
            {
                Assert.That(QuestItemSkin.Name(EGameSystem.GFStarQuest, QuestItemSkin.Canonical(aofName)),
                    Is.Not.EqualTo(aofName), $"'{aofName}' is AoF: Quest's word");
            }

            foreach (string canonical in QuestItemSkin.AllCanonicalNames)
            {
                Assert.That(QuestItemSkin.Name(EGameSystem.AoFQuest, canonical), Is.Not.EqualTo(canonical),
                    $"'{canonical}' is Star Quest's word and AoF: Quest has its own");
            }
        }

        [Test]
        public void MoneyIsCreditsInOneSystemAndGoldInTheOther()
        {
            // "Hazard Pay - Get 5g times the chosen Difficulty level" (AoFQ) against 5c (GFSQ). The
            // letter rides on the value because that is how both books write it and how the engine
            // stores a treasure's contents.
            Assert.That(QuestItemSkin.Money(EGameSystem.GFStarQuest, 15), Is.EqualTo("15c"));
            Assert.That(QuestItemSkin.Money(EGameSystem.AoFQuest, 15), Is.EqualTo("15g"));
            Assert.That(QuestItemSkin.CurrencyWord(EGameSystem.AoFQuest), Is.EqualTo("gold"));
            Assert.That(QuestItemSkin.CurrencyWord(EGameSystem.GFStarQuest), Is.EqualTo("credits"));
        }

        [Test]
        public void BothCurrencies_ParseBackToANumber()
        {
            // QuestTreasure stores contents as plain strings and parses the money back out when a hero
            // picks it up. A gold amount that failed to parse would be found, announced and then worth
            // nothing.
            Assert.That(QuestItems.Credits("15c"), Is.EqualTo(15));
            Assert.That(QuestItems.Credits("15g"), Is.EqualTo(15));
            Assert.That(QuestItems.Credits("5g"), Is.EqualTo(5));
            Assert.That(QuestItems.Credits("10g"), Is.EqualTo(10));

            Assert.That(QuestItems.Credits("Health Potion"), Is.Null, "an item is not money");
            Assert.That(QuestItems.Credits(""), Is.Null);
        }

        [Test]
        public void AnUnknownName_IsLeftAlone()
        {
            // Custom items, and names from books that do not exist yet, must pass through rather than
            // being mangled into something the engine thinks it recognises.
            Assert.That(QuestItemSkin.Name(EGameSystem.AoFQuest, "Frag Grenade"), Is.EqualTo("Frag Grenade"));
            Assert.That(QuestItemSkin.Canonical("Vorpal Sword of Nothing"),
                Is.EqualTo("Vorpal Sword of Nothing"));
            Assert.That(QuestItemSkin.Name(EGameSystem.AoFQuest, ""), Is.Empty);
        }

        [Test]
        public void AHeroSavedBeforeThisChange_StillHoldsWorkingItems()
        {
            // Every .fdghero written before #418 holds canonical names, Star Quest's being the only
            // vocabulary there was. They must keep resolving whichever system loads them.
            foreach (string canonical in new[] { "Med Kit", "Curing Kit", "Power Injector" })
            {
                Assert.That(QuestItems.Find(canonical), Is.Not.Null, canonical);
            }
        }
    }
}
