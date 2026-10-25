using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// Horde Mode's "within 9\" of a table corner" placement region: a circle centred on the corner,
    /// clipped to the table. It IS a <see cref="CircularZone"/> (so renderers and the point/path geometry
    /// treat it as one), but it re-implements <see cref="IBoundedZone"/> so its <see cref="Bounds"/> - what
    /// the placement resolvers scan and inset bases against - stops at the table edges instead of hanging
    /// three quarters off the board.
    /// </summary>
    public sealed class TableCornerZone : CircularZone, IBoundedZone
    {
        public float TableWidthInches { get; }
        public float TableHeightInches { get; }

        [JsonConstructor]
        public TableCornerZone(Float2 center, float radius, float tableWidthInches, float tableHeightInches)
            : base(center, radius)
        {
            TableWidthInches = tableWidthInches;
            TableHeightInches = tableHeightInches;
        }

        /// <summary>The circle's bounding box clipped to the table.</summary>
        [JsonIgnore]
        public new ZoneBounds Bounds => new ZoneBounds(
            MathF.Max(0f, Center.X - Radius), MathF.Min(TableWidthInches, Center.X + Radius),
            MathF.Max(0f, Center.Y - Radius), MathF.Min(TableHeightInches, Center.Y + Radius));
    }
}
