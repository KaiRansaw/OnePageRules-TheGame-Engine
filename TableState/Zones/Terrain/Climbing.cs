using FDG.Data;

namespace FDG
{
    /// <summary>
    /// Climbing and standing on terrain (Advanced Rulebooks 3.6.0, Terrain p12, identical in GF / GFF / AoF / AoFS and the
    /// AoF: Quest Beginners Guide; Kai's calls 2026-11-22, house rules section C "Climbing").
    ///
    /// <para><b>Climbable</b>: "any terrain piece that is up to 3" tall may be climbed as part of a unit's move (units may
    /// not end their move mid-climb) ... short terrain up to 1" tall may be ignored for the purposes of movement (instead
    /// of having to climb up 1" and down 1" to cross it)." Kai: every inch climbed, up AND down, comes off the move; a
    /// model may end on a top only where its whole base sits flat (no "partially overhanging ... terrain"). Over 3" the
    /// piece is Impassable (<see cref="TerrainBook.WithClimbableHeight"/>).</para>
    ///
    /// <para><b>Steep</b> (hills): "Units moving onto steep terrain count it as being in Difficult terrain, but units moving
    /// off it count it as open terrain." A slope is walked, not climbed: no climbing cost and no flat-top check, but a
    /// model standing on it stands at its height.</para>
    ///
    /// <para>Where a model stands is never stored: it is always the ground under its centre (<see cref="StandingHeight"/>),
    /// so a saved game, a network update and every front end agree.</para>
    /// </summary>
    public static class Climbing
    {
        /// <summary>"short terrain up to 1" tall may be ignored for the purposes of movement".</summary>
        public const float IgnoredInches = 1f;
        /// <summary>The most the ground may vary under a standing model's base: a top is flat enough to stand on when
        /// every spot under the base is within this of the others (Kai, 2026-11-22).</summary>
        public const float FlatInches = 0.5f;
        /// <summary>How finely a path is walked to add up climbing.</summary>
        private const float StepInches = 0.25f;

        private static bool IsClimbable(ITerrain t) =>
            t.TerrainType.HasFlag(ETerrainType.Climbable) && !t.TerrainType.HasFlag(ETerrainType.Impassible) && t is not Stages.ModelSightBlocker;

        private static bool IsSteep(ITerrain t) =>
            t.TerrainType.HasFlag(ETerrainType.Steep) && !t.TerrainType.HasFlag(ETerrainType.Impassible) && t is not Stages.ModelSightBlocker;

        /// <summary>How tall a piece is at a point for standing on it (0 off it, or when it records no height).</summary>
        private static float TopOf(ITerrain piece, Float2 point)
        {
            if (TerrainHeights.HasPartHeights(piece)) return TerrainHeights.TopAt(piece, point) ?? 0f;
            return piece.Shape.IsPointWithinZone(point) ? MathF.Max(0f, piece.HeightInches) : 0f;
        }

        /// <summary>The height of the climbable ground at a point (Climbable pieces only; up to 1" counts as the table).</summary>
        public static float ClimbableGroundAt(IEnumerable<ITerrain> terrain, Float2 point)
        {
            float best = 0f;
            foreach (ITerrain t in terrain)
            {
                if (!IsClimbable(t)) continue;
                float top = TopOf(t, point);
                if (top > IgnoredInches && top > best) best = top;
            }
            return best;
        }

        /// <summary>How high a model whose centre is at <paramref name="point"/> stands: on the tallest Climbable or Steep
        /// piece under it (a Climbable piece up to 1" tall counts as the table).</summary>
        public static float StandingHeight(IEnumerable<ITerrain> terrain, Float2 point)
        {
            float best = 0f;
            foreach (ITerrain t in terrain)
            {
                bool climbable = IsClimbable(t);
                if (!climbable && !IsSteep(t)) continue;
                float top = TopOf(t, point);
                if (climbable && top <= IgnoredInches) continue;
                if (top > best) best = top;
            }
            return best;
        }

        /// <summary>The position lifted to the height it stands at (its own height kept when already higher).</summary>
        public static Position Standing(IEnumerable<ITerrain> terrain, Position at)
        {
            float h = StandingHeight(terrain, at.Position2D);
            return h > at.y ? new Position(at.x, h, at.z) : at;
        }

        [ThreadStatic] private static bool t_pushed;

        /// <summary>
        /// While a pushed unit's move is checked on this thread (Pushing, GFF / AoFS 3.6.0), going DOWN costs nothing:
        /// a model "pushed off an elevated position" falls (<see cref="Falling"/>) rather than climbing down. Going up
        /// still costs - a push is no climb. Used as <c>using (Climbing.Pushed()) { ... }</c> around a synchronous check.
        /// </summary>
        public static IDisposable Pushed() { bool was = t_pushed; t_pushed = true; return new Restore(was); }

        private sealed class Restore : IDisposable
        {
            private readonly bool _was;
            public Restore(bool was) => _was = was;
            public void Dispose() => t_pushed = _was;
        }

