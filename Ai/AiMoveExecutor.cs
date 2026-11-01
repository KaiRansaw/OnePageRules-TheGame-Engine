using FDG.Ai.Tactician;
using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai
{
    /// <summary>
    /// #419 - "walk this unit toward that point and stop short of it", the one move every objective-seeking
    /// AI needs, with the guard that keeps it from hanging the game.
    ///
    /// <para>
    /// Extracted from the Quest hero resolver so the official Solo &amp; Co-Op bot uses the same copy. Both
    /// brains answer the same question - a point on the table and how close to get - and both pay for a
    /// mistake here the same way.
    /// </para>
    /// <para>
    /// <b>The guard, which is the whole reason this is a shared helper and not two similar methods.</b> When
    /// a walk cannot be made legal (the unit is boxed in, or the goal sits behind something impassable) the
    /// obvious move is to hand back to the solo path resolver. That hangs the game: the solo resolver
    /// declines a move it has no legal answer for, the engine's Back affordance reopens the action menu, the
    /// unit is offered Move again, and round it goes until the ~1.5M-decision watchdog fires. One Horde seed
    /// went from finishing in under a second to not finishing in ten minutes. So an unachievable walk
    /// returns a VALIDATED stand-still instead - a definite answer the engine accepts.
    /// </para>
    /// </summary>
    public static class AiMoveExecutor
    {
        /// <summary>
        /// A validated move toward <paramref name="goal"/>, stopping about <paramref name="stopWithin"/>
        /// short, routing round terrain. Never declines: an impossible walk comes back as a stand-still.
        /// </summary>
        /// <param name="budgetCapInches">
        /// The furthest this move may go, which is also what decides whether the engine reads it as an
        /// Advance or a Rush - pass <c>request.MaxAdvanceDistance</c> to stay inside Advance (so the unit
        /// can still shoot afterwards) or <c>request.MaxDistanceInches</c> to Rush.
        /// </param>
        public static List<ModelMoveEntry> Toward(DefineMovementPathRequest request, ITableState table,
            PlayerID me, Position goal, float stopWithin, float budgetCapInches)
        {
            var living = request.UnitDataBinding.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return MovementPlanner.StayInPlace(request.UnitDataBinding);

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            float dist = Position.GetDistance2D(new Position(cx, cz), goal);

            // Aim for half the slack rather than the stopping distance itself: the reach a caller cares
            // about is measured from a model's EDGE (AiCombatMath.DistanceToPoint subtracts the base
            // radius) while the move is planned from the unit's centre of mass, and with a multi-model
            // unit those are not the same point. Half the slack costs nothing - the budget caps it anyway -
            // and stopping at 0.9" when 1" was needed is the one failure that cannot be fixed next
            // activation, because the unit is already standing where it meant to be.
            float cap = Math.Max(0f, Math.Min(budgetCapInches, request.MaxDistanceInches) - 0.001f);
            float budget = Math.Clamp(dist - stopWithin * 0.5f, 0f, cap);

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(request.UnitDataBinding, living, table,
                goal, budget, request.MaxDistanceInches, budgetFor, request.CanMoveThroughEnemies,
                request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain);

            var terrain = table.Terrain.Objects.ToList();
            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(table, me);
            var friendlyFootprints = MovementPlanner.LiveFriendlyFootprints(table, me,
                request.UnitDataBinding.GetValue().ID);
            if (MovementUtilities.ValidatePaths(move, budgetFor, enemyFootprints, request.CanMoveThroughEnemies,
                    request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlyFootprints))
                return move;

            return MovementPlanner.StayInPlaceValidated(request.UnitDataBinding, living, budgetFor,
                enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, terrain, friendlyFootprints);
        }
    }
}
