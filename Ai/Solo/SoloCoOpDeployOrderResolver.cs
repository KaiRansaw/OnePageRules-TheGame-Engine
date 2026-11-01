using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Stages;
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
        private readonly ITableState? _table;
        private readonly AiSelectionResolver<UnitData> _fallback;

        /// <param name="table">
        /// #423: needed for GF's Transport clause, which asks the AI to fill its transports up rather
        /// than spread the cargo. Optional, so the construction without it keeps #419's behaviour.
        /// </param>
        public SoloCoOpDeployOrderResolver(RuleEvaluator? evaluator, Random rng, ITableState? table = null)
        {
            _evaluator = evaluator;
            _rng = rng;
            _table = table;
            _fallback = new AiSelectionResolver<UnitData>(evaluator);
        }

        public Task<DataBinding<UnitData>> Resolve(SelectionRequest<UnitData> request)
        {
            // #423 Transport (Grimdark Future only): "The AI must always place random units in each
            // Transport, TRYING TO FILL UP THEIR CARGO LIMIT." This request type serves two prompts, and
            // the embark offer is the one carrying a cancel labelled "Deploy Normally" - the same
            // discriminator AiSelectionResolver matches on, for the same reason.
            //
            // Taking the ride at all was already right (#191 A5-10 answers this prompt rather than
            // declining it); what was missing is WHICH transport. The first offered one spreads a load
            // thinly across two half-empty hulls, so the fullest transport that still has room wins -
            // best-fit packing, and "trying to fill up their cargo limit" in as many words. Every option
            // the engine offers is already validated to fit this unit, so the smallest remaining space is
            // a safe pick without knowing what the unit costs.
            if (request.AllowCancel
                && request.CancelLabel == ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE
                && request.ValidOptions.Count > 1
                && _evaluator != null && _table != null)
            {
                if (FullestWithRoom(request) is { } fullest) return Task.FromResult(fullest);
            }

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

        /// <summary>
        /// The offered transport with the least room left - the one this load fills up. Null when the
        /// capacities cannot be read, in which case the caller keeps the old first-offered behaviour.
        /// </summary>
        private DataBinding<UnitData>? FullestWithRoom(SelectionRequest<UnitData> request)
        {
            CoreRulesProfile profile = CoreRulesProfile.For(_table!.Progress.GameSystem);
            List<IUnit> allUnits = _table.Units.Objects.ToList();

            DataBinding<UnitData>? best = null;
            int bestRemaining = int.MaxValue;
            foreach (SelectionRequest<UnitData>.ValidOption option in request.ValidOptions)
            {
                IUnit transport = option.Option.GetValue();
                int remaining = TransportUtilities.GetRemainingCapacity(
                    transport, allUnits, _evaluator!, profile);
                if (remaining <= 0 || remaining >= bestRemaining) continue;
                bestRemaining = remaining;
                best = option.Option;
            }

            return best;
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
