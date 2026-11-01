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

    public static IReadOnlyList<SpecialRuleDefinition> All => new[] { Hastened, Slowed };

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
