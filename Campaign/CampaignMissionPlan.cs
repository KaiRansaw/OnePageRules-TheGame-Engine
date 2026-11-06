using System.Text.Json;
using System.Text.Json.Serialization;
using FDG.Missions;

namespace FDG.Campaign
{
    /// <summary>
    /// #465 - one mission of a campaign, planned before it is played (Kai, 2026-11-05). The plan card on
    /// Mission Setup: a title, the map, the deployment style, the mission type and the difficulty, plus
    /// the player's story notes (kept in the campaign journal's page for that mission, so there is one
    /// place for a mission's words).
    ///
    /// <para>
    /// Kai's design: every mission is made when you get to it ("add as I go"); <b>Generate</b> rolls every
    /// field from the books and any one can be re-rolled or edited; <b>Write</b> is picking them by hand.
    /// The book decides the game, the notes are the story on top. A campaign's plans save as a file that
    /// can start a new campaign with fresh armies, its missions still editable and re-rollable.
    /// </para>
    /// </summary>
    public sealed class CampaignMissionPlan
    {
        /// <summary>The mission this plans, counting from 1.</summary>
        public int Mission { get; set; }

        public string Title { get; set; } = "";

        /// <summary>How the terrain is made. A custom map names its file in <see cref="MapName"/>.</summary>
        public ETerrainPlacementMode TerrainMode { get; set; } = ETerrainPlacementMode.AutoFromLayout;

        /// <summary>A saved map's name (no folder, no extension) - portable between computers.</summary>
        public string? MapName { get; set; }

        public EDeploymentStyle DeploymentStyle { get; set; } = EDeploymentStyle.Core;

        /// <summary>Off is the core rules' objectives; the rest are the Mission Cards games.</summary>
        public EMissionCardGame MissionCards { get; set; } = EMissionCardGame.Off;

        [JsonConverter(typeof(MissionDifficultyJsonConverter))]
        public EMissionDifficulty Difficulty { get; set; } = EMissionDifficulty.Normal;

        /// <summary>The story notes. Only used inside a saved campaign file; a live campaign keeps them in
        /// its journal (see <see cref="CampaignJournal.NoteFor"/>).</summary>
        public string Notes { get; set; } = "";

        public CampaignMissionPlan Clone() => (CampaignMissionPlan)MemberwiseClone();
    }

    /// <summary>
    /// #465 - rolls a mission plan's fields. Where a book gives the roll, it is the book's: the deployment
    /// style is the Advanced Rulebooks' own (pick a family, then D6 within it). Where none does - which
    /// mission type, which difficulty - the odds are ours and recorded in <c>claude/house-rules.md</c>.
    /// </summary>
    public static class CampaignMissionGenerator
    {
        /// <summary>A whole new plan for <paramref name="mission"/>.</summary>
        public static CampaignMissionPlan Generate(int mission, EGameSystem system, Func<int> d6)
        {
            var plan = new CampaignMissionPlan
            {
                Mission = mission,
                TerrainMode = ETerrainPlacementMode.AutoFromLayout,
                DeploymentStyle = RollDeployment(system, d6),
                MissionCards = RollMissionType(system, d6),
                Difficulty = RollDifficulty(d6),
            };
            plan.Title = DefaultTitle(plan);
            return plan;
        }

        /// <summary>
        /// The Advanced Rulebooks' deployment styles: a family on a D3 (Standard, Strategic, Asymmetric),
        /// then the books' D6 within it. Core rules for a game whose books have no Advanced deployment.
        /// </summary>
        public static EDeploymentStyle RollDeployment(EGameSystem system, Func<int> d6)
        {
            if (!AdvancedDeployment.Available(system)) return EDeploymentStyle.Core;
            EDeploymentStyle family = ((d6() + 1) / 2) switch
            {
                1 => EDeploymentStyle.RandomStandard,
                2 => EDeploymentStyle.RandomStrategic,
                _ => EDeploymentStyle.RandomAsymmetric,
            };
            return AdvancedDeployment.Resolve(family, d6);
        }

        /// <summary>
        /// House rule: 1-2 the core objectives, 3-4 a fixed-deck Mission Cards game (odd Battle of Wits,
        /// even Total Domination), 5-6 one of the six custom-deck games. Core objectives for a game with no
        /// Mission Cards.
        /// </summary>
        public static EMissionCardGame RollMissionType(EGameSystem system, Func<int> d6)
        {
            if (!MissionCardCatalog.Available(system)) return EMissionCardGame.Off;
            int roll = d6();
            if (roll <= 2) return EMissionCardGame.Off;
            if (roll <= 4) return d6() % 2 == 1 ? EMissionCardGame.BattleOfWits : EMissionCardGame.TotalDomination;
            return EMissionCardGame.DirectChallenge + (d6() - 1);
        }

