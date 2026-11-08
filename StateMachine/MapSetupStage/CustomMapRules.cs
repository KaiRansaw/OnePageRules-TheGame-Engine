using FDG.SaveLoad;

namespace FDG.Stages
{
    /// <summary>
    /// Map maker: reads the extra part of a custom map (fixed objectives) for the stages that use it.
    /// A map's old deployment zones are NOT read: deployment is picked in the lobby (Kai, 2026-11-07). A custom map is the "Custom Map" terrain mode (LoadFromFile) with a layout
    /// path; any other mode, a missing path or an unreadable file means "no custom map" and every
    /// stage behaves exactly as before.
    /// </summary>
    public static class CustomMapRules
    {
        private static readonly IReadOnlyList<MapObjectiveEntry> NoObjectives = new List<MapObjectiveEntry>();

        public static TerrainLayoutFile? ActiveMap(GameSettings settings)
        {
            if (settings.TerrainPlacementMode != ETerrainPlacementMode.LoadFromFile) return null;
            string? path = settings.TerrainLayoutPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            return TerrainLayoutLoader.TryLoadFromFile(path, out _);
        }

        public static IReadOnlyList<MapObjectiveEntry> FixedObjectives(GameSettings settings)
        {
            IReadOnlyList<MapObjectiveEntry> spots =
                (IReadOnlyList<MapObjectiveEntry>?)ActiveMap(settings)?.Objectives ?? NoObjectives;
            // Mission Cards: "Place 6 objective markers" - a map with some other number is placed as usual.
            if (FDG.Missions.MissionCardGame.IsOn(settings) && spots.Count != FDG.Missions.MissionCardGame.ObjectiveCount)
                return NoObjectives;
            return spots;
        }
    }
}
