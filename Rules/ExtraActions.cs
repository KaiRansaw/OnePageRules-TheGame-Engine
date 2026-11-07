using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG
{
    /// <summary>Which Extra Actions the players agreed to use. Any combination; None is the standard game.</summary>
    [Flags]
    public enum EExtraActions
    {
        None = 0,
        LastStand = 1,
        HunkerDown = 2,
        DefensiveStance = 4,
        FocusedFire = 8,
        HeavyCharge = 16,
        StealthMove = 32,
        CoveringFire = 64,
        Overwatch = 128,
        All = 255,
    }

    /// <summary>
    /// Extra Actions, from the Advanced Rulebooks 3.6.0 ("Advanced Actions - Extra Actions"; the same text in
    /// the GF, GF: Firefight, AoF and AoF: Skirmish books):
    ///
    /// <para>"Before the game, players can agree to use any or all of the following extra actions in the game
    /// ... When using these extra actions the game is played normally, however players may pick any of the
    /// selected extra actions in addition to the regular ones."</para>
    ///
    /// <list type="bullet">
    /// <item><b>Last Stand</b> - "Units may use a Last Stand action even if they are Shaken. When using Last
    /// Stand actions, units may use a Hold, Advance, Rush, or Charge action, but count as being Shaken for the
    /// purpose of special rules until the beginning of their next activation. Units in Last Stand get -1 to
    /// Quality and Defense rolls, halve their movement, can't contest or seize objectives, and can't use any
    /// special rules that require picking a target (ex.: Caster)."</item>
    /// <item><b>Hunker Down</b> - "the unit uses a Hold action and remains idle, and it gets +1 to Defense
    /// rolls when taking hits from shooting until its next activation. Shaken units may use Hunker Down
    /// actions to get this bonus as well as stopping to be Shaken."</item>
    /// <item><b>Defensive Stance</b> - "the unit uses a Hold action, remains idle, and may not strike back
    /// until its next activation, and it gets +1 to Defense rolls when taking hits in melee until its next
    /// activation. Shaken units may use Defensive Stance actions ..."</item>
    /// <item><b>Focused Fire</b> - "the unit uses a Hold action, and gets +1 to hit rolls when shooting at
    /// targets within 12"."</item>
    /// <item><b>Heavy Charge</b> - "the unit uses a Charge action to charge a target that is between 3" and 6"
    /// away, and gets +1 to hit rolls in melee."</item>
    /// <item><b>Stealth Move</b> - "the unit uses an Advance action, but may not shoot, and enemies shooting
    /// at it from over 12" away get -1 to their hit rolls until its next activation."</item>
    /// <item><b>Covering Fire</b> - "the unit uses a Hold action and shoots, but only hits on unmodified
    /// results of 6. Then, roll one die per hit, and if you roll at least one 4+, then the target must take a
    /// Morale Test (regardless of casualties). All hits are then discarded as misses."</item>
    /// <item><b>Overwatch</b> - "the unit uses a Hold action and remains idle, and until its next activation
    /// it may react once to an enemy unit's activation at any point as that enemy unit moves or shoots. ...
    /// Movement: Shoot at an enemy at any point during its movement, but gets -1 to hit rolls. Shooting: Shoot
    /// back at an enemy within 24" that shot at it, but gets -1 to hit rolls."</item>
    /// </list>
    ///
    /// <para>The state each action leaves behind is a token on the unit (see <see cref="TokenType"/>), so it
    /// is visible on the unit card, survives a save, and reaches a network client with the rest of the
    /// table. The judgement calls are in claude/house-rules.md, section "Extra Actions".</para>
    /// </summary>
    public static class ExtraActions
    {
        public const string LastStand = "Last Stand";
        public const string HunkerDown = "Hunker Down";
        public const string DefensiveStance = "Defensive Stance";
        public const string FocusedFire = "Focused Fire";
        public const string HeavyCharge = "Heavy Charge";
        public const string StealthMove = "Stealth Move";
        public const string CoveringFire = "Covering Fire";
        public const string Overwatch = "Overwatch";

        /// <summary>Focused Fire's and Stealth Move's 12" band.</summary>
        public const float CloseRangeInches = 12f;
        /// <summary>Heavy Charge: "a target that is between 3" and 6" away".</summary>
        public const float HeavyChargeMinInches = 3f;
        public const float HeavyChargeMaxInches = 6f;
        /// <summary>Overwatch: "Shoot back at an enemy within 24" that shot at it".</summary>
        public const float OverwatchShootBackInches = 24f;

        /// <summary>Every action in book order, with the flag that enables it.</summary>
        public static readonly IReadOnlyList<(string Name, EExtraActions Flag)> All = new[]
        {
            (LastStand, EExtraActions.LastStand),
            (HunkerDown, EExtraActions.HunkerDown),
            (DefensiveStance, EExtraActions.DefensiveStance),
            (FocusedFire, EExtraActions.FocusedFire),
            (HeavyCharge, EExtraActions.HeavyCharge),
            (StealthMove, EExtraActions.StealthMove),
            (CoveringFire, EExtraActions.CoveringFire),
            (Overwatch, EExtraActions.Overwatch),
        };

        /// <summary>What each one does, as the action menu shows it. ASCII only.</summary>
        public static string Describe(string name) => name switch
        {
            LastStand => "Act even though Shaken: Hold, Advance, Rush or Charge. Until its next activation it counts as Shaken "
                + "for special rules, gets -1 to Quality and Defense rolls, moves half as far, can't seize or contest "
                + "objectives and can't use rules that pick a target (like casting).",
            HunkerDown => "Hold and stay idle. +1 to Defense rolls against shooting until its next activation. A Shaken unit "
                + "also recovers.",
            DefensiveStance => "Hold and stay idle. +1 to Defense rolls in melee, but it may not strike back, until its next "
                + "activation. A Shaken unit also recovers.",
            FocusedFire => "Hold and shoot, with +1 to hit against targets within 12\".",
            HeavyCharge => "Charge a target that is 3\" to 6\" away, with +1 to hit in melee.",
            StealthMove => "Advance without shooting. Enemies shooting from over 12\" get -1 to hit until its next activation.",
            CoveringFire => "Hold and shoot to suppress: hits only on unmodified 6s, then one die per hit - any 4+ forces the "
                + "target to take a Morale Test. The hits do no damage.",
            Overwatch => "Hold and stay idle. Once before its next activation it may shoot at -1 to hit at an enemy that moves, "
                + "or at one within 24\" that shoots it.",
            _ => "",
        };

        /// <summary>
        /// The agreed set for this game. The Quest systems have no Advanced Rulebook, so none apply there.
        /// </summary>
        public static EExtraActions Enabled(GameSettings settings) =>
            GameSystemInfo.IsQuest(settings.GameSystem) || GameSystemInfo.IsQuestMode(settings.GameMode)
                ? EExtraActions.None
                : settings.ExtraActions;

        public static bool IsOn(IGameContext game, EExtraActions action) => (Enabled(game.Settings) & action) != 0;

        public static bool AnyOn(IGameContext game) => Enabled(game.Settings) != EExtraActions.None;

        public static EExtraActions FlagOf(string name) =>
            All.FirstOrDefault(a => a.Name == name).Flag;

        // --- the state each action leaves on the unit -------------------------------------------------

        /// <summary>The tokens that last "until its next activation" - cleared when the unit next activates.</summary>
        private static readonly TokenType[] UntilNextActivation =
        {
            TokenType.LastStand, TokenType.HunkerDown, TokenType.DefensiveStance, TokenType.StealthMove,
            TokenType.Overwatch, TokenType.OverwatchShotAt,
        };

        /// <summary>Called as a unit activates: the previous activation's Extra Action has run its course.</summary>
        public static void ClearAtActivation(IUnit unit)
        {
            foreach (TokenType type in UntilNextActivation)
            {
                if (unit.Tokens.HasToken(type)) unit.Tokens.RemoveTokens(type);
            }
        }

        public static void Mark(IUnit unit, TokenType type)
        {
            if (!unit.Tokens.HasToken(type)) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(type));
        }

        public static bool InLastStand(IUnit unit) => unit.Tokens.HasToken(TokenType.LastStand);

        /// <summary>
        /// Last Stand "count[s] as being Shaken for the purpose of special rules": a rule condition reading the
        /// Shaken token sees one more. Core rules read the token directly and are not affected.
        /// </summary>
        public static int ShakenForSpecialRules(ITokenContainer tokens, TokenType type) =>
            type == TokenType.Shaken && tokens.HasToken(TokenType.LastStand) ? 1 : 0;

        /// <summary>Last Stand: "halve their movement".</summary>
        public static float MoveFactor(IUnit unit) => InLastStand(unit) ? 0.5f : 1f;

        /// <summary>
        /// The hit-roll modifiers the Extra Actions add to one roll, each named for the dice chips. A positive
        /// delta makes the roll easier (the granted-modifier convention).
        /// </summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IUnit attacker, IUnit defender, bool isMelee,
            bool isSpell, float distanceInches)
        {
            var result = new List<GrantedModifier>();
            if (isSpell) return result;
            if (InLastStand(attacker)) result.Add(new GrantedModifier(LastStand, -1));
            if (!isMelee && attacker.Tokens.HasToken(TokenType.FocusedFire) && distanceInches <= CloseRangeInches + 0.001f)
                result.Add(new GrantedModifier(FocusedFire, 1));
            if (isMelee && attacker.Tokens.HasToken(TokenType.HeavyCharge))
                result.Add(new GrantedModifier(HeavyCharge, 1));
            if (!isMelee && attacker.Tokens.HasToken(TokenType.OverwatchFiring))
                result.Add(new GrantedModifier(Overwatch, -1));
            if (!isMelee && defender.Tokens.HasToken(TokenType.StealthMove) && distanceInches > CloseRangeInches + 0.001f)
                result.Add(new GrantedModifier(StealthMove, -1));
            return result;
        }

        /// <summary>The Defense-roll modifiers for the unit taking the hits. Positive is easier to save.</summary>
        public static IReadOnlyList<GrantedModifier> SaveModifiers(IUnit defender, bool isMelee, bool isSpell)
        {
            var result = new List<GrantedModifier>();
            if (InLastStand(defender)) result.Add(new GrantedModifier(LastStand, -1));
            if (!isMelee && !isSpell && defender.Tokens.HasToken(TokenType.HunkerDown))
                result.Add(new GrantedModifier(HunkerDown, 1));
            if (isMelee && defender.Tokens.HasToken(TokenType.DefensiveStance))
                result.Add(new GrantedModifier(DefensiveStance, 1));
            return result;
        }

        /// <summary>Last Stand's -1 to Quality applies to morale tests too (they are Quality rolls).</summary>
        public static int MoraleModifier(IUnit unit) => InLastStand(unit) ? -1 : 0;

        public static int Net(IReadOnlyList<GrantedModifier> modifiers) => modifiers.Sum(m => m.Delta);

        /// <summary>Last Stand bars "special rules that require picking a target": any ability not aimed at itself.</summary>
        public static bool PicksATarget(AbilityOffer offer) =>
            offer.Ability.TargetSelector is { } selector && selector.TargetAffinity != ETargetAffinity.Self;

        /// <summary>Defensive Stance: "may not strike back until its next activation".</summary>
        public static bool MayNotStrikeBack(IUnit unit) => unit.Tokens.HasToken(TokenType.DefensiveStance);

        /// <summary>Last Stand: "can't contest or seize objectives".</summary>
        public static bool CannotHoldObjectives(IUnit unit) => InLastStand(unit);
    }
}
