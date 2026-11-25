using System.Linq;
using System.Collections.Generic;


namespace FDG.Stages
{
    public class DetermineNextDeployPlayerStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;
        public StageBinding OnFinishedDeployingAllUnits;

        public DetermineNextDeployPlayerStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
            OnFinishedDeployingAllUnits = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entering Determine Next Deploy Player stage.");

            // Attack & Defend: the mission says who deploys what, in order.
            if (context.MissionPlan is { } plan)
            {
                context.HasStarted = true;
                PlayerID? next = plan.NextPlayer(context);
                if (next == null)
                {
                    plan.Finish(GameContext, context);
                    context.Log("All players have deployed.");
                    await OnFinishedDeployingAllUnits.Activate(context);
                    return;
                }
                context.Cursor.PointAt(next.Value);
                await OnFinish.Activate(context);
                return;
            }

            // First time through we keep the cursor's initial state (the roll-off winner) — but only
            // if that player actually has a unit to deploy. If their whole army was set aside (e.g.
            // every unit is Scout) the pool is empty, so we fall through to the normal skip-empty
            // advance rather than handing ChooseUnitToDeployStage an empty pool (which used to throw).
            if (context.HasStarted == false)
            {
                // Command Points: the playstyle and each player's doctrine are settled "before the game begins".
                await CommandPoints.Settle(GameContext);
                await PoliticalIntrigue.Settle(GameContext);
                // Advanced Casting - Spell Books: "before the game begins", each caster's book.
                await AdvancedCasting.Settle(GameContext);
                // Advanced Casting - Living Spells: each army's spell from each category.
                await LivingSpells.Settle(GameContext);
                // Advanced Casting - Relic Wargear: each Hero's item; a Ranger's Standard Scout then waits for the Scout pass.
                if (await RelicWargear.Settle(GameContext)) context.DeferNewScouts();
                // Fog of War - Escalating Battles: each player picks the detachment that deploys now.
                await FogOfWar.OpeningDetachments(GameContext, context);
                context.HasStarted = true;
                if (context.DoesPlayerHaveRemainingDeployments(context.GetCurrentDeployingPlayerID()))
                {
                    await DelayOrGo(context);
                    return;
                }
            }

            bool advanced = context.Cursor.TryAdvance(
                teamHasRemainingWork: context.DoesTeamHaveRemainingDeployments,
                playerHasRemainingWork: context.DoesPlayerHaveRemainingDeployments,
                out _, out _);

            if (!advanced)
            {
                context.Log("All players have deployed.");
                await OnFinishedDeployingAllUnits.Activate(context);
                return;
            }

            await DelayOrGo(context);
        }

        /// <summary>Command Points - Delayed Deployment: the player whose turn it is may pass it to an opponent who still has
        /// units to place. The turn then moves on once more, and the alternation carries on from there.</summary>
        private async Task DelayOrGo(IDeploymentTurnContext context)
        {
            PlayerID current = context.GetCurrentDeployingPlayerID();
            ITeam? mine = GameContext.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(current));
            List<PlayerID> others = GameContext.TableState.Teams.Objects.Where(t => t != mine)
                .SelectMany(t => t.Players).Where(context.DoesPlayerHaveRemainingDeployments).ToList();
            if (await CommandPoints.DelayDeployment(GameContext, current, others)
                && context.Cursor.TryAdvance(
                    teamHasRemainingWork: context.DoesTeamHaveRemainingDeployments,
                    playerHasRemainingWork: context.DoesPlayerHaveRemainingDeployments,
                    out _, out _))
                context.Log($"{GameContext.GetPlayerName(current)} delays - {GameContext.GetPlayerName(context.GetCurrentDeployingPlayerID())} places a unit first.");
            await OnFinish.Activate(context);
        }
    }
}

