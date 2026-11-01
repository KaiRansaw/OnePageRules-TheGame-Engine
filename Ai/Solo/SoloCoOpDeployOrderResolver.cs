using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - which unit the official AI deploys next. Two clauses from <b>Solo &amp; Co-Op Rules
    /// v3.6.0</b> decide it:
    ///
    /// <para>
    /// "<b>Scout:</b> AI units with Scout must always be deployed <b>after all other units</b>, following
    /// the AI deployment rules." And, from the deployment paragraph, "<b>deploy one random unit at a time</b>
    /// in its section".
    /// </para>
    /// <para>
    /// Scout last is the clause with teeth. Scout deploys further forward than the rest, so going last means
    /// it is placed knowing where everything else already stands - which is the point of the rule, and the
    /// opposite of what the baseline does by taking whatever the engine offered first.
    /// </para>
    /// <para>
    /// <b>Transports still deploy before their cargo</b>, ahead of the random pick. That is not in this
    /// book - it is the engine's own #191 A5-10 rule, and it has to stay: the embark offer only exists for a
    /// transport already on the table, so a transport deployed late means cargo that walks. GF's own
    /// Transport clause ("the AI must always place random units in each Transport") wants them filled, which
    /// needs them down first, so the two agree in effect.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpDeployOrderResolver : IStageResolver<SelectionRequest<UnitData>, DataBinding<UnitData>>
    {
        private readonly RuleEvaluator? _evaluator;
        private readonly Random _rng;
        private readonly AiSelectionResolver<UnitData> _fallback;

        public SoloCoOpDeployOrderResolver(RuleEvaluator? evaluator, Random rng)
        {
            _evaluator = evaluator;
            _rng = rng;
            _fallback = new AiSelectionResolver<UnitData>(evaluator);
        }

        public Task<DataBinding<UnitData>> Resolve(SelectionRequest<UnitData> request)
        {
            var options = request.ValidOptions.Select(o => o.Option).ToList();
            if (options.Count <= 1) return _fallback.Resolve(request);

            // Transports first - the engine's rule, and the reason GF's Transport clause can fill them.
            // Delegated so the rule-graph question ("is this a transport?") is asked in exactly one place.
            if (_evaluator != null && options.Any(IsTransport)) return _fallback.Resolve(request);

            // "deployed after all other units": anything without Scout goes first, and only once the
            // non-Scouts are gone does a Scout become eligible.
            var eligible = options.Where(o => !HasScout(o)).ToList();
            if (eligible.Count == 0) eligible = options;

            // "one random unit at a time".
            DataBinding<UnitData> pick = eligible[_rng.Next(eligible.Count)];
            return Task.FromResult(pick);
        }

        private bool IsTransport(DataBinding<UnitData> unit)
        {
            foreach (ResolvedRule rule in unit.GetValue().RuleDefinitions)
                if (rule.Definition == CoreRuleCatalog.Transport) return true;
            return false;
        }

        private static bool HasScout(DataBinding<UnitData> unit)
        {
            foreach (ResolvedRule rule in unit.GetValue().RuleDefinitions)
                if (rule.Definition == CoreRuleCatalog.Scout) return true;
            return false;
        }
    }
}
