using FDG.Stages;
using FDG.Utilities;

namespace FDG.Ai
{
    /// <summary>
    /// #419 - the geometry and damage arithmetic every AI brain needs: how far apart two units are, whether
    /// one can see the other, how far its guns reach, and roughly how many wounds an attack would land.
    ///
    /// <para>
    /// Extracted from <c>StarQuestAiBrain</c>, which had them as public statics because it was the only
    /// brain that needed them. The Solo &amp; Co-Op bot needs the same five, and two copies of
    /// <see cref="ExpectedWounds"/> would be two things to keep in step with the hit/save pipeline. The
    /// Quest brain keeps its own names as thin forwards, so nothing that already called them had to change.
    /// </para>
    /// <para>
    /// All of it is an ESTIMATE, on purpose. <see cref="ExpectedWounds"/> is the average of a simplified
    /// roll - quality to hit, defence plus AP to block, no re-rolls, no Blast, no Deadly, no rule hooks -
    /// because its only job is to answer "would charging hurt more than shooting?" An AI that asked the
    /// real pipeline for the answer would have to resolve an attack it has not committed to.
    /// </para>
    /// </summary>
    public static class AiCombatMath
    {
        public static IUnit? FindUnit(ITableState table, UnitID id) =>
            table.Units.Objects.FirstOrDefault(u => u.ID.Equals(id));

        /// <summary>Closest approach between two units' living models, ignoring height.</summary>
        public static float Distance(IUnit a, IUnit b) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(a, b, out _, out _, includeVertical: false);

        /// <summary>True when any living model of <paramref name="a"/> can see any living model of <paramref name="b"/>.</summary>
        public static bool Sees(IUnit a, IUnit b, List<ITerrain> terrain) =>
            a.Models.Where(m => m.GetIsAlive()).Any(ma => b.Models.Where(m => m.GetIsAlive())
                .Any(mb => LineOfSightUtilities.HasLineOfSight(ma.Position, mb.Position, terrain)));

        /// <summary>The longest ranged weapon on any living model, or 0 for a melee-only unit.</summary>
        public static float MaxRange(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive()).SelectMany(m => m.Weapons)
                .Select(w => w.RangeInches).DefaultIfEmpty(0f).Max();

        /// <summary>
        /// Average wounds the unit's weapons of one kind would deal to the target (hit, then fail to block).
        /// </summary>
        /// <param name="melee">True to total the melee weapons, false the ranged ones.</param>
        /// <param name="reachInches">Ranged only: how far away the target will be; weapons that cannot
        /// reach it are skipped.</param>
        public static float ExpectedWounds(IUnit attacker, IUnit target, bool melee, float reachInches = 0f)
        {
            int quality = attacker is UnitData ua ? ua.Quality : 4;
            int defense = target is UnitData ut ? ut.Defense : 4;
            float pHit = Math.Clamp((7f - quality) / 6f, 1f / 6f, 5f / 6f);
            float total = 0f;
            foreach (IModel model in attacker.Models.Where(m => m.GetIsAlive()))
                foreach (Weapon w in model.Weapons)
                {
                    bool isMelee = w.RangeInches <= 0f;
                    if (isMelee != melee || (!melee && w.RangeInches < reachInches)) continue;
                    int needed = defense + w.ArmorPenetration;
                    float pBlock = needed > 6 ? 1f / 6f : Math.Clamp((7f - needed) / 6f, 1f / 6f, 5f / 6f);
                    total += w.Attacks * pHit * (1f - pBlock);
                }
            return total;
        }

        /// <summary>How far the unit's nearest living model's EDGE is from a point, floored at 0.</summary>
        public static float DistanceToPoint(IUnit unit, Position p) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => MathF.Max(0f, Position.GetDistance2D(m.Position, p) - m.BaseRadiusInches))
                .DefaultIfEmpty(float.MaxValue).Min();

        /// <summary>Living, on-table units not allied to <paramref name="me"/>.</summary>
        public static List<IUnit> Enemies(ITableState table, PlayerID me) =>
            table.Units.Objects.Where(u => !ITeamExtensions.AreAllied(table.Teams.Objects, me, u.PlayerID)
                && u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();
    }
}
