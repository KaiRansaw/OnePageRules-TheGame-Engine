using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// A deployment zone made of several rectangles joined together - an L, a T, a band with a bite
    /// out of it. Kai asked for these on 2026-11-02: "can we add the ability to make oddly shaped
    /// deployment zones? right now its just a square/rectagle, but what if i wanted to do an L shaped
    /// deployment zone?"
    ///
    /// <para>
    /// It is a <see cref="RectangularZone"/> whose own Left/Right/Bottom/Top are the bounding box of
    /// all its parts, with the geometry overridden to answer for the union. That is deliberate and it
    /// is the whole reason this works: the deployment pipeline is typed to <c>RectangularZone</c> from
    /// <c>ChooseDeploymentZoneRequest</c> through the data bindings to the saved game, so an L-shaped
    /// zone that IS a rectangular zone needs none of that re-typed, and an old save still loads.
    /// </para>
    ///
    /// <para>
    /// Callers that follow the <see cref="IBoundedZone"/> contract - scan <see cref="Bounds"/> for
    /// candidates, then ask <see cref="IsPointWithinZone"/> - are correct automatically. Callers that
    /// treat the four edges AS the zone get the bounding box, which is a superset: they will offer
    /// ground in the notch. Those are the places to hunt when something lets a model stand where it
    /// should not.
    /// </para>
    /// </summary>
    public sealed class MultiRectZone : RectangularZone
    {
        /// <summary>The rectangles whose union is this zone. Never empty.</summary>
        public IReadOnlyList<RectangularZone> Parts { get; }

        public MultiRectZone(IReadOnlyList<RectangularZone> parts)
            : base(Checked(parts).Min(p => p.Left), Checked(parts).Max(p => p.Right),
                   Checked(parts).Min(p => p.Bottom), Checked(parts).Max(p => p.Top))
        {
            Parts = parts.ToArray();
        }

        /// <summary>
        /// Validates before the base constructor runs. The base call has to compute the bounding box
        /// from the parts, so an empty list would die inside Min with "Sequence contains no elements"
        /// - which says nothing about what the caller got wrong.
        /// </summary>
        private static IReadOnlyList<RectangularZone> Checked(IReadOnlyList<RectangularZone> parts)
        {
            if (parts == null || parts.Count == 0)
                throw new ArgumentException("A multi-rectangle zone needs at least one rectangle.", nameof(parts));
            return parts;
        }

        /// <summary>
        /// Builds the right kind of zone for a set of rectangles: a plain one for a single rectangle,
        /// so the common case carries no extra machinery and no behaviour changes for existing maps.
        /// </summary>
        public static RectangularZone Of(IReadOnlyList<RectangularZone> parts) =>
            parts.Count == 1 ? parts[0] : new MultiRectZone(parts);

        public override bool IsPointWithinZone(Float2 position)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].IsPointWithinZone(position)) return true;
            return false;
        }

        public override bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].DoesPathIntersectZone(startPosition, endPosition)) return true;
            return false;
        }

        public override bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i].DoesPathIntersectZone(startPosition, endPosition, inflationRadius)) return true;
            return false;
        }

        /// <summary>The earliest entry into ANY part - the first moment the segment is inside the union.</summary>
        public override Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(startPosition)) return startPosition;

            Float2? best = null;
            float bestDistSq = float.MaxValue;
            foreach (RectangularZone part in Parts)
            {
                if (part.GetFirstSegmentEntry(startPosition, endPosition) is not Float2 hit) continue;
                float dx = hit.X - startPosition.X, dy = hit.Y - startPosition.Y;
                float d = dx * dx + dy * dy;
                if (d < bestDistSq) { bestDistSq = d; best = hit; }
            }
            return best;
        }

        /// <summary>The latest exit from any part - where the segment finally leaves the union.</summary>
        public override Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
        {
            if (IsPointWithinZone(endPosition)) return null;

            Float2? best = null;
            float bestDistSq = -1f;
            foreach (RectangularZone part in Parts)
            {
                if (part.GetLastSegmentExit(startPosition, endPosition) is not Float2 hit) continue;
                float dx = hit.X - startPosition.X, dy = hit.Y - startPosition.Y;
                float d = dx * dx + dy * dy;
                if (d > bestDistSq) { bestDistSq = d; best = hit; }
            }
            return best;
        }

        /// <summary>
        /// Whether the whole footprint is inside the union - not merely its centre.
        ///
        /// <para>
        /// This is the question the notch of an L makes interesting. A model standing in the short arm
        /// an inch from the inner corner has its centre inside and half its base over ground that is
        /// not its zone, and a centre-only test would wave it through.
        /// </para>
        ///
        /// <para>
        /// Answered by carving the footprint up: the box is cut against each part in turn, and what
        /// survives is the area the union does not cover. Nothing left means fully inside. That is
        /// exact for any number of axis-aligned rectangles, where testing corners is not - a box can
        /// have all four corners in different parts and a hole in the middle.
        /// </para>
        /// </summary>
        public override bool ContainsFootprint(float centreX, float centreZ, float halfX, float halfZ)
        {
            var remaining = new List<(float L, float R, float B, float T)>
            {
                (centreX - halfX, centreX + halfX, centreZ - halfZ, centreZ + halfZ),
            };

            foreach (RectangularZone part in Parts)
            {
                var next = new List<(float L, float R, float B, float T)>(remaining.Count * 4);
                foreach (var box in remaining) Subtract(box, part, next);
                remaining = next;
                if (remaining.Count == 0) return true;
            }
            return remaining.Count == 0;
        }

        /// <summary>
        /// Adds the parts of <paramref name="box"/> that <paramref name="cut"/> does not cover to
        /// <paramref name="into"/>, as up to four axis-aligned pieces (below, above, left, right).
        /// Slivers thinner than a thousandth of an inch are dropped - they are float noise from the
        /// pixel-to-inch conversion, not real uncovered ground.
        /// </summary>
        private static void Subtract((float L, float R, float B, float T) box, RectangularZone cut,
            List<(float L, float R, float B, float T)> into)
        {
            const float eps = 0.001f;

            // No overlap at all: the box survives whole.
            if (cut.Right <= box.L + eps || cut.Left >= box.R - eps
                || cut.Top <= box.B + eps || cut.Bottom >= box.T - eps)
            {
                into.Add(box);
                return;
            }

            float midB = MathF.Max(box.B, cut.Bottom);
            float midT = MathF.Min(box.T, cut.Top);

            if (cut.Bottom > box.B + eps) into.Add((box.L, box.R, box.B, cut.Bottom));
            if (cut.Top < box.T - eps) into.Add((box.L, box.R, cut.Top, box.T));
            if (cut.Left > box.L + eps) into.Add((box.L, cut.Left, midB, midT));
            if (cut.Right < box.R - eps) into.Add((cut.Right, box.R, midB, midT));
        }
    }
}
