using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// What a move will cost in Dangerous-terrain dice, BEFORE it is made (2026-11-14, Build 2.07). The move
    /// screens show "Dangerous terrain: N dice" on the confirm, so a player sees the risk while they can still
    /// change their mind.
    /// <para>
    /// The rule is the 3.6.0 one, word for word in GF, AoF, AoFS v3.6.0 and GFSQ v2.1.0: "Units with models moving
    /// across Dangerous terrain ... must roll as many dice as the number of wounds it would take to fully destroy
    /// them". One path touching the piece puts the whole unit's remaining wounds on the table.
    /// </para>
    /// <para>
    /// The flags are worked out by the engine, where the rules are, and travel on the move request - a client in
    /// a network game has no rule evaluator. The dice count itself is pure geometry and is the same code the real
    /// roll uses (<see cref="MovementExecutor.RollDangerousTerrain"/> calls <see cref="WouldTest"/> and
    /// <see cref="DiceFor(IUnit)"/>), so the preview cannot drift from the roll.
    /// </para>
    /// </summary>
    public sealed record DangerousTerrainOdds(bool Ignores, bool CountsAs, bool OnlyWhereStarted)
    {
        public static readonly DangerousTerrainOdds Normal = new(false, false, false);

        /// <summary>
        /// The flags for <paramref name="unit"/>'s next move, read the same way the roll reads them:
        /// Flying (the AllTerrain ignore) waives the test; a "counts as in Dangerous terrain" grant makes every
        /// move test; and in GFF, AoFS and both Quest games a flier still feels the Dangerous terrain it STARTS
        /// its move in ("...terrain it doesn't start its move in", #463).
        /// </summary>
        public static DangerousTerrainOdds For(IGameContext gameContext, IUnit unit)
        {
            bool ignores = MovementRuleQueries.IgnoresAllTerrain(unit, gameContext.RuleEvaluator);
            bool countsAs = MovementRuleQueries.CountsAsInTerrain(unit, gameContext.RuleEvaluator,
                ECountAsTerrain.Dangerous);
            bool onlyWhereStarted = ignores
                && CoreRulesProfile.For(gameContext).FlyingKeepsTerrainItStartsIn
                && MovementRuleQueries.AllTerrainOnlyFromFlying(unit, gameContext.RuleEvaluator);
            return new DangerousTerrainOdds(ignores && !onlyWhereStarted, countsAs, onlyWhereStarted);
        }

        /// <summary>
        /// How many dice <paramref name="unit"/> rolls if it makes <paramref name="paths"/> - 0 when the move
        /// does not test. Call it before the move is committed: a flier's "where it started" is read from where
        /// its models stand now.
        /// </summary>
        public int Dice(IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> terrain, IUnit unit)
        {
            if (Ignores) return 0;
            IEnumerable<ITerrain> pieces = OnlyWhereStarted ? StartedIn(unit, terrain) : terrain;
            return WouldTest(paths, pieces, CountsAs) ? DiceFor(unit) : 0;
        }

        /// <summary>The Dangerous pieces a living model of <paramref name="unit"/> stands in right now.</summary>
        public static List<ITerrain> StartedIn(IUnit unit, IEnumerable<ITerrain> terrain) =>
            terrain.Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous))
                .Where(piece => unit.Models.Any(m => m.GetIsAlive()
                    && TerrainProximityQueries.IsModelWithinInches(m, new[] { piece }, 0f)))
                .ToList();

        /// <summary>
        /// Whether a move along <paramref name="paths"/> takes the test. Unstable is included because AoF: Quest's
        /// Unstable counts AS Dangerous for a model that moved over 6" (the distance check lives in
        /// <see cref="MovementUtilities.DoesPathCrossDangerousTerrain"/>).
        /// </summary>
        public static bool WouldTest(IReadOnlyList<ModelMoveEntry> paths, IEnumerable<ITerrain> terrain,
            bool countsAsInDangerousTerrain)
        {
            List<ITerrain> dangerous = terrain
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)
                    || t.TerrainType.HasFlag(ETerrainType.Unstable))
                .ToList();
            if (dangerous.Count == 0 && !countsAsInDangerousTerrain) return false;

            foreach (ModelMoveEntry move in paths)
            {
                if (move.Positions.Count == 0) continue;
                if (countsAsInDangerousTerrain || MovementUtilities.DoesPathCrossDangerousTerrain(move, dangerous))
                    return true;
            }
            return false;
        }

        /// <summary>The unit's remaining wounds over its living models, at least one die each - the test's pool.</summary>
        public static int DiceFor(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive()).Sum(DiceFor);

        /// <summary>One model's share of the pool.</summary>
        public static int DiceFor(IModel model) => Math.Max(1, (int)MathF.Round(model.RemainingWounds));
    }
}
