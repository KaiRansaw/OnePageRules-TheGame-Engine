using FDG.Data;

namespace FDG.Stages
{
    public class DetermineInRangeDefendersStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_IN_RANGE_DEFENDER_FINISHED_TRANSITION = "DetermineInRangeDefenderFinished";

        public StageBinding ToChooseMeleeWeapons;

        /// <summary>
        /// #355: the attacker is in contact with nothing to swing (an impact-only charger, or a unit whose
        /// melee-armed models died), so the weapon offer is skipped and the melee goes straight to the
        /// strike-back the defender is owed. This stage owns the branch because it is the last one to run
        /// before <c>ChooseMeleeWeaponStage</c>, which throws on an empty pool — and because the strike-back
        /// needs the in-range defenders THIS stage records.
        /// </summary>
        public StageBinding ToStrikeBackUnopposed;

        public DetermineInRangeDefendersStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToChooseMeleeWeapons = new StageBinding(this);
            ToStrikeBackUnopposed = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // #017: record which defending models are within melee range of an attacker; these are the
            // models eligible to strike back (consumed by StrikeBackStage).
            IReadOnlyList<DataBinding<ModelData>> defenderModels = context.DefendingUnit.GetValue().ModelBindings;
            IReadOnlyList<DataBinding<ModelData>> attackerModels = context.AttackingUnit.GetValue().ModelBindings;

            List<DataBinding<ModelData>> inRange = MeleeRangeUtilities.GetModelsInMeleeRange(defenderModels, attackerModels);
            // #458 Fast-Play: "When fighting in melee, all models always count as being in range of the enemy,
            // regardless of their position, so everyone can always strike with all weapons." Only once the
            // two units ARE in melee - a charge where nobody reached contact is still no melee.
            if (inRange.Count > 0 && FastPlay.Melee(GameContext.Settings))
                inRange = MeleeRangeUtilities.AllLiving(defenderModels);
            context.SetInRangeDefenders(inRange);

            GameContext.Log($"Determine In Range Defenders: {inRange.Count} of {MeleeRangeUtilities.AllLiving(defenderModels).Count} living models in melee range.");

            // #355: nothing to swing - skip the weapon offer (and the extra-attack window before it, which
            // is an extra ATTACK for a unit making none) and let the defender strike back.
            if (context.AvailableWeapons.Count == 0)
            {
                await ToStrikeBackUnopposed.Activate(context);
                return;
            }

            await ToChooseMeleeWeapons.Activate(context);
        }

    }
}
