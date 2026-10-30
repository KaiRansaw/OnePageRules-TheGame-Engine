using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Campaign Rules v3.6.0 traits a campaign unit keeps between games: hero injury traits, hero talent
/// traits and hero skill traits. All four books (Grimdark Future, GF: Firefight, Age of Fantasy,
/// AoF: Skirmish) use the same tables, so there is one set of definitions here. They are written into
/// the unit's rules in the campaign army file and work like any other special rule.
/// <para>
/// This replaces the v3.5.1 Firefight tables in <see cref="CampaignRuleCatalog"/>, which stays
/// registered so a strike team file written before the 3.6 move still resolves its old rule names
/// instead of reporting them as unimplemented. Several names appear in both catalogs with a DIFFERENT
/// effect - 3.6 wins for anything created from now on.
/// </para>
/// <para>
/// Eleven of the eighteen hero skills are "once per game" effects (Explorer, Idol, Berserker,
/// Sharpshooter, Healer, Tactician, Strategist, Mastermind) or need a hook context the engine does not
/// expose yet (Motivator and Leader need to know what triggered a morale test; Duellist needs to know
/// the hero was the one charged). Those are plain markers here, named and described so they show on the
/// unit card and in the Battle Report, with no mechanical effect. This is the same gap already recorded
/// for Elite's once-per-game re-roll: the engine has no general per-unit once-per-game offer. Deferred
/// deliberately, recorded in WorkItems/410.
/// </para>
/// </summary>
public static class Campaign36RuleCatalog
{
    // -- Injury traits -------------------------------------------------------------------------------

    public static SpecialRuleDefinition Weakened { get; } = new("Injury: Weakened",
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
        Description: "Campaign injury: -1 to Defense rolls when blocking hits.");

