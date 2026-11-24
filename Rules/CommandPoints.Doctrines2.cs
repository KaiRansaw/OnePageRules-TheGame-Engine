using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// Command Points - the doctrine stratagems added in 3.24: Combat Fatigue, Eternal Vigilance, Total Shutdown,
    /// Hindered Advance, Minimize Losses and Tactical Retreat (GF p43, GFF p40, AoF p43, AoFS p40 - the same text).
    /// Judgement calls: house rules C, "Command Points - doctrine stratagems".
    /// </summary>
    public static partial class CommandPoints
    {
        // ================================================================= Shock - Combat Fatigue

        /// <summary>
        /// "Pick one enemy unit that is about to strike in melee and roll one die, on a 4+ that unit counts as Fatigued
        /// for that melee, but doesn't get Fatigued from that melee." Offered to the striker's opponents with the Shock
        /// Doctrine when it isn't Fatigued already. Bots say yes.
        /// </summary>
        public static async Task OfferCombatFatigue(IGameContext c, IUnit striker)
        {
            if (!On(c) || striker.Tokens.HasToken(TokenType.CpCombatFatigue)) return;
            foreach (PlayerID o in OpponentsOf(c, striker.PlayerID))
            {
                if (!HasDoctrine(c, o, CombatFatigue)) continue;
                if (!await Offer(c, o, CombatFatigue, $"{striker.Name} is about to strike in melee. Roll: on a 4+ it counts as Fatigued (hits only on 6s) for this melee?",
                        botSays: true, askOncePerRound: false)) continue;
                int roll = D6(c);
                if (roll >= 4)
                {
                    Mark(striker, TokenType.CpCombatFatigue);
                    await c.Announce($"Combat Fatigue ({roll}): {striker.Name} counts as Fatigued for this melee.", BattlefieldEffects.Amber, EBannerTier.Toast);
                }
                else await c.Announce($"Combat Fatigue ({roll}): no effect.", BattlefieldEffects.Amber, EBannerTier.Toast);
                return;
            }
        }

        // ================================================================= Defensive - Eternal Vigilance

        /// <summary>"Pick one friendly model that just suffered a wound which would kill it and roll one die, on a 4+ that
        /// wound is ignored." True = ignore one wound. Bots say yes.</summary>
        public static async Task<bool> OfferEternalVigilance(IGameContext c, IUnit unit, IModel model)
        {
            if (!On(c) || !HasDoctrine(c, unit.PlayerID, EternalVigilance)) return false;
            if (!await Offer(c, unit.PlayerID, EternalVigilance, $"A model of {unit.Name} is about to be killed. Roll: on a 4+ that wound is ignored?",
                    botSays: true, askOncePerRound: false)) return false;
            int roll = D6(c);
            await c.Announce($"Eternal Vigilance ({roll}): " + (roll >= 4 ? "the wound is ignored!" : "no luck."), BattlefieldEffects.Amber, EBannerTier.Toast);
            return roll >= 4;
        }

        // ================================================================= Tactical - Total Shutdown

        /// <summary>"At the end of the round, pick one objective marker under your control that is about to be seized by
        /// your opponent, which becomes neutral instead." Asked of the holder. Bots say yes.</summary>
        public static Task<bool> OfferTotalShutdown(IGameContext c, PlayerID holder, PlayerID seizer) =>
            On(c) && HasDoctrine(c, holder, TotalShutdown)
                ? Offer(c, holder, TotalShutdown, $"{c.GetPlayerName(seizer)} is about to seize one of your objectives. Make it neutral instead?",
                    botSays: true, askOncePerRound: false)
                : Task.FromResult(false);

        // ================================================================= Strategic - Hindered Advance

        /// <summary>"Pick one enemy unit that is moving through terrain, which counts as Dangerous terrain for that
        /// movement." Offered to the mover's opponents with the Strategic Doctrine when its path crosses a piece that
        /// isn't already Dangerous (and isn't decoration). A "no" isn't asked again that round. Bots say yes.</summary>
        public static async Task<bool> OfferHinderedAdvance(IGameContext c, IUnit mover, IReadOnlyList<ModelMoveEntry> paths,
            IEnumerable<ITerrain> terrain)
        {
            if (!On(c)) return false;
            var pieces = terrain.Where(t => t.TerrainType != ETerrainType.None
                && !t.TerrainType.HasFlag(ETerrainType.Dangerous) && !t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();
            if (pieces.Count == 0 || !paths.Any(p => p.Positions.Count > 0 && MovementUtilities.DoesPathCrossAnyOf(p, pieces)))
                return false;
            foreach (PlayerID o in OpponentsOf(c, mover.PlayerID))
                if (HasDoctrine(c, o, HinderedAdvance)
                    && await Offer(c, o, HinderedAdvance, $"{mover.Name} is moving through terrain. Make it count as Dangerous terrain for this move?",
                        botSays: true, askOncePerRound: true))
                    return true;
            return false;
        }

        // ================================================================= moving directly away

        /// <summary>The closest living enemy unit, by unit middles.</summary>
        private static IUnit? ClosestEnemy(IGameContext c, IUnit unit)
        {
            (float x, float z) = QuestActions.Centre(unit);
            var enemies = OpponentsOf(c, unit.PlayerID).ToHashSet();
            return c.TableState.Units.Objects
                .Where(u => enemies.Contains(u.PlayerID) && u.GetIsAlive() && u.Models.Any(m => m.GetIsAlive()))
                .OrderBy(u => { var (ex, ez) = QuestActions.Centre(u); return (ex - x) * (ex - x) + (ez - z) * (ez - z); })
                .FirstOrDefault();
        }

        /// <summary>The distance between two units' nearest bases.</summary>
        private static float Gap(IUnit a, IUnit b) =>
            a.Models.Where(m => m.GetIsAlive()).SelectMany(m => b.Models.Where(n => n.GetIsAlive()).Select(n =>
                MathF.Sqrt((m.Position.x - n.Position.x) * (m.Position.x - n.Position.x) + (m.Position.z - n.Position.z) * (m.Position.z - n.Position.z))
                - m.BaseShape.CircumscribedRadiusInches - n.BaseShape.CircumscribedRadiusInches))
            .DefaultIfEmpty(float.MaxValue).Min();

        /// <summary>
        /// "May move up to X\" directly away from the closest enemy": the whole unit moves as one block, straight away
        /// from the closest enemy's middle, as far as it can - it stops at anything in the way or the table edge (it's a
        /// voluntary move, so nothing hurts it and it never leaves the table). Returns the inches moved.
        /// </summary>
        public static float MoveAway(IGameContext c, IUnit unit, float inches)
        {
            if (unit is not UnitData data || ClosestEnemy(c, unit) is not { } from) return 0f;
            var living = data.Models.OfType<ModelData>().Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return 0f;
            (float fx, float fz) = QuestActions.Centre(from);
            (float tx, float tz) = QuestActions.Centre(unit);
            float dx = tx - fx, dz = tz - fz, len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) { dx = 1f; dz = 0f; len = 1f; }
            dx = dx / len * inches;
            dz = dz / len * inches;
            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight, t = 1f;
            foreach (ModelData m in living)
            {
                float r = m.BaseShape.CircumscribedRadiusInches;
                t = MathF.Min(t, QuestActions.Reach(m.Position.x, dx, r, w - r));
                t = MathF.Min(t, QuestActions.Reach(m.Position.z, dz, r, h - r));
            }
            t = MathF.Max(0f, MathF.Min(t, QuestActions.FreeTravel(c, data, living, dx, dz, 1f, inches)));
            foreach (ModelData m in living)
                m.SetPosition(new Position(m.Position.x + dx * t, m.Position.z + dz * t));
            return inches * t;
        }

        // ================================================================= Valorous - Minimize Losses

        /// <summary>"Pick one friendly unit that just became Shaken, which may move up to 4D3+4\" directly away from the
        /// closest enemy." Asked of the unit's player. Bots don't use it (running off objectives costs them more).</summary>
        public static async Task OfferMinimizeLosses(IGameContext c, IUnit unit)
        {
            if (!On(c) || !unit.GetIsAlive() || ClosestEnemy(c, unit) is not { } enemy || !HasDoctrine(c, unit.PlayerID, MinimizeLosses)) return;
            if (!await Offer(c, unit.PlayerID, MinimizeLosses, $"{unit.Name} is Shaken. Fall back 4D3+4\" directly away from {enemy.Name}?",
                    botSays: false, askOncePerRound: false)) return;
            int roll = D3(c) + D3(c) + D3(c) + D3(c) + 4;
            float moved = MoveAway(c, unit, roll);
            await c.Announce($"Minimize Losses: {unit.Name} falls back {moved:0.#}\" (rolled {roll}\").", BattlefieldEffects.Amber, EBannerTier.Toast);
        }

        // ================================================================= Defensive - Tactical Retreat

        /// <summary>Whether Tactical Retreat can go on the unit's menu: Defensive Doctrine, 3 CP, an enemy within 12".</summary>
        public static bool CanTacticalRetreat(IGameContext c, IUnit unit) =>
            On(c) && Can(c, unit.PlayerID, TacticalRetreat) && ClosestEnemy(c, unit) is { } e && Gap(unit, e) <= 12f;

        /// <summary>"Pick one friendly unit that is within 12\" of an enemy, and it may move up to 2D3+2\" directly away
        /// from the closest enemy." Picked from the action menu; the unit then goes on with its activation.</summary>
        public static async Task TakeTacticalRetreat(IGameContext c, IUnit unit)
        {
            if (!CanTacticalRetreat(c, unit) || !Use(c, unit.PlayerID, TacticalRetreat)) return;
            int roll = D3(c) + D3(c) + 2;
            float moved = MoveAway(c, unit, roll);
            await c.Announce($"Tactical Retreat: {unit.Name} falls back {moved:0.#}\" (rolled {roll}\").", BattlefieldEffects.Amber, EBannerTier.Toast);
        }
    }
}
