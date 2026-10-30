using FDG.Rules.Foundation;

namespace FDG.Rules.Tokens;

/// <summary>
/// Optional structured data attached to a <see cref="Token"/> when its
/// <see cref="Foundation.TokenType"/> and count alone don't carry enough information.
///
/// The common cases that motivate the type:
/// <list type="bullet">
///   <item>Spell-applied "next time it would apply" rule grants — the token says
///         "next time you shoot / are shot at, you have rule X" and needs to record
///         <i>which</i> rule. Without a payload you'd need a separate
///         <see cref="Foundation.TokenType"/> per grantable rule.</item>
///   <item>Cross-unit target-tagging tokens (e.g. <i>Unstoppable Mark</i>,
///         <i>Precision Fighting Mark</i>) that grant a rule to friendlies attacking
///         the bearer — payload carries the granted rule reference and any
///         affinity restriction.</item>
///   <item>One-shot stat modifiers granted by spells or abilities that don't have a
///         named rule equivalent (e.g. "+1 to hit shooting next attack") — payload
///         carries the roll kind and delta directly.</item>
///   <item>Targeted "only against X" buffs (e.g. <i>Raiding Drugs</i>) — payload
///         carries the granted rule plus a constrained target <see cref="UnitID"/>.</item>
/// </list>
///
/// Most tokens — Shaken, Fatigued, spell tokens, stacking markers — have no payload
/// and leave the field null. Implemented as a closed sum type via sealed nested
/// record subtypes (one per payload shape), so each concrete payload is type-checked
/// at the point of use rather than relying on stringly-typed dictionaries.
///
/// Subtypes are added on demand as the rules requiring them come up in test cases.
/// </summary>
public abstract record TokenPayload
{
    /// <summary>
    /// Records that the token grants the rule named <see cref="RuleName"/> to its
    /// bearer for the duration of <see cref="Lifetime"/>. Backs <c>Effect.AddRule</c>
    /// and <c>Effect.Aura</c> resolution — a "next time you shoot you have Blessed
    /// Ammo" buff, or an aura granting Regeneration to a whole unit, becomes a token
    /// carrying this payload rather than a bespoke token type per grantable rule.
    /// </summary>
    public sealed record RuleGrant(string RuleName, ELifetime Lifetime) : TokenPayload;

    /// <summary>
    /// Records a one-shot/duration numeric roll modifier granted to the bearer (#033 stat-modifier
    /// primitive) — a "+1 to hit / -1 to defense / -1 to morale" buff or debuff. The roll it applies to is
    /// the carrying token's <see cref="Foundation.TokenType"/> (HitRollModifier / SaveRollModifier /
    /// MoraleRollModifier), so this payload does not name the roll — keeping <c>Tokens</c> independent of
    /// <c>ERollKind</c> (which lives in <c>Definitions</c>).
    ///
    /// <para><paramref name="Source"/> (#407) is what granted the buff - a rule, skill, item or random
    /// event name - so the roll stages can say "Vengeful Strike +1" instead of a bare "buff +1". Kai's
    /// QA complaint was exactly this: with ~120 skills built there was no way to tell from play whether
    /// one had fired. Optional, and null in two cases: a save written before this field existed, and a
    /// grant from a call site that has not been given its name yet (those still read "buff").</para>
    ///
    /// <para>Note this widens the token MERGE key, since <c>TokenContainer.AddToken</c> matches on payload
    /// equality: two +1 hit buffs from different sources now stay separate entries instead of collapsing
    /// into one count. That is deliberate and matches the reasoning already recorded there for
    /// <see cref="RuleGrant"/> — distinct things must stay distinct or the provenance is lost. The net
    /// delta <c>GrantedRollModifiers</c> computes is unchanged either way, since it sums
    /// <c>Delta * Count</c> across every matching token.</para>
    /// </summary>
    public sealed record StatModifier(int Delta, string? Source = null) : TokenPayload;

    /// <summary>
    /// #032 Limited: names the once-per-game weapon a <see cref="Foundation.TokenType.LimitedSpent"/> token
    /// records as fired. Lets a single model track two different Limited weapons independently (the spent-state
    /// lives on the model, keyed by weapon name since <c>Weapon</c> has no ID).
    /// </summary>
    public sealed record WeaponName(string Name) : TokenPayload;

    /// <summary>
    /// #197 P12: a FRACTIONAL quantity for a token whose value cannot be an integer. The engine's only
    /// user is the Regenerative Strength marker, whose value is "wounds ignored" — a roll-derived
    /// quantity that the probabilistic roller reports as a float (see
    /// <see cref="Foundation.TokenType.REGENERATIVE_STRENGTH_MARKER_ID"/>).
    ///
    /// <para>The fraction lives here rather than on <see cref="Token.Count"/> deliberately
    /// (owner-signed 2026-07-31): a float <c>Count</c> would put non-integers in front of spell-token
    /// pools, Limited-spent flags and every cost gate, all of which must stay integral. Confining it to
    /// a payload means exactly one reader — <see cref="ITokenContainer.GetTokenMagnitude"/> — ever sees a
    /// fraction, and the rest of the token system is untouched.</para>
    ///
    /// <para>Unlike every other payload, magnitude tokens MERGE rather than stacking as distinct
    /// entries: <see cref="TokenContainer.AddToken"/> special-cases them and sums the values, because
    /// two markers worth 0.33 each are one marker worth 0.67, not two unrelated tokens. That is the only
    /// place payload equality is deliberately not the stacking key.</para>
    /// </summary>
    public sealed record Magnitude(float Value) : TokenPayload;
}
