using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #423 - the <b>Special Rules</b> section of <b>Solo &amp; Co-Op Rules v3.6.0</b>: the clauses that
    /// override the general shooting and action rules when an AI unit carries a particular rule. Quoted
    /// from the books rather than summarised, because that is the whole reason this file exists.
    ///
    /// <para>
    /// <b>Indirect:</b> "AI units with Indirect weapons that are in range of enemies always use Hold
    /// actions and shoot."<br/>
    /// <b>Relentless:</b> "AI units with Relentless that have weapons that are in range of enemies always
    /// use Hold and shoot."
    /// </para>
    /// <para>
    /// <b>Blast:</b> "...always target multi-model units <b>without Tough</b> first, and multi-model units
    /// with Tough second" (GF, AoF) / "...always target multi-model units first, and single-model units
    /// second" (Firefight, AoF: Skirmish).<br/>
    /// <b>Deadly:</b> "...always target single-model units <b>with Tough</b> first, and multi-model units
    /// with Tough second" (GF, AoF) / "...always target single-model units first, and multi-model units
    /// second" (Firefight, AoF: Skirmish).
    /// </para>
    /// <para>
    /// <b>Why these are not in the general targeting code.</b> The books state them in their own section,
    /// after the shooting clause, and keyed on the rule rather than on the unit - so a Blast weapon's
    /// priority applies to <i>that weapon's</i> shot and not to the unit's other guns. The general keys
    /// (in the open before in Cover, not-yet-activated before activated, then closest) still decide the
    /// order <i>within</i> a priority band; this only decides the bands. Reading the specific clause as
    /// outranking the general one is the ordinary way to read a rulebook, and it is the only reading under
    /// which these clauses do anything at all - "closest first" would otherwise always win.
    /// </para>
    /// <para>
    /// <b>Two definitions the books leave to the reader</b>, both recorded rather than buried:
    /// </para>
    /// <list type="bullet">
    /// <item><b>"multi-model unit"</b> is read as more than one <b>living</b> model. A five-model squad shot
    /// down to one is one body on the table, and the clause exists to point Blast at bodies and Deadly at
    /// single big models; counting the dead would aim Blast at a unit it can no longer spread hits across
    /// (Blast's own rule caps hits at the models in the target).</item>
    /// <item><b>"with Tough"</b> is read as most living models having Tough, the same majority the rules
    /// engine uses everywhere else for "most models". Tough is uniform across a unit in the whole corpus,
    /// so the two readings only differ for a unit with a joined Hero - and there the majority answer is
    /// the one the rest of the engine would give.</item>
    /// </list>
    /// </summary>
    public static class SoloAiSpecialRuleClauses
    {
        /// <summary>The rule names this file keys on, matched the way <c>AircraftRules</c> matches: by
        /// name, case-insensitively, against both the resolved definition and the name the army asked
        /// for, so an army's own embedded copy of the rule counts.</summary>
        private const string Indirect = "Indirect";
        private const string Relentless = "Relentless";
        private const string Blast = "Blast";
        private const string Deadly = "Deadly";
        private const string Tough = "Tough";

        /// <summary>
        /// "AI units with Indirect weapons that are in range of enemies always use Hold actions and shoot"
        /// - and the same sentence for Relentless. True when this unit must therefore stand still and fire
        /// instead of consulting the decision tree.
        ///
        /// <para>
        /// The range tested is the range of the weapon that <i>carries</i> the rule, not the unit's longest
        /// gun: a unit whose Indirect mortar is out of range has no Indirect shot to take, and the clause
        /// does not apply to it. Relentless is unit-scoped in the corpus ("AI units with Relentless that
        /// have weapons that are in range"), so there any ranged weapon in range triggers it.
        /// </para>
        /// <para>
        /// This matters more than it looks for Indirect specifically: since OPR 3.6.0 the rule only ignores
        /// line of sight <b>on a Hold action</b>, so an AI that walked and fired threw the rule away. The
        /// clause and the rule were written to fit each other.
        /// </para>
        /// </summary>
        public static bool MustHoldAndShoot(ITableState table, PlayerID me, IUnit unit)
        {
            List<IUnit> enemies = AiCombatMath.Enemies(table, me);
            if (enemies.Count == 0) return false;

            float indirectRange = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                foreach (IWeapon weapon in model.Weapons)
                {
                    if (weapon.RangeInches <= 0f) continue;
                    if (WeaponHasRule(weapon, Indirect)) indirectRange = Math.Max(indirectRange, weapon.RangeInches);
                }
            }

            // Relentless is carried by the UNIT and speaks of "weapons that are in range", so any ranged
            // weapon it has will do.
            float relentlessRange = UnitHasRule(unit, Relentless) ? AiCombatMath.MaxRange(unit) : 0f;

            float reach = Math.Max(indirectRange, relentlessRange);
            if (reach <= 0f) return false;

            return enemies.Any(enemy => AiCombatMath.Distance(unit, enemy) <= reach);
        }

        /// <summary>
        /// The priority band a target falls into for the weapon being fired: 0 is "target these first",
        /// and a weapon with neither Blast nor Deadly puts every target in band 0 so the general keys
        /// decide alone. Lower sorts first.
        /// </summary>
        public static int TargetBand(SoloCoOpProfile profile, IWeapon weapon, IUnit target)
        {
            bool blast = WeaponHasRule(weapon, Blast);
            bool deadly = WeaponHasRule(weapon, Deadly);
            if (!blast && !deadly) return 0;

            bool multiModel = LivingModels(target) > 1;
            bool hasTough = MostLivingModelsAreTough(target);

            // A weapon with both (the corpus has a few) is ranked by Blast: it is the rule whose own text
            // is about spreading hits across models, so it is the one that should pick the target. Recorded
            // rather than silently ordered - the books say nothing about a weapon carrying both.
            if (blast)
            {
                return profile.Blast switch
                {
                    EBlastPriority.MultiModelWithoutToughFirst =>
                        multiModel && !hasTough ? 0
                        : multiModel ? 1
                        : 2,
                    _ => multiModel ? 0 : 1,
                };
            }

            return profile.Deadly switch
            {
                EDeadlyPriority.SingleModelWithToughFirst =>
                    !multiModel && hasTough ? 0
                    : multiModel && hasTough ? 1
                    : 2,
                _ => !multiModel ? 0 : 1,
            };
        }

        private static int LivingModels(IUnit unit) => unit.Models.Count(m => m.GetIsAlive());

        /// <summary>
        /// "Most living models have Tough" - the majority the rules engine uses for every other "most
        /// models" clause. Read off the rule by name as well as off max wounds, because a unit's Tough can
        /// arrive either way (an army book's embedded rating, or a model whose wounds were already set).
        /// </summary>
        private static bool MostLivingModelsAreTough(IUnit unit)
        {
            List<IModel> living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return false;

            int tough = living.Count(m => m.TotalWounds > 1f || ModelHasRule(m, Tough));
            return tough * 2 > living.Count;
        }

        private static bool WeaponHasRule(IWeapon weapon, string ruleName) =>
            weapon.RuleDefinitions.Any(rule => Matches(rule, ruleName));

        private static bool UnitHasRule(IUnit unit, string ruleName) =>
            unit.RuleDefinitions.Any(rule => Matches(rule, ruleName))
            || unit.Models.Any(model => model.GetIsAlive() && ModelHasRule(model, ruleName));

        private static bool ModelHasRule(IModel model, string ruleName) =>
            model.RuleDefinitions.Any(rule => Matches(rule, ruleName));

        private static bool Matches(Rules.Dispatch.ResolvedRule rule, string ruleName) =>
            string.Equals(rule.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase);
    }
}
