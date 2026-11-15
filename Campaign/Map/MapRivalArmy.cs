using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FDG.Campaign.Map
{
    /// <summary>
    /// #467 step 3 - a rival army built for you (Kai, 2026-11-15: "auto-built"; rivals may share a faction with you or each
    /// other). A random army book of the game, a name, and a starting army bought from the book's treasury by the
    /// Campaign Rules' own preparation rules: at most one Hero, and at least the minimum spend - built the way a player
    /// would, through <see cref="CampaignEconomy.Recruit"/>, so every price and cap is the book's.
    ///
    /// <para>It buys a Hero first (the campaign tables are written around one), then units picked at random among those
    /// it can still afford, favouring ones it doesn't have yet - so a rival looks like an army, not twelve copies of its
    /// cheapest unit.</para>
    /// </summary>
    public static class MapRivalArmy
    {
        /// <summary>A rival's starting army, or null when no book of the game can make a legal one.</summary>
        public static (string Name, BuiltArmyFile Army)? Build(EGameSystem system, IReadOnlyList<BookFile> books, Random rng,
                                                              IReadOnlyCollection<string>? namesTaken = null)
        {
            if (books.Count == 0) return null;
            CampaignNumbers n = CampaignNumbers.For(system);
            string name = PickName(CampaignMapRules.StyleFor(system), rng, namesTaken);
            foreach (BookFile book in Shuffled(books, rng).Take(8))
            {
                BuiltArmyFile? army = TryBuild(book, name, system, n, rng);
                if (army != null) return (name, army);
            }
            return null;
        }

        /// <summary>
        /// A neutral region's local garrison (house rule B24): a bot army from a random book, with a roster worth about the
        /// player's army, so Mission Setup's force-picking sizes what it brings. Null if no book can field one.
        /// </summary>
        public static BuiltArmyFile? Garrison(EGameSystem system, IReadOnlyList<BookFile> books, Random rng, string regionName, int budget)
        {
            CampaignNumbers n = CampaignNumbers.For(system);
            foreach (BookFile book in Shuffled(books, rng).Take(8))
                if (TryBuild(book, $"Garrison of {regionName}", system, n, rng, Math.Max(budget, 1), legalStart: false) is { } army)
                    return army;
            return null;
        }

        /// <summary>
        /// Spends a fresh treasury (or <paramref name="budget"/>) on one book's units. Null if it can't reach the minimum
        /// spend - unless <paramref name="legalStart"/> is false (a garrison, which only needs some units).
        /// </summary>
        public static BuiltArmyFile? TryBuild(BookFile book, string name, EGameSystem system, CampaignNumbers n, Random rng,
                                              int? budget = null, bool legalStart = true)
        {
            BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList { Name = name, BookName = book.Name });
            army.Name = name;
            CampaignRules.Start(army, name, "", n, system, ECampaignType.Endless, isBot: true);
            StrikeTeamInfo info = army.StrikeTeam!;
            info.BankedPoints = budget ?? n.Treasury;

            bool CanBuy(RosterUnit r) =>
                r.BasePointCost > 0 && r.BasePointCost <= info.BankedPoints
                && CampaignEconomy.CannotRecruit(army, book, Wanted(r), false) == null;

            // One Hero (the book's preparation allows at most one) - none for a small garrison.
            var heroes = book.Units.Where(r => CampaignBot.IsHeroEntry(r) && CanBuy(r)).ToList();
            if (heroes.Count > 0 && (legalStart || info.BankedPoints >= n.Treasury / 2))
                CampaignEconomy.Recruit(army, book, Wanted(heroes[rng.Next(heroes.Count)]), n);

            // Then units at random, new kinds first, until nothing more fits.
            var bought = new HashSet<string>();
            for (int guard = 0; guard < 60; guard++)
            {
                var affordable = book.Units.Where(r => !CampaignBot.IsHeroEntry(r) && CanBuy(r)).ToList();
                if (affordable.Count == 0) break;
                var fresh = affordable.Where(r => !bought.Contains(r.Id)).ToList();
                var from = fresh.Count > 0 ? fresh : affordable;
                RosterUnit pick = from[rng.Next(from.Count)];
                if (!CampaignEconomy.Recruit(army, book, Wanted(pick), n).Ok) break;
                bought.Add(pick.Id);
            }
            army.Book = book;
            if (army.Units.Count == 0) return null;
            return !legalStart || CampaignRules.CannotStart(army, n) == null ? army : null;
        }

        /// <summary>
        /// Gives an army that is already a campaign army (one built here) its name and its file: every unit's sheet points
        /// at the file, which is how the game's write-back finds where a mission's results go. Re-starting it instead would
        /// wipe the sheets the recruits were bought onto.
        /// </summary>
        public static void Adopt(ArmyListFile army, string name, string path, bool noPermadeath)
        {
            StrikeTeamInfo info = army.StrikeTeam!;
            army.Name = info.Name = name;
            info.IsBot = true;
            info.NoPermadeath = noPermadeath;
            info.CampaignType = ECampaignType.Endless;
            foreach (UnitFileEntry unit in army.Units)
                if (CampaignRules.Sheet(unit) is { } sheet)
                {
                    sheet.TeamFile = path;
                    CampaignRules.Save(unit, sheet);
                }
        }

        private static BuilderUnit Wanted(RosterUnit roster) => new() { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };

        private static List<T> Shuffled<T>(IEnumerable<T> items, Random rng)
        {
            var list = items.ToList();
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        public static string PickName(EMapStyle style, Random rng, IReadOnlyCollection<string>? taken = null)
        {
            string[] names = style == EMapStyle.Realm ? RealmNames : StarNames;
            var free = names.Where(x => taken == null || !taken.Contains(x, StringComparer.OrdinalIgnoreCase)).ToList();
            return free.Count > 0 ? free[rng.Next(free.Count)] : names[rng.Next(names.Length)] + " " + rng.Next(2, 99);
        }

        // Made-up army names (ASCII - game text).
        private static readonly string[] StarNames =
        {
            "The Hollow Choir", "Brass Legion", "Pale Court", "Iron Covenant", "The Ninth Host", "Ashen Fleet",
            "Red Tithe", "Black Lantern Company", "The Sundered", "Vigil of Thorns", "Grey Wardens", "The Ember Pact",
            "Silent Armada", "Crimson Vanguard", "The Last Muster", "Obsidian Watch", "Storm Syndicate", "The Gilded Ruin",
            "Rust Brotherhood", "Hollow Star Raiders", "Seventh Pyre", "The Cold Accord", "Vanta Host", "Dust Kings",
        };

        private static readonly string[] RealmNames =
        {
            "The Thorn Court", "Ironhill Muster", "Grave Wardens", "Free Company of the Vale", "Blackmoor Host",
            "The Ashen Banner", "Sons of the Barrow", "Crown of Briars", "Wolfsden Levy", "The Gilded Pact", "Raven Lords",
            "Stonebridge Guard", "The Hollow Crown", "Ember Riders", "Mirefield Raiders", "Oakenshaw Wardens",
            "The Pale Hunt", "Saltreach Corsairs", "Duskhold Order", "Grey Pilgrims", "The Bone Choir", "Hearthguard",
        };
    }
}
