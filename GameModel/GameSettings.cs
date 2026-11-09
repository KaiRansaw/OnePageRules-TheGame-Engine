using System.Collections.Generic;
using System;
using Newtonsoft.Json;

namespace FDG
{
    public struct GameSettings
    {
        public int ArmyPoints;

        /// <summary>
        /// Which ruleset drives the match: the standard 4-round game, or OPR Horde Mode (waves of
        /// enemies against a defending side). <see cref="EGameMode.Standard"/> is 0, so a pre-Horde
        /// save (field absent from the JSON) resumes as the standard game it was played as.
        /// </summary>
        public EGameMode GameMode;

        /// <summary>
        /// Which OPR game this match is (Grimdark Future, Age of Fantasy, GF: Firefight, ...). The Game Mode
        /// (<see cref="GameMode"/>: Standard, Horde, ...) is the kind of game played within it.
        /// <see cref="EGameSystem.GrimdarkFuture"/> is 0, so a save or config from before this field resumes
        /// as the Grimdark Future game it was played as.
        /// </summary>
        public EGameSystem GameSystem;

        /// <summary>
        /// Standard play, or Game Master: the host gets a panel to change the game out of order (end or
        /// jump rounds, move any unit, edit wounds / Shaken / XP / traits, Horde points and objectives)
        /// for testing. Standard is 0, so a pre-existing save resumes as Standard.
        /// </summary>
        public EPlayStyle PlayStyle;

        /// <summary>
        /// #402 - which game systems' armies this lobby accepts. Host-owned, synced, and changeable
        /// in the lobby like <see cref="ArmyPoints"/>; an army from a system this doesn't allow is
        /// flagged on its roster row and BLOCKS the launch (see
        /// <see cref="ArmyBuilding.LaunchGate.BlockingProblems"/>).
        /// <see cref="ArmyBuilding.EAllowedGameSystems.All"/> is 0, so a pre-#402 save (field absent
        /// from the JSON) resumes accepting everything, which is what it was played under.
        /// </summary>
        public ArmyBuilding.EAllowedGameSystems AllowedGameSystems;

        /// <summary>
        /// #201 house-rule cover proximity exceptions (lobby toggle, default ON): a cover piece is
        /// voided when the shooter's muzzle hugs it (sight-line exit within 2" of the shooter's base
        /// and not also hugged by the target), or when shooter and target share the same piece at
        /// under 6" - see <see cref="FDG.Stages.CoverProximityRules"/>. Nullable so pre-#201 saves
        /// (field absent from the JSON) resolve to the default ON rather than silently OFF; read via
        /// <see cref="CoverProximityExceptionsEnabled"/>, never directly.
        /// </summary>
        public bool? CoverProximityExceptions;

        [JsonIgnore]
        public bool CoverProximityExceptionsEnabled => CoverProximityExceptions ?? true;

        /// <summary>
        /// #384 house rule (lobby toggle, default OFF): when true, NO same-team model ever blocks
        /// shooting line of sight - the pre-#384 behavior (#044). When false (official rules), only
        /// the shooting unit's own models and the target unit's models are transparent; every other
        /// unit's models, friendly or enemy, block. Plain bool, so a pre-#384 save (field absent
        /// from the JSON) resumes under the official rules - deliberate: "default = official" was
        /// the explicit ruling, even though the save was played see-through (see WorkItems/384).
        /// </summary>
        public bool SeeThroughFriendlyUnits;

        /// <summary>
        /// #384 house rule (lobby toggle, default OFF): when true, a shooting unit may split its
        /// fire across any number of distinct enemy units in one shoot action. When false, the
        /// <see cref="Utilities.GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION"/> cap (2)
        /// applies - today's behavior, so a pre-#384 save deserializes to what it was played with.
        /// </summary>
        public bool UnlimitedSplitFire;

