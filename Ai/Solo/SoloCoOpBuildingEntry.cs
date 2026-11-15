using FDG.Ai.Tactician;
using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #460 Simplified Terrain - a bot going into a building, which it does only to take an objective that is in the
    /// building or within 3" of it (Kai, 2026-11-15: "for objectives"). Everywhere else the bots' planners treat a
    /// building as a wall, which is always legal.
    ///
    /// <para>
    /// The whole unit must fit and end inside (<see cref="SolidBuildings.MoveProblem"/>): the models are laid out on a
    /// grid inside the footprint, nearest spots first, and walked there in a straight line. The move is checked with
    /// exactly the stage's own rules (paths with building entry, then the building rules); any failure - out of
    /// reach, no room, something in the way - returns null and the unit moves as before.
    /// </para>
    /// </summary>
    public static class SoloCoOpBuildingEntry
    {
        public static List<ModelMoveEntry>? Try(DefineMovementPathRequest request, ITableState table, PlayerID me,
                                               Position objective, float budgetCap)
        {
            var terrain = table.Terrain.Objects.ToList();
            if (SolidBuildings.ForObjective(objective, terrain) is not { } building) return null;

            UnitData unit = request.UnitDataBinding.GetValue();
            if (ReferenceEquals(SolidBuildings.Holding(unit, terrain), building)) return null;
            var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0 || SolidBuildings.Spaces(unit) is null) return null;
            if (building.Shape is not IBoundedZone bounded) return null;

            float cap = Math.Min(budgetCap, request.MaxDistanceInches) - 0.01f;
            var centre = new Position(living.Average(m => m.GetValue().Position.x), living.Average(m => m.GetValue().Position.z));

            // Spots inside the footprint, a base apart, nearest the unit first.
            float spacing = living.Max(m => m.GetValue().BaseShape.CircumscribedRadiusInches) * 2f + 0.3f;
            float margin = spacing / 2f;
            ZoneBounds b = bounded.Bounds;
            var spots = new List<Position>();
            for (float x = b.Left + margin; x <= b.Right - margin + 0.001f; x += spacing)
                for (float z = b.Bottom + margin; z <= b.Top - margin + 0.001f; z += spacing)
                    if (building.Shape.IsPointWithinZone(new Float2(x, z))) spots.Add(new Position(x, z));
            if (spots.Count < living.Count) return null;
            spots = spots.OrderBy(p => Position.GetDistance2D(p, centre)).Take(living.Count * 2).ToList();

            // Each model, furthest-travelling first, takes its nearest free spot.
            var moves = new List<ModelMoveEntry>();
            var free = new List<Position>(spots);
            foreach (var model in living.OrderByDescending(m => spots.Min(p => Position.GetDistance2D(p, m.GetValue().Position))))
            {
                Position at = model.GetValue().Position;
                Position spot = free.OrderBy(p => Position.GetDistance2D(p, at)).First();
                free.Remove(spot);
                if (Position.GetDistance2D(spot, at) > cap) return null;
                moves.Add(new ModelMoveEntry(model, new List<Position> { spot }));
            }

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, Math.Min(maxDist, budgetCap));
            };
            var enemies = MovementPlanner.LiveEnemyFootprints(table, me);
            var friends = MovementPlanner.LiveFriendlyFootprints(table, me, unit.ID);
            if (!MovementUtilities.ValidatePaths(moves, budgetFor, enemies, request.CanMoveThroughEnemies,
                    request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friends,
                    lenientCoherency: true, buildingEntry: true))
                return null;
            return DefinePathStage.BuildingProblem(table, unit, moves) == null ? moves : null;
        }
    }
}
