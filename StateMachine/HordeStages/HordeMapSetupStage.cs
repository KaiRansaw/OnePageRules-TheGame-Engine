using FDG.Presentation.Beats;

namespace FDG.Stages
{
    /// <summary>
    /// Map setup for Horde Mode: the standard terrain placement, then the five fixed objective markers
    /// (see <see cref="HordeObjectiveSetupStage"/>) in place of the rolled count and alternating placement.
    /// </summary>
    public class HordeMapSetupStage : ParentStage<IGameContext, IMapSetupContext>
    {
        public StageBinding ToDeployment;

        public HordeMapSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            await context.Announce("Horde Mode - Map Setup", tier: EBannerTier.Headline);
            await base.Enter(context);
        }

        protected override IMapSetupContext GetNewChildContext(IGameContext contextSelf)
            => new MapSetupContext(contextSelf);

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMapSetupContext> startingChild)
        {
            ToDeployment = new StageBinding(this);

            var dictionary = new TransitionSetBuilder(this)
                .AddChild(new RollForFirstTerrainPlacementStage(GameContext, this), out var rollForFirstTerrain)
                .AddChild(new PlaceTerrainStage(GameContext, this), out var placeTerrain)
                .AddChild(new HordeObjectiveSetupStage(GameContext, this), out var hordeObjectives)
                .AddSibling(nameof(ToDeployment), ToDeployment, out string toDeploymentEvent)
                .Build();

            startingChild = rollForFirstTerrain;

            rollForFirstTerrain.OnRollComplete.Bind(placeTerrain);
            placeTerrain.OnTerrainPlaced.Bind(hordeObjectives);
            hordeObjectives.OnObjectivesPlaced.Bind(toDeploymentEvent);

            return dictionary;
        }
    }
}
