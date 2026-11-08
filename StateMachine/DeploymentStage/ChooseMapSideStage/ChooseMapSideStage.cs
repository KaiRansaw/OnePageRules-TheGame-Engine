

using FDG.BuiltInAssets;
using FDG.Data;
using FDG.SerializableVisuals.Materials;
using FDG.SerializableVisuals.Meshes;
using FDG.StageResolution.Requests;
using System.Numerics;

namespace FDG.Stages
{

    public class ChooseMapSideStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToRollForFirstDeployment;

        public ChooseMapSideStage(IGameContext gameContext, IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToRollForFirstDeployment = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            context.LogDebug("Entered Choose Map Side stage.");

            List<ITeam> teamOrderedByRoll = context.MapSideRollOrder;

            List<RectangularZone> zoneOptions = GetRectangularZones(teamOrderedByRoll.Count, context);

            List<DataBinding<RectangularZone>> zoneBindings = new List<DataBinding<RectangularZone>>(zoneOptions.Count);

            for(int i = 0; i < zoneOptions.Count; i++)
            {
                RectangularZone zone = zoneOptions[i];

                DataReference zoneReference = context.GameDataStore().Create(zone);
                DataBinding<RectangularZone> zoneBinding = context.GameDataStore().GetDataBinding<RectangularZone>(zoneReference);
                zoneBindings.Add(zoneBinding);
            }

            if(context.MapSideRollOrder == null)
            {
                throw new NullReferenceException();
            }

            
            Dictionary<ITeam, DataBinding<RectangularZone>> choices = new Dictionary<ITeam, DataBinding<RectangularZone>>(teamOrderedByRoll.Count);

            for(int i = 0; i < teamOrderedByRoll.Count; i++)
            {
                ITeam thisTeam = teamOrderedByRoll[i];

                //If there's only one option left, take that one.
                if(zoneBindings.Count == 1)
                {
                    context.Log("Auto-assigning deployment zone as only one is left to pick.");
                    choices.Add(thisTeam, zoneBindings.First());
                    zoneBindings.RemoveAt(0); //Still removing so that we can hit the next exception if the team count is malaligned.
                    continue;
                }
                else if (zoneBindings.Count == 0)
                {
                    throw new Exception("Somehow ran out of zones before we went through all the teams.");
                }

                //Have the first player on the team choose a zone.
                PlayerID firstTeamPlayer = thisTeam.Players.First();

                context.LogDebug($"Requesting player choose deployment zone.");
                ChooseDeploymentZoneRequest request = new ChooseDeploymentZoneRequest(firstTeamPlayer,
                    "Choosing a Deployment Zone", zoneBindings, choices.Values.ToList());

                DataBinding<RectangularZone> choice 
                    = await context.PlayerRequester().RequestDecision<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>
                        (request);

                zoneBindings.Remove(choice);

                choices.Add(thisTeam, choice);
            }

            context.SetDeploymentZones(choices);

            // #429: and into the store, so they outlive this stage. The DeploymentContext is thrown away
            // the moment deployment ends, so until now NOTHING in the game could say where anyone had
            // deployed from the first round onwards - which the campaign's Intimidator objective needs,
            // and which the Solo AI's three sections (drawn along the AI's own deployment edge) want too.
            var byTeam = new Dictionary<int, ZoneRect>();
            foreach ((ITeam team, DataBinding<RectangularZone> zone) in choices)
            {
                if (zone is { IsValid: true } binding && binding.GetValue() is { } rect)
                    byTeam[team.TeamNumber] = ZoneRect.From(rect);
            }
            if (byTeam.Count > 0) GameProgressUtilities.SetDeploymentZones(context.GameContext.GameDataStore, byTeam);

            await ToRollForFirstDeployment.Activate(context);
        }

