namespace FDG.Rules.Definitions;

/// <summary>
/// #412: the defender's hit-cancelling dice pool. Parry(X) - "when this unit takes hits in melee, roll
/// X dice per model with this rule in it, up to as many dice as hits taken; for each 6+ one hit is
/// ignored" - is the first rule that removes hits that have already landed, rather than modifying the
/// save they force.
/// <para>
/// A pool rather than a flat reduction, because the cancellation is itself rolled: the sink carries how
/// many dice to throw and the threshold each must beat, and <c>RollToHitStage</c> throws them once the
/// final hit count is known (after extra-hit injection and Blast multiplication, so Parry answers the
/// volley that actually arrived). The running total lives on the concrete sink, as the other sinks do.
/// </para>
/// </summary>
public interface IHitIgnoreSink
{
    /// <summary> Adds <paramref name="dice"/> dice to the pool, each cancelling a hit on
    /// <paramref name="successThreshold"/> or more. </summary>
    void AddIgnoreDice(float dice, int successThreshold);
}