        // #456 - Fast-Play Rules (GF and AoF Advanced Rulebooks 3.6.0, p17): "Fast-Play rules allow you
        // to speed up regular games, as well as play epic battles of massive size quickly. We highly
        // recommend using these rules when playing games with 4000pts armies or higher" (GF; AoF says
        // 3000pts). "Pick any of the following" - so each is its own toggle, all OFF by default, and a
        // save from before #456 deserializes with them off, i.e. exactly as it was played.
        // Only GF and AoF have them; the Firefight and Skirmish Advanced books do not print Fast-Play, so
        // FastPlay.Applies gates on the game system as well as these flags. Kai chose Fast-Play over a
        // bigger table for big games (2026-11-03); Simplified Terrain, the fourth rule, is deferred by his
        // call (2026-11-05) as its own job.

        /// <summary>#456 Fast-Play: Leader-Based Movement. See <see cref="FastPlay"/>.</summary>
        public bool FastPlayMovement;

        /// <summary>#456 Fast-Play: Leader-Based Melee. See <see cref="FastPlay"/>.</summary>
        public bool FastPlayMelee;

        /// <summary>#456 Fast-Play: Leader-Based Shooting. See <see cref="FastPlay"/>.</summary>
        public bool FastPlayShooting;

        /// <summary>
        /// The Advanced Rulebooks' deployment style (Kai, 2026-11-05). <see cref="EDeploymentStyle.Core"/> - the
        /// core rules' 12" from your edge - by default. See <see cref="AdvancedDeployment"/>; only games whose
        /// pages are in use it, and a custom map's own zones win over it.
        /// </summary>
        public EDeploymentStyle DeploymentStyle;

        /// <summary>
        /// The Mission Cards (Mission Cards v3.6.0, Kai 2026-11-05) - Battle of Wits or Total Domination, or
        /// <see cref="FDG.Missions.EMissionCardGame.Off"/> (the default, and every older save) for the core
        /// rules' objectives. Only a standard game in GF, GF: Firefight, AoF or AoF: Skirmish uses it.
        /// </summary>
        public FDG.Missions.EMissionCardGame MissionCardGame;

        /// <summary>
        /// House rule (lobby toggle, default ON): when true, separate friendly units may end their
        /// move within 1" of each other, and may pass through each other's bases. Models still may
        /// not END overlapping another model, the enemy standoff is untouched, and within-unit
        /// coherency is untouched.
        /// <para>
        /// Turning it OFF restores the rules-as-written: "Units may only be within 1" of other units
        /// when charging" (GF/AoF core rules, Movement), which applies to friendly units too.
        /// </para>
        /// <para>
        /// Default ON (Kai's call, 2026-10-29). The rule bites hardest in GF: Star Quest and AoF:
        /// Quest, where every hero is its own unit, so a party can never close up - the opposite
        /// pressure to the 1" within-unit coherency rule that holds a squad together. It also starves
        /// <c>HordePlacement.PlaceUnitNear</c>: each hero placed carries a 1" exclusion ring, so
        /// deploying a party inside a 6" circle at the table centre can leave no legal spot at all.
        /// NOTE: this default makes that far less likely but is NOT the fix for it - the placement
        /// search must be bounded so it reports failure instead of hunting forever. See
        /// <see cref="Utilities.GameWideConstants.FRIENDLY_UNITS_MAY_OVERLAP"/>.
        /// </para>
        /// </summary>
        public bool FriendlyUnitsMayOverlap;

        /// <summary>
        /// A table size picked in the lobby, in inches. Null = automatic: the book table, growing with the
        /// army points above a full match (Kai, 2026-11-08). <see cref="TableSizing.Resolve"/> decides.
        /// </summary>
        public float? TableWidthInches;
        public float? TableHeightInches;

        /// <summary>
        /// The table the chosen map (Load From File) was drawn for, copied off the map when the lobby picks
        /// it so every player resolves the same table without reading the file. A map always plays on its
        /// own table - see <see cref="TableSizing.Resolve"/>. Null when no map is chosen.
        /// </summary>
        public float? MapTableWidthInches;
        public float? MapTableHeightInches;

