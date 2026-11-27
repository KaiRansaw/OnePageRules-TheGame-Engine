using FDG.Ai.Solo;
using FDG.Data;
using FDG.Rules;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Tactician.Resolvers
{
    /// <summary>
    /// Plays out the planner's cached macro-action when the engine asks for the actual move
    /// (#191 A4-2). The cached move was built by the MovementPlanner ladder, so it is engine-valid
    /// by construction - but it is re-checked against THIS request's budgets before submitting,
    /// because a rejection would fault the stage with no retry; anything without a valid plan
    /// falls back to the solo-rules movement resolver (G3).
    /// </summary>
    public class TacticianMovementResolver
        : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly IMovePlanSource _planner;
        private readonly ITableState _tableState;
        private readonly IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> _soloFallback;
        // #216 / #264 issue 6: a silent degradation to solo is the failure mode behind several
        // walled-unit reports - the unit just looks passive and nothing says why. Null in normal
        // play; wired to the same analysis sink as the planner's decision table.
        private readonly Action<string>? _log;

        public TacticianMovementResolver(IMovePlanSource planner, ITableState tableState,
            IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>> soloFallback,
            Action<string>? log = null)
        {
            _planner = planner;
            _tableState = tableState;
            _soloFallback = soloFallback;
            _log = log;
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            List<ModelMoveEntry>? planned = _planner.TakePlannedMove(request.UnitDataBinding);
            if (planned == null)
                return DegradeToSolo(request, "no cached plan (Hold/Pass, or the planner had no claim)");

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            var enemies = MovementPlanner.LiveEnemyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID);
            // #205: reject a planned macro-move that would end stacked on a friendly, falling back to the solo
            // resolver (which re-plans around friendlies) rather than submitting a move the stage rejects.
            var friendlies = MovementPlanner.LiveFriendlyFootprints(_tableState,
                request.UnitDataBinding.GetValue().PlayerID, request.UnitDataBinding.GetValue().ID);
            // #159: lenientCoherency mirrors DefinePathStage so this pre-check agrees with the authoritative
            // one - a plan that holds an already-broken unit isn't rejected for a coherency it can't restore.
            bool valid = MovementUtilities.ValidatePaths(planned, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _, friendlies,
                lenientCoherency: true);

            // #460 Simplified Terrain (Kai, 2026-11-15: "bots enter for objectives"): the planner sees a building as
            // a wall, so a move toward an objective in or beside one stops at the wall. Go in instead, under the same
            // distance the planned move used, so the action the planner chose (an Advance that shoots) stays legal.
            if (request.MainActivationMove && IntoBuilding(request, planned) is { } inside)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Selected<List<ModelMoveEntry>>(inside));

            // #460: the path check lets a model end inside a building (it may cross a wall into one); the building
            // rules - the whole unit goes in or none of it, room, 6" to leave - are the stage's next check. A planned
            // objective move that put part of a unit inside faulted the game (lane 2 sweep, 2026-11-27, Simplified
            // Terrain with the Tactical bot), so the same check runs here and a failing plan is repaired or replaced.
            UnitData mover = request.UnitDataBinding.GetValue();
            if (valid && DefinePathStage.BuildingProblem(_tableState, mover, planned) == null)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(planned));

            // #264 issue 7 (with #216's repair pass): the plan was built at Choose Action time, before
            // this request existed, so its budgets can disagree with the request's per-model ones - a
            // joined Slow hero makes EVERY planned move fail here, and the unit then plays the solo
            // resolver for the whole game without anything saying so. The macro choice was still the
            // right one; only its distance was wrong. Re-run the same ladder toward the same
            // destination under THIS request's budgets before conceding the activation.
            List<ModelMoveEntry>? repaired = TryRepairWithinRequestBudgets(request, planned, budgetFor,
                enemies, friendlies);
            if (repaired != null && DefinePathStage.BuildingProblem(_tableState, mover, repaired) == null)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(
                    new Selected<List<ModelMoveEntry>>(repaired));

            return DegradeToSolo(request, "the planned move failed this request's per-model re-check "
                + "and could not be re-planned within its budgets");
        }

        /// <summary>How near the planned move must end to an objective for it to count as going for that objective.</summary>
        public const float ObjectiveGoalInches = 6f;

        /// <summary>
        /// The planned move, taken into the building that holds (or stands within 3" of) the objective it was heading
        /// for - or null when there is no such building, the unit is in it already, or it can't get in this turn
        /// (the official AI's own entry check: whole unit fits, in reach, nothing in the way).
        /// </summary>
        private List<ModelMoveEntry>? IntoBuilding(DefineMovementPathRequest request, List<ModelMoveEntry> planned) =>
            IntoBuilding((_planner as TacticianPlanner)?.LastMacro?.Intent, request, _tableState, planned, _log);

        /// <summary>The test seam of <see cref="IntoBuilding(DefineMovementPathRequest, List{ModelMoveEntry})"/>.</summary>
        internal static List<ModelMoveEntry>? IntoBuilding(EMacroIntent? intent, DefineMovementPathRequest request, ITableState table,
            List<ModelMoveEntry> planned, Action<string>? log = null)
        {
            // Only a move the planner chose to take an objective - not a charge, a fall-back or a firing position.
            if (intent is not (EMacroIntent.AdvanceOnObjective or EMacroIntent.RushObjective)) return null;
            var terrain = table.Terrain.Objects.ToList();
            if (!terrain.Any(SolidBuildings.Is)) return null;
            var ends = planned.Where(e => e.Positions.Count > 0).Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return null;
            var goal = new Position(ends.Average(p => p.x), ends.Average(p => p.z));
            var objective = table.Objectives.Objects
                .Select(o => o.Position)
                .Where(o => Position.GetDistance2D(o, goal) <= ObjectiveGoalInches)
                .OrderBy(o => Position.GetDistance2D(o, goal))
                .Cast<Position?>().FirstOrDefault();
            if (objective is not { } at) return null;

            // The distance the planned move used: within the Advance allowance keeps it an Advance.
            float longest = planned.Max(e =>
            {
                var start = e.Model.GetValue().Position;
                float d = 0;
                foreach (var p in e.Positions) { d += Position.GetDistance2D(start, p); start = p; }
                return d;
            });
            float cap = longest <= request.MaxAdvanceDistance + 0.01f ? request.MaxAdvanceDistance : request.MaxDistanceInches;
            List<ModelMoveEntry>? inside = SoloCoOpBuildingEntry.Try(request, table, request.UnitDataBinding.GetValue().PlayerID, at, cap);
            if (inside != null) log?.Invoke($"building {request.UnitDataBinding.GetValue().Name}: goes in to take the objective");
            return inside;
        }

        private Task<CancellableResult<List<ModelMoveEntry>>> DegradeToSolo(
            DefineMovementPathRequest request, string reason)
        {
            _log?.Invoke($"solo-fallback {request.UnitDataBinding.GetValue().Name}: {reason}");
            return BuildingSafe(request, _soloFallback.Resolve(request));
        }

        /// <summary>The fallback's move too must keep the building rules; if not, the unit holds exactly where it stands (not a re-packed stay, which can step off the table).</summary>
        private async Task<CancellableResult<List<ModelMoveEntry>>> BuildingSafe(DefineMovementPathRequest request,
            Task<CancellableResult<List<ModelMoveEntry>>> pending)
        {
            CancellableResult<List<ModelMoveEntry>> result = await pending;
            if (result is not Selected<List<ModelMoveEntry>> { Value: { } path }
                || DefinePathStage.BuildingProblem(_tableState, request.UnitDataBinding.GetValue(), path) == null)
                return result;
            _log?.Invoke($"building {request.UnitDataBinding.GetValue().Name}: the fallback move broke the building rules - standing still");
            return new Selected<List<ModelMoveEntry>>(MovementPlanner.HoldExactPositions(request.UnitDataBinding.GetValue().ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList()));
        }

        // Re-plan toward the cached move's own destination at the SLOWEST model's allowance, with each
        // model validated against its own cap. Null when even that cannot be made legal (the solo
        // resolver, which re-plans from scratch, is the right answer then).
        private List<ModelMoveEntry>? TryRepairWithinRequestBudgets(DefineMovementPathRequest request,
            List<ModelMoveEntry> planned, Func<ModelMoveEntry, ModelMoveBudget> budgetFor,
            List<EnemyModelFootprint> enemies, List<EnemyModelFootprint> friendlies)
        {
            DataBinding<UnitData> unit = request.UnitDataBinding;
            var living = unit.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return null;

            var ends = planned.Where(e => e.Positions.Count > 0)
                .Select(e => e.Positions[^1]).ToList();
            if (ends.Count == 0) return null;
            var goal = new Position(ends.Average(p => p.x), ends.Average(p => p.z));

            float rush = float.MaxValue, hardCap = float.MaxValue;
            foreach (DataBinding<ModelData> model in living)
            {
                var (_, modelRush, modelMax) = request.BudgetFor(model.GetValue().ID);
                rush = Math.Min(rush, modelRush);
                hardCap = Math.Min(hardCap, modelMax);
            }
            if (hardCap <= 0f) return null;

            List<ModelMoveEntry> repaired = MovementPlanner.PlanMoveToward(unit, living, _tableState,
                goal, Math.Max(0f, Math.Min(rush, hardCap) - 0.001f), hardCap, budgetFor,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain);

            bool valid = MovementUtilities.ValidatePaths(repaired, budgetFor, enemies,
                request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects.ToList(), out _,
                friendlies, lenientCoherency: true);
            return valid ? repaired : null;
        }
    }
}
