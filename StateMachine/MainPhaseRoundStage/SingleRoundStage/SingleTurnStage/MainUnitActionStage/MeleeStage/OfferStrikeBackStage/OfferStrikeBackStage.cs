
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;
using FDG.Utilities;
using System;

namespace FDG.Stages
{

    public class OfferStrikeBackStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnOfferAccepted;
        public StageBinding OnOfferRejected;

        public OfferStrikeBackStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnOfferAccepted = new StageBinding(this);
            OnOfferRejected = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            // Skip if no in-range defending model has a melee weapon to fight back with (#017).
            if (MeleeRangeUtilities.GetMeleeWeaponsFromModels(context.InRangeDefendingModels).Count == 0)
            {
                GameContext.Log("No in-range defender with a melee weapon; skipping strikeback.");
                await SkipStrikingBack(context);
                return;
            }

            // #437: whether striking back is a CHOICE is a per-system question.
            //   Full-scale and skirmish books: "Then defenders MAY CHOOSE to strike back, but don't have to."
            //   GFSQ Core v2.1.0:  "Then defenders MUST strike back"
            //   AoFQ Core v2.1.0:  "the defending unit MUST strike back"
            // So in both Quest systems there is no question to ask, and the engine asked it anyway in
            // every system. Kai's call (2026-11-01) is that the strike-back must still be VISIBLE when it
            // is forced - a prompt is the wrong way to show it, but silence is worse - so it is announced
            // instead of offered. Declining was only ever a self-nerf, which is why nobody noticed.
            if (Rules.Dispatch.CoreRulesProfile.For(GameContext).StrikeBackIsMandatory)
            {
                GameContext.Log($"{context.DefendingUnit.Name()} must strike back.");
                await GameContext.Announce($"{context.DefendingUnit.Name()} strikes back!",
                    tier: EBannerTier.Toast);
                await MoveToStrikingBack(context);
                return;
            }

            GameContext.Log("Offering strikeback.");

            //TODO: Indicate if they have struck back yet.
            YesNoRequest yesNoRequest = new YesNoRequest(context.DefendingUnit.PlayerID(), "Strike back?",
                defaultAnswer: true, displayName: "Deciding Whether to Strike Back");

            Task<bool> task = GameContext.PlayerRequester
                .RequestDecision<YesNoRequest, bool>(yesNoRequest);

            await task;

            if(task.Result)
            {
                await MoveToStrikingBack(context);
            }
            else
            {
                await SkipStrikingBack(context);
            }
        }

        private Task MoveToStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders striking back.");
            return OnOfferAccepted.Activate(context);
        }

        private Task SkipStrikingBack(ICombatActionContext context)
        {
            GameContext.Log("Defenders not striking back.");
            return OnOfferRejected.Activate(context);
        }
    }
}