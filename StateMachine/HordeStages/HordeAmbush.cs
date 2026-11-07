using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Enemy Ambush in the four Horde Mode books (GF, GF: Firefight, AoF, AoF: Skirmish 3.6.0). The Horde
    /// books say the enemy army is controlled "by the AI (using the Solo &amp; Co-Op rules)", and those say:
    /// "AI units with Ambush must always be kept in reserve, and must always deploy at the start of the
    /// second round." Where they deploy is not said; Kai's ruling (2026-11-06) is a random table corner, the
    /// same roll as every wave unit. The Quest Horde games have their own Ambush handling
    /// (<see cref="QuestChallenges.HoldForAmbush"/>).
    /// </summary>
    public static class HordeAmbush
    {
        public const int ArrivalRound = 2;

        /// <summary>At set-up: an enemy unit with Ambush leaves the table and waits. True if it was held back.</summary>
        public static bool HoldBack(IGameContext gameContext, UnitData unit)
        {
            if (!QuestChallenges.HasRule(unit, "Ambush")) return false;
            foreach (IModel model in unit.Models)
                if (model is ModelData data) data.SetPosition(new Position());
            ReserveRules.PlaceInReserve(unit);
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.PendingAmbushArrival));
            gameContext.Log($"  {unit.Name} has Ambush - it waits in reserve and arrives at the start of round {ArrivalRound}.");
            return true;
        }

        /// <summary>The units still waiting.</summary>
        public static List<UnitData> Waiting(IGameContext gameContext)
        {
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(gameContext.TableState);
            return HordePlacement.AllUnits(gameContext)
                .Where(u => !defenders.Contains(u.PlayerID) && u.GetIsAlive() && ReserveRules.IsInReserve(u)
                    && u.Tokens.HasToken(TokenType.PendingAmbushArrival))
                .ToList();
        }

        /// <summary>Start of round 2: every waiting ambusher arrives at a random corner.</summary>
        public static async Task Arrive(IGameContext gameContext)
        {
            List<UnitData> waiting = Waiting(gameContext);
            if (waiting.Count == 0) return;

            await gameContext.Announce("Ambush!", new TextColor(255, 120, 80, 255), EBannerTier.Headline);
            var arrived = new List<string>();
            foreach (UnitData unit in waiting)
            {
                unit.Tokens.RemoveTokens(TokenType.PendingAmbushArrival);
                int corner = await HordePlacement.PlaceAtRandomCorner(gameContext, unit, "ambush");
                if (unit.GetIsOnBattlefield()) arrived.Add($"{unit.Name} ({StateMachine.HordeRules.CornerName(corner)})");
            }
            if (arrived.Count > 0)
                await gameContext.Announce($"Arrived: {string.Join(", ", arrived)}", new TextColor(255, 150, 110, 255),
                    EBannerTier.Notice);
        }
    }
}
