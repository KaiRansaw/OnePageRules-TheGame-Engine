using FDG.Rules.Foundation;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - who an AI unit shoots at and who it charges, from <b>Solo &amp; Co-Op Rules v3.6.0</b>.
    ///
    /// <para>
    /// <b>Shooting:</b> "AI units always shoot at the <b>closest</b> valid target, <b>prioritizing units
    /// that haven't activated yet</b>. If the closest target is in Cover but there is another valid target
    /// in the open, the AI must <b>prioritize the target in the open</b>."
    /// </para>
    /// <para>
    /// <b>Melee:</b> "AI units always charge the <b>closest</b> valid target, <b>prioritizing units that
    /// haven't activated yet</b>."
    /// </para>
    /// <para>
    /// <b>What the engine did instead.</b> The old resolver ordered candidates by cover, then by how many
    /// of its own models could shoot - and <b>distance was not in the ordering at all</b>. So it would
    /// happily shoot a unit at the far end of the table over one at 6", as long as the far one was in the
    /// open or more guns could see it. The "not yet activated" half was missing entirely in both shooting
    /// and melee, which is the clause that makes the AI feel like it is playing the clock: hitting a unit
    /// that has not moved yet takes a whole activation off the enemy, while hitting one that has already
    /// gone costs them nothing until next round.
    /// </para>
    /// <para>
    /// <b>How the three keys are ordered, and why.</b> The book gives shooting three instructions and does
    /// not say which wins when they disagree. Read in order of how the book states them: cover is an
    /// explicit override of "closest" ("if the closest target is in Cover but there is another in the open
    /// ... prioritize the target in the open"), so it sorts first; "prioritizing units that haven't
    /// activated yet" reads as a tie-break on an otherwise equal choice, so it sorts after; and distance
    /// decides the rest. For melee the book gives only closest plus the activation tie-break, so that is
    /// all this applies. Where the book leaves a genuine tie, "Multiple Choices" says to roll for it - the
    /// callers are deterministic here instead, because a dice roll per target pick would be a roll the
    /// engine has to log and a player would never see the reason for.
    /// </para>
    /// </summary>
    public static class SoloAiTargeting
    {
        /// <summary>
        /// Whether this unit has already activated this round, which the book asks the AI to deprioritise.
        /// </summary>
        public static bool HasActivated(IUnit unit) =>
            unit.Tokens.HasToken(TokenType.ActivatedThisRound);

        /// <summary>
        /// The shooting pick: in the open before in Cover, then not-yet-activated before activated, then
        /// closest. <paramref name="candidates"/> must already be filtered to what this unit can legally
        /// shoot - this decides only the order.
        /// </summary>
        public static IUnit? PickShootTarget(ITableState table, IUnit shooter, IReadOnlyList<IUnit> candidates) =>
            candidates
                .OrderBy(e => InCover(table, e) ? 1 : 0)
                .ThenBy(e => HasActivated(e) ? 1 : 0)
                .ThenBy(e => AiCombatMath.Distance(shooter, e))
                .FirstOrDefault();

        /// <summary>
        /// The melee pick: not-yet-activated before activated, then closest. Cover does not appear - the
        /// book's cover clause is written for shooting only, and Cover does not apply in melee anyway.
        /// </summary>
        public static IUnit? PickChargeTarget(IUnit charger, IReadOnlyList<IUnit> candidates) =>
            candidates
                .OrderBy(e => HasActivated(e) ? 1 : 0)
                .ThenBy(e => AiCombatMath.Distance(charger, e))
                .FirstOrDefault();

        /// <summary>
        /// Whether the unit is in Cover terrain. Deliberately the unit's own footing rather than a
        /// line-of-sight test from the shooter: the book's phrase is "a valid target in the open", a
        /// property of where the target stands, and the engine's real cover calculation happens later in
        /// the attack pipeline with the firing models known. An AI ordering targets has no firing model yet.
        /// </summary>
        public static bool InCover(ITableState table, IUnit unit)
        {
            foreach (ITerrain piece in table.Terrain.Objects)
            {
                if (!piece.TerrainType.HasFlag(ETerrainType.Cover)) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (piece.Shape.IsPointWithinZone(new Float2(model.Position.x, model.Position.z)))
                        return true;
                }
            }

            return false;
        }
    }
}
