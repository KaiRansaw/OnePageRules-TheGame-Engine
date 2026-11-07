using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Dispatch;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{

    public class ResolveRangedMoraleStage : StageBase<ICombatActionContext>
    {
        public const string RESOLVE_RANGED_MORALE_FINISHED_TRANSITION =
            "ResolveRangedMoraleFinished";

        public StageBinding ToFinished;

        public ResolveRangedMoraleStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            if (context.AttackedDefenders.Count > 0)
                GameContext.Log("Resolving ranged morale.");

            // Once per unit that was shot at this activation, in the order they were first targeted, and
            // only after every weapon has fired. Melee already works this way (its morale sub-chain sits
            // after the weapon loop drains); shooting used to run this stage inside the weapon loop, which
            // tested only the current weapon's target and re-baselined its wounds on every weapon.
            foreach (AttackedDefender attacked in context.AttackedDefenders)
            {
                await ResolveForDefender(attacked, context.AttackingUnit.GetValue());

                // Extra Actions - Overwatch: a unit on Overwatch that was shot at may shoot back (OverwatchStage).
                UnitData defender = attacked.Defender.GetValue();
                if (defender.Tokens.HasToken(TokenType.Overwatch)) ExtraActions.Mark(defender, TokenType.OverwatchShotAt);
            }

            await ResolveSuppression(context.AttackingUnit.GetValue());

            await ToFinished.Activate(context);
        }

        /// <summary>
        /// Covering Fire's Morale Test, taken "regardless of casualties" by every unit the suppression dice
        /// pinned this shooting action - once each, whatever number of weapons rolled a 4+ at it. A failure is
        /// Shaken, like any shooting morale failure.
        /// </summary>
        private async Task ResolveSuppression(IUnit attacker)
        {
            List<DataBinding<UnitData>> pinned = GameContext.GameDataStore.GetAllValues<ArmyData>()
                .SelectMany(army => army.UnitBindings)
                .Where(b => b.GetValue().Tokens.HasToken(TokenType.Suppressed))
                .ToList();
            foreach (DataBinding<UnitData> binding in pinned)
            {
                UnitData unit = binding.GetValue();
                unit.Tokens.RemoveTokens(TokenType.Suppressed);
                if (!unit.GetIsAlive()) continue;

                float shotFrom = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, unit, out _, out _, includeVertical: true);
                var outcome = await MoraleUtilities.TakeMoraleTest(GameContext, unit,
                    HeroStatRules.GetMoraleQuality(unit, GameContext.TableState), EMoraleTrigger.Shooting,
                    failureCausesShakenOrRout: true, attackerLaunchDistanceInches: shotFrom);
                if (outcome.Passed)
                {
                    GameContext.Log($"{unit.Name} holds under the Covering Fire.");
                    continue;
                }
                GameContext.Log($"{unit.Name} fails its morale test under Covering Fire and is now Shaken.");
                await MoraleUtilities.ApplyShakenWithPresentation(GameContext, binding);
            }
        }

        // A unit left at half strength or less by wounds from shooting must take a morale test; a failed
        // test makes it Shaken (a non-melee morale failure never Routs — Rout is a melee-only result,
        // GF v3.5.1). The test fires whenever the shoot action wounded the unit and it now sits at half
        // or less — a unit already below half tests again every activation it takes wounds (#254). The
        // baseline is its wound count when this attacker first targeted it, so a unit shot by three
        // weapons tests at most once, and a targeted-but-unwounded unit doesn't test.
        private async Task ResolveForDefender(AttackedDefender attacked, IUnit attacker)
        {
            DataBinding<UnitData> defenderBinding = attacked.Defender;

            // #412 Scourge banks "counts as extra wounds dealt" on the attacker during the save rolls
            // (see Effect.BankTokenPerUnmodifiedSaveRoll). Spent here for the half-strength comparison
            // only - the defender loses no actual wounds - which is the shooting half of the rule's
            // "or if shooting wounds would trigger Morale Tests".
            float counted = attacker.Tokens.GetTokenCount(TokenType.MeleeWoundTally);
            if (counted > 0f)
            {
                GameContext.Log($"{defenderBinding.Name()} counts as having taken +{counted} wound(s) "
                    + $"from {attacker.Name} for this morale check (Scourge).");
            }

            // #442: the Hold the Line family's +1 applies only when shot from over 9" away, so the test
            // carries the shooter's distance. The same measurement RollToHitStage and AssignWoundsStage
            // take, so this gate reads the range exactly as the hit and save gates did.
            float shotFrom = UnitCompareUtilities.MinDistanceBetweenUnits(
                attacker, defenderBinding.GetValue(), out _, out _, includeVertical: true);

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(
                GameContext, defenderBinding, attacked.RemainingWoundsAtStart,
                EMoraleTrigger.Shooting, shotFrom, counted);

            if (result == true)
            {
                GameContext.Log($"{defenderBinding.Name()} is at half strength or less but passed its morale test (needed {defenderBinding.Quality()}).");
            }
            else if (result == false)
            {
                GameContext.Log($"{defenderBinding.Name()} is at half strength or less, failed its morale test, and is now Shaken.");
                // #414 Pushing (Firefight/Skirmish only): "whenever a unit is Shaken after being
                // attacked, the attacker may try to push it". This is one of the two places an attack can
                // Shake a unit; before 3.6.0 the trigger hung off Firefight's wound-effect roll, which no
                // longer exists. No-op in every other game system.
                await FirefightRules.TryPush(GameContext, defenderBinding.GetValue(), attacker);
            }
        }
    }
}
