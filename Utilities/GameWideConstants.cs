
namespace FDG
{
    public static class GameWideConstants
    {
        public const float MOVE_SHOOT_DISTANCE_INCHES = 6;

        public const float RUSH_DISTANCE_INCHES = 12;

        public const float CHARGE_DISTANCE_INCHES = 12;

        /// <summary>
        /// How close models must be to engage in melee, after the charge has already happened.
        ///
        /// <para>
        /// **#414: a flat 3", and this is an OPR 3.6.0 correction rather than a Star Quest one.** Every
        /// 3.6.0 core book reads *"Charging models must move into base contact with targets, or as close
        /// as possible, and then defenders must do the same by moving up to 3". Models within 3" of
        /// enemies must strike with all melee weapons"* - checked word for word in **GF Core Rules
        /// v3.6.0**, **GFF Core Rules v3.6.0** and **GFSQ Core Rules v2.1.0**. One distance, no vertical
        /// clause anywhere.
        /// </para>
        ///
        /// <para>
        /// The split 2" horizontal / 4" vertical the engine used until now is the **3.5.1** wording
        /// ("All models that are within 2" horizontally and 4" vertically ... may attack it"), which
        /// only ever appeared in the Advanced Rules and the pre-3.6 Firefight book. R1 moved the game to
        /// 3.6.0 and missed it, so it is kept as one constant for both axes rather than made per-system:
        /// there is no system left that wants the split.
        /// </para>
        ///
        /// <para>
        /// It cuts both ways in play - reach grows by an inch on the flat, and shrinks by one up or
        /// down, so a model on a 4" ledge can no longer fight what is below it.
        /// </para>
        /// </summary>
        /// <para>
        /// #418 made this a field set per game rather than a compile-time constant, because <b>AoF:
        /// Quest still wants the split</b>: its book is the 2.0.1 generation and reads "Models within
        /// <b>2" horizontally and 4" vertically</b> of enemies must strike". Every other system - and
        /// Star Quest on 2.1.0 - is the flat 3" above, which is still the default here, so a caller
        /// that never calls <see cref="ConfigureTable"/> behaves exactly as it did.
        /// </para>
        public static float MELEE_RANGE_INCHES_HORIZONTAL = MELEE_RANGE_INCHES;

        /// <summary>The same 3" for 3.6.0 and GFSQ 2.1.0, which state one melee distance and no
        /// vertical exception; 4" under AoFQ 2.0.1. Set by <see cref="ConfigureTable"/>.</summary>
        public static float MELEE_RANGE_INCHES_VERTICAL = MELEE_RANGE_INCHES;

        /// <summary>Melee reach, OPR 3.6.0 / GFSQ 2.1.0: a flat 3" in every direction.</summary>
        public const float MELEE_RANGE_INCHES = 3;

        /// <summary>
        /// Furthest any model in a unit can be from the closest other model in the same unit.
        /// </summary>
        public const float MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES = 1;

        /// <summary>
        /// Furthest any model in a unit can be from any other model in the same unit.
        /// </summary>
        /// The full game uses 9"; the skirmish games (GF: Firefight, AoF: Skirmish) use 6". Set by
        /// <see cref="ConfigureTable"/> when a game starts.
        public static float MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES { get; private set; } = STANDARD_COHERENCY_INCHES;

        public const float STANDARD_COHERENCY_INCHES = 9;
        public const float SKIRMISH_COHERENCY_INCHES = 6;

        public const float DIFFICULT_TERRAIN_MOVE_CAP_INCHES = 6f;

        /// <summary>
        /// Models that aren't charging must keep at least this far (base-to-base) from enemy models.
        /// A move may end either at or beyond this standoff, or in base contact (a charge) — but not in
        /// the band between, and never overlapping or passing through an enemy base.
        /// </summary>
        public const float ENEMY_STANDOFF_DISTANCE_INCHES = 1f;

        /// <summary>
        /// House rule: when true, separate friendly units may end within 1" of each other and may
        /// pass through each other's bases. Ending stacked on another model stays illegal, and the
        /// enemy standoff above is untouched. Set by <see cref="ConfigureTable"/> from
        /// <see cref="GameModel.GameSettings.FriendlyUnitsMayOverlap"/> when a game starts; false
        /// (rules-correct) unless the lobby turned it on.
        /// </summary>
        public static bool FRIENDLY_UNITS_MAY_OVERLAP { get; private set; } = false;

        /// <summary>
        /// Depth of each side's deployment zone: "places one unit fully within 12\" of their table edge"
        /// (GF v3.5.1 core rules, Deploying Armies). Was 9" before the rules-accuracy pass.
        /// </summary>
        public const float DEPLOYMENT_DISTANCE_INCHES = 12;

