using FDG.Presentation.Beats;

namespace FDG.Stages
{
    /// <summary>
    /// Ends a Horde Mode mission. Horde has no objective-count winner: the defenders either held out
    /// through round twelve or were overrun, and the score is points earned x waves finished.
    /// Points come from the end-of-wave objective rewards (see HordeRoundHooks).
    /// </summary>
    public class HordeVictoryStage : StageBase<IGameContext>
    {
        public HordeVictoryStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            var table = GameContext.TableState;
            var players = table.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
            IReadOnlyList<PlayerObjectiveScore> finalScores = table.Progress.Scores;

            // Progress.RoundCount is the last round played when the main phase hands off here (the
            // standard victory stage reads it as "rounds played" too).
            int roundsFinished = Math.Max(0, table.Progress.RoundCount ?? 0);
            bool defendersDown = FDG.StateMachine.HordeRules.AllDefendersDownOrShaken(table);
            bool objectivesGone = !table.Objectives.Objects.Any();
            bool survived = roundsFinished >= GameWideConstants.HORDE_NUMBER_OF_ROUNDS
                && !defendersDown && !objectivesGone;

            int? defendingTeam = FDG.StateMachine.HordeRules.DefendingTeamNumber(players);
            var defenders = players.Where(p => p.TeamNumber == defendingTeam).ToList();
            var enemies = players.Where(p => p.TeamNumber != defendingTeam).ToList();

            int waves = FDG.StateMachine.HordeRules.WavesFinished(roundsFinished, survived);
            int pointsEarned = table.Progress.HordePointsEarned ?? 0;
            int highScore = pointsEarned * waves;

            string how = survived
                ? "The defenders held out!"
                : objectivesGone && !defendersDown
                    ? $"Every objective was destroyed in round {roundsFinished}."
                    : $"The defenders were overrun in round {roundsFinished}.";
            string message = $"{how} {pointsEarned} points x {waves} wave{(waves == 1 ? "" : "s")} = high score {highScore}.";

            GameContext.Log($"Horde Mode ended after round {roundsFinished}: {message}");

            var winners = survived ? defenders : enemies;
            GameResult result;
            if (winners.Count == 0)
            {
                result = GameResult.ForTie(finalScores, roundsFinished);
            }
            else
            {
                result = GameResult.ForWinWithMessage(
                    winners.Select(p => p.PlayerID).ToList(),
                    winners.Select(p => p.Name).ToList(),
                    finalScores, roundsFinished, message);
            }

            await context.Announce(result.Message, new TextColor(255, 215, 0, 255), EBannerTier.Headline);
            GameContext.NotifyGameCompleted(result);
        }

        public override void Exit()
        {
        }
    }
}
