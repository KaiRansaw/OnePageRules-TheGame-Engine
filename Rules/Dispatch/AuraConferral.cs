using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Tokens;
using FDG.Stages;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// What a unit can hand to OTHER friendly units in the four games whose books print the wide wording
    /// (GF: Firefight, AoF: Skirmish, GF: Star Quest, AoF: Quest - <see cref="CoreRulesProfile.CommandGroupExtraPicks"/>).
    ///
    /// <para>
    /// #439 did this for Banner and Musician. Army Forge's text (OfficialRuleText.json, 3.6.0 / 2.1.0) shows it was
    /// never only those two: in those four systems <b>every aura</b> reads <i>"This model's unit and up to 2 friendly
    /// units picked before the start of the game get X. If this model is killed, picked models that are deployed lose
    /// the rule."</i> - 103 auras in Firefight alone - where Grimdark Future and Age of Fantasy print <i>"This model
    /// and its unit get X"</i>. So a conferral is either a Command Group upgrade (it hands over ITSELF) or an aura
    /// (a rule whose passive is <see cref="Effect.Aura"/>, handing over the rule the aura names).
    /// </para>
    ///
    /// <para>
    /// The Quest games add "up to 2 <b>nearest</b> friendly units" to most enemy-side auras. Kai, 2026-11-13: for a
    /// Quest enemy that means <b>nearest when its wave arrives</b> - see <see cref="GrantToNearest"/>. A player's
    /// heroes pick on the same screen as every other system.
    /// </para>
    /// </summary>
    public static class AuraConferral
    {
        /// <summary>One thing a bearer can confer: the rule it carries (for the prompt and the report) and the rule a
        /// picked unit receives. The same name for Banner/Musician; "Furious Aura" -> "Furious" for an aura.</summary>
        public sealed record Conferral(string Source, string Granted);

        /// <summary>Every conferral <paramref name="unit"/> carries, unit-level and model-level (a joined Hero's own
        /// rules sit on its model - see <see cref="PickCommandGroupUnitsStage.CarriesRule"/>). Distinct by granted rule.</summary>
        public static IReadOnlyList<Conferral> For(IUnit unit)
        {
            var found = new List<Conferral>();
            foreach (string name in CoreRulesProfile.ConferringCommandGroupRules)
                if (PickCommandGroupUnitsStage.CarriesRule(unit, name)) found.Add(new Conferral(name, name));

            IEnumerable<ResolvedRule> rules = unit.RuleDefinitions.Concat(unit.Models.SelectMany(m => m.RuleDefinitions));
            foreach (ResolvedRule rule in rules)
                foreach (HookEntry entry in rule.Definition.Passive)
                    if (entry.Effect is Effect.Aura aura)
                        found.Add(new Conferral(rule.Definition.Name, aura.RuleName));

            return found
                .GroupBy(c => c.Granted, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
        }

        /// <summary>A Quest enemy: its auras go to the nearest units when its wave lands, not to picks made before
        /// the game (nothing of the enemy side is on the table then, and nobody is there to pick).</summary>
        public static bool IsQuestEnemy(IGameContext context, IUnit unit) =>
            GameSystemInfo.IsQuestMode(context.Settings.GameMode) && unit is UnitData data && !data.IsQuestHero;

        /// <summary>
        /// A unit that may receive <paramref name="granted"/> from <paramref name="bearer"/>: another living unit of the
        /// bearer's own army ("friendly", read as your own - #439's house rule) that neither carries the rule nor
        /// already holds a granted copy (the modifier sinks ADD, so a second copy would double the bonus).
        /// </summary>
        public static bool CanReceive(IUnit bearer, IUnit candidate, string granted) =>
            candidate.ID != bearer.ID
            && candidate.PlayerID == bearer.PlayerID
            && candidate.GetIsAlive()
            && !PickCommandGroupUnitsStage.CarriesRule(candidate, granted)
            && !PickCommandGroupUnitsStage.HoldsGrantFor(candidate, granted);

        /// <summary>
        /// Quest enemies (Kai, 2026-11-13): when <paramref name="bearer"/> is placed with its wave, each of its
        /// conferrals goes to the <paramref name="count"/> nearest friendly units already on the table. Measured
        /// centre to centre between unit middles; ties broken by table order so every machine agrees.
        /// </summary>
        public static void GrantToNearest(IGameContext context, IUnit bearer, int count)
        {
            if (count <= 0 || !bearer.GetIsAlive()) return;
            IReadOnlyList<Conferral> conferrals = For(bearer);
            if (conferrals.Count == 0) return;

            Position from = Middle(bearer);
            List<UnitData> onTable = context.TableState.Units.Objects.OfType<UnitData>()
                .Where(u => u.GetIsOnBattlefield() && u.GetIsAlive())
                .ToList();

            foreach (Conferral c in conferrals)
            {
                List<UnitData> nearest = onTable
                    .Where(u => CanReceive(bearer, u, c.Granted))
                    .Select((u, i) => (Unit: u, Index: i, Distance: Position.GetDistance2D(from, Middle(u))))
                    .OrderBy(x => x.Distance).ThenBy(x => x.Index)
                    .Take(count)
                    .Select(x => x.Unit)
                    .ToList();
                if (nearest.Count == 0) continue;

                foreach (UnitData u in nearest)
                    PickCommandGroupUnitsStage.Grant(u, c.Granted, bearer);
                context.Log($"{c.Source} on {bearer.Name} also covers the nearest: {string.Join(" and ", nearest.Select(u => u.Name))}.");
            }
        }

        private static Position Middle(IUnit unit)
        {
            List<IModel> alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return unit.Models.First().Position;
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }
    }
}
