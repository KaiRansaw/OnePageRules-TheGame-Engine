using System;
using System.Collections.Generic;
using FDG.Data;

namespace FDG
{
    /// <summary>
    /// Lays a unit out along a line the player draws, rather than at a point they click.
    ///
    /// <para>
    /// Kai's choice on 2026-11-02, from three sketched gestures: "drag a line, models fill it" - press
    /// where the left-hand model goes, drag to where the right-hand one goes, release. With one
    /// amendment he asked for on the spot: "can we somehow make it capable of making models curved on
    /// the line? so there's an option/way to not have them move/deploy in a straight line?"
    /// </para>
    ///
    /// <para>
    /// So the input is a <b>polyline</b>, not two endpoints - the actual path the cursor travelled.
    /// Drag straight and you get a straight line; sweep the mouse and the unit curves to follow it. No
    /// extra control to learn and no mode to be in: the shape of the drag IS the shape of the unit.
    /// </para>
    ///
    /// <para>
    /// Pure geometry, and in the engine rather than the front end on purpose: it is the same job for
    /// deploying and for moving, it is a rule-shaped decision (the spacing has to satisfy cohesion),
    /// and it survives the Godot port untouched.
    /// </para>
    /// </summary>
    public static class PathFormation
    {
        /// <summary>Where one model ends up, and which way it looks.</summary>
        public readonly record struct Placement(Position At, Float2 Facing);

        /// <summary>
        /// Base-to-base gap when models are packed as tightly as they go. Not zero: touching bases read
        /// as overlapping and some validators treat exact contact as a collision.
        /// </summary>
        public const float TightGapInches = 0.1f;

        /// <summary>
        /// How far apart models may be spread. The books' nearest-neighbour rule is 1", so a unit
        /// stretched further than this along the drag is not a wide formation - it is a broken one.
        /// A long drag therefore spreads the unit to this and no further, and the rest of the stroke
        /// goes unused rather than producing an illegal line.
        /// </summary>
        public static float MaxGapInches => GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES;

        /// <summary>
        /// Places <paramref name="widths"/>.Count models along <paramref name="path"/>.
        ///
        /// <para>
        /// Three cases, and the middle one is the point of the whole gesture:
        /// </para>
        /// <list type="bullet">
        /// <item><b>The drag is long enough to spread out.</b> Models sit <see cref="MaxGapInches"/>
        /// apart and the run is centred on the stroke, so the extra length you dragged past what the
        /// unit can legally span is ignored rather than tearing the unit apart.</item>
        /// <item><b>The drag fits the unit.</b> The gap is solved so the models exactly fill the stroke
        /// end to end. This is the normal case, and it is why dragging further spreads them out.</item>
        /// <item><b>The drag is too short for one row.</b> The unit wraps into rows behind the first,
        /// which is what Kai's sketch showed: "shorter drag = 3-2". A short drag is a request for a
        /// deeper formation, not an instruction to overlap.</item>
        /// </list>
        ///
        /// <para>
        /// Each model faces perpendicular to the path where it stands, turned to the side
        /// <paramref name="forward"/> points at - so a unit dropped along a curve has every model
        /// looking outward along its own part of the curve, not all parallel.
        /// </para>
        /// </summary>
        /// <param name="path">The cursor's path in table inches, in order. One point is treated as a
        /// zero-length stroke, which wraps the unit into rows at the tightest spacing.</param>
        /// <param name="widths">Each model's full width across the line, in inches, in the order they
        /// should stand from the start of the stroke.</param>
        /// <param name="depths">Each model's full depth, used to space the rows when the unit wraps.</param>
        /// <param name="forward">Which way the unit should face - normally toward the enemy. The facing
        /// is the path normal nearer to this.</param>
        public static Placement[] Along(IReadOnlyList<Position> path,
            IReadOnlyList<float> widths, IReadOnlyList<float> depths, Float2 forward)
        {
            int n = widths.Count;
            if (n == 0) return Array.Empty<Placement>();

            var pts = Simplify(path);
            if (pts.Count == 0) return Array.Empty<Placement>();
            if (pts.Count == 1) pts.Add(new Position(pts[0].x + 0.001f, pts[0].z));

            float pathLength = Length(pts);

            // How many models fit along the stroke at the tightest spacing. That decides whether this
            // is one row or several.
            int perRow = HowManyFitIn(pathLength, widths, TightGapInches);
            var result = new Placement[n];

            if (perRow >= n)
            {
                PlaceRow(pts, widths, 0, n, pathLength, forward, rowOffset: 0f, result);
                return result;
            }

            // Wrapping. Rows are laid back-to-front along the inward normal, each offset by the deepest
            // model in the row before it - so a wrapped unit is still a legal block, not a stack.
            int placed = 0, row = 0;
            float offset = 0f;
            while (placed < n)
            {
                int count = Math.Max(1, Math.Min(perRow, n - placed));
                PlaceRow(pts, widths, placed, count, pathLength, forward, offset, result);

                float deepest = 0f;
                for (int i = placed; i < placed + count; i++) deepest = MathF.Max(deepest, depths[i]);
                offset += deepest + TightGapInches;

                placed += count;
                row++;
                if (row > n) break;   // belt and braces: perRow >= 1 guarantees progress
            }
            return result;
        }

