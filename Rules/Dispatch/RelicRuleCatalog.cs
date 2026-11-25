using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch;

/// <summary>
/// Relic Wargear / Magic Items (Advanced Rulebooks 3.6.0: GF p36-37, GFF p33-34, AoF p36-37, AoFS p33-34): the
/// items that are plain standing rules, granted by <c>FDG.RelicWargear</c> to the Hero's unit (a rule-grant token)
/// or to the Hero's weapons. Registered through <see cref="CoreRuleCatalog.All"/> so a grant can name them; the
/// "Relic: " prefix keeps them apart from any army book's own rule. The items that need a choice or a die at a
/// moment (Luckstone, the scrolls, the shields) live in <c>FDG.RelicWargear</c> itself.
/// </summary>
public static class RelicRuleCatalog
{
    public const string SwiftnessName = "Relic: Banner of Swiftness";
    public const string RampagerName = "Relic: Rampager's Standard";
    public const string EternalFlameName = "Relic: Banner of Eternal Flame";
    public const string DisciplineName = "Relic: Standard of Discipline";
    public const string TricksterName = "Relic: Trickster's Shard";

    /// <summary>"+2\" when using Advance actions."</summary>
    public static SpecialRuleDefinition Swiftness { get; } = new(SwiftnessName,
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Advance),
                new Effect.MovementBonus(EActionType.Advance, DistanceInches: 2f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Banner of Swiftness (relic): +2\" when using Advance actions.");

    /// <summary>"+4\" when using Charge actions."</summary>
    public static SpecialRuleDefinition Rampager { get; } = new(RampagerName,
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.ActionTypeIs(EActionType.Charge),
                new Effect.MovementBonus(EActionType.Charge, DistanceInches: 4f),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Rampager's Standard (relic): +4\" when using Charge actions.");

    /// <summary>"May ignore all terrain effects" - read as Flying's terrain clause: Difficult and Dangerous terrain
    /// don't slow or test the unit (house rules C).</summary>
    public static SpecialRuleDefinition EternalFlame { get; } = new(EternalFlameName,
        new[]
        {
            new HookEntry(EHookID.Movement_OnMoveThroughTerrain,
                new Condition.Always(),
                new Effect.IgnoreTerrainEffects(ETerrainIgnoreScope.AllTerrain),
                ELifetime.ThisActivation),
        },
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Banner of Eternal Flame (relic): ignores the effects of Difficult and Dangerous terrain.");

    /// <summary>"Pass Morale Tests on rolls of 2+" - read by the morale test (a marker rule).</summary>
    public static SpecialRuleDefinition Discipline { get; } = new(DisciplineName,
        Array.Empty<HookEntry>(),
        Array.Empty<ActivatedAbility>(),
        Valence: EValence.Positive,
        Description: "Standard of Discipline (relic): passes Morale Tests on rolls of 2+.");

    /// <summary>"The Hero's weapons ignore Regeneration." Attached to each of the Hero's weapons.</summary>
    public static SpecialRuleDefinition Trickster { get; } = new(TricksterName,
        new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete,
                new Condition.Always(),
                new Effect.IgnoreRule("Regeneration"),
                ELifetime.ThisAttack),
        },
        Array.Empty<ActivatedAbility>(),
        ERuleScope.Weapon,
        Valence: EValence.Positive,
        Description: "Trickster's Shard (relic): this weapon ignores Regeneration.");

    public static IReadOnlyList<SpecialRuleDefinition> All => new[] { Swiftness, Rampager, EternalFlame, Discipline, Trickster };
}
