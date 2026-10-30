using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Reads — and, for one-shot "next time" grants, consumes — a unit's granted numeric roll modifiers
    /// (#033 <see cref="Effect.StatModifier"/>). The relevant roll stage calls <see cref="ConsumeNet"/> for
    /// the rolling unit and roll kind, then folds the returned net delta into its own roll-modifier math
    /// (same sign convention as the rule-driven roll-modifier sink: a positive delta makes the roll easier).
    /// FirstTrigger ("once") grants are removed here — this roll is that "next time"; duration grants
    /// (ThisActivation / ThisRound) are left for <see cref="Tokens.TokenClearService"/> to sweep at their hook.
    /// </summary>
    public static class GrantedRollModifiers
    {
        /// <summary>
        /// Read-only twin of <see cref="ConsumeNet"/> (#325 pre-roll forecast): the same net delta the
        /// next roll will fold in, computed WITHOUT removing the one-shot grants that roll will spend.
        /// Safe to call while building UI; the forecast stays honest because each offer pass recomputes
        /// it, so a grant spent by an earlier volley of the same action has already left the tokens.
        /// </summary>
        public static int PeekNet(IUnit unit, ERollKind roll)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            int net = 0;
            foreach (Token token in unit.Tokens.GetAllTokens(type))
            {
                if (token.Payload is TokenPayload.StatModifier modifier)
                {
                    net += modifier.Delta * token.Count;
                }
            }
            return net;
        }

        /// <summary>
        /// #407: the same buffs <see cref="PeekNet"/> totals, but itemised - one entry per granting
        /// source, so a roll stage can render "Vengeful Strike +1" rather than a bare "buff +1". Does not
        /// consume, like its namesake. Entries with the same source are merged, and their deltas sum to
        /// <see cref="PeekNet"/>; a buff whose source was never recorded comes back with a null
        /// <c>Source</c> and is rendered generically.
        /// </summary>
        public static IReadOnlyList<GrantedModifier> PeekDetailed(IUnit unit, ERollKind roll)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            // Grouped by source and kept in first-seen order, so two stacking copies of one skill read
            // as "Bless +2" rather than "Bless +1  Bless +1".
            var order = new List<string?>();
            var totals = new Dictionary<string, int>();
            int unattributed = 0;
            bool sawUnattributed = false;

            foreach (Token token in unit.Tokens.GetAllTokens(type))
            {
                if (token.Payload is not TokenPayload.StatModifier modifier) continue;
                int delta = modifier.Delta * token.Count;

                if (string.IsNullOrWhiteSpace(modifier.Source))
                {
                    if (!sawUnattributed) { order.Add(null); sawUnattributed = true; }
                    unattributed += delta;
                    continue;
                }

                if (!totals.ContainsKey(modifier.Source)) order.Add(modifier.Source);
                totals.TryGetValue(modifier.Source, out int running);
                totals[modifier.Source] = running + delta;
            }

            var result = new List<GrantedModifier>(order.Count);
            foreach (string? source in order)
            {
                result.Add(source == null
                    ? new GrantedModifier(null, unattributed)
                    : new GrantedModifier(source, totals[source]));
            }
            return result;
        }

        public static int ConsumeNet(IUnit unit, ERollKind roll)
        {
            TokenType type = RollModifierTokens.TypeFor(roll);

            int net = 0;
            int firstTriggerCount = 0;
            foreach (Token token in unit.Tokens.GetAllTokens(type).ToList())
            {
                if (token.Payload is TokenPayload.StatModifier modifier)
                {
                    net += modifier.Delta * token.Count;
                }
                if (token.ClearTrigger is TokenClearTrigger.FirstTrigger)
                {
                    firstTriggerCount += token.Count;
                }
            }

            // Remove the consumed one-shot grants — and only those. The trigger-aware drain leaves
            // duration grants (ThisActivation / ThisRound) of the same roll kind untouched, so mixing a
            // "once" and a duration buff on one unit no longer clears the duration one early (#033 edge).
            if (firstTriggerCount > 0)
            {
                unit.Tokens.RemoveFirstTriggerTokens(type, firstTriggerCount);
            }
            return net;
        }

        /// <summary>
        /// #407: a roll stage's itemised view of the consumed buffs - the detail to show beside the roll,
        /// taken BEFORE the one-shot grants are drained, paired with the net that actually applied.
        /// One call so a stage cannot accidentally read the detail after consuming (which would come back
        /// empty for every "once" buff - the ones most worth naming).
        /// </summary>
        public static (int Net, IReadOnlyList<GrantedModifier> Detail) ConsumeDetailed(IUnit unit, ERollKind roll)
        {
            IReadOnlyList<GrantedModifier> detail = PeekDetailed(unit, roll);
            return (ConsumeNet(unit, roll), detail);
        }
    }

    /// <summary>
    /// #407: one granted roll buff, as a player reads it. <paramref name="Source"/> is the skill, item,
    /// rule or event that granted it, or null for a buff granted before sources were recorded (or by a
    /// call site not yet passing one) - those render as a plain "buff".
    /// </summary>
    public readonly record struct GrantedModifier(string? Source, int Delta)
    {
        /// <summary>"Vengeful Strike +1", or "buff +1" when the source is unknown. ASCII only.</summary>
        public string Describe()
        {
            string signed = Delta >= 0 ? $"+{Delta}" : Delta.ToString();
            return $"{(string.IsNullOrWhiteSpace(Source) ? "buff" : Source)} {signed}";
        }
    }
}
