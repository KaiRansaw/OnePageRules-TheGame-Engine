using System;
using System.Collections.Generic;

namespace FDG.SaveLoad
{
    [Serializable]
    public class TerrainLayoutFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgterrain";
        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// The table this map was drawn on, in inches. Defaults to the standard 6' x 4', which is also
        /// what every layout written before these fields gets - so an old map keeps the size it was
        /// actually drawn at and nothing shifts underneath it.
        ///
        /// <para>
        /// Stored as a SIZE rather than a game system on purpose (Kai, 2026-11-02): a 72x48 map is
        /// playable in any system that uses a 72x48 table, and tying a map to one system would hide it
        /// from the others for no reason. <see cref="GameWideConstants.TableSizeFor"/> is what
        /// turns a system into the size to compare against.
        /// </para>
        /// </summary>
        public float TableWidthInches { get; set; } = GameWideConstants.STANDARD_TABLE_WIDTH_INCHES;

        public float TableHeightInches { get; set; } = GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES;

        public List<TerrainPieceEntry> Pieces { get; set; } = new List<TerrainPieceEntry>();

        /// <summary>
        /// Map maker: model bases dropped on the table purely as size references, so the person drawing
        /// a map can see whether a unit actually fits through a gap. <b>The game ignores these
        /// completely</b> - they are not terrain, they block nothing and they are never placed. They are
        /// saved with the map only so they are still there when it is reopened.
        /// </summary>
        public List<MapBaseMarkerEntry> BaseMarkers { get; set; } = new List<MapBaseMarkerEntry>();

        /// <summary>
        /// Map maker: fixed objective markers. Empty (the default, and every layout written before this
        /// field) means the game rolls D3+2 and places objectives the usual way; non-empty means exactly
        /// these markers are placed, with no roll. Only read when the layout is the game's Custom Map.
        /// </summary>
        public List<MapObjectiveEntry> Objectives { get; set; } = new List<MapObjectiveEntry>();

        /// <summary>
        /// Map maker: custom deployment zones. Used instead of the standard long-edge strips when there
        /// are at least as many zones as teams; otherwise the standard zones are used.
        /// </summary>
        public List<MapDeploymentZoneEntry> DeploymentZones { get; set; } = new List<MapDeploymentZoneEntry>();
    }

    /// <summary>An objective marker's position on a custom map, in table inches.</summary>
    [Serializable]
    public class MapObjectiveEntry
    {
        public float X { get; set; }
        public float Z { get; set; }
    }

    /// <summary>
    /// A model base dropped on a map as a size reference. Carries a real
    /// <see cref="BaseFileEntry"/> rather than a bare diameter, so what the map maker draws is the same
    /// footprint an army list can actually give a model - a reference that disagreed with the game's own
    /// base shapes would be worse than none.
    /// </summary>
    [Serializable]
    public class MapBaseMarkerEntry
    {
        public float X { get; set; }
        public float Z { get; set; }

        public BaseFileEntry Base { get; set; } = new BaseFileEntry();

        /// <summary>What to call it on the board ("28mm", "Cavalry"). Cosmetic. ASCII only.</summary>
        public string Label { get; set; } = string.Empty;
    }

    /// <summary>A rectangular deployment zone on a custom map, in table inches.</summary>
    [Serializable]
    public class MapDeploymentZoneEntry
    {
        public float Left { get; set; }
        public float Right { get; set; }
        public float Bottom { get; set; }
        public float Top { get; set; }
    }

    [Serializable]
    public class TerrainPieceEntry
    {
        /// <summary>
        /// #268 — optional display name ("Boulder", "Shipping container"). Resolvers fall back to the
        /// type + dimensions label when it is empty, so layouts written before this field, and
        /// hand-authored ones that omit it, are unaffected. Purely cosmetic: nothing keys off it.
        /// ASCII only, like every other user-facing string (see CLAUDE.md).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public ETerrainType TerrainType { get; set; }

        //Polymorphic — IZone is serialized with TypeNameHandling.Auto so RectangularZone / CircularZone round-trip.
        public IZone Shape { get; set; } = null!;

        public float HeightInches { get; set; }

        /// <summary>
        /// #301 - what the piece costs in "Alternating: Points" terrain placement. 1-3 across the
        /// built-in palette today (roughly footprint x how strongly the type shapes play), but not
        /// hard-capped - a future monster piece may cost more. Defaults to 1 so layouts written
        /// before this field price every piece at the minimum; ignored by every other placement mode.
        /// </summary>
        public int Points { get; set; } = 1;
    }
}
