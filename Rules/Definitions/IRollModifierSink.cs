using FDG;

namespace FDG.Rules.Definitions;

/// <summary>
/// The write face of a roll-modifier accumulator. An operation that adjusts a roll
/// (<see cref="RuleOperation.ApplyRollModifier"/>) applies itself here via
/// <see cref="SinkOperation{TSink}.ApplyTo"/>, so a stage folds roll modifiers without ever
/// inspecting an operation's concrete type. The read face (the net total) lives on the concrete sink.
/// </summary>

public interface IRollModifierSink
{
    void Add(ERollKind roll, int delta);

    /// <summary>
    /// #422: the same write, tagged with the unit whose rule produced it. Needed by Unstoppable, whose
    /// text is "ignores all negative modifiers <b>which are not from this model's special rules</b>" - a
    /// net cannot answer that, and neither can the positive/negative split Pierce added, because the
    /// question is about the modifier's SOURCE rather than its sign.
    ///
    /// <para>
    /// A default implementation forwarding to the untagged <see cref="Add(ERollKind, int)"/> so every
    /// existing implementer keeps working unchanged: a sink that does not care about provenance simply
    /// drops the tag.
    /// </para>
    /// </summary>
    void Add(ERollKind roll, int delta, UnitID? source) => Add(roll, delta);
}