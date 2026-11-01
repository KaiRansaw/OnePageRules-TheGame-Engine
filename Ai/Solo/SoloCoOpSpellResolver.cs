using System;
using System.Linq;
using FDG.Ai.Resolvers;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #423 - the official AI's Caster clause, from <b>Solo &amp; Co-Op Rules v3.6.0</b> (identical in all
    /// four books):
    ///
    /// <para>
    /// "<b>Caster:</b> AI units always cast spells <b>after moving</b>, selecting a <b>random spell by
    /// rolling D3+X</b>, where X is their caster level. If there is no valid target for that spell, or they
    /// don't have enough tokens to cast it, they must <b>cycle through the list</b> until there is a valid
    /// spell, or else <b>don't cast anything</b>."
    /// </para>
    /// <para>
    /// This class is the selection half. The "after moving" half is in
    /// <see cref="SoloCoOpActionResolver"/>, which until #423 never picked Cast at all - so the official
    /// bot's casters never cast, in any mode, which is the larger of the two faults this clause fixed.
    /// </para>
    /// <para>
    /// <b>What it replaces:</b> <see cref="AiChooseSpellResolver"/> takes the first castable spell in the
    /// list, which is the frozen baseline's behaviour (plan D1) and deterministic in a way the book is
    /// deliberately not - an enemy caster that always leads with spell 1 is a caster you only have to read
    /// once.
    /// </para>
    /// <para>
    /// <b>How D3+X indexes the list.</b> OPR prints spell lists numbered 1-6, so D3+X is a spell NUMBER:
    /// with Caster(1) it rolls 2-4 and with Caster(3) it rolls 4-6. Converted to a zero-based index and
    /// then cycled forward with wraparound, so every spell is reachable - without the wrap, a Caster(1)
    /// could never reach spell 1 or 6 even when they are the only castable ones, and "cycle through the
    /// list until there is a valid spell" plainly means the whole list. A list shorter than the roll (a
    /// faction with four spells) wraps for the same reason rather than failing to cast.
    /// </para>
    /// <para>
    /// <b>No boost.</b> The clause says nothing about spending extra tokens on the roll, so nothing is
    /// spent - the same choice <see cref="AiChooseSpellResolver"/> makes. Star Quest's resolver does boost,
    /// because its own flowchart tells it to; this one has no such instruction and inventing one would be
    /// house AI in the book bot's clothing.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
    {
        /// <summary>The roll: a D3, injected so a seeded game replays identically.</summary>
        private readonly Func<int> _rollD3;

        public SoloCoOpSpellResolver(Func<int> rollD3) => _rollD3 = rollD3;

        public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
        {
            int count = request.Spells.Count;
            if (count == 0) return Task.FromResult(ChooseSpellReply.Cancel);

            int casterLevel = CasterLevel(request.CastingUnit.GetValue());
            int spellNumber = Math.Max(1, _rollD3() + casterLevel);

            // Spell numbers are 1-based in the books; the list is 0-based here.
            int start = (spellNumber - 1) % count;

            for (int step = 0; step < count; step++)
            {
                int index = (start + step) % count;
                if (!request.Spells[index].Castable) continue;   // "no valid target" or not enough tokens
                return Task.FromResult(new ChooseSpellReply(index, 0));
            }

            // "...or else don't cast anything." Cancelling here is safe: the engine only raises this
            // request when at least one spell is castable, so this is the belt-and-braces path rather
            // than the forced-Cast livelock AiChooseSpellResolver's remarks warn about.
            return Task.FromResult(ChooseSpellReply.Cancel);
        }

        /// <summary>
        /// "X is their caster level" - the rating on the unit's own Caster(X). Read off the attached rule
        /// by name, including a rule carried by a joined model (the most common caster in the corpus is a
        /// Hero attached to a squad, whose rules live on its model). Defaults to 1, which is the lowest
        /// rating the corpus prints, so an unrated or oddly-named Caster still rolls a real spell number
        /// rather than collapsing to spell 1 every time.
        /// </summary>
        private static int CasterLevel(IUnit unit)
        {
            int fromUnit = RatingOf(unit.RuleDefinitions);
            if (fromUnit > 0) return fromUnit;

            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                int fromModel = RatingOf(model.RuleDefinitions);
                if (fromModel > 0) return fromModel;
            }

            return 1;
        }

        private static int RatingOf(IEnumerable<Rules.Dispatch.ResolvedRule> rules)
        {
            foreach (Rules.Dispatch.ResolvedRule rule in rules)
            {
                bool isCaster =
                    string.Equals(rule.Definition.Name, "Caster", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(rule.RequestedName, "Caster", StringComparison.OrdinalIgnoreCase);
                if (!isCaster) continue;
                if (rule.Arguments.Count > 0 && rule.Arguments[0] is RuleArgument.Int rating)
                {
                    return rating.Value;
                }
            }

            return 0;
        }
    }
}
