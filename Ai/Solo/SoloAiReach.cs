using FDG.Ai.Tactician;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #364 (the official AI's side of it) - how far a charge really has to go: around anything Impassable, not
    /// through it. The Solo &amp; Co-Op tree asks "is any enemy in range to charge?"; measured in a straight line it
    /// said yes to an enemy behind a wall the charge could never get round, planned the charge, and the move then
    /// failed - the unit lost its activation. With Simplified Terrain (#460) every piece is a wall, which made it
    /// common.
    ///
    /// <para>
    /// The gap is walked: from the closest pair of models, the route the bots' own pathfinder would take
    /// (<see cref="RouteMetrics.Route"/> - the straight line when it is clear, so open ground costs nothing), minus
    /// the two bases. An enemy inside a solid building is charged by touching the wall (Advanced Rulebook p33), so
    /// that building is left out of the obstacles and the walk stops where the route meets it.
    /// </para>
    /// </summary>
    public static class SoloAiReach
    {
        public static float ChargeGap(ITableState table, IUnit unit, IUnit enemy)
        {
            float straight = AiCombatMath.Distance(unit, enemy);
            var terrain = table.Terrain.Objects.ToList();
            if (!terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Impassible))) return straight;

            var mine = unit.Models.Where(m => m.GetIsAlive()).ToList();
            var theirs = enemy.Models.Where(m => m.GetIsAlive()).ToList();
            if (mine.Count == 0 || theirs.Count == 0) return straight;

            (IModel From, IModel To) pair = (mine[0], theirs[0]);
            float best = float.MaxValue;
            foreach (IModel m in mine)
                foreach (IModel e in theirs)
                {
                    float d = Position.GetDistance2D(m.Position, e.Position);
                    if (d < best) { best = d; pair = (m, e); }
                }

            ITerrain? building = SolidBuildings.Holding(enemy, terrain);
            var obstacles = building == null ? terrain : terrain.Where(t => !ReferenceEquals(t, building)).ToList();
            float radius = pair.From.BaseShape.CircumscribedRadiusInches;
            float theirRadius = pair.To.BaseShape.CircumscribedRadiusInches;
            List<Position> route = RouteMetrics.Route(obstacles, () => TerrainGridCache.Get(obstacles, radius),
                pair.From.Position, pair.To.Position, radius);

            if (building == null) return Math.Max(straight, RouteMetrics.Length(route) - radius - theirRadius);

            // Walk the route until it reaches the building's wall.
            float walked = 0f;
            for (int i = 1; i < route.Count; i++)
            {
                var a = new Float2(route[i - 1].x, route[i - 1].z);
                var b = new Float2(route[i].x, route[i].z);
                if (building.Shape.GetFirstSegmentEntry(a, b) is { } entry)
                    return Math.Max(0f, walked + Position.GetDistance2D(route[i - 1], new Position(entry.X, entry.Y)) - radius);
                walked += Position.GetDistance2D(route[i - 1], route[i]);
            }
            return Math.Max(0f, walked - radius);
        }
    }
}