        /// <summary>The table the current game is played on (72" x 48" for Grimdark Future; Star Quest plays on a
        /// 48" x 48" table). Set by <see cref="ConfigureTable"/> when a game starts; everything that needs the table's
        /// size reads these. Not constants any more, so a game type can change them.</summary>
        public static float DEFAULT_TABLE_WIDTH_INCHES { get; private set; } = STANDARD_TABLE_WIDTH_INCHES;

        public static float DEFAULT_TABLE_HEIGHT_INCHES { get; private set; } = STANDARD_TABLE_HEIGHT_INCHES;

        /// <summary>The standard 6' x 4' Grimdark Future table.</summary>
        public const float STANDARD_TABLE_WIDTH_INCHES = 72;
        public const float STANDARD_TABLE_HEIGHT_INCHES = 48;

        /// <summary>The table size a game type plays on: Star Quest / Quest use a 4' x 4' area, everything else
        /// the standard 6' x 4'.</summary>
        public static (float Width, float Height) TableSizeFor(EGameSystem system) => system switch
        {
            EGameSystem.GFStarQuest or EGameSystem.AoFQuest or EGameSystem.GFFirefight or EGameSystem.AoFSkirmish => (48f, 48f),
            _ => (STANDARD_TABLE_WIDTH_INCHES, STANDARD_TABLE_HEIGHT_INCHES),
        };

        /// <summary>Sets the table for the game about to be played. Called when a game (or a saved game) starts.</summary>
        /// <param name="friendlyUnitsMayOverlap">The lobby's house-rule toggle. Defaults to false so
        /// every existing caller (tests included) keeps the rules-correct behaviour.</param>
        public static void ConfigureTable(EGameSystem system, bool friendlyUnitsMayOverlap = false)
        {
            FRIENDLY_UNITS_MAY_OVERLAP = friendlyUnitsMayOverlap;
            (DEFAULT_TABLE_WIDTH_INCHES, DEFAULT_TABLE_HEIGHT_INCHES) = TableSizeFor(system);
            FDG.StateMachine.HordeRules.Configure(system);
            MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES = GameSystemInfo.IsSkirmish(system)
                ? SKIRMISH_COHERENCY_INCHES : STANDARD_COHERENCY_INCHES;
            // #418: melee reach is a flat 3" everywhere except AoF: Quest, whose 2.0.1 book keeps the
            // old 2" horizontal / 4" vertical split. A non-Quest system reads the flat value.
            if (GameSystemInfo.IsQuest(system))
            {
                FDG.Rules.Dispatch.QuestRulesProfile quest =
                    FDG.Rules.Dispatch.QuestRulesProfile.For(system);
                MELEE_RANGE_INCHES_HORIZONTAL = quest.MeleeReachHorizontalInches;
                MELEE_RANGE_INCHES_VERTICAL = quest.MeleeReachVerticalInches;
            }
            else
            {
                MELEE_RANGE_INCHES_HORIZONTAL = MELEE_RANGE_INCHES;
                MELEE_RANGE_INCHES_VERTICAL = MELEE_RANGE_INCHES;
            }
            // A new game starts: no Star Quest smoke left over from an earlier one.
            FDG.Stages.QuestTreasure.SmokeSpots = Array.Empty<Position>();
        }

        /// <summary>
        /// The number of rounds a game lasts before victory is calculated (the official GDF game length).
        /// After this many rounds have been reconciled the main phase hands off to victory calculation.
        /// </summary>
        public const int NUMBER_OF_ROUNDS = 4;

        /// <summary>OPR Horde Mode v3.6.0: the mission lasts twelve rounds. Waves arrive at the start of
        /// rounds 4, 7 and 10, and point rewards are paid at the end of rounds 3, 6, 9 and 12 - but the
        /// mission and the high score both count ROUNDS (#409).</summary>
        public const int HORDE_NUMBER_OF_ROUNDS = 12;

        /// <summary>OPR Horde Mode v3.6.0: a new wave of enemies starts at rounds 4, 7 and 10.</summary>
        public const int HORDE_ROUNDS_PER_WAVE = 3;

        /// <summary>
        /// Maximum number of distinct enemy units a single unit may target during one shoot action.
        /// </summary>
        public const int MAX_TARGETED_UNITS_PER_SHOOT_ACTION = 2;

        /// <summary>
        /// A Caster(X) can never hold more than this many spell tokens at once. Caster grants X tokens
        /// at the start of each round and unspent tokens carry over, so the round-start grant clamps the
        /// running total to this cap (#033).
        /// </summary>
        public const int MAX_SPELL_TOKENS = 6;

        /// <summary>
        /// Range within which other Caster units may spend their own spell tokens to modify a cast roll —
        /// friendly Casters for +1 each, enemy Casters for -1 each (#103). Measured unit-to-unit,
        /// base-to-base.
        /// </summary>
        public const float CASTER_ASSIST_RANGE_INCHES = 18f;
    }
}
