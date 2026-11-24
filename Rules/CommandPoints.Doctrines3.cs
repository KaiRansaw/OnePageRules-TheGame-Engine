using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>
    /// Command Points - the doctrine stratagems added in 3.25: Closing Fire and Frenzied Attack (reaction shootings, run
    /// by <see cref="OverwatchStage"/>), Vanish (end of the round), Hit &amp; Run (after a melee), Coordinated Move and
    /// Strategic Relocation (action menu). GF p43, GFF p40, AoF p43, AoFS p40 - the same text.
    /// Judgement calls: house rules C, "Command Points - doctrine stratagems".
    /// </summary>
    public static partial class CommandPoints
    {
        // ================================================================= who the active unit shot at

        private sealed class Shots { public readonly Dictionary<UnitID, HashSet<UnitID>> By = new(); }

        /// <summary>The units each unit shot at this activation (Frenzied Attack: "another enemy unit").</summary>
        private static readonly ConditionalWeakTable<object, Shots> ShotTargets = new();

        /// <summary>Notes a shooting attack (called from the hit roll).</summary>
        public static void NoteShot(IGameContext c, IUnit shooter, IUnit target)
        {
            if (!On(c)) return;
            var by = ShotTargets.GetOrCreateValue(c.GameDataStore).By;
            if (!by.TryGetValue(shooter.ID, out var set)) by[shooter.ID] = set = new HashSet<UnitID>();
            set.Add(target.ID);
        }

        public static IReadOnlyCollection<UnitID> ShotAt(IGameContext c, UnitID shooter) =>
            ShotTargets.TryGetValue(c.GameDataStore, out var s) && s.By.TryGetValue(shooter, out var set)
                ? set : (IReadOnlyCollection<UnitID>)Array.Empty<UnitID>();

        // ================================================================= Hunting - Closing Fire / Frenzied Attack

        /// <summary>
        /// Closing Fire: "Pick one friendly unit that is being charged by an enemy unit, which may shoot at the charging
        /// unit at any point during its movement, but gets -1 to hit rolls." Asked of the charged unit's player once the
        /// charger's move has brought it into contact (as Overwatch; the engine moves a unit as one path). Bots say yes.
        /// True = the unit shoots now, marked for the -1.
        /// </summary>
        public static async Task<bool> OfferClosingFire(IGameContext c, IUnit charged, IUnit charger)
        {
            if (!On(c) || !HasDoctrine(c, charged.PlayerID, ClosingFire)) return false;
            if (!await Offer(c, charged.PlayerID, ClosingFire, $"{charger.Name} is charging {charged.Name}. Shoot at it first (-1 to hit)?",
                    botSays: true, askOncePerRound: false)) return false;
            Mark(charged, TokenType.CpClosingFire);
            return true;
        }

        /// <summary>Whether a unit standing in contact with <paramref name="mover"/> after its move was just charged.</summary>
        public static bool InContact(IUnit unit, IUnit mover) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(unit, mover, out _, out _, includeVertical: true)
                <= GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + 0.01f;

        /// <summary>
        /// Frenzied Attack: "Pick one friendly unit that shot at an enemy unit, which may shoot at another enemy unit
        /// within 12\" of it, but only hits on unmodified results of 6." Offered after the unit's shooting; "it" is read
        /// as the enemy it shot. Returns the picked target, or null. Bots take it while they have 4+ CP.
        /// </summary>
        public static async Task<UnitID?> OfferFrenziedAttack(IGameContext c, IUnit shooter, IReadOnlyList<IUnit> targets)
        {
            if (!On(c) || targets.Count == 0 || !Can(c, shooter.PlayerID, FrenziedAttack)) return null;
            var options = targets.Select(t => t.Name).Distinct().ToList();
            options.Add(NoThanks);
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(shooter.PlayerID,
                $"Command Points ({Cp(c, shooter.PlayerID)}): Frenzied Attack for 2 CP - {shooter.Name} shoots another enemy within 12\" of the one it shot, hitting only on 6s?",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Deciding on a stratagem")
            { Recommended = Of(c, shooter.PlayerID) >= 4 ? options[0] : NoThanks });
            IUnit? pick = targets.FirstOrDefault(t => t.Name == answer);
            if (pick == null || !Use(c, shooter.PlayerID, FrenziedAttack)) return null;
            Mark(shooter, TokenType.CpFrenziedAttack);
            await c.Announce($"{c.GetPlayerName(shooter.PlayerID)}: Frenzied Attack - {shooter.Name} turns on {pick.Name}!", BattlefieldEffects.Amber, EBannerTier.Toast);
            return pick.ID;
        }

        /// <summary>The enemies a Frenzied Attack could shoot: not shot this activation, within 12\" of one that was.</summary>
        public static List<IUnit> FrenzyTargets(IGameContext c, IUnit shooter)
        {
            var shot = ShotAt(c, shooter.ID);
            if (shot.Count == 0) return new List<IUnit>();
            var enemies = OpponentsOf(c, shooter.PlayerID).ToHashSet();
            var shotUnits = c.TableState.Units.Objects.Where(u => shot.Contains(u.ID)).ToList();
            return c.TableState.Units.Objects
                .Where(u => enemies.Contains(u.PlayerID) && u.GetIsAlive() && u.GetIsOnBattlefield() && !shot.Contains(u.ID))
                .Where(u => shotUnits.Any(s => UnitCompareUtilities.MinDistanceBetweenUnits(s, u, out _, out _, includeVertical: true) <= 12f))
                .ToList();
        }

        // ================================================================= Tactical - Vanish

        private static bool HasAmbush(IGameContext c, IUnit unit) =>
            c.RuleEvaluator.EvaluateAllNamed(new PreDeploymentSelectContext(unit), RuleParticipant.Actor(unit))
                .Any(n => n.Op is RuleOperation.DeferDeployment d && d.Timing == EDeferTiming.LaterRound
                    && n.RuleName.Contains("Ambush", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Vanish: "At the end of the round, pick one friendly unit with Ambush and remove it from play (dropping any
        /// objectives), and which may be deployed again starting from the next round." Asked of every player with the
        /// Tactical Doctrine and a unit with Ambush on the table. Bots don't use it.
        /// </summary>
        public static async Task OfferVanish(IGameContext c)
        {
            if (!On(c)) return;
            foreach (PlayerID p in c.TableState.Teams.Objects.SelectMany(t => t.Players).ToList())
            {
                if (!Can(c, p, Vanish)) continue;
                var units = c.TableState.Units.Objects
                    .Where(u => u.PlayerID.Equals(p) && u.GetIsAlive() && u.GetIsOnBattlefield()
                        && !ReserveRules.IsInReserve(u) && !TransportUtilities.IsEmbarked(u) && HasAmbush(c, u))
                    .ToList();
                if (units.Count == 0) continue;
                var options = units.Select(u => u.Name).Distinct().ToList();
                options.Add(NoThanks);
                string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(p,
                    $"Command Points ({Cp(c, p)}): Vanish for 2 CP - take a unit with Ambush off the table (it drops any objectives) to deploy it again from next round?",
                    options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Deciding on a stratagem")
                { Recommended = NoThanks });
                IUnit? unit = units.FirstOrDefault(u => u.Name == answer);
                if (unit == null || !Use(c, p, Vanish)) continue;
                await new GameOperationServices(c).RedeployAsAmbush(unit);
            }
        }

        // ================================================================= Shock - Hit & Run

        /// <summary>"Pick one friendly unit that has just finished fighting in melee, which may move by up to 3\" in any
        /// direction." Asked of each side's player after the melee (not for a Shaken unit - Shaken units stay idle).
        /// Bots don't use it.</summary>
        public static async Task OfferHitAndRun(IGameContext c, IUnit unit)
        {
            if (!On(c) || !unit.GetIsAlive() || unit.Tokens.HasToken(TokenType.Shaken) || !HasDoctrine(c, unit.PlayerID, HitAndRun)) return;
            if (!await Offer(c, unit.PlayerID, HitAndRun, $"{unit.Name} has finished fighting. Move it up to 3\" in any direction?",
                    botSays: false, askOncePerRound: false)) return;
            await new GameOperationServices(c).MoveUnit(unit, 3f, isOptional: true);
        }

        // ================================================================= Strategic - Coordinated Move / Strategic Relocation

        /// <summary>The friendly units a Coordinated Move may pair with: alive, on the table, within 12\".</summary>
        public static List<IUnit> CoordinatedPartners(IGameContext c, IUnit unit) =>
            c.TableState.Units.Objects
                .Where(u => u.PlayerID.Equals(unit.PlayerID) && u.ID != unit.ID && u.GetIsAlive() && u.GetIsOnBattlefield()
                    && !TransportUtilities.IsEmbarked(u) && !u.Tokens.HasToken(TokenType.Shaken)
                    && UnitCompareUtilities.MinDistanceBetweenUnits(unit, u, out _, out _, includeVertical: true) <= 12f)
                .ToList();

        /// <summary>
        /// Coordinated Move: "During your turn, pick two friendly units that are within 12\" of each other, which may each
        /// move by up to 3\" in any direction." From the activating unit's menu: pick the second unit, then each moves.
        /// True when it was used (cancelling the pick spends nothing).
        /// </summary>
        public static async Task<bool> TakeCoordinatedMove(IGameContext c, IUnit unit)
        {
            var partners = CoordinatedPartners(c, unit);
            if (partners.Count == 0 || !Can(c, unit.PlayerID, CoordinatedMove)) return false;
            var options = partners.Select(u => u.Name).Distinct().ToList();
            options.Add("Cancel");
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(unit.PlayerID,
                $"Coordinated Move: which friendly unit within 12\" of {unit.Name} moves with it (up to 3\" each)?",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Deciding on a stratagem")
            { Recommended = options[0] });
            IUnit? partner = partners.FirstOrDefault(u => u.Name == answer);
            if (partner == null || !Use(c, unit.PlayerID, CoordinatedMove)) return false;
            await c.Announce($"Coordinated Move: {unit.Name} and {partner.Name}.", BattlefieldEffects.Amber, EBannerTier.Toast);
            var ops = new GameOperationServices(c);
            await ops.MoveUnit(unit, 3f, isOptional: true);
            await ops.MoveUnit(partner, 3f, isOptional: true);
            return true;
        }

        /// <summary>
        /// Strategic Relocation: "Pick one friendly unit that has just been activated, but that hasn't done anything yet,
        /// which may be placed anywhere within 6\" of its position." From a fresh activation's menu; each model is placed
        /// within 6\" of where it stood (the Teleport placement). True when placed (backing out spends nothing).
        /// </summary>
        public static async Task<bool> TakeStrategicRelocation(IGameContext c, UnitData unit)
        {
            if (!Can(c, unit.PlayerID, StrategicRelocation)) return false;
            List<DataBinding<ModelData>> living = unit.ModelBindings.Where(b => b.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return false;
            var wholeTable = new RectangularZone(0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, 0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"Strategic Relocation: {unit.Name}",
                wholeTable, living, allowCancel: true, maxDistanceFromStartInches: 6f,
                cancelHint: "Cancel - no CP spent.");
            var result = await c.PlayerRequester
                .RequestDecision<PlaceObjectsRequest<ModelData>, CancellableResult<List<PlacedObjectEntry<ModelData>>>>(request);
            if (result is not Selected<List<PlacedObjectEntry<ModelData>>> selected || !Use(c, unit.PlayerID, StrategicRelocation)) return false;
            foreach (PlacedObjectEntry<ModelData> placement in selected.Value)
            {
                ModelData model = placement.Binding.GetValue();
                model.SetPosition(placement.Position);
                if (placement.Facing.HasValue) model.SetFacing(placement.Facing.Value);
            }
            await c.Announce($"Strategic Relocation: {unit.Name} is placed again.", BattlefieldEffects.Amber, EBannerTier.Toast);
            return true;
        }
    }
}
