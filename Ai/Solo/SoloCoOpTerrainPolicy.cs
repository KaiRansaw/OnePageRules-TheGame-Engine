using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Players;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #424 - the Terrain clause of <b>Solo &amp; Co-Op Rules v3.6.0</b>, word for word in all four books:
    ///
    /// <para>
    /// "<b>Terrain:</b> AI units must always try to <b>end their move into or behind Cover terrain</b> if
    /// possible, <b>moving around Difficult and Dangerous terrain</b>. AI units only move into Difficult or
    /// Dangerous terrain if there is an <b>objective inside of it</b>, if they are in <b>charge range of a
    /// unit inside of it</b>, or if they have a <b>special rule that ignores its effects</b>."
    /// </para>
    /// <para>
    /// <b>What was already there, and what was not.</b> The shared mover routes around Impassible, and
    /// since #281 around <b>Difficult</b> too when a comparably-priced clear lane exists. It has never
    /// looked at <b>Dangerous</b> at all - the word appears nowhere in <c>MovementPlanner</c> - so the
    /// official bot walked its units through ground that makes them take a Dangerous Terrain Test. And
    /// nothing anywhere tried to <b>end</b> a move in cover.
    /// </para>
    /// <para>
    /// <b>Why this adjusts the AIM POINT rather than the router.</b> <c>MovementPlanner</c> is shared with
    /// the Tactician and with the frozen benchmark baseline, which plan D1 forbids changing. Choosing
    /// where to stop is the solo resolver's own business, so the clause is implemented as "pick a better
    /// end point, then let the existing mover get there" - which also means a candidate is only ever
    /// accepted if the ordinary validated move can reach it.
    /// </para>
    /// <para>
    /// <b>The tree still outranks the terrain.</b> A unit heading for an objective does not give up the
    /// objective to stand in a wood: a candidate end point is rejected outright if it is more than
    /// <see cref="GoalSlackInches"/> further from the plan's goal than stopping normally would be. The
    /// book says "if possible", and the five-step tree is what decides what is possible.
    /// </para>
    /// </summary>
    public static class SoloCoOpTerrainPolicy
    {
        /// <summary>
        /// How much further from the plan's goal the AI will stop in order to reach cover or to stay off
        /// bad ground. The books give no number - "if possible" is the whole instruction - so this is the
        /// engine's judgement and it is recorded in <c>claude/house-rules.md</c>. 3" is one objective
        /// radius: far enough to reach the wood beside the marker, not far enough to abandon the marker.
        /// </summary>
        public const float GoalSlackInches = 3f;

        /// <summary>
        /// Ring radii sampled around the plain destination, in inches. The small ones matter as much as
        /// the large: stepping 1" clear of a swamp's edge, or 3" to the sheltered side of a wall, is the
        /// commonest form this clause takes, and a coarse ring would miss both and report that nothing
        /// better exists.
        /// </summary>
        private static readonly float[] SampleRadii = { 1f, 2f, 3f, 4f, 6f };

        /// <summary>Directions sampled on each ring. Eight is enough to find a piece of cover 6" away.</summary>
        private const int SampleDirections = 8;

        /// <summary>
        /// The end point this unit should aim at: the plain destination, or a nearby one that is in or
        /// behind Cover and off Difficult/Dangerous ground.
        /// </summary>
        /// <param name="plainDestination">Where the unit would stop with no terrain preference at all.</param>
        /// <param name="planGoal">What the decision tree is actually heading for - the thing not to give up.</param>
        /// <param name="ignoresDifficult">
        /// The request's own flag, which is the clause's "special rule that ignores its effects" (Strider,
        /// Flying). A unit with it may stand in bad ground freely, so only the cover half applies to it.
        /// </param>
        public static Position PreferredEndPoint(ITableState table, PlayerID me, IUnit unit,
            Position plainDestination, Position planGoal, bool ignoresDifficult)
        {
            List<ITerrain> terrain = table.Terrain.Objects.ToList();
            if (terrain.Count == 0) return plainDestination;

            float baseRadius = unit.Models.Where(m => m.GetIsAlive())
                .Select(m => m.BaseShape.BoundingRadiusInches)
                .DefaultIfEmpty(0.5f)
                .Max();

            float plainGoalDistance = Position.GetDistance2D(plainDestination, planGoal);
            float allowedGoalDistance = plainGoalDistance + GoalSlackInches;

            List<IUnit> enemies = AiCombatMath.Enemies(table, me);

            Candidate plain = Score(table, terrain, enemies, unit, plainDestination, planGoal, baseRadius,
                ignoresDifficult);

            // Only a TERRAIN improvement is a reason to move the end point. Distance to the goal is the
            // tie-break among the candidates that improve, never a reason on its own: this is a terrain
            // clause, and a policy that also shuffled the unit goal-wards would be quietly rewriting the
            // mover's own arithmetic for every move on an empty table.
            Candidate? best = null;
            foreach (Position sample in Samples(plainDestination))
            {
                if (Position.GetDistance2D(sample, planGoal) > allowedGoalDistance) continue;
                if (IsInside(terrain, ETerrainType.Impassible, sample, baseRadius)) continue;

                Candidate candidate = Score(table, terrain, enemies, unit, sample, planGoal, baseRadius,
                    ignoresDifficult);
                if (!candidate.ImprovesTerrainOn(plain)) continue;
                if (best == null || candidate.BeatsForTieBreak(best.Value)) best = candidate;
            }

            return best?.Point ?? plainDestination;
        }

        private static IEnumerable<Position> Samples(Position centre)
        {
            foreach (float radius in SampleRadii)
            {
                for (int i = 0; i < SampleDirections; i++)
                {
                    double angle = 2 * Math.PI * i / SampleDirections;
                    yield return new Position(
                        centre.x + radius * (float)Math.Cos(angle),
                        centre.z + radius * (float)Math.Sin(angle));
                }
            }
        }

        /// <summary>
        /// One candidate end point, graded on the clause's two instructions. <b>Bad ground is ranked
        /// ahead of cover</b> where the two disagree: the cover bonus is worth +1 to the enemy's save
        /// roll, while Difficult ground halves the move it took to get there and Dangerous ground rolls
        /// to wound the unit. The book gives no priority, so this is recorded as a judgement call.
        /// </summary>
        private static Candidate Score(ITableState table, List<ITerrain> terrain, List<IUnit> enemies,
            IUnit unit, Position point, Position planGoal, float baseRadius, bool ignoresDifficult)
        {
            bool onBadGround = !ignoresDifficult
                && (IsInside(terrain, ETerrainType.Difficult, point, baseRadius)
                    || IsInside(terrain, ETerrainType.Dangerous, point, baseRadius))
                && !BadGroundIsAllowed(table, terrain, enemies, unit, point, baseRadius);

            bool covered = IsInside(terrain, ETerrainType.Cover, point, baseRadius)
                || IsBehindCover(terrain, enemies, point, baseRadius);

            return new Candidate(point, onBadGround, covered,
                Position.GetDistance2D(point, planGoal));
        }

        /// <summary>
        /// The clause's three exceptions: "if there is an objective inside of it, if they are in charge
        /// range of a unit inside of it, or if they have a special rule that ignores its effects" (the
        /// third is the caller's <c>ignoresDifficult</c>, checked before this).
        /// </summary>
        private static bool BadGroundIsAllowed(ITableState table, List<ITerrain> terrain,
            List<IUnit> enemies, IUnit unit, Position point, float baseRadius)
        {
            foreach (ITerrain piece in terrain)
            {
                if (!piece.TerrainType.HasFlag(ETerrainType.Difficult)
                    && !piece.TerrainType.HasFlag(ETerrainType.Dangerous)) continue;
                if (!Contains(piece, point, baseRadius)) continue;

                // "an objective inside of it"
                foreach (IObjective objective in table.Objectives.Objects)
                {
                    if (piece.Shape.IsPointWithinZone(new Float2(objective.Position.x, objective.Position.z)))
                        return true;
                }

                // "in charge range of a unit inside of it" - measured from where the unit is NOW, since
                // that is what a charge would start from.
                foreach (IUnit enemy in enemies)
                {
                    if (!enemy.Models.Where(m => m.GetIsAlive()).Any(m =>
                            piece.Shape.IsPointWithinZone(new Float2(m.Position.x, m.Position.z)))) continue;
                    if (AiCombatMath.Distance(unit, enemy) <= GameWideConstants.CHARGE_DISTANCE_INCHES)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// "behind Cover terrain": a Cover or Blocking piece stands between this point and the nearest
        /// enemy. Checked against the nearest enemy only - it is the one most likely to shoot, and a point
        /// that hides from everything at once is a luxury the clause does not ask for.
        /// </summary>
        private static bool IsBehindCover(List<ITerrain> terrain, List<IUnit> enemies, Position point,
            float baseRadius)
        {
            IUnit? nearest = null;
            float best = float.MaxValue;
            foreach (IUnit enemy in enemies)
            {
                foreach (IModel model in enemy.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    float distance = Position.GetDistance2D(point, model.Position);
                    if (distance >= best) continue;
                    best = distance;
                    nearest = enemy;
                }
            }

            if (nearest == null) return false;

            IModel? shooter = nearest.Models.Where(m => m.GetIsAlive())
                .OrderBy(m => Position.GetDistance2D(point, m.Position))
                .FirstOrDefault();
            if (shooter == null) return false;

            var from = new Float2(shooter.Position.x, shooter.Position.z);
            var to = new Float2(point.x, point.z);
            foreach (ITerrain piece in terrain)
            {
                bool blocks = piece.TerrainType.HasFlag(ETerrainType.Cover)
                    || piece.TerrainType.HasFlag(ETerrainType.Blocking);
                if (!blocks) continue;
                // The end point itself being inside the piece is "into cover", handled by the caller;
                // here the piece has to be between the two, so a hit on the segment counts only when the
                // point is outside it.
                if (Contains(piece, point, baseRadius)) continue;
                if (piece.Shape.DoesPathIntersectZone(from, to)) return true;
            }

            return false;
        }

        private static bool IsInside(List<ITerrain> terrain, ETerrainType type, Position point,
            float baseRadius) =>
            terrain.Any(piece => piece.TerrainType.HasFlag(type) && Contains(piece, point, baseRadius));

        /// <summary>
        /// Whether a model standing at <paramref name="point"/> would be in <paramref name="piece"/>. The
        /// centre decides: a base clipping the edge of a wood is a judgement a human makes at the table,
        /// and inflating by the radius here would make a unit refuse to stand anywhere near cover at all.
        /// </summary>
        private static bool Contains(ITerrain piece, Position point, float baseRadius) =>
            piece.Shape.IsPointWithinZone(new Float2(point.x, point.z));

        /// <summary>
        /// A graded end point. Ordering, worst key first: on bad ground (never, unless allowed), not in
        /// cover, then distance from the plan's goal - so between two equally good pieces of cover the
        /// one nearer the objective wins.
        /// </summary>
        private readonly record struct Candidate(Position Point, bool OnBadGround, bool Covered,
            float GoalDistance)
        {
            /// <summary>
            /// True when this candidate is better than <paramref name="other"/> on the clause's own two
            /// instructions, and worse on neither: off bad ground where the other is on it, or in cover
            /// where the other is not. Bad ground ranks first - see <c>Score</c> for why.
            /// </summary>
            public bool ImprovesTerrainOn(Candidate other)
            {
                if (OnBadGround != other.OnBadGround) return !OnBadGround;
                if (Covered != other.Covered) return Covered;
                return false;
            }

            /// <summary>
            /// Between two candidates that both improve, the better one: off bad ground, then in cover,
            /// then nearest the plan's goal - so the AI takes the piece of cover closest to where it was
            /// going.
            /// </summary>
            public bool BeatsForTieBreak(Candidate other)
            {
                if (OnBadGround != other.OnBadGround) return !OnBadGround;
                if (Covered != other.Covered) return Covered;
                return GoalDistance < other.GoalDistance - 0.001f;
            }
        }
    }
}
