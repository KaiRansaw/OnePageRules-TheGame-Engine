using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// Which way a model looks (Kai, 2026-11-12: "all models deployed the same direction... id also want the models to
    /// turn and look which way they move and shoot").
    ///
    /// <para><b>Round bases turn freely.</b> In OPR a model's facing changes nothing for a round base - range, line of
    /// sight and contact are measured from the base all round - so for those it is pure presentation, kept in the
    /// engine (not the screen) so saves, a networked friend and every front end agree. A non-round base (a vehicle's
    /// oval or rectangle) is different: its facing decides the ground it covers, so it only turns when a move or a
    /// placement says so - never to shoot (Kai's call, 2026-11-12).</para>
    ///
    /// <list type="bullet">
    /// <item>Placed with no facing given (a player's click-to-place) -> faces the table centre, i.e. the enemy side.</item>
    /// <item>Moved with no facing given (AI moves, pile-in, consolidation) -> faces the way it went, and stays so.</item>
    /// <item>Shooting or fighting -> faces the nearest model of the unit it attacks, and stays so.</item>
    /// </list>
    /// </summary>
    public static class FacingRules
    {
        /// <summary>True when turning the model can't change anything the rules measure.</summary>
        public static bool TurnsFreely(IModel model) => model.BaseShape is CircleBase;

        /// <summary>The unit direction from one point to another on the table, or null when they coincide.</summary>
        public static Float2? Toward(Position from, Position to)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            return len < 1e-3f ? null : new Float2(dx / len, dz / len);
        }

        /// <summary>
        /// A placement's facing: the one given, else (round base) towards the table centre. A non-round base with
        /// none given keeps the facing its footprint was checked with.
        /// </summary>
        public static void ApplyPlaced(ModelData model, Float2? given, GameSettings settings)
        {
            if (given.HasValue) { model.SetFacing(given.Value); return; }
            if (!TurnsFreely(model)) return;
            (float w, float h) = TableSizing.Resolve(settings);
            if (Toward(model.Position, new Position(w / 2f, 0f, h / 2f)) is { } centre) model.SetFacing(centre);
        }

        /// <summary>A round-base model that moved from <paramref name="from"/> to <paramref name="to"/> faces that way.</summary>
        public static void ApplyTravel(ModelData model, Position from, Position to)
        {
            if (TurnsFreely(model) && Toward(from, to) is { } way) model.SetFacing(way);
        }

        /// <summary>The attacking unit's round-base models each turn to the nearest living model of the unit attacked.</summary>
        public static void FaceTarget(IUnit? attacker, IUnit? target)
        {
            if (attacker == null || target == null) return;
            List<IModel> marks = target.Models.Where(m => m.GetIsAlive()).ToList();
            if (marks.Count == 0) return;
            foreach (IModel model in attacker.Models)
            {
                if (!model.GetIsAlive() || !TurnsFreely(model)) continue;
                IModel nearest = marks.OrderBy(t => Dist2(model.Position, t.Position)).First();
                if (Toward(model.Position, nearest.Position) is { } way) model.SetFacing(way);
            }
        }

        private static float Dist2(Position a, Position b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return dx * dx + dz * dz;
        }
    }
}
