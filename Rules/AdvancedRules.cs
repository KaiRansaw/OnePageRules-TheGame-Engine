using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// The optional systems of the Advanced Rulebooks 3.6.0 that are switched on one tick box each (Kai, 2026-11-23:
    /// "do everything"). One flag per option, grouped by chapter; <see cref="AdvancedRules.Catalog"/> says which games
    /// print each one. Stored in <see cref="GameSettings.AdvancedRules"/>.
    /// </summary>
    [Flags]
    public enum EAdvancedRules : ulong
    {
        None = 0,
        // Unit Psychology (GF p47, GFF p44, AoF p47, AoFS p44)
        Intimidation = 1UL << 0,
        Frenzy = 1UL << 1,
        Panic = 1UL << 2,
        Retreating = 1UL << 3,
        Suppression = 1UL << 4,
        // Brutal Damage (GF p48, AoF p48 only)
        DeathZone = 1UL << 5,
        HeavyDamage = 1UL << 6,
        DirectionalDamage = 1UL << 7,
        UnitAttrition = 1UL << 8,
        FriendlyFire = 1UL << 9,
        HorribleMishaps = 1UL << 10,
        BrutalCriticalHits = 1UL << 11,
        // Critical Hits (GFF p45, AoFS p45 only)
        SkirmishCriticalHits = 1UL << 12,
        CriticalHitsByWeaponType = 1UL << 13,
    }

    /// <summary>One optional rule as the lobby shows it.</summary>
    public sealed record AdvancedRuleOption(string Chapter, string Name, EAdvancedRules Flag,
        Func<EGameSystem, bool> Applies, string BookText, string GameText);

    public static class AdvancedRules
    {
        private static bool FourGames(EGameSystem s) => !GameSystemInfo.IsQuest(s);

        public const string UnitPsychologyChapter = "Unit Psychology";
        public const string BrutalDamageChapter = "Brutal Damage";
        public const string CriticalHitsChapter = "Critical Hits";

        public static readonly IReadOnlyList<AdvancedRuleOption> Catalog = BuildCatalog();

        private static List<AdvancedRuleOption> BuildCatalog()
        {
            var list = new List<AdvancedRuleOption>();
            foreach ((string name, EAdvancedRules flag) in UnitPsychology.All)
                list.Add(new AdvancedRuleOption(UnitPsychologyChapter, name, flag, FourGames,
                    UnitPsychology.BookText(name), UnitPsychology.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in BrutalDamage.All)
                list.Add(new AdvancedRuleOption(BrutalDamageChapter, name, flag, BrutalDamage.BooksHaveIt,
                    BrutalDamage.BookText(name), BrutalDamage.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in CriticalHits.All)
                list.Add(new AdvancedRuleOption(CriticalHitsChapter, name, flag, CriticalHits.BooksHaveIt,
                    CriticalHits.BookText(name), CriticalHits.GameText(name)));
            return list;
        }

        /// <summary>The chapters in catalog order, with a line on what each is for (the lobby's chapter tick box).</summary>
        public static readonly IReadOnlyDictionary<string, string> ChapterIntro = new Dictionary<string, string>
        {
            [UnitPsychologyChapter] = "Unit psychology rules change how units behave as they are put through continuous fighting, making them less predictable. Pick any of the following.",
            [BrutalDamageChapter] = "Brutal Damage rules make the game deadlier by modifying how units are damaged during combat. Pick any of the following.",
            [CriticalHitsChapter] = "Critical Hits rules make weapons a lot deadlier by modifying how units are damaged with a little bit of luck. Use the generic tables, or the weapon type tables instead.",
        };

        public static IEnumerable<string> Chapters(EGameSystem system) =>
            Catalog.Where(o => o.Applies(system)).Select(o => o.Chapter).Distinct();

        public static IEnumerable<AdvancedRuleOption> In(string chapter, EGameSystem system) =>
            Catalog.Where(o => o.Chapter == chapter && o.Applies(system));

        public static EAdvancedRules ChapterFlags(string chapter) =>
            Catalog.Where(o => o.Chapter == chapter).Aggregate(EAdvancedRules.None, (a, o) => a | o.Flag);

        /// <summary>The set in play: only options this game's book prints, and none in the Quest games.</summary>
        public static EAdvancedRules Enabled(GameSettings settings)
        {
            if (GameSystemInfo.IsQuest(settings.GameSystem) || GameSystemInfo.IsQuestMode(settings.GameMode)) return EAdvancedRules.None;
            EAdvancedRules allowed = Catalog.Where(o => o.Applies(settings.GameSystem)).Aggregate(EAdvancedRules.None, (a, o) => a | o.Flag);
            return settings.AdvancedRules & allowed;
        }

        public static bool IsOn(IGameContext c, EAdvancedRules flag) => (Enabled(c.Settings) & flag) != 0;

        public static bool IsOn(GameSettings settings, EAdvancedRules flag) => (Enabled(settings) & flag) != 0;
    }
}
