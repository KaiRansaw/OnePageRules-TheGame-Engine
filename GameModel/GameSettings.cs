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
        /// AoF: Quest still falls through to Age of Fantasy, and that is a known gap rather than a
        /// finding: its own enemy books exist at Army Forge (id 8, 48 of them) and are simply not
        /// imported yet. The system is not selectable in the lobby either, so nothing plays on the wrong
        /// data today - but the moment it becomes selectable this line has to move with it.
        /// </para>
        /// </summary>
        public static ArmyBuilding.EAllowedGameSystems ArmySource(EGameSystem system) => system switch
        {
            EGameSystem.GFFirefight => ArmyBuilding.EAllowedGameSystems.GFFirefight,
            EGameSystem.AoFSkirmish => ArmyBuilding.EAllowedGameSystems.AoFSkirmish,
            EGameSystem.GFStarQuest => ArmyBuilding.EAllowedGameSystems.GFStarQuest,
            EGameSystem.AgeOfFantasy or EGameSystem.AoFQuest
                => ArmyBuilding.EAllowedGameSystems.AgeOfFantasy,
            _ => ArmyBuilding.EAllowedGameSystems.GrimdarkFuture,
        };

        /// <summary>Whether the lobby may pick it yet. The others are listed so the line-up is visible, but
        /// stay greyed out until their rules are in.</summary>
        public static bool IsAvailable(EGameSystem system) => system is
            EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy or EGameSystem.GFFirefight or EGameSystem.GFStarQuest;

        /// <summary>The Game Modes this game offers.</summary>
        public static EGameMode[] Modes(EGameSystem system) => system switch
        {
            EGameSystem.GFStarQuest => new[] { EGameMode.StarQuestMission, EGameMode.StarQuestHorde },
            _ => new[] { EGameMode.Standard, EGameMode.Horde },
        };

        /// <summary>True for the Star Quest / Quest games: the local side plays a party of heroes.</summary>
        public static bool IsQuest(EGameSystem system) => system is EGameSystem.GFStarQuest or EGameSystem.AoFQuest;

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
