using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG
{
    /// <summary>
    /// #460 - Fast-Play <b>Simplified Terrain</b> (GF / AoF Advanced Rulebooks 3.6.0, p17): <i>"all pieces of terrain
    /// must be treated like solid buildings, with clearly defined areas that can be entered and exited. When using
    /// these rules, the only way for units to get Cover is by entering a piece of terrain."</i> The Solid Buildings
    /// rules it points at (p33), word for word the same in both books:
    /// <list type="bullet">
    ///   <item><i>"Solid buildings may hold up to X friendly models or Heroes with up to Tough(6), and non-Heroes with
    ///   up to Tough(3) which occupy 3 spaces each."</i> - the Transport rule's own wording, so its space costs and
    ///   ride caps are reused (<see cref="TransportUtilities"/>).</item>
    ///   <item><i>"Units may enter/exit by using any move action, getting into contact with the building to enter, and
    ///   staying within 6" when exiting, however units may not both enter and exit a building as part of the same
    ///   activation."</i></item>
    ///   <item><i>"Units may shoot into and out of any side of a solid building that has some sort of firing port"</i> -
    ///   Kai, 2026-11-15: every side has one. <i>"Units inside of solid buildings count as being in Cover."</i></item>
    /// </list>
    ///
    /// <para><b>How it is played here.</b> Models stand inside the piece's footprint - the game draws them on it -
    /// so everything that already measures from models (range, objectives, cover) works unchanged. Each piece is
    /// stamped at set-up (<see cref="Simplified"/>): Impassable to everything except a unit entering or leaving it,
    /// sight passes into and out of it but not through it (the Dense rule), and models inside are in Cover.
    /// Capacity by size (Kai, 2026-11-15, the book's guideline): small 6, medium 11, large 21.</para>
    ///
    /// <para><b>Not yet (#460 slice 2):</b> charging a unit inside ("only one unit inside of it is targeted, and the
    /// defender may choose which"), and the bots entering buildings - they treat every piece as a wall. The book's
    /// optional Building Destruction and Collateral Damage are left out (Kai, 2026-11-15: not now).</para>
    /// </summary>
    public static class SolidBuildings
    {
        /// <summary>A piece's types once Simplified Terrain makes it a solid building.</summary>
        public static ETerrainType Simplified(ETerrainType declared) =>
            (declared & ~(ETerrainType.Blocking | ETerrainType.Difficult | ETerrainType.Steep | ETerrainType.Climbable))
            | ETerrainType.SolidBuilding | ETerrainType.Impassible | ETerrainType.Dense | ETerrainType.Cover;

        public static bool Is(ITerrain t) => t.TerrainType.HasFlag(ETerrainType.SolidBuilding);

        /// <summary>"Exiting... staying within 6"."</summary>
        public const float ExitWithinInches = 6f;

        public const int SmallCapacity = 6, MediumCapacity = 11, LargeCapacity = 21;

        /// <summary>Footprint area (square inches) up to which a piece counts as small / medium.</summary>
        public const float SmallUpToSqIn = 24f, MediumUpToSqIn = 72f;

        /// <summary>How many spaces the piece holds - by its footprint (Kai: "by size").</summary>
        public static int Capacity(ITerrain t)
        {
            float area = Area(t.Shape);
            return area <= SmallUpToSqIn ? SmallCapacity : area <= MediumUpToSqIn ? MediumCapacity : LargeCapacity;
        }

        /// <summary>The footprint's area, sampled on a quarter-inch grid over its bounds.</summary>
        public static float Area(IZone shape)
        {
            if (shape is not IBoundedZone bounded) return MediumUpToSqIn;
            ZoneBounds b = bounded.Bounds;
            const float step = 0.25f;
            int inside = 0;
            for (float x = b.Left + step / 2; x < b.Right; x += step)
                for (float z = b.Bottom + step / 2; z < b.Top; z += step)
                    if (shape.IsPointWithinZone(new Float2(x, z))) inside++;
            return inside * step * step;
        }

        private static bool Inside(ITerrain t, Position p) => t.Shape.IsPointWithinZone(new Float2(p.x, p.z));

        /// <summary>The building a unit is in: the piece holding its first living model, or null.</summary>
        public static ITerrain? Holding(IUnit unit, IEnumerable<ITerrain> terrain)
        {
            IModel? first = unit.Models.FirstOrDefault(m => m.GetIsAlive());
            return first == null ? null : terrain.FirstOrDefault(t => Is(t) && Inside(t, first.Position));
        }

        /// <summary>Spaces a unit takes inside (the Transport rule's), or null when one of its models is too big to go in.</summary>
        public static int? Spaces(IUnit unit)
        {
            int spaces = 0;
            foreach (IModel m in unit.Models.Where(m => m.GetIsAlive()))
            {
                bool hero = TransportUtilities.IsHeroModel(m, unit);
                if (!TransportUtilities.CanModelRide(m, hero)) return null;
                spaces += TransportUtilities.GetModelSpaceCost(m, hero);
            }
            return spaces;
        }

        /// <summary>
        /// Whether a model's path may cross this piece: only the building the model starts in (leaving) or ends in
        /// (entering). Everything else stays Impassable.
        /// </summary>
        public static bool MayCross(ITerrain t, Position start, Position end) => Is(t) && (Inside(t, start) || Inside(t, end));

        /// <summary>
        /// Null when the move keeps the building rules, else why not (plain words, ASCII - it is shown on the move
        /// screen). <paramref name="allied"/> says whether another unit is on the mover's side.
        /// </summary>
        public static string? MoveProblem(IUnit unit, IReadOnlyList<ModelMoveEntry> moves, IReadOnlyList<ITerrain> terrain,
            IEnumerable<IUnit> allUnits, Func<IUnit, bool> allied)
        {
            var buildings = terrain.Where(Is).ToList();
            if (buildings.Count == 0) return null;

            var starts = new List<Position>();
            var ends = new List<Position>();
            var moved = moves.ToDictionary(m => m.Model.GetValue().ID, m => m);
            foreach (IModel model in unit.Models.Where(m => m.GetIsAlive()))
            {
                starts.Add(model.Position);
                ends.Add(moved.TryGetValue(model.ID, out ModelMoveEntry? move) && move.Positions.Count > 0
                    ? move.Positions[^1] : model.Position);
            }
            if (ends.Count == 0) return null;

            ITerrain? from = buildings.FirstOrDefault(b => Inside(b, starts[0]));
            ITerrain? to = null;
            foreach (ITerrain b in buildings)
            {
                int n = ends.Count(p => Inside(b, p));
                if (n == 0) continue;
                if (n < ends.Count) return "The whole unit has to go inside the building, or none of it.";
                to = b;
            }

            if (from != null && to == null)
            {
                if (ends.Any(p => !from.Shape.DoesPathIntersectZone(new Float2(p.x, p.z), new Float2(p.x + 0.001f, p.z), ExitWithinInches)))
                    return "A unit leaving a building has to stay within 6\" of it.";
                return null;
            }
            if (to == null || ReferenceEquals(to, from)) return null;
            if (from != null) return "A unit can't leave one building and enter another in the same activation.";

            var others = allUnits.Where(u => !ReferenceEquals(u, unit) && u.GetIsAlive()
                                             && ReferenceEquals(Holding(u, buildings), to)).ToList();
            if (others.Any(u => !allied(u))) return "The enemy holds that building.";
            if (Spaces(unit) is not int mine) return "A model in this unit is too big to go inside a building.";
            int used = others.Sum(u => Spaces(u) ?? 0);
            int capacity = Capacity(to);
            if (used + mine > capacity)
                return $"That building holds {capacity} spaces; {used} are taken and this unit needs {mine}.";
            return null;
        }
    }
}