        /// <summary>Every inch climbed up and down along a path (its points in order, start first).</summary>
        public static float ClimbAlong(IReadOnlyList<Float2> path, IEnumerable<ITerrain> terrain)
        {
            if (path.Count < 2) return 0f;
            IReadOnlyList<ITerrain> pieces = terrain.Where(IsClimbable).ToList();
            if (pieces.Count == 0) return 0f;
            float climb = 0f, last = ClimbableGroundAt(pieces, path[0]);
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Float2 a = path[i], b = path[i + 1];
                float dx = b.X - a.X, dz = b.Y - a.Y, len = MathF.Sqrt(dx * dx + dz * dz);
                int steps = Math.Max(1, (int)MathF.Ceiling(len / StepInches));
                for (int k = 1; k <= steps; k++)
                {
                    Float2 p = a + (b - a) * (k / (float)steps);
                    float h = ClimbableGroundAt(pieces, p);
                    climb += t_pushed ? MathF.Max(0f, h - last) : MathF.Abs(h - last);
                    last = h;
                }
            }
            return climb;
        }

        /// <summary>Whether a model with a base of <paramref name="radius"/> may end its move at <paramref name="centre"/>:
        /// all of its base on the table or all of it flat on one top - never part way up a climb or hanging over an edge.</summary>
        public static bool MayEndAt(Float2 centre, float radius, IEnumerable<ITerrain> terrain)
        {
            IReadOnlyList<ITerrain> pieces = terrain as IReadOnlyList<ITerrain> ?? terrain.ToList();
            if (!pieces.Any(IsClimbable)) return true;
            float lo = float.MaxValue, hi = float.MinValue;
            void At(Float2 p)
            {
                float h = ClimbableGroundAt(pieces, p);
                lo = MathF.Min(lo, h);
                hi = MathF.Max(hi, h);
            }
            At(centre);
            float r = radius * 0.95f;
            foreach (float ring in new[] { r, r * 0.5f })
                for (int k = 0; k < 12; k++)
                {
                    float a = k * MathF.Tau / 12f;
                    At(new Float2(centre.X + MathF.Cos(a) * ring, centre.Y + MathF.Sin(a) * ring));
                }
            return hi - lo <= FlatInches;
        }

        /// <summary>
        /// The battle report's line for a move over heights (before the move is committed): how far the furthest
        /// climber climbed and where the unit ends up standing. Null when the move touched no heights.
        /// </summary>
        public static string? Describe(IUnit unit, IReadOnlyList<StageResolution.Requests.ModelMoveEntry> paths, IEnumerable<ITerrain> terrain)
        {
            IReadOnlyList<ITerrain> pieces = terrain as IReadOnlyList<ITerrain> ?? terrain.ToList();
            if (!pieces.Any(t => (t.TerrainType & (ETerrainType.Climbable | ETerrainType.Steep)) != 0)) return null;
            float climbed = 0f, high = 0f, wasHigh = 0f;
            int raised = 0;
            bool onSteep = false;
            foreach (StageResolution.Requests.ModelMoveEntry e in paths)
            {
                if (e.Positions.Count == 0) continue;
                Float2 start = e.Model.GetValue().Position.Position2D;
                var path = new List<Float2> { start };
                foreach (Position p in e.Positions) path.Add(p.Position2D);
                climbed = MathF.Max(climbed, ClimbAlong(path, pieces));
                wasHigh = MathF.Max(wasHigh, StandingHeight(pieces, start));
                float h = StandingHeight(pieces, path[^1]);
                if (h > 0f) { raised++; high = MathF.Max(high, h); }
                onSteep |= MovesOntoSteep(start, path.Skip(1).ToList(), pieces);
            }
            var parts = new List<string>();
            if (climbed > 0f) parts.Add($"climbed {climbed:0.#}\" (up and down count against the move)");
            if (onSteep) parts.Add("moved onto Steep terrain (Difficult going up)");
            if (raised > 0) parts.Add($"{raised} model{(raised == 1 ? "" : "s")} now stand{(raised == 1 ? "s" : "")} up to {high:0.#}\" high");
            else if (wasHigh > 0f) parts.Add("came down to the ground");
            return parts.Count == 0 ? null : $"Heights: {unit.Name} " + string.Join(", ", parts) + ".";
        }

