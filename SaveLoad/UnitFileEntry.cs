
namespace FDG.SaveLoad
{
    [Serializable]
    public class UnitFileEntry
    {
        // #191: Interlocked, not `_nextID++`. Army files are deserialized concurrently by the lab
        // (games run in parallel) and by the search; a non-atomic increment hands two entries the
        // same StableID, which is the key other data points at.
        public int StableID { get; } = System.Threading.Interlocked.Increment(ref _nextID) - 1;

        private static int _nextID = 1;

        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Optional author-stable handle for this unit within its army file, used as the target of a
        /// Hero's <see cref="JoinsUnitId"/> (#006). Distinct from <see cref="StableID"/>, which is a
        /// per-process counter and is never serialized, so it can't be a cross-file reference.
        /// </summary>
        public string? Id { get; set; }

        /// <summary>
        /// When this entry is a Hero (carries the "Hero" special rule), names the <see cref="Id"/> of the
        /// friendly multi-model unit it joins at army setup (#006). Null = the hero deploys on its own.
        /// </summary>
        public string? JoinsUnitId { get; set; }

        public int ModelCount { get; set; }

        /// <summary>Cosmetic: the 3D model file name picked for each model, in model order (see
        /// <see cref="IModel.Look"/>). Null, short, or a null/empty entry = that model uses the unit's first version.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public List<string?>? ModelLooks { get; set; }

        /// <summary>
        /// The base footprint shared by all this unit's models (#149). Defaults to a 28mm circle, so a
        /// pre-#149 army file (no <c>base</c> property) is unchanged.
        /// </summary>
        public BaseFileEntry Base { get; set; } = new BaseFileEntry();

        public int Quality { get; set; }

        public int Defense { get; set; }

        public List<SpecialRuleEntry> SpecialRules { get; set; } = new List<SpecialRuleEntry>();

        public List<WeaponFileEntry> Weapons { get; set; } = new List<WeaponFileEntry>();

        public int PointCost { get; set; }

        /// <summary>Star Quest: the hero sheet (a <see cref="FDG.Quest.QuestHero"/> as JSON) this unit was built
        /// from, or null for an ordinary unit. Carried onto the unit in play.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? QuestHeroJson { get; set; }

        /// <summary>GF: Firefight Campaign: this unit's strike team sheet (a <see cref="FDG.Campaign.StrikeUnit"/> as
        /// JSON), or null outside a strike team. Carried onto the unit in play.</summary>
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        [Newtonsoft.Json.JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? StrikeUnitJson { get; set; }
    }
}
