using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// A placement area built from the Attack &amp; Defend missions' wording (Advanced Rulebooks 3.6.0): "within 12" of
    /// the marker", "within 12" of any table edge", "within 6" of at least one other friendly unit", each optionally
    /// "over 12" away from the marker". <see cref="Include"/> is the area a model's centre must be in (null = the whole
    /// table); <see cref="Holes"/> are circles it must stay out of. Like every non-rectangular placement zone, it judges
    /// model CENTRES, so a hole's radius is widened by the placing unit's base when it is built.
    /// </summary>
    public sealed class MissionPlacementZone : IBoundedZone
    {
        public float TableWidthInches { get; }
        public float TableHeightInches { get; }
        public IZone? Include { get; }
        public IReadOnlyList<CircularZone> Holes { get; }

        [JsonConstructor]
        public MissionPlacementZone(float tableWidthInches, float tableHeightInches, IZone? include,
            IReadOnlyList<CircularZone>? holes)
        {
            TableWidthInches = tableWidthInches;
            TableHeightInches = tableHeightInches;
            Include = include;
            Holes = holes ?? Array.Empty<CircularZone>();
        }

        public ZoneBounds Bounds
        {
            get
            {
                if (Include is IBoundedZone bounded)
                {
                    ZoneBounds b = bounded.Bounds;
                    return new ZoneBounds(Math.Max(0f, b.Left), Math.Min(TableWidthInches, b.Right),
                        Math.Max(0f, b.Bottom), Math.Min(TableHeightInches, b.Top));
                }
                return new ZoneBounds(0f, TableWidthInches, 0f, TableHeightInches);
            }
        }

        public bool IsPointWithinZone(Float2 position)
        {
            if (position.X < 0f || position.X > TableWidthInches || position.Y < 0f || position.Y > TableHeightInches)
                return false;
            if (Include != null && !Include.IsPointWithinZone(position)) return false;
            foreach (CircularZone hole in Holes)
                if (hole.IsPointWithinZone(position)) return false;
            return true;
        }

        // Paths through a placement zone are never walked (it is only placed into); the shape's own answers are the
        // honest approximation.
        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => Include?.DoesPathIntersectZone(startPosition, endPosition) ?? true;

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
            => Include?.DoesPathIntersectZone(startPosition, endPosition, inflationRadius) ?? true;

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
            => Include?.GetFirstSegmentEntry(startPosition, endPosition) ?? startPosition;

        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
            => Include?.GetLastSegmentExit(startPosition, endPosition);
    }
}
