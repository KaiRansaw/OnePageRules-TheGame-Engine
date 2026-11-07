using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FDG.Data;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// A non-logging rule evaluator for read-only questions asked outside the state machine - "how far does
    /// this unit Advance?" from the bot's decision tree or the unit card - that sees the SAME rules the game
    /// does, including each army's own book-supplement definitions.
    ///
    /// <para>
    /// <b>Why this exists.</b> Those callers used to build their evaluator on the core catalog alone. A unit's
    /// own attached rules carry their definitions with them, so that mostly worked - but a rule GRANTED by an
    /// aura (Highborn Boost from a High Elf hero, Self-Repair Boost, Versatile Attack (Piercing), the Vinci
    /// Tech variants...) is looked up BY NAME, and the core catalog has never heard of a supplement name. So
    /// every such grant read as "does nothing" to the bot's reach and to the movement shown on the unit card,
    /// while the real game, whose resolver had the army definitions registered, applied it. Found in the bot
    /// audit (2026-11-07) as "Granted rule ... has no definition in the registry" warnings.
    /// </para>
    /// <para>
    /// The resolver is rebuilt from the table's <see cref="ArmyData"/> - which every joined player has, so this
    /// works on a network client too - and cached per store until the set of armies changes.
    /// </para>
    /// </summary>
    public static class ReadOnlyRuleEvaluators
    {
        private sealed class Entry
        {
            public ArmyData[] Armies = System.Array.Empty<ArmyData>();
            public RuleEvaluator? Evaluator;
        }

        private static readonly ConditionalWeakTable<IReadableGameDataStore, Entry> s_cache = new();

        /// <summary>The core catalog only - for a caller with no table (tests, previews before a game exists).</summary>
        public static RuleEvaluator Core { get; } =
            new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: CoreRuleCatalog.CreateResolver());

        /// <summary>The evaluator for this table's game: the core catalog plus every army's definitions.</summary>
        public static RuleEvaluator For(ITableState? table)
        {
            IReadableGameDataStore? store = table?.DataStore;
            if (store == null || !store.IsTypeAssigned<ArmyData>()) return Core;

            ArmyData[] armies = store.GetAllValues<ArmyData>().ToArray();
            Entry entry = s_cache.GetOrCreateValue(store);
            lock (entry)
            {
                if (entry.Evaluator != null && SameArmies(entry.Armies, armies)) return entry.Evaluator;

                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                foreach (ArmyData army in armies)
                {
                    IReadOnlyList<SpecialRuleDefinition>? definitions = army.RestoreRuleData()?.RuleDefinitions;
                    if (definitions == null) continue;
                    foreach (SpecialRuleDefinition definition in definitions) resolver.RegisterOrReplace(definition);
                }

                entry.Armies = armies;
                entry.Evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);
                return entry.Evaluator;
            }
        }

        // By reference: a store hands back the same object until it is updated, and an updated army is
        // exactly when the definitions might have changed.
        private static bool SameArmies(ArmyData[] a, ArmyData[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (!ReferenceEquals(a[i], b[i])) return false;
            return true;
        }
    }
}
