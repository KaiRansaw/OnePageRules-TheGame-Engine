using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using FDG.Utilities;
using FDG.Presentation.Beats;

namespace FDG
{
    /// <summary>
    /// Unit Psychology (Advanced Rulebooks 3.6.0) - "rules change how units behave as they are put through continuous
    /// fighting, making them less predictable. Pick any of the following". The same five systems in all four books
    /// (Kai, 2026-11-23: "do everything"). Lobby tick boxes; not in the Quest games (no Advanced Rulebook).
    /// The judgement calls are in house rules C, "Unit Psychology readings".
    /// </summary>
    public static class UnitPsychology
    {
        public const string Intimidation = "Intimidation";
        public const string Frenzy = "Frenzy";
        public const string Panic = "Panic";
        public const string Retreating = "Retreating";
        public const string Suppression = "Suppression";

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (Intimidation, EAdvancedRules.Intimidation),
            (Frenzy, EAdvancedRules.Frenzy),
            (Panic, EAdvancedRules.Panic),
            (Retreating, EAdvancedRules.Retreating),
            (Suppression, EAdvancedRules.Suppression),
        };

        /// <summary>The book's text, for the lobby hovers.</summary>
        public static string BookText(string name) => name switch
        {
            Intimidation => "When units from opposite sides are contesting the same objective, compare the total point value of all units from each side, where units with half or less of their total size or Tough value (for single-model units) are worth half of their point value (rounding down).\nIf one side has a total that is at least 50% higher than the other, then it seizes the objective.",
            Frenzy => "When a unit fully destroys an enemy unit in melee or shooting, roll one die. On a 1, the unit is frenzied.\nWhen activated, frenzied units must either shoot at the closest available enemy (moving as needed to get as many of its models into range as possible), or must charge the closest enemy unit, and if neither is possible, then the unit ends its activation. Frenzied units stop being frenzied at the end of their activation.",
            Panic => "When a unit would be Shaken, roll one die. On a 1, the unit panics instead of being Shaken.\nWhen a unit that is panicking is activated, the opponent may control it for that activation, and then it stops panicking at the end of its activation.",
            Retreating => "At the end of an activation in which a unit has taken wounds from shooting, it must move X\" directly away from the shooting unit, where X is 1\" for each wound it took.\nWhen a unit is Shaken after losing a melee, it must move X\" directly away from the enemy unit it fought, where X is 2\" for each wound it took.\nIf a retreat move can't be completed because the unit would hit an obstacle or unit, then it takes D3 wounds and stops within 1\" of the obstacle or unit. If a retreat move would bring off the table, then it's removed from play as destroyed.",
            Suppression => "Units get 1 suppression marker after they fought in melee, when a friendly unit within 6\" is destroyed or Routs after failing a Morale Test, or willingly to add +2\" to their Advance or +4\" to their Rush/Charge moves. There is no limit to how many markers a unit can have.\nFor each marker: -1\" Advance and -2\" Rush/Charge, -1 to hit rolls when shooting or in melee, -1 to Morale Test rolls.\nWhen a marker brings a unit to 3 or more, it must take a Morale Test, and if failed it is Shaken, even if it didn't take wounds.\nWhen a unit with markers is activated, it takes as many Morale Tests as markers on it (ignoring the -1 from suppression); each success removes one marker.\nUnits with markers may Rest by staying idle during their activation, which removes all markers at the end of the round (Shaken units resting also stop being Shaken).\nWhen a Hero is activated, remove 1 marker from the Hero and its unit, and from all other friendly units within 12\" (no rolls).",
            _ => "",
        };

        /// <summary>What the game does, in a line - the second half of the lobby hover.</summary>
        public static string GameText(string name) => name switch
        {
            Intimidation => "At the end of a round a contested marker goes to a side with 50% more points within 3\" of it (half points for units at half strength).",
            Frenzy => "A frenzied unit may only Hold or Advance and shoot the closest enemy, or charge the closest enemy. No Rush, casting or Pass while it can do either.",
            Panic => "On a 1 the unit is not Shaken but panics. When it next activates, the opponent picks: it stays idle, runs toward the nearest enemy, or runs away from it (a straight Rush).",
            Retreating => "Wounded units fall back straight away from whoever hurt them; bumping into something costs D3 wounds; off the table = destroyed.",
            Suppression => "Markers slow units, cost -1 to hit and -1 morale each. Asked before each move whether to take one for the extra distance. Passing an activation is the Rest action.",
            _ => "",
        };

