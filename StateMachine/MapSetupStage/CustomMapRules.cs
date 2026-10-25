using FDG.SaveLoad;

namespace FDG.Stages
{
    /// <summary>
    /// Map maker: reads the extra parts of a custom map (fixed objectives, deployment zones) for the
    /// stages that use them. A custom map is the "Custom Map" terrain mode (LoadFromFile) with a layout
    /// path; any other mode, a missing path or an unreadable file means "no custom map" and every
    /// stage behaves exactly as before.
    /// </summary>
    public static class CustomMapRules
    {
        private static readonly IReadOnlyList<MapObjectiveEntry> NoObjectives = new List<MapObjectiveEntry>();
        private static readonly IReadOnlyList<MapDeploymentZoneEntry> NoZones = new List<MapDeploymentZoneEntry>();

        public static TerrainLayoutFile? ActiveMap(GameSettings settings)
        {
            if (settings.TerrainPlacementMode != ETerrainPlacementMode.LoadFromFile) return null;
            string? path = settings.TerrainLayoutPath;
            if (string.IsNullOrWhiteSpace(path)) return null;
            return TerrainLayoutLoader.TryLoadFromFile(path, out _);
        }

        public static IReadOnlyList<MapObjectiveEntry> FixedObjectives(GameSettings settings)
            => (IReadOnlyList<MapObjectiveEntry>?)ActiveMap(settings)?.Objectives ?? NoObjectives;

        public static IReadOnlyList<MapDeploymentZoneEntry> DeploymentZones(GameSettings settings)
            => (IReadOnlyList<MapDeploymentZoneEntry>?)ActiveMap(settings)?.DeploymentZones ?? NoZones;
    }
}
