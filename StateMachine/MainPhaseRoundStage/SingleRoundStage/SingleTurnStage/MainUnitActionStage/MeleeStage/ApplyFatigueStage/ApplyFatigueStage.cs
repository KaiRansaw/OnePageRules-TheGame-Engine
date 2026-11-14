using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    public class ApplyFatigueStage : StageBase<ICombatActionContext>
    {
        public const string APPLY_FATIGUE_FINISHED_TRANSITION = "ApplyFatiqueFinished";

        public StageBinding OnFatigueApplied;

        public ApplyFatigueStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnFatigueApplied = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // #020 Fatigue, 3.6.0: "After a melee where they attacked, units hit on unmodified rolls of 6 in
            // melee until the end of that round." Applied here, at the end of the melee, so the swings just
            // resolved used full Quality. FatigueUtilities.ApplyFatigued is idempotent.
            //   - AttackingUnit:  the unit that swung first (the charger, or a Counter unit that swapped in).
            //   - DefendingUnit:  only if it actually attacked - struck back, or (after a Counter swap) made the
            //                     charge's own attack. A charger a Counter unit left with no model able to swing
            //                     did not attack, so it is not Fatigued (until 1.94 it was, for charging - the
            //                     3.5.1 wording, "at the end of an activation in which they charged").
            IUnit swinger = context.AttackingUnit.GetValue();
            FatigueUtilities.ApplyFatigued(swinger);
            GameContext.Log($"{swinger.Name} is Fatigued.");

            if (context.DefenderStruckBack)
            {
                IUnit striker = context.DefendingUnit.GetValue();
                FatigueUtilities.ApplyFatigued(striker);
                GameContext.Log($"{striker.Name} struck back and is Fatigued.");
            }

            // #381: both combatants also get the round-scoped "was in melee" fact. Distinct from Fatigued
            // because a defender that neither charged nor struck back is never fatigued, yet it WAS in
            // melee - and "after being in melee" rules (AoF Retreating Strike) gate on this token as
            // plain data. Same end-of-melee seam as fatigue, same RoundEnd sweep.
            StampWasInMelee(context.AttackingUnit.GetValue());
            StampWasInMelee(context.DefendingUnit.GetValue());

            await OnFatigueApplied.Activate(context);
        }

        private static void StampWasInMelee(IUnit unit)
        {
            if (!unit.Tokens.HasToken(TokenType.WasInMeleeThisRound))
            {
                unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.WasInMeleeThisRound));
            }
        }
    }
}