        /// <summary>
        /// How many rounds a standard game lasts, when not the books' 4. Null = 4. Horde and the Quest games
        /// keep their own lengths. A house rule when set (see claude/house-rules.md).
        /// </summary>
        public int? NumberOfRounds;

        /// <summary>
        /// Fast-Play: Simplified Terrain (GF / AoF Advanced Rulebook 3.6.0, p17): "all pieces of terrain must be
        /// treated like solid buildings, with clearly defined areas that can be entered and exited ... the
        /// only way for units to get Cover is by entering a piece of terrain." Off by default.
        /// </summary>
        public bool FastPlaySimplifiedTerrain;

        /// <summary>
        /// Multiplayer: King of the Hill (Advanced Rulebooks 3.6.0, Multiplayer Games). A free-for-all where
        /// attacking the king - the player whose army is worth the most - earns combat bonus points, each
        /// spendable once per activation for +1 to all dice in one roll. Only plays with three or more sides.
        /// </summary>
        public bool KingOfTheHill;

        /// <summary>
        /// Extra Actions (Advanced Rulebooks 3.6.0, "Advanced Actions"): "Before the game, players can agree to
        /// use any or all of the following extra actions". Each flag adds that action to every unit's menu
        /// alongside the regular ones. None by default. See <see cref="FDG.ExtraActions"/>.
        /// </summary>
        public EExtraActions ExtraActions;

        /// <summary>
        /// The Solo &amp; Co-Op Rules v3.6.0 "Challenge Bonuses (optional)" - the books' own difficulty for
        /// playing against the official AI: "Players may give the AI any of the following bonuses". Word for
        /// word the same in all four books (GF, GFF, AoF, AoFS); the Quest games have no Solo &amp; Co-Op book
        /// and so none. Each bonus is its own flag because the book lets players pick "any". None by default
        /// and in every older save. See <see cref="FDG.ChallengeBonuses"/>.
        /// </summary>
        public EChallengeBonuses ChallengeBonuses;

        /// <summary>
        /// Each seat's lobby colour pick (slot id -> colour index), recorded at launch so a resumed game
        /// gives everyone back the colour they chose. Seats left on automatic are absent. Null in older saves.
        /// </summary>
        public Dictionary<int, int>? SlotColors;

        public int TerrainPieceCount;

        /// <summary>
        /// #301 Alternating: Points - the shared budget of terrain points the players place between
        /// them. Dealt out at phase start in placing order, <see cref="TerrainPointsPerTurn"/> at a
        /// time, until it runs out (so the last chunk may be partial and early players may get one
        /// more chunk than late ones). Read only in
        /// <see cref="ETerrainPlacementMode.AlternatingPoints"/> mode.
        /// </summary>
        public int TerrainPointsTotal;

        /// <summary>
        /// #301 Alternating: Points - how many terrain points a player spends on each of their
        /// placement turns. A piece costing more can still open a turn; the difference is taken from
        /// the player's next turn(s) as debt (see <see cref="Stages.TerrainPointsLedger"/>).
        /// </summary>
        public int TerrainPointsPerTurn;

        public ERandomnessType RandomnessType;

        /// <summary>
        /// Optional seed for <see cref="ERandomnessType.Realistic"/> dice (#167): set, the host's
        /// roller produces the same sequence every run, making manual repro cases shareable
        /// ("seed 42, scenario X, the bug appears on the second volley"). Null (the default and the
        /// value in every pre-#167 save) keeps today's unseeded behavior. Rolls happen host-side
        /// only, so the seed never needs to reach clients.
        /// </summary>
        public int? DiceSeed;

        public ETurnStyle TurnStyle;

        /// <summary>
        /// #371 - whether a unit's shooting targets are all declared before any dice are rolled, or
        /// chosen one weapon at a time with the previous weapon's casualties already on the table. See
        /// <see cref="EShootingMode"/>. Default <see cref="EShootingMode.OneAtATime"/>, which is what
        /// the game did before the setting existed - so a pre-#371 save (field absent from the JSON)
        /// deserializes to the behaviour it was played with.
        /// </summary>
        public EShootingMode ShootingMode;

