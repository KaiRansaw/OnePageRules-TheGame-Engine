using FDG.Ai.Resolvers;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - moves an official-AI unit where <see cref="SoloAiBrain"/>'s plan says, which for four of the
    /// tree's five branches is an objective marker rather than an enemy.
    ///
    /// <para>
    /// This is the half that makes the decision tree real. The baseline's mover walks at the nearest enemy;
    /// the book's tree spends most of its branches walking to objectives - "Rush toward objective",
    /// "Advance towards objective and shoot closest", "Hold position" - and none of that happens unless the
    /// movement request is answered with the objective's position.
    /// </para>
    /// <para>
    /// <b>Advance versus Rush is decided here, by distance.</b> The engine reads the action from how far the
    /// move goes, so a plan that wants to shoot afterwards is capped at the Advance budget and a plan that
    /// just wants ground gets the full move. Getting that backwards would quietly cost the AI either its
    /// shooting or its pace.
    /// </para>
    /// <para>
    /// Charges are handed to the baseline mover on purpose: driving a unit into legal base contact is
    /// fiddly, that resolver already does it, and a charge is the one branch where "go at the nearest
    /// enemy" is exactly right.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpMovementResolver
        : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly ITableState _table;
        private readonly PlayerID _me;
        private readonly AiDefineMovementResolver _baseline;

        public SoloCoOpMovementResolver(ITableState table, PlayerID me, SoloMoveDeclineLatch? latch)
        {
            _table = table;
            _me = me;
            _baseline = new AiDefineMovementResolver(table, me, latch);
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            // Forced moves, consolidations and anything that is not the unit's own activation move keep the
            // baseline behaviour - the tree has nothing to say about them.
            if (!request.MainActivationMove) return _baseline.Resolve(request);

            UnitData unit = request.UnitDataBinding.GetValue();
            SoloAiPlan plan = SoloAiBrain.Plan(_table, _me, unit);

            switch (plan.Kind)
            {
                case ESoloAiMove.Charge:
                    return _baseline.Resolve(request);

                case ESoloAiMove.RushToObjective when plan.Goal is { } rushGoal:
                    // Every inch: the unit is crossing ground, not setting up a shot.
                    return Done(TowardWithTerrain(request, unit, rushGoal,
                        SoloAiBrain.ObjectiveReachInches, request.MaxDistanceInches));

                case ESoloAiMove.AdvanceToObjective when plan.Goal is { } advanceGoal:
                    // Capped at the Advance budget so the shot the plan promised is still legal.
                    return Done(TowardWithTerrain(request, unit, advanceGoal,
                        SoloAiBrain.ObjectiveReachInches, request.MaxAdvanceDistance));

                case ESoloAiMove.AdvanceAndShoot when plan.Target != null:
                    // Step 2 and step 5's shooting branch. Walk toward the target only as far as needed to
                    // be inside its own range band, and no further - standing in the open for nothing is
                    // what the book's "Hold and shoot if in range" exists to prevent.
                    return Done(MoveIntoRange(request, plan));

                case ESoloAiMove.Hold:
                default:
                    // Step 2's "Hold position": a validated stand-still, not a decline. A decline reopens
                    // the action menu and invites the loop documented in AiMoveExecutor.
                    return Done(AiMoveExecutor.Toward(request, _table, _me, CentreOf(unit), 0f, 0f));
            }
        }

        /// <summary>
        /// #424 the Terrain clause: "AI units must always try to end their move into or behind Cover
        /// terrain if possible, moving around Difficult and Dangerous terrain."
        ///
        /// <para>
        /// The plain destination is worked out first - how far the unit would get toward its goal with no
        /// terrain preference - and then <see cref="SoloCoOpTerrainPolicy"/> is asked whether a nearby end
        /// point is better. The adjusted point is aimed at with no stopping distance, since it already IS
        /// a stopping point; the mover then plans and VALIDATES the walk to it exactly as before, so an
        /// unreachable piece of cover costs nothing but the attempt.
        /// </para>
        /// </summary>
        private List<ModelMoveEntry> TowardWithTerrain(DefineMovementPathRequest request, IUnit unit,
            Position goal, float stopWithin, float budgetCap)
        {
            Position plain = PlainDestination(request, unit, goal, stopWithin, budgetCap);
            Position aim = SoloCoOpTerrainPolicy.PreferredEndPoint(_table, _me, unit, plain, goal,
                request.IgnoresDifficultTerrain);

            // Unchanged? Take the original path, which keeps the goal's own stopping distance and the
            // tie-break behaviour of every move before #424.
            if (Position.GetDistance2D(aim, plain) <= 0.001f)
                return AiMoveExecutor.Toward(request, _table, _me, goal, stopWithin, budgetCap);

            return AiMoveExecutor.Toward(request, _table, _me, aim, 0f, budgetCap);
        }

        /// <summary>
        /// Where the unit would stop with no terrain preference: straight at the goal, as far as the
        /// budget allows, stopping <paramref name="stopWithin"/> short. Mirrors
        /// <see cref="AiMoveExecutor.Toward"/>'s own arithmetic - deliberately a copy rather than a
        /// refactor of it, because that method is shared with the frozen baseline and the Tactician.
        /// </summary>
        private static Position PlainDestination(DefineMovementPathRequest request, IUnit unit,
            Position goal, float stopWithin, float budgetCap)
        {
            List<IModel> living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return goal;

            var centre = new Position(living.Average(m => m.Position.x), living.Average(m => m.Position.z));
            float distance = Position.GetDistance2D(centre, goal);
            float cap = Math.Max(0f, Math.Min(budgetCap, request.MaxDistanceInches) - 0.001f);
            float travel = Math.Clamp(distance - stopWithin * 0.5f, 0f, cap);
            if (distance <= 0.001f || travel <= 0.001f) return centre;

            float t = travel / distance;
            return new Position(
                centre.x + (goal.x - centre.x) * t,
                centre.z + (goal.z - centre.z) * t);
        }

        /// <summary>
        /// Closes to just inside the unit's own weapon range, staying an inch inside it so a float
        /// rounding error cannot leave the shot out of reach.
        /// </summary>
        private List<ModelMoveEntry> MoveIntoRange(DefineMovementPathRequest request, SoloAiPlan plan)
        {
            IUnit target = plan.Target!;
            Position aim = CentreOf(target);
            float standoff = Math.Max(0f, plan.Range - 1f);
            return AiMoveExecutor.Toward(request, _table, _me, aim, standoff, request.MaxAdvanceDistance);
        }

        private static Position CentreOf(IUnit unit)
        {
            var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return new Position(0f, 0f);
            return new Position(living.Average(m => m.Position.x), living.Average(m => m.Position.z));
        }

        private static Task<CancellableResult<List<ModelMoveEntry>>> Done(List<ModelMoveEntry> move) =>
            Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Selected<List<ModelMoveEntry>>(move));
    }
}
