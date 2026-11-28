namespace FDG.Stages
{
    public class DetermineNextObjectivePlacerStage : StageBase<IObjectivePlacementTurnContext>
    {
        public StageBinding OnPlacerDetermined;
        public StageBinding OnAllMarkersPlaced;

        public DetermineNextObjectivePlacerStage(IGameContext gameContext,
            IStateMachineLayer<IObjectivePlacementTurnContext> parent)
            : base(gameContext, parent)
        {
            OnPlacerDetermined = new StageBinding(this);
            OnAllMarkersPlaced = new StageBinding(this);
        }

        public override async Task Enter(IObjectivePlacementTurnContext context)
        {
            context.LogDebug($"Entered {nameof(DetermineNextObjectivePlacerStage)}.");

            if (!context.HasRemainingMarkers())
            {
                context.Log($"All {context.TotalMarkers} objective markers placed.");
                await OnAllMarkersPlaced.Activate(context);
                return;
            }

            // First placement uses the cursor's initial state (the roll-off winner's team, that team's first player).
            if (!context.HasStarted)
            {
                context.HasStarted = true;
                var team = context.Cursor.GetCurrentTeam();
                var player = context.Cursor.GetCurrentPlayerID();
                context.Log(PlacerLine(context.GameContext, team.TeamNumber, player, context.MarkersPlaced + 1, context.TotalMarkers));
                await OnPlacerDetermined.Activate(context);
                return;
            }

            // Otherwise round-robin. Every team always has "remaining work" until total count is reached —
            // the bookkeeping is on the per-marker counter, not on per-team quotas.
            bool advanced = context.Cursor.TryAdvance(
                teamHasRemainingWork: team => context.HasRemainingMarkers()
                    && (context.OnlyTeam == null || team.TeamNumber == context.OnlyTeam.TeamNumber),
                playerHasRemainingWork: _ => context.HasRemainingMarkers(),
                out ITeam? nextTeam,
                out PlayerID? nextPlayerID);

            if (!advanced || nextTeam == null || nextPlayerID == null)
            {
                // Defensive — HasRemainingMarkers already returned true so this shouldn't happen.
                throw new InvalidOperationException("Cursor failed to advance despite markers remaining.");
            }

            context.Log(PlacerLine(context.GameContext, nextTeam.TeamNumber, nextPlayerID.Value, context.MarkersPlaced + 1, context.TotalMarkers));
            await OnPlacerDetermined.Activate(context);
        }

        /// <summary>"Team 1 (Kai) will place objective 3 of 6." - the player's name, not their internal id (bug hunt
        /// 2026-11-28: the line printed the id).</summary>
        internal static string PlacerLine(IGameContext game, int teamNumber, PlayerID player, int marker, int total) =>
            $"Team {teamNumber} ({game.GetPlayerName(player)}) will place objective {marker} of {total}.";
    }
}
