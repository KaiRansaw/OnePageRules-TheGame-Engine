using FDG.Missions;
using FDG.Rules.Foundation;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// Bots and the Extra Missions whose prize is carried to a table edge (Smash &amp; Grab's relic, The Rescue's marker).
    /// The Solo &amp; Co-Op tree only knows "take and hold objectives", and a carried marker is no longer on the table, so
    /// the tree never saw it: the carrier stood about and the other side ignored it (overnight sweep, 2026-11-22: the
    /// prize never reached an edge in 24 games). House rules C, "Bots and the missions":
    /// <list type="bullet">
    /// <item>the unit carrying the prize rushes for the nearest table edge;</item>
    /// <item>every other unit on the other side goes after the carrier - charge it if it can, else shoot it, else
    /// rush toward it;</item>
    /// <item>the carrier's own side plays the book's tree as before.</item>
    /// </list>
    /// </summary>
    public static class SoloAiMissions
    {
        /// <summary>The mission's plan for this unit, or null to let the book's tree decide.</summary>
        public static SoloAiPlan? Plan(ITableState table, PlayerID me, IUnit unit)
        {
            ExtraMissionState? s;
            try { s = GameProgressUtilities.ExtraMission(table.DataStore); }
            catch (Exception) { return null; }
            if (s == null || s.Mission is not (EExtraMission.SmashAndGrab or EExtraMission.TheRescue) || s.Carried.Count == 0)
                return null;
            string carrierId = s.Carried[0].Unit;
            IUnit? carrier = table.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == carrierId && u.GetIsAlive());
            if (carrier == null) return null;

            if (ReferenceEquals(carrier, unit) || carrier.ID.ID == unit.ID.ID)
                return new SoloAiPlan(ESoloAiMove.RushToObjective, null, EdgeGoal(unit), 0f,
                    "carrying the prize - rush for the table edge");

            if (ITeamExtensions.AreAllied(table.Teams.Objects, me, carrier.PlayerID)) return null;
            if (unit.Tokens.HasToken(TokenType.Shaken)) return null;

            float reach = SoloAiBrain.ChargeReach(unit, table);
            if (AiCombatMath.Distance(unit, carrier) <= reach && SoloAiReach.ChargeGap(table, unit, carrier) <= reach)
                return new SoloAiPlan(ESoloAiMove.Charge, carrier, null, 0f, $"charge {carrier.Name}, who carries the prize");

            float range = AiCombatMath.MaxRange(unit);
            Position at = Centre(carrier);
            if (range > 0f && AiCombatMath.Distance(unit, carrier) <= range + SoloAiBrain.AdvanceReach(unit, table)
                && AiCombatMath.Sees(unit, carrier, table.Terrain.Objects.ToList()))
                return new SoloAiPlan(ESoloAiMove.AdvanceToObjective, carrier, at, range, $"close in and shoot {carrier.Name}, who carries the prize");

            return new SoloAiPlan(ESoloAiMove.RushToObjective, null, at, 0f, $"chase {carrier.Name}, who carries the prize");
        }

        private static Position Centre(IUnit unit)
        {
            var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (alive.Count == 0) return unit.Models.First().Position;
            return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
        }

        /// <summary>A point 2" in from the table edge nearest the unit.</summary>
        public static Position EdgeGoal(IUnit unit)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            Position c = Centre(unit);
            float left = c.x, right = w - c.x, bottom = c.z, top = h - c.z;
            float min = MathF.Min(MathF.Min(left, right), MathF.Min(bottom, top));
            const float inset = 2f;
            if (min == left) return new Position(inset, c.z);
            if (min == right) return new Position(w - inset, c.z);
            if (min == bottom) return new Position(c.x, inset);
            return new Position(c.x, h - inset);
        }
    }
}
