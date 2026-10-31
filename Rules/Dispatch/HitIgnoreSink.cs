using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <inheritdoc cref="IHitIgnoreSink"/>
public sealed class HitIgnoreSink : IHitIgnoreSink
{
    private float _dice;
    private int _threshold = int.MaxValue;

    public void AddIgnoreDice(float dice, int successThreshold)
    {
        if (dice <= 0f) return;
        _dice += dice;
        // Two sources with different thresholds pool their dice at the EASIER one. Composition by
        // minimum mirrors RerollSink's band widening: a defender carrying both should not be made worse
        // off than carrying the better alone, and no corpus rule pairs two different Parry thresholds.
        _threshold = System.Math.Min(_threshold, successThreshold);
    }

    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IHitIgnoreSink> operation in operations.OfType<SinkOperation<IHitIgnoreSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary> Dice to throw. Zero when no hit-cancelling rule fired. </summary>
    public float TotalDice => _dice;

    /// <summary> What each die must beat to cancel a hit. Meaningless when <see cref="TotalDice"/> is 0. </summary>
    public int SuccessThreshold => _threshold == int.MaxValue ? 6 : _threshold;
}
