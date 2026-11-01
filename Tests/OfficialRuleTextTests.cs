using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - the shipped official rule text, and the rule that it is what a player reads.
    ///
    /// <para>
    /// The fault this guards against is not a crash. It is a convincing sentence in front of the wrong
    /// behaviour: 101 authored summaries, 36 of the 38 checkable ones disagreeing with OPR's own text,
    /// and two of those disagreements turning out to be missing clauses in the implementation (Impact's
    /// and Ravage's "unless this unit is Fatigued"). Nothing failed, because nothing compared the two.
    /// These tests are that comparison.
    /// </para>
    /// </summary>
    [TestFixture]
    public class OfficialRuleTextTests
    {
        [TearDown]
        public void TearDown() => OfficialRuleText.OverrideForTests(null);

        /// <summary>Every system the lobby can start a game as needs text, or that game shows summaries.</summary>
        [Test]
        public void EverySystemTheGameCanPlayHasText()
        {
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                string slug = GameSystemInfo.Slug(system);
                Assert.That(OfficialRuleText.Text("Tough", slug), Is.Not.Null.And.Not.Empty,
                    $"{GameSystemInfo.DisplayName(system)} ({slug}) has no official text - every system "
                    + "the lobby offers must be in the shipped asset, including the two Quest systems "
                    + "whose two Army Forge ids are merged onto one slug.");
            }
        }

        /// <summary>
        /// The rule the owner reported, 2026-10-31, from the in-game Army Builder. The authored summary
        /// read "When attacking in melee, rolls dice that deal automatic unsaveable wounds before
        /// swings" - which drops "once per round", drops "unless this unit is Fatigued" (a clause the
        /// implementation was also missing), and invents "unsaveable", a word the book does not use.
        /// </summary>
        [Test]
        public void Ravage_ReadsExactlyAsTheBookPrintsIt()
        {
            const string Book = "Once per round, when it's this model's turn to strike in melee, roll X "
                + "dice before attacking, unless this unit is Fatigued. For each 6+ the target takes one "
                + "wound.";

            Assert.That(OfficialRuleText.Text("Ravage", GameSystems.AgeOfFantasy), Is.EqualTo(Book));
            Assert.That(OfficialRuleText.Display(CoreRuleCatalog.Ravage, GameSystems.AgeOfFantasy),
                Is.EqualTo(Book),
                "Display() is what the UI calls, so it must return the book's text and not the "
                + "definition's authored Description.");
        }

        /// <summary>
        /// The whole reason this is keyed per system. These three are not editing differences - they
        /// change what a player does at the table, and <c>CoreRuleCatalog</c> carries one shared
        /// definition written to Grimdark Future's wording for all of them.
        /// </summary>
        [Test]
        public void StarQuestRewordsRulesAndKeepsItsOwnText()
        {
            foreach (string rule in new[] { "Fear", "Blast", "Tough", "Scout", "Flying", "Strider" })
            {
                string? gf = OfficialRuleText.Text(rule, GameSystems.GrimdarkFuture);
                string? sq = OfficialRuleText.Text(rule, GameSystems.GFStarQuest);

                Assert.That(gf, Is.Not.Null, $"{rule} missing for Grimdark Future");
                Assert.That(sq, Is.Not.Null, $"{rule} missing for Star Quest");
                Assert.That(sq, Is.Not.EqualTo(gf),
                    $"{rule} is reworded in Star Quest's book, so one shared string would show a Star "
                    + "Quest player Grimdark Future's version. If OPR has genuinely aligned them, "
                    + "re-harvest and drop this rule from the list rather than deleting the test.");
            }
        }

        /// <summary>Star Quest's Fear, specifically: a different sentence that changes what you check.</summary>
        [Test]
        public void StarQuestFearPointsAtTheMoraleTest_NotWhoWonTheMelee()
        {
            Assert.That(OfficialRuleText.Text("Fear", GameSystems.GrimdarkFuture),
                Does.Contain("checking who won"));
            Assert.That(OfficialRuleText.Text("Fear", GameSystems.GFStarQuest),
                Does.Contain("Morale Test"));
        }

        /// <summary>
        /// The ImGui font atlas bakes Basic Latin + Latin-1 only, and OPR's text is full of typographic
        /// quotes and dashes - every 9" in the corpus arrives as a 9 followed by U+201D. Unfolded, the
        /// whole ruleset would render with '?' where the measurements are.
        /// </summary>
        [Test]
        public void AllShippedTextIsAscii()
        {
            foreach (EGameSystem system in GameSystemInfo.DisplayOrder)
            {
                string slug = GameSystemInfo.Slug(system);
                foreach (string rule in new[] { "Tough", "Scout", "AP", "Blast", "Impact", "Fear" })
                {
                    string? text = OfficialRuleText.Text(rule, slug);
                    if (text == null) continue;
                    foreach (char c in text)
                        Assert.That(c, Is.LessThan((char)128),
                            $"{slug}/{rule} carries U+{(int)c:X4}, which the font atlas renders as '?'.");
                }
            }
        }

        /// <summary>
        /// A rule a system's own data never mentions falls back to its parent rather than going blank -
        /// but only then. A system that HAS its own entry must never be given its parent's.
        /// </summary>
        [Test]
        public void FallbackFiresOnlyWhenTheSystemHasNoTextOfItsOwn()
        {
            OfficialRuleText.OverrideForTests(new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase)
            {
                [GameSystems.GrimdarkFuture] = new() { ["Shared"] = "parent text", ["Both"] = "parent's own" },
                [GameSystems.GFFirefight] = new() { ["Both"] = "firefight's own" },
            });

            Assert.Multiple(() =>
            {
                Assert.That(OfficialRuleText.Text("Shared", GameSystems.GFFirefight), Is.EqualTo("parent text"),
                    "Firefight has no entry of its own, so Grimdark Future's is better than nothing.");
                Assert.That(OfficialRuleText.Text("Both", GameSystems.GFFirefight), Is.EqualTo("firefight's own"),
                    "where the system HAS its own text, the parent must never win - that is the whole "
                    + "point of keying this per system.");
                Assert.That(OfficialRuleText.Text("Nowhere", GameSystems.GFFirefight), Is.Null);
            });
        }

        /// <summary>
        /// The engine authors helper definitions with names no book prints - "AP(+1) in melee",
        /// "Trait: Agile", "Shred when shooting" - to carry one effect apiece. OPR publishes no text for
        /// those and never will, so their authored description is the only text there is and must
        /// survive.
        /// </summary>
        [Test]
        public void AGeneratedHelperRuleKeepsItsAuthoredDescription()
        {
            var definition = new SpecialRuleDefinition("AP(+1) in melee",
                Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(),
                Description: "This unit's melee weapons get AP(+1).");

            Assert.That(OfficialRuleText.Text(definition.Name, GameSystems.GrimdarkFuture), Is.Null,
                "no OPR book prints a rule under this name - it is one the engine generates.");
            Assert.That(OfficialRuleText.Display(definition, GameSystems.GrimdarkFuture),
                Is.EqualTo("This unit's melee weapons get AP(+1)."));
        }

        /// <summary>
        /// The canary. These four authored summaries were each measurably wrong in a way a player would
        /// act on, and all four are what the game showed before #420. If any of them is what Display()
        /// returns again, something has re-pointed the UI at Description.
        /// </summary>
        [Test]
        public void TheKnownBadSummariesAreNoLongerWhatAPlayerSees()
        {
            (SpecialRuleDefinition Rule, string Missing)[] cases =
            {
                (CoreRuleCatalog.Ambush, "alternate"),   // summary dropped "players alternate in placing"
                (CoreRuleCatalog.Blast, "Cover"),        // summary dropped "ignores Cover" entirely
                (CoreRuleCatalog.Impact, "Fatigued"),    // summary dropped the clause the CODE was missing too
                (CoreRuleCatalog.Deadly, "resolved first"), // summary dropped the ordering clause
            };

            foreach ((SpecialRuleDefinition rule, string missing) in cases)
            {
                string shown = OfficialRuleText.Display(rule, GameSystems.GrimdarkFuture);
                Assert.That(shown, Is.Not.EqualTo(rule.Description),
                    $"{rule.Name} is still showing its authored summary.");
                Assert.That(shown, Does.Contain(missing).IgnoreCase,
                    $"{rule.Name}'s official text should carry \"{missing}\", which the summary dropped.");
            }
        }

        /// <summary>A null or unknown definition is an empty string, not a throw, in a draw loop.</summary>
        [Test]
        public void DisplayIsSafeForNull()
        {
            Assert.That(OfficialRuleText.Display((SpecialRuleDefinition?)null, GameSystems.GrimdarkFuture),
                Is.Empty);
            Assert.That(OfficialRuleText.Text(null, GameSystems.GrimdarkFuture), Is.Null);
            Assert.That(OfficialRuleText.Text("  ", GameSystems.GrimdarkFuture), Is.Null);
        }

        /// <summary>
        /// Staleness: the asset is a snapshot of OPR's text on the day it was taken, and the only way to
        /// tell a fresh bake from a two-ruleset-old one is the date it records.
        /// </summary>
        [Test]
        public void TheAssetRecordsWhenItWasHarvested()
        {
            Assert.That(OfficialRuleText.Harvested, Does.Match(@"^\d{4}-\d{2}-\d{2}$"),
                "regenerate with scripts/harvest-rule-text.py; the date is how a later session tells "
                + "whether the text predates a ruleset change.");
        }
    }
}
