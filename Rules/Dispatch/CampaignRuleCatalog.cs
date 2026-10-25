using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// GF: Firefight Campaign (v3.5.1, Campaign Rules) traits a strike team unit keeps between games: hero injuries,
/// hero talents and hero skill traits. They are written into the unit's rules in the strike team file, so they work
/// like any other special rule. The ones whose effect lives elsewhere (auras around the hero, wound-effect rolls,
/// the first morale test of a match) are plain markers here and are read by <c>FirefightCampaign</c> /
/// <c>FirefightRules</c> by name. Named "Injury: X" / "Talent: X" / "Skill: X" so they never collide with an army
/// book's own rule of the same name.
/// </summary>
public static class CampaignRuleCatalog
{
    public const string InjuryPrefix = "Injury: ";
    public const string TalentPrefix = "Talent: ";
    public const string SkillPrefix = "Skill: ";

    // ── Injuries ────────────────────────────────────────────────────────────────────────────────────

    public static SpecialRuleDefinition ChestWound { get; } = new("Injury: Chest Wound",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.AllModelsHaveThisRule(),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to rolls when blocking hits.");

    public static SpecialRuleDefinition BlindedEye { get; } = new("Injury: Blinded Eye",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Not(new Condition.IsMelee()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to hit rolls when shooting.");

    public static SpecialRuleDefinition ArmInjury { get; } = new("Injury: Arm Injury",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.IsMelee(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to hit rolls in melee.");

    public static SpecialRuleDefinition Traumatized { get; } = new("Injury: Traumatized",
        new[]
        {
            new HookEntry(EHookID.Morale_OnPreMoraleTest,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Morale, Delta: -1),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to morale test rolls.");

    public static SpecialRuleDefinition SmashedLeg { get; } = new("Injury: Smashed Leg",
        Moves(-1f, -2f),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: moves -1\" when Advancing and -2\" when Rushing or Charging.");

    // ── Talents ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Read by <c>MoraleUtilities.TakeMoraleTest</c>: the first morale test of the match is passed.</summary>
    public static SpecialRuleDefinition Motivated { get; } = Marker("Talent: Motivated",
        "Campaign talent: always passes the first morale test of the match.");

    /// <summary>Read by <c>FirefightCampaign.OnAttack</c>: +1 hit on a charge (the rule says +1 attack).</summary>
    public static SpecialRuleDefinition Crazed { get; } = Marker("Talent: Crazed",
        "Campaign talent: +1 attack in melee when charging (played as +1 hit).");

    public static SpecialRuleDefinition BitterRivalry { get; } = new("Talent: Bitter Rivalry",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.TargetHasRule("Hero"),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign talent: +1 to hit against enemy heroes.");

    public static SpecialRuleDefinition HorribleScars { get; } = MeleeDodge("Talent: Horrible Scars",
        "Campaign talent: enemy units get -1 to hit when in melee against the hero.");

    /// <summary>Tough(+1) is written into the unit's Tough value; this marker just records the talent.</summary>
    public static SpecialRuleDefinition Toughened { get; } = Marker("Talent: Toughened",
        "Campaign talent: Tough(+1) (already added to the hero's Tough).");

    public static SpecialRuleDefinition NaturalTalent { get; } = Marker("Talent: Natural Talent",
        "Campaign talent: the hero recovered and got +1 XP.");

    // ── Hero skills ─────────────────────────────────────────────────────────────────────────────────

    // Captain - read by FirefightCampaign when a friendly unit activates within 6" of the hero.
    public static SpecialRuleDefinition Leader { get; } = Marker("Skill: Leader",
        "Campaign skill: friendly units that activate within 6\" of the hero get +1 to morale test rolls.");
    public static SpecialRuleDefinition Instigator { get; } = Marker("Skill: Instigator",
        "Campaign skill: friendly units that activate within 6\" of the hero get +1 to hit in melee.");
    public static SpecialRuleDefinition Tactician { get; } = Marker("Skill: Tactician",
        "Campaign skill: friendly units that activate within 6\" of the hero get +1 to hit when shooting.");

    // Support
    public static SpecialRuleDefinition Vanguard { get; } = Marker("Skill: Vanguard",
        "Campaign skill: friendly units that activate within 6\" of the hero get +4\" charge range.");
    public static SpecialRuleDefinition Scavenger { get; } = Marker("Skill: Scavenger",
        "Campaign skill: friendly units that activate within 6\" of the hero get +6\" range when shooting.");
    public static SpecialRuleDefinition Mastermind { get; } = Marker("Skill: Mastermind",
        "Campaign skill: enemy units within 6\" of the hero get -1 Defense.");

    // Fighter
    public static SpecialRuleDefinition Duellist { get; } = MeleeDodge("Skill: Duellist",
        "Campaign skill: enemy units get -1 to hit in melee against the hero.");
    /// <summary>+1 attack is written into the hero's melee weapon; this marker records the skill.</summary>
    public static SpecialRuleDefinition Fanatic { get; } = Marker("Skill: Fanatic",
        "Campaign skill: the hero gets +1 attack in melee (already added to its melee weapon).");
    public static SpecialRuleDefinition Berserker { get; } = Marker("Skill: Berserker",
        "Campaign skill: enemies get +1 to wound effect rolls when taking wounds from the hero in melee.");

    // Shooter
    public static SpecialRuleDefinition Hunter { get; } = new("Skill: Hunter",
        new[]
        {
            new HookEntry(EHookID.Activation_OnActionChoice,
                new Condition.Always(),
                new Effect.ShootAfterRush(),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: the hero may shoot even after Rush actions.");
    public static SpecialRuleDefinition Destroyer { get; } = new("Skill: Destroyer",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.Not(new Condition.IsMelee()),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: the hero gets AP(+1) when shooting.");
    public static SpecialRuleDefinition Suppressor { get; } = Marker("Skill: Suppressor",
        "Campaign skill: enemies the hero shoots at get -1 to hit when shooting until the end of the round.");

    // Pathfinder
    public static SpecialRuleDefinition Runner { get; } = new("Skill: Runner",
        Moves(+2f, +2f),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: the hero always moves +2\".");
    public static SpecialRuleDefinition Prowler { get; } = new("Skill: Prowler",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.DistanceGreaterThan(12f), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: enemy units further than 12\" away get -1 to hit when shooting at the hero.");
    public static SpecialRuleDefinition Climber { get; } = Marker("Skill: Climber",
        "Campaign skill: the hero needs only 2+ when jumping (this game has no jumping, so no effect).");

    // Healer - read by FirefightRules.ApplyWounds / FirefightCampaign.
    public static SpecialRuleDefinition Chemist { get; } = Marker("Skill: Chemist",
        "Campaign skill: friendly units within 6\" of the hero get -1 to wound effect rolls.");
    public static SpecialRuleDefinition Herbalist { get; } = Marker("Skill: Herbalist",
        "Campaign skill: friendly units within 6\" of the hero may ignore wounds on a roll of 6+.");
    public static SpecialRuleDefinition Warden { get; } = Marker("Skill: Warden",
        "Campaign skill: when the hero is activated, all friendly units within 6\" stop being Shaken.");

    /// <summary>Granted for one activation by a Vanguard hero's aura.</summary>
    public static SpecialRuleDefinition VanguardCharge { get; } = new("+4\" charge range",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "+4\" when Charging (Vanguard).");

    public static IReadOnlyList<SpecialRuleDefinition> All => new[]
    {
        ChestWound, BlindedEye, ArmInjury, Traumatized, SmashedLeg,
        Motivated, Crazed, BitterRivalry, HorribleScars, Toughened, NaturalTalent,
        Leader, Instigator, Tactician, Vanguard, Scavenger, Mastermind, Duellist, Fanatic, Berserker,
        Hunter, Destroyer, Suppressor, Runner, Prowler, Climber, Chemist, Herbalist, Warden,
        VanguardCharge,
    };

    private static SpecialRuleDefinition Marker(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Valence: EValence.Positive,
            Description: description);

    private static SpecialRuleDefinition MeleeDodge(string name, string description) => new(name,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: description);

    private static HookEntry[] Moves(float advance, float rushCharge) => new[]
    {
        new HookEntry(EHookID.Movement_OnMoveActionDeclared,
            new Condition.ActionTypeIs(EActionType.Advance),
            new Effect.MovementBonus(EActionType.Advance, DistanceInches: advance),
            ELifetime.ThisActivation),
        new HookEntry(EHookID.Movement_OnMoveActionDeclared,
            new Condition.ActionTypeIs(EActionType.Rush),
            new Effect.MovementBonus(EActionType.Rush, DistanceInches: rushCharge),
            ELifetime.ThisActivation),
        new HookEntry(EHookID.Movement_OnMoveActionDeclared,
            new Condition.ActionTypeIs(EActionType.Charge),
            new Effect.MovementBonus(EActionType.Charge, DistanceInches: rushCharge),
            ELifetime.ThisActivation),
    };
}
