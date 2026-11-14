using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// "Players alternate in picking D3 pieces of terrain each, which count as Dangerous Terrain" - the last step
    /// of the Advanced Rulebooks' (3.6.0) Generic terrain generator. One pick per request: the reply is the index
    /// of the chosen piece in <see cref="Options"/>. Mandatory - the book gives no way to pass.
    ///
    /// <para>The options are plain data (name, centre, rules) rather than live terrain objects: a request crosses
    /// the wire through Newtonsoft, and a client's table holds its own copies of the pieces. A front end finds the
    /// piece on its table with <see cref="IndexOf"/>.</para>
    /// </summary>
    public class PickDangerousTerrainRequest : IStageTaskRequest<int>
    {
        /// <summary>One piece that may be picked. X / Z is the middle of the piece, in table inches.</summary>
        public record TerrainOption(string Name, float X, float Z, ETerrainType Rules);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }
        public string Instructions { get; }

        /// <summary>This player's pick number (1-based) and how many each player picks (the D3).</summary>
        public int PickNumber { get; }
        public int PicksEach { get; }

        public float TableWidthInches { get; }
        public float TableHeightInches { get; }

        /// <summary>At least one. The reply is an index into this.</summary>
        public IReadOnlyList<TerrainOption> Options { get; }

        [JsonConstructor]
        public PickDangerousTerrainRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            int pickNumber, int picksEach, float tableWidthInches, float tableHeightInches,
            IReadOnlyList<TerrainOption> options)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            Instructions = instructions;
            PickNumber = pickNumber;
            PicksEach = picksEach;
            TableWidthInches = tableWidthInches;
            TableHeightInches = tableHeightInches;
            Options = options;
            TaskName = "Picking Dangerous Terrain";
        }

        public PickDangerousTerrainRequest(PlayerID targetPlayerID, string instructions, int pickNumber, int picksEach,
            float tableWidthInches, float tableHeightInches, IReadOnlyList<TerrainOption> options)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), instructions, pickNumber, picksEach,
                tableWidthInches, tableHeightInches, options)
        {
        }

        /// <summary>The middle of a piece's shape, as the options record it.</summary>
        public static (float X, float Z) CentreOf(IZone shape) =>
            shape is IBoundedZone b ? (b.Bounds.CenterX, b.Bounds.CenterZ) : (0f, 0f);

        /// <summary>The option that is this piece of terrain (same name and centre), or -1.</summary>
        public int IndexOf(ITerrain terrain)
        {
            (float x, float z) = CentreOf(terrain.Shape);
            for (int i = 0; i < Options.Count; i++)
            {
                TerrainOption o = Options[i];
                if (o.Name == terrain.Name && MathF.Abs(o.X - x) < 0.01f && MathF.Abs(o.Z - z) < 0.01f)
                    return i;
            }
            return -1;
        }

        public Task<int> Resolve(int resolution)
        {
            return Task.FromResult(resolution);
        }
    }
}
