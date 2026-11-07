using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - where the official AI puts a unit at deployment, from <b>Solo &amp; Co-Op Rules v3.6.0</b>:
    ///
    /// <para>
    /// "AI units are randomly divided into <b>3 groups</b> of equal size (as far as possible). To deploy,
    /// divide the table into <b>3 sections</b> along the AI's deployment zone table edge [<b>2</b> in AoF:
    /// Skirmish] ... For each group roll a D3 to determine which section its units deploy in, re-rolling if
    /// all 3 groups would deploy in the same section. Then deploy one random unit at a time in its section,
    /// <b>as close as possible to the closest objective</b>, and <b>outside of Difficult and Dangerous
    /// terrain</b> (unless the unit has either Strider or Flying)."
    /// </para>
    /// <para>
    /// Only the <i>aim point</i> is this class's business. Everything that makes a deployment legal - packing
    /// the models into a cohesion-valid block, clearing the zone shape, impassable terrain and units already
    /// down, spiral-searching outward when the aim is blocked - is inherited from
    /// <see cref="AiPlaceObjectsResolver{T}"/>, which is 400-odd lines of geometry it would be madness to
    /// re-derive. The Tactician overrides the same seam for the same reason.
    /// </para>
    /// <para>
    /// What changes for a player: the baseline fans units out in lanes across its whole zone, evenly and
    /// predictably. The book instead bunches each group into one third of the edge and points it at the
    /// nearest objective, so the AI arrives already committed to a part of the table - which is what makes
    /// its first two rounds look like a plan rather than a wall advancing.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpPlaceObjectsResolver<T> : AiPlaceObjectsResolver<T>
    {
        private readonly ITableState _table;
        private readonly SoloCoOpDeploymentPlan _plan;

        public SoloCoOpPlaceObjectsResolver(ITableState table, SoloCoOpDeploymentPlan plan) : base(table)
        {
            _table = table;
            _plan = plan;
        }

        protected override Position PreferredBlockCenter(PlaceObjectsRequest<T> request, ZoneBounds bounds,
            int deployIndex, float maxRadius, float gridWidth, float gridHeight, float spacingX)
        {
            SoloCoOpProfile profile = SoloCoOpProfile.For(_table.Progress.GameSystem);
            int section = _plan.SectionFor(deployIndex, profile);

            // The sections run along the deployment zone's table edge, so they divide the zone across its
            // LONGER axis - which for every deployment zone the engine builds is its width.
            float usableLeft = bounds.Left + maxRadius;
            float usableRight = bounds.Right - maxRadius;
            float span = MathF.Max(0f, usableRight - usableLeft);
            float sectionWidth = span / profile.DeploymentSections;
            float sectionLeft = usableLeft + sectionWidth * (section - 1);
            float sectionRight = sectionLeft + sectionWidth;

            // "as close as possible to the closest objective": aim at the point of this section nearest the
            // nearest marker. With no markers on the table yet (some scenarios place none) the section's
            // own centre is the honest answer.
            Position? objective = NearestObjective(new Position((sectionLeft + sectionRight) / 2f, bounds.CenterZ));
            if (objective == null)
                return new Position((sectionLeft + sectionRight) / 2f, bounds.CenterZ);

            float aimX = Math.Clamp(objective.Value.x, sectionLeft, sectionRight);
            float aimZ = Math.Clamp(objective.Value.z, bounds.Bottom + maxRadius, bounds.Top - maxRadius);

            // "outside of Difficult and Dangerous terrain (unless the unit has either Strider or Flying)".
            // Nudged along the section, not across it, so the unit stays in the third its group was given.
            if (!IgnoresBadGround(request)) aimX = SlideClearOfBadGround(aimX, aimZ, sectionLeft, sectionRight);

            DeployTrace?.Invoke(request.TargetPlayerID, section, new Position(aimX, aimZ));
            return new Position(aimX, aimZ);
        }

        /// <summary>Test hook for the bot audit: the section each deployment was given and where it aimed.</summary>
        internal static Action<PlayerID, int, Position>? DeployTrace;

        /// <summary>
        /// "outside of Difficult and Dangerous terrain (unless the unit has either Strider or Flying)" for every
        /// MODEL, not just the aim point - the block is packed round the aim, so its outer models could still
        /// land in a wood the aim missed (bot audit, 2026-11-07: 5 units in 12 games). Weighted well below
        /// leaving the zone or standing in a building, so a zone that is all bad ground still deploys.
        /// </summary>
        protected override float ExtraModelPenalty(PlaceObjectsRequest<T> request, Position modelCentre, float radius)
            => IgnoresBadGround(request) || IsClear(modelCentre.x, modelCentre.z) ? 0f : BadGroundPenalty;

        private const float BadGroundPenalty = 50f;

        private Position? NearestObjective(Position from)
        {
            IObjective? closest = _table.Objectives.Objects
                .OrderBy(o => Position.GetDistance2D(o.Position, from))
                .FirstOrDefault();
            return closest?.Position;
        }

        /// <summary>
        /// Strider and Flying both ignore the effects of Difficult and Dangerous terrain, and the book
        /// names them as the exception, so a unit with either may be aimed straight at the objective.
        /// </summary>
        private bool IgnoresBadGround(PlaceObjectsRequest<T> request)
        {
            foreach (DataBinding<T> binding in request.ModelsToPlace)
            {
                if (binding.GetValue() is not IModel model) continue;
                IUnit? owner = _table.Units.Objects.FirstOrDefault(u => u.Models.Contains(model));
                if (owner is not UnitData unit) continue;

                foreach (ResolvedRule rule in unit.RuleDefinitions)
                {
                    if (rule.Definition == CoreRuleCatalog.Strider
                        || rule.Definition == CoreRuleCatalog.Flying) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Steps the aim along the section until it is clear of Difficult and Dangerous ground, taking the
        /// nearest clear point to where it started. Returns the original aim when the whole section is bad
        /// ground - the base class's spiral search is a better judge of a cramped zone than a guess here,
        /// and the book's "as close as possible" has to give somewhere.
        /// </summary>
        private float SlideClearOfBadGround(float aimX, float aimZ, float sectionLeft, float sectionRight)
        {
            if (IsClear(aimX, aimZ)) return aimX;

            const float StepInches = 1f;
            float span = sectionRight - sectionLeft;
            int steps = Math.Max(1, (int)(span / StepInches));
            for (int i = 1; i <= steps; i++)
            {
                float offset = i * StepInches;
                if (aimX + offset <= sectionRight && IsClear(aimX + offset, aimZ)) return aimX + offset;
                if (aimX - offset >= sectionLeft && IsClear(aimX - offset, aimZ)) return aimX - offset;
            }

            return aimX;
        }

        private bool IsClear(float x, float z)
        {
            var point = new Float2(x, z);
            foreach (ITerrain piece in _table.Terrain.Objects)
            {
                bool bad = piece.TerrainType.HasFlag(ETerrainType.Difficult)
                    || piece.TerrainType.HasFlag(ETerrainType.Dangerous);
                if (!bad) continue;
                if (piece.Shape.IsPointWithinZone(point)) return false;
            }

            return true;
        }
    }
}
