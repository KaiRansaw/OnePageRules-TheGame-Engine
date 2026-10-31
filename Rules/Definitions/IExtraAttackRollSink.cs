namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of an extra-ATTACK-DICE accumulator (#412 Psychotic / Crazed).
/// <para>
/// Distinct from <see cref="IHitInjectionSink"/>, and the distinction is the whole point. A rule that
/// says "unmodified 6s to hit deal 1 extra hit" (Furious, Relentless, Surge) hands the attacker a hit
/// outright and goes through the injection sink. A rule that says "unmodified 6s to hit let you roll
/// +1 attack" (Psychotic, Crazed, Savage Frenzy) hands the attacker a DIE, which then has to beat the
/// same hit threshold the volley did - worth roughly half as much at Quality 4, and nothing at all on
/// a bad throw. Routing the second family through the injection sink would have silently doubled
/// every one of those rules.
/// </para>
/// <para>
/// The dice total accumulates here; the hit-roll stage reads it back, throws that many dice at the
/// attack's own (already clamped) hit threshold, and appends the successes. The book's "this rule
/// does not apply to newly generated attacks" is honoured by construction: the stage throws the extra
/// dice ONCE, outside the rule-evaluation pass, so a 6 on an extra die cannot cascade.
/// </para>
/// </summary>
public interface IExtraAttackRollSink
{
    void AddAttackDice(float count);
}
