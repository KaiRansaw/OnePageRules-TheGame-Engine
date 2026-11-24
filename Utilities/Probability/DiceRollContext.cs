using System;
using FDG;
using FDG.Rules.Definitions;

/// <summary>
/// What the roll being made right now is for, and whose it is - for dice that care (How to Play's set dice,
/// <see cref="ScriptedDiceRoller"/>). Set around the core rolls only: the hit roll (the attacker's), the Defense
/// roll (the defender's) and a Morale Test's die (the testing unit's). Every other roll has no context.
///
/// <para>Thread-static: each of those rolls is one synchronous call inside the scope, on the engine's thread.</para>
/// </summary>
public static class DiceRollContext
{
    [ThreadStatic] private static ERollKind? _kind;
    [ThreadStatic] private static PlayerID? _roller;

    /// <summary>The roll in progress, or null when nothing set one.</summary>
    public static (ERollKind Kind, PlayerID Roller)? Current =>
        _kind is { } k && _roller is { } r ? (k, r) : null;

    /// <summary>Marks the rolls inside the returned scope as <paramref name="kind"/>, made by <paramref name="roller"/>.</summary>
    public static Scope Begin(ERollKind kind, PlayerID roller)
    {
        var scope = new Scope(_kind, _roller);
        _kind = kind;
        _roller = roller;
        return scope;
    }

    /// <summary>Runs <paramref name="roll"/> with the context set, and clears it afterwards whatever happens.</summary>
    public static T Within<T>(ERollKind kind, PlayerID roller, Func<T> roll)
    {
        using (Begin(kind, roller)) return roll();
    }

    public readonly struct Scope : IDisposable
    {
        private readonly ERollKind? _kind;
        private readonly PlayerID? _roller;
        internal Scope(ERollKind? kind, PlayerID? roller) { _kind = kind; _roller = roller; }
        public void Dispose()
        {
            DiceRollContext._kind = _kind;
            DiceRollContext._roller = _roller;
        }
    }
}
