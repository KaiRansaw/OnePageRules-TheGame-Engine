namespace FDG.ArmyBuilding
{
    /// <summary>
    /// #395 - a legal army made up on the spot from one army book, for the lobby's Random Army when there is no saved
    /// army of this game to hand out (GF: Firefight and AoF: Skirmish ship with none; a fresh install might have no
    /// armies folder at all). Before this the button silently did nothing.
    ///
    /// <para>
    /// The list is filled the way a person would: one Hero first, then units at random - new kinds before repeats -
    /// each at its default size and options, kept only when the army is still legal (<see cref="ListValidator"/>:
    /// the points limit, and the force-org caps on heroes, copies and unit size). It stops when nothing more fits.
    /// Upgrades are left at the book's defaults - a random army is a starting point, not a tuned list.
    /// </para>
    /// </summary>
    public static class RandomArmyBuilder
    {
        /// <summary>
        /// An army from <paramref name="book"/> at up to <paramref name="pointsLimit"/>, or null when not even one unit
        /// fits. Deterministic for a given <paramref name="rng"/> seed.
        /// </summary>
        public static BuiltArmyFile? Build(BookFile book, int pointsLimit, Random rng, string? name = null)
        {
            if (pointsLimit <= 0 || book.Units.Count == 0) return null;
            var list = new BuilderList { Name = name ?? $"Random {book.Name}", BookName = book.Name, PointsLimit = pointsLimit };

            int Problems() => ListValidator.Validate(book, list, ListCompiler.Compile(book, list))
                .Count(i => !i.Message.Contains("no published points cost", StringComparison.Ordinal)
                            // the Hero goes in first, so "no non-Hero units" is true until the second unit
                            && !i.Message.Contains("no non-Hero units", StringComparison.Ordinal));

            bool TryAdd(RosterUnit roster)
            {
                int before = Problems();
                if (BuilderListEditing.AddUnit(book, list, roster.Id) < 0) return false;
                if (Problems() <= before) return true;
                list.Units.RemoveAt(list.Units.Count - 1);
                return false;
            }

            var heroes = book.Units.Where(IsHero).OrderBy(_ => rng.Next()).ToList();
            foreach (RosterUnit hero in heroes)
                if (TryAdd(hero)) break;

            var others = book.Units.Where(u => !IsHero(u)).ToList();
            var tried = new HashSet<string>();
            var full = new HashSet<string>();
            for (int guard = 0; guard < 200; guard++)
            {
                var candidates = others.Where(u => !full.Contains(u.Id)).ToList();
                if (candidates.Count == 0) break;
                var fresh = candidates.Where(u => !tried.Contains(u.Id)).ToList();
                List<RosterUnit> from = fresh.Count > 0 ? fresh : candidates;
                RosterUnit pick = from[rng.Next(from.Count)];
                tried.Add(pick.Id);
                if (!TryAdd(pick)) full.Add(pick.Id);
            }

            if (list.Units.Count == 0 || list.Units.All(u => heroes.Any(h => h.Id == u.RosterUnitId))) return null;
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            army.Name = list.Name;
            army.Book = book;
            return army;
        }

        private static bool IsHero(RosterUnit unit) =>
            Campaign.CampaignBot.IsHeroEntry(unit);
    }
}
