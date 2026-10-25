namespace FDG.Stages
{
    /// <summary>
    /// Optional per-game-mode extra steps around each main-phase round. The standard game has none
    /// (<see cref="MainPhaseRoundStage.RoundHooks"/> stays null and every round runs exactly as before);
    /// Horde Mode uses them for priority objectives, enemy waves, objective damage and its end conditions.
    /// </summary>
    public interface IRoundHooks
    {
        /// <summary>Runs at the very start of each round, before the round banner and reserve arrivals.</summary>
        Task OnRoundStart(IMainPhaseContext context);

        /// <summary>
        /// Runs at the end of each round, right after objective control is settled and before the
        /// round-end token sweep. Return true to end the game after this round.
        /// </summary>
        Task<bool> OnRoundEnd(IMainPhaseContext context, int roundJustFinished);
    }
}
