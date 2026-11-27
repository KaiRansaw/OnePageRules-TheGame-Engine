using System.Linq;
using System.Collections.Generic;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #191 A5 / #244 — answers the dedicated spell picker with the planner's highest-net-value
    /// castable spell (see <see cref="TacticianPlanner.ChooseSpell"/>), never Cancel (the
    /// forced-Cast livelock class). No self-boost yet — a boost policy is a recorded refinement,
    /// same bucket as the assist policy. Falls back to the solo first-castable pick when the
    /// planner has no claim (unknown active unit / army).
    /// </summary>
    public class TacticianChooseSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
    {
        private readonly TacticianPlanner _planner;
        private readonly IStageResolver<ChooseSpellRequest, ChooseSpellReply> _soloFallback;

        public TacticianChooseSpellResolver(TacticianPlanner planner,
            IStageResolver<ChooseSpellRequest, ChooseSpellReply> soloFallback)
        {
            _planner = planner;
            _soloFallback = soloFallback;
        }

        public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
        {
            // The planner values spells by their picker label ("Name (cost)") — hand it the castable
            // rows' labels and map its pick back to the request index.
            List<string> castableLabels = new List<string>();
            foreach (ChooseSpellRequest.SpellOption option in request.Spells)
            {
                if (option.Castable) castableLabels.Add(option.Label);
            }

            // Advanced Casting (lane 2, 2026-11-27): the planner weighs each spell by its real chance - 2D6 Casting's 7+ or
            // 10+ outside a Spell Book, or Magic Pools' "as many 4+ as the spell's casting value" with the dice the bot
            // would roll - instead of the one-die 4+ every spell was priced at.
            float? Chance(string label)
            {
                ChooseSpellRequest.SpellOption? o = request.Spells.FirstOrDefault(s => s.Castable && s.Label == label);
                if (o == null) return null;
                if (o.Needed is int needed) return (float)PoolBlockPolicy.TwoD6Chance(needed);
                if (request.DicePool)
                    return (float)PoolBlockPolicy.CastChance(PoolBlockPolicy.CastDiceFor(o.Cost, request.AvailableTokens, o.PoolDie ?? 4),
                        o.Cost, o.PoolDie ?? 4);
                return null;
            }
            string? pick = _planner.ChooseSpell(castableLabels, Chance);
            if (pick != null)
            {
                for (int i = 0; i < request.Spells.Count; i++)
                {
                    if (!request.Spells[i].Castable || request.Spells[i].Label != pick) continue;
                    // Magic Pools: roll extra dice while they're worth their backlash risk (the boost is the extra dice).
                    int extra = request.DicePool
                        ? Math.Max(0, Math.Min(PoolBlockPolicy.CastDiceFor(request.Spells[i].Cost, request.AvailableTokens,
                                request.Spells[i].PoolDie ?? 4)
                            - request.Spells[i].Cost, request.AvailableTokens - request.Spells[i].Cost))
                        : 0;
                    return Task.FromResult(new ChooseSpellReply(i, extra));
                }
            }
            return _soloFallback.Resolve(request);
        }
    }
}
