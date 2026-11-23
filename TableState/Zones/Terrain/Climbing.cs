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
                    climb += MathF.Abs(h - last);
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
}
