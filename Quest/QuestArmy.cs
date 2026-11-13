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
        /// <summary>Star Quest's party faction name, kept as the default so every party file saved
        /// before #418 still reads back the same.</summary>
        public const string PartyFaction = "Star Quest Party";

        /// <summary>AoF: Quest's party faction name (#418).</summary>
        public const string AoFPartyFaction = "AoF Quest Party";

        /// <summary>
        /// Whether an army is a hero party, of EITHER Quest system. Anything that asks "is this the party?"
        /// must use this rather than comparing to <see cref="PartyFaction"/>: the lobby's launch gate did,
        /// so an AoF: Quest party (faction "AoF Quest Party") never counted and AoF: Quest could not be
        /// launched at all (Kai, 2026-11-05).
        /// </summary>
        public static bool IsPartyFaction(string? faction) =>
            faction == PartyFaction || faction == AoFPartyFaction;

        /// <summary>The party faction name for a Quest system.</summary>
        public static string PartyFactionFor(EGameSystem system) =>
            system == EGameSystem.AoFQuest ? AoFPartyFaction : PartyFaction;

        /// <summary>
        /// The army-book system a Quest system's heroes read their faction rules and spells from:
        /// its OWN enemy books. #418 - both Quest systems have them now (Star Quest 54, AoF: Quest 46),
        /// and a hero resolving its rules against another system's edition is the same class of fault
        /// as pricing it off another system's book.
        /// </summary>
        public static string BookSystemFor(EGameSystem system) =>
            system == EGameSystem.AoFQuest ? GameSystems.AoFQuest : GameSystems.GFStarQuest;

        /// <summary>
        /// The army book a Star Quest hero's rules and spells are read from, out of the whole bundled
        /// library: the faction's <b>Star Quest</b> edition where there is one, and only then any other
        /// system's.
        /// <para>
        /// #414. Both party builders used to take <c>FirstOrDefault(MatchesFactionName)</c>, and with 242
        /// books across five systems in the library that is whichever edition the scan happened to reach
        /// first - in practice Grimdark Future's. The same named rule can be worded differently per
        /// system, so a Star Quest hero was resolving its rules against the wrong book, which is the same
        /// class of fault as pricing it off the wrong book. Falling back to another system is still right
        /// when Star Quest has no edition of that faction: a definition from the parent system beats no
        /// definition at all, and the hero's own stats come from the hero dataset either way.
        /// </para>
        /// </summary>
        public static BookFile? BookForHeroFaction(IEnumerable<BookFile> library, string? factionName,
            EGameSystem system = EGameSystem.GFStarQuest)
        {
            List<BookFile> matches = library.Where(b => b.MatchesFactionName(factionName)).ToList();
            string preferred = BookSystemFor(system);
            return matches.FirstOrDefault(b => GameSystems.SameSystem(b.GameSystem, preferred))
                ?? matches.FirstOrDefault();
        }

        /// <param name="bookForFaction">The army book of a faction (for the faction's special rule
        /// definitions and spells), or null when there is none. Callers hand this in because the library
        /// lives on the client side; <see cref="BookForHeroFaction"/> is the lookup they should use.</param>
        public static BuiltArmyFile Build(IReadOnlyList<QuestHero> heroes,
            Func<string, BookFile?> bookForFaction,
            EGameSystem system = EGameSystem.GFStarQuest)
        {
            var army = new BuiltArmyFile
            {
                Name = "Party: " + string.Join(", ", heroes.Select(h => h.Name)),
                Faction = PartyFactionFor(system),
                // #412/#414: a Star Quest party is a STAR QUEST army, and the launch gate enforces that.
                // This said GrimdarkFuture, which was harmless only while Star Quest was played on
                // Grimdark Future's books. Now that GFStarQuest is its own army source, a party stamped
                // grimdark-future fails its own lobby's filter - "Grimdark Future army, but this lobby
                // takes GF: Star Quest armies only" - so the LAUNCH button was disabled by the party
                // itself and no Star Quest game could be started from the GUI at all. Pinned by
                // QuestArmyGameSystemTests.
                // #418: and an AoF: Quest party is an AoF: QUEST army, for the same reason - stamping
                // one GFStarQuest would fail an AoF: Quest lobby's filter exactly the way a
                // grimdark-future stamp failed Star Quest's.
                GameSystem = BookSystemFor(system),
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
                // The hero's unit is named after the HERO ("Krrek"), so its model is found through its look:
                // the picked file, or the base hero's folder (its first version) when none was picked.
                ModelLooks = new List<string?> { hero.Look ?? QuestHeroLook(hero) },
            };
        }

        /// <summary>"Faction/Base Hero/" - the base hero's model folder, meaning its first version.</summary>
        public static string QuestHeroLook(QuestHero hero) => $"{hero.Faction}/{hero.BaseHeroName}/";

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
