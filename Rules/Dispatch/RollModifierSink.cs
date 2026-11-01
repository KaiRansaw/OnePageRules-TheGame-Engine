using FDG;
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

    // #422: every UNFAVOURABLE modifier with the unit whose rule produced it, for Unstoppable's
    // "which are not from this model's special rules". Kept as a list rather than folded per source
    // because the question is only ever asked once per roll, and the list is a handful of entries.
    private readonly List<(ERollKind Roll, int Delta, UnitID? Source)> _negativeSources = new();

    /// <summary>
    /// Write face (called by operations): adds <paramref name="delta"/> to <paramref name="roll"/>.
    /// </summary>
    public void Add(ERollKind roll, int delta) => Add(roll, delta, source: null);

    /// <summary>
    /// #422: the tagged write. Unstoppable asks a question neither the net nor the sign split can answer -
    /// "ignores all negative modifiers <b>which are not from this model's special rules</b>" - so each
    /// negative is also kept with the unit whose rule produced it. Positives are not kept individually:
    /// nothing in the corpus asks where a FAVOURABLE modifier came from, and the running tallies above
    /// already answer everything that does.
    /// </summary>
    public void Add(ERollKind roll, int delta, UnitID? source)
    {
        _deltas.TryGetValue(roll, out int current);
        _deltas[roll] = current + delta;

        Dictionary<ERollKind, int> side = delta >= 0 ? _positive : _negative;
        side.TryGetValue(roll, out int sideCurrent);
        side[roll] = sideCurrent + delta;

        if (delta < 0) _negativeSources.Add((roll, delta, source));
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

    /// <summary>
    /// #422 Unstoppable: the net with every unfavourable modifier discarded EXCEPT the ones
    /// <paramref name="bearer"/>'s own rules contributed - "ignores all negative modifiers which are not
    /// from this model's special rules".
    ///
    /// <para>
    /// Deliberately not "drop every negative": a model carrying both Unstoppable and a self-inflicted
    /// penalty (the HitPenaltyWhenShooting family) keeps its own penalty, which is what the clause says
    /// and the reason the clause is there at all. Favourable modifiers are untouched.
    /// </para>
    /// <para>
    /// An untagged negative counts as foreign and is dropped. That is the conservative direction for a
    /// rule that is a buff: the attacker's own rules always arrive tagged (the effect reads them off the
    /// bearer), so an untagged entry is a synthetic or test-authored op rather than one of this model's.
    /// </para>
    /// </summary>
    public int NetIgnoringForeignNegatives(ERollKind roll, UnitID bearer)
    {
        int net = _positive.TryGetValue(roll, out int positive) ? positive : 0;
        foreach ((ERollKind entryRoll, int delta, UnitID? source) in _negativeSources)
        {
            if (entryRoll == roll && source is { } owner && owner.Equals(bearer)) net += delta;
        }

        return net;
    }
}