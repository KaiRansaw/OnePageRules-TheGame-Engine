using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.BuiltInAssets;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #420 - the OFFICIAL wording of every special rule, as OnePageRules publishes it, keyed by game
    /// system.
    ///
    /// <para>
    /// <b>Why this exists.</b> <see cref="SpecialRuleDefinition.Description"/> was hand-written: 101
    /// one-line summaries, authored alongside each rule's implementation. Measured against OPR's own
    /// text on 2026-10-31, <b>2 of the 38 that could be checked matched; 36 did not</b>, and the misses
    /// were not stylistic. Ambush lost "players alternate in placing"; Blast lost "ignores Cover";
    /// Caster lost the 6-token cap; Aircraft's summary invented a "-12&quot; range, -1 to hit" clause the
    /// book does not have. A summary that drifts is indistinguishable, from the outside, from a rule that
    /// is built wrong - and two of the first three checked turned out to be both, Impact and Ravage having
    /// each lost "unless this unit is Fatigued" from the code as well as from the text.
    /// </para>
    /// <para>
    /// <b>A warning for whoever reads this next, because this very comment got it wrong once.</b> An
    /// earlier draft asserted here that Fanatic was "built as an entirely different rule". It is not, and
    /// an audit later cited this comment back as evidence that it was. <b>Fanatic is two rules sharing a
    /// name:</b> the army book rule - "after this model is deployed, it may be placed anywhere fully
    /// within 9&quot; of its position, over 9&quot; away from non-Vanguard enemy units", verified against
    /// the Soul Snatcher Cults book at Army Forge game systems 2, 3 and 9 - and the campaign Hero trait,
    /// "the Hero and its unit get AP(+1) in melee when charging". The engine implements the book rule,
    /// which is the one a unit on the table actually carries, and is right to; it is only missing the
    /// enemy standoff. Five other names collide the same way: Agile, Berserker, Crazed, Destroyer and
    /// Runner. <b>Diff the text, then read the code, then report.</b> A wrong description is a reason to
    /// go and look, never a finding on its own.
    /// </para>
    /// <para>
    /// Owner's instruction, 2026-10-31: "<i>I want everything to be 1:1 in wording as the official book.
    /// none of this 'summarize'.</i>" So the displayed text is now OPR's, verbatim.
    /// </para>
    /// <para>
    /// <b>Where it comes from.</b> Not retyped - harvested by <c>scripts/harvest-rule-text.py</c> from
    /// the two Army Forge endpoints the importer already uses: <c>/api/rules/common/{id}</c> (the ~39
    /// core rules and ~36 Hero traits, worded per system) and each book's <c>specialRules</c> (every
    /// book-level rule, plus the core-tagged ones the common endpoint omits - Ravage among them). The
    /// game has been downloading this text all along and discarding it. Regenerate the asset when OPR
    /// publishes a new ruleset; the file's <c>_meta</c> block records when it was taken.
    /// </para>
    /// <para>
    /// <b>Per system, deliberately.</b> One shared string would be wrong: the same rule name is worded
    /// differently in different systems, and those differences are real rules differences. Star Quest's
    /// Fear counts toward the Morale Test where Grimdark Future's counts toward who won the melee; its
    /// Blast splits hits between units; its Tough is three sentences shorter. Flattening them is the
    /// mistake <c>CoreRuleCatalog</c> already makes once (one shared catalog written to Grimdark
    /// Future's wording) and this must not repeat it.
    /// </para>
    /// <para>
    /// <b>This changes no behaviour.</b> It is the text a player reads, nothing else. Where the wording
    /// revealed that an implementation is wrong, that is fixed in the rule itself and tested there -
    /// correcting the label on a rule that does the wrong thing would make the fault harder to find,
    /// not easier.
    /// </para>
    /// </summary>
    public static class OfficialRuleText
    {
        public const string ASSET_PATH = "FDG.BuiltInAssets.OfficialRuleText.json";

        private static readonly object Gate = new();
        private static Dictionary<string, Dictionary<string, string>>? _bySystem;
        private static string? _harvested;

        /// <summary>
        /// Where a system falls back when it has no text of its own for a rule.
        ///
        /// <para>
        /// A fallback is a last resort, not a default: it only ever fires for a rule the system's own
        /// data does not mention at all, which in practice means a rule carried by a book of another
        /// system entirely. Showing the parent system's wording there is better than showing nothing,
        /// and strictly better than the hand-written summary it replaces - but it is NOT a licence to
        /// assume the parent's wording is correct for a rule both systems have. When both have it, the
        /// system's own entry wins, because that is the whole point of keying this per system.
        /// </para>
        /// </summary>
        private static readonly Dictionary<string, string> FallsBackTo = new(StringComparer.OrdinalIgnoreCase)
        {
            [GameSystems.GFFirefight] = GameSystems.GrimdarkFuture,
            [GameSystems.GFStarQuest] = GameSystems.GrimdarkFuture,
            [GameSystems.AoFSkirmish] = GameSystems.AgeOfFantasy,
            [GameSystems.AoFQuest] = GameSystems.AgeOfFantasy,
        };

        /// <summary>The day the shipped text was taken from Army Forge, for staleness checks.</summary>
        public static string Harvested
        {
            get { EnsureLoaded(); return _harvested ?? "unknown"; }
        }

        /// <summary>The systems the shipped asset carries text for.</summary>
        public static IReadOnlyCollection<string> Systems
        {
            get { EnsureLoaded(); return _bySystem!.Keys; }
        }

        /// <summary>
        /// OPR's own wording for <paramref name="ruleName"/> in <paramref name="systemSlug"/>, or null
        /// when OPR publishes none under that name.
        ///
        /// <para>
        /// Null is the normal answer for an internally generated rule - the engine authors helper
        /// definitions with names like "AP(+1) in melee", "Trait: Agile" and "Shred when shooting" that
        /// exist to carry one effect and are not rules a player ever looks up. Those keep their authored
        /// <see cref="SpecialRuleDefinition.Description"/>, which for them is the only text there is.
        /// </para>
        /// </summary>
        public static string? Text(string? ruleName, string? systemSlug)
        {
            if (string.IsNullOrWhiteSpace(ruleName)) return null;
            EnsureLoaded();

            string slug = GameSystems.Normalize(systemSlug);
            for (int hop = 0; hop < 4; hop++)
            {
                if (_bySystem!.TryGetValue(slug, out Dictionary<string, string>? forSystem)
                    && forSystem.TryGetValue(ruleName.Trim(), out string? text))
                    return text;

                if (!FallsBackTo.TryGetValue(slug, out string? parent)) return null;
                slug = parent;
            }

            return null;
        }

        /// <summary>
        /// The text to show a player for a rule: OPR's wording where it exists, the authored
        /// description otherwise. <b>Every display site should call this rather than reading
        /// <see cref="SpecialRuleDefinition.Description"/> directly</b>, so there is one answer to
        /// "what does this rule say" and one place to change it.
        /// </summary>
        public static string Display(SpecialRuleDefinition? definition, string? systemSlug)
        {
            if (definition == null) return string.Empty;
            return Text(definition.Name, systemSlug) ?? definition.Description;
        }

        /// <summary>Same, for a resolved rule - which carries its definition and nothing else that matters here.</summary>
        public static string Display(ResolvedRule? rule, string? systemSlug) =>
            rule == null ? string.Empty : Display(rule.Definition, systemSlug);

        /// <summary>
        /// Replaces the shipped text, for tests that need a known corpus. Pass null to go back to the
        /// shipped asset.
        /// </summary>
        public static void OverrideForTests(Dictionary<string, Dictionary<string, string>>? bySystem)
        {
            lock (Gate)
            {
                _bySystem = bySystem;
                if (bySystem == null) _harvested = null;
            }
        }

        private static void EnsureLoaded()
        {
            if (_bySystem != null) return;
            lock (Gate)
            {
                if (_bySystem != null) return;

                var loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                byte[] bytes = BuiltInAssetHelper.GetEmbeddedResource(ASSET_PATH);
                using JsonDocument doc = JsonDocument.Parse(bytes);

                foreach (JsonProperty system in doc.RootElement.EnumerateObject())
                {
                    if (system.NameEquals("_meta"))
                    {
                        if (system.Value.TryGetProperty("harvested", out JsonElement when))
                            _harvested = when.GetString();
                        continue;
                    }

                    var forSystem = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty rule in system.Value.EnumerateObject())
                    {
                        string? text = rule.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) forSystem[rule.Name] = text!;
                    }

                    loaded[system.Name] = forSystem;
                }

                _bySystem = loaded;
            }
        }
    }
}
