namespace FDG.Stages
{
    /// <summary>
    /// Horde Mode map setup: instead of rolling a count and taking turns placing, the five objective
    /// markers go at fixed spots - the table centre and the centre of each table quarter. If terrain
    /// blocks a spot, the marker slides to the nearest legal point.
    /// </summary>
    public class HordeObjectiveSetupStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnObjectivesPlaced;

        private const float MinObjectiveSeparationInches = 9f;
        private const float SearchStepInches = 1f;
        private const float MaxSearchRadiusInches = 12f;

        public HordeObjectiveSetupStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnObjectivesPlaced = new StageBinding(this);
        }

        public override async Task Enter(IMapSetupContext context)
        {
            context.LogDebug($"Entered {nameof(HordeObjectiveSetupStage)}.");

            float tableW = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
            float tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            var wholeTable = new RectangularZone(left: 0f, right: tableW, bottom: 0f, top: tableH);

            var impassable = context.GameContext.TableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();

            var placed = new List<IObjective>();
            var anchors = FDG.StateMachine.HordeRules.ObjectiveAnchors(tableW, tableH);

            for (int i = 0; i < anchors.Count; i++)
            {
                Position anchor = anchors[i];
                if (!TryFindLegalSpot(anchor, wholeTable, placed, impassable, out Position spot))
                {
                    context.Log($"  No legal spot near objective {i + 1}'s anchor ({anchor.x:F1}, {anchor.z:F1}); skipped.");
                    continue;
                }

                var data = new ObjectiveData(spot, context.GameContext.GameDataStore, hordeNumber: i + 1);
                context.GameContext.GameDataStore.Create(data);
                placed.Add(data);

                context.Log($"  Placed objective {i + 1} at ({spot.x:F1}, {spot.z:F1}).");
            }

            await context.Announce($"{placed.Count} Objectives", new TextColor(255, 210, 80, 255));
            await OnObjectivesPlaced.Activate(context);
        }

        // Anchor first, then rings of growing radius around it until the shared validator accepts a spot.
        private static bool TryFindLegalSpot(Position anchor, RectangularZone band, IReadOnlyList<IObjective> existing,
            IReadOnlyList<ITerrain> impassable, out Position spot)
        {
            if (ObjectivePlacementValidator.Check(anchor, band, MinObjectiveSeparationInches, existing, impassable)
                == ObjectivePlacementValidity.Valid)
            {
                spot = anchor;
                return true;
            }

            for (float r = SearchStepInches; r <= MaxSearchRadiusInches; r += SearchStepInches)
            {
                int samples = Math.Max(8, (int)(r * 8));
                for (int s = 0; s < samples; s++)
                {
                    double angle = 2.0 * Math.PI * s / samples;
                    var candidate = new Position(anchor.x + (float)(Math.Cos(angle) * r),
                        anchor.z + (float)(Math.Sin(angle) * r));
                    if (ObjectivePlacementValidator.Check(candidate, band, MinObjectiveSeparationInches, existing, impassable)
                        == ObjectivePlacementValidity.Valid)
                    {
                        spot = candidate;
                        return true;
                    }
                }
            }

            spot = default;
            return false;
        }
    }
}
