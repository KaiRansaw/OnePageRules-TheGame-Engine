using FDG.Quest;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - the shop's arithmetic, in one place and tested once.
    //
    // There were three copies of these rules (the between-wave shop, the campaign screen's inline shop,
    // and now the Hero Builder's) and all of them have to agree about four things: which tier a hero's
    // level unlocks, what it can afford, what a sell pays, and that a hero can't sell what it isn't
    // carrying. Selling has already been wrong once in this project - it paid half, where the book pays
    // full cost - and a third hand-written copy is a third chance at that.
    //
    // Note what is NOT here. Where a shop may legally open is mission structure, not shop arithmetic:
    // Horde opens a full shop between waves, a campaign before and after a chapter but "not between
    // chapter missions", and between missions heroes may only hand things over. Those rules live with the
    // code that knows about waves and chapters, so QuestShop answers "may this hero buy this item" and
    // never "may this hero buy right now".
    [TestFixture]
    public class QuestShopTests
    {
        private static QuestHero Hero(int level = 1, int credits = 100, params string[] items)
        {
            var hero = new QuestHero { Name = "Vex", Class = "Soldier", Level = level, Credits = credits };
            hero.Items.AddRange(items);
            return hero;
        }

        /// <summary>The cheapest always-for-sale item, so affordability tests don't depend on the catalog.</summary>
        private static QuestItemInfo Tier0 =>
            QuestItems.All.Where(i => i.Tier == 0).OrderBy(i => i.Cost).First();

        private static QuestItemInfo Tier3 =>
            QuestItems.All.Where(i => i.Tier == 3).OrderBy(i => i.Cost).First();

        // ── Buying ──────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void BuyingTakesTheCreditsAndGivesTheItem()
        {
            QuestHero hero = Hero(credits: 100);
            QuestItemInfo item = Tier0;

            Assert.That(QuestShop.Buy(hero, item), Is.True);
            Assert.That(hero.Credits, Is.EqualTo(100 - item.Cost));
            Assert.That(hero.Items, Does.Contain(item.Name));
        }

        [Test]
        public void AHeroCannotBuyWhatItCannotAfford_AndNothingChanges()
        {
            // The important half is "nothing changes": a shop that takes the item but not the credits, or
            // the credits but not the item, is worse than one that refuses.
            QuestItemInfo item = Tier0;
            QuestHero hero = Hero(credits: item.Cost - 1);

            Assert.That(QuestShop.Buy(hero, item), Is.False);
            Assert.That(hero.Credits, Is.EqualTo(item.Cost - 1));
            Assert.That(hero.Items, Is.Empty);
            Assert.That(QuestShop.WhyCannotBuy(hero, item), Does.Contain("Costs"));
        }

        [Test]
        public void AHeroCannotBuyAboveItsTier()
        {
            // Tiers 1/2/3 unlock at levels 2/5/9, so a level 1 hero may only buy tier 0.
            QuestHero rookie = Hero(level: 1, credits: 1000);

            Assert.That(QuestShop.Buy(rookie, Tier3), Is.False);
            Assert.That(rookie.Credits, Is.EqualTo(1000));
            Assert.That(QuestShop.WhyCannotBuy(rookie, Tier3), Does.Contain("level 9"));
        }

        [TestCase(1, 0)]
        [TestCase(2, 1)]
        [TestCase(4, 1)]
        [TestCase(5, 2)]
        [TestCase(8, 2)]
        [TestCase(9, 3)]
        [TestCase(15, 3)]
        public void TierUnlocksMatchTheLevelsTheShopQuotes(int level, int expectedTier)
        {
            // LevelForTier is the inverse of TierUnlocked and only exists to explain a locked row, so the
            // two must not drift - a message naming the wrong level is worse than no message.
            Assert.That(QuestProgression.TierUnlocked(level), Is.EqualTo(expectedTier));
            if (expectedTier < 3)
            {
                Assert.That(QuestShop.LevelForTier(expectedTier + 1), Is.GreaterThan(level),
                    "the next tier must unlock above this level");
            }
        }

        [Test]
        public void TheStockListsTheWholeCatalog_LockedRowsIncluded()
        {
            // A player saving up needs to see what is coming, so Stock returns everything with a reason
            // rather than filtering. A shop that hides tier 3 looks like a shop with nothing in it.
            QuestHero rookie = Hero(level: 1, credits: 0);
            var stock = QuestShop.Stock(rookie).ToList();

            Assert.That(stock.Count, Is.EqualTo(QuestItems.All.Count));
            Assert.That(stock.All(s => s.WhyNot != null), Is.True, "a broke level-1 hero can buy nothing");
            Assert.That(stock.Select(s => s.Item.Tier), Is.Ordered, "cheapest tiers first");
        }

        // ── Selling ─────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void SellingPaysFullCost()
        {
            // The book's price, and a real bug once: the shop paid half.
            QuestItemInfo item = Tier0;
            QuestHero hero = Hero(credits: 0, items: item.Name);

            Assert.That(QuestShop.Sell(hero, item.Name), Is.True);
            Assert.That(hero.Credits, Is.EqualTo(item.Cost));
            Assert.That(hero.Items, Is.Empty);
        }

        [Test]
        public void SellingOneOfTwoLeavesTheOther()
        {
            QuestItemInfo item = Tier0;
            QuestHero hero = Hero(credits: 0, items: new[] { item.Name, item.Name });

            Assert.That(QuestShop.Sell(hero, item.Name), Is.True);
            Assert.That(hero.Items, Is.EqualTo(new[] { item.Name }).AsCollection);
        }

        [Test]
        public void AHeroCannotSellWhatItIsNotCarrying()
        {
            QuestHero hero = Hero(credits: 7);

            Assert.That(QuestShop.Sell(hero, Tier0.Name), Is.False);
            Assert.That(hero.Credits, Is.EqualTo(7), "a refused sell must not pay out");
        }

        [Test]
        public void AnUnknownItemCannotBeSold_BecauseItHasNoPrice()
        {
            // The one that would mint credits. A hero may be carrying a string from a book this build
            // doesn't know; pricing it at 0 would silently destroy it, and guessing a price would invent
            // money, so the sell is refused and the item stays in the pack.
            QuestHero hero = Hero(credits: 0, items: "Antimatter Teapot");

            Assert.That(QuestShop.Sell(hero, "Antimatter Teapot"), Is.False);
            Assert.That(hero.Credits, Is.EqualTo(0));
            Assert.That(hero.Items, Does.Contain("Antimatter Teapot"), "and it is not destroyed either");
        }

        // ── Handing things over ─────────────────────────────────────────────────────────────────────

        [Test]
        public void AnItemCanBeHandedToAnotherHero()
        {
            QuestItemInfo item = Tier0;
            QuestHero vex = Hero(items: item.Name);
            QuestHero mara = Hero();

            Assert.That(QuestShop.Give(vex, mara, item.Name), Is.True);
            Assert.That(vex.Items, Is.Empty);
            Assert.That(mara.Items, Does.Contain(item.Name));
        }

        [Test]
        public void AnUnknownItemCanStillBeHandedOver()
        {
            // Unlike selling: moving a string between packs needs no price.
            QuestHero vex = Hero(items: "Antimatter Teapot");
            QuestHero mara = Hero();

            Assert.That(QuestShop.Give(vex, mara, "Antimatter Teapot"), Is.True);
            Assert.That(mara.Items, Does.Contain("Antimatter Teapot"));
        }

        [Test]
        public void AHeroCannotHandAnItemToItself()
        {
            // Without the guard this removes then re-adds, which looks harmless until the list is being
            // enumerated by the screen that drew the button.
            QuestItemInfo item = Tier0;
            QuestHero vex = Hero(items: item.Name);

            Assert.That(QuestShop.Give(vex, vex, item.Name), Is.False);
            Assert.That(vex.Items, Does.Contain(item.Name));
        }

        [Test]
        public void AHeroCannotHandOverWhatItIsNotCarrying()
        {
            QuestHero vex = Hero();
            QuestHero mara = Hero();

            Assert.That(QuestShop.Give(vex, mara, Tier0.Name), Is.False);
            Assert.That(mara.Items, Is.Empty, "nothing may be created out of an empty pack");
        }

        [Test]
        public void CreditsCanBeHandedOver_AndNeverMoreThanTheGiverHas()
        {
            QuestHero vex = Hero(credits: 10);
            QuestHero mara = Hero(credits: 0);

            Assert.That(QuestShop.GiveCredits(vex, mara, 25), Is.EqualTo(10),
                "asking to give more than it has moves what it has, not what it was asked for");
            Assert.That(vex.Credits, Is.EqualTo(0));
            Assert.That(mara.Credits, Is.EqualTo(10));
        }

        [TestCase(0)]
        [TestCase(-5)]
        public void GivingNothingOrLess_MovesNothing(int amount)
        {
            QuestHero vex = Hero(credits: 10);
            QuestHero mara = Hero(credits: 3);

            Assert.That(QuestShop.GiveCredits(vex, mara, amount), Is.EqualTo(0));
            Assert.That(vex.Credits, Is.EqualTo(10));
            Assert.That(mara.Credits, Is.EqualTo(3), "a negative gift must not be a theft");
        }

        [Test]
        public void TotalCreditsAndItemsAreConserved_AcrossEveryTransfer()
        {
            // The property that matters for a shared party: handing things around is a move, never a
            // mint. Stated once over a sequence, because each operation above only checks itself.
            QuestItemInfo item = Tier0;
            QuestHero vex = Hero(credits: 30, items: new[] { item.Name, item.Name });
            QuestHero mara = Hero(credits: 10, items: item.Name);
            int credits = vex.Credits + mara.Credits;
            int items = vex.Items.Count + mara.Items.Count;

            QuestShop.Give(vex, mara, item.Name);
            QuestShop.GiveCredits(mara, vex, 5);
            QuestShop.Give(mara, vex, item.Name);
            QuestShop.GiveCredits(vex, mara, 1000);

            Assert.That(vex.Credits + mara.Credits, Is.EqualTo(credits));
            Assert.That(vex.Items.Count + mara.Items.Count, Is.EqualTo(items));
        }
    }
}
