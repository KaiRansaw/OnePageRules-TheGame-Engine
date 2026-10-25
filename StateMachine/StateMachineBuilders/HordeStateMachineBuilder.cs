using FDG;
using FDG.Stages;

namespace FDG.StateMachine.StateMachineBuilders
{
    /// <summary>
    /// State machine for <see cref="EGameMode.Horde"/> (OPR Horde Mode v3.5.0, Grimdark Future).
    /// Specialised slice by slice. Done: Horde map setup (five fixed objectives), Horde deployment
    /// (defenders at the centre, enemies at random corners), a 12-round main phase with priority
    /// objectives, enemy waves, objective damage and the Horde end conditions, and a Horde result stage.
    /// To come: XP and traits, point rewards, upgrades and recruiting, the high score.
    /// </summary>
    public class HordeStateMachineBuilder : IStateMachineBuilder<IGameContext>
    {
        public Dictionary<string, StageBase<IGameContext>> BuildStateMachine(StateMachine<IGameContext> stateMachine,
            IGameContext gameContext, out StageBase<IGameContext> startingStage)
        {
            HordeMapSetupStage mapSetupStage = new HordeMapSetupStage(gameContext, stateMachine);
            HordeDeploymentStage deploymentStage = new HordeDeploymentStage(gameContext, stateMachine);
            MainPhaseRoundStage mainPhaseRoundStage = new MainPhaseRoundStage(gameContext, stateMachine);

            // Horde's per-round steps: priority objective, enemy waves, objective damage, end checks.
            mainPhaseRoundStage.RoundHooks = new HordeRoundHooks(gameContext);
            HordeVictoryStage victoryCalculationStage = new HordeVictoryStage(gameContext, stateMachine);

            mapSetupStage.ToDeployment.Bind(deploymentStage);
            deploymentStage.ToMain.Bind(mainPhaseRoundStage);
            mainPhaseRoundStage.ToVictoryCalculation.Bind(victoryCalculationStage);

            Dictionary<string, StageBase<IGameContext>> bindings = new Dictionary<string, StageBase<IGameContext>>()
            {
                {mapSetupStage.Name, mapSetupStage},
                {deploymentStage.Name, deploymentStage},
                {mainPhaseRoundStage.Name, mainPhaseRoundStage},
                {victoryCalculationStage.Name, victoryCalculationStage}
            };

            startingStage = mapSetupStage;

            return bindings;
        }
    }
}