        /// <summary>
        /// How objective markers are placed during map setup. See
        /// <see cref="EObjectivePlacementMode"/>. Default <see cref="EObjectivePlacementMode.AutoPlaced"/>
        /// (the engine places all markers with no player interaction).
        /// </summary>
        public EObjectivePlacementMode ObjectivePlacementMode;

        public ETerrainPlacementMode TerrainPlacementMode;

        /// <summary>
        /// Path to a <see cref="FDG.SaveLoad.TerrainLayoutFile"/> JSON, used only
        /// when <see cref="TerrainPlacementMode"/> is
        /// <see cref="ETerrainPlacementMode.LoadFromFile"/>. Resolved on the host;
        /// the file is not transmitted to clients (the engine runs on the host
        /// and broadcasts the placed terrain through table-state events).
        /// </summary>
        public string? TerrainLayoutPath;

        /// <summary>
        /// Cosmetic table surface the front end paints under the terrain (#265). Purely visual - no
        /// rule reads it - but it is a lobby setting rather than a local preference so every player at
        /// the table sees the same board, and it rides the save so a resumed game looks like the one
        /// that was put down. Default (and the value a pre-#265 save deserializes to) is
        /// <see cref="ETableBackground.Forest"/>, which is the board every existing game was played on.
        /// </summary>
        public ETableBackground TableBackground;

        /// <summary>
        /// The saved settings a resumed game launches with, with the handful of fields a resume lobby
        /// is allowed to re-pick taken from <paramref name="lobbySettings"/> (#265).
        ///
        /// <para>Today that is <see cref="TableBackground"/> and nothing else. Everything else is
        /// either already spent (army points, terrain and objective placement all happened during the
        /// saved game's setup) or would change the rules of a game in progress (randomness, dice seed,
        /// turn style, shooting mode, the cover house rules, #402's allowed game systems) - so the
        /// save stays authoritative for them,
        /// whatever the lobby panel happens to be showing. Adding a field here is a deliberate decision, not a
        /// default: it must be safe to change mid-game.</para>
        /// </summary>
        public GameSettings WithResumeOverridesFrom(GameSettings lobbySettings)
        {
            GameSettings merged = this;
            merged.TableBackground = lobbySettings.TableBackground;
            // Game Master only adds testing tools - it never changes a rule - so a save can be resumed
            // with them switched on (or off) to poke at a game in progress.
            merged.PlayStyle = lobbySettings.PlayStyle;
            return merged;
        }

        public static GameSettings GetDefault()
        {
            return new GameSettings()
            {
                ArmyPoints = 2000,
                GameMode = EGameMode.Standard,
                GameSystem = EGameSystem.GrimdarkFuture,
                PlayStyle = EPlayStyle.Standard,
                AllowedGameSystems = ArmyBuilding.EAllowedGameSystems.All,
                CoverProximityExceptions = true,
                SeeThroughFriendlyUnits = false,
                UnlimitedSplitFire = false,
                FriendlyUnitsMayOverlap = true,
                TerrainPieceCount = 20,
                TerrainPointsTotal = 30,
                TerrainPointsPerTurn = 3,
                RandomnessType = ERandomnessType.Realistic,
                DiceSeed = null,
                TurnStyle = ETurnStyle.Standard,
                ShootingMode = EShootingMode.OneAtATime,
                ObjectivePlacementMode = EObjectivePlacementMode.AutoPlaced,
                TerrainPlacementMode = ETerrainPlacementMode.AutoFromLayout,
                TerrainLayoutPath = null,
                TableBackground = ETableBackground.Forest,
            };
        }
    }

    /// <summary>
    /// The table's cosmetic surface (#265). Front-end only: the renderer maps each value to a felt
    /// colour, grid tint, edge trim, and mottling pattern. Forest is first so it is both the default
    /// and what <c>default(GameSettings)</c> / a pre-#265 save resolves to.
    /// </summary>
    public enum ETableBackground
    {
        Forest,
        Desert,
        Ice,
        MarsLike,
        Urban,
        Barren,
    }

    /// <summary>
    /// The OPR game a match belongs to - the "Game Type" in the lobby. Grimdark Future first so it is both
    /// the default and what <c>default(GameSettings)</c> / an older save resolves to. Append new values at
    /// the end - the numeric value is what saves and the wire carry. See <see cref="GameSystemInfo"/>.
    /// </summary>
    public enum EGameSystem
    {
        GrimdarkFuture,
        AgeOfFantasy,
        GFFirefight,
        AoFSkirmish,
        GFStarQuest,
        AoFQuest,
    }

    /// <summary>Names, army sources and availability for each <see cref="EGameSystem"/>.</summary>
    public static class GameSystemInfo
    {
        /// <summary>The order the lobby lists them in.</summary>
        public static readonly EGameSystem[] DisplayOrder =
        {
            EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.GFStarQuest,
            EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish, EGameSystem.AoFQuest,
        };

        public static string DisplayName(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => "Grimdark Future",
            EGameSystem.AgeOfFantasy => "Age of Fantasy",
            EGameSystem.GFFirefight => "GF: Firefight",
            EGameSystem.AoFSkirmish => "AoF: Skirmish",
            EGameSystem.GFStarQuest => "GF: Star Quest",
            EGameSystem.AoFQuest => "AoF: Quest",
            _ => system.ToString(),
        };

        /// <summary>
        /// The Army Forge slug for a game system - the same key <see cref="ArmyBuilding.GameSystems"/>
        /// stamps on a book or army, and what any per-system data keys off.
        ///
        /// <para>
        /// #420 added this because the rule-text lookup needed it and three screens were about to grow
        /// their own copy of the switch. The two sides of the same fact were already drifting: this enum
        /// is what a GAME is played as, the slug is what a BOOK was built from, and nothing converted
        /// between them, so every site that needed both wrote the mapping again.
        /// </para>
        /// </summary>
        public static string Slug(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => ArmyBuilding.GameSystems.GrimdarkFuture,
            EGameSystem.AgeOfFantasy => ArmyBuilding.GameSystems.AgeOfFantasy,
            EGameSystem.GFFirefight => ArmyBuilding.GameSystems.GFFirefight,
            EGameSystem.AoFSkirmish => ArmyBuilding.GameSystems.AoFSkirmish,
            EGameSystem.GFStarQuest => ArmyBuilding.GameSystems.GFStarQuest,
            EGameSystem.AoFQuest => ArmyBuilding.GameSystems.AoFQuest,
            _ => ArmyBuilding.GameSystems.GrimdarkFuture,
        };

        /// <summary>Whose armies the game is played with: the Grimdark Future games take Grimdark Future
        /// armies, the Age of Fantasy games take Age of Fantasy armies.</summary>
        /// <summary>
        /// #406: each game type takes ONLY its own system's armies - Kai's call, 2026-10-29: no
        /// warnings, no allow-anyway. OPR publishes a separate book per system, with different points,
        /// unit sizes, available units and upgrade options, so a Grimdark Future list is simply not a
        /// legal Firefight army.
        ///
        /// <para>Game MODE does not appear here on purpose: Horde inherits its parent game type's
        /// system (GF Horde takes GF armies, GFF Horde takes GFF armies), so the army source is a
        /// property of the system alone.</para>
        ///
        /// <para>
        /// A Quest system's own side is not an army at all - heroes come from the Hero Builder - so what
        /// this answers for one is where its ENEMY force comes from. #406 left that as an open question
        /// ("confirm whether Star Quest publishes its own enemy book") and defaulted Star Quest to
        /// Grimdark Future's.
        /// </para>
        /// <para>
        /// #412 answers it: <b>it does, and the points are on a different scale.</b> Army Forge serves 54
        /// official Star Quest enemy books, and Alien Hives' Grunt Veteran is 40pts in Grimdark Future
        /// against 100pts there. Every Star Quest wave and mission priced off the Grimdark Future book
        /// was therefore buying roughly two and a half times the enemy its points were meant to pay for -
        /// which is not an approximation, it is the wrong data, and it is what made Star Quest stop being
        /// its own game. So Star Quest takes Star Quest armies.
        /// </para>
        /// <para>
        /// #417 does the same for AoF: Quest, which used to fall through to Age of Fantasy: its 48 enemy
        /// books are imported now (Army Forge id 8, NOT id 7 - that one is the hero roster), so AoF:
        /// Quest takes AoF: Quest armies. The hero roster is still missing, which is why the system is
        /// not selectable yet - but the enemy data is right, so when it becomes selectable this line
        /// does not have to move.
        /// </para>
        /// </summary>
        public static ArmyBuilding.EAllowedGameSystems ArmySource(EGameSystem system) => system switch
        {
            EGameSystem.GFFirefight => ArmyBuilding.EAllowedGameSystems.GFFirefight,
            EGameSystem.AoFSkirmish => ArmyBuilding.EAllowedGameSystems.AoFSkirmish,
            EGameSystem.GFStarQuest => ArmyBuilding.EAllowedGameSystems.GFStarQuest,
            EGameSystem.AoFQuest => ArmyBuilding.EAllowedGameSystems.AoFQuest,
            EGameSystem.AgeOfFantasy => ArmyBuilding.EAllowedGameSystems.AgeOfFantasy,
            _ => ArmyBuilding.EAllowedGameSystems.GrimdarkFuture,
        };

        /// <summary>Whether the lobby may pick it yet. The others are listed so the line-up is visible, but
        /// stay greyed out until their rules are in.</summary>
        /// <summary>
        /// Whether the lobby may pick it. <b>Every system this game carries data for is now selectable</b>,
        /// so this returns true for all of <see cref="DisplayOrder"/> - the method stays because the gate
        /// is what a new system has to pass, and the next one added starts out failing it.
        /// <para>
        /// #418 added AoF: Quest, and the bar it had to clear is worth recording: its own enemy books
        /// (#417), its own 161-hero roster, its own faction/class data, a party stamped as its own
        /// system, and - the part that actually took the work - its own rules profile, because its book
        /// is the <b>2.0.1</b> generation where Star Quest is on 2.1.0 and they disagree about power
        /// costs, failed hero skills, failed casts, melee reach, Scout and the Villain aura. Until that
        /// existed, making this line say yes would have played AoF: Quest by another system's rules,
        /// which is the one thing CLAUDE.md says never to do.
        /// </para>
        /// <para>
        /// <b>#420: AoF: Skirmish was left out of this list and nothing was blocking it.</b> It had its
        /// own 46 army books (#412), its slug, army folder, force-org profile, CLI alias and table/
        /// coherency rules, and it shares <see cref="Rules.Dispatch.CoreRulesProfile.Skirmish"/> with
        /// Firefight, which was selectable throughout. It also played full headless games the whole
        /// time. The line simply never got updated when its books landed, so one of the six game modes
        /// sat greyed out in the lobby for no reason anybody had written down - which is why this now
        /// enumerates the whole display order rather than listing members by hand.
        /// </para>
        /// </summary>
        public static bool IsAvailable(EGameSystem system) => Array.IndexOf(DisplayOrder, system) >= 0;