        /// <summary>Dev check (env FDG_HEIGHTS_AUDIT=1, the heights sweep): after every activation, every model on the table
        /// must stand flat - never part way up a climb or hanging over a top's edge. Logs "[HEIGHTS AUDIT]" lines.</summary>
        public static void Audit(IGameContext c)
        {
            if (Environment.GetEnvironmentVariable("FDG_HEIGHTS_AUDIT") != "1") return;
            List<ITerrain> terrain = c.TableState.Terrain.Objects.ToList();
            if (!terrain.Any(IsClimbable)) return;
            foreach (IUnit u in c.TableState.Units.Objects)
            {
                // Flying units hover over terrain (an Aircraft's 30" straight line ends where it ends).
                if (u is UnitData ud && Rules.Dispatch.MovementRuleQueries.IgnoresAllTerrain(ud, c.RuleEvaluator)) continue;
                if (u is UnitData art && art.RuleDefinitions.Any(r => r.Definition == Rules.Dispatch.CoreRuleCatalog.Artillery)
                    && art.Models.FirstOrDefault(m => m.GetIsAlive()) is { } first && !(first.Position.x == 0f && first.Position.z == 0f))
                    c.Log($"[HEIGHTS AUDIT] artillery {u.Name} stands at {StandingHeight(terrain, first.Position.Position2D):0.0}\" " +
                        $"({first.Position.x:0.0}, {first.Position.z:0.0}).");
                foreach (IModel m in u.Models)
                {
                    if (!m.GetIsAlive() || (m.Position.x == 0f && m.Position.z == 0f)) continue;
                    if (!MayEndAt(m.Position.Position2D, m.BaseShape.BoundingRadiusInches, terrain))
                        c.Log($"[HEIGHTS AUDIT] {u.Name}: a model stands mid-climb at ({m.Position.x:0.0}, {m.Position.z:0.0}).");
                }
            }
        }

        /// <summary>Whether a move from <paramref name="start"/> along <paramref name="path"/> goes onto a Steep piece from
        /// off it ("moving onto steep terrain") - counted as Difficult terrain. Moving off it, or about on top, is open.</summary>
        public static bool MovesOntoSteep(Float2 start, IReadOnlyList<Float2> path, IEnumerable<ITerrain> terrain)
        {
            foreach (ITerrain t in terrain)
            {
                if (!IsSteep(t) || t.Shape.IsPointWithinZone(start)) continue;
                Float2 from = start;
                foreach (Float2 p in path)
                {
                    if (t.Shape.DoesPathIntersectZone(from, p)) return true;
                    from = p;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Falling (GF: Firefight / AoF: Skirmish Advanced Rules 3.6.0 p15, the same words in both): "If a model falls or is
    /// pushed off an elevated position at least 2" tall, roll X dice, where X is one die for every 1" it fell, and for
    /// each roll of 1-3 it takes one wound. Then place the model within 2" of the bottom ..." The game reaches it through
    /// Pushing, the only thing in those games that moves a model off a top against its will; the model is already at
    /// the bottom when it lands, so the placing needs nothing more.
    /// </summary>
    public static class Falling
    {
        public const float MinInches = 2f;

        /// <summary>Each living model's standing height now, to compare after a move.</summary>
        public static Dictionary<IModel, float> HeightsOf(IUnit unit, IEnumerable<ITerrain> terrain)
        {
            var heights = new Dictionary<IModel, float>();
            foreach (IModel m in unit.Models)
                if (m.GetIsAlive()) heights[m] = Climbing.StandingHeight(terrain, m.Position.Position2D);
            return heights;
        }

        /// <summary>How many whole inches each model fell (only drops of 2" or more).</summary>
        public static List<(IModel Model, int Dice)> Falls(Dictionary<IModel, float> before, IEnumerable<ITerrain> terrain)
        {
            var falls = new List<(IModel, int)>();
            foreach ((IModel m, float was) in before)
            {
                if (!m.GetIsAlive()) continue;
                float drop = was - Climbing.StandingHeight(terrain, m.Position.Position2D);
                if (drop >= MinInches - 1e-3f) falls.Add((m, (int)MathF.Floor(drop + 1e-3f)));
            }
            return falls;
        }

        /// <summary>Rolls and lands the falling wounds for every model that fell 2" or more.</summary>
        public static async Task Resolve(IGameContext c, IUnit unit, Dictionary<IModel, float> before)
        {
            List<(IModel Model, int Dice)> falls = Falls(before, c.TableState.Terrain.Objects);
            if (falls.Count == 0) return;
            var pending = new List<Stages.PendingModelWound>();
            foreach ((IModel m, int dice) in falls)
            {
                IDiceResults roll = c.DiceRoller.Roll(6, dice);
                float wounds = roll.At(1) + roll.At(2) + roll.At(3);
                c.Log($"Falling: a model of {unit.Name} fell {dice}\" - {dice} dice, {wounds:0.##} wound(s) on 1-3.");
                await c.Presenter.Present(Presentation.Beats.DiceRolledBeat.From(roll, successThreshold: 4, c.Settings.RandomnessType,
                    "Falling", wounds <= 0f ? "Landed safely" : $"{wounds:0.##} wound{(wounds == 1f ? "" : "s")}"));
                if (wounds > 0f) pending.Add(new Stages.PendingModelWound(m, wounds));
            }
            if (pending.Count == 0) return;
            bool wasAlive = unit.GetIsAlive();
            await Stages.CasualtyPresentation.ApplyAndPresent(c, pending, unit.ID, unit.Name);
            if (wasAlive && !unit.GetIsAlive())
                await Stages.UnitDestructionNotifier.NotifyUnitDestroyed(c, unit, killer: null);
        }
    }
}
