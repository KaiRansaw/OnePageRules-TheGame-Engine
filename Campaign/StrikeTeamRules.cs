using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>How one strike team unit did in a match. Kept as the Firefight-flavoured name for the
    /// shared <see cref="CampaignUnitResult"/>.</summary>
    public readonly record struct StrikeUnitResult(string Id, bool Casualty, int Kills, int HeroKills)
    {
        public CampaignUnitResult ToCampaignResult() => new(Id, Casualty, Kills, HeroKills);
    }

    /// <summary>
    /// GF: Firefight Campaign. Since the 3.6 move this is a thin wrapper over <see cref="CampaignRules"/>
    /// with the GFF price list (<see cref="CampaignNumbers.Firefight"/>) - the four Campaign Rules v3.6.0
    /// books share one loop and differ only in their numbers, so there is one implementation.
    /// <para>
    /// Everything here used to be hand-rolled against the v3.5.1 Firefight campaign pages, and every
    /// number in it was wrong against the standalone GFF book that arrived with 3.6: a 200pt army cap
    /// instead of a 400pt treasury, +20/+40 mission rewards instead of +80/+160, +5/+15 level costs
    /// instead of +15/+25, and -5/+5 injury and talent costs instead of -15/+15. The trait tables
    /// changed too - see <see cref="CampaignTables"/>.
    /// </para>
    /// </summary>
    public static class StrikeTeamRules
    {
        /// <summary>The GFF price list every call here uses.</summary>
        public static CampaignNumbers Numbers => CampaignNumbers.Firefight;

        public const int XpPerLevel = CampaignNumbers.XpPerLevel;
        public const int MaxXp = CampaignNumbers.MaxXp;

        public const string Specialist = CampaignTables.Specialist;
        public const string FastLearner = CampaignTables.FastLearner;
        public const string SkillSetPick = CampaignTables.SkillSetPick;

        public static string[] Injuries => CampaignTables.Injuries;
        public static string[] Talents => CampaignTables.Talents;
        public static IReadOnlyDictionary<string, string[]> SkillSets => CampaignTables.SkillSets;

        public static bool IsStrikeTeam(ArmyListFile army) => CampaignRules.IsCampaignArmy(army);

        public static StrikeUnit? Sheet(UnitFileEntry unit) => CampaignRules.Sheet(unit);

        public static void Save(UnitFileEntry unit, StrikeUnit sheet) => CampaignRules.Save(unit, sheet);

        public static bool IsHero(UnitFileEntry unit) => CampaignRules.IsHero(unit);

        /// <summary>Why this army can't start a strike team, or null.</summary>
        public static string? CannotStart(ArmyListFile army) => CampaignRules.CannotStart(army, Numbers);

        /// <summary>Turns an army into a strike team, with whatever is left of the 400pt treasury saved.</summary>
        public static void Start(ArmyListFile army, string teamName, string teamFile) =>
            CampaignRules.Start(army, teamName, teamFile, Numbers, EGameSystem.GFFirefight);

        /// <summary>Points the strike team is worth.</summary>
        public static int TeamValue(ArmyListFile army) => CampaignRules.ArmyValue(army);

        /// <summary>Casualties, XP, level ups, then VP and treasury.</summary>
        public static List<string> AfterGame(ArmyListFile team, IReadOnlyList<StrikeUnitResult> results,
                                             bool won, Func<int> d6) =>
            CampaignRules.AfterGame(team, results.Select(r => r.ToCampaignResult()).ToList(), won, Numbers, d6);

        public static List<string> AddXp(UnitFileEntry unit, StrikeUnit sheet, int amount, Func<int> d6) =>
            CampaignRules.AddXp(unit, sheet, amount, Numbers, d6);

        public static List<string> LevelUp(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6) =>
            CampaignRules.LevelUp(unit, sheet, Numbers, d6);

        public static List<string> SpecialistLeft(StrikeUnit sheet) => CampaignRules.SpecialistLeft(sheet);

        public static List<string> TraitsLeft(StrikeUnit sheet) => CampaignRules.TraitsLeft(sheet);

        public static List<string> Options(StrikeUnit sheet, string pending) => CampaignRules.Options(sheet, pending);

        public static string Choose(UnitFileEntry unit, string pending, string choice, Func<int> d6) =>
            CampaignRules.Choose(unit, pending, choice, d6);

        public static void AddRule(UnitFileEntry unit, string rule) => CampaignRules.AddRule(unit, rule);
    }
}
