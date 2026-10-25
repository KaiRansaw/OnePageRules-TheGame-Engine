

namespace FDG.Stages
{
    public class ReconcileNewRoundStage : StageBase<IMainPhaseContext>
    {
        public const string TO_START_EXTRA_ACTIONS_TRANSITION = "ReconcileToStartExtraActions";

        public StageBinding ToStartExtraActions;

        public ReconcileNewRoundStage(IGameContext gameContext, IStateMachineLayer<IMainPhaseContext> parent) : base(gameContext, parent)
        {
            ToStartExtraActions = new StageBinding(this);
        }

        public override async Task Enter(IMainPhaseContext context)
        {
            context.LogDebug($"Entered {nameof(ReconcileNewRoundStage)}.");

            // #083 — break the synchronous-continuation chain once per round. With piped/AI input every
            // stage transition completes synchronously, so without a yield the entire game would run as one
            // ever-deepening continuation stack. Yielding at the round boundary caps stack growth.
            await Task.Yield();

            //TODO: Not sure we actually need to do anything here, if we call OnEndOfRound elsewhere.

            // Game Master "jump to round N": the round that is starting becomes N, before its start-of-round
            // steps run - so jumping to round 4 of a Horde game sets a priority and deploys wave 2.
            GameContext.GameMaster?.OnRoundStart();
            if (GameContext.GameMaster?.ConsumeJumpToRound() is int jumpTo)
            {
                int limit = FDG.StateMachine.HordeRules.RoundLimit(GameContext.Settings.GameMode);
                context.SetRoundCount(Math.Min(jumpTo, limit));
                context.Log($"Game Master: this is now round {context.RoundCount}.");
            }

            // Game-mode round-start steps (Horde Mode: priority objective, enemy waves). None in the
            // standard game.
            if ((Parent as MainPhaseRoundStage)?.RoundHooks is IRoundHooks roundHooks)
            {
                await roundHooks.OnRoundStart(context);
            }

            await ToStartExtraActions.Activate(context);
        }

    }
}