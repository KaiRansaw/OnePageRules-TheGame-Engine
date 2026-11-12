using System;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// #029 — Aircraft forced movement. GF Core / Advanced Rules 3.6.0, Aircraft: <i>"May only use Advance actions,
    /// moving in a straight line, and adding 30" to its total move ... Aircraft can't move into the table edges to
    /// move less than 30""</i>. So every activation the Aircraft picks a straight line - any direction - and flies
    /// 30-36" along it, and a line that would run into a table edge before 30" is not allowed. It never leaves the
    /// table. (Until 2026-11-11 the engine kept a fixed heading and let the Aircraft fly off the edge and redeploy
    /// next round - an older edition's rule, retired under the current-books ruling of 2026-11-01. Kai, 2026-11-11:
    /// "you shouldn't be able to make a unit go off the table physically".)
    /// </summary>
    public static class ForcedAircraftMove
    {
        /// <summary>The forced Advance's distance band — the unit flies 30–36" straight ahead, player's choice.</summary>
        public const float MinDistanceInches = 30f;
        public const float MaxDistanceInches = 36f;

        // Living models of one Aircraft must share a heading to within this tolerance (they move rigidly and are
        // aimed together, so any larger divergence is a bug, not float drift).
        private const float HeadingMatchToleranceInches = 0.01f;

        /// <summary>
        /// The way the Aircraft faces now (a unit vector): the shared <see cref="IModel.Facing"/> of its living
        /// models - as placed at deployment, then the line of its last flight. It is the suggested line for the next
        /// move; the player may pick another (<see cref="AircraftAdvanceResult.Heading"/>). Asserts all living
        /// models face the same way.
        /// </summary>
        public static Float2 GetHeading(UnitData unit)
        {
            Float2? shared = null;
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
            {
                if (!model.GetValue().GetIsAlive()) continue;
                Float2 facing = model.GetValue().Facing;
                if (shared is not Float2 s)
                {
                    shared = facing;
                    continue;
                }
                if (MathF.Abs(s.X - facing.X) > HeadingMatchToleranceInches ||
                    MathF.Abs(s.Y - facing.Y) > HeadingMatchToleranceInches)
                {
                    throw new InvalidOperationException(
                        $"Aircraft '{unit.Name}' has models with divergent facing - an Aircraft's models must share one heading.");
                }
            }
            return shared ?? new Float2(0f, 1f);
        }

        /// <summary>
        /// The rigid straight-line move: every living model translates by <paramref name="heading"/> ×
        /// <paramref name="distanceInches"/>. The whole unit shifts by the same delta, so coherency is preserved.
        /// </summary>
        public static List<ModelMoveEntry> BuildPaths(DataBinding<UnitData> unit, Float2 heading, float distanceInches)
        {
            float dx = heading.X * distanceInches;
            float dz = heading.Y * distanceInches;

            List<ModelMoveEntry> paths = new List<ModelMoveEntry>();
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                if (!model.GetIsAlive()) continue;
                Position from = model.GetValue().PositionBinding.GetValue();
                paths.Add(new ModelMoveEntry(model, new List<Position> { new Position(from.x + dx, from.z + dz) }));
            }
            return paths;
        }

        /// <summary>
        /// Whether the move <paramref name="paths"/> would carry any model's base off the table — its centre
        /// would land outside [r, W−r] × [r, H−r], with r the CIRCUMSCRIBING radius so a rotated rectangular
        /// base is judged at any heading (#150).
        /// </summary>
        public static bool WouldLeaveTable(List<ModelMoveEntry> paths)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                Position end = move.Positions[move.Positions.Count - 1];
                float r = move.Model.GetValue().BaseShape.CircumscribedRadiusInches;
                if (end.x < r || end.x > w - r || end.z < r || end.z > h - r)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// How far the Aircraft can fly along <paramref name="heading"/> before any living model's base would cross a
        /// table edge (same bounds as <see cref="WouldLeaveTable"/>), capped at <see cref="MaxDistanceInches"/>.
        /// </summary>
        public static float RoomAlong(DataBinding<UnitData> unit, Float2 heading)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float room = MaxDistanceInches;
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                if (!model.GetIsAlive()) continue;
                Position p = model.GetValue().PositionBinding.GetValue();
                float r = model.GetValue().BaseShape.CircumscribedRadiusInches;
                room = MathF.Min(room, RoomOnAxis(p.x, heading.X, r, w - r));
                room = MathF.Min(room, RoomOnAxis(p.z, heading.Y, r, h - r));
            }
            return MathF.Max(0f, room);
        }

        private static float RoomOnAxis(float at, float step, float min, float max)
        {
            if (step > 1e-5f) return (max - at) / step;
            if (step < -1e-5f) return (min - at) / step;
            return float.MaxValue;
        }

        /// <summary>A line the book allows: the full 30" fits on the table along it.</summary>
        public static bool IsLegalHeading(DataBinding<UnitData> unit, Float2 heading) =>
            RoomAlong(unit, Normalised(heading)) >= MinDistanceInches - 0.01f;

        /// <summary>
        /// <paramref name="wanted"/> if the book allows it, else the allowed line closest to it (searched a degree at
        /// a time either side). If no line on the table fits 30" (a tiny table), the line with the most room.
        /// </summary>
        public static Float2 LegalHeading(DataBinding<UnitData> unit, Float2 wanted)
        {
            Float2 w = Normalised(wanted);
            if (IsLegalHeading(unit, w)) return w;
            float a0 = MathF.Atan2(w.Y, w.X);
            Float2 best = w;
            float bestRoom = RoomAlong(unit, w);
            for (int deg = 1; deg <= 180; deg++)
            {
                foreach (int sign in new[] { 1, -1 })
                {
                    float a = a0 + sign * deg * MathF.PI / 180f;
                    var h = new Float2(MathF.Cos(a), MathF.Sin(a));
                    float room = RoomAlong(unit, h);
                    if (room >= MinDistanceInches - 0.01f) return h;
                    if (room > bestRoom) { best = h; bestRoom = room; }
                }
            }
            return best;
        }

        /// <summary>Turns every living model to face <paramref name="heading"/> (the line it is flying).</summary>
        public static void FaceAlong(UnitData unit, Float2 heading)
        {
            foreach (DataBinding<ModelData> model in unit.ModelBindings)
                if (model.GetValue().GetIsAlive()) model.GetValue().SetFacing(heading);
        }

        public static Float2 Normalised(Float2 v)
        {
            float len = MathF.Sqrt(v.X * v.X + v.Y * v.Y);
            return len < 1e-5f ? new Float2(0f, 1f) : new Float2(v.X / len, v.Y / len);
        }
    }
}
