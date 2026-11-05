using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #456 - Fast-Play's Unit Leaders, settled before the game: "Before the game begins, you must clearly
    /// define a single model within each unit to be its leader, and which will remain the unit's leader until
    /// the end of the game."
    ///
    /// <para>
    /// Falls straight through unless a Fast-Play rule is on in a game whose books have it
    /// (<see cref="FastPlay.UsesLeaders"/>). Otherwise every unit gets the game's suggested leader
    /// (<see cref="FastPlay.AutoPick"/>) and each player is asked once to confirm or change theirs - Kai's
    /// call, 2026-11-05: "Game picks, I can change it".
    /// </para>
    ///
    /// <para>
    /// Runs on every machine in lockstep, like <see cref="PickCommandGroupUnitsStage"/>: the suggestions are
    /// computed from the models alone so every machine reaches the same ones, the requests go out in army
    /// order, and every machine applies the same replies. Placed before units are deployed, which is when
    /// "before the game begins" puts it.
    /// </para>
    /// </summary>
    public class PickUnitLeadersStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToDeployAllUnits;

        public PickUnitLeadersStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToDeployAllUnits = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            if (!FastPlay.UsesLeaders(context.GameContext.Settings))
            {
                context.LogDebug("Unit leaders: no Fast-Play rule is in use - nothing to pick.");
                await ToDeployAllUnits.Activate(context);
                return;
            }

            // Army order, then each army's own unit order - identical on every machine.
            List<DataBinding<UnitData>> allUnits = context.TableState()
                .Armies.Objects
                .OfType<ArmyData>()
                .SelectMany(army => army.UnitBindings)
                .ToList();

            foreach (DataBinding<UnitData> binding in allUnits)
                binding.GetValue().LeaderModelId = FastPlay.AutoPick(binding.GetValue());
            context.Log($"Fast-Play: every unit has a leader ({allUnits.Count} unit(s)). A unit is measured "
                        + "from its leader, and the leader is the last of its models to die.");

            foreach (PlayerID player in allUnits.Select(u => u.GetValue().PlayerID).Distinct().ToList())
                await AskPlayer(context, player, allUnits.Where(u => u.GetValue().PlayerID == player).ToList());

            await ToDeployAllUnits.Activate(context);
        }

        private static async Task AskPlayer(IDeploymentContext context, PlayerID player, List<DataBinding<UnitData>> units)
        {
            var entries = new List<PickUnitLeadersRequest.UnitEntry>();
            foreach (DataBinding<UnitData> binding in units)
            {
                UnitData unit = binding.GetValue();
                if (unit.LeaderModelId is not { } suggested) continue;
                var options = LeaderCandidates(unit)
                    .Select(m => new PickUnitLeadersRequest.ModelOption(m.ID, FastPlay.Describe(unit, m)))
                    .ToList();
                // A unit of one has nothing to choose - its only model leads it.
                if (options.Count < 2) continue;
                entries.Add(new PickUnitLeadersRequest.UnitEntry(binding, unit.Name, suggested, options));
            }

            if (entries.Count == 0) return;

            var request = new PickUnitLeadersRequest(player, entries);
            CancellableResult<List<PickUnitLeadersRequest.LeaderPick>> reply = await context.GameContext.PlayerRequester
                .RequestDecision<PickUnitLeadersRequest, CancellableResult<List<PickUnitLeadersRequest.LeaderPick>>>(request);

            // A cancel keeps the suggestions - leaders are compulsory, so "none" is not an answer.
            if (reply is not Selected<List<PickUnitLeadersRequest.LeaderPick>> selected) return;

            int changed = 0;
            foreach (PickUnitLeadersRequest.LeaderPick pick in selected.Value)
            {
                PickUnitLeadersRequest.UnitEntry? entry = entries.FirstOrDefault(e => e.Unit.GetValue().ID == pick.Unit.GetValue().ID);
                if (entry == null) continue;
                // Only a model the request offered - a reply naming anything else is ignored, never trusted.
                if (!entry.Models.Any(o => o.Model.Equals(pick.Model))) continue;
                UnitData unit = entry.Unit.GetValue();
                if (unit.LeaderModelId is { } current && current.Equals(pick.Model)) continue;
                unit.LeaderModelId = pick.Model;
                changed++;
            }

            if (changed > 0) context.Log($"{changed} unit leader(s) changed from the suggestion.");
        }

        /// <summary>The models that may lead a unit: its living models, not a joined Hero.</summary>
        internal static IEnumerable<IModel> LeaderCandidates(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive() && !(unit.JoinedHeroModelId is { } hero && m.ID.Equals(hero)));
    }
}
