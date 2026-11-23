using FDG.Data;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// One end of a sight line: where a model stands, how wide its base is and how tall its volume is.
    /// Under the classic (top-down) method only <see cref="Pos"/> is read; under Volumetric Line of Sight
    /// the end is the model's whole cylinder.
    /// </summary>
    public readonly record struct SightEnd(Position Pos, float RadiusInches, float HeightInches)
    {
        public static SightEnd Of(IModel model) => Of(model.Position, model.BaseShape);

        public static SightEnd Of(Position position, IBaseShape? baseShape) => baseShape == null
            ? Point(position)
            : new SightEnd(position, baseShape.BoundingRadiusInches, VolumetricSight.HeightForBase(baseShape));

        /// <summary>A sight end with no model behind it (a planned spot, a unit centroid, a marker): counted as a
        /// 32mm-based model, the book's smallest common infantry row after 25mm.</summary>
        public static SightEnd Point(Position position)
            => new SightEnd(position, VolumetricSight.DefaultRadiusInches, VolumetricSight.DefaultHeightInches);
    }

    /// <summary>
    /// A living model's base standing in a sight line, as <see cref="LineOfSightUtilities.BuildModelBlockers"/> makes
    /// them. Under the classic method it is a plain Blocking footprint, exactly as before; under Volumetric Line of
    /// Sight it is a cylinder as tall as <see cref="VolumetricSight.HeightForBase"/> says, so a taller model sees over it.
    /// Never a "Sight Blocker" for Cover - that is terrain.
    /// </summary>
    public sealed class ModelSightBlocker : TerrainData
    {
        /// <summary>The height its base stands at (a wall top, a hill) - its volume runs from here up.</summary>
        public float BottomInches { get; }

        public ModelSightBlocker(IZone footprint, float heightInches, float bottomInches = 0f)
            : base(ETerrainType.Blocking, footprint, heightInches) { BottomInches = bottomInches; }
    }

    /// <summary>
    /// <para><b>Volumetric Line of Sight</b> - Advanced Rulebook v3.6.0, Tournament Guidelines (GF p58, AoF p58,
    /// GFF p55, AoFS p55; the same words in all four): "Before the game begins, players must assign a height value
    /// in inches to all units. Each model occupies a volume of space of that height above its base, determined by
    /// its base size ... To determine line of sight, simply draw a straight line from any point of the model's
    /// volume to any point of the target model's volume. If the line doesn't pass through any obstacles or unit
    /// volumes, then the model has line of sight."</para>
    ///
    /// <para>The always-apply list on the same page still holds: models see in all directions, can't see through
    /// solid obstacles including other units, and see through their own unit (the blocker lists already leave the
    /// shooter's and the target's own models out).</para>
    ///
    /// <para>How the engine does it: terrain pieces are prisms (their footprint, from the table up to
    /// <see cref="ITerrain.HeightInches"/>), models are cylinders on their bases, and the "any point to any point"
    /// search tries a fixed set of points on each cylinder (top, middle and low, centre and both sides as seen from
    /// the other end). Because every piece is no taller than the classic method's "infinitely tall" footprint and
    /// the classic centre-to-centre line is one of the tried lines, this method only ever ADDS sight: whenever the
    /// classic answer is not Blocking it is returned unchanged.</para>
    ///
    /// <para>Cover: a target seen over or past a terrain piece that would have blocked it under the classic method
    /// is "behind a Sight Blocker" (core rules, Cover Terrain) and gets Cover; seeing over another unit's models
    /// gives nothing. Cover pieces keep their classic test.</para>
    ///
    /// <para>Prior art: the Niemandsland project (MIT, github.com/DutchMaxwell/Niemandsland) builds the same rule as
    /// prisms and cylinders; the idea, not its code, was borrowed.</para>
    /// </summary>
    public static class VolumetricSight
    {
        /// <summary>Set per game from <see cref="GameSettings.VolumetricLineOfSight"/> by
        /// <see cref="GameWideConstants.ConfigureTable(GameSettings)"/>; off otherwise.</summary>
        public static bool On;

        public const float MmPerInch = 25.4f;

        /// <summary>The book's pre-set heights: (base size mm, height inches).</summary>
        public static readonly (float BaseMm, float HeightInches)[] BookHeights =
        {
            (25f, 1f), (32f, 1.25f), (40f, 1.5f), (50f, 2f), (60f, 3f), (100f, 4f),
        };

        public static readonly float DefaultHeightInches = 1.25f;
        public static readonly float DefaultRadiusInches = 32f / MmPerInch / 2f;

        /// <summary>
        /// A model's height in inches from its base size: the book's rows exactly, a straight line between two rows,
        /// 1" under 25mm, and past 100mm the 60mm-to-100mm slope carried on (1" per 40mm, so 160mm = 5.5"). The book
        /// leaves sizes off its list to the players ("based on the typical size of miniatures mounted on those base
        /// sizes") - house rules list, judgement calls.
        /// </summary>
        public static float HeightForBaseMm(float baseMm)
        {
            if (baseMm <= BookHeights[0].BaseMm) return BookHeights[0].HeightInches;
            for (int i = 1; i < BookHeights.Length; i++)
            {
                (float hiMm, float hiH) = BookHeights[i];
                if (baseMm > hiMm) continue;
                (float loMm, float loH) = BookHeights[i - 1];
                return loH + (hiH - loH) * (baseMm - loMm) / (hiMm - loMm);
            }
            (float lastMm, float lastH) = BookHeights[^1];
            (float prevMm, float prevH) = BookHeights[^2];
            return lastH + (lastH - prevH) * (baseMm - lastMm) / (lastMm - prevMm);
        }

        /// <summary>Every base is round since house rule B25 (an oval plays as a circle of its long side), so the
        /// circumscribed diameter is the base size.</summary>
        public static float HeightForBase(IBaseShape baseShape)
            => HeightForBaseMm(baseShape.CircumscribedRadiusInches * 2f * MmPerInch);

        /// <summary>A piece with no height recorded is as tall as the classic method treats every piece.</summary>
        public static float TopOf(ITerrain piece) => piece.HeightInches > 0f ? piece.HeightInches : float.MaxValue;

        // Heights tried on each cylinder (fractions of its height) and sideways offsets (fractions of its radius).
        // Order matters only for speed: the top-centre pair is tried first because it is the usual answer.
        private static readonly float[] HeightSamples = { 1f, 0.5f, 0.1f };
        private static readonly float[] SideSamples = { 0f, 0.95f, -0.95f };

        /// <summary>
        /// The volumetric answer for one pair of ends. <paramref name="classic"/> is the top-down answer for the same
        /// centre-to-centre line (already folded through the proximity exceptions); anything but Blocking is
        /// returned as it is.
        /// </summary>
        public static ESightLineEffect Evaluate(SightEnd a, SightEnd b, IEnumerable<ITerrain> terrain,
            ESightLineEffect classic, in CoverContext? coverContext, bool applyProximityExceptions)
        {
            if (classic != ESightLineEffect.Blocking) return classic;

            IReadOnlyList<ITerrain> pieces = terrain as IReadOnlyList<ITerrain> ?? terrain.ToList();
            if (!AnyClearLine(a, b, pieces)) return ESightLineEffect.Blocking;

            // Seen. Cover if the centre line crosses a terrain piece that blocks under the classic method (the target
            // is behind a Sight Blocker) or a Cover piece; models are never Sight Blockers.
            Float2 a2 = a.Pos.Position2D;
            Float2 b2 = b.Pos.Position2D;
            ESightLineEffect best = ESightLineEffect.Clear;
            foreach (ITerrain piece in pieces)
            {
                if (piece is ModelSightBlocker) continue;
                ESightLineEffect effect = piece.EvaluateSightLine(a.Pos, b.Pos);
                if (effect == ESightLineEffect.Clear) continue;
                if (applyProximityExceptions && coverContext is { } ctx && CoverProximityRules.VoidsCover(piece, ctx))
                    continue;
                if (!piece.Shape.DoesPathIntersectZone(a2, b2)) continue;
                best = ESightLineEffect.Cover;
                break;
            }
            return best;
        }

        /// <summary>True when some line between the two cylinders passes no obstacle and no other model's volume.</summary>
        public static bool AnyClearLine(SightEnd a, SightEnd b, IReadOnlyList<ITerrain> pieces)
        {
            // A model standing on a piece (a wall top, a hill) looks from up there (Climbing, 2026-11-22).
            a = a with { Pos = Climbing.Standing(pieces, a.Pos) };
            b = b with { Pos = Climbing.Standing(pieces, b.Pos) };
            Float2 a2 = a.Pos.Position2D;
            Float2 b2 = b.Pos.Position2D;
            float dx = b2.X - a2.X, dz = b2.Y - a2.Y;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 1e-4f) return true; // the same spot: nothing can stand between
            // Sideways direction, as seen along the line.
            float px = -dz / len, pz = dx / len;

            Span<Float3> fromPts = stackalloc Float3[HeightSamples.Length * SideSamples.Length];
            Span<Float3> toPts = stackalloc Float3[HeightSamples.Length * SideSamples.Length];
            Fill(fromPts, a, px, pz);
            Fill(toPts, b, px, pz);

            foreach (Float3 p in fromPts)
            {
                foreach (Float3 q in toPts)
                {
                    if (SegmentClear(p, q, a, b, pieces)) return true;
                }
            }
            return false;
        }

        private static void Fill(Span<Float3> pts, SightEnd end, float px, float pz)
        {
            int i = 0;
            float baseY = end.Pos.y;
            foreach (float h in HeightSamples)
            {
                foreach (float s in SideSamples)
                {
                    float off = s * end.RadiusInches;
                    pts[i++] = new Float3(end.Pos.x + px * off, baseY + h * end.HeightInches, end.Pos.z + pz * off);
                }
            }
        }

        private static bool SegmentClear(Float3 p, Float3 q, SightEnd a, SightEnd b, IReadOnlyList<ITerrain> pieces)
        {
            foreach (ITerrain piece in pieces)
            {
                if (BlocksSegment(piece, p, q, a, b)) return false;
            }
            return true;
        }

        /// <summary>Whether this piece stops the 3D segment p-q. <paramref name="a"/>/<paramref name="b"/> are the two
        /// ends (Dense terrain lets a model standing in it see out, and be seen into).</summary>
        public static bool BlocksSegment(ITerrain piece, Float3 p, Float3 q, SightEnd a, SightEnd b)
        {
            ETerrainType type = piece.TerrainType;
            bool blocking = type.HasFlag(ETerrainType.Blocking);
            bool dense = type.HasFlag(ETerrainType.Dense);
            if (!blocking && !dense)
            {
                // Custom pieces decide for themselves on the flat line; Cover never stops sight.
                if (piece is TerrainData) return false;
                return piece.EvaluateSightLine(new Position(p.X, p.Z), new Position(q.X, q.Z))
                       == ESightLineEffect.Blocking;
            }
            if (piece is not TerrainData)
            {
                // A custom piece with its own sight logic (a door) is as tall as the classic method says.
                return piece.EvaluateSightLine(new Position(p.X, p.Z), new Position(q.X, q.Z))
                       == ESightLineEffect.Blocking;
            }

            // Each part of the piece stands to its own height (heights read from the model, 2026-11-22): a line
            // passing over the low parts and beside the tall ones gets through.
            bool crosses = false;
            float bottom = piece is ModelSightBlocker standing ? standing.BottomInches : 0f;
            foreach ((IZone zone, float top) in TerrainHeights.Parts(piece))
            {
                if (!ClipToSlab(p, q, bottom, bottom + top, out Float2 c0, out Float2 c1)) continue;
                if (!zone.DoesPathIntersectZone(c0, c1)) continue;
                crosses = true;
                break;
            }
            if (!crosses) return false;
            if (blocking) return true;
            // Dense: into and out of, not through.
            return !piece.Shape.IsPointWithinZone(a.Pos.Position2D) && !piece.Shape.IsPointWithinZone(b.Pos.Position2D);
        }

        /// <summary>The flat part of p-q that lies between heights y0 and y1, or false when the segment passes
        /// wholly over (or under) that band. Touching the top counts as inside.</summary>
        public static bool ClipToSlab(Float3 p, Float3 q, float y0, float y1, out Float2 c0, out Float2 c1)
        {
            const float Eps = 1e-4f;
            float lo = y0 - Eps, hi = y1 + Eps;
            float t0 = 0f, t1 = 1f;
            float dy = q.Y - p.Y;
            if (MathF.Abs(dy) < 1e-7f)
            {
                if (p.Y < lo || p.Y > hi) { c0 = c1 = default; return false; }
            }
            else
            {
                float ta = (lo - p.Y) / dy, tb = (hi - p.Y) / dy;
                t0 = MathF.Max(0f, MathF.Min(ta, tb));
                t1 = MathF.Min(1f, MathF.Max(ta, tb));
                if (t0 > t1) { c0 = c1 = default; return false; }
            }
            c0 = new Float2(p.X + (q.X - p.X) * t0, p.Z + (q.Z - p.Z) * t0);
            c1 = new Float2(p.X + (q.X - p.X) * t1, p.Z + (q.Z - p.Z) * t1);
            return true;
        }
    }
}
