using System.Text.Json.Serialization;

namespace FDG.Rules.Definitions;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(D3), "d3")]
[JsonDerivedType(typeof(D6), "d6")]

public abstract record DiceExpression
{
    /// <summary> Number of faces on the die — used to roll the expression. </summary>
    public abstract int Sides { get; }

    /// <summary>
    /// How many of that die to throw — the N in "2D3" (#412, the Enhanced Wolfborn / Enhanced Rapid
    /// Blink family, which doubles a base rule's D3 reposition). Defaults to 1, so every expression
    /// authored before this existed still means one die and serialises without the field.
    /// </summary>
    [JsonPropertyName("count")]
    public int Count { get; init; } = 1;

    public sealed record D3 : DiceExpression
    {
        public override int Sides => 3;
    }

    public sealed record D6 : DiceExpression
    {
        public override int Sides => 6;
    }
}
