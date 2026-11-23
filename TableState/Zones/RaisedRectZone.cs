using Newtonsoft.Json;

namespace FDG
{
    /// <summary>
    /// A rectangle with a height: one solid part of a terrain piece, standing from the table up to
    /// <see cref="HeightInches"/>. Kai, 2026-11-22 ("Heights", read from the models): a ruin's broken wall is tall, its
    /// low rubble short, so 3D line of sight (Volumetric Line of Sight) can see over the low parts, and models can stand
    /// on a part's top (Climbable / Steep pieces).
    ///
    /// <para>It is a <see cref="RectangularZone"/> in every other way - everything that draws, moves around or tests a
    /// rectangle treats it as one - and it may sit inside a <see cref="RotatedZoneWrapper"/> like any rectangle.</para>
    /// </summary>
    public sealed class RaisedRectZone : RectangularZone
    {
        public float HeightInches { get; }

        [JsonConstructor]
        public RaisedRectZone(float left, float right, float bottom, float top, float heightInches)
            : base(left, right, bottom, top)
        {
            HeightInches = MathF.Max(0f, heightInches);
        }
    }
}
