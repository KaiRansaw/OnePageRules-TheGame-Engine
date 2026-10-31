using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;

namespace FDG.Rules.Dispatch;

/// <inheritdoc cref="IExtraAttackRollSink"/>
public sealed class ExtraAttackRollSink : IExtraAttackRollSink
{
    private float _dice;

    public void AddAttackDice(float count)
    {
        if (count <= 0f) return;
        _dice += count;
    }

    /// <summary>
    /// Applies every extra-attack-dice operation in <paramref name="operations"/> to this sink.
    /// </summary>
    public void ApplyFrom(IEnumerable<RuleOperation> operations)
    {
        foreach (SinkOperation<IExtraAttackRollSink> operation
                 in operations.OfType<SinkOperation<IExtraAttackRollSink>>())
        {
            operation.ApplyTo(this);
        }
    }

    /// <summary>
    /// Read face (called by the hit-roll stage): how many extra attack dice to throw. Zero when no
    /// extra-attack rule fired. Fractional under the probabilistic roller, exactly as the injection
    /// sink's total is.
    /// </summary>
    public float TotalAttackDice => _dice;
}
