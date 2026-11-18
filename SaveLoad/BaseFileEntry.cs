namespace FDG.SaveLoad
{
    /// <summary> The base shapes an army-list entry can choose (#149). More may be added later. </summary>
    public enum EBaseShapeKind
    {
        Circle,
        Rectangle,
    }

    /// <summary>
    /// The per-unit base footprint as authored in a <c>.fdgarmy</c> file (#149). Stored as plain primitives
    /// (a string enum + dimensions in inches) rather than a polymorphic shape, so it needs no custom STJ
    /// converter and an old army file with no base simply keeps the defaults (a 28mm circle). Dimensions are
    /// in INCHES: a circle uses <see cref="DiameterInches"/>; a rectangle uses <see cref="WidthInches"/> ×
    /// <see cref="HeightInches"/>. <see cref="ToBaseShape"/> turns it into the engine <see cref="IBaseShape"/>.
    /// </summary>
    [Serializable]
    public class BaseFileEntry
    {
        public EBaseShapeKind Shape { get; set; } = EBaseShapeKind.Circle;

        public float DiameterInches { get; set; } = BaseShapeDefaults.CircleDiameterInches;

        public float WidthInches { get; set; } = BaseShapeDefaults.RectangleWidthInches;

        public float HeightInches { get; set; } = BaseShapeDefaults.RectangleHeightInches;

        /// <summary>
        /// Kai, 2026-11-18 (house rule): every model stands on a ROUND base. Army Forge gives big models an oval
        /// ("120x92") and vehicles only a square/rectangle ("175x125"); both become a circle as wide as the LONG
        /// side, so the model never hangs off its base. A Rectangle entry left in an older army file is read the
        /// same way, so lists saved before the change play on circles too.
        /// </summary>
        public IBaseShape ToBaseShape() => new CircleBase(RoundDiameterInches / 2f);

        /// <summary>The diameter this entry plays at: its circle, or the long side of a rectangle/oval.</summary>
        public float RoundDiameterInches =>
            Shape == EBaseShapeKind.Rectangle ? Math.Max(WidthInches, HeightInches) : DiameterInches;

        /// <summary>This entry as a circle (see <see cref="ToBaseShape"/>).</summary>
        public BaseFileEntry AsRound() => Shape == EBaseShapeKind.Circle
            ? this
            : new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = RoundDiameterInches };
    }
}
