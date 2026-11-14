using System.Text.RegularExpressions;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Each game's own core rulebook (Kai, 2026-11-14: "each game gets its own copy of the core rules, worded as
    /// its book says"). For every core rule the engine knows, for one game: the definition that game plays, that
    /// game's own Army Forge wording, and how the two are reconciled.
    ///
    /// <para>
    /// The engine still builds a rule worded the same everywhere ONCE (<see cref="CoreRuleCatalog"/>) and a game
    /// swaps in its own definition where its book differs (<see cref="SystemRuleVariants"/>, via
    /// <see cref="CoreRuleCatalog.CreateResolver"/>). What this adds is the complete per-game list, with nothing
    /// left implicit: every rule whose wording in a game differs from the shared wording must say here how that
    /// game plays it, and <c>GameRulebookTests</c> fails the build the moment Army Forge's text (re-harvested into
    /// <c>BuiltInAssets/OfficialRuleText.json</c>) disagrees with this list. A difference can no longer hide by
    /// nobody noticing it - before this, every game silently played Grimdark Future's version of anything not
    /// patched.
    /// </para>
    /// </summary>
    public static class GameRulebook
    {
        public enum EReading
        {
            /// <summary>The game's book words it exactly as the shared rule does.</summary>
            Same,
            /// <summary>The game's book doesn't have the rule (no Army Forge text for it in this game).</summary>
            NotInThisGame,
            /// <summary>The game plays its own definition under the same name (<see cref="SystemRuleVariants"/>).</summary>
            OwnDefinition,
            /// <summary>The definition is shared but the engine reads it per game (<see cref="CoreRulesProfile"/>
            /// or a stage switch) - <see cref="Entry.Note"/> names where.</summary>
            HandledPerGame,
            /// <summary>Worded differently, played the same: the difference is shortening, not a different rule.</summary>
            SameBehaviour,
            /// <summary>A real difference the engine does not play yet. Each one is on the to-do list.</summary>
            KnownGap,
        }

        /// <summary>One rule in one game's rulebook.</summary>
        public sealed record Entry(string Rule, EGameSystem System, EReading Reading, string Note,
            SpecialRuleDefinition Definition, string? GameText, string? SharedText);

        /// <summary>The games, in the order the lobby lists them.</summary>
        public static readonly IReadOnlyList<EGameSystem> Games = new[]
        {
            EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest,
            EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest,
        };

        // ── The ledger: every difference from the shared wording, and how this game plays it ─────────────────
        // Built 2026-11-14 by diffing every CoreRuleCatalog rule's Army Forge text (harvested 2026-10-31) across the
        // six games. Auras are covered by one rule below, not listed one by one.

        private static readonly EGameSystem[] Skirmish = { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish };
        private static readonly EGameSystem[] Quest = { EGameSystem.GFStarQuest, EGameSystem.AoFQuest };
        private static readonly EGameSystem[] SkirmishAndQuest =
            { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest, EGameSystem.AoFQuest };

        private static readonly List<(string Rule, EGameSystem[] Games, EReading Reading, string Note)> Ledger = new()
        {
            ("Slayer", SkirmishAndQuest, EReading.OwnDefinition, "Tough(9) in the skirmish games, Tough(27) in Quest (#463)."),
            ("Indirect", Quest, EReading.OwnDefinition, "No Hold needed; -1 to hit when shooting after moving (#463)."),
            ("Furious Buff", new[] { EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest, EGameSystem.AoFQuest }, EReading.OwnDefinition,
                "Up to 3 units; once per game in Star Quest; AoF: Quest's \"nearest\" not enforced (house rule) (#463)."),
            ("Fanatic", new[] { EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish }, EReading.OwnDefinition,
                "AoF: AP(+1) charging for the Hero and its unit; Skirmish: the Hero alone (#463)."),

            ("Blast", SkirmishAndQuest, EReading.HandledPerGame,
                "CoreRulesProfile.BlastSplitsBetweenNearbyUnits - hits split between nearby enemy units."),
            ("Fear", Quest, EReading.HandledPerGame, "CoreRulesProfile.MeleeResultsDecideMorale - counts toward the Morale Test, not who won."),
            ("Scout", Quest, EReading.HandledPerGame, "CoreRulesProfile.ScoutIsSimpleRedeploy - deploy fully within 12\" (Army Forge's number)."),
            ("Hero", SkirmishAndQuest, EReading.HandledPerGame,
                "CoreRulesProfile.EveryHeroLendsMoraleQuality / HeroJoinToughCap - every Hero lends Quality within 6\"; joining per Kai's call."),
            ("Transport", SkirmishAndQuest, EReading.HandledPerGame,
                "CoreRulesProfile Transport caps - Tough x3 (skirmish) / x9 (Quest) scale. Army Forge's text for these games is cut short."),
            ("Caster", Quest, EReading.HandledPerGame, "Quest casters: heroes spend Power on the roll (QuestActions) - same tokens rule."),
            ("Ambush", Quest, EReading.HandledPerGame,
                "Quest has no alternation / can't-seize clause; Quest enemies never use the standard Ambush arrival (waves bring them)."),

            ("Tough", SkirmishAndQuest, EReading.SameBehaviour,
                "These books print only \"killed after X wounds\"; the wound-assignment clause is the core rules' general allocation."),

            ("Flying", SkirmishAndQuest, EReading.KnownGap,
                "Dangerous terrain it starts in: done (CoreRulesProfile.FlyingKeepsTerrainItStartsIn). Not done: Difficult terrain it "
                + "starts in, and auto-passing jumping rolls (no jumping until multi-level maps)."),
            ("Strider", SkirmishAndQuest, EReading.KnownGap, "Halves jumping dice - no jumping until multi-level maps; Difficult terrain part is played."),
        };

        /// <summary>The auras of the four wide games: "This model's unit and up to 2 (nearest) friendly units picked
        /// before the start of the game ..." - played by AuraConferral (build 1.96).</summary>
        private static readonly Regex WideAura = new(
            "^This model's unit and up to 2 (nearest )?friendly units picked before the start of the game ", RegexOptions.Compiled);

        /// <summary>Every core rule as <paramref name="system"/> plays it.</summary>
        public static IReadOnlyList<Entry> For(EGameSystem system)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver(system);
            string slug = GameSystemInfo.Slug(system);
            var entries = new List<Entry>();
            foreach (SpecialRuleDefinition shared in CoreRuleCatalog.All)
            {
                SpecialRuleDefinition played = resolver.TryResolve(shared.Name, out ResolvedRule r) ? r.Definition : shared;
                string? own = Clean(OfficialRuleText.OwnText(shared.Name, slug));
                string? reference = SharedText(shared.Name);
                (EReading reading, string note) = Classify(shared.Name, system, own, reference);
                entries.Add(new Entry(shared.Name, system, reading, note, played, own, reference));
            }
            return entries;
        }

        /// <summary>How <paramref name="system"/> reads <paramref name="rule"/>, given its own and the shared wording.</summary>
        public static (EReading Reading, string Note) Classify(string rule, EGameSystem system, string? own, string? reference)
        {
            foreach (var row in Ledger)
                if (row.Rule == rule && row.Games.Contains(system))
                    return (row.Reading, row.Note);
            if (own == null) return (EReading.NotInThisGame, "");
            if (own == reference) return (EReading.Same, "");
            if (rule.EndsWith("Aura", StringComparison.Ordinal) && CoreRulesProfile.For(system).CommandGroupExtraPicks > 0
                && WideAura.IsMatch(own))
                return (EReading.HandledPerGame, "AuraConferral - its own unit and up to 2 picked (Quest enemies: nearest) units (1.96).");
            return (EReading.KnownGap, "UNREVIEWED: this game's wording differs and nobody has said how it plays.");
        }

        /// <summary>The ledger rows, for the test that checks them against the variants and the text.</summary>
        public static IEnumerable<(string Rule, EGameSystem System, EReading Reading)> LedgerRows =>
            Ledger.SelectMany(r => r.Games.Select(g => (r.Rule, g, r.Reading)));

        /// <summary>
        /// The shared wording a rule is compared against: Grimdark Future's, or - for a rule Grimdark Future doesn't
        /// print (Counter-Attack, Bane in melee) - the wording most of the games that do print it share.
        /// </summary>
        public static string? SharedText(string rule)
        {
            string? gf = Clean(OfficialRuleText.OwnText(rule, GameSystemInfo.Slug(EGameSystem.GrimdarkFuture)));
            if (gf != null) return gf;
            return Games.Select(g => Clean(OfficialRuleText.OwnText(rule, GameSystemInfo.Slug(g))))
                .Where(t => t != null)
                .GroupBy(t => t).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault();
        }

        private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
