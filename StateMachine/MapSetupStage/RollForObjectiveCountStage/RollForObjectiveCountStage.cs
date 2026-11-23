using FDG.Presentation.Beats;

namespace FDG.Stages
{
    public class RollForObjectiveCountStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnRollComplete;

        public RollForObjectiveCountStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnRollComplete = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.LogDebug($"Entered {nameof(RollForObjectiveCountStage)}.");

            // Extra Missions (Advanced Rulebooks 3.6.0): the mission is settled first (a Random one rolled), and most
            // fix their own number of markers.
            await FDG.Missions.ExtraMissions.Begin(context.GameContext);
            if (FDG.Missions.ExtraMissions.MarkerCount(context.GameContext) is int fixedCount)
            {
                context.Log(fixedCount == 0
                    ? "Each side sets up its own markers once the deployment zones are chosen."
                    : $"The mission sets up {fixedCount} objective{(fixedCount == 1 ? "" : "s")}; no objective roll.");
                if (fixedCount > 0) await context.Announce($"{fixedCount} Objective{(fixedCount == 1 ? "" : "s")}", new TextColor(255, 210, 80, 255));
                context.SetObjectiveCount(fixedCount);
                await OnRollComplete.Activate(context);
                return;
            }

            // Map maker: a custom map with its own objectives skips the roll - its markers ARE the count.
            int mapObjectives = CustomMapRules.FixedObjectives(context.GameContext.Settings).Count;
            if (mapObjectives > 0)
            {
                context.Log($"The map has {mapObjectives} objectives; no objective roll.");
                await context.Announce($"{mapObjectives} Objectives", new TextColor(255, 210, 80, 255));
                context.SetObjectiveCount(mapObjectives);
                await OnRollComplete.Activate(context);
                return;
            }

            // Mission Cards: "Place 6 objective markers." - no roll.
            if (FDG.Missions.MissionCardGame.IsOn(context.GameContext))
            {
                int six = FDG.Missions.MissionCardGame.ObjectiveCount;
                context.Log($"Mission Cards: {six} objectives, no objective roll.");
                await context.Announce($"{six} Objectives", new TextColor(255, 210, 80, 255));
                context.SetObjectiveCount(six);
                await OnRollComplete.Activate(context);
                return;
            }

            // D3+2 gives 3–5 objectives. Roll a real, decisive D3 (RollDecisive resolves one concrete
            // face even under the probabilistic roller; a plain Roll would spread to an expected value).
            // Rolling a genuine 3-sided die — rather than a d6 then halving — lets the front-end show a
            // D3 and keeps the "+2" legible. #090.
            IDiceResults rollResult = context.GameContext.DiceRoller.RollDecisive(3);
            int roll = rollResult.SideMin;
            for (int v = rollResult.SideMin; v <= rollResult.SideMax; v++)
                if (rollResult.At(v) > 0f) { roll = v; break; }
            // Attack & Defend's Ambush sets up "a total of D3" - no +2.
            int bonus = FDG.Missions.ExtraMissions.D3Bonus(context.GameContext);
            int objectiveCount = roll + bonus;

            context.Log($"Rolled D3={roll} (+{bonus}) - {objectiveCount} objectives will be placed.");

            // Show the D3 (the rolled face lights up), then a big banner with the resulting count. The
            // label carries the formula so the +2 between the rolled face and the count is understood.
            // #289: FromDecisive - the D3 above resolved to one concrete face in either roller mode, so
            // the front-end draws the die rather than a probability bar.
            await context.GameContext.Presenter.Present(
                DiceRolledBeat.FromDecisive(rollResult, roll, bonus == 2 ? "Roll for Objectives (D3 + 2)" : "Roll for Objectives (D3)",
                    $"{objectiveCount} objectives"));
            await context.Announce($"{objectiveCount} Objectives", new TextColor(255, 210, 80, 255));

            context.SetObjectiveCount(objectiveCount);
            await OnRollComplete.Activate(context);
        }
    }
}