        /// <summary>
        /// The system an Army Forge slug names, or null when it names none this game plays.
        ///
        /// <para>
        /// #421 added it because saving an army needs to put the file in its game's folder, and the army
        /// carries a SLUG while the folders are keyed by <see cref="EGameSystem"/>. The pairing is
        /// <see cref="Slug"/> read backwards, derived from it rather than typed out again - two
        /// hand-written halves of one mapping drift, and this one decides where a list is filed, so the
        /// drift would show up as an army that vanishes from the picker.
        /// </para>
        /// <para>
        /// Matching is case-insensitive and tolerates a null or blank slug as Grimdark Future, which is
        /// what <c>GameSystems.Normalize</c> means by an absent field: every army written before the
        /// field existed is a Grimdark Future army.
        /// </para>
        /// </summary>
        public static EGameSystem? FromSlug(string? slug)
        {
            string normalized = ArmyBuilding.GameSystems.Normalize(slug);
            foreach (EGameSystem system in DisplayOrder)
                if (string.Equals(Slug(system), normalized, StringComparison.OrdinalIgnoreCase))
                    return system;

            return null;
        }

        /// <summary>The Game Modes this game offers.</summary>
        public static EGameMode[] Modes(EGameSystem system) => system switch
        {
            // Both Quest systems play the Quest modes. The mode enum members are named for Star Quest
            // only because it got here first; they are the shared Quest modes. (The note that used to
            // sit here said AoF: Quest was "not selectable yet (no hero roster)" - #418 imported its
            // 161-hero roster and made it selectable, and the comment outlived that by two weeks.)
            EGameSystem.GFStarQuest or EGameSystem.AoFQuest
                => new[] { EGameMode.StarQuestMission, EGameMode.StarQuestHorde },
            _ => new[] { EGameMode.Standard, EGameMode.Horde },
        };

        /// <summary>True for the Star Quest / Quest games: the local side plays a party of heroes.</summary>
        public static bool IsQuest(EGameSystem system) => system is EGameSystem.GFStarQuest or EGameSystem.AoFQuest;

        /// <summary>
        /// What a Quest system calls itself in game text: "Star Quest" (GF) or "Quest" (AoF: Quest). The Quest
        /// modes were built for Star Quest first and their labels said "Star Quest" in AoF: Quest too (Kai,
        /// 2026-11-05 screenshot: "AoF: Quest - Star Quest Horde").
        /// </summary>
        public static string QuestName(EGameSystem system) => system == EGameSystem.AoFQuest ? "Quest" : "Star Quest";

        /// <summary>A game mode's name as the lobby and saves show it, worded for the system it is played in.</summary>
        public static string ModeLabel(EGameMode mode, EGameSystem system) => mode switch
        {
            EGameMode.Standard => "Standard",
            EGameMode.Horde => "Horde",
            EGameMode.StarQuestHorde => $"{QuestName(system)} Horde",
            EGameMode.StarQuestMission => $"{QuestName(system)} Mission",
            _ => mode.ToString(),
        };

        /// <summary>True for the skirmish games (GF: Firefight / AoF: Skirmish): 4' x 4' table, 6" coherency,
        /// army morale at the end of each round instead of morale from wounds and melee.</summary>
        public static bool IsSkirmish(EGameSystem system) => system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        /// <summary>Horde-style modes: one defending side against waves.</summary>
        public static bool IsHordeLike(EGameMode mode) => mode is EGameMode.Horde or EGameMode.StarQuestHorde or EGameMode.StarQuestMission;

        /// <summary>Star Quest modes: a party of heroes against the AI, with hero actions.</summary>
        public static bool IsQuestMode(EGameMode mode) => mode is EGameMode.StarQuestHorde or EGameMode.StarQuestMission;
    }

    /// <summary>
    /// The ruleset a match is played under. Standard first so it is both the default and what
    /// <c>default(GameSettings)</c> / a pre-Horde save resolves to. Append new modes at the end -
    /// the numeric value is what saves and the wire carry.
    /// </summary>
    public enum EGameMode
    {
        /// <summary>The normal Grimdark Future game: four rounds, objectives decide the winner.</summary>
        Standard,

        /// <summary>OPR Horde Mode v3.6.0 (Grimdark Future): defenders survive waves of enemies.</summary>
        Horde,

        /// <summary>GF: Star Quest Horde Mode v2.0.0: 2-4 heroes hold out against waves that arrive as the
        /// enemies' alertness rises, earning XP and levelling up between waves.</summary>
        StarQuestHorde,

