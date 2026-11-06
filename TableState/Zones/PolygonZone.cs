using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// A deployment zone made of one or more CONVEX polygons - the triangles and wedges of the Advanced
    /// Rulebooks' deployment styles (Side Battle, Disordered, Spearhead, Long Haul, Lightning Strike), which
    /// no set of rectangles can draw. Kai sent the GF: Firefight / AoF: Skirmish pages on 2026-11-05.
    ///
    /// <para>
    /// Same trick as <see cref="MultiRectZone"/>: it IS a <see cref="RectangularZone"/> whose own edges are
    /// the bounding box of its parts, with the geometry overridden to answer for the union. The deployment
    /// pipeline is typed to <c>RectangularZone</c> from the zone pick to the saved game, and a zone that is
    /// one needs none of that re-typed.
    /// </para>
    ///
    /// <para>
    /// <b>Convex parts only</b>, and parts that at most touch at a point. Every shape in the books is like
    /// that, and it is what makes the footprint test exact: a box is inside a convex polygon exactly when its
    /// four corners are, and two parts that share no edge cannot hold a box between them.
    /// </para>
    ///
    /// <para>
    /// Stored as flat coordinate lists (x0, z0, x1, z1, ...) so the saved game and the network carry plain
    /// numbers. Each polygon is normalised to counter-clockwise on construction.
    /// </para>
    /// </summary>
    public sealed class PolygonZone : RectangularZone
    {
        /// <summary>The polygons, each as x0, z0, x1, z1, ... counter-clockwise. Never empty.</summary>
        public IReadOnlyList<float[]> Polygons { get; }

        private readonly Float2[][] _points;

        public PolygonZone(IReadOnlyList<float[]> polygons)
            : base(Bound(polygons, 0, min: true), Bound(polygons, 0, min: false),
                   Bound(polygons, 1, min: true), Bound(polygons, 1, min: false))
        {
            _points = polygons.Select(ToCcw).ToArray();
            Polygons = _points.Select(p => p.SelectMany(v => new[] { v.X, v.Y }).ToArray()).ToArray();
        }

        /// <summary>A zone from vertex lists, e.g. <c>Of(new[] { (0f, 48f), (24f, 48f), (0f, 24f) })</c>.</summary>
        public static PolygonZone Of(params (float x, float z)[][] polygons) =>
            new(polygons.Select(p => p.SelectMany(v => new[] { v.x, v.z }).ToArray()).ToArray());

        /// <summary>Each polygon's vertices, counter-clockwise.</summary>
        public IReadOnlyList<IReadOnlyList<Float2>> Vertices => _points;

        private static float Bound(IReadOnlyList<float[]> polygons, int axis, bool min)
        {
            if (polygons == null || polygons.Count == 0)
                throw new ArgumentException("A polygon zone needs at least one polygon.", nameof(polygons));
            float best = min ? float.MaxValue : float.MinValue;
            foreach (float[] p in polygons)
            {
                if (p == null || p.Length < 6 || p.Length % 2 != 0)
                    throw new ArgumentException("A polygon needs at least three x,z pairs.", nameof(polygons));
                for (int i = axis; i < p.Length; i += 2)
                    best = min ? MathF.Min(best, p[i]) : MathF.Max(best, p[i]);
            }
            return best;
        }

        private static Float2[] ToCcw(float[] flat)
        {
            var pts = new Float2[flat.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Float2(flat[i * 2], flat[i * 2 + 1]);
            float area2 = 0f;
            for (int i = 0; i < pts.Length; i++)
            {
                Float2 a = pts[i], b = pts[(i + 1) % pts.Length];
                area2 += a.X * b.Y - b.X * a.Y;
            }
            if (area2 < 0f) Array.Reverse(pts);
            return pts;
        }

        private const float Eps = 1e-4f;

        private static bool InConvex(Float2[] poly, Float2 p)
        {
            for (int i = 0; i < poly.Length; i++)
            {
                Float2 a = poly[i], b = poly[(i + 1) % poly.Length];
                float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (cross < -Eps) return false;
            }
            return true;
        }

        public override bool IsPointWithinZone(Float2 position)
        {
            foreach (Float2[] poly in _points)
                if (InConvex(poly, position)) return true;
            return false;
        }

        /// <summary>
        /// Whether the whole box is inside one part. Exact for convex parts that share no edge (the class
        /// contract): a box lies in a convex polygon exactly when its corners do.
        /// </summary>
        public override bool ContainsFootprint(float centreX, float centreZ, float halfX, float halfZ)
        {
            var corners = new[]
            {
                new Float2(centreX - halfX, centreZ - halfZ), new Float2(centreX + halfX, centreZ - halfZ),
                new Float2(centreX + halfX, centreZ + halfZ), new Float2(centreX - halfX, centreZ + halfZ),
            };
            foreach (Float2[] poly in _points)
                if (corners.All(c => InConvex(poly, c))) return true;
            return false;
        }

        public override bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition) =>
            DoesPathIntersectZone(startPosition, endPosition, 0f);

        public override bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
        {
            foreach (Float2[] poly in _points)
            {
                if (InConvex(poly, startPosition) || InConvex(poly, endPosition)) return true;
                for (int i = 0; i < poly.Length; i++)
                    if (SegmentDistance(startPosition, endPosition, poly[i], poly[(i + 1) % poly.Length])
                        <= inflationRadius + Eps) return true;
            }
            return false;
        }

        public override Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(startPosition)) return startPosition;
            float best = float.MaxValue;
            Float2? hit = null;
            foreach ((float t, Float2 p) in Crossings(startPosition, endPosition))
                if (t < best) { best = t; hit = p; }
            return hit;
        }

        public override Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(endPosition)) return null;
            float best = float.MinValue;
            Float2? hit = null;
            foreach ((float t, Float2 p) in Crossings(startPosition, endPosition))
                if (t > best) { best = t; hit = p; }
            return hit;
        }

        private IEnumerable<(float t, Float2 p)> Crossings(Float2 s, Float2 e)
        {
            foreach (Float2[] poly in _points)
                for (int i = 0; i < poly.Length; i++)
                    if (Intersect(s, e, poly[i], poly[(i + 1) % poly.Length], out float t))
                        yield return (t, new Float2(s.X + (e.X - s.X) * t, s.Y + (e.Y - s.Y) * t));
        }

        private static bool Intersect(Float2 p, Float2 p2, Float2 q, Float2 q2, out float t)
        {
            float rX = p2.X - p.X, rY = p2.Y - p.Y, sX = q2.X - q.X, sY = q2.Y - q.Y;
            float denom = rX * sY - rY * sX;
            t = 0f;
            if (MathF.Abs(denom) < 1e-9f) return false;
            float qpX = q.X - p.X, qpY = q.Y - p.Y;
            t = (qpX * sY - qpY * sX) / denom;
            float u = (qpX * rY - qpY * rX) / denom;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }

        private static float SegmentDistance(Float2 a, Float2 b, Float2 c, Float2 d)
        {
            if (Intersect(a, b, c, d, out _)) return 0f;
            return MathF.Min(MathF.Min(PointSeg(a, c, d), PointSeg(b, c, d)),
                             MathF.Min(PointSeg(c, a, b), PointSeg(d, a, b)));
        }

        private static float PointSeg(Float2 p, Float2 a, Float2 b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y;
            float len2 = dx * dx + dy * dy;
            float t = len2 < 1e-12f ? 0f : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2, 0f, 1f);
            float ex = a.X + dx * t - p.X, ey = a.Y + dy * t - p.Y;
            return MathF.Sqrt(ex * ex + ey * ey);
        }
    }
}
