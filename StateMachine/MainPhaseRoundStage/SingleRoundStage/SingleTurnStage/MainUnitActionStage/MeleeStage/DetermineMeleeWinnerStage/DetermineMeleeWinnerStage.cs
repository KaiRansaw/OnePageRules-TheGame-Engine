using FDG.Presentation.Beats;
﻿
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{
    public class DetermineMeleeWinnerStage : StageBase<ICombatActionContext>
    {
        public const string DETERMINE_MELEE_WINNER_NEEDS_ROLL_TRANSITION = "DetermineMeleeWinnerNeedsRoll";
        public const string DETERMINE_MELEE_WINNER_DOESNT_NEED_ROLL_TRANSITION = "DetermineMeleeWinnerDoesntNeedRoll";

        public StageBinding OnNeedsRollToDecide;
        public StageBinding OnDoesntNeedRollToDecide;

        public DetermineMeleeWinnerStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnNeedsRollToDecide = new StageBinding(this);
            OnDoesntNeedRollToDecide = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            IUnit attacker = context.AttackingUnit.GetValue();
            IUnit defender = context.DefendingUnit.GetValue();

            //Get wounds dealt by each side.
            float attackerWoundsDealt = context.DefenderRemainingWoundsAtStart - context.DefendingUnit.RemainingWounds();
            float defenderWoundsDealt = context.AttackerRemainingWoundsAtStart - context.AttackingUnit.RemainingWounds();

            // #021 Fear(X): a unit counts as dealing +X extra wounds for the who-won-melee check only (no
            // real wounds). Fire Melee_OnMeleeResolution for each side as the Actor and fold its own
            // ExtraMeleeWoundCount into its dealt total, so the loser — and thus who must test morale — can flip.
            MeleeResolutionContext resolution = new MeleeResolutionContext(attacker, defender);
            float attackerBonus = SumExtraMeleeWounds(resolution, attacker);
            float defenderBonus = SumExtraMeleeWounds(resolution, defender);
            if (attackerBonus > 0f)
                GameContext.Log($"{attacker.Name} counts as +{attackerBonus} wounds in melee (Fear).");
            if (defenderBonus > 0f)
                GameContext.Log($"{defender.Name} counts as +{defenderBonus} wounds in melee (Fear).");
            // #414: Star Quest has no Melee Results step. Its Morale section has one trigger only - "at
            // the end of an activation where wounds leave a unit with half or less of its total size or
            // Tou value" - and no "compare wounds, the loser tests". The engine was running Grimdark
            // Future's who-won rule in every Star Quest melee, which is a rule Star Quest does not have,
            // and it is also why GFSQ 2.1.0 words Fear(X) as "+X wounds when checking IF THEY MUST TAKE A
            // MORALE TEST (must deal at least one wound)" where every other book says "when checking who
            // won": in Star Quest there is nothing called who-won for it to attach to.
            //
            // So in Star Quest both units take the ordinary half-strength test instead, and each one's
            // check counts its OPPONENT's Fear - Fear reads "enemies count as having taken +X wounds", so
            // the bearer's rating inflates what the enemy is treated as having suffered. The "must deal at
            // least one wound" clause needs no code: WoundsLeftUnitAtHalfStrength already returns false
            // for a unit that took no real wounds, whatever the counted extras.
            //
            // The activating unit tests first. The book does not order them, and this is its activation.
            if (!CoreRulesProfile.For(GameContext).MeleeResultsDecideMorale)
            {
                // #442: a melee cause, and each unit is asked for its OWN charge-launch distance - whoever
                // charged gets 0, because the Hold the Line family's band is "charged from over 6" away"
                // and the charger was not charged.
                await MoraleUtilities.ResolveWoundDrivenMorale(GameContext, context.AttackingUnit,
                    context.AttackerRemainingWoundsAtStart, EMoraleTrigger.Melee,
                    context.ChargeLaunchDistanceAgainst(attacker), extraWoundsCounted: defenderBonus);
                await MoraleUtilities.ResolveWoundDrivenMorale(GameContext, context.DefendingUnit,
                    context.DefenderRemainingWoundsAtStart, EMoraleTrigger.Melee,
                    context.ChargeLaunchDistanceAgainst(defender), extraWoundsCounted: attackerBonus);

                // No winner is recorded: nothing downstream of this stage reads one on this path, and a
                // Tie result would be a lie about a comparison the rules never make.
                context.AddResult(new DetermineMeleeWinnerResults(
                    DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
                await OnDoesntNeedRollToDecide.Activate(context);
                return;
            }

            attackerWoundsDealt += attackerBonus;
            defenderWoundsDealt += defenderBonus;

            if (attackerWoundsDealt == defenderWoundsDealt)
            {
                GameContext.Log($"Attackers and defenders tied: {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                await AnnounceResult($"The melee is a tie - {attackerWoundsDealt:0.#} wounds each.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.Tie));
                await OnDoesntNeedRollToDecide.Activate(context);
            }
            else if (attackerWoundsDealt > defenderWoundsDealt)
            {
                GameContext.Log($"Attackers won melee {attackerWoundsDealt} vs. {defenderWoundsDealt}.");
                await AnnounceResult($"Melee won by {attacker.Name}, {attackerWoundsDealt:0.#} wounds to {defenderWoundsDealt:0.#}.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.AttackerWon));
                FDG.Missions.MissionCardEvents.MeleeWon(GameContext, attacker, attackerWoundsDealt, defenderWoundsDealt);
                await OnNeedsRollToDecide.Activate(context);
            }
            else
            {
                GameContext.Log($"Defenders won melee {defenderWoundsDealt} vs. {attackerWoundsDealt}.");
                await AnnounceResult($"Melee won by {defender.Name}, {defenderWoundsDealt:0.#} wounds to {attackerWoundsDealt:0.#}.");
                context.AddResult(new DetermineMeleeWinnerResults(DetermineMeleeWinnerResults.EMeleeWinnerResult.DefenderWon));
                FDG.Missions.MissionCardEvents.MeleeWon(GameContext, defender, defenderWoundsDealt, attackerWoundsDealt);
                await OnNeedsRollToDecide.Activate(context);
            }
        }

        // Who won, by name, in the Battle Report. The log line above says "Attackers"/"Defenders", which a
        // Counter swap turns upside down (the charged unit is the "attacker" then) and which never reached
        // the player anyway - a lost melee was only visible as a Morale test (look pass, 2026-11-06).
        private Task AnnounceResult(string text) => GameContext.Announce(text, tier: EBannerTier.Toast);

        // Sum the extra melee wounds (Fear) the actor contributes to its own who-won total. The actor
        // sits in the Actor seat (Fear's default), so only its rules fire; the opponent's Fear is summed
        // by the symmetric call. ExtraMeleeWoundCount arrives already resolved to an int.
        //
        // #175: the unit's LIVING models ride along (AnyOwner), the same shape every other model-aware
        // dispatch site uses. Fear(X)'s rulebook text is per-MODEL ("this model counts as having dealt
        // +X wounds"), and a joined hero's rules live on the hero MODEL (#006 slice F) — without the
        // models list this site collected unit-static rules only, so a hero-only Fear contributed
        // nothing at all. (X) rules are exempt from the no-stack dedup, so a host unit's Fear and a
        // joined hero's Fear are two sources and sum; the unit-level rule still counts once, not once
        // per model carrying it. A hero killed during this melee is not among the living and adds
        // nothing, matching every other living-models site.
        private float SumExtraMeleeWounds(MeleeResolutionContext resolution, IUnit actor)
        {
            IReadOnlyList<RuleOperation> operations = GameContext.RuleEvaluator.EvaluateAll(
                resolution, RuleParticipant.Actor(actor, models: HeroStatRules.LivingModels(actor)));

            float extra = 0f;
            foreach (RuleOperation operation in operations)
            {
                if (operation is RuleOperation.ExtraMeleeWoundCount fear) extra += fear.Amount;
            }
            return extra;
        }
    }

    public readonly struct DetermineMeleeWinnerResults
    {
        public readonly EMeleeWinnerResult Winner;

        public DetermineMeleeWinnerResults(EMeleeWinnerResult result)
        {
            Winner = result;
        }

        public enum EMeleeWinnerResult
        {
            AttackerWon,
            DefenderWon,
            Tie
        }
    }
}