        // RETIRED (work item 071, formerly numbered 052): dead debug probe — never called — that proved a stage could push
        // TempVisuals. The TempVisual system is superseded by the presentation-beat stream
        // (FDG.Presentation); a stage now narrates via context.Presenter.Present(beat). Kept
        // commented for reference rather than deleted. The rest of the TempVisual plumbing
        // (ITempVisualDrawer on the contexts/controllers, NetworkedTempVisualDrawer, TempVisualRelayer,
        // the TempVisual messages) is still wired but unused, pending the full interface sweep.
        /*
        private void TempTestVisuals(IDeploymentContext context)
        {
            //TODO: This doesn't belong here but I want to make sure we can make visuals happen from the stage machine.
            System.Diagnostics.Debug.WriteLine("Testing drawing something in ChooseMapSideStage.");
            var meshProvider = new BuiltInObjMeshProvider(BuiltInAssetHelper.SILLYMANMODEL_PATH);

            var materialProvider = new BasicMaterial();
            materialProvider.BaseColor = new Vector4(1, 1, 1, 1);

            Position position = new Position(10f, 25f);
            Quaternion rotation = new Quaternion(Vector3.UnitY, 0f);
            Vector3 scale = new Vector3(1, 1, 1);

            TempVisual tempVisual1 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual1);

            position = new Position(12f, 25f);

            TempVisual tempVisual2 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual2);

            position = new Position(14f, 25f);
            TempVisual tempVisual3 = new TempVisual(meshProvider, materialProvider, position, rotation, scale);
            context.GameContext.TempVisualDrawer.AddVisual(tempVisual3);
        }
        */


        private List<RectangularZone> GetRectangularZones(int teamCount, IDeploymentContext context)
        {
            if(teamCount <= 0)
            {
                throw new InvalidOperationException($"Can't have fewer than one team.");
            }

            // Deployment is picked in the lobby, never by the map (Kai, 2026-11-07: "deployment is ONLY
            // picked in the lobby"). Maps drawn before that decision can still carry deployment zones in
            // their file (TerrainLayoutFile.DeploymentZones); they are ignored here on purpose, so the
            // lobby's Deployment pick always wins. This used to hand a map with a zone per team its own
            // zones and silently override the pick.

            // The Advanced Rulebooks' deployment styles (2026-11-05): two sides, a game whose pages are in,
            // and something other than the core layout picked. A random style rolls one die here, on every
            // machine in lockstep off the shared dice, so all of them land on the same style.
            if (teamCount == 2 && AdvancedDeployment.Available(GameContext.Settings.GameSystem)
                && GameContext.Settings.DeploymentStyle != EDeploymentStyle.Core)
            {
                EDeploymentStyle picked = GameContext.Settings.DeploymentStyle;
                int rolled = 0;
                EDeploymentStyle style = AdvancedDeployment.Resolve(picked, () => rolled = HordePlacement.RollD6(GameContext));
                context.Log(rolled > 0
                    ? $"Deployment style: rolled {rolled} - {AdvancedDeployment.Name(style)}."
                    : $"Deployment style: {AdvancedDeployment.Name(style)}.");
                (RectangularZone a, RectangularZone b) = AdvancedDeployment.Zones(style, GameContext.Settings.GameSystem,
                    GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
                return new List<RectangularZone> { a, b };
            }

            // Three or four sides: the Advanced Rulebooks' Free-for-All (3.6.0, Multiplayer Games) - "players
            // alternate in picking a table corner and deploying one unit each within a 2'x1' area" (1'x1' in
            // GF: Firefight and AoF: Skirmish), in roll order, which the zone choice below already is. The
            // long side of the 2'x1' runs along the long table edge. Before this, three or four sides split
            // the long edges into strips - a layout no book prints.
            if (teamCount is 3 or 4 && !GameSystemInfo.IsQuest(GameContext.Settings.GameSystem))
            {
                float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
                bool skirmish = GameSystemInfo.IsSkirmish(GameContext.Settings.GameSystem);
                float along = skirmish ? 12f : 24f, deep = 12f;
                context.Log("Free-for-all: each side deploys in a table corner.");
                return new List<RectangularZone>
                {
                    new RectangularZone(0f, along, 0f, deep),
                    new RectangularZone(w - along, w, 0f, deep),
                    new RectangularZone(0f, along, h - deep, h),
                    new RectangularZone(w - along, w, h - deep, h),
                };
            }

            //We need to return equally-sized zones, and there are two sides to the table.
            //That means there will be an extra zone when the team number is odd.

            int zonesPerSide = (teamCount + 1) / 2; //Rounds up.

            float zoneWidth = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / zonesPerSide;

            List<RectangularZone> zones = new List<RectangularZone>(zonesPerSide * 2);

            for(int i = 0; i < zonesPerSide; i++)
            {
                float left = i * zoneWidth;
                float right = left + zoneWidth;

                //Bottom zone.
                zones.Add(new RectangularZone(left, right, 0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES));

                //Top zone.
                zones.Add(new RectangularZone(left, right,
                    GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES,
                    GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES));
            }

            return zones;
        }
    }
}