        /// <summary>House rule: even thirds of Easy, Normal and Hard. Brutal is only ever chosen.</summary>
        public static EMissionDifficulty RollDifficulty(Func<int> d6) => d6() switch
        {
            <= 2 => EMissionDifficulty.Easy,
            <= 4 => EMissionDifficulty.Normal,
            _ => EMissionDifficulty.Hard,
        };

        public static string DefaultTitle(CampaignMissionPlan plan)
        {
            string deployment = plan.DeploymentStyle == EDeploymentStyle.Core ? "Battle Lines" : AdvancedDeployment.Name(plan.DeploymentStyle);
            return plan.MissionCards == EMissionCardGame.Off
                ? $"Mission {plan.Mission}: {deployment}"
                : $"Mission {plan.Mission}: {deployment} - {MissionCardCatalog.Name(plan.MissionCards)}";
        }

        /// <summary>What a mission type means, for the plan card. Game text: ASCII.</summary>
        public static string DescribeMissionType(EMissionCardGame game) => game switch
        {
            EMissionCardGame.Off => "The core rules: D3+2 objectives, held at the end of the game.",
            EMissionCardGame.BattleOfWits => "Mission Cards: your own 36-card deck, three cards face up.",
            EMissionCardGame.TotalDomination => "Mission Cards: one shared deck and three cards to race for.",
            _ => $"Mission Cards: build an 18-card deck - {MissionCardCatalog.Name(game)}.",
        };
    }

    /// <summary>
    /// #465 - a campaign saved as a plan others can play: its name, its game, and each mission's plan with
    /// its notes. Starting a campaign from one copies the plans in; nothing in it is locked (Kai: "edit
    /// and reroll").
    /// </summary>
    public sealed class CampaignScript
    {
        public const string Extension = ".fdgcampaign";

        public string Name { get; set; } = "";
        public EGameSystem GameSystem { get; set; } = EGameSystem.GrimdarkFuture;
        /// <summary>The campaign's opening note (the journal's first page).</summary>
        public string Premise { get; set; } = "";
        public List<CampaignMissionPlan> Missions { get; set; } = new();

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static CampaignScript? FromJson(string json)
        {
            try { return JsonSerializer.Deserialize<CampaignScript>(json, Options); }
            catch (JsonException) { return null; }
        }

        /// <summary>A campaign's plans as a script: every planned mission, its notes from the journal.</summary>
        public static CampaignScript From(StrikeTeamInfo info, string name)
        {
            var script = new CampaignScript
            {
                Name = name,
                GameSystem = info.GameSystem,
                Premise = info.Journal,
            };
            foreach (CampaignMissionPlan plan in info.Plans.OrderBy(p => p.Mission))
            {
                CampaignMissionPlan copy = plan.Clone();
                copy.Notes = CampaignJournal.NoteFor(info, plan.Mission);
                script.Missions.Add(copy);
            }
            return script;
        }

        /// <summary>Puts a script's missions (and their notes) into a newly started campaign.</summary>
        public void ApplyTo(StrikeTeamInfo info)
        {
            info.Plans = Missions.Select(m => m.Clone()).ToList();
            foreach (CampaignMissionPlan plan in info.Plans)
            {
                if (plan.Notes.Length > 0) CampaignJournal.SetNote(info, plan.Mission, plan.Notes);
                plan.Notes = "";
            }
            if (info.Journal.Length == 0 && Premise.Length > 0) CampaignJournal.SetCampaignNote(info, Premise);
        }
    }

    public static class CampaignPlans
    {
        /// <summary>The plan for the next mission, or null when it has not been made yet.</summary>
        public static CampaignMissionPlan? Next(StrikeTeamInfo info) =>
            info.Plans.FirstOrDefault(p => p.Mission == info.Matches + 1);

        /// <summary>Stores <paramref name="plan"/> as the plan for its mission, replacing any older one.</summary>
        public static void Set(StrikeTeamInfo info, CampaignMissionPlan plan)
        {
            info.Plans.RemoveAll(p => p.Mission == plan.Mission);
            info.Plans.Add(plan);
            info.Plans.Sort((a, b) => a.Mission.CompareTo(b.Mission));
        }
    }
}
