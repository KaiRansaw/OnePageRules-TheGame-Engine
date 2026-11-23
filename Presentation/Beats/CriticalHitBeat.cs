using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>One row of a critical hit table: "4-5", "Blade Storm", what it does.</summary>
    [Serializable]
    public sealed record CriticalRow(string Range, string Name, string Text);

    /// <summary>
    /// A critical hit (GF: Firefight / AoF: Skirmish Advanced Rulebooks p45), with the whole table it was rolled on so
    /// the front end can show it (Kai, 2026-11-23: "the player needs to know exactly what they're rolling for"):
    /// the roll's panel, and the attack's block in the battle report.
    /// </summary>
    [Serializable]
    public sealed class CriticalHitBeat : PresentationBeat
    {
        public UnitID? AttackerUnit { get; }
        public UnitID? TargetUnit { get; }
        public string AttackerName { get; }
        public string TargetName { get; }
        public string WeaponName { get; }
        /// <summary>"Bladed", "Generic Ranged"...</summary>
        public string TableName { get; }
        public int Die { get; }
        public IReadOnlyList<CriticalRow> Rows { get; }
        /// <summary>Index into <see cref="Rows"/> of the result rolled.</summary>
        public int Picked { get; }
        /// <summary>What the result does here, in plain words ("1 hit at AP(+4), and Raptors take 2 hits").</summary>
        public string Result { get; }

        public CriticalHitBeat(UnitID? attackerUnit, UnitID? targetUnit, string attackerName, string targetName,
            string weaponName, string tableName, int die, IReadOnlyList<CriticalRow> rows, int picked, string result)
        {
            AttackerUnit = attackerUnit;
            TargetUnit = targetUnit;
            AttackerName = attackerName;
            TargetName = targetName;
            WeaponName = weaponName;
            TableName = tableName;
            Die = die;
            Rows = rows ?? Array.Empty<CriticalRow>();
            Picked = picked;
            Result = result;
        }

        /// <summary>Held like a notice: a short hitch to register it, then play goes on while the panel stays up.</summary>
        public override bool Held => true;
        public override TimeSpan HoldLeadIn => TimeSpan.FromMilliseconds(700);
        public override TimeSpan NominalDuration => TimeSpan.FromMilliseconds(4500);

        public override string? Text => $"Critical Hit - {TableName}: {Die} = {(Picked >= 0 && Picked < Rows.Count ? Rows[Picked].Name : "")}";
    }
}
