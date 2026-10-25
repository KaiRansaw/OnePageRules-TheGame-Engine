using System.Text.Json;

namespace FDG.ArmyBuilding
{
    /// <summary>
    /// Horde Mode shop: a unit's army-builder choices plus the book they belong to, remembered on the unit
    /// (<see cref="FDG.UnitData.HordeLoadoutJson"/>) so its next upgrade starts from what it really has.
    /// </summary>
    public sealed class HordeLoadout
    {
        public string BookName { get; set; } = string.Empty;
        public BuilderUnit Unit { get; set; } = new();

        public string ToJson() => JsonSerializer.Serialize(this);

        public static HordeLoadout? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                return JsonSerializer.Deserialize<HordeLoadout>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}
