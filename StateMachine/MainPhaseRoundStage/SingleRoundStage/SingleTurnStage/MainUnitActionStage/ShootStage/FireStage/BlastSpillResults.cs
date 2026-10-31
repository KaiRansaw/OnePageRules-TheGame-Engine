using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// The hits one blast scattered onto units that were never the target, queued for
    /// <see cref="ResolveBlastSpillStage"/> to resolve one at a time.
    ///
    /// <para>
    /// #414. Blast in the three skirmish-scale games (GF: Firefight, AoF: Skirmish, GF: Star Quest) splits
    /// its hits between the target and every enemy unit within 3" of it; <c>RollToHitStage</c> works out
    /// the distribution with <see cref="Rules.Dispatch.BlastSpreadRules"/>, keeps the target's share in the
    /// ordinary hit list, and leaves the rest here. In the two full-scale games this result is never added.
    /// </para>
    ///
    /// <para>
    /// A cursor rather than a plain list because the resolver is a looping stage: each pass consumes one
    /// share, builds a fresh combat metadata around it and runs the save/wound chain, exactly as
    /// <c>ResolveSpellDamageStage</c> does for a multi-target spell. The weapon rides along because the
    /// spilled hits keep the firing weapon's AP - they are that weapon's hits, just landing elsewhere.
    /// </para>
    /// </summary>
    public sealed class BlastSpillResults
    {
        private readonly List<(DataBinding<UnitData> Unit, float Hits)> _shares;
        private int _cursor;

        public BlastSpillResults(DataBinding<UnitData> attacker, IWeapon weapon,
            List<(DataBinding<UnitData> Unit, float Hits)> shares)
        {
            Attacker = attacker;
            Weapon = weapon;
            _shares = shares;
        }

        /// <summary>The unit that fired. The spilled hits are still its attack.</summary>
        public DataBinding<UnitData> Attacker { get; }

        /// <summary>The weapon that fired, for its AP and its rules.</summary>
        public IWeapon Weapon { get; }

        /// <summary>Units still waiting for their share of the blast.</summary>
        public bool HasMore => _cursor < _shares.Count;

        /// <summary>How many units the blast spilled onto in total, consumed or not.</summary>
        public int Count => _shares.Count;

        /// <summary>Advances to and returns the next unit and its hits; check <see cref="HasMore"/> first.</summary>
        public (DataBinding<UnitData> Unit, float Hits) ConsumeNext() => _shares[_cursor++];
    }
}
