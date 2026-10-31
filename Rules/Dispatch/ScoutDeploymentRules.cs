using System;
using System.Collections.Generic;
using System.Linq;
using FDG.StageResolution.Requests;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The relational half of Scout, added by OPR 3.6.0 (#409).
    ///
    /// <para>3.5.1's Scout said only "may be deployed anywhere fully within 12" of their deployment zone".
    /// 3.6.0 narrows the range to 9" and adds a keep-out the rule never had: the unit must also be placed
    /// "over 9" away from <b>non-Scout</b> enemy units". The forward range is a number the rule definition
    /// can carry; the keep-out is a constraint against the live table, so it is built here at request time
    /// and handed to the placement request as keep-out discs - the same mechanism Ambush's Repel Ambushers
    /// already uses (#197 P22), judged by <c>PlacementDistanceRules</c>.</para>
    ///
    /// <para><b>Why discs and not <see cref="PlaceObjectsRequest{T}.MinDistanceFromEnemiesInches"/>:</b>
    /// that field applies to every live enemy model without exception, and this rule has an exception -
    /// enemy Scouts. Two opposing forward screens are supposed to be able to meet near the middle; only the
    /// normally-deployed line pushes a Scout back. A per-source disc list expresses "these enemies, not
    /// those" exactly, and costs nothing extra to validate.</para>
    ///
    /// <para>Like Ambush's discs, each radius is widened by both bases so a centre-to-centre test lands on
    /// the edge-to-edge distance the rulebook means, and discs radiate only from living, on-table models of
    /// non-allied units. A unit still in reserve projects nothing: it has no table presence, and a reserve
    /// model's position is a stale sentinel.</para>
    /// </summary>
    public static class ScoutDeploymentRules
    {
        /// <summary>
        /// How far forward of its own deployment zone a Scout may be placed. 9" in OPR 3.6.0 (was 12" in
        /// 3.5.1). Read by <see cref="CoreRuleCatalog.Scout"/> so the rule text and the zone maths can
        /// never drift apart.
        /// </summary>
        public const float ForwardRangeInches = 9f;

        /// <summary>
        /// How far a Scout must stay from non-Scout enemy units. "Over 9" away", so a placement exactly at
        /// 9" is illegal - which is what a strictly-outside disc test gives.
        /// </summary>
        public const float EnemyKeepOutInches = 9f;

        /// <summary>
        /// Keep-out discs for <paramref name="scout"/>'s forward placement: one per living, on-table model
        /// of every enemy unit that does NOT itself have Scout.
        /// </summary>
        public static IReadOnlyList<PlacementDisc> EnemyKeepOutDiscs(IUnit scout, ITableState tableState)
        {
            float scoutReach = WidestLivingBaseRadius(scout);
            List<PlacementDisc>? discs = null;

            foreach (IUnit unit in tableState.Units.Objects)
            {
                if (ReferenceEquals(unit, scout)) continue;
                if (!unit.GetIsAlive()) continue;
                if (ReserveRules.IsInReserve(unit)) continue;
                if (ITeamExtensions.AreAllied(tableState.Teams.Objects, scout.PlayerID, unit.PlayerID)) continue;

                // The exception that makes this rule need its own builder: enemy Scouts don't push.
                if (HasScout(unit)) continue;

                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    Position pos = model.Position;
                    // Default-constructed Position is the unplaced sentinel; never radiate from it.
                    if (pos.x == 0f && pos.z == 0f) continue;

                    (discs ??= new List<PlacementDisc>()).Add(new PlacementDisc(pos,
                        EnemyKeepOutInches + model.BaseShape.BoundingRadiusInches + scoutReach));
                }
            }

            return (IReadOnlyList<PlacementDisc>?)discs ?? Array.Empty<PlacementDisc>();
        }

        /// <summary>
        /// Whether a unit carries Scout. A plain name check over
        /// <see cref="IUnit.RuleDefinitions"/>, mirroring <see cref="AircraftRules.IsAircraft"/>: this runs
        /// in deployment stage code with no <see cref="RuleEvaluator"/> in scope, and name matching (rather
        /// than reference equality) means an army book's own embedded "Scout" is recognised too.
        /// </summary>
        /// <summary>
        /// Widens a set of placement radii by <see cref="ForwardRangeInches"/> for a Scout unit in a system
        /// whose Scout is a plain wider deployment, or returns <paramref name="radii"/> unchanged.
        ///
        /// <para>
        /// #414. GFSQ Core Rules v2.1.0 says the whole of Scout in one line: <i>"May be deployed anywhere
        /// fully within 9" of its deployment zone."</i> No set-aside, no deploying last, no alternating
        /// placement, no staying 9" from enemies - four clauses the other four books have and Star Quest
        /// does not. What is left is simply "this unit's deployment area is 9" bigger".
        /// </para>
        /// <para>
        /// Which makes it <b>dead in Star Quest today</b>. The rule's one hook defers the unit to the Scout
        /// pass, and a Star Quest enemy never goes through normal deployment at all - it waits in the
        /// collection and arrives with a wave, placed within 6" of a table corner. 158 units across the
        /// Star Quest books carry Scout, plus 10 player heroes, and for all of them it did nothing: the
        /// hook fired, the deferred pass never ran for a reserved unit, and the wave placed it like
        /// anything else. Applying the widening at the PLACEMENT site rather than the deployment site is
        /// what reaches a wave arrival, which is where a Star Quest unit is actually put on the table.
        /// </para>
        /// <para>
        /// The wave corner IS the unit's deployment zone in Horde (<i>"Waves are deployed within 6" of a
        /// random table corner"</i>), so "within 9" of its deployment zone" is that corner's radius plus
        /// 9". The crowding escalation above it still applies; every step widens together, so a Scout in a
        /// packed corner keeps its advantage over its non-Scout neighbours.
        /// </para>
        /// </summary>
        public static float[] WidenForScout(IGameContext gameContext, IUnit unit, float[] radii)
        {
            if (!CoreRulesProfile.For(gameContext).ScoutIsSimpleRedeploy) return radii;
            if (!HasScout(unit)) return radii;

            var widened = new float[radii.Length];
            for (int i = 0; i < radii.Length; i++) widened[i] = radii[i] + ForwardRangeInches;
            return widened;
        }

        public static bool HasScout(IUnit unit) =>
            unit.RuleDefinitions.Any(rule =>
                string.Equals(rule.Definition.Name, "Scout", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.RequestedName, "Scout", StringComparison.OrdinalIgnoreCase));

        private static float WidestLivingBaseRadius(IUnit unit)
        {
            float reach = 0f;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                reach = Math.Max(reach, model.BaseShape.BoundingRadiusInches);
            }
            return reach;
        }
    }
}
