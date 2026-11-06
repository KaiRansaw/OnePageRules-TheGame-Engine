using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Places units set aside during normal deployment (Scout). Runs once, after the normal
    /// deploy loop finishes, for every <see cref="IDeploymentTurnContext.DeferredUnits"/> entry
    /// whose timing is <see cref="EDeferTiming.AfterNormalDeployment"/>: each is placed within a
    /// forward-expanded version of its owner's deployment zone (extended toward table center by the
    /// rule's range), reusing the normal <see cref="PlaceObjectsRequest{T}"/> flow.
    /// </summary>
    public class PlaceDeferredUnitsStage : StageBase<IDeploymentTurnContext>
    {
        public StageBinding OnFinish;

        public PlaceDeferredUnitsStage(IGameContext gameContext, IStateMachineLayer<IDeploymentTurnContext> parent)
            : base(gameContext, parent)
        {
            OnFinish = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentTurnContext context)
        {
            context.LogDebug("Entered Place Deferred Units stage.");

            // Only Scout-style (AfterNormalDeployment) units reach the deferred pool. Ambush units are
            // held interactively at selection time (ChooseUnitToDeployStage) and stay off-table until a
            // later round, so they never appear here.
            // #420: "Players alternate in placing Scout units, starting with the player that activates
            // next." DeferredUnits is built team -> player -> army -> unit, so walking it straight placed
            // every Scout one player owned before the other placed any - and with two forward screens a
            // side, the second player sees the whole enemy screen before committing. Interleaving by
            // owner is the whole fix; each owner's own units keep their relative order.
            List<DeferredUnitEntry> inTurnOrder = Rules.Dispatch.AlternatingPlacement.Interleave(
                context.DeferredUnits.Where(e => e.Defer.Timing == EDeferTiming.AfterNormalDeployment),
                entry => entry.Unit.GetValue().PlayerID,
                Rules.Dispatch.AlternatingPlacement.ActivationOrder(context.FirstDeploymentRollOrder));

            foreach (DeferredUnitEntry entry in inTurnOrder)
            {

                UnitData unit = entry.Unit.GetValue();
                RectangularZone forwardZone = BuildForwardZone(context, unit, entry.Defer.PlacementRangeInches);

                // #409 (OPR 3.6.0): Scout gained "over 9\" away from non-Scout enemy units". Built as
                // per-source keep-out discs rather than the flat MinDistanceFromEnemiesInches, because the
                // rule has an exception that flat field cannot express - enemy SCOUTS don't push a Scout
                // back, so two forward screens can still meet near the middle. Snapshotted here at request
                // build: nothing on the table moves while this placement is being resolved.
                IReadOnlyList<PlacementDisc> keepOut =
                    Rules.Dispatch.ScoutDeploymentRules.EnemyKeepOutDiscs(unit, GameContext.TableState);

                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, "Place Scout Unit",
                    forwardZone, unit.ModelBindings,
                    displayName: $"Deploying {unit.Name} (Scout)",
                    enemyKeepOutDiscs: keepOut);

                // #282: commit-time overlap check, same as normal deployment.
                List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard
                    .RequestClearPlacement(GameContext, request);

                // On the table is the negation of in reserve (a Scout defers within deployment, so it never
                // carries the token; a legacy save's bootstrap may have stamped it). #309: cleared BEFORE
                // the positions so a networked client never captures an on-table position paired with a
                // still-reserved unit state.
                Rules.Dispatch.ReserveRules.ClearReserve(unit);

                foreach (PlacedObjectEntry<ModelData> placement in placements)
                {
                    placement.Binding.GetValue().SetPosition(placement.Position);
                    if (placement.Facing.HasValue) placement.Binding.GetValue().SetFacing(placement.Facing.Value);
                }

                context.Log($"{unit.Name} deployed via Scout.");
            }

            await OnFinish.Activate(context);
        }

        /// <summary>
        /// The owner's deployment zone extended toward the middle of the table by
        /// <paramref name="rangeInches"/>, clamped at the middle so a Scout cannot cross it.
        ///
        /// <para>
        /// <b>On BOTH axes, and never shrinking.</b> This used to assume every deployment zone was a band
        /// across the bottom or the top of the table, and extended only in Z. That is true of every zone
        /// the game generates, and false of a hand-drawn one from the Map Maker - which is how Kai found
        /// it (2026-11-02, "scout doesn't seem to work with the custom deployment zones on the custom map
        /// i made").
        /// </para>
        /// <para>
        /// It did not merely fail to widen. A zone spanning the table's full height - a LEFT or RIGHT
        /// zone, which a hand-drawn map can easily have - has its centre exactly on the midline, so the
        /// old code took the "top zone" branch and moved the zone's BOTTOM edge up to the midline. The
        /// Scout's zone came out as the top half of its own deployment zone: smaller than where it could
        /// have stood by deploying normally, and in the wrong place. A rule that silently takes something
        /// away is worse than one that silently does nothing.
        /// </para>
        /// <para>
        /// Each axis is treated on its own: a zone entirely on one side of the middle has its inner edge
        /// pushed toward the middle; a zone that already spans the middle on that axis is left alone,
        /// because there is no "forward" along it. That reproduces the old behaviour exactly for the
        /// standard top and bottom bands, and does the right thing for left/right, corner and arbitrary
        /// hand-drawn rectangles.
        /// </para>
        /// </summary>
        private RectangularZone BuildForwardZone(IDeploymentTurnContext context, UnitData unit, float rangeInches)
        {
            ITeam ownerTeam = context.PlayerDeploymentZones!.Keys
                .First(team => team.Players.Contains(unit.PlayerID));
            RectangularZone zone = context.PlayerDeploymentZones[ownerTeam].GetValue();

            // The LIVE table, not a constant: ConfigureTable sets these per game system at launch, so a
            // 48x48 Firefight table and a 72x48 Grimdark Future one both get their own middle.
            return ForwardZone(zone, rangeInches,
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);
        }

        /// <summary>
        /// The arithmetic of <see cref="BuildForwardZone"/>, with no table state - so the awkward zone
        /// shapes that broke it can be tested directly.
        /// </summary>
        private static PolygonZone PushPolygonForward(PolygonZone zone, float rangeInches, float middleX, float middleZ)
        {
            var parts = new List<float[]>();
            foreach (IReadOnlyList<Float2> part in zone.Vertices)
            {
                float cx = part.Average(v => v.X), cz = part.Average(v => v.Y);
                float dx = middleX - cx, dz = middleZ - cz, len = MathF.Sqrt(dx * dx + dz * dz);
                var pts = new List<Float2>(part);
                if (len > 0.001f)
                    foreach (Float2 v in part)
                        pts.Add(new Float2(v.X + dx / len * MathF.Min(rangeInches, len),
                                           v.Y + dz / len * MathF.Min(rangeInches, len)));
                parts.Add(ConvexHull(pts).SelectMany(v => new[] { v.X, v.Y }).ToArray());
            }
            return new PolygonZone(parts);
        }

        /// <summary>Monotone-chain convex hull, counter-clockwise.</summary>
        private static List<Float2> ConvexHull(List<Float2> pts)
        {
            var p = pts.OrderBy(v => v.X).ThenBy(v => v.Y).ToList();
            if (p.Count < 3) return p;
            float Cross(Float2 o, Float2 a, Float2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            var hull = new List<Float2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (Float2 v in pass == 0 ? p : Enumerable.Reverse(p))
                {
                    while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], v) <= 0f) hull.RemoveAt(hull.Count - 1);
                    hull.Add(v);
                }
                hull.RemoveAt(hull.Count - 1);
            }
            return hull;
        }

        internal static RectangularZone ForwardZone(RectangularZone zone, float rangeInches,
                                                    float middleX, float middleZ)
        {
            // An odd-shaped zone is pushed forward PART BY PART, and the result is the union of the
            // extended parts. Taking its bounding box instead would hand a Scout the notch of an L -
            // ground its own unit was never allowed to deploy in - which is a bigger gift than Scout
            // is meant to be. (Kai reported the plain-rectangle version of this bug on 2026-11-02;
            // L-shaped zones arrived the same day and would have reintroduced it.)
            if (zone is MultiRectZone multi)
            {
                var pushed = new List<RectangularZone>(multi.Parts.Count);
                foreach (RectangularZone part in multi.Parts)
                    pushed.Add(ForwardZone(part, rangeInches, middleX, middleZ));
                return MultiRectZone.Of(pushed);
            }

            // A triangle or wedge zone (Advanced deployment styles) is swept toward the middle as a whole:
            // the convex hull of each part and the same part moved forward by the range - exactly the ground
            // it passes over, never the empty corners of its bounding box.
            if (zone is PolygonZone poly)
                return PushPolygonForward(poly, rangeInches, middleX, middleZ);

            float left = zone.Left, right = zone.Right, bottom = zone.Bottom, top = zone.Top;

            if (right <= middleX) right = Math.Min(right + rangeInches, middleX);
            else if (left >= middleX) left = Math.Max(left - rangeInches, middleX);

            if (top <= middleZ) top = Math.Min(top + rangeInches, middleZ);
            else if (bottom >= middleZ) bottom = Math.Max(bottom - rangeInches, middleZ);

            return new RectangularZone(left, right, bottom, top);
        }
    }
}