        /// <summary>The agreed set for this game. The Quest systems have no Advanced Rulebook, so none apply there.</summary>
        public static bool IsOn(IGameContext c, EAdvancedRules flag) => AdvancedRules.IsOn(c, flag);

        private static int D6(IGameContext c) => HordePlacement.RollD6(c);

        private static int D3(IGameContext c) => (D6(c) + 1) / 2;

        private static readonly TextColor Amber = new(255, 170, 60, 255);

        // ================================================================= Intimidation

        /// <summary>
        /// A contested objective: "compare the total point value of all units from each side" (units counted as
        /// for contesting - within 3", not Shaken, not just arrived), "units with half or less of their total size
        /// or Tough value ... are worth half of their point value (rounding down)". A side at least 50% higher
        /// seizes it. Returns one of that side's contesting players, or null when nobody is 50% ahead.
        /// </summary>
        public static PlayerID? Intimidate(IGameContext c, IObjective objective, Dictionary<IModel, IUnit> modelToUnit,
            HashSet<PlayerID> contesting)
        {
            if (!IsOn(c, EAdvancedRules.Intimidation) || contesting.Count < 2) return null;
            var units = new HashSet<IUnit>();
            foreach (IModel model in c.TableState.Models.Objects)
            {
                if (!modelToUnit.TryGetValue(model, out IUnit? unit) || !contesting.Contains(unit.PlayerID)) continue;
                var only = new HashSet<PlayerID>(ReconcileObjectivesStage.PlayersNearObjective(objective, new[] { model }, modelToUnit));
                if (only.Contains(unit.PlayerID)) units.Add(unit);
            }
            var totals = new Dictionary<int, int>();
            foreach (IUnit unit in units)
            {
                int team = TeamOf(c, unit.PlayerID);
                int cost = (unit as UnitData)?.PointCost ?? 0;
                if (unit.GetIsAtHalfStrength()) cost /= 2;
                totals[team] = totals.GetValueOrDefault(team) + cost;
            }
            if (totals.Count < 2) return null;
            var ordered = totals.OrderByDescending(kv => kv.Value).ToList();
            int top = ordered[0].Value, next = ordered[1].Value;
            // "at least 50% higher": top >= 1.5 x next, in whole points (2 x top >= 3 x next).
            if (top <= 0 || 2 * top < 3 * next) return null;
            int winner = ordered[0].Key;
            PlayerID seizer = contesting.First(p => TeamOf(c, p) == winner);
            c.Log($"  Intimidation: {top} pts against {next} pts - {c.GetPlayerName(seizer)} seizes it.");
            return seizer;
        }

        private static int TeamOf(IGameContext c, PlayerID player) =>
            c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(player))?.TeamNumber ?? -1;

        // ================================================================= Frenzy, and Suppression from a death

        /// <summary>A unit was destroyed: Frenzy for its killer, Suppression for its friends within 6".</summary>
        public static void UnitDestroyed(IGameContext c, IUnit dead, IUnit? killer, EKillCause cause)
        {
            if (IsOn(c, EAdvancedRules.Frenzy) && killer != null && killer.GetIsAlive()
                && cause is EKillCause.Shooting or EKillCause.Melee && !killer.PlayerID.Equals(dead.PlayerID))
            {
                int roll = D6(c);
                if (roll == 1)
                {
                    if (!killer.Tokens.HasToken(TokenType.Frenzied)) killer.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Frenzied));
                    c.Log($"Frenzy: {killer.Name} destroyed {dead.Name} and rolled a 1 - it is frenzied.");
                }
                else c.Log($"Frenzy: {killer.Name} destroyed {dead.Name} (rolled {roll}) - not frenzied.");
            }
            if (IsOn(c, EAdvancedRules.Suppression))
            {
                foreach (IUnit friend in c.TableState.Units.Objects.Where(u => u.GetIsAlive() && !ReferenceEquals(u, dead)
                             && !u.ID.Equals(dead.ID) && u.PlayerID.Equals(dead.PlayerID) && !ReserveRules.IsInReserve(u)))
                {
                    if (UnitCompareUtilities.MinDistanceBetweenUnits(friend, dead, out _, out _, includeVertical: true) > 6f + 0.001f) continue;
                    AddMarker(c, friend, $"{dead.Name} was destroyed within 6\"");
                }
            }
        }

        public static bool IsFrenzied(IUnit unit) => unit.Tokens.HasToken(TokenType.Frenzied);

        /// <summary>The closest enemy unit on the table - a frenzied unit's only legal target.</summary>
        public static IUnit? ClosestEnemy(IGameContext c, IUnit unit)
        {
            int team = TeamOf(c, unit.PlayerID);
            return c.TableState.Units.Objects
                .Where(u => u.GetIsAlive() && TeamOf(c, u.PlayerID) != team && !ReserveRules.IsInReserve(u)
                            && u.Models.Any(m => m.GetIsAlive()))
                .OrderBy(u => UnitCompareUtilities.MinDistanceBetweenUnits(unit, u, out _, out _, includeVertical: true))
                .FirstOrDefault();
        }

        // ================================================================= Panic

        /// <summary>"When a unit would be Shaken, roll one die. On a 1, the unit panics instead of being Shaken."
        /// Returns true when it panicked (the caller then does not make it Shaken).</summary>
        public static async Task<bool> TryPanic(IGameContext c, IUnit unit)
        {
            if (!IsOn(c, EAdvancedRules.Panic) || !unit.GetIsAlive()) return false;
            int roll = D6(c);
            if (roll != 1)
            {
                c.Log($"Panic: {unit.Name} rolled {roll} - Shaken as usual.");
                return false;
            }
            if (!unit.Tokens.HasToken(TokenType.Panicking)) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Panicking));
            await c.Announce($"{unit.Name} panics! The opponent controls it on its next activation.", Amber, EBannerTier.Notice);
            return true;
        }

        public const string PanicIdle = "Stays where it is";
        public const string PanicToward = "Runs toward the nearest enemy";
        public const string PanicAway = "Runs away from the nearest enemy";

        /// <summary>
        /// A panicking unit activates: "the opponent may control it for that activation". The opponent picks what it
        /// does (house rules C, "Unit Psychology readings"): it stays idle, or runs (a straight Rush, moved as one
        /// block, stopping at obstacles and the table edge) toward or away from the nearest enemy. No shooting,
        /// charging or casting. Returns true when the activation was used up this way.
        /// </summary>
        public static async Task<bool> PanicActivation(IGameContext c, IUnit unit)
        {
            if (!unit.Tokens.HasToken(TokenType.Panicking)) return false;
            unit.Tokens.RemoveTokens(TokenType.Panicking, unit.Tokens.GetTokenCount(TokenType.Panicking));
            int team = TeamOf(c, unit.PlayerID);
            PlayerID? opponent = c.TableState.Teams.Objects.Where(t => t.TeamNumber != team)
                .SelectMany(t => t.Players).Cast<PlayerID?>().FirstOrDefault();
            if (opponent == null) return false;
            IUnit? nearest = ClosestEnemy(c, unit);
            var options = new List<string> { PanicToward, PanicAway, PanicIdle };
            if (nearest == null) options = new List<string> { PanicIdle };
            string pick = options.Count == 1 ? PanicIdle : await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(
                new StringSelectionRequest(opponent.Value,
                    $"{unit.Name} (the enemy's) panics - you control it this activation. What does it do?",
                    options, new List<StringSelectionRequest.InvalidOption>(),
                    optionDescriptions: new Dictionary<string, string>
                    {
                        [PanicToward] = $"It rushes straight toward {nearest?.Name}.",
                        [PanicAway] = $"It rushes straight away from {nearest?.Name}.",
                        [PanicIdle] = "It does nothing this activation.",
                    },
                    displayName: "Control the panicking unit") { Recommended = PanicToward });
            c.Log($"Panic: {c.GetPlayerName(opponent.Value)} controls {unit.Name} - {pick}.");
            if (pick != PanicIdle && nearest != null && unit is UnitData data)
            {
                const float rush = 12f; // the core Rush move; a panicked run is the plain move (house rules C)
                await QuestActions.Shove(c, nearest, data, rush, away: pick == PanicAway);
            }
            else await c.Announce($"{unit.Name} panics and stays where it is.", Amber, EBannerTier.Toast);
            return true;
        }

        // ================================================================= Retreating

        private sealed class ShotLedger { public readonly List<(IUnit Target, IUnit Shooter, float Wounds)> Hits = new(); }

        private static readonly ConditionalWeakTable<object, ShotLedger> Shots = new();

        /// <summary>Wounds a unit took from shooting this activation (fed by the wound stage).</summary>
        public static void ShootingWounds(IGameContext c, IUnit target, IUnit shooter, float wounds)
        {
            if (!IsOn(c, EAdvancedRules.Retreating) || wounds <= 0f) return;
            Shots.GetValue(c.GameDataStore, _ => new ShotLedger()).Hits.Add((target, shooter, wounds));
        }

        /// <summary>"At the end of an activation in which a unit has taken wounds from shooting, it must move X"
        /// directly away from the shooting unit, where X is 1" for each wound it took."</summary>
        public static async Task EndOfActivation(IGameContext c)
        {
            if (Shots.TryGetValue(c.GameDataStore, out ShotLedger? ledger) && ledger.Hits.Count > 0)
            {
                var hits = ledger.Hits.ToList();
                ledger.Hits.Clear();
                foreach (var group in hits.GroupBy(h => (h.Target, h.Shooter)))
                {
                    float wounds = group.Sum(h => h.Wounds);
                    int inches = (int)MathF.Floor(wounds + 0.001f);
                    if (inches <= 0 || !group.Key.Target.GetIsAlive()) continue;
                    await Retreat(c, group.Key.Target, group.Key.Shooter, inches, "took shooting wounds");
                }
            }
            await ProcessPendingSuppressionTests(c);
        }

        /// <summary>"When a unit is Shaken after losing a melee, it must move X" directly away from the enemy unit it
        /// fought, where X is 2" for each wound it took."</summary>
        public static async Task ShakenAfterMelee(IGameContext c, IUnit loser, IUnit enemy, float woundsTaken)
        {
            if (!IsOn(c, EAdvancedRules.Retreating) || !loser.GetIsAlive()) return;
            int inches = 2 * (int)MathF.Floor(woundsTaken + 0.001f);
            if (inches <= 0) return;
            await Retreat(c, loser, enemy, inches, "lost the melee and is Shaken");
        }

        /// <summary>The retreat move itself: straight away from <paramref name="from"/>, as one block. Hitting an
        /// obstacle or unit: D3 wounds and it stops there; leaving the table: destroyed.</summary>
        internal static async Task Retreat(IGameContext c, IUnit unit, IUnit from, float inches, string why)
        {
            if (unit is not UnitData data) return;
            var living = data.Models.OfType<ModelData>().Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return;
            (float fx, float fz) = QuestActions.Centre(from);
            (float tx, float tz) = QuestActions.Centre(unit);
            float dx = tx - fx, dz = tz - fz;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) { dx = 1f; dz = 0f; len = 1f; }
            dx = dx / len * inches;
            dz = dz / len * inches;

            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;
            float edgeT = 1f;
            foreach (ModelData m in living)
            {
                float r = m.BaseShape.CircumscribedRadiusInches;
                edgeT = MathF.Min(edgeT, QuestActions.Reach(m.Position.x, dx, r, w - r));
                edgeT = MathF.Min(edgeT, QuestActions.Reach(m.Position.z, dz, r, h - r));
            }
            edgeT = MathF.Max(0f, edgeT);
            float freeT = QuestActions.FreeTravel(c, data, living, dx, dz, 1f, inches);
            if (freeT >= 1f - 0.0001f && edgeT < 1f - 0.0001f)
            {
                // Nothing in the way, but the table ends first: "removed from play as destroyed".
                await c.Announce($"Retreating: {unit.Name} {why} and flees off the table - destroyed!", Amber, EBannerTier.Notice);
                await CasualtyPresentation.ApplyUnitWounds(c, data, data.RemainingWounds + 1f);
                return;
            }
            float t = MathF.Min(freeT, edgeT);
            foreach (ModelData m in living)
                m.SetPosition(new Position(m.Position.x + dx * t, m.Position.z + dz * t));
            float moved = inches * t;
            if (t >= 1f - 0.0001f)
            {
                await QuestActions.Toast(c, $"Retreating: {unit.Name} {why} and falls back {inches:0}\" from {from.Name}.");
                return;
            }
            int wounds = D3(c);
            await QuestActions.Toast(c, $"Retreating: {unit.Name} {why}, falls back {moved:0.#}\" of {inches:0}\" and runs into "
                + (edgeT <= freeT ? "the table edge" : "an obstacle") + $" - {wounds} wound{(wounds == 1 ? "" : "s")}.");
            if (edgeT <= freeT && edgeT < 1f - 0.0001f)
            {
                // Stopped by the table edge with the obstacle check clear up to it: the move would leave the table.
                await c.Announce($"Retreating: {unit.Name} is pushed off the table - destroyed!", Amber, EBannerTier.Notice);
                await CasualtyPresentation.ApplyUnitWounds(c, data, data.RemainingWounds + 1f);
                return;
            }
            await CasualtyPresentation.ApplyUnitWounds(c, data, wounds);
        }

        // ================================================================= Suppression

        public static int Markers(IUnit unit) => unit.Tokens.GetTokenCount(TokenType.SuppressionMarker);

        private sealed class PendingTests { public readonly List<IUnit> Units = new(); }

        private static readonly ConditionalWeakTable<object, PendingTests> Pending = new();

        /// <summary>Gives a unit one suppression marker. Reaching 3 or more owes a Morale Test, taken at the end of the
        /// current activation (the marker can arrive in the middle of someone else's attack).</summary>
        public static void AddMarker(IGameContext c, IUnit unit, string why)
        {
            if (!IsOn(c, EAdvancedRules.Suppression) || !unit.GetIsAlive()) return;
            int before = Markers(unit);
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.SuppressionMarker));
            c.Log($"Suppression: {unit.Name} gets a marker ({why}) - {before + 1} now.");
            if (before < 3 && before + 1 >= 3)
            {
                var list = Pending.GetValue(c.GameDataStore, _ => new PendingTests()).Units;
                if (!list.Contains(unit)) list.Add(unit);
            }
        }

        /// <summary>"After they fought in melee" - both sides of a melee get a marker.</summary>
        public static void FoughtInMelee(IGameContext c, IUnit a, IUnit b)
        {
            if (!IsOn(c, EAdvancedRules.Suppression)) return;
            AddMarker(c, a, "fought in melee");
            AddMarker(c, b, "fought in melee");
        }

        /// <summary>"When a unit takes a suppression marker that brings it to a total of 3 markers or more, then it must
        /// take a Morale Test, and if failed, then the unit is Shaken, even if it didn't take wounds."</summary>
        public static async Task ProcessPendingSuppressionTests(IGameContext c)
        {
            if (!Pending.TryGetValue(c.GameDataStore, out PendingTests? pending) || pending.Units.Count == 0) return;
            var units = pending.Units.ToList();
            pending.Units.Clear();
            foreach (IUnit unit in units)
            {
                if (!unit.GetIsAlive() || unit is not UnitData data) continue;
                var outcome = await MoraleUtilities.TakeMoraleTest(c, unit,
                    HeroStatRules.GetMoraleQuality(data, c.TableState), EMoraleTrigger.OtherSource);
                if (outcome.Passed) { c.Log($"Suppression: {unit.Name} holds its nerve with {Markers(unit)} markers."); continue; }
                c.Log($"Suppression: {unit.Name} fails its test at {Markers(unit)} markers.");
                if (BindingOf(c, data) is { } binding) await MoraleUtilities.ApplyShakenWithPresentation(c, binding);
            }
        }

        private static readonly AsyncLocal<bool> IgnoringPenalty = new();

        /// <summary>-1 to Morale Test rolls per marker (not on the activation tests, which "ignore the penalty").</summary>
        public static int MoraleModifier(IUnit unit) => IgnoringPenalty.Value ? 0 : -Markers(unit);

        /// <summary>-1 to hit rolls when shooting or in melee, per marker. Spells are not hit rolls.</summary>
        public static int HitModifier(IUnit attacker, bool isSpell) => isSpell ? 0 : -Markers(attacker);

        /// <summary>Movement: -1" Advance and -2" Rush/Charge per marker; a willing marker this activation +2" / +4".</summary>
        public static (float Advance, float RushCharge) MoveChange(IUnit unit)
        {
            int m = Markers(unit);
            bool boost = unit.Tokens.HasToken(TokenType.SuppressionBoost);
            return (-1f * m + (boost ? 2f : 0f), -2f * m + (boost ? 4f : 0f));
        }

        /// <summary>
        /// A unit with markers activates: a Hero first lifts one from itself and its unit and every friendly unit within
        /// 12"; then the unit "must take as many Morale Tests as markers on it, ignoring the penalty of -1 to Morale Test
        /// rolls from suppression. For each success you may remove one marker". A failed test only fails to remove one.
        /// </summary>
        public static async Task OnActivationStart(IGameContext c, IUnit unit)
        {
            unit.Tokens.RemoveTokens(TokenType.SuppressionBoost, unit.Tokens.GetTokenCount(TokenType.SuppressionBoost));
            if (!IsOn(c, EAdvancedRules.Suppression)) return;
            if (unit.Models.Any(m => m.GetIsAlive() && HasHeroRule(m)))
            {
                foreach (IUnit friend in c.TableState.Units.Objects.Where(u => u.GetIsAlive() && u.PlayerID.Equals(unit.PlayerID)
                             && Markers(u) > 0 && (ReferenceEquals(u, unit) || u.ID.Equals(unit.ID)
                                 || UnitCompareUtilities.MinDistanceBetweenUnits(unit, u, out _, out _, includeVertical: true) <= 12f + 0.001f)))
                {
                    friend.Tokens.RemoveTokens(TokenType.SuppressionMarker, 1);
                    c.Log($"Suppression: {unit.Name}'s Hero lifts a marker from {friend.Name} ({Markers(friend)} left).");
                }
            }
            int n = Markers(unit);
            if (n == 0 || unit is not UnitData data) return;
            int removed = 0;
            IgnoringPenalty.Value = true;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    var outcome = await MoraleUtilities.TakeMoraleTest(c, unit,
                        HeroStatRules.GetMoraleQuality(data, c.TableState), EMoraleTrigger.OtherSource);
                    if (outcome.Passed) removed++;
                }
            }
            finally { IgnoringPenalty.Value = false; }
            if (removed > 0) unit.Tokens.RemoveTokens(TokenType.SuppressionMarker, removed);
            c.Log($"Suppression: {unit.Name} takes {n} test{(n == 1 ? "" : "s")} and removes {removed} marker{(removed == 1 ? "" : "s")} ({Markers(unit)} left).");
        }

        private static bool HasHeroRule(IModel model) =>
            model.RuleDefinitions.Any(r => r.RequestedName == "Hero");

        private static DataBinding<UnitData>? BindingOf(IGameContext c, IUnit unit)
        {
            foreach (ArmyData army in c.GameDataStore.GetAllValues<ArmyData>())
                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                    if (binding.GetValue().ID.Equals(unit.ID)) return binding;
            return null;
        }

        /// <summary>The unit stayed idle this activation: the Rest action, "which removes all markers at the end of the
        /// round".</summary>
        public static void Rested(IGameContext c, IUnit unit)
        {
            if (!IsOn(c, EAdvancedRules.Suppression) || Markers(unit) == 0) return;
            if (!unit.Tokens.HasToken(TokenType.Resting)) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Resting));
            c.Log($"Suppression: {unit.Name} rests - its markers go at the end of the round.");
        }

        /// <summary>End of the round: resting units drop all their markers.</summary>
        public static void RoundEnd(IGameContext c)
        {
            foreach (IUnit unit in c.TableState.Units.Objects)
            {
                if (!unit.Tokens.HasToken(TokenType.Resting)) continue;
                unit.Tokens.RemoveTokens(TokenType.Resting, unit.Tokens.GetTokenCount(TokenType.Resting));
                int n = Markers(unit);
                if (n > 0)
                {
                    unit.Tokens.RemoveTokens(TokenType.SuppressionMarker, n);
                    c.Log($"Suppression: {unit.Name} rested - {n} marker{(n == 1 ? "" : "s")} removed.");
                }
            }
        }

        /// <summary>"Units may also willingly get 1 suppression marker to add +2" to their Advance or +4" to their
        /// Rush/Charge moves." Asked once, before a unit moves; bots decline.</summary>
        public static async Task OfferBoost(IGameContext c, IUnit unit)
        {
            if (!IsOn(c, EAdvancedRules.Suppression) || unit.Tokens.HasToken(TokenType.SuppressionBoost)) return;
            bool take = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(unit.PlayerID,
                $"Suppression: give {unit.Name} a suppression marker to move +2\" on Advance or +4\" on Rush / Charge?"
                + (Markers(unit) > 0 ? $" (It has {Markers(unit)}.)" : ""),
                defaultAnswer: false, displayName: $"{unit.Name}: push on?"));
            if (!take) return;
            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.SuppressionBoost));
            AddMarker(c, unit, "pushed on for extra distance");
        }
    }
}
