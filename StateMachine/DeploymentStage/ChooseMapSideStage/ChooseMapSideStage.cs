

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


        /// <summary>
        /// One of a custom map's deployment zones as a real zone. A zone drawn as several rectangles -
        /// an L, a T (Kai, 2026-11-02) - becomes a <see cref="MultiRectZone"/>, which IS a
        /// <see cref="RectangularZone"/> whose geometry answers for the union, so nothing downstream
        /// needs to know the difference. A single rectangle stays a plain one.
        /// </summary>
        internal static RectangularZone BuildMapZone(FDG.SaveLoad.MapDeploymentZoneEntry entry)
        {
            var parts = new List<RectangularZone>();
            foreach (FDG.SaveLoad.MapZoneRect r in entry.AllParts())
            {
                float left = MathF.Min(r.Left, r.Right), right = MathF.Max(r.Left, r.Right);
                float bottom = MathF.Min(r.Bottom, r.Top), top = MathF.Max(r.Bottom, r.Top);
                // A part with no area is something half-drawn in the map maker, not a shape - skip it
                // rather than letting RectangularZone's constructor throw mid-deployment.
                if (right - left < 0.01f || top - bottom < 0.01f) continue;
                parts.Add(new RectangularZone(left, right, bottom, top));
            }

            if (parts.Count == 0)
                throw new InvalidOperationException("A map deployment zone has no usable rectangles.");
            return MultiRectZone.Of(parts);
        }

        private List<RectangularZone> GetRectangularZones(int teamCount, IDeploymentContext context)
        {
            if(teamCount <= 0)
            {
                throw new InvalidOperationException($"Can't have fewer than one team.");
            }

            // Map maker: a custom map's own deployment zones, when it has one for every team.
            var mapZones = CustomMapRules.DeploymentZones(GameContext.Settings);
            if (mapZones.Count >= teamCount && mapZones.Count >= 2)
                return mapZones.Select(BuildMapZone).ToList();

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
