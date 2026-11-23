using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Utilities;

namespace FDG.Stages
{

    public class ApplyWoundsStage<TMetadata> : CombatStage<ApplyWoundsResults, ApplyWoundsStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public ApplyWoundsStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) 
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<ApplyWoundsResults, Task> onFinished)
        {
            AssignWoundsResults assignWoundsResults = QueryForResultOrThrowException<AssignWoundsResults>(metaData);

            float totalWoundsApplied = 0;
            int modelsKilled = 0;

            UnitData defendingUnit = metaData.DefendingUnit.GetValue();

            // Alive-before/dead-after guard for the destruction seam below: upstream stages already stop
            // attacking a dead defender, so this is defense-in-depth against a double-fire.
            bool defenderWasAlive = metaData.DefendingUnit.GetIsAlive();

            // #232 casualty cascade: every casualty beat except the LAST overlaps the next (the
            // presenter paces only the stagger), so a multi-casualty volley plays as a rapid-fire
            // cascade with the final death/flinch running out in full. The assignments are
            // precomputed, so the last entry that will emit a beat (Wounds > 0) is known up front.
            // #414: Firefight used to branch here into its own wound effects (models die one per wound,
            // then markers on the last one and a roll). The GFF Core Rules v3.6.0 deleted that system -
            // "Once a model has taken as many wounds as its Tough value, it is killed" - so every system
            // now takes the same path below. Kai's call, 2026-10-30: play the rules as written.
            int lastCasualtyIndex = -1;
            for (int i = 0; i < assignWoundsResults.PendingWounds.Count; i++)
            {
                if (assignWoundsResults.PendingWounds[i].Wounds > 0)
                    lastCasualtyIndex = i;
            }

            for (int i = 0; i < assignWoundsResults.PendingWounds.Count; i++)
            {
                PendingWounds pendingWound = assignWoundsResults.PendingWounds[i];
                ModelData model = pendingWound.Model; //Shorthand.
                float woundsToDeal = pendingWound.Wounds;

                float modelRemainingWounds = model.TotalWounds - model.WoundsDealt;

                if (woundsToDeal > modelRemainingWounds)
                {
                    throw new Exception($"Tried to deal {woundsToDeal} to a model with only {modelRemainingWounds} left.");
                }

                model.DealWounds(woundsToDeal);
                totalWoundsApplied += woundsToDeal;

                bool overlap = i != lastCasualtyIndex;

                if(model.GetIsDead())
                {
                    modelsKilled++;
                    FDG.Missions.MissionCardEvents.ModelKilled(GameContext, model, metaData.AttackingUnit.GetValue());

                    // Present the death where the model fell. State is already dead; the front-end
                    // plays the death animation over the beat's duration before dropping the model.
                    await GameContext.Presenter.Present(
                        new ModelDiedBeat(model.ID, defendingUnit.ID, defendingUnit.Name, model.Position,
                            overlap, woundsToDeal));
                }
                else if (woundsToDeal > 0)
                {
                    // Survived a wound (e.g. Tough) — a hurt flinch, distinct from death.
                    await GameContext.Presenter.Present(new ModelWoundedBeat(model.ID, model.Position, overlap, woundsToDeal));
                }
            }

            // Unit Psychology - Retreating: shooting wounds are noted; the unit falls back at the end of the activation.
            if (!metaData.IsMelee && !metaData.IsSpell && totalWoundsApplied > 0)
                UnitPsychology.ShootingWounds(GameContext, defendingUnit, metaData.AttackingUnit.GetValue(), totalWoundsApplied);

            if(metaData.DefendingUnit.GetIsAlive())
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models.");
            }
            else
            {
                GameContext.Log($"Applying {totalWoundsApplied} wounds killed {modelsKilled} models, killing the unit.");

                // The one choke point every attacker-caused death passes through (shooting, melee swings,
                // impact hits, spell damage, strafing): clear the dead unit's cross-unit marks and fire
                // the unit-destroyed hook with the attacker as killer.
                if (defenderWasAlive)
                {
                    await UnitDestructionNotifier.NotifyUnitDestroyed(GameContext, defendingUnit,
                        metaData.AttackingUnit.GetValue(),
                        metaData.IsSpell ? FDG.Missions.EKillCause.Spell
                            : metaData.IsMelee ? FDG.Missions.EKillCause.Melee
                            : FDG.Missions.EKillCause.Shooting);
                }
            }

            await ApplyBrutalDamage(metaData, totalWoundsApplied);
            await ApplySelfWounds(metaData);

            await onFinished(new ApplyWoundsResults(modelsKilled));
        }

        /// <summary>
        /// #197 Hazardous: the ATTACKER's own overheat, counted at hit-roll-complete and carried on
        /// <see cref="RollToHitResults.SelfWounds"/>. Applied here — the last stage of every attack chain
        /// that rolls to hit — so the attack the models paid for has fully resolved first (owner-ruled
        /// 2026-07-29), and never in <c>RollToHitStage</c> itself, whose continuation after
        /// <c>onFinished</c> is unreachable.
        /// <para>
        /// Chains with no hit roll (Ravage, Impact hits, spell damage) carry no such result and skip this
        /// entirely. The wounds are unignorable by construction: <c>ApplyUnitWounds</c> bypasses the save
        /// and wound-ignore pipeline, matching dangerous terrain and No Retreat.
        /// </para>
        /// </summary>
        /// <summary>Brutal Damage: Critical Hits' extra wounds, and Directional Damage's D3 for wounds taken in the rear.</summary>
        private async Task ApplyBrutalDamage(ICombatMetadata metaData, float woundsApplied)
        {
            if (!metaData.QueryForResult(out RollToHitResults hit)) return;
            UnitData target = metaData.DefendingUnit.GetValue();
            float extra = hit.ExtraTargetWounds;
            if (hit.RearFacingHit && woundsApplied > 0f)
            {
                int d3 = (HordePlacement.RollD6(GameContext) + 1) / 2;
                GameContext.Log($"Directional Damage: {target.Name} was hit in its rear - {d3} extra wound{(d3 == 1 ? "" : "s")}.");
                extra += d3;
            }
            if (extra <= 0f || !target.GetIsAlive()) return;
            await QuestActions.Toast(GameContext, $"{target.Name} takes {extra:0} extra wound{(extra == 1f ? "" : "s")} (Brutal Damage).");
            await CasualtyPresentation.ApplyUnitWounds(GameContext, target, extra);
        }

        private async Task ApplySelfWounds(ICombatMetadata metaData)
        {
            if (!metaData.QueryForResult(out RollToHitResults hitResults) || hitResults.SelfWounds <= 0f)
            {
                return;
            }

            UnitData attacker = metaData.AttackingUnit.GetValue();
            GameContext.Log($"{attacker.Name} takes {hitResults.SelfWounds:0.##} " +
                $"wound{(hitResults.SelfWounds == 1f ? "" : "s")} from its own {metaData.WeaponType.Name}.");

            await CasualtyPresentation.ApplyUnitWounds(GameContext, attacker, hitResults.SelfWounds);
        }
    }
}