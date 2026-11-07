using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// Horde Mode deployment (OPR v3.6.0): the defenders place every unit within 9" of the table centre,
    /// then each enemy unit rolls a die and is placed within 9" of that table corner (1-4), or of a
    /// corner the defenders pick (5-6). Defenders take the first activation of round 1.
    /// <para>
    /// Ambush and Scout (Kai's rulings, 2026-11-06, claude/house-rules.md): the defenders deploy as the book
    /// says, "all their units within 9\" of the table centre" - their Ambush and Scout do nothing at set-up.
    /// Enemy units with Ambush follow the Solo &amp; Co-Op rules the Horde books hand the AI to ("must always
    /// be kept in reserve, and must always deploy at the start of the second round"): they are held back here
    /// and arrive at round 2 at a random corner, like any wave unit (<see cref="HordeRoundHooks"/>). Enemy
    /// Scout only orders deployment ("deployed after all other units"), which corner placement makes moot.
    /// </para>
    /// </summary>
    public class HordeDeploymentStage : StageBase<IGameContext>
    {
        public StageBinding ToMain;

        public HordeDeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            await context.Announce("Deployment", tier: EBannerTier.Headline);

            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(GameContext.TableState);
            List<UnitData> units = HordePlacement.AllUnits(GameContext);

            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;

            GameContext.Log($"Horde Mode: defenders deploy within {FDG.StateMachine.HordeRules.DeploymentRadiusInches:0}\" of the table centre.");
            await DeployDefenders(units.Where(u => defenders.Contains(u.PlayerID)).ToList(),
                radius => new CircularZone(w / 2f, h / 2f, radius));

            GameContext.Log("Horde Mode: wave 1 arrives at random table corners.");
            foreach (UnitData unit in units.Where(u => !defenders.Contains(u.PlayerID)))
            {
                if (HordeAmbush.HoldBack(GameContext, unit)) continue;
                await HordePlacement.PlaceAtRandomCorner(GameContext, unit, "wave 1");
            }

            await context.Announce("Wave 1 arrives!", new TextColor(255, 120, 80, 255), EBannerTier.Headline);

            // Defending team activates first in round 1; every other team follows in team-number order.
            List<ITeam> teams = GameContext.TableState.Teams.Objects.ToList();
            List<ITeam> order = teams
                .OrderBy(t => t.Players.Any(defenders.Contains) ? 0 : 1)
                .ThenBy(t => t.TeamNumber)
                .ToList();
            context.SetFirstDeploymentRollOrder(order);

            await ToMain.Activate(context);
        }

        private sealed record PlacedUnit(UnitData Unit, List<PlacedObjectEntry<ModelData>> Placements);

        /// <summary>
        /// Places each defending unit in turn, with deployment-level Undo / Redo: "Undo Last Unit" picks the
        /// previously placed unit back up and asks for it again; "Redo Unit" puts an undone unit back where
        /// it was. Undo only reaches back to the same player's units, so in a co-op game nobody can pick up
        /// an ally's (or a bot's) placement. A fresh placement clears the redo history, like any editor.
        /// </summary>
        private async Task DeployDefenders(List<UnitData> defenderUnits, Func<float, IBoundedZone> makeZone)
        {
            string taskName = TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName;
            var placed = new List<PlacedUnit>();
            var redo = new Stack<PlacedUnit>();

            int i = 0;
            while (i < defenderUnits.Count)
            {
                UnitData unit = defenderUnits[i];
                bool canUndo = placed.Count > 0 && placed[^1].Unit.PlayerID.Equals(unit.PlayerID);
                bool canRedo = redo.Count > 0 && ReferenceEquals(redo.Peek().Unit, unit);

                // Offer the area the unit can actually fit in (9", or wider when the centre is packed).
                float radius = await HordePlacement.RoomyRadius(GameContext, unit, makeZone, taskName);
                string displayName = radius > HordePlacement.CrowdedRadiiInches[0]
                    ? $"Deploying {unit.Name} (no room within {FDG.StateMachine.HordeRules.DeploymentRadiusInches:0}\" - within {radius:0}\" allowed)"
                    : $"Deploying {unit.Name}";
                if (radius > HordePlacement.CrowdedRadiiInches[0])
                    GameContext.Log($"  No room for {unit.Name} within {FDG.StateMachine.HordeRules.DeploymentRadiusInches:0}\" of the centre - it may deploy within {radius:0}\".");

                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, taskName, makeZone(radius),
                    unit.ModelBindings, displayName: displayName, canUndoUnit: canUndo, canRedoUnit: canRedo);

                CancellableResult<List<PlacedObjectEntry<ModelData>>> result = await GameContext.PlayerRequester
                    .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);

                switch (result)
                {
                    case Selected<List<PlacedObjectEntry<ModelData>>> selected:
                    {
                        List<PlacedObjectEntry<ModelData>> placements =
                            await PlacementCommitGuard.EnsureClear(GameContext, request, selected.Value);
                        HordePlacement.Commit(unit, placements);
                        placed.Add(new PlacedUnit(unit, placements));
                        redo.Clear();
                        i++;
                        break;
                    }

                    case UndoUnitRequested<List<PlacedObjectEntry<ModelData>>> when canUndo:
                    {
                        PlacedUnit last = placed[^1];
                        placed.RemoveAt(placed.Count - 1);
                        HordePlacement.Unplace(last.Unit);
                        redo.Push(last);
                        i = defenderUnits.IndexOf(last.Unit);
                        GameContext.Log($"  Undo: {last.Unit.Name} picked back up - place it again.");
                        break;
                    }

                    case RedoUnitRequested<List<PlacedObjectEntry<ModelData>>> when canRedo:
                    {
                        PlacedUnit again = redo.Pop();
                        HordePlacement.Commit(again.Unit, again.Placements);
                        placed.Add(again);
                        GameContext.Log($"  Redo: {again.Unit.Name} put back where it was.");
                        i++;
                        break;
                    }

                    default:
                        // A Cancelled (or an undo/redo that was not on offer) just asks for the same unit again.
                        break;
                }
            }
        }
    }
}