        /// <summary>GF: Star Quest one-off mission (Campaign Builder): primary + secondary objectives, sentries and
        /// reinforcements, then the heroes leave by a table corner.</summary>
        StarQuestMission,
    }

    /// <summary>
    /// The three Challenge Bonuses (Solo &amp; Co-Op Rules v3.6.0) a game can give the AI. Flags, because the
    /// book says "any of the following". The numeric values are what saves and the wire carry - append only.
    /// </summary>
    [Flags]
    public enum EChallengeBonuses
    {
        None = 0,

        /// <summary>"When it's the AI's turn to activate a unit, it gets to activate D3 units instead."</summary>
        ExtraActivations = 1,

        /// <summary>"If the AI is tied with the players for objective control or VPs, then all of its units get
        /// +1 to hit rolls."</summary>
        TiedHitBonus = 2,

        /// <summary>"If the AI is losing for objective control or VPs, then all of its units get +1 hit rolls and
        /// +1 to Defense rolls."</summary>
        LosingBonus = 4,

        All = ExtraActivations | TiedHitBonus | LosingBonus,
    }

    /// <summary>Normal play, or Game Master testing tools for the host. Append new values at the end.</summary>
    public enum EPlayStyle
    {
        Standard,
        GameMaster,
    }

    public enum ERandomnessType
    {
        Realistic,
        Probabilistic
    }

    public enum ETurnStyle
    {
        Standard,
        BoltAction
    }

    /// <summary>
    /// #371 - when a shooting unit commits to its targets.
    /// </summary>
    public enum EShootingMode
    {
        /// <summary>
        /// Pick a weapon and a target, resolve it to wounds, then pick the next weapon - already knowing
        /// what the last one killed. Declared first so it is both the default and what
        /// <c>default(GameSettings)</c> / a pre-#371 save resolves to.
        /// </summary>
        OneAtATime,

        /// <summary>
        /// Declare a target for every weapon the unit intends to fire, THEN roll them all. Casualties
        /// from an earlier weapon cannot re-aim a later one; if a declared target is wiped out before
        /// its weapon fires, those shots are lost.
        /// </summary>
        DeclareFirst,
    }

    public enum ETerrainPlacementMode
    {
        /// <summary>Server places the built-in default terrain pool verbatim. Default for headless/automation.</summary>
        AutoFromLayout,

        /// <summary>Roll-off + alternating placement, one piece at a time, until <see cref="GameSettings.TerrainPieceCount"/> reached.</summary>
        Alternating,

        /// <summary>Server places the contents of <see cref="GameSettings.TerrainLayoutPath"/> verbatim.</summary>
        LoadFromFile,

        /// <summary>
        /// #301 "Alternating: Points" - roll-off + alternating placement like <see cref="Alternating"/>
        /// ("Alternating: One Per" in the UI), but each piece costs its <see cref="SaveLoad.TerrainPieceEntry.Points"/>
        /// and a turn spends <see cref="GameSettings.TerrainPointsPerTurn"/> points (one big piece or
        /// several small ones) until <see cref="GameSettings.TerrainPointsTotal"/> is exhausted.
        /// Declared last so the wire/save value of the older modes is unchanged.
        /// </summary>
        AlternatingPoints,
    }

    public enum EObjectivePlacementMode
    {
        /// <summary>
        /// Server auto-places every objective marker with no player interaction, using the
        /// balanced placement in <see cref="FDG.Stages.ObjectiveAutoPlacer"/> (the same
        /// algorithm the solo-rules AI uses). Default; the fast path for debug/automation.
        /// </summary>
        AutoPlaced,

        /// <summary>
        /// Roll-off + alternating placement: each player is asked to place a marker in turn
        /// (human via a resolver, AI via <see cref="FDG.Ai.Resolvers.AiPlaceObjectiveResolver"/>).
        /// </summary>
        PlayerPlaced,
    }
}
