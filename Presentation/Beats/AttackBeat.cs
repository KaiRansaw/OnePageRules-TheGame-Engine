using System;
using System.Collections.Generic;

namespace FDG.Presentation.Beats
{
    /// <summary>
    /// A unit attacking another — shots in flight (ranged) or a clash (melee). Carries the
    /// attacker model positions (<see cref="From"/>) and the target model positions (<see cref="To"/>)
    /// so the front-end can animate between them; it renders differently per <see cref="IsMelee"/>
    /// (tracers vs. a clash). <see cref="From"/> holds one position per firing weapon, at its actual
    /// model — a volley fires all of them simultaneously, and <see cref="VolleyCount"/> volleys play
    /// one after another (so five A2 rifles show five together, then five more). <see cref="ArmorPenetration"/>
    /// scales their size. Emitted before the hit dice; the dice then show the resolution.
    /// </summary>
    [Serializable]
    public sealed class AttackBeat : PresentationBeat
    {
        public bool IsMelee { get; }

        /// <summary>One position per firing weapon, at its model — a volley fires all of these at once.</summary>
        public IReadOnlyList<Position> From { get; }
        public IReadOnlyList<Position> To { get; }
        public int VolleyCount { get; }       // volleys fired one after another (Attacks per weapon)
        public int ArmorPenetration { get; }

        /// <summary>
        /// #239: the firing weapon's effect-set key — an opaque identifier from the army data that
        /// selects the projectile/swing visual and sounds. Null means the front-end's global default.
        /// </summary>
        public string? WeaponEffect { get; }

        /// <summary>
        /// #239: natural to-hit successes and attacks rolled, so front-ends can land only the shots
        /// that actually hit and overshoot the misses. Floats — Realistic-mode dice are fractional.
        /// <see cref="AttackCount"/> &lt;= 0 means unknown: render the legacy all-shots-connect way.
        /// </summary>
        public float HitCount { get; }

        /// <inheritdoc cref="HitCount"/>
        public float AttackCount { get; }

        /// <summary>
        /// The attacking and attacked units (Kai, 2026-11-05: hovering a battle-report card should draw a
        /// line between the units involved and highlight them). Carried by id rather than worked out from
        /// <see cref="From"/>/<see cref="To"/>, because "the unit with a model nearest this point" is wrong
        /// exactly where it matters - two units locked in melee, or a target standing beside a friend.
        /// Null on a beat from an older save or host that predates the field: the front end falls back to
        /// the positional guess.
        /// </summary>
        public UnitID? AttackerUnit { get; }

        /// <inheritdoc cref="AttackerUnit"/>
        public UnitID? TargetUnit { get; }

        /// <summary>
        /// The weapon this beat fires and how many models fire it - "10x Juggler Bombs". The battle report
        /// used to list every ranged weapon the unit owns, so a unit with one weapon in range read as having
        /// fired everything (found playing a full AoF game, 2026-11-06). Null from an older host or save.
        /// </summary>
        public string? WeaponName { get; }

        /// <inheritdoc cref="WeaponName"/>
        public int WeaponCount { get; }

        public AttackBeat(bool isMelee, IReadOnlyList<Position> from, IReadOnlyList<Position> to,
            int volleyCount, int armorPenetration,
            string? weaponEffect = null, float hitCount = 0f, float attackCount = 0f,
            UnitID? attackerUnit = null, UnitID? targetUnit = null,
            string? weaponName = null, int weaponCount = 0)
        {
            WeaponName = weaponName;
            WeaponCount = weaponCount;
            AttackerUnit = attackerUnit;
            TargetUnit = targetUnit;
            IsMelee = isMelee;
            From = from;
            To = to;
            VolleyCount = volleyCount;
            ArmorPenetration = armorPenetration;
            WeaponEffect = weaponEffect;
            HitCount = hitCount;
            AttackCount = attackCount;
        }

        public override TimeSpan NominalDuration => PresentationDurations.ForVolleys(VolleyCount);

        // #238: held with a zero lead-in, so the presenter emits the attack and immediately proceeds
        // to the to-hit DiceRolledBeat that always follows it — front-ends animate the shots/swings
        // WHILE the dice tumble instead of after. The attack's duration (ForVolleys caps at 1600ms)
        // always fits inside the dice beat's envelope, so the dice pacing covers both.
        public override bool Held => true;
        public override TimeSpan HoldLeadIn => TimeSpan.Zero;

        // Purely visual — the surrounding hit/wound logs already narrate the outcome.
        public override string? Text => null;
    }
}
