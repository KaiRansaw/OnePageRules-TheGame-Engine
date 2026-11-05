using System.Linq;
using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #453 - campaign units get names of their own. The book: "Each army and unit must be given a unique
    /// name." Kai, 2026-11-03: pre-filled with the book's name, renamed where you care; shown as the name
    /// with the unit type small underneath ("Vargas Squad" / "Assault Grunts").
    /// </summary>
    [TestFixture]
    public class CampaignUnitNameTests
    {
        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit
                {
                    Id = "trooper", Name = "Troopers", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "gun", Label = "Upgrade one model's weapon",
                            Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "heavy", Label = "Heavy Rifle", Cost = 20,
                                    WeaponsGained = { new WeaponFileEntry { Name = "Heavy Rifle", RangeInches = 30, Attacks = 2 } },
                                },
                            },
                        },
                    },
                },
            },
        };

        /// <summary>Two squads of the same type, the case where the book's names collide.</summary>
        private static (BuiltArmyFile Army, BookFile Book) Started()
        {
            BookFile book = Book();
            var list = new BuilderList { Name = "Test", BookName = book.Name };
            list.Units.Add(new BuilderUnit { RosterUnitId = "trooper", ModelCount = 5 });
            list.Units.Add(new BuilderUnit { RosterUnitId = "trooper", ModelCount = 5 });
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            CampaignRules.Start(army, "Iron Reavers", "x.fdgarmy", CampaignNumbers.Firefight, EGameSystem.GFFirefight);
            army.StrikeTeam!.BankedPoints = 100;
            return (army, book);
        }

        [Test]
        public void UnitsStartWithTheBooksNameAndRememberTheirType()
        {
            (BuiltArmyFile army, BookFile book) = Started();

            Assert.That(army.Units.Select(u => u.Name), Is.EqualTo(new[] { "Troopers", "Troopers 2" }),
                "pre-filled with the book's name, made unique the way it always has been.");
            Assert.That(army.Units.Select(u => CampaignRules.UnitTypeOf(u, book)), Is.All.EqualTo("Troopers"),
                "the second squad is still a Troopers squad, not a 'Troopers 2'.");
            Assert.That(CampaignRules.HasOwnName(army.Units[0], book), Is.False);
            Assert.That(CampaignRules.HasOwnName(army.Units[1], book), Is.True,
                "'Troopers 2' is not its type, so the type is shown under it - which is right.");
        }

        [Test]
        public void RenamingKeepsTheType()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            UnitFileEntry unit = army.Units[1];

            Assert.That(CampaignRules.Rename(army, unit, "Vargas Squad", book), Is.Null);

            Assert.That(unit.Name, Is.EqualTo("Vargas Squad"));
            Assert.That(CampaignRules.UnitTypeOf(unit, book), Is.EqualTo("Troopers"));
            Assert.That(CampaignRules.HasOwnName(unit, book), Is.True);
            Assert.That(CampaignRules.Sheet(unit)!.UnitType, Is.EqualTo("Troopers"), "saved on the unit's sheet.");
        }

        [Test]
        public void NamesMustBeGivenAndUnique()
        {
            (BuiltArmyFile army, BookFile book) = Started();

            Assert.That(CampaignRules.Rename(army, army.Units[0], "   ", book), Is.EqualTo("A unit needs a name."));
            Assert.That(CampaignRules.Rename(army, army.Units[0], "troopers 2", book),
                Is.EqualTo("Another unit in this army already has that name."), "case does not make it a different name.");
            Assert.That(CampaignRules.Rename(army, army.Units[0], new string('x', CampaignRules.MaxUnitNameLength + 1), book),
                Does.Contain("characters"));
            Assert.That(army.Units[0].Name, Is.EqualTo("Troopers"), "a refused rename changes nothing.");
        }

        [Test]
        public void RenamingAUnitToItsOwnNameIsFine()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            Assert.That(CampaignRules.Rename(army, army.Units[0], "TROOPERS", book), Is.Null,
                "a unit is not in conflict with itself.");
        }

        [Test]
        public void TypographyIsFoldedAndLineBreaksFlattened()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            Assert.That(CampaignRules.Rename(army, army.Units[0], "The “Iron”\nFist", book), Is.Null);
            Assert.That(army.Units[0].Name, Is.EqualTo("The \"Iron\" Fist"),
                "the name is in every log line and hover; it must be drawable and on one line.");
        }

        /// <summary>
        /// The likeliest way to lose a name: buying an upgrade re-compiles the unit from the book. The name
        /// must survive, or every upgrade would quietly turn "Vargas Squad" back into "Troopers".
        /// </summary>
        [Test]
        public void BuyingAnUpgradeKeepsTheName()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            UnitFileEntry unit = army.Units[0];
            CampaignRules.Rename(army, unit, "Vargas Squad", book);

            CampaignPurchase bought = CampaignEconomy.ChangeUpgrades(army, unit, book, new BuilderUnit
            {
                RosterUnitId = "trooper", ModelCount = 5,
                Choices = { new UpgradeChoice { SectionId = "gun", OptionId = "heavy", Count = 1 } },
            });

            Assert.That(bought.Ok, Is.True, bought.Message);
            Assert.That(unit.Name, Is.EqualTo("Vargas Squad"));
            Assert.That(CampaignRules.UnitTypeOf(unit, book), Is.EqualTo("Troopers"));
        }

        [Test]
        public void ARecruitKnowsItsTypeToo()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            CampaignPurchase hired = CampaignEconomy.Recruit(army, book,
                new BuilderUnit { RosterUnitId = "trooper", ModelCount = 5 }, CampaignNumbers.Firefight);

            Assert.That(hired.Ok, Is.True, hired.Message);
            UnitFileEntry recruit = army.Units.Last();
            Assert.That(recruit.Name, Is.EqualTo("Troopers 3"));
            Assert.That(CampaignRules.UnitTypeOf(recruit, book), Is.EqualTo("Troopers"));
        }

        /// <summary>
        /// A unit from a campaign made before #453 has no type on its sheet. It could never have been
        /// renamed, so its type comes from its loadout's roster entry - and renaming it fixes the type first
        /// so it is not lost.
        /// </summary>
        [Test]
        public void AnOlderUnitFindsItsTypeAndKeepsItWhenRenamed()
        {
            (BuiltArmyFile army, BookFile book) = Started();
            UnitFileEntry unit = army.Units[1];
            StrikeUnit sheet = CampaignRules.Sheet(unit)!;
            sheet.UnitType = "";
            CampaignRules.Save(unit, sheet);

            Assert.That(CampaignRules.UnitTypeOf(unit, book), Is.EqualTo("Troopers"),
                "from the roster entry, not from 'Troopers 2'.");
            Assert.That(CampaignRules.UnitTypeOf(unit, null), Is.EqualTo("Troopers 2"),
                "with no book to ask, the name it always had is the best answer there is.");

            CampaignRules.Rename(army, unit, "Old Guard", book);
            Assert.That(CampaignRules.UnitTypeOf(unit, null), Is.EqualTo("Troopers"),
                "fixed onto the sheet at the rename, so it no longer needs the book.");
        }
    }
}
