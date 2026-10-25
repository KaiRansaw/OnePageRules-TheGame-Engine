using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FDG.Quest
{
    /// <summary>
    /// Turns a Star Quest party (2-4 <see cref="QuestHero"/>es) into an ordinary army file the engine can play:
    /// each hero becomes a one-model unit with its Quality, Defense, Tough, rules, gear and weapons, and carries
    /// its hero sheet along (<see cref="UnitFileEntry.QuestHeroJson"/>) so the mission can read and update its
    /// Star Quest stats and XP.
    /// </summary>
    public static class QuestArmy
    {
        public const string PartyFaction = "Star Quest Party";

        /// <param name="bookForFaction">The Grimdark Future army book of a faction (for the faction's special
        /// rule definitions and spells), or null when there is none.</param>
        public static BuiltArmyFile Build(IReadOnlyList<QuestHero> heroes, Func<string, BookFile?> bookForFaction)
        {
            var army = new BuiltArmyFile
            {
                Name = "Party: " + string.Join(", ", heroes.Select(h => h.Name)),
                Faction = PartyFaction,
                GameSystem = GameSystems.GrimdarkFuture,
                PointsLimit = heroes.Sum(h => h.Points),
            };
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QuestHero hero in heroes)
            {
                BookFile? book = bookForFaction(hero.Faction);
                army.Units.Add(ToUnit(hero, book));
                if (book == null || !seen.Add(book.Faction)) continue;
                foreach (var def in book.RuleDefinitions)
                    if (!army.RuleDefinitions.Any(d => d.Name == def.Name)) army.RuleDefinitions.Add(def);
                if (hero.Caster > 0)
                    foreach (var spell in book.Spells)
                        if (!army.Spells.Any(s => s.Name == spell.Name)) army.Spells.Add(spell);
            }
            return army;
        }

        public static UnitFileEntry ToUnit(QuestHero hero, BookFile? book)
        {
            var rules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero") };
            foreach (string r in hero.Rules)
            {
                if (r.StartsWith("Tough(", StringComparison.OrdinalIgnoreCase)) continue;
                if (r.StartsWith("Caster(", StringComparison.OrdinalIgnoreCase)) continue;
                Add(rules, SpecialRuleEntryParser.Parse(r));
            }
            // Gear is a bundle of rules: "Combat Shield (Shielded)" gives the hero Shielded.
            foreach (string g in hero.Gear)
                foreach (string inner in GearRules(g))
                    Add(rules, SpecialRuleEntryParser.Parse(inner));
            if (hero.Tough > 1) Add(rules, new SpecialRuleEntry_CoreNumeric("Tough", hero.Tough));
            if (hero.Caster > 0) Add(rules, new SpecialRuleEntry_CoreNumeric("Caster", hero.Caster));

            string faction = book?.Faction ?? hero.Faction;
            var weapons = new List<WeaponFileEntry>();
            foreach (QuestWeapon w in hero.Weapons)
            {
                var entry = new WeaponFileEntry
                {
                    Name = w.Name,
                    Quantity = Math.Max(1, w.Count),
                    RangeInches = w.Range,
                    Attacks = w.Attacks,
                };
                foreach (string raw in w.Rules)
                {
                    SpecialRuleEntry rule = SpecialRuleEntryParser.Parse(raw);
                    if (rule is SpecialRuleEntry_CoreNumeric { Name: "AP" } ap) entry.ArmorPenetration = ap.NumericValue;
                    else entry.SpecialRules.Add(rule);
                }
                entry.EffectSet = WeaponEffectAssigner.Match(faction, entry, book?.GameSystem);
                weapons.Add(entry);
            }

            return new UnitFileEntry
            {
                Name = hero.Name,
                ModelCount = 1,
                Quality = hero.Quality,
                Defense = hero.Defense,
                PointCost = hero.Points,
                Base = DefaultBaseEstimator.Estimate(rules, out _),
                SpecialRules = rules,
                Weapons = weapons,
                QuestHeroJson = hero.ToJson(),
            };
        }

        private static void Add(List<SpecialRuleEntry> rules, SpecialRuleEntry rule)
        {
            if (!rules.Contains(rule)) rules.Add(rule);
        }

        /// <summary>The rules inside a gear label: "Wings (Ambush, Flying)" -> Ambush, Flying. Weapon profiles
        /// inside gear ("Leaping Tiger (A3, AP(2))") are skipped.</summary>
        public static IEnumerable<string> GearRules(string gear)
        {
            int open = gear.IndexOf(" (", StringComparison.Ordinal);
            if (open < 0 || !gear.EndsWith(")")) yield break;
            string inner = gear.Substring(open + 2, gear.Length - open - 3);
            int depth = 0, start = 0;
            var parts = new List<string>();
            for (int i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '(') depth++;
                else if (inner[i] == ')') depth--;
                else if (inner[i] == ',' && depth == 0) { parts.Add(inner[start..i]); start = i + 1; }
            }
            parts.Add(inner[start..]);
            foreach (string p in parts)
            {
                string t = p.Trim();
                if (t.Length == 0 || t.Contains("(A", StringComparison.Ordinal)) continue;
                yield return t;
            }
        }
    }
}
