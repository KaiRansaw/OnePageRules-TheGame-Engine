using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// #103 cast-assist policy (#191 A5): one token shifts the cast's 4+ threshold one face, so
    /// it buys 1/6 of the spell's expected effect - boosting a friendly cast or denying an enemy
    /// one alike (the solo bot always declines both). The spell is priced from the CASTING unit's
    /// eligible targets, since the already-chosen targets are not in the request (recorded
    /// approximation). Spends only while a token's buy beats its opportunity cost, capped per
    /// request so one cast never drains a whole pool.
    /// <para>
    /// #414 — prices TOKENS only, and answers with zero Power. The Tactician plays an army, not a Star
    /// Quest party, so its casters have no Power to begin with; pricing a currency it never holds would be
    /// untested code paying for itself. If the Tactician is ever pointed at a hero party, the valuation
    /// above is the place to extend, because a point of Power buys exactly the same 1/6 of a spell as a
    /// token while costing the hero a share of its revive budget - a different opportunity cost, not a
    /// different effect.
    /// </para>
    /// </summary>
    public class TacticianCastAssistResolver : IStageResolver<CastAssistRequest, CastAssistReply>
    {
        private readonly ITableState _tableState;
        private readonly RuleEvaluator _evaluator;
        // #384: the game's see-through-allies setting, for the spell pricing's sight tests.
        private readonly bool _seeThroughFriendlyUnits;

        public TacticianCastAssistResolver(ITableState tableState, RuleEvaluator evaluator,
            bool seeThroughFriendlyUnits = false)
        {
            _tableState = tableState;
            _evaluator = evaluator;
            _seeThroughFriendlyUnits = seeThroughFriendlyUnits;
        }

        public Task<CastAssistReply> Resolve(CastAssistRequest request)
        {
            if (request.AvailableTokens <= 0) return Task.FromResult(CastAssistReply.None);

            ArmyData? army = SpellValuation.ArmyOf(_tableState, request.CastingUnit.GetValue().PlayerID);
            RuntimeSpell? spell = army == null ? null : SpellValuation.FindByName(army, request.SpellName);
            if (spell == null) return Task.FromResult(CastAssistReply.None); // unknown spell: decline, like solo

            float gross = SpellValuation.GrossCastValue(_evaluator, _tableState, request.CastingUnit, spell,
                _seeThroughFriendlyUnits);
            float perToken = gross / 6f;
            if (perToken <= TacticianWeights.CastTokenValue) return Task.FromResult(CastAssistReply.None);

            return Task.FromResult(new CastAssistReply(
                Math.Min(request.AvailableTokens, TacticianWeights.CastAssistMaxTokens)));
        }
    }
}
