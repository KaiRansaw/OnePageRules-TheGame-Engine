using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// The movement subsystem as a callable primitive — the engine refactor (#042 Phase 7h)
    /// that lets a unit be moved from outside the normal turn/stage flow (e.g. a rule's
    /// <see cref="Rules.Definitions.RuleOperation.InvokeTriggeredMove"/>, mid-move attacks,
    /// post-deploy repositions).
    /// <para>
    /// The normal movement stages delegate their bodies here so there is exactly one place
    /// that applies dangerous-terrain effects (<see cref="ApplyDangerousTerrainEffects"/>) and
    /// one place that commits positions (<see cref="CommitPositions"/>). The two stay separate
    /// methods because the normal flow runs them as distinct stages — merging them would
    /// double-apply terrain effects.
    /// </para>
    /// </summary>
    public static class MovementExecutor
    {
        /// <summary>The batched dangerous-terrain test result: the single roll (one d6 per testing model),
        /// how many models tested, total wounds rolled (fractional under the probabilistic roller), the
        /// per-model wounds still PENDING, and who is testing. Returned so the (async) caller can present
        /// one dice beat for the whole batch and then land the wounds - see
        /// <see cref="ResolveDangerousTerrain"/>.</summary>
        public readonly struct DangerousTerrainResult
        {
            public readonly IDiceResults? Roll;
            public readonly int ModelCount;
            public readonly float Wounds;
            /// <summary>The wounds this test owes, NOT yet applied. Landed by <see cref="ResolveDangerousTerrain"/>.</summary>
            public readonly IReadOnlyList<PendingModelWound> PendingWounds;
            /// <summary>
            /// The unit that tested. Carried as the unit itself (not just its ID) because
            /// <see cref="ResolveDangerousTerrain"/> needs it for the destruction seam when the test wipes
            /// the unit out, not only for the casualty beats. Null only on <see cref="None"/>.
            /// </summary>
            public readonly IUnit? Unit;

            public DangerousTerrainResult(IDiceResults? roll, int modelCount, float wounds,
                IReadOnlyList<PendingModelWound> pendingWounds, IUnit? unit)
            {
                Roll = roll; ModelCount = modelCount; Wounds = wounds;
                PendingWounds = pendingWounds; Unit = unit;
            }
            public bool AnyTested => ModelCount > 0 && Roll != null && Unit != null;
            public static DangerousTerrainResult None =>
                new DangerousTerrainResult(null, 0, 0f, Array.Empty<PendingModelWound>(), null);
        }

        /// <summary>
        /// Every model whose path crosses dangerous terrain takes a test; they all roll together in ONE
        /// batch (a wound on a 1). Batching is what lets the probabilistic roller work properly here -- a
        /// single N-die roll yields the expected number of 1s, which N separate decisive rolls could not.
        /// <para>
        /// Rolls only: the wounds come back PENDING and are landed later by
        /// <see cref="ResolveDangerousTerrain"/>, after the move has been animated, so a casualty falls
        /// at its destination instead of vanishing from the start line. Rolling here keeps the dice draw
        /// in its original place in the seeded stream. Out-of-band callers that only want the state
        /// change can land the batch with <see cref="CasualtyPresentation.ApplyOnly"/>.
        /// </para>
        /// </summary>
        public static DangerousTerrainResult RollDangerousTerrain(IGameContext gameContext,
            IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> relevantTerrain, IUnit unit,
            bool ignoresDangerousTerrain = false, bool countsAsInDangerousTerrain = false)
        {
            // Flying (AllTerrain scope) ignores Dangerous-terrain effects entirely — no roll, no wounds —
            // including a "counts as in Dangerous Terrain" grant (ignoring the real effect ignores the
            // counted-as one).
            if (ignoresDangerousTerrain) return DangerousTerrainResult.None;

            // Unstable belongs in this list even though it is not Dangerous: AoF: Quest's Unstable counts
            // AS Dangerous for a model that moved over 6", and that distance rule lives inside
            // MovementUtilities.DoesPathCrossDangerousTerrain below. Filtering to Dangerous alone here
            // would hand that method a list with no Unstable piece in it, so the rule would be
            // unreachable - correct code downstream, fed nothing. (Audit finding 8.)
            List<ITerrain> dangerous = relevantTerrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)
                    || t.TerrainType.HasFlag(ETerrainType.Unstable))
                .ToList();

            // "Counts as being in Dangerous Terrain" (#153): every moving model tests, regardless of what
            // its path actually crosses — so the no-dangerous-on-table early-out doesn't apply.
            if (dangerous.Count == 0 && !countsAsInDangerousTerrain) return DangerousTerrainResult.None;

            // #414 / OPR 3.6.0: the trigger is per-path, the TEST is per-unit. "Units with models moving
            // across Dangerous terrain, or that activate in it, must roll as many dice as the number of
            // wounds it would take to fully destroy them" - word for word in GF, AoF, AoFS v3.6.0 and GFSQ
            // v2.1.0, so this is every game system. Before 3.6 each crossing model tested for itself, which
            // meant one model clipping a corner of a minefield risked one die while the nine models behind
            // it risked nothing. Now the unit tests as a unit: one model's path touching the piece is enough
            // to put the whole unit's remaining wounds on the table.
            bool anyCrossed = false;
            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                if (!countsAsInDangerousTerrain
                    && !MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous)) continue;
                anyCrossed = true;
                break;
            }
            if (!anyCrossed) return DangerousTerrainResult.None;

            return RollBatch(gameContext, UnitTesters(unit), unit);
        }

        /// <summary>
        /// A dangerous-terrain test forced on <paramref name="unit"/> without any move: every LIVING model
        /// tests, wherever it stands. The immediate arm of #197 P8's Dangerous Terrain Debuff
        /// ("pick one enemy unit within 18\", which must immediately take a Dangerous Terrain test") —
        /// distinct from the deferred "counts as being in Dangerous Terrain once" arm, which rides the
        /// unit's next move through <see cref="RollDangerousTerrain"/> instead.
        /// <para>
        /// Same waiver as a real crossing: a Flying target (the AllTerrain ignore scope)
        /// takes no test at all. Owner-ruled 2026-07-28 — one rule for all three dangerous-terrain paths
        /// beats a debuff that ignores what every other terrain effect honours. Strider is DifficultOnly
        /// and so does NOT waive it, exactly as when it walks through the real thing.
        /// </para>
        /// Rolls only; the caller lands the wounds with <see cref="ResolveDangerousTerrain"/>.
        /// </summary>
        public static DangerousTerrainResult RollForcedDangerousTerrain(IGameContext gameContext, IUnit unit,
            bool ignoresDangerousTerrain = false)
        {
            if (ignoresDangerousTerrain) return DangerousTerrainResult.None;

            return RollBatch(gameContext, UnitTesters(unit), unit);
        }

        /// <summary>
        /// A dangerous-terrain test on a unit that simply ACTIVATED in Dangerous terrain, having moved
        /// nowhere - the clause 3.6.0 added: *"Units with models moving across Dangerous terrain, **or that
        /// activate in it**, must roll..."*. Identical in GF, AoF, AoFS v3.6.0 and GFSQ v2.1.0.
        ///
        /// <para>
        /// This closes a real hole: before 3.6 a unit could Hold inside a minefield indefinitely and never
        /// test, so sitting in dangerous terrain was strictly safer than walking out of it. It also settles
        /// a design fork this repo had carried open for months ("what happens when you hold in dangerous
        /// terrain?") - the book now answers it outright.
        /// </para>
        ///
        /// <para>
        /// Membership is "any living model touching the piece", measured with
        /// <see cref="Rules.Definitions.TerrainProximityQueries.IsModelWithinInches"/> at 0", which is
        /// base-aware rather than centre-only: a model whose base overlaps the edge of a minefield is in
        /// the minefield. Same waiver as every other dangerous path - Flying (AllTerrain) and Explorer skip
        /// it; Strider is DifficultOnly and does not.
        /// </para>
        /// </summary>
        public static DangerousTerrainResult RollActivationDangerousTerrain(IGameContext gameContext,
            IUnit unit, IEnumerable<ITerrain> relevantTerrain, bool ignoresDangerousTerrain = false)
        {
            if (ignoresDangerousTerrain) return DangerousTerrainResult.None;

            // Dangerous ONLY, deliberately - no Unstable here. This is the "or that activate in it" half
            // of the rule, and AoF: Quest's Unstable is conditional on MOVING over 6" into or through the
            // piece: "If any model in a unit MOVES over 6" when moving into or through unstable terrain,
            // then it counts as Dangerous terrain." A unit that simply starts its activation standing in
            // rubble has not moved, so it tests for nothing. (Audit finding 8.)
            List<ITerrain> dangerous = relevantTerrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .ToList();
            if (dangerous.Count == 0) return DangerousTerrainResult.None;

            bool standingInIt = false;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                if (!Rules.Definitions.TerrainProximityQueries.IsModelWithinInches(model, dangerous, 0f))
                    continue;
                standingInIt = true;
                break;
            }
            if (!standingInIt) return DangerousTerrainResult.None;

            return RollBatch(gameContext, UnitTesters(unit), unit);
        }

        /// <summary>
        /// The unit's living models, which under 3.6.0 is the testing pool for EVERY dangerous-terrain test
        /// however it was triggered - a crossing, an activation, or a forced one. Kept in one place so the
        /// three triggers cannot drift into rolling different numbers of dice for the same rule.
        /// </summary>
        private static List<IModel> UnitTesters(IUnit unit) =>
            unit.Models.Where(model => model.GetIsAlive()).ToList();

        /// <summary>
        /// The batched roll shared by every dangerous-terrain trigger: ONE roll of N d6, a wound per 1.
        /// Batching is what lets the probabilistic roller work here — a single N-die roll yields the
        /// expected number of 1s, which N separate decisive rolls could not.
        /// </summary>
        private static DangerousTerrainResult RollBatch(IGameContext gameContext, IReadOnlyList<IModel> testers,
            IUnit unit)
        {
            if (testers.Count == 0) return DangerousTerrainResult.None;

            // #414 / 3.6.0: "as many dice as the number of wounds it would take to fully destroy them" -
            // the unit's REMAINING wounds, summed over its living models, not its Tough at full strength.
            // "Fully destroy" is measured from where the unit is now, so a mauled squad rolls fewer dice
            // than a fresh one, which is the same reading Falling and Regeneration use.
            //
            // This replaces the 3.5.x wording ("roll one die, or as many dice as the model's Tough value"),
            // which was per crossing MODEL. The arithmetic below is unchanged on purpose: each entry is
            // still that model's share of the pool, so the pending-wound distribution, the per-model cap and
            // the probabilistic spread all keep working. What changed is WHO is in the pool and how many
            // dice each one brings - and because a model's share is now its remaining wounds rather than its
            // Tough, the cap is also exactly the damage that model can absorb, so a 3-wound roll can no
            // longer be parked on a model with 1 wound left.
            int[] dice = new int[testers.Count];
            int totalDice = 0;
            for (int i = 0; i < testers.Count; i++)
            {
                dice[i] = Math.Max(1, (int)MathF.Round(testers[i].RemainingWounds));
                totalDice += dice[i];
            }

            IDiceResults roll = gameContext.DiceRoller.Roll(6, totalDice);
            float ones = roll.At(1); // 1s are wounds (fractional under the probabilistic roller)

            List<PendingModelWound> pending = new List<PendingModelWound>();
            float woundsRolled;
            if (gameContext.Settings.RandomnessType == ERandomnessType.Probabilistic)
            {
                // Spread the expected wounds by dice - each die carried its own 1/6 chance of a 1.
                float perDie = ones / totalDice;
                if (perDie > 0f)
                {
                    for (int i = 0; i < testers.Count; i++)
                        pending.Add(new PendingModelWound(testers[i], perDie * dice[i]));
                }
                woundsRolled = ones;
            }
            else
            {
                // Realistic: a whole number of 1s came up. Hand them out model by model, each taking at
                // most as many wounds as dice it rolled.
                int wounds = (int)MathF.Round(ones);
                int given = 0;
                for (int i = 0; i < testers.Count && given < wounds; i++)
                {
                    int take = Math.Min(dice[i], wounds - given);
                    pending.Add(new PendingModelWound(testers[i], take));
                    given += take;
                }
                woundsRolled = wounds;
            }

            // The die count is the player-facing number now that the pool is unit-wide: "6 dice" is what
            // they can check against the unit's own wound total, where "3 models tested" no longer explains
            // the handful of dice on the table.
            gameContext.Log($"{unit.Name}: dangerous terrain test - {totalDice} dice, "
                + $"{woundsRolled:0.##} wound(s) dealt.");
            return new DangerousTerrainResult(roll, testers.Count, woundsRolled, pending, unit);
        }

        /// <summary>
        /// Commit the final model positions for each move entry. Body lifted verbatim from
        /// <c>ExecuteMoveStage</c>.
        /// </summary>
        public static void CommitPositions(IReadOnlyList<ModelMoveEntry> paths)
        {
            foreach (ModelMoveEntry modelEntry in paths)
            {
                if (modelEntry.Positions.Count > 0)
                {
                    //Setting each position may be redundant for awhile, but we might add some kind of animation
                    //where the position updates queue up. So, we'll do this anyway.
                    for (int i = 0; i < modelEntry.Positions.Count; i++)
                    {
                        modelEntry.Model.GetValue().SetPosition(modelEntry.Positions[i]);
                        // Per-waypoint facing (#150), when the resolver supplied it (default: direction of travel).
                        if (modelEntry.Facings != null && i < modelEntry.Facings.Count)
                            modelEntry.Model.GetValue().SetFacing(modelEntry.Facings[i]);
                    }
                }
            }
        }

        /// <summary>
        /// The full move sequence for an out-of-band move: roll the dangerous-terrain test, then commit
        /// positions. Mirrors the normal flow's <c>ApplyNonMovementTerrainEffectsStage</c> →
        /// <c>ExecuteMoveStage</c> order. The wounds come back PENDING; the caller lands and animates
        /// them with <see cref="ResolveDangerousTerrain"/> once the models are where they end up — the
        /// normal flow resolves from its stage, so the out-of-band caller must too.
        /// </summary>
        public static DangerousTerrainResult Commit(IGameContext gameContext, IReadOnlyList<ModelMoveEntry> paths,
            IEnumerable<ITerrain> relevantTerrain, IUnit unit, bool ignoresDangerousTerrain = false)
        {
            DangerousTerrainResult result =
                RollDangerousTerrain(gameContext, paths, relevantTerrain, unit, ignoresDangerousTerrain);
            CommitPositions(paths);
            return result;
        }

        /// <summary>
        /// Lands a rolled dangerous-terrain batch: the whole roll as a single <see cref="DiceRolledBeat"/>
        /// (one d6 per testing model: 2+ safe/green, a 1 a wound), then the wounds themselves, each
        /// model's death or flinch animating as its wound lands (<see cref="CasualtyPresentation"/>).
        /// Shared by the normal-move stage and the out-of-band (triggered) move so a Vanguard / forced
        /// move shows the same sequence the player sees on a normal move.
        /// <para>
        /// Called AFTER the move has been animated, so a casualty falls at the destination it walked to.
        /// Dangerous terrain deals wounds but is NOT a morale-test source, so no morale test is run here —
        /// but a test that wipes the unit out IS a destruction, and goes through the destruction seam.
        /// </para>
        /// </summary>
        public static async Task ResolveDangerousTerrain(IGameContext gameContext,
            DangerousTerrainResult result)
        {
            if (!result.AnyTested) return;
            IUnit unit = result.Unit!;

            string summary = result.Wounds <= 0f
                ? "All safe"
                : $"{result.Wounds:0.##} wound{(result.Wounds == 1f ? "" : "s")}";
            await gameContext.Presenter.Present(DiceRolledBeat.From(result.Roll!, successThreshold: 2,
                gameContext.Settings.RandomnessType, "Dangerous Terrain", summary));

            bool wasAlive = unit.GetIsAlive();

            await CasualtyPresentation.ApplyAndPresent(gameContext, result.PendingWounds,
                unit.ID, unit.Name);

            // Dangerous terrain can finish a unit off, and that death is a destruction like any other:
            // the dead unit's OwnerDestroyed marks on OTHER units have to clear, and if it was a Transport
            // its cargo has to spill rather than stay embarked in a wreck that no longer exists (the #169
            // ghost state, reachable through terrain). Killer-less, so the Shooting_OnUnitDestroyed hook
            // correctly stays silent - there is no attacker to credit.
            if (wasAlive && !unit.GetIsAlive())
            {
                await UnitDestructionNotifier.NotifyUnitDestroyed(gameContext, unit, killer: null);
            }
        }

        /// <summary>
        /// Move <paramref name="unit"/> along <paramref name="paths"/>, validated against a single
        /// distance budget of <paramref name="maxInches"/> (no charge semantics). On success the move
        /// is committed and the method returns true; on failure nothing is mutated and
        /// <paramref name="errors"/> describes why.
        /// </summary>
        public static bool TryMove(IGameContext gameContext, DataBinding<UnitData> unit,
            List<ModelMoveEntry> paths, float maxInches, out List<ReasonForInvalidMove> errors,
            out DangerousTerrainResult dangerResult)
        {
            dangerResult = DangerousTerrainResult.None;
            List<ITerrain> relevantTerrain = new List<ITerrain>(gameContext.TableState.Terrain.Objects);

            // #090: an out-of-band move (e.g. Vanguard) is enemy-aware like a normal move — it may not pass
            // through or end stacked on an enemy unless the unit may move through enemies (Strafing fly-over).
            List<EnemyModelFootprint> enemyFootprints = MovementUtilities.GetEnemyModelFootprints(unit, gameContext);
            // #205: a triggered move (Harassing / Hit & Run / forced move) may not end stacked on a friendly.
            List<EnemyModelFootprint> friendlyFootprints = MovementUtilities.GetFriendlyModelFootprints(unit, gameContext);
            bool canMoveThroughEnemies = Rules.Dispatch.MovementRuleQueries.CanMoveThroughEnemies(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresDifficultTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresDifficultTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);
            bool ignoresAllTerrain = Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(
                unit.GetValue(), gameContext.RuleEvaluator);

            if (!MovementUtilities.ValidatePaths(paths, maxInches, enemyFootprints, canMoveThroughEnemies,
                    ignoresDifficultTerrain, ignoresAllTerrain, relevantTerrain, out errors, friendlyFootprints))
            {
                return false;
            }

            // Flying ignores Dangerous terrain too (same AllTerrain scope as the impassible waiver above).
            dangerResult = Commit(gameContext, paths, relevantTerrain, unit.GetValue(), ignoresDangerousTerrain: ignoresAllTerrain);
            return true;
        }
    }
}
