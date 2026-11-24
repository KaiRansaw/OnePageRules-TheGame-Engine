using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Dice that roll what a script says (How to Play, Kai 2026-11-24: "Set dice: always rolls what the lesson
/// needs"). The practice battle plans the rolls of the step the player is on; each planned entry answers the
/// next roll of one or more dice, and every roll with nothing planned falls through to ordinary seeded dice.
///
/// <para><b>How an entry becomes a roll.</b> An entry is a face pattern. A roll of N dice takes the pattern's
/// first N faces, repeating it from the start if N is longer, so <c>{6, 5, 2}</c> makes 2 dice "6, 5" and
/// 5 dice "6, 5, 2, 6, 5". The pattern is written most-important-face first, so it reads the same whatever
/// the unit's size turned out to be.</para>
///
/// <para>A roll of zero dice (no hits, so no blocks) never takes an entry - otherwise one empty roll would push
/// every later planned roll onto the wrong dice. Only six-sided rolls are planned; anything else falls through.</para>
///
/// <para><b>Steering.</b> A melee or a shooting attack is several rolls whose number depends on the weapons
/// (a unit with a pistol and a blade rolls twice), so a step can't always list them one by one. <see cref="Steer"/>
/// answers by what the roll is and whose it is (<see cref="DiceRollContext"/>: hit, Defense, Morale): "my hits roll
/// high, their Defense rolls low". A steered roll does not use a planned entry; a planned entry answers
/// everything steering leaves alone.</para>
///
/// <para>Thread-safe: the screen plans on the main thread while the engine rolls on its own.</para>
/// </summary>
public sealed class ScriptedDiceRoller : IDiceRoller
{
    private readonly object _gate = new();
    private readonly Queue<int[]> _plan = new();
    private readonly IDiceRoller _fallback;

    public ScriptedDiceRoller(int? seed = null) : this(new RealisticDiceRoller(seed)) { }

    public ScriptedDiceRoller(IDiceRoller fallback)
    {
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
    }

    private Func<FDG.Rules.Definitions.ERollKind, FDG.PlayerID, int[]?>? _steer;

    /// <summary>
    /// Faces for a roll by what it is and whose it is; null leaves that roll to the plan and the ordinary dice. Set
    /// and cleared by the step being played.
    /// </summary>
    public Func<FDG.Rules.Definitions.ERollKind, FDG.PlayerID, int[]?>? Steer
    {
        get { lock (_gate) return _steer; }
        set { lock (_gate) _steer = value; }
    }

    /// <summary>How many rolls steering has answered since the game started.</summary>
    public int Steered { get; private set; }

    /// <summary>Planned rolls not used yet.</summary>
    public int Planned { get { lock (_gate) return _plan.Count; } }

    /// <summary>How many planned rolls have been used since the game started.</summary>
    public int Used { get; private set; }

    /// <summary>Adds rolls to the end of the plan. Every face must be 1-6 and every pattern non-empty.</summary>
    public void Plan(params int[][] rolls)
    {
        foreach (int[] faces in rolls)
        {
            if (faces == null || faces.Length == 0) throw new ArgumentException("A planned roll needs at least one face.");
            if (faces.Any(f => f < 1 || f > IDiceRollerExtensions.DEFAULT_SIDE_COUNT))
                throw new ArgumentOutOfRangeException(nameof(rolls), "Planned faces are 1 to 6.");
        }
        lock (_gate)
            foreach (int[] faces in rolls) _plan.Enqueue((int[])faces.Clone());
    }

    /// <summary>Drops whatever is still planned (the step ended without using it).</summary>
    public void ClearPlan() { lock (_gate) _plan.Clear(); }

    public IDiceResults Roll(int sideCount, float rollCount)
    {
        int count = (int)Math.Round(rollCount);
        int[]? pattern = null;
        if (count > 0 && sideCount == IDiceRollerExtensions.DEFAULT_SIDE_COUNT)
            lock (_gate)
            {
                if (_steer != null && DiceRollContext.Current is { } now
                    && _steer(now.Kind, now.Roller) is { Length: > 0 } steered && steered.All(f => f >= 1 && f <= 6))
                { pattern = steered; Steered++; }
                else if (_plan.Count > 0) { pattern = _plan.Dequeue(); Used++; }
            }

        if (pattern == null) return _fallback.Roll(sideCount, rollCount);

        float[] faces = new float[sideCount];
        for (int i = 0; i < count; i++) faces[pattern[i % pattern.Length] - 1]++;
        return new DiceResults(faces);
    }

    public IDiceResults RollDecisive(int sideCount) => Roll(sideCount, 1);
}
