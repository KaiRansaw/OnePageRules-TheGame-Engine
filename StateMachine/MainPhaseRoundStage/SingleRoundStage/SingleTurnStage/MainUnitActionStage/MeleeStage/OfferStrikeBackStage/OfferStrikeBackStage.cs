
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

            // Counter swapped the roles: the "defender" being offered a strike back is the unit that CHARGED,
            // and its swing is the charge's own attack, not a strike back. The books: "the charging unit
            // attacks first, then the defenders may strike back" - Counter only moves the defender's swing
            // ahead of the charger's ("May strike first with this weapon when charged"). The charger never
            // had a choice about attacking, so it is not asked (look pass, 2026-11-06: an AoF charge into
            // Counter spears asked the charging player "Strike back?").
            if (context.ChargingUnit is { } charger && ReferenceEquals(charger, context.DefendingUnit))
            {
                GameContext.Log($"{context.DefendingUnit.Name()} attacks (its charge, after the counter).");
                await GameContext.Announce($"{context.DefendingUnit.Name()} attacks!", tier: EBannerTier.Toast);
                await MoveToStrikingBack(context);
                return;
            }

            // Extra Actions: Defensive Stance "may not strike back until its next activation" - and that holds
            // even in the Quest books' must-strike-back systems, though Extra Actions are off there anyway.
            if (ExtraActions.MayNotStrikeBack(context.DefendingUnit.GetValue()))
            {
                GameContext.Log($"{context.DefendingUnit.Name()} is in a Defensive Stance - it may not strike back.");
                await GameContext.Announce($"{context.DefendingUnit.Name()} holds its Defensive Stance - no strike back.",
                    tier: EBannerTier.Toast);
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
            YesNoRequest yesNoRequest = new YesNoRequest(context.DefendingUnit.PlayerID(),
                // Who and at whom: a bare "Strike back?" in an otherwise empty panel did not say which of your
                // units was being asked (look pass, 2026-11-06).
                $"{context.DefendingUnit.Name()}: strike back at {context.AttackingUnit.Name()}?",
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