using System.Text;

namespace FDG.ArmyBuilding
{
    /// <summary>One difference found between two books, at whatever level it was found.</summary>
    /// <param name="Unit">The unit it concerns, or "" for a book-level finding.</param>
    /// <param name="What">What differs, in plain words ("Points", "Unit size", "Upgrade: Heavy Rifle").</param>
    /// <param name="Left">What the first book says, or "" when it has nothing.</param>
    /// <param name="Right">What the second says, or "" when it has nothing.</param>
    /// <param name="Kind">Added, Removed or Changed, from the second book's point of view.</param>
    public readonly record struct BookChange(string Unit, string What, string Left, string Right, EBookChange Kind);

    /// <summary>Whether the second book gained something, lost something, or has it differently.</summary>
    public enum EBookChange { Added, Removed, Changed }

    /// <summary>Everything that differs between one pair of books.</summary>
    /// <param name="BookName">The book's name (both sides, when they agree).</param>
    /// <param name="LeftLabel">What the first side is, e.g. "OPR v3.5.3" or "Grimdark Future".</param>
    /// <param name="RightLabel">What the second side is.</param>
    /// <param name="Changes">Every difference, unit by unit.</param>
    public readonly record struct BookComparison(
        string BookName, string LeftLabel, string RightLabel, IReadOnlyList<BookChange> Changes)
    {
        public int Added => Changes.Count(c => c.Kind == EBookChange.Added);
        public int Removed => Changes.Count(c => c.Kind == EBookChange.Removed);
        public int Changed => Changes.Count(c => c.Kind == EBookChange.Changed);
        public bool Any => Changes.Count > 0;
    }

    /// <summary>
    /// Compares two army books unit by unit and says what differs: points, unit size, stats, weapons,
    /// rules and every upgrade option and its price.
    /// <para>
    /// It answers two questions with one mechanism, because they are the same question asked twice:
    /// </para>
    /// <list type="number">
    /// <item><b>Did the refresh do what it should?</b> Compare the old books against the new ones. Every
    /// unit whose points or size moved shows up, and so does every upgrade that appeared or vanished -
    /// which is the honest way to check 1,817 units without reading 1,817 units.</item>
    /// <item><b>Are the game systems really using their own data?</b> Compare Grimdark Future's books
    /// against Firefight's. OPR publishes genuinely different points, unit sizes and upgrade lists for
    /// them (#406), so a Firefight book that comes out identical to its Grimdark Future namesake is the
    /// signal that something fell back to the wrong data.</item>
    /// </list>
    /// <para>
    /// Units are matched by <b>name</b>, not id: ids are per-book, so the same unit in two different
    /// systems has two different ids and only the name joins them. Upgrade options are matched by their
    /// own <b>option</b> id, which OPR does keep stable; <b>section</b> ids are regenerated on every API
    /// response and pair nothing, which made this tool unusable for the refresh it was built for until
    /// #443 measured it. See <see cref="Options"/>.
    /// </para>
    /// </summary>
    public static class BookDiff
    {
        /// <summary>Compares two books. Everything is reported from <paramref name="right"/>'s point of
        /// view: Added means right has it and left does not.</summary>
        public static BookComparison Compare(BookFile left, BookFile right, string leftLabel, string rightLabel)
        {
            var changes = new List<BookChange>();

            var leftUnits = ByName(left);
            var rightUnits = ByName(right);

            foreach (string name in leftUnits.Keys.Where(k => !rightUnits.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(name, "Unit", Describe(leftUnits[name]), "", EBookChange.Removed));

            foreach (string name in rightUnits.Keys.Where(k => !leftUnits.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(name, "Unit", "", Describe(rightUnits[name]), EBookChange.Added));

            foreach (string name in leftUnits.Keys.Where(rightUnits.ContainsKey).OrderBy(k => k))
                CompareUnit(leftUnits[name], rightUnits[name], changes);

            return new BookComparison(right.Name.Length > 0 ? right.Name : left.Name, leftLabel, rightLabel, changes);
        }

        /// <summary>
        /// Compares every book that appears in both folders, by file name. Books in only one folder are
        /// reported as their own line so a missing or extra book is never silently skipped.
        /// </summary>
        public static IReadOnlyList<BookComparison> CompareSets(
            IReadOnlyDictionary<string, BookFile> left, IReadOnlyDictionary<string, BookFile> right,
            string leftLabel, string rightLabel)
        {
            var results = new List<BookComparison>();

            foreach (string key in left.Keys.Where(k => !right.ContainsKey(k)).OrderBy(k => k))
                results.Add(new BookComparison(left[key].Name, leftLabel, rightLabel, new[]
                {
                    new BookChange("", "Whole book", $"{left[key].Units.Count} unit(s)", "", EBookChange.Removed),
                }));

            foreach (string key in right.Keys.Where(k => !left.ContainsKey(k)).OrderBy(k => k))
                results.Add(new BookComparison(right[key].Name, leftLabel, rightLabel, new[]
                {
                    new BookChange("", "Whole book", "", $"{right[key].Units.Count} unit(s)", EBookChange.Added),
                }));

            foreach (string key in left.Keys.Where(right.ContainsKey).OrderBy(k => k))
                results.Add(Compare(left[key], right[key], leftLabel, rightLabel));

            return results;
        }

        // -- One unit ---------------------------------------------------------------------------------

        private static void CompareUnit(RosterUnit a, RosterUnit b, List<BookChange> changes)
        {
            string unit = b.Name;

            Number(unit, "Points", a.BasePointCost, b.BasePointCost, changes);
            Number(unit, "Quality", a.Quality, b.Quality, changes);
            Number(unit, "Defense", a.Defense, b.Defense, changes);

            if (a.BaseModelCount != b.BaseModelCount || a.MinModels != b.MinModels || a.MaxModels != b.MaxModels)
                changes.Add(new BookChange(unit, "Unit size",
                    Size(a), Size(b), EBookChange.Changed));

            CompareWeapons(unit, a.Weapons, b.Weapons, changes);
            CompareRules(unit, a.Rules, b.Rules, changes);
            CompareSections(unit, a, b, changes);
        }

        private static void CompareWeapons(string unit, List<SaveLoad.WeaponFileEntry> a,
                                           List<SaveLoad.WeaponFileEntry> b, List<BookChange> changes)
        {
            var left = a.GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var right = b.GroupBy(w => w.Name, StringComparer.OrdinalIgnoreCase)
                         .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (string name in left.Keys.Where(k => !right.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(unit, "Weapon: " + name, Describe(left[name]), "", EBookChange.Removed));
            foreach (string name in right.Keys.Where(k => !left.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(unit, "Weapon: " + name, "", Describe(right[name]), EBookChange.Added));
            foreach (string name in left.Keys.Where(right.ContainsKey).OrderBy(k => k))
            {
                string la = Describe(left[name]), lb = Describe(right[name]);
                if (la != lb) changes.Add(new BookChange(unit, "Weapon: " + name, la, lb, EBookChange.Changed));
            }
        }

        private static void CompareRules(string unit, List<SaveLoad.SpecialRuleEntry> a,
                                         List<SaveLoad.SpecialRuleEntry> b, List<BookChange> changes)
        {
            var left = a.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var right = b.Select(r => r.PrintableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string name in left.Where(n => !right.Contains(n)).OrderBy(n => n))
                changes.Add(new BookChange(unit, "Rule", name, "", EBookChange.Removed));
            foreach (string name in right.Where(n => !left.Contains(n)).OrderBy(n => n))
                changes.Add(new BookChange(unit, "Rule", "", name, EBookChange.Added));
        }

        private static void CompareSections(string unit, RosterUnit a, RosterUnit b, List<BookChange> changes)
        {
            var left = Options(a);
            var right = Options(b);

            foreach (string key in left.Keys.Where(k => !right.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(unit, "Upgrade: " + left[key].Label,
                    Price(left[key]), "", EBookChange.Removed));

            foreach (string key in right.Keys.Where(k => !left.ContainsKey(k)).OrderBy(k => k))
                changes.Add(new BookChange(unit, "Upgrade: " + right[key].Label,
                    "", Price(right[key]), EBookChange.Added));

            foreach (string key in left.Keys.Where(right.ContainsKey).OrderBy(k => k))
            {
                UpgradeOption la = left[key], lb = right[key];
                if (!string.Equals(la.Label, lb.Label, StringComparison.Ordinal))
                    changes.Add(new BookChange(unit, "Upgrade renamed", la.Label, lb.Label, EBookChange.Changed));
                string pa = Price(la), pb = Price(lb);
                if (pa != pb)
                    changes.Add(new BookChange(unit, "Upgrade: " + lb.Label, pa, pb, EBookChange.Changed));
            }

            // A section's shape decides how many times an option may be taken, so a change there is a
            // real rules change even when every option still costs the same. Paired by label, not id -
            // see SectionsByLabel.
            Dictionary<string, UpgradeSection> leftSections = SectionsByLabel(a);
            foreach ((string key, UpgradeSection s) in SectionsByLabel(b))
            {
                if (!leftSections.TryGetValue(key, out UpgradeSection? old)) continue;
                string so = Shape(old), sn = Shape(s);
                if (so != sn)
                    changes.Add(new BookChange(unit, "Upgrade section: " + s.Label, so, sn, EBookChange.Changed));
            }
        }

        // -- Describing things ------------------------------------------------------------------------

        private static Dictionary<string, RosterUnit> ByName(BookFile book) =>
            book.Units.GroupBy(u => u.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Keys a unit's upgrade options so the same option pairs with itself across two imports.
        ///
        /// <para>
        /// <b>By option id, NOT by section id.</b> The key used to be <c>sectionId + "/" + optionId</c>,
        /// on the stated premise that OPR keeps its ids stable. Measured against a live import on
        /// 2026-11-01 (#443): <b>section</b> ids are regenerated on every API response - 0 of 3,672
        /// matched across Grimdark Future alone - while <b>option</b> ids are perfectly stable, 48,155 of
        /// 48,155. So the old key paired nothing, and every option in every book came out as both
        /// removed and added: 48,170 of each, with the 2 genuine price changes buried in the middle. The
        /// tool was built to review a refresh and could not be used for one.
        /// </para>
        ///
        /// <para>
        /// Option ids are unique within a unit in 5,142 of 5,145 units. The three exceptions are one unit
        /// - Vampiric Undead's Werewolf Champion, in the three fantasy systems - whose two identically
        /// labelled "Replace Dual Hand Weapon" sections reuse the same five ids. The occurrence suffix
        /// pairs those first-with-first in section order, which is right as long as section order is
        /// stable, and it is: it is the order the book prints them in.
        /// </para>
        /// </summary>
        private static Dictionary<string, UpgradeOption> Options(RosterUnit unit)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var keyed = new Dictionary<string, UpgradeOption>(StringComparer.OrdinalIgnoreCase);

            foreach (UpgradeSection section in unit.Sections)
            {
                foreach (UpgradeOption option in section.Options)
                {
                    seen.TryGetValue(option.Id, out int n);
                    seen[option.Id] = n + 1;
                    keyed[n == 0 ? option.Id : $"{option.Id}#{n}"] = option;
                }
            }

            return keyed;
        }

        /// <summary>
        /// Pairs sections across two imports by label rather than id, for the same reason
        /// <see cref="Options"/> does: a section id is regenerated per response and pairs nothing.
        /// Labels repeat within a unit in 19% of units ("Upgrade with one", up to three times), so the
        /// occurrence index disambiguates - again in the book's own printed order.
        /// </summary>
        private static Dictionary<string, UpgradeSection> SectionsByLabel(RosterUnit unit)
        {
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var keyed = new Dictionary<string, UpgradeSection>(StringComparer.OrdinalIgnoreCase);

            foreach (UpgradeSection section in unit.Sections)
            {
                string label = section.Label ?? "";
                seen.TryGetValue(label, out int n);
                seen[label] = n + 1;
                keyed[n == 0 ? label : $"{label}#{n}"] = section;
            }

            return keyed;
        }

        private static void Number(string unit, string what, int a, int b, List<BookChange> changes)
        {
            if (a != b) changes.Add(new BookChange(unit, what, a.ToString(), b.ToString(), EBookChange.Changed));
        }

        private static string Size(RosterUnit u) =>
            u.MinModels == u.MaxModels
                ? $"{u.BaseModelCount} model(s), fixed"
                : $"{u.BaseModelCount} model(s) ({u.MinModels}-{u.MaxModels})";

        private static string Describe(RosterUnit u) =>
            $"{u.BasePointCost} pts, {Size(u)}, Q{u.Quality}+ D{u.Defense}+";

        private static string Describe(SaveLoad.WeaponFileEntry w)
        {
            var sb = new StringBuilder();
            if (w.Quantity != 1) sb.Append(w.Quantity).Append("x ");
            sb.Append(w.RangeInches > 0 ? $"{w.RangeInches}\", " : "melee, ");
            sb.Append('A').Append(w.Attacks);
            if (w.ArmorPenetration > 0) sb.Append(", AP(").Append(w.ArmorPenetration).Append(')');
            foreach (SaveLoad.SpecialRuleEntry r in w.SpecialRules.OrderBy(r => r.PrintableName, StringComparer.OrdinalIgnoreCase))
                sb.Append(", ").Append(r.PrintableName);
            return sb.ToString();
        }

        private static string Price(UpgradeOption o) =>
            o.CostUnpriced ? "no published price" : $"{o.Cost} pts";

        private static string Shape(UpgradeSection s) =>
            $"{s.Variant}/{s.Affects}" + (s.MaxApplications > 0 ? $", up to {s.MaxApplications}" : "")
            + (s.Variant == UpgradeVariant.PickN ? $", pick {s.MinPicks}-{s.MaxPicks}" : "");

        // -- The written report -----------------------------------------------------------------------

        /// <summary>
        /// The whole comparison as a Markdown report: a summary table first, so a glance says whether
        /// anything moved, then one section per book listing every difference.
        /// </summary>
        public static string Report(IReadOnlyList<BookComparison> comparisons, string title)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# " + title).AppendLine();

            int books = comparisons.Count;
            int touched = comparisons.Count(c => c.Any);
            int added = comparisons.Sum(c => c.Added);
            int removed = comparisons.Sum(c => c.Removed);
            int changed = comparisons.Sum(c => c.Changed);

            sb.AppendLine($"{books} book(s) compared. **{touched} differ**, {books - touched} identical.")
              .AppendLine()
              .AppendLine($"- **{changed}** thing(s) changed")
              .AppendLine($"- **{added}** added on the right")
              .AppendLine($"- **{removed}** only on the left")
              .AppendLine();

            if (touched == 0)
            {
                sb.AppendLine("Nothing differs at all. If you expected a change, that is the finding: ")
                  .AppendLine("the two sides are the same data.");
                return sb.ToString();
            }

            sb.AppendLine("## Summary").AppendLine()
              .AppendLine("| Book | Changed | Added | Only on the left |")
              .AppendLine("|---|---|---|---|");
            foreach (BookComparison c in comparisons.Where(c => c.Any).OrderByDescending(c => c.Changes.Count))
                sb.AppendLine($"| {c.BookName} | {c.Changed} | {c.Added} | {c.Removed} |");
            sb.AppendLine();

            foreach (BookComparison c in comparisons.Where(c => c.Any).OrderBy(c => c.BookName, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"## {c.BookName}").AppendLine()
                  .AppendLine($"Left: {c.LeftLabel}  -  Right: {c.RightLabel}").AppendLine()
                  .AppendLine("| Unit | What | Left | Right |")
                  .AppendLine("|---|---|---|---|");
                foreach (BookChange ch in c.Changes)
                    sb.AppendLine($"| {Cell(ch.Unit)} | {Cell(ch.What)} | {Cell(ch.Left)} | {Cell(ch.Right)} |");
                sb.AppendLine();
            }
            return sb.ToString();
        }

        /// <summary>A table cell: empty reads as a dash, and a pipe would break the row.</summary>
        private static string Cell(string text) =>
            text.Length == 0 ? "-" : text.Replace("|", "\\|");
    }
}
