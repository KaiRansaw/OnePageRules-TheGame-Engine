using Newtonsoft.Json;

namespace FDG
{
    public interface ITerrain : IZone
    {
        /// <summary>
        /// Optional display name carried over from the palette entry the piece was placed from
        /// ("Boulder", "Shipping container"). Empty for unnamed pieces; purely cosmetic.
        /// </summary>
        string Name { get; }

        ETerrainType TerrainType { get; }

        IZone Shape { get; }

        float HeightInches { get; }

        /// <summary>The book terrain kind ("Forest", "Ruins"...) or empty - cosmetic, see <see cref="TerrainData.Kind"/>.</summary>
        string Kind => string.Empty;

        /// <summary>The map's picked 3D look, or empty for the game's own pick - cosmetic.</summary>
        string Look => string.Empty;

        /// <summary>
        /// What this piece does to a sight line drawn between two points. The default
        /// implementation on <see cref="TerrainData"/> reads <see cref="TerrainType"/>
        /// flags and queries <see cref="Shape"/>; custom terrain (doors, fog, mesh
        /// colliders, etc.) can override to inject state-dependent or richer behavior.
        /// </summary>
        ESightLineEffect EvaluateSightLine(Position attacker, Position target);
    }

    public class TerrainData : ITerrain
    {
        public string Name { get; }

        public ETerrainType TerrainType { get; }

        public IZone Shape { get; }

        public float HeightInches { get; }

        /// <summary>
        /// Which of the book's common terrain kinds this piece is (<see cref="TerrainBook.Kinds"/> id: "Forest",
        /// "Ruins"...), from the map it was placed from; empty for pieces made without one. Cosmetic - the rules are
        /// <see cref="TerrainType"/>. The front ends pick the 3D look from it (2026-11-14, terrain looks).
        /// </summary>
        public string Kind { get; }

        /// <summary>The look the map picked for this piece, or empty for the game's own pick. Cosmetic.</summary>
        public string Look { get; }

        //name is optional and last so pre-existing positional call sites (and saves/wire payloads
        //written before the field existed) keep working unchanged.
        [JsonConstructor]
        public TerrainData(ETerrainType terrainType, IZone shape, float heightInches, string? name = null,
            string? kind = null, string? look = null)
        {
            if (shape == null)
            {
                throw new ArgumentNullException(nameof(shape));
            }

            TerrainType = terrainType;
            Shape = shape;
            HeightInches = heightInches;
            Name = name ?? string.Empty;
            Kind = kind ?? string.Empty;
            Look = look ?? string.Empty;
        }

        public TerrainData(ETerrainType terrainType, IZone shape) : this(terrainType, shape, 0f) { }

        public bool IsPointWithinZone(Float2 position) => Shape.IsPointWithinZone(position);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition)
            => Shape.DoesPathIntersectZone(startPosition, endPosition);

        public bool DoesPathIntersectZone(Float2 startPosition, Float2 endPosition, float inflationRadius)
            => Shape.DoesPathIntersectZone(startPosition, endPosition, inflationRadius);

        public Float2? GetFirstSegmentEntry(Float2 startPosition, Float2 endPosition)
            => Shape.GetFirstSegmentEntry(startPosition, endPosition);

        public Float2? GetLastSegmentExit(Float2 startPosition, Float2 endPosition)
            => Shape.GetLastSegmentExit(startPosition, endPosition);

        public virtual ESightLineEffect EvaluateSightLine(Position attacker, Position target)
        {
            //2D footprint query. Height is intentionally ignored for now — when
            //heightmap-aware sight lines land, override this method on a height-aware
            //TerrainData subclass rather than complicating the default.
            Float2 a = new Float2(attacker.x, attacker.z);
            Float2 b = new Float2(target.x, target.z);

            if (TerrainType.HasFlag(ETerrainType.Blocking) && Shape.DoesPathIntersectZone(a, b))
            {
                return ESightLineEffect.Blocking;
            }

            // Dense (AoF: Quest): "Units may draw line of sight INTO AND OUT OF dense terrain, but not
            // THROUGH it to targets on the other side." So unlike Blocking, which stops any line that
            // touches it, this stops only a line that crosses the piece with BOTH ends outside - a unit
            // standing in a dense wood shoots out of it and is shot at normally. Checked after Blocking
            // because a piece carrying both is simply Blocking, which is the stricter reading.
            if (TerrainType.HasFlag(ETerrainType.Dense) && Shape.DoesPathIntersectZone(a, b)
                && !Shape.IsPointWithinZone(a) && !Shape.IsPointWithinZone(b))
            {
                return ESightLineEffect.Blocking;
            }

            if (TerrainType.HasFlag(ETerrainType.Cover) && Shape.DoesPathIntersectZone(a, b))
            {
                return ESightLineEffect.Cover;
            }
            return ESightLineEffect.Clear;
        }
    }
}
