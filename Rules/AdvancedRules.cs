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
        // Battlefield Effects (all four books). Bits 20-23 hold the picked Special Condition (see BattlefieldEffects).
        SpecialConditions = 1UL << 14,
        TerrainObjectiveEffects = 1UL << 15,
        RandomEvents = 1UL << 16,
        // Fog of War (all four books). Bits 20-23 are the Special Condition pick - skipped.
        EscalatingBattles = 1UL << 24,
        SurpriseEngagement = 1UL << 25,
        EbbAndFlow = 1UL << 26,
        ShiftingFocus = 1UL << 27,
        CombatHesitation = 1UL << 28,
        ProlongedBattle = 1UL << 29,
        EscalatingRandomArrival = 1UL << 30,
        // Command Points (all four books). Bits 32-34 hold the picked playstyle (see CommandPoints).
        CommandPoints = 1UL << 31,
        // Political Intrigue (GF p49, AoF p49 only). Bits 32-34 are Command Points' playstyle pick - skipped.
        PoliticalDirectives = 1UL << 35,
        Politicians = 1UL << 36,
        // Advanced Terrain - Army Terrain (all four books).
        ArmyTerrain = 1UL << 37,
        // Advanced Casting - Currents / Winds of Power (all four books).
        SpellBooks = 1UL << 38,
        TwoD6Casting = 1UL << 39,
        MagicPools = 1UL << 40,
        HeavyBacklash = 1UL << 41,
        LivingSpells = 1UL << 42,
        // Advanced Casting - Relic Wargear / Magic Items (all four books). Bits 44-45 hold the selection method.
        RelicWargear = 1UL << 43,
        // Tactical Phase (GF: Firefight / AoF: Skirmish p46).
        ForwardPositions = 1UL << 46,
        BattlePreparations = 1UL << 47,
        // City Fighting (GF: Firefight / AoF: Skirmish p47).
        UrbanTerrain = 1UL << 48,
        SewerSystems = 1UL << 49,
        DefensivePositions = 1UL << 50,
        CorneredUnits = 1UL << 51,
        DestructibleCover = 1UL << 52,
        ThrownObjects = 1UL << 53,
        BuildingsOnFire = 1UL << 54,
        HiddenMovement = 1UL << 55,
        // Fog of War, continued: Escalating Battles' second option.
        EscalatingFlexibleStrategies = 1UL << 56,
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
        public const string TacticalPhaseChapter = "Tactical Phase";
        public const string BattlefieldEffectsChapter = "Battlefield Effects";
        public const string FogOfWarChapter = "Fog of War";
        public const string CommandPointsChapter = "Command Points";
        public const string PoliticalIntrigueChapter = "Political Intrigue";
        public const string ArmyTerrainChapter = "Army Terrain";
        public const string AdvancedCastingChapter = "Advanced Casting";
        public const string CityFightingChapter = CityFighting.Chapter;

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
            foreach ((string name, EAdvancedRules flag) in BattlefieldEffects.All)
                list.Add(new AdvancedRuleOption(BattlefieldEffectsChapter, name, flag, FourGames,
                    BattlefieldEffects.BookText(name), BattlefieldEffects.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in CommandPoints.All)
                list.Add(new AdvancedRuleOption(CommandPointsChapter, name, flag, FourGames,
                    CommandPoints.BookText(EGameSystem.GrimdarkFuture), CommandPoints.GameText));
            foreach ((string name, EAdvancedRules flag) in FogOfWar.All)
                list.Add(new AdvancedRuleOption(FogOfWarChapter, name, flag, FourGames,
                    FogOfWar.BookText(name, EGameSystem.GrimdarkFuture), FogOfWar.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in ArmyTerrain.All)
                list.Add(new AdvancedRuleOption(ArmyTerrainChapter, name, flag, FourGames,
                    ArmyTerrain.BookText(name), ArmyTerrain.GameText(name)));
            // Advanced Casting: one entry per book, since the four print different wound counts.
            foreach (EGameSystem book in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                foreach ((string name, EAdvancedRules flag) in AdvancedCasting.All)
                    list.Add(new AdvancedRuleOption(AdvancedCastingChapter, name, flag, s => s == book,
                        AdvancedCasting.BookText(name, book), AdvancedCasting.GameText(name)));
            foreach (EGameSystem book in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                list.Add(new AdvancedRuleOption(AdvancedCastingChapter, LivingSpells.Option, EAdvancedRules.LivingSpells, s => s == book,
                    LivingSpells.BookText(book), LivingSpells.GameText));
            foreach (EGameSystem book in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                list.Add(new AdvancedRuleOption(AdvancedCastingChapter, RelicWargear.OptionName(book), EAdvancedRules.RelicWargear, s => s == book,
                    RelicWargear.BookText(book), RelicWargear.GameText));
            foreach ((string name, EAdvancedRules flag) in PoliticalIntrigue.All)
                list.Add(new AdvancedRuleOption(PoliticalIntrigueChapter, name, flag, PoliticalIntrigue.BooksHaveIt,
                    PoliticalIntrigue.BookText(name), PoliticalIntrigue.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in TacticalPhase.All)
                list.Add(new AdvancedRuleOption(TacticalPhaseChapter, name, flag, TacticalPhase.BooksHaveIt,
                    TacticalPhase.BookText(name), TacticalPhase.GameText(name)));
            foreach ((string name, EAdvancedRules flag) in CityFighting.All)
                list.Add(new AdvancedRuleOption(CityFightingChapter, name, flag, CityFighting.BooksHaveIt,
                    CityFighting.BookText(name), CityFighting.GameText(name)));
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
            [BattlefieldEffectsChapter] = "Battlefield Effects add special conditions to the whole table, effects to terrain and objectives, and random events every round. Pick any of the following.",
            [CommandPointsChapter] = "Command Points (CP) are an extra resource that can be added to the game, giving players access to critical tactical abilities that boost their units in battle.",
            [FogOfWarChapter] = "Fog of War rules add an element of uncertainty to battles, by affecting how units are deployed and how rounds are played. Pick any of the following.",
            [ArmyTerrainChapter] = "Army terrain allows each army to place a special piece of terrain that affects the game in different interesting ways.",
            [AdvancedCastingChapter] = AdvancedCasting.Intro + " Pick any of the following.",
            [PoliticalIntrigueChapter] = "Political Intrigue rules add a new strategic layer on top of the regular game, giving players the ability to influence the game and mess with each other's plans.",
            [TacticalPhaseChapter] = "The Tactical Phase is an optional replacement to the normal deployment phase, which adds some interesting strategies to the pre-game sequence. Pick any of the following. Note that if you are using all of them, then the steps must be followed in that order before the game begins.",
            [CityFightingChapter] = CityFighting.Intro,
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
