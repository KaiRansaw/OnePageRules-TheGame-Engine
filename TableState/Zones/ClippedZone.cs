using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// A zone cut down to a rectangle: a point is in it when it is in <see cref="Inner"/> and in <see cref="Clip"/>.
    /// Fog of War's Surprise Engagement places a unit in one section of a deployment zone that is not a plain
    /// rectangle (an Advanced deployment style's L or triangles). Like every non-rectangular placement zone, it
    /// judges model CENTRES.
    /// </summary>
    public sealed class ClippedZone : IBoundedZone
    {
        public IZone Inner { get; }
        public RectangularZone Clip { get; }

        [JsonConstructor]
        public ClippedZone(IZone inner, RectangularZone clip)
        {
            Inner = inner;
            Clip = clip;
        }

        public ZoneBounds Bounds
        {
            get
            {
                ZoneBounds c = Clip.Bounds;
                if (Inner is not IBoundedZone b) return c;
                ZoneBounds i = b.Bounds;
                return new ZoneBounds(Math.Max(i.Left, c.Left), Math.Min(i.Right, c.Right),
                    Math.Max(i.Bottom, c.Bottom), Math.Min(i.Top, c.Top));
            }
        }

        public bool IsPointWithinZone(Float2 position) => Clip.IsPointWithinZone(position) && Inner.IsPointWithinZone(position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => Clip.DoesPathIntersectZone(startPosition, endPosition) && Inner.DoesPathIntersectZone(startPosition, endPosition);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
            => Clip.DoesPathIntersectZone(startPosition, endPosition, inflationRadius)
               && Inner.DoesPathIntersectZone(startPosition, endPosition, inflationRadius);

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
            => Inner.GetFirstSegmentEntry(startPosition, endPosition);

        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
            => Inner.GetLastSegmentExit(startPosition, endPosition);
    }
}
