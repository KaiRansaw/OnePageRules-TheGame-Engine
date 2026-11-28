using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Bug hunt 2026-11-28 (AoF campaign): Campaign Rules v3.6.0, 5. Upgrade Armies - <i>"Players may sell any of their
    /// upgrades at half of their original price (rounding down to the nearest multiple of 5). If an item is only worth
    /// 5pts roll one die, on a 1-3 the item can't be sold, on a 4+ it can be sold for 5pts (you can only try to sell a
    /// 5pts item once after each mission). When selling upgrades worth less than 0pts, you must spend their full points
    /// cost instead of gaining those points."</i> The camp screen refunded the full price of every sale, and a swap
    /// inside one choice cost only the gap between the two options.
    /// </summary>
    [TestFixture]
    public class CampaignSellingTests
    {
        private const string Id = "squad";

        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        private static BookFile Book() => new()
        {
            Name = "Test Kingdom", Faction = "Test Kingdom",
            Units =
            {
                new RosterUnit
                {
                    Id = Id, Name = "Spearmen", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Spear", Attacks = 1 } },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "mount", Label = "Upgrade with one", Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options =
                            {
                                new UpgradeOption { Id = "wings", Label = "Wings", Cost = 20, WeaponsGained = { new WeaponFileEntry { Name = "Talons", Attacks = 1 } } },
                                new UpgradeOption { Id = "raider", Label = "Raider Wings", Cost = 30, WeaponsGained = { new WeaponFileEntry { Name = "Raider Talons", Attacks = 2 } } },
                            },
                        },
                        new UpgradeSection
                        {
                            Id = "banner", Label = "Upgrade one model", Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options = { new UpgradeOption { Id = "flag", Label = "Banner", Cost = 5, WeaponsGained = { new WeaponFileEntry { Name = "Flagpole", Attacks = 1 } } } },
                        },
                        new UpgradeSection
                        {
                            Id = "rabble", Label = "Upgrade one model", Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options = { new UpgradeOption { Id = "cheap", Label = "Rusty Kit", Cost = -10, WeaponsGained = { new WeaponFileEntry { Name = "Rusty Kit", Attacks = 1 } } } },
                        },
                    },
                },
            },
        };

        private static BuilderUnit Loadout(params (string Section, string Option)[] picks) => new()
        {
            RosterUnitId = Id, ModelCount = 5,
            Choices = picks.Select(p => new UpgradeChoice { SectionId = p.Section, OptionId = p.Option, Count = 1 }).ToList(),
        };

        /// <summary>A started campaign army whose one unit already owns <paramref name="owned"/>.</summary>
        private static (BuiltArmyFile Army, BookFile Book, UnitFileEntry Unit) Owning(int treasury, params (string, string)[] owned)
        {
            BookFile book = Book();
            var list = new BuilderList { Name = "T", BookName = book.Name };
            list.Units.Add(Loadout(owned));
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            CampaignRules.Start(army, "T", "t.fdgarmy", CampaignNumbers.AgeOfFantasy, EGameSystem.AgeOfFantasy);
            army.StrikeTeam!.BankedPoints = treasury;
            return (army, book, army.Units[0]);
        }

        [Test]
        public void SellingAnUpgrade_PaysHalfItsPriceRoundedDownToAFive()
        {
            var (army, book, unit) = Owning(0, ("mount", "raider"));
            CampaignPurchase sold = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout());
            Assert.That(sold.Ok, Is.True, sold.Message);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(15), "half of 30");
        }

        [Test]
        public void SwappingInsideOneChoice_SellsTheOldAtHalfAndBuysTheNewAtFull()
        {
            // Raider Wings (30) -> Wings (20): sell for 15, buy for 20 = 5 to pay. It used to PAY 10 back.
            var (army, book, unit) = Owning(50, ("mount", "raider"));
            Assert.That(CampaignEconomy.PriceOfChange(book, unit, Loadout(("mount", "wings"))), Is.EqualTo(5));
            CampaignPurchase swap = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout(("mount", "wings")));
            Assert.That(swap.Ok, Is.True, swap.Message);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(45));
        }

        [Test]
        public void BuyingAnUpgrade_StillCostsItsFullPrice()
        {
            var (army, book, unit) = Owning(50);
            Assert.That(CampaignEconomy.PriceOfChange(book, unit, Loadout(("mount", "wings"))), Is.EqualTo(20));
        }

        [Test]
        public void AFivePointItem_SellsForFiveOnAFourPlus()
        {
            var (army, book, unit) = Owning(0, ("banner", "flag"));
            CampaignPurchase sold = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout(), Dice(4));
            Assert.That(sold.Ok, Is.True, sold.Message);
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(5));
        }

        [Test]
        public void AFivePointItem_OnAOneToThree_CantBeSold_AndCantBeTriedAgainUntilAfterTheNextMission()
        {
            var (army, book, unit) = Owning(0, ("banner", "flag"));

            CampaignPurchase first = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout(), Dice(3));
            Assert.That(first.Ok, Is.False);
            Assert.That(unit.Weapons.Any(w => w.Name == "Flagpole"), Is.True, "still carried");
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(0));

            CampaignPurchase again = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout(), Dice(6));
            Assert.That(again.Ok, Is.False, "only one try after each mission");

            // A mission later it may be tried again.
            CampaignRules.AfterGame(army, new List<CampaignUnitResult>(), won: false, CampaignNumbers.AgeOfFantasy, Dice(3));
            CampaignPurchase next = CampaignEconomy.ChangeUpgrades(army, unit, book, Loadout(), Dice(5));
            Assert.That(next.Ok, Is.True, next.Message);
        }

        [Test]
        public void SellingAnUpgradeWorthLessThanZero_CostsItsFullPrice()
        {
            var (army, book, unit) = Owning(20, ("rabble", "cheap"));
            Assert.That(CampaignEconomy.PriceOfChange(book, unit, Loadout()), Is.EqualTo(10),
                "\"you must spend their full points cost instead of gaining those points\"");
        }
    }
}
