using FDG.Data;

namespace FDG
{
    /// <summary>
    /// Heights on the table (Kai, 2026-11-22). A terrain piece is a set of parts, each a footprint standing from the
    /// table to a height: a piece whose rules shape was read from its model carries one <see cref="RaisedRectZone"/> per
    /// solid part (each its own height); any other piece is its whole shape at <see cref="ITerrain.HeightInches"/>
    /// (no height recorded = as tall as the classic top-down method treats every piece).
    /// </summary>
    public static class TerrainHeights
    {
        /// <summary>The piece's parts and how tall each stands.</summary>
        public static IEnumerable<(IZone Zone, float Top)> Parts(ITerrain piece)
        {
            bool any = false;
            foreach ((IZone zone, float top) in RaisedParts(piece.Shape))
            {
                any = true;
                yield return (zone, top);
            }
            if (!any) yield return (piece.Shape, piece.HeightInches > 0f ? piece.HeightInches : float.MaxValue);
        }

        /// <summary>Whether this piece's height was read part by part (its model's solid parts).</summary>
        public static bool HasPartHeights(ITerrain piece) => RaisedParts(piece.Shape).Any();

        // A raised part as it sits on the table (still turned, when the piece is), with its height.
        private static IEnumerable<(IZone Zone, float Top)> RaisedParts(IZone zone)
        {
            switch (zone)
            {
                case RaisedRectZone r:
                    yield return (r, r.HeightInches);
                    break;
                case RotatedZoneWrapper { Inner: RaisedRectZone r } w:
                    yield return (w, r.HeightInches);
                    break;
                case CompositeZone c:
                    foreach (IZone part in c.Parts)
                        foreach (var r in RaisedParts(part)) yield return r;
                    break;
            }
        }

        /// <summary>How tall the piece stands at a point (its tallest part there), or null when the point is off it.</summary>
        public static float? TopAt(ITerrain piece, Float2 point)
        {
            float? best = null;
            foreach ((IZone zone, float top) in Parts(piece))
                if (zone.IsPointWithinZone(point) && (best == null || top > best)) best = top;
            return best;
        }
    }
}
