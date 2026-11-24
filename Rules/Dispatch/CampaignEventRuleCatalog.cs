using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// #432 - the two rules the campaign random event table needs and nothing else in the game has: a
/// movement buff and its exact mirror, both reading "+/-1\" on Advance and +/-2\" on Rush/Charge".
///
/// <para>
/// Three of the 36 events use that shape - Sticky Foliage (26), Psychic Fog (44) and Haste Stims (51) -
/// and it is one rule, not two: the book changes both numbers together. Prowler (+1 Advance) and Runner
/// (+2 Rush/Charge) between them cover the positive half, but granting a unit two unrelated campaign
/// SKILLS to express one weather event would show two wrong things on its card, and there is no
/// negative pair at all.
/// </para>
///
/// <para>
/// Registered through <see cref="CoreRuleCatalog.All"/> like <see cref="Campaign36RuleCatalog"/>, so a
/// round-start grant can name them. Named with the "Campaign event: " prefix so they cannot collide
/// with an army book's own rule, which is the same guard the injury and talent traits use.
/// </para>
/// </summary>
public static class CampaignEventRuleCatalog
{
    public const string HastenedName = "Campaign event: Hastened";
    public const string SlowedName = "Campaign event: Slowed";

    public static SpecialRuleDefinition Hastened { get; } = Movement(HastenedName, +1f, +2f,
        EValence.Positive, "Campaign event: moves +1\" on Advance and +2\" on Rush/Charge actions.");

    public static SpecialRuleDefinition Slowed { get; } = Movement(SlowedName, -1f, -2f,
        EValence.Negative, "Campaign event: moves -1\" on Advance and -2\" on Rush/Charge actions.");

    /// <summary>Twists &amp; Gambits, gambit A3's power (Advanced Rulebooks 3.6.0: GF p45, GFF p42, AoF p45, AoFS p42):
    /// "One friendly unit's weapons ignore the Stealth and Regeneration rule until the end of the round." Granted for
    /// the round by <c>FDG.Missions.TwistsGambits</c>; shooting and melee both run through these hooks.</summary>
    public const string AggressiveTakeoverName = "Gambit: Aggressive Takeover";

    public static SpecialRuleDefinition AggressiveTakeover { get; } = new(AggressiveTakeoverName,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollModifier,
                new Condition.Always(),
                new Effect.IgnoreRule("Stealth"),
                ELifetime.ThisAttack),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Gambit: this unit's weapons ignore the Stealth and Regeneration rules until the end of the round.");

    /// <summary>Political Intrigue (GF p49, AoF p49): the politicians' round-long effects, granted by <c>FDG.PoliticalIntrigue</c>.
    /// Trader: "the target friendly unit gets AP(+1)" (also the one-attack mark on an enemy unit). Warden: "enemy units get
    /// AP(-1), to a min. of AP(0), when attacking the target friendly unit". Deceiver: a marker the Dangerous-terrain tests
    /// read (Strider is granted beside it for Difficult terrain).</summary>
    public const string TraderName = "Politician: Trader";
    public const string WardenName = "Politician: Warden";
    public const string DeceiverName = "Politician: Deceiver";

    public static SpecialRuleDefinition Trader { get; } = new(TraderName,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.Always(),
                new Effect.RollModifier(ERollKind.Save, Delta: -1),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Politician (Trader): this unit's attacks get AP(+1).");

    public static SpecialRuleDefinition Warden { get; } = new(WardenName,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                // Granted to the whole unit, so every model has it (the lint's defensive-rule gate).
                new Condition.AllModelsHaveThisRule(),
                new Effect.ReduceArmorPenetration(1),
                ELifetime.ThisAttack,
                ERuleSeat.Subject),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Politician (Warden): enemy units get AP(-1), to a min. of AP(0), when attacking this unit.");

    public static SpecialRuleDefinition Deceiver { get; } = new(DeceiverName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Politician (Deceiver): this unit ignores Difficult and Dangerous terrain until the end of the round.");

    public static IReadOnlyList<SpecialRuleDefinition> All => new[] { Hastened, Slowed, AggressiveTakeover, Trader, Warden, Deceiver };

    /// <summary>
    /// The shared shape: one hook per action type, because <see cref="Effect.MovementBonus"/> is per
    /// action and the three actions take different numbers.
    /// <para>
    /// <see cref="ELifetime.ThisActivation"/> on the hooks, not ThisRound: the lifetime here is how long
    /// the MOVEMENT BONUS lasts once the hook fires, and a move bonus belongs to the move that declared
    /// it. How long the unit keeps the RULE is decided by the grant that handed it over - a round-end
    /// clear trigger, set where the event grants it.
    /// </para>
    /// </summary>
    private static SpecialRuleDefinition Movement(string name, float advance, float rushAndCharge,
        EValence valence, string description) =>
        new(name,
            new[]
            {
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.ActionTypeIs(EActionType.Advance),
                    new Effect.MovementBonus(EActionType.Advance, DistanceInches: advance),
                    ELifetime.ThisActivation),
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.ActionTypeIs(EActionType.Rush),
                    new Effect.MovementBonus(EActionType.Rush, DistanceInches: rushAndCharge),
                    ELifetime.ThisActivation),
                new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                    new Condition.ActionTypeIs(EActionType.Charge),
                    new Effect.MovementBonus(EActionType.Charge, DistanceInches: rushAndCharge),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: valence,
            Description: description);
}
