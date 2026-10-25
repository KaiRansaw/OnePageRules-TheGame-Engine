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

        public List<TerrainPieceEntry> Pieces { get; set; } = new List<TerrainPieceEntry>();

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