    public static SpecialRuleDefinition Maimed { get; } = new("Injury: Maimed",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.IsMelee(),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to hit rolls when fighting in melee.");

    public static SpecialRuleDefinition Blinded { get; } = new("Injury: Blinded",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.Not(new Condition.IsMelee()), new Condition.AfterMoving()),
                new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: -1 to hit rolls when using move actions and shooting.");

    public static SpecialRuleDefinition InjuryCrippled { get; } = new("Injury: Crippled",
        Moves(-1f, -2f),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Negative,
        Description: "Campaign injury: moves -1\" on Advance and -2\" on Rush/Charge actions.");

    // -- Talent traits -------------------------------------------------------------------------------

    /// <summary>The +1 XP is handed out when the talent is rolled; this marker records it.</summary>
    public static SpecialRuleDefinition NaturalTalent { get; } = Marker("Talent: Natural Talent",
        "Campaign talent: the hero recovered and earned +1 XP.");

    public static SpecialRuleDefinition Motivated { get; } = new("Talent: Motivated",
        new[]
        {
            new HookEntry(EHookID.Morale_OnPreMoraleTest,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign talent: +1 to rolls when taking Morale Tests.");

    public static SpecialRuleDefinition Crazed { get; } = new("Talent: Crazed",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign talent: +1 to hit rolls in melee when charging.");

    public static SpecialRuleDefinition Vigilant { get; } = new("Talent: Vigilant",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Not(new Condition.IsMelee()),
                new Effect.RollModifier(ERollKind.Hit, Delta: +1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign talent: +1 to hit rolls when shooting.");

    /// <summary>Tough(+3) is written into the unit's Tough value; this marker records the talent.</summary>
    public static SpecialRuleDefinition Toughened { get; } = Marker("Talent: Toughened",
        "Campaign talent: Tough(+3) (already added to the hero's Tough).");

    // -- Pathfinder skills ---------------------------------------------------------------------------

    public static SpecialRuleDefinition Prowler { get; } = new("Skill: Prowler",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 1f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: moves +1\" on Advance actions.");

    public static SpecialRuleDefinition Runner { get; } = new("Skill: Runner",
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Rush),
                new Effect.MovementBonus(EActionType.Rush, DistanceInches: 2f),
                ELifetime.ThisActivation),
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 2f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: moves +2\" on Rush/Charge actions.");

    /// <summary>
    /// Once per game, ignore Difficult and Dangerous terrain for an activation.
    /// <para>
    /// Offered at <see cref="EHookID.Activation_OnActivationStart"/> rather than at the terrain hook,
    /// because only the ten hooks in <c>RuleFireLint.AbilityOfferingHooks</c> ever offer an
    /// <see cref="ActivatedAbility"/> - an ability authored at
    /// <see cref="EHookID.Movement_OnMoveThroughTerrain"/> would be dead data the fire-lint rejects.
    /// So the ability grants <see cref="ExplorerSurefooted"/> for the activation and the real waiver
    /// lives there as an ordinary passive, the way Flying expresses the same thing permanently.
    /// </para>
    /// <para>
    /// <see cref="ETerrainIgnoreScope.AllTerrain"/>, not <c>DifficultOnly</c>: the book waives
    /// Dangerous as well, and DifficultOnly is Strider, which does not.
    /// </para>
    /// </summary>
    public static SpecialRuleDefinition Explorer { get; } = new("Skill: Explorer",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnActivationStart, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.AddRule(ExplorerBoostName, ELifetime.ThisActivation),
                new Condition.Always()),
        },
        Valence: EValence.Positive,
        Description: "Campaign skill: once per game, when activated, ignores the effects of Difficult "
        + "and Dangerous terrain for that activation.");

    public const string ExplorerBoostName = "Explorer: Surefooted";

    /// <summary>The waiver <see cref="Explorer"/> grants itself for one activation. Not rolled on any
    /// table and not shown as a skill - it exists only as the body of Explorer's ability.</summary>
    public static SpecialRuleDefinition ExplorerSurefooted { get; } = new(ExplorerBoostName,
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                new Condition.Always(),
                new Effect.IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Ignores the effects of Difficult and Dangerous terrain this activation.");

    // -- Captain skills ------------------------------------------------------------------------------

    public static SpecialRuleDefinition Motivator { get; } = Marker("Skill: Motivator",
        "Campaign skill: +1 to Morale Test rolls triggered by shooting "
        + "(not wired up - a morale test does not record what triggered it yet).");

    public static SpecialRuleDefinition Leader { get; } = Marker("Skill: Leader",
        "Campaign skill: +1 to Morale Test rolls in melee "
        + "(not wired up - a morale test does not record what triggered it yet).");

    public static SpecialRuleDefinition Idol { get; } = Marker("Skill: Idol",
        "Campaign skill: once per game, may ignore becoming Shaken "
        + "(not wired up - the engine has no once-per-game offer yet).");

    // -- Warrior skills ------------------------------------------------------------------------------

    public static SpecialRuleDefinition Duellist { get; } = Marker("Skill: Duellist",
        "Campaign skill: AP(+1) in melee when charged "
        + "(not wired up - a melee strike-back does not record that this unit was the one charged).");

    public static SpecialRuleDefinition Fanatic { get; } = new("Skill: Fanatic",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: AP(+1) in melee when charging.");

    public static SpecialRuleDefinition Berserker { get; } = Marker("Skill: Berserker",
        "Campaign skill: once per game, may ignore becoming Fatigued "
        + "(not wired up - the engine has no once-per-game offer yet).");

    // -- Hunter skills -------------------------------------------------------------------------------

    // Suppressor and Destroyer both want AP(+1), which is a modifier on the TARGET's Defense roll, so
    // it has to be applied at Shooting_OnHitRollComplete. That hook's context does not carry whether
    // the attacker moved, so neither "when using Hold and shooting" nor "when using move actions and
    // shooting" can be expressed there - the catalog fire-lint catches it. Markers until the save-roll
    // context carries the attacker's action. Recorded in WorkItems/410.
    public static SpecialRuleDefinition Suppressor { get; } = Marker("Skill: Suppressor",
        "Campaign skill: AP(+1) when using Hold and shooting "
        + "(not wired up - the Defense roll does not know whether the attacker moved).");

    public static SpecialRuleDefinition Destroyer { get; } = Marker("Skill: Destroyer",
        "Campaign skill: AP(+1) when using move actions and shooting "
        + "(not wired up - the Defense roll does not know whether the attacker moved).");

    /// <summary>
    /// Once per game, +12" range when shooting, for an activation.
    /// <para>
    /// Same shape as <see cref="Explorer"/> and for the same reason: <c>Shooting_OnRangeCheck</c> is a
    /// passive-only hook, so the ability is offered at activation start and grants
    /// <see cref="SharpshooterRange"/>, whose passive is the range modifier. Offering it at
    /// <see cref="EHookID.Activation_OnBeforeAttackAction"/> would be closer to the book's "when
    /// shooting", but abilities there are taken INSTEAD of attacking, which would make the skill
    /// cancel the shot it is meant to extend.
    /// </para>
    /// </summary>
    public static SpecialRuleDefinition Sharpshooter { get; } = new("Skill: Sharpshooter",
        Array.Empty<HookEntry>(),
        new[]
        {
            new ActivatedAbility(EHookID.Activation_OnActivationStart, new Cost.OncePerGame(),
                new TargetSelector(0f, 1, 1, ETargetAffinity.Self, false),
                new Effect.AddRule(SharpshooterBoostName, ELifetime.ThisActivation),
                new Condition.Always()),
        },
        Valence: EValence.Positive,
        Description: "Campaign skill: once per game, when activated, its ranged weapons get +12\" "
        + "range for that activation.");

    public const string SharpshooterBoostName = "Sharpshooter: Long Shot";

    /// <summary>The +12" <see cref="Sharpshooter"/> grants itself for one activation. Mirrors the core
    /// rule "Increased Shooting Range" (+6), which is the shipped example of this passive.</summary>
    public static SpecialRuleDefinition SharpshooterRange { get; } = new(SharpshooterBoostName,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnRangeCheck,
                new Condition.Always(),
                new Effect.RangeModifier(Delta: +12),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "This unit's ranged weapons get +12\" range this activation.");

    // -- Warden skills -------------------------------------------------------------------------------

    public static SpecialRuleDefinition Herbalist { get; } = new("Skill: Herbalist",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.IsMelee(), new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Save, Delta: +1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: +1 to Defense rolls in melee.");

    public static SpecialRuleDefinition Chemist { get; } = new("Skill: Chemist",
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(new Condition.Not(new Condition.IsMelee()),
                                  new Condition.AllModelsHaveThisRule()),
                new Effect.RollModifier(ERollKind.Save, Delta: +1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Campaign skill: +1 to Defense rolls when shot.");

    public static SpecialRuleDefinition Healer { get; } = Marker("Skill: Healer",
        "Campaign skill: once per game, when activated, may restore D3 models/wounds "
        + "(not wired up - the engine has no once-per-game offer yet).");

    // -- Overseer skills -----------------------------------------------------------------------------

    public static SpecialRuleDefinition Tactician { get; } = Marker("Skill: Tactician",
        "Campaign skill: once per game, may re-roll one roll of any kind in melee "
        + "(not wired up - the engine has no once-per-game offer yet).");

    public static SpecialRuleDefinition Strategist { get; } = Marker("Skill: Strategist",
        "Campaign skill: once per game, may re-roll one roll of any kind when shooting "
        + "(not wired up - the engine has no once-per-game offer yet).");

    public static SpecialRuleDefinition Mastermind { get; } = Marker("Skill: Mastermind",
        "Campaign skill: once per game, when targeted by an enemy, may force it to re-roll one roll "
        + "of any kind (not wired up - the engine has no once-per-game offer yet).");

    public static IReadOnlyList<SpecialRuleDefinition> All => new[]
    {
        Weakened, Maimed, Blinded, InjuryCrippled,
        NaturalTalent, Motivated, Crazed, Vigilant, Toughened,
        Prowler, Runner, Explorer,
        Motivator, Leader, Idol,
        Duellist, Fanatic, Berserker,
        Suppressor, Destroyer, Sharpshooter,
        Herbalist, Chemist, Healer,
        Tactician, Strategist, Mastermind,
        // Helper rules: never rolled on a table, granted by the skill above them for one activation.
        ExplorerSurefooted, SharpshooterRange,
    };

    private static SpecialRuleDefinition Marker(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Valence: EValence.Positive,
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
