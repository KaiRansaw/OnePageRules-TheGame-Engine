using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Quest;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #418 - the 36 random events, named per Quest system.
    ///
    /// <para>
    /// The two Campaign Builders' event tables are one table: same codes, same effects, different
    /// flavour. 21 of the 36 are renamed. The engine carried Star Quest's names only, so an AoF: Quest
    /// party was being told about Solar Flares and Robot Guides - nothing broken, every effect correct,
    /// and wrong in the way that only shows if you have both books open.
    /// </para>
    /// <para>
    /// The pairs below are typed from <c>AoFQ - Beginners Guide v2.1.0</c> / <c>Campaign Builder
    /// v2.1.0</c> against the Star Quest names already in <see cref="QuestChallenges.EventNames"/>, so
    /// this fixture is where someone checks the data without opening a PDF.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestEventNameTests
    {
        /// <summary>code -> (Star Quest, AoF: Quest), for every event whose flavour differs.</summary>
        private static readonly (int Code, string StarQuest, string AoF)[] Renamed =
        {
            (13, "Debilitating Static", "Debilitating Curse"),
            (21, "Explosive Trap", "Magic Trap"),
            (22, "Alien Shriek", "Eldritch Shriek"),
            (23, "Anomalous Gale", "Hexed Gale"),
            (32, "Solar Flare", "Melting Curse"),
            (41, "Psychic Pool", "Magical Pool"),
            (42, "Robot Guide", "Animal Guide"),
            (43, "Treasure Holopad", "Treasure Map"),
            (44, "Spotter Drone", "Spotter Bird"),
            (45, "Refreshment Kit", "Refreshing Grubs"),
            (51, "Psychic Fog", "Mystic Fog"),
            (52, "Haste Stims", "Haste Berries"),
            (53, "Invigorating Fumes", "Weapon Enchantment"),
            (54, "Support Robot", "Supporting Spirit"),
            (55, "Enhancing Force-Field", "Enchanted Winds"),
            (56, "Shield-Projector Drone", "Guardian Spirit"),
            (61, "Injector Pouch", "Elixir Pouch"),
            (63, "Grenade Bag", "Bomb Bag"),
            (64, "Credits Stash", "Gold Stash"),
            (65, "Kit Box", "Potion Box"),
            (66, "Godly Blessing", "Godly Sunrays"),
        };

        /// <summary>The events both books spell identically - asserted so a future "tidy-up" cannot
        /// quietly reskin one of them into something no book says.</summary>
        private static readonly int[] Shared =
        {
            11, 12, 14, 15, 16, 24, 25, 26, 31, 33, 34, 35, 36, 46, 62,
        };

        [Test]
        public void EveryRenamedEvent_IsTheNameFromItsBook()
        {
            foreach ((int code, string sq, string aof) in Renamed)
            {
                Assert.That(QuestChallenges.EventName(code, EGameSystem.GFStarQuest), Is.EqualTo(sq),
                    $"event {code} in Star Quest");
                Assert.That(QuestChallenges.EventName(code, EGameSystem.AoFQuest), Is.EqualTo(aof),
                    $"event {code} in AoF: Quest");
            }
        }

        [Test]
        public void TheSharedEvents_ReadTheSameInBothBooks()
        {
            foreach (int code in Shared)
            {
                string sq = QuestChallenges.EventName(code, EGameSystem.GFStarQuest);
                Assert.That(QuestChallenges.EventName(code, EGameSystem.AoFQuest), Is.EqualTo(sq),
                    $"event {code} is word for word the same in both books");
            }
        }

        [Test]
        public void EveryEventCode_HasAName_InBothSystems()
        {
            // The whole table, both ways: a code with no name reads as "Event 43" to the player, which
            // is the shape of a half-finished skin.
            foreach (int code in QuestChallenges.EventNames.Keys)
                foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
                {
                    string name = QuestChallenges.EventName(code, system);
                    Assert.That(name, Is.Not.Empty);
                    Assert.That(name, Does.Not.StartWith("Event "), $"event {code} in {system}");
                }

            Assert.That(QuestChallenges.EventNames, Has.Count.EqualTo(36), "two dice, 11-66");
            Assert.That(Renamed.Length + Shared.Length, Is.EqualTo(36),
                "every event is either renamed by AoF: Quest or shared - this fixture must cover all 36");
        }

        [Test]
        public void TheRenamedSetIsExactlyWhatTheSkinClaims()
        {
            // The skin owns 17 names itself and defers the four treasure-shaped ones to QuestItemSkin,
            // so "renamed" is split across two files. This proves the union is the 21 above and nothing
            // has been added in one place and forgotten in the other.
            var skinned = QuestEventNames.RenamedCodes.ToHashSet();
            var treasureShaped = new[] { 61, 63, 64, 65 };
            var expected = Renamed.Select(r => r.Code).ToHashSet();

            Assert.That(skinned.Concat(treasureShaped).ToHashSet(), Is.EquivalentTo(expected));
            Assert.That(skinned.Intersect(treasureShaped), Is.Empty,
                "a treasure entry's name must have ONE owner (QuestItemSkin), not two");
        }

        [Test]
        public void NoAoFEventName_StillReadsAsScienceFiction()
        {
            // The sweep that would have caught this in the first place: no AoF: Quest event may carry a
            // word from the sci-fi table. Deliberately a word list rather than a name list, because the
            // failure mode is a NEW event being added with Star Quest's flavour.
            string[] sciFi = { "Robot", "Drone", "Holopad", "Stims", "Force-Field", "Alien", "Solar",
                "Psychic", "Credits", "Injector", "Grenade", "Kit" };

            var leaked = new List<string>();
            foreach (int code in QuestChallenges.EventNames.Keys)
            {
                string name = QuestChallenges.EventName(code, EGameSystem.AoFQuest);
                leaked.AddRange(sciFi.Where(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))
                    .Select(w => $"{code} '{name}' contains '{w}'"));
            }

            Assert.That(leaked, Is.Empty, string.Join("; ", leaked));
        }

        [Test]
        public void TheEventsThatHandOverSomething_NameWhatThisSystemCallsIt()
        {
            // The text under the event, which used to promise a Med Kit to a wizard. The grant itself
            // was skinned first; this is the sentence that describes it, and the two must agree or the
            // log contradicts the hero's pack.
            Assert.That(QuestChallenges.EventTextFor(61, EGameSystem.GFStarQuest),
                Is.EqualTo("gets a Power Injector"));
            Assert.That(QuestChallenges.EventTextFor(61, EGameSystem.AoFQuest),
                Is.EqualTo("gets a Power Elixir"));

            Assert.That(QuestChallenges.EventTextFor(63, EGameSystem.AoFQuest),
                Is.EqualTo("gets a Stunning Bomb"));
            Assert.That(QuestChallenges.EventTextFor(65, EGameSystem.AoFQuest),
                Is.EqualTo("gets a Health Potion"));

            Assert.That(QuestChallenges.EventTextFor(64, EGameSystem.GFStarQuest), Is.EqualTo("gets 15c"));
            Assert.That(QuestChallenges.EventTextFor(64, EGameSystem.AoFQuest), Is.EqualTo("gets 15g"));
        }

        [Test]
        public void EveryEventText_IsFullyResolved()
        {
            // A placeholder that reaches a player is worse than a wrong name: "gets a {item:Med Kit}".
            foreach (int code in QuestChallenges.EventNames.Keys)
                foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
                {
                    string text = QuestChallenges.EventTextFor(code, system);
                    Assert.That(text, Is.Not.Empty, $"event {code} in {system} has no effect text");
                    Assert.That(text, Does.Not.Contain("{"), $"event {code} in {system}: {text}");
                    Assert.That(text, Does.Not.Contain("}"), $"event {code} in {system}: {text}");
                }
        }

        [Test]
        public void AnUnknownPlaceholder_ReadsAsItselfRatherThanVanishing()
        {
            // Defensive, and the reason Fill is a parser rather than a string.Replace chain: a typo in a
            // future event should be visible in the log, not silently delete the sentence.
            Assert.That(QuestEventNames.Fill("gets a {whatsit}", EGameSystem.AoFQuest),
                Is.EqualTo("gets a whatsit"));
            Assert.That(QuestEventNames.Fill("plain text", EGameSystem.AoFQuest), Is.EqualTo("plain text"));
            Assert.That(QuestEventNames.Fill("", EGameSystem.AoFQuest), Is.Empty);
        }

        [Test]
        public void TheGameMastersEventList_IsTheWholeTable_NamedForThisGame()
        {
            IReadOnlyList<KeyValuePair<int, string>> sq = QuestChallenges.EventsFor(EGameSystem.GFStarQuest);
            IReadOnlyList<KeyValuePair<int, string>> aof = QuestChallenges.EventsFor(EGameSystem.AoFQuest);

            Assert.That(sq, Has.Count.EqualTo(36));
            Assert.That(aof.Select(e => e.Key), Is.EqualTo(sq.Select(e => e.Key)).AsCollection,
                "same codes in the same order - only the names differ");
            Assert.That(aof.Count(e => e.Value != sq.First(s => s.Key == e.Key).Value),
                Is.EqualTo(Renamed.Length), "and exactly the renamed ones differ");
        }
    }
}
