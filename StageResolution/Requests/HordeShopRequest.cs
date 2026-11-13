using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>A seized objective the shop can deploy recruits around (Horde Mode).</summary>
    public class HordeShopObjective
    {
        public int Number { get; }
        public float X { get; }
        public float Z { get; }
        public bool IsPriority { get; }

        [JsonConstructor]
        public HordeShopObjective(int number, float x, float z, bool isPriority)
        {
            Number = number;
            X = x;
            Z = z;
            IsPriority = isPriority;
        }
    }

    /// <summary>One of the shopper's units that may buy upgrades this wave (within 9" of a seized objective).</summary>
    public class HordeShopUnit
    {
        public Guid UnitId { get; }
        public string Name { get; }
        public int ModelCount { get; }
        public int PointCost { get; }

        /// <summary>Its remembered builder choices (<see cref="ArmyBuilding.HordeLoadout"/> JSON), or null
        /// for a unit from the starting list - the shop then starts it from the book's basic loadout.</summary>
        public string? LoadoutJson { get; }

        /// <summary>What it carries now, in words, so the player can compare.</summary>
        public string CurrentGear { get; }

        /// <summary>Why this unit can't buy upgrades right now (e.g. too far from a seized objective), or
        /// null when it can. Listed anyway so the shop can say why.</summary>
        [JsonProperty]
        public string? NotEligibleReason { get; set; }

        /// <summary>The hero joined to this unit, if any. Upgrades change the unit's own models only; the
        /// hero (and its points) stay as they are, and <see cref="ModelCount"/> / <see cref="PointCost"/>
        /// leave it out.</summary>
        [JsonProperty]
        public string? JoinedHeroName { get; set; }

        /// <summary>#409 (OPR 3.6.0) Reinforcing Units: what it costs to reinforce this unit - half its
        /// own point value, rounding up. Only meaningful when <see cref="NotReinforceableReason"/> is
        /// null.</summary>
        [JsonProperty]
        public int ReinforceCost { get; set; }

        /// <summary>#409: what reinforcing would give back, in words ("+5 models", "+3 wounds"), so the
        /// shop can show the player what they are buying before they buy it.</summary>
        [JsonProperty]
        public string? ReinforceRestores { get; set; }

        /// <summary>#409: why this unit can't be reinforced right now - most often that it is not yet down
        /// to half strength - or null when it can. Listed anyway so the shop can say why, the same way
        /// <see cref="NotEligibleReason"/> does for upgrades.</summary>
        [JsonProperty]
        public string? NotReinforceableReason { get; set; }

        [JsonConstructor]
        public HordeShopUnit(Guid unitId, string name, int modelCount, int pointCost, string? loadoutJson,
            string currentGear)
        {
            UnitId = unitId;
            Name = name ?? "";
            ModelCount = modelCount;
            PointCost = pointCost;
            LoadoutJson = loadoutJson;
            CurrentGear = currentGear ?? "";
        }
    }

    /// <summary>
    /// Horde Mode's shop, offered to a defending player at a point-reward round (the end of rounds 3, 6
    /// and 9): spend points to recruit new units, deployed fully within 9" of an objective the defenders
    /// seized; to buy upgrades for units in range; and - new in 3.6.0 (#409) - to REINFORCE units in range
    /// that are down to half strength, bringing their models or wounds back.
    /// </summary>
    public class HordeShopRequest : IStageTaskRequest<HordeShopReply>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string DisplayName { get; }

        /// <summary>The wave just finished.</summary>
        public int Wave { get; }

        /// <summary>Points the defending side can spend right now.</summary>
        public int PointsAvailable { get; }

        /// <summary>The shopper's army faction and name, so the shop can open that army's book.</summary>
        public string Faction { get; }
        public string ArmyName { get; }

        /// <summary>Objectives seized this wave - where recruits may deploy. Never empty.</summary>
        public List<HordeShopObjective> SeizedObjectives { get; }

        /// <summary>The lobby's Army Source setting: which game systems' books the shop offers. A settable
        /// property (not a constructor argument) so a request without it reads as All.</summary>
        [JsonProperty]
        public ArmyBuilding.EAllowedGameSystems AllowedGameSystems { get; set; } = ArmyBuilding.EAllowedGameSystems.All;

        /// <summary>The shopper's units that may buy upgrades (within 9" of a seized objective).</summary>
        [JsonProperty]
        public List<HordeShopUnit> Upgradable { get; set; } = new List<HordeShopUnit>();

        /// <summary>What this wave's reward paid (seized objectives x <see cref="PointsPerObjective"/>, plus
        /// <see cref="PriorityBonus"/> when the priority one was seized), so the shop can show where the points came
        /// from; the rest of <see cref="PointsAvailable"/> was kept from earlier shops. Display only.</summary>
        [JsonProperty]
        public int PointsEarned { get; set; }

        /// <summary>The game being played's book slug (e.g. "grimdark-future"), so the shop opens the army's book for
        /// THIS game and not the same-named faction of another one (Army Forge publishes each faction once per game).
        /// Null on requests from before it existed.</summary>
        [JsonProperty]
        public string? GameSystemSlug { get; set; }

        [JsonProperty]
        public int PointsPerObjective { get; set; }

        [JsonProperty]
        public int PriorityBonus { get; set; }

        /// <summary>How far from a seized objective recruits deploy and upgrades reach (9" GF / AoF, 6" GFF / AoFS).</summary>
        [JsonProperty]
        public float ShopRangeInches { get; set; } = 9f;

        /// <summary>How far from a seized objective a unit may be reinforced (9" in all four Horde books, #437).</summary>
        [JsonProperty]
        public float ReinforceRangeInches { get; set; } = 9f;

        [JsonConstructor]
        public HordeShopRequest(PlayerID targetPlayerID, TaskID taskID, int wave, int pointsAvailable,
            string faction, string armyName, List<HordeShopObjective> seizedObjectives)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            TaskName = "Horde Shop";
            DisplayName = "Horde Shop";
            Wave = wave;
            PointsAvailable = pointsAvailable;
            Faction = faction ?? "";
            ArmyName = armyName ?? "";
            SeizedObjectives = seizedObjectives ?? new List<HordeShopObjective>();
        }

        public HordeShopRequest(PlayerID targetPlayerID, int wave, int pointsAvailable, string faction,
            string armyName, List<HordeShopObjective> seizedObjectives)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), wave, pointsAvailable, faction, armyName,
                seizedObjectives)
        {
        }

        public Task<HordeShopReply> Resolve(HordeShopReply resolution) => Task.FromResult(resolution);
    }

    /// <summary>
    /// What the defender bought. <see cref="RecruitsJson"/> is an army list file (the same format as a
    /// .fdgarmy) holding just the recruited units plus the rules and spells they use, compiled by the army
    /// builder from the faction's book. <see cref="ObjectiveNumbers"/> lines up with its units: the seized
    /// objective each one deploys at. Empty = buy nothing.
    /// </summary>
    public class HordeShopReply
    {
        public string? RecruitsJson { get; }
        public List<int> ObjectiveNumbers { get; }

        /// <summary>Each recruit's builder choices (<see cref="ArmyBuilding.HordeLoadout"/> JSON), lined up
        /// with the recruits, so the unit remembers them for later upgrades.</summary>
        [JsonProperty]
        public List<string?> RecruitLoadouts { get; set; } = new List<string?>();

        /// <summary>Upgrades: an army list file holding each upgraded unit's NEW loadout, lined up with
        /// <see cref="UpgradeUnitIds"/> and <see cref="UpgradeLoadouts"/>. The price of each is its new
        /// cost minus what the unit costs now (a negative price gives points back).</summary>
        [JsonProperty]
        public string? UpgradesJson { get; set; }

        [JsonProperty]
        public List<Guid> UpgradeUnitIds { get; set; } = new List<Guid>();

        [JsonProperty]
        public List<string?> UpgradeLoadouts { get; set; } = new List<string?>();

        /// <summary>#409 (OPR 3.6.0): units to reinforce. Each costs half its own point value (rounding
        /// up) and gets back up to half its total models, or half its wounds for a single-model unit. No
        /// loadout or list travels with it - a reinforcement restores the unit the player already has.
        /// </summary>
        [JsonProperty]
        public List<Guid> ReinforceUnitIds { get; set; } = new List<Guid>();

        [JsonConstructor]
        public HordeShopReply(string? recruitsJson, List<int>? objectiveNumbers)
        {
            RecruitsJson = recruitsJson;
            ObjectiveNumbers = objectiveNumbers ?? new List<int>();
        }

        public static HordeShopReply Nothing => new HordeShopReply(null, null);
    }
}
