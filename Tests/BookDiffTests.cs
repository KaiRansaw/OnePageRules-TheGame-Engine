using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #412 - comparing two army books. This is the check that makes a 1,817-unit refresh reviewable, and
    /// the check that proves two game systems are carrying their own data rather than one silently
    /// falling back to the other (#406). The thing it must never do is report "no differences" when
    /// something moved.
    /// </summary>
    [TestFixture]
    public class BookDiffTests
    {
        private static BookFile Book(string name = "Test Legion", string version = "OPR v3.5.3") => new()
        {
            Name = name,
            Faction = name,
            Version = version,
            Units =
            {
                new RosterUnit
                {
                    Id = "grunts", Name = "Assault Grunts",
                    Quality = 4, Defense = 4,
                    BaseModelCount = 10, MinModels = 10, MaxModels = 20,
                    BasePointCost = 105,
                    Weapons = { new WeaponFileEntry { Name = "Claws", Attacks = 2, ArmorPenetration = 1 } },
                    Rules = { new SpecialRuleEntry_Core("Furious") },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "gear", Label = "Upgrade with one",
                            Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options = { new UpgradeOption { Id = "shield", Label = "Hive Protector", Cost = 15 } },
                        },
                    },
                },
                new RosterUnit
                {
                    Id = "lord", Name = "Hive Lord",
                    Quality = 3, Defense = 2, BaseModelCount = 1, MinModels = 1, MaxModels = 1,
                    BasePointCost = 360,
                    Rules = { new SpecialRuleEntry_Core("Hero"), new SpecialRuleEntry_CoreNumeric("Tough", 12) },
                },
            },
        };

        private static IReadOnlyDictionary<string, BookFile> Set(params BookFile[] books) =>
            books.ToDictionary(b => b.Name + ".fdgbook", b => b, StringComparer.OrdinalIgnoreCase);

        private static bool Has(BookComparison c, string unit, string what) =>
            c.Changes.Any(ch => ch.Unit == unit && ch.What.StartsWith(what, StringComparison.Ordinal));

        [Test]
        public void TwoIdenticalBooksReportNothing()
        {
            BookComparison c = BookDiff.Compare(Book(), Book(), "left", "right");
            Assert.That(c.Any, Is.False);
            Assert.That(c.Changes, Is.Empty);
        }

        [Test]
        public void PointsQualityAndDefenseChangesAreCaught()
        {
            BookFile after = Book();
            after.Units[0].BasePointCost = 120;
            after.Units[0].Quality = 5;
            after.Units[1].Defense = 3;

            BookComparison c = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.Multiple(() =>
            {
                Assert.That(Has(c, "Assault Grunts", "Points"), Is.True);
                Assert.That(Has(c, "Assault Grunts", "Quality"), Is.True);
                Assert.That(Has(c, "Hive Lord", "Defense"), Is.True);
                Assert.That(c.Changed, Is.EqualTo(3));
            });
        }

        /// <summary>Kai's own example of why systems need their own books: same unit, different size.</summary>
        [Test]
        public void UnitSizeChangesAreCaught()
        {
            BookFile firefight = Book();
            firefight.Units[0].BaseModelCount = 3;
            firefight.Units[0].MinModels = 3;
            firefight.Units[0].MaxModels = 6;
            firefight.Units[0].BasePointCost = 30;

            BookComparison c = BookDiff.Compare(Book(), firefight, "Grimdark Future", "GF: Firefight");
            Assert.That(Has(c, "Assault Grunts", "Unit size"), Is.True);
            Assert.That(c.Changes.First(ch => ch.What == "Unit size").Left, Does.Contain("10"));
            Assert.That(c.Changes.First(ch => ch.What == "Unit size").Right, Does.Contain("3"));
        }

        /// <summary>The other half of that example: the Hive Lord is not in the Firefight book at all.</summary>
        [Test]
        public void AUnitMissingFromOneSideIsReported()
        {
            BookFile firefight = Book();
            firefight.Units.RemoveAt(1);

            BookComparison c = BookDiff.Compare(Book(), firefight, "Grimdark Future", "GF: Firefight");
            Assert.That(c.Changes.Any(ch => ch.Unit == "Hive Lord" && ch.Kind == EBookChange.Removed), Is.True);
            Assert.That(c.Removed, Is.EqualTo(1));
        }

        [Test]
        public void ANewUnitIsReportedAsAdded()
        {
            BookFile after = Book();
            after.Units.Add(new RosterUnit { Id = "new", Name = "Spore Mines", BasePointCost = 40 });

            BookComparison c = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.That(c.Changes.Any(ch => ch.Unit == "Spore Mines" && ch.Kind == EBookChange.Added), Is.True);
            Assert.That(c.Added, Is.EqualTo(1));
        }

        [Test]
        public void UpgradePriceChangesAndMissingUpgradesAreCaught()
        {
            BookFile after = Book();
            after.Units[0].Sections[0].Options[0].Cost = 25;

            BookComparison priced = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.That(priced.Changes.Any(ch => ch.What.Contains("Hive Protector") && ch.Left == "15 pts" && ch.Right == "25 pts"),
                Is.True);

            BookFile gone = Book();
            gone.Units[0].Sections[0].Options.Clear();
            BookComparison dropped = BookDiff.Compare(Book(), gone, "Grimdark Future", "GF: Firefight");
            Assert.That(dropped.Changes.Any(ch => ch.What.Contains("Hive Protector") && ch.Kind == EBookChange.Removed),
                Is.True);
        }

        /// <summary>
        /// An option OPR publishes no price for reads as "no published price", never as 0 pts - a 0 there
        /// would look like a free upgrade and quietly misprice an army.
        /// </summary>
        [Test]
        public void AnUnpricedOptionSaysSoRatherThanReadingAsFree()
        {
            BookFile after = Book();
            after.Units[0].Sections[0].Options[0].Cost = 0;
            after.Units[0].Sections[0].Options[0].CostUnpriced = true;

            BookComparison c = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.That(c.Changes.Any(ch => ch.Right == "no published price"), Is.True);
            Assert.That(c.Changes.Any(ch => ch.Right == "0 pts"), Is.False);
        }

        [Test]
        public void WeaponAndRuleChangesAreCaught()
        {
            BookFile after = Book();
            after.Units[0].Weapons[0].Attacks = 3;
            after.Units[0].Rules.Add(new SpecialRuleEntry_Core("Fearless"));
            after.Units[1].Rules.RemoveAll(r => r is SpecialRuleEntry_Core { Name: "Hero" });

            BookComparison c = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.Multiple(() =>
            {
                Assert.That(Has(c, "Assault Grunts", "Weapon: Claws"), Is.True);
                Assert.That(c.Changes.Any(ch => ch.Unit == "Assault Grunts" && ch.Right == "Fearless" && ch.Kind == EBookChange.Added), Is.True);
                Assert.That(c.Changes.Any(ch => ch.Unit == "Hive Lord" && ch.Left == "Hero" && ch.Kind == EBookChange.Removed), Is.True);
            });
        }

        [Test]
        public void AnUpgradeSectionsShapeChangeIsCaught()
        {
            BookFile after = Book();
            after.Units[0].Sections[0].Affects = UpgradeAffects.Any;
            after.Units[0].Sections[0].MaxApplications = 2;

            BookComparison c = BookDiff.Compare(Book(), after, "3.5", "3.6");
            Assert.That(Has(c, "Assault Grunts", "Upgrade section"), Is.True);
        }

        // -- Whole sets -------------------------------------------------------------------------------

        [Test]
        public void ABookOnOnlyOneSideIsReportedNotSkipped()
        {
            var left = Set(Book("Alien Hives"), Book("Robot Legions"));
            var right = Set(Book("Alien Hives"));

            var results = BookDiff.CompareSets(left, right, "Grimdark Future", "GF: Firefight");
            Assert.That(results.Count, Is.EqualTo(2));
            BookComparison missing = results.First(r => r.BookName == "Robot Legions");
            Assert.That(missing.Changes.Single().What, Is.EqualTo("Whole book"));
            Assert.That(missing.Changes.Single().Kind, Is.EqualTo(EBookChange.Removed));
        }

        [Test]
        public void TheReportLeadsWithTheSummaryAndNamesEveryBookThatDiffers()
        {
            BookFile after = Book("Alien Hives");
            after.Units[0].BasePointCost = 120;
            var results = BookDiff.CompareSets(Set(Book("Alien Hives")), Set(after), "3.5", "3.6");

            string report = BookDiff.Report(results, "Test report");
            Assert.Multiple(() =>
            {
                Assert.That(report, Does.StartWith("# Test report"));
                Assert.That(report, Does.Contain("**1 differ**"));
                Assert.That(report, Does.Contain("## Summary"));
                Assert.That(report, Does.Contain("Alien Hives"));
                Assert.That(report, Does.Contain("105").And.Contain("120"));
            });
        }

        /// <summary>
        /// The finding that matters most when comparing two game systems: two sides that come out
        /// identical is itself the signal, so the report has to say so in words rather than print an
        /// empty page.
        /// </summary>
        [Test]
        public void AnIdenticalComparisonSaysSoPlainly()
        {
            var results = BookDiff.CompareSets(Set(Book("Alien Hives")), Set(Book("Alien Hives")),
                "Grimdark Future", "GF: Firefight");
            string report = BookDiff.Report(results, "Systems");
            Assert.That(report, Does.Contain("Nothing differs at all"));
            Assert.That(report, Does.Contain("the same data"));
        }
    }
}
