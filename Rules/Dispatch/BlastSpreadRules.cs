using FDG.Utilities;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Blast(X) in the three skirmish-scale games, where the hits spread off the target onto whatever is
    /// standing near it.
    ///
    /// <para>
    /// #414. The two scales word Blast differently, and the engine only had the big games' version:
    /// </para>
    /// <list type="table">
    /// <listheader><term>Book</term><description>Blast(X)</description></listheader>
    /// <item><term>GF / AoF 3.6.0</term><description>"each hit is multiplied by X, <b>where X is up to as
    /// many hits as models in the target unit</b>." The hits all land on the target.</description></item>
    /// <item><term>GFF / AoFS 3.6.0, GFSQ 2.1.0</term><description>"each hit is multiplied by X. <b>Hits
    /// must be split evenly between the target and all enemy units within 3" of any model from the
    /// target</b> (defender picks how), but each unit may only take up to as many hits as models in
    /// it."</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>How the cap is read, and why.</b> Taken absolutely literally, the second sentence's per-unit cap
    /// makes Blast <i>worse than no Blast</i> against a small lone unit: 3 hits x Blast(3) = 9, capped at a
    /// 2-model squad's 2, delivers 2 hits where the unmultiplied volley delivered 3. The older wording of
    /// the same rule (GFSQ 2.0.1, which carries worked examples) caps the MULTIPLIER by the models in the
    /// cluster instead - "where X is up to as many hits as enemy models in the target unit and within 3" of
    /// it" - and its examples give a 2-model unit 4 hits from an A2 Blast(3), and a 1-model unit 2 hits.
    /// So the cluster-capped reading is the one the examples describe, and it is the one Kai picked
    /// (2026-10-31) when shown both sets of numbers. Recorded here because the literal reading is right
    /// there in the 3.6.0 text and the next person to read it will wonder.
    /// </para>
    ///
    /// <para>
    /// Pure arithmetic and geometry, deliberately: the distribution is decided here and tested on its own,
    /// and the stages do the dice. "Defender picks how" is not offered as a choice - an even split with the
    /// remainder going to the target is deterministic, and a prompt per blast in a game the player fires
    /// dozens of shots in would cost more than it adds. Noted as a deviation rather than silently taken.
    /// </para>
    /// </summary>
    public static class BlastSpreadRules
    {
        // ─────────────────────────────────────────────────────────────────────────────────────────────
        // NOT WIRED INTO THE SHOOTING PIPELINE YET. Blast still behaves as the full-scale games' version
        // in every system; this is the decided distribution, tested on its own, waiting for the stage
        // that delivers it. Recorded here and in WorkItems/414 rather than left to be discovered.
        //
        // What the remaining slice needs, and why it was not rushed:
        //
        //   * A looping resolver inside FireStage, following ResolveSpellDamageStage's shape exactly (a
        //     ParentStage that pops one share per pass, builds a fresh CombatMetadata, and runs
        //     DetermineSaveRollsNeeded -> RollToSave -> AssignWounds -> ApplyWounds), plus a loop-decision
        //     sibling like DetermineMoreSpellTargetsStage. FireStage's children are StageBase<ICombatMetadata>
        //     and a ParentStage is one, so it nests cleanly.
        //   * The spilled hits must NOT go through SyntheticHitResolution, which is the obvious choice and
        //     the wrong one: that runs the whole hit-complete fold, so Blast would multiply a second time.
        //     They are already-multiplied hits that need the weapon's AP and the RECEIVING unit's own
        //     defensive rules - its Shielded, its Fortified, its Parry - and not the attacker's hit-adding
        //     rules again.
        //   * Each receiving unit needs its own cover check seeded (Blast ignores Cover, so zero) and its
        //     own morale consequence, since a bystander taken to half strength tests like any other.
        //
        // Three ways to get a half-built version subtly wrong, which is why the arithmetic landed first.
        // ─────────────────────────────────────────────────────────────────────────────────────────────

        /// <summary>"all enemy units within 3" of any model from the target".</summary>
        public const float SpreadRangeInches = 3f;

        /// <summary>One receiving unit and the hits it takes.</summary>
        public readonly record struct Share(IUnit Unit, float Hits);

        /// <summary>
        /// The whole distribution for one blast: who takes hits and how many, plus the multiplier actually
        /// used. <see cref="Target"/> is always the first share, even when it is zero, so a caller can rely
        /// on it being there.
        /// </summary>
        public readonly record struct Spread(int EffectiveMultiplier, float TotalHits, Share Target,
            IReadOnlyList<Share> Others)
        {
            /// <summary>True when any hits spill onto a unit that was not the target.</summary>
            public bool Spills => Others.Count > 0;
        }

        /// <summary>
        /// Every enemy unit of <paramref name="attacker"/> that <paramref name="target"/> is standing
        /// within 3" of - the units the hits spread to. The target itself is not in the list.
        /// <para>
        /// Measured model-to-model from the target (not from the attacker), because the rule reads "within
        /// 3" of any model from the target". Dead and off-table units are skipped: a corpse is not standing
        /// next to anything. Allies of the ATTACKER are skipped too - the rule says enemy units, so a
        /// blast landing beside the firer's own squad does not scatter into it.
        /// </para>
        /// </summary>
        public static List<IUnit> UnitsInSpreadRange(ITableState tableState, IUnit attacker, IUnit target)
        {
            var found = new List<IUnit>();
            foreach (IUnit other in tableState.Units.Objects)
            {
                if (ReferenceEquals(other, target)) continue;
                if (!other.GetIsAlive()) continue;
                if (ITeamExtensions.AreAllied(tableState.Teams.Objects, attacker.PlayerID, other.PlayerID)) continue;
                if (ReserveRules.IsInReserve(other)) continue;

                float gap = UnitCompareUtilities.MinDistanceBetweenUnits(target, other, out _, out _, includeVertical: true);
                if (gap <= SpreadRangeInches) found.Add(other);
            }

            return found;
        }

        /// <summary>
        /// How the hits of one blast are distributed: <paramref name="hits"/> is the volley's
        /// post-injection hit total and <paramref name="authoredMultiplier"/> the rule's X.
        /// <para>
        /// The multiplier is capped at the living models across the whole cluster (target plus everything
        /// within 3"), then the total is split as evenly as the receiving units allow, with any remainder
        /// going to the target - the unit the player actually aimed at should not end up with less than the
        /// bystanders.
        /// </para>
        /// </summary>
        public static Spread Distribute(IUnit target, IReadOnlyList<IUnit> nearby, float hits,
            int authoredMultiplier)
        {
            int clusterModels = LivingModels(target);
            foreach (IUnit unit in nearby) clusterModels += LivingModels(unit);

            // Floored at 1 so a cluster with no living models leaves the hits untouched rather than
            // erasing them - unreachable in play, since such a unit is not a legal target.
            int multiplier = Math.Max(1, Math.Min(authoredMultiplier, clusterModels));
            float total = hits * multiplier;

            if (nearby.Count == 0)
            {
                return new Spread(multiplier, total, new Share(target, total), Array.Empty<Share>());
            }

            // An even split across the cluster. Whole hits only: half a save die is not a thing, and the
            // probabilistic roller is fed integer batches everywhere else.
            int receivers = nearby.Count + 1;
            int each = (int)(total / receivers);
            float remainder = total - each * receivers;

            var others = new List<Share>(nearby.Count);
            foreach (IUnit unit in nearby)
            {
                if (each > 0) others.Add(new Share(unit, each));
            }

            return new Spread(multiplier, total, new Share(target, each + remainder), others);
        }

        private static int LivingModels(IUnit unit)
        {
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) count++;
            }

            return count;
        }
    }
}
