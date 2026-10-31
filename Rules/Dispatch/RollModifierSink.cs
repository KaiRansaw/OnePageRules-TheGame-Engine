using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

public sealed class RollModifierSink : IRollModifierSink
{
    private readonly Dictionary<ERollKind, int> _deltas = new();

    // #412 Pierce ("ignores all positive Defense modifiers on the target") needs the favourable and
    // unfavourable halves separable, which a running net cannot give back - +1 cover and -1 AP net to
    // zero and the information is gone. So each side is also tallied on its own; Net stays the sum of
    // the two and every existing caller is unaffected.
    private readonly Dictionary<ERollKind, int> _positive = new();
    private readonly Dictionary<ERollKind, int> _negative = new();

    /// <summary>
    /// Write face (called by operations): adds <paramref name="delta"/> to <paramref name="roll"/>.
    /// </summary>
    public void Add(ERollKind roll, int delta)
    {
        _deltas.TryGetValue(roll, out int current);
        _deltas[roll] = current + delta;

        Dictionary<ERollKind, int> side = delta >= 0 ? _positive : _negative;
        side.TryGetValue(roll, out int sideCurrent);
        side[roll] = sideCurrent + delta;
    }

    /// <summary>
    /// Applies every roll-modifier operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IRollModifierSink> operation in operations.OfType<SinkOperation<IRollModifierSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the stage): net modifier accumulated for <paramref name="roll"/> (0 if none).
    /// </summary>
    public int Net(ERollKind roll)
    {
        return _deltas.TryGetValue(roll, out int delta) ? delta : 0;
    }

    /// <summary>
    /// The net with every FAVOURABLE modifier for <paramref name="roll"/> discarded - what Pierce
    /// leaves the defender. Deliberately not <c>Math.Min(Net, 0)</c>: that would also delete the
    /// attacker's own AP whenever a cover bonus happened to outweigh it.
    /// </summary>
    public int NetIgnoringPositive(ERollKind roll)
    {
        return _negative.TryGetValue(roll, out int delta) ? delta : 0;
    }
}