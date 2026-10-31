namespace FDG.Quest
{
    /// <summary>
    /// Buying, selling and handing over Star Quest consumables - the rules half of every shop in the
    /// game, in one place.
    ///
    /// <para>
    /// #414. There were three shops and three copies of these rules: the between-wave shop in
    /// <c>QuestTreasure.Shop</c>, the between-chapter shop drawn inline by the campaign screen, and now a
    /// fourth in the Hero Builder. Every copy has to agree about the same four things - which tier a
    /// hero's level unlocks, what it can afford, what a sell is worth, and that a hero cannot carry what
    /// it has not bought - and three hand-written copies of that is three chances to disagree. The
    /// drawing stays with each screen; the arithmetic lives here and is tested once.
    /// </para>
    ///
    /// <para>
    /// <b>Where a shop may legally open is NOT this class's business</b>, and deliberately so. The books
    /// are specific: Horde opens a full shop between waves, a campaign opens one before and after a
    /// chapter but <i>"not between chapter missions"</i>, and between missions heroes may only hand items
    /// and credits to each other. Those are mission-structure rules and they live with the structures
    /// that know about waves and chapters. This class answers "may this hero buy this item", never "may
    /// this hero buy right now".
    /// </para>
    /// </summary>
    public static class QuestShop
    {
        /// <summary>
        /// Why <paramref name="hero"/> cannot buy <paramref name="item"/>, or null if it can. The text is
        /// shown to the player, so it says what to do about it.
        /// </summary>
        public static string? WhyCannotBuy(QuestHero hero, QuestItemInfo item)
        {
            int tier = QuestProgression.TierUnlocked(hero.Level);
            if (item.Tier > tier)
            {
                return $"Tier {item.Tier} unlocks at level {LevelForTier(item.Tier)}; "
                    + $"{hero.Name} is level {hero.Level}.";
            }

            return item.Cost > hero.Credits
                ? $"Costs {item.Cost}c and {hero.Name} has {hero.Credits}c."
                : null;
        }

        /// <summary>The hero level at which a shop tier becomes available (tiers 1/2/3 at levels 2/5/9).
        /// The inverse of <see cref="QuestProgression.TierUnlocked"/>, for explaining a locked row.</summary>
        public static int LevelForTier(int tier) => tier switch { <= 0 => 1, 1 => 2, 2 => 5, _ => 9 };

        /// <summary>
        /// Buys <paramref name="item"/> for <paramref name="hero"/>: credits down, item in the pack.
        /// Returns false and changes nothing when <see cref="WhyCannotBuy"/> has a reason.
        /// </summary>
        public static bool Buy(QuestHero hero, QuestItemInfo item)
        {
            if (WhyCannotBuy(hero, item) != null) return false;
            hero.Credits -= item.Cost;
            hero.Items.Add(item.Name);
            return true;
        }

        /// <summary>
        /// Sells one copy of <paramref name="itemName"/> back. The shop pays full cost (<see
        /// cref="QuestItemInfo.SellValue"/>), which is the book's price and was a real bug once when it
        /// paid half. Returns false when the hero isn't carrying one, or when the name isn't a shop item
        /// at all - an unknown name has no price, and inventing one would mint credits.
        /// </summary>
        public static bool Sell(QuestHero hero, string itemName)
        {
            if (QuestItems.Find(itemName) is not { } info) return false;
            if (!hero.Items.Remove(itemName)) return false;
            hero.Credits += info.SellValue;
            return true;
        }

        /// <summary>
        /// Hands one copy of <paramref name="itemName"/> from one hero to another. Returns false and
        /// changes nothing if the giver isn't carrying one or the two are the same hero.
        /// <para>
        /// An unknown item name is still transferable, unlike <see cref="Sell"/>: a hero may be carrying
        /// something from a book this build doesn't know, and passing it along moves a string from one
        /// pack to another without pricing it.
        /// </para>
        /// </summary>
        public static bool Give(QuestHero from, QuestHero to, string itemName)
        {
            if (ReferenceEquals(from, to)) return false;
            if (!from.Items.Remove(itemName)) return false;
            to.Items.Add(itemName);
            return true;
        }

        /// <summary>
        /// Hands <paramref name="credits"/> from one hero to another, as much as the giver actually has.
        /// Returns the amount moved, which is 0 for a non-positive amount or the same hero twice.
        /// </summary>
        public static int GiveCredits(QuestHero from, QuestHero to, int credits)
        {
            if (ReferenceEquals(from, to) || credits <= 0) return 0;
            int moved = Math.Min(credits, from.Credits);
            if (moved <= 0) return 0;
            from.Credits -= moved;
            to.Credits += moved;
            return moved;
        }

        /// <summary>
        /// Everything the shop stocks, in display order (tier, then name), each with the reason it can't
        /// be bought right now or null. The whole catalog is returned rather than just the affordable
        /// rows: a player deciding what to save up for needs to see the locked ones too.
        /// </summary>
        public static IEnumerable<(QuestItemInfo Item, string? WhyNot)> Stock(QuestHero hero) =>
            QuestItems.All
                .OrderBy(i => i.Tier)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .Select(i => (i, WhyCannotBuy(hero, i)));
    }
}
