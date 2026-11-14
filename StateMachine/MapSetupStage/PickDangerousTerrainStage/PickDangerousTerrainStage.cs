using FDG.Data;
using FDG.Presentation.Beats;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// The last step of the Advanced Rulebooks' (3.6.0) Generic terrain generator: <i>"After terrain has been placed,
    /// roll-off to see who goes first, and then players alternate in picking D3 pieces of terrain each, which count
    /// as Dangerous Terrain."</i> Only runs when <see cref="GameSettings.PickDangerousTerrain"/> is on (the lobby
    /// ticks it for a map rolled on the Generic table).
    ///
    /// <para>House rules (claude/house-rules.md): one D3 is rolled and every player picks that many, so nobody
    /// gets more than anyone else; a piece that is already Dangerous, or is Impassable (nobody can move into it,
    /// so it could never be tested), is not offered; picking stops early if the table runs out of pieces.</para>
    ///
    /// <para>A picked piece is replaced by a copy carrying the Dangerous flag - terrain is immutable data, and a
    /// replacement goes through the data store like any other change, so every connected table sees it.</para>
    /// </summary>
    public class PickDangerousTerrainStage : StageBase<IMapSetupContext>
    {
        public StageBinding OnPicksComplete;

        private static readonly TextColor DangerColour = new TextColor(240, 120, 80, 255);

        public PickDangerousTerrainStage(IGameContext gameContext, IStateMachineLayer<IMapSetupContext> parent)
            : base(gameContext, parent)
        {
            OnPicksComplete = new StageBinding(this);
        }

        /// <summary>Whether a piece may be picked: not Dangerous already, and not Impassable.</summary>
        public static bool CanPick(ITerrain terrain) =>
            !terrain.TerrainType.HasFlag(ETerrainType.Dangerous) && !terrain.TerrainType.HasFlag(ETerrainType.Impassible);

        public override async Task Enter(IMapSetupContext context)
        {
            context.LogDebug($"Entered {nameof(PickDangerousTerrainStage)}.");
            // Only the four Advanced Rulebooks print the generator; the Quest games have no such step.
            if (!context.GameContext.Settings.PickDangerousTerrain
                || !TerrainBook.HasGenerator(context.GameContext.Settings.GameSystem))
            {
                await OnPicksComplete.Activate(context);
                return;
            }

            if (!context.GameContext.TableState.Terrain.Objects.Any(CanPick))
            {
                context.Log("Dangerous terrain picks: no piece on the table can be made Dangerous; skipped.");
                await OnPicksComplete.Activate(context);
                return;
            }

            List<ITeam> teams = context.GameContext.TableState.Teams.Objects.Where(t => t.Players.Count > 0).ToList();
            if (teams.Count == 0)
            {
                await OnPicksComplete.Activate(context);
                return;
            }

            ITeam winner = teams.Count == 1 ? teams[0] : await DiceUtilities.RollOff_SingleWinner(teams,
                teams.Select(t => $"Team {t.TeamNumber}").ToList(),
                context.GameContext.TextOutput, context.GameContext.DiceRoller, context.GameContext.Presenter,
                "Dangerous Terrain Roll-Off");
            var order = new List<ITeam> { winner };
            order.AddRange(teams.Where(t => !ReferenceEquals(t, winner)));

            IDiceResults d3 = context.GameContext.DiceRoller.RollDecisive(3);
            int each = d3.SideMin;
            for (int v = d3.SideMin; v <= d3.SideMax; v++)
                if (d3.At(v) > 0f) { each = v; break; }
            int players = order.Sum(t => t.Players.Count);
            await context.GameContext.Presenter.Present(DiceRolledBeat.FromDecisive(d3, each,
                "Dangerous Terrain (D3 each)", $"{each} piece{(each == 1 ? "" : "s")} each"));
            context.Log($"Dangerous terrain picks: {context.GetTeamLeadName(winner)} won the roll-off; D3={each}, so each player picks {each}.");
            await context.Announce($"{context.GetTeamLeadName(winner)} picks Dangerous terrain first - {each} each", DangerColour);

            var cursor = new TeamPlayerAlternationCursor(order);
            var picked = new Dictionary<PlayerID, int>();
            for (int turn = 0; turn < each * players; turn++)
            {
                PlayerID picker = cursor.GetCurrentPlayerID();
                cursor.TryAdvance(_ => true, _ => true, out _, out _);

                List<DataBinding<TerrainData>> pickable = context.GameContext.GameDataStore
                    .GetAllDataBindings<TerrainData>().Where(b => CanPick(b.GetValue())).ToList();
                if (pickable.Count == 0)
                {
                    context.Log("Dangerous terrain picks: no pieces left to pick; stopping early.");
                    break;
                }

                picked.TryGetValue(picker, out int done);
                picked[picker] = done + 1;
                var options = pickable.Select(b =>
                {
                    TerrainData t = b.GetValue();
                    (float x, float z) = PickDangerousTerrainRequest.CentreOf(t.Shape);
                    return new PickDangerousTerrainRequest.TerrainOption(t.Name, x, z, t.TerrainType);
                }).ToList();
                var request = new PickDangerousTerrainRequest(picker,
                    $"Pick a piece of terrain to count as Dangerous ({done + 1} of {each}).",
                    done + 1, each, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES,
                    options);
                int index = await context.GameContext.PlayerRequester.RequestDecision<PickDangerousTerrainRequest, int>(request);
                if (index < 0 || index >= pickable.Count) index = 0;

                DataBinding<TerrainData> chosen = pickable[index];
                TerrainData old = chosen.GetValue();
                context.GameContext.GameDataStore.Destroy(chosen.Reference);
                context.GameContext.GameDataStore.Create(new TerrainData(old.TerrainType | ETerrainType.Dangerous,
                    old.Shape, old.HeightInches, old.Name));

                string pieceName = string.IsNullOrWhiteSpace(old.Name) ? "a piece of terrain" : old.Name;
                context.Log($"{context.GetPlayerName(picker)} makes {pieceName} Dangerous.");
                await context.Announce($"{context.GetPlayerName(picker)}: {pieceName} is now Dangerous", DangerColour);
            }

            await OnPicksComplete.Activate(context);
        }
    }
}
