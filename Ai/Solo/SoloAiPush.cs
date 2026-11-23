using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// Where the bot pushes an enemy (GF: Firefight / AoF: Skirmish Pushing: "move one model from the target by up to
    /// 2" in any direction"). The Solo &amp; Co-Op books have no clause for it, so the bot used to push the way its plain
    /// mover happened to point. Now (house rules C, "Bots and pushing"): off a ledge first - a model pushed down 2" or
    /// more falls and takes wounds (Advanced Rulebook, Falling) - then off an objective the target is standing on, so it
    /// stops holding or contesting it. With neither on offer, the old push.
    /// </summary>
    public static class SoloAiPush
    {
        private const int Directions = 16;

        /// <summary>The best push found, or null when no direction gains anything.</summary>
        public static List<ModelMoveEntry>? Best(DefineMovementPathRequest request, ITableState table, PlayerID me)
        {
            UnitData target = request.UnitDataBinding.GetValue();
            var living = target.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return null;
            var terrain = table.Terrain.Objects.ToList();
            var before = living.ToDictionary(m => m.ID, m => Climbing.StandingHeight(terrain, m.Position.Position2D));
            float cx = living.Average(m => m.Position.x), cz = living.Average(m => m.Position.z);
            var held = table.Objectives.Objects
                .Where(o => living.Any(m => Position.GetDistance2D(m.Position, o.Position) - m.BaseShape.BoundingRadiusInches <= 3f))
                .ToList();

            List<ModelMoveEntry>? best = null;
            float bestScore = 0f;
            for (int k = 0; k < Directions; k++)
            {
                float a = k * MathF.Tau / Directions;
                var goal = new Position(cx + MathF.Cos(a) * 8f, cz + MathF.Sin(a) * 8f);
                List<ModelMoveEntry> move;
                try
                {
                    using (Climbing.Pushed())
                        move = AiMoveExecutor.Toward(request, table, me, goal, 0f, request.MaxDistanceInches);
                }
                catch (Exception) { continue; }

                float score = 0f;
                foreach (ModelMoveEntry entry in move)
                {
                    if (entry.Positions.Count == 0) continue;
                    ModelData model = entry.Model.GetValue();
                    Position end = entry.Positions[^1];
                    float drop = before.GetValueOrDefault(model.ID) - Climbing.StandingHeight(terrain, end.Position2D);
                    if (drop >= Falling.MinInches) score += 10f * MathF.Floor(drop);
                }
                foreach (IObjective o in held)
                {
                    bool stillOn = move.Any(e => e.Positions.Count > 0
                        && Position.GetDistance2D(e.Positions[^1], o.Position) - e.Model.GetValue().BaseShape.BoundingRadiusInches <= 3f);
                    if (!stillOn) score += 3f;
                }
                if (score > bestScore) { bestScore = score; best = move; }
            }
            return best;
        }
    }
}