        /// <summary>
        /// Lays <paramref name="count"/> models, starting at <paramref name="first"/>, along the stroke
        /// at <paramref name="rowOffset"/> inches behind it.
        /// </summary>
        private static void PlaceRow(List<Position> pts, IReadOnlyList<float> widths,
            int first, int count, float pathLength, Float2 forward, float rowOffset, Placement[] into)
        {
            float widthSum = 0f;
            for (int i = first; i < first + count; i++) widthSum += widths[i];

            float gap;
            float span;
            if (count <= 1)
            {
                gap = 0f;
                span = widthSum;
            }
            else
            {
                float spare = pathLength - widthSum;
                gap = spare / (count - 1);
                gap = Math.Clamp(gap, TightGapInches, MaxGapInches);
                span = widthSum + gap * (count - 1);
            }

            // Centre the run on the stroke. When the unit fills the stroke this changes nothing; when
            // the drag was longer than the unit can legally span, it puts the unit in the middle of
            // where you dragged rather than bunched at the start.
            float cursor = MathF.Max(0f, (pathLength - span) * 0.5f);

            for (int i = first; i < first + count; i++)
            {
                float along = cursor + widths[i] * 0.5f;
                (Position at, Float2 tangent) = Sample(pts, along);

                Float2 normal = NormalToward(tangent, forward);
                Position shifted = rowOffset <= 0f
                    ? at
                    : new Position(at.x - normal.X * rowOffset, at.z - normal.Y * rowOffset);

                into[i] = new Placement(shifted, normal);
                cursor += widths[i] + gap;
            }
        }

        /// <summary>How many of the models from the start fit in <paramref name="length"/> inches.</summary>
        private static int HowManyFitIn(float length, IReadOnlyList<float> widths, float gap)
        {
            float used = 0f;
            int fit = 0;
            for (int i = 0; i < widths.Count; i++)
            {
                float next = used + (fit > 0 ? gap : 0f) + widths[i];
                if (fit > 0 && next > length) break;
                used = next;
                fit++;
            }
            return Math.Max(1, fit);
        }

        /// <summary>
        /// Drops points that are too close together to carry direction. A mouse drag arrives as a dense
        /// spray of nearly identical points, and a segment a thousandth of an inch long has a tangent
        /// that is pure noise - which would make every model's facing jitter.
        /// </summary>
        private static List<Position> Simplify(IReadOnlyList<Position> path)
        {
            const float minSegment = 0.15f;
            var outp = new List<Position>(path.Count);
            foreach (Position p in path)
            {
                if (outp.Count == 0) { outp.Add(p); continue; }
                Position last = outp[^1];
                float dx = p.x - last.x, dz = p.z - last.z;
                if (dx * dx + dz * dz >= minSegment * minSegment) outp.Add(p);
            }
            // Always keep the true end: a slow finish to the drag would otherwise be trimmed off, and
            // the unit would stop short of where the player let go.
            if (path.Count > 0 && outp.Count > 0)
            {
                Position end = path[^1], last = outp[^1];
                float dx = end.x - last.x, dz = end.z - last.z;
                if (dx * dx + dz * dz > 0.0001f) outp.Add(end);
            }
            return outp;
        }

        private static float Length(List<Position> pts)
        {
            float total = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                float dx = pts[i].x - pts[i - 1].x, dz = pts[i].z - pts[i - 1].z;
                total += MathF.Sqrt(dx * dx + dz * dz);
            }
            return total;
        }

        /// <summary>
        /// The point <paramref name="along"/> inches into the polyline, and the direction of travel
        /// there. Past the end it extends along the last segment rather than clamping, so a row that
        /// needs a little more room than the stroke has continues straight instead of piling every
        /// remaining model onto the final point.
        /// </summary>
        private static (Position At, Float2 Tangent) Sample(List<Position> pts, float along)
        {
            float remaining = MathF.Max(0f, along);
            for (int i = 1; i < pts.Count; i++)
            {
                float dx = pts[i].x - pts[i - 1].x, dz = pts[i].z - pts[i - 1].z;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len <= 0.0001f) continue;

                if (remaining <= len || i == pts.Count - 1)
                {
                    float t = remaining / len;
                    return (new Position(pts[i - 1].x + dx * t, pts[i - 1].z + dz * t),
                            new Float2(dx / len, dz / len));
                }
                remaining -= len;
            }
            return (pts[^1], new Float2(1f, 0f));
        }

        /// <summary>
        /// The perpendicular to <paramref name="tangent"/> that points the same way as
        /// <paramref name="forward"/>. A line has two normals and picking the wrong one has the unit
        /// facing its own deployment edge.
        /// </summary>
        private static Float2 NormalToward(Float2 tangent, Float2 forward)
        {
            var left = new Float2(-tangent.Y, tangent.X);
            float dot = left.X * forward.X + left.Y * forward.Y;
            if (dot >= 0f) return left;
            return new Float2(tangent.Y, -tangent.X);
        }
    }
}
