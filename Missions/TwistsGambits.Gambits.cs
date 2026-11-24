using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Presentation.Beats;
using FDG.Stages;
using FDG.StateMachine;

namespace FDG.Missions
{
    /// <summary>
    /// Twists &amp; Gambits, part 2: the gambits (GF p45, GFF p42, AoF p45, AoFS p42). "At the beginning of the game, each
    /// player gets three random gambits, and when they use or score a gambit, they must roll for a new gambit. At the end
    /// of each round, players may also discard one of their gambits and roll for a new one. ... Once per round, players
    /// may either use one gambit's power at the beginning of the round, or score one of their gambits at the end of the
    /// round for 4 VPs." Book differences: GF says "Control" where the others say "Seize" on six scorings; Fortify Domain
    /// brings back a non-Tough unit (GF, AoF) or a multi-model unit (GFF, AoFS); Search &amp; Destroy deals D3 wounds
    /// (AoFS: D3 hits). Judgement calls: house rules C, "Twists &amp; Gambits readings".
    /// </summary>
    public static partial class TwistsGambits
    {
        public const int GambitVp = 4;

        public sealed record Gambit(string Name, string Power, string Scoring);

        /// <summary>The book's words (Grimdark Firefight / AoF / AoF: Skirmish wording; GF and AoFS differences below).</summary>
        public static readonly IReadOnlyDictionary<string, Gambit> Gambits = new Dictionary<string, Gambit>
        {
            ["A1"] = new("Ground War", "One friendly unit gets the Strider rule until the end of the round.",
                "Seize the objective marker inside the enemy deployment zone."),
            ["A2"] = new("Valiant Drive", "One friendly unit gets the Fearless rule until the end of the round.",
                "At least one friendly unit within 3\" of the long table edge within the enemy deployment zone."),
            ["A3"] = new("Aggressive Takeover", "One friendly unit's weapons ignore the Stealth and Regeneration rule until the end of the round.",
                "Seize at least two objectives that were controlled by the enemy."),
            ["A4"] = new("Total Domination", "One friendly unit gets the Relentless rule until the end of the round.",
                "Seize at least three objective markers."),
            ["A5"] = new("Silent Assault", "One friendly unit gets the Furious rule until the end of the round.",
                "Seize at least one objective that was controlled by the enemy with units that didn't shoot or fight in melee this round."),
            ["A6"] = new("Cautious Destruction", "One friendly unit gets the Stealth rule until the end of the round.",
                "At least one enemy unit destroyed, and more enemy units than friendly units destroyed this round."),
            ["B1"] = new("Storm Strike", "One friendly unit gets the Fast rule until the end of the round.",
                "At least one friendly unit fought in melee, and no friendly units were destroyed this round."),
            ["B2"] = new("Seize & Guard", "One friendly unit gets the Counter rule on all melee weapons until the end of the round.",
                "Seize objective 1."),
            ["B3"] = new("Grab & Secure", "One friendly unit gets the Regeneration rule until the end of the round.",
                "Seize objective 2."),
            ["B4"] = new("Search & Destroy", "One enemy unit within 12\" of a friendly Hero takes D3 wounds.",
                "Seize objective 3."),
            ["B5"] = new("Distributed Violence", "Remove D3 wounds from a friendly Hero unit.",
                "At least two friendly units are within 3\" of the two opposite table edges outside of deployment zones."),
            ["B6"] = new("Fortify Domain", "Deploy one previously destroyed multi-model unit with half of its models (rounding up) within 6\" of any table edge, over 12\" away from enemy units.",
                "Seize the objective marker inside your deployment zone."),
        };

        /// <summary>GF prints "Control" instead of "Seize" on these scorings.</summary>
        private static readonly HashSet<string> GfControls = new() { "A1", "A4", "B2", "B3", "B4", "B6" };

        public static bool ScoredByControl(EGameSystem system, string code) =>
            system == EGameSystem.GrimdarkFuture && GfControls.Contains(code);

        /// <summary>Fortify Domain: GF / AoF "non-Tough unit", GFF / AoFS "multi-model unit".</summary>
        public static bool FortifyNeedsMultiModel(EGameSystem system) =>
            system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        /// <summary>Search &amp; Destroy: AoF: Skirmish prints "D3 hits", the others "D3 wounds".</summary>
        public static bool SearchDealsHits(EGameSystem system) => system == EGameSystem.AoFSkirmish;

        public static string PowerText(EGameSystem system, string code) => code switch
        {
            "B4" when SearchDealsHits(system) => "One enemy unit within 12\" of a friendly Hero takes D3 hits.",
            "B6" when !FortifyNeedsMultiModel(system) => "Deploy one previously destroyed non-Tough unit with half of its models (rounding up) within 6\" of any table edge, over 12\" away from enemy units.",
            _ => Gambits[code].Power,
        };

        public static string ScoringText(EGameSystem system, string code) =>
            ScoredByControl(system, code) ? Gambits[code].Scoring.Replace("Seize", "Control") : Gambits[code].Scoring;

        private static string RollGambit(IGameContext c)
        {
            int type = c.DiceRoller.RollDecisiveFace(), which = c.DiceRoller.RollDecisiveFace();
            return (type <= 3 ? "A" : "B") + which;
        }

        private static List<string> HandOf(TwistsGambitsState t, int slot) =>
            t.Hand.TryGetValue(slot, out var h) ? h : t.Hand[slot] = new List<string>();

        private static void Replace(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo seat, string code)
        {
            List<string> hand = HandOf(t, seat.SlotID);
            int i = hand.IndexOf(code);
            string fresh = RollGambit(c);
            if (i >= 0) hand[i] = fresh; else hand.Add(fresh);
            c.Log($"  {seat.Name} rolls a new gambit: {Gambits[fresh].Name}.");
        }

        // ---------------------------------------------------------------- the beginning of a round

        private static async Task GambitsRoundStart(IGameContext c, TwistsGambitsState t, int round)
        {
            if (!t.Dealt)
            {
                foreach (IPlayerSlotInfo seat in Seats(c))
                {
                    List<string> hand = HandOf(t, seat.SlotID);
                    hand.Clear();
                    for (int i = 0; i < 3; i++) hand.Add(RollGambit(c));
                    c.Log($"Twists & Gambits: {seat.Name}'s gambits: {string.Join(", ", hand.Select(g => Gambits[g].Name))}.");
                }
                t.Dealt = true;
            }
            foreach (IPlayerSlotInfo seat in Seats(c)) await OfferPower(c, t, seat);
        }

        public const string NoPower = "No power this round (keep the chance to score one)";

        private static string PowerLabel(IGameContext c, string code) =>
            $"{Gambits[code].Name}: {PowerText(c.Settings.GameSystem, code)}";

        private static async Task OfferPower(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo seat)
        {
            List<string> hand = HandOf(t, seat.SlotID);
            var usable = hand.Distinct().Where(g => PowerTargets(c, seat.SlotID, g).Count > 0).ToList();
            if (usable.Count == 0) return;
            var options = new List<string> { NoPower };
            options.AddRange(usable.Select(g => PowerLabel(c, g)));
            string? botPick = BotPower(c, seat.SlotID, usable);
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                "Gambits: use one gambit's power now? (Once per round: a power now, or score a gambit at the end of the round for 4 VP.)",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Twists & Gambits - gambit power")
            { Recommended = botPick != null ? PowerLabel(c, botPick) : NoPower });
            int idx = options.IndexOf(answer);
            if (idx <= 0) return;
            string code = usable[idx - 1];
            if (!await UsePower(c, t, seat, code)) return;
            t.PowerUsed.Add(seat.SlotID);
            await c.Announce($"{seat.Name} plays {Gambits[code].Name}", Gold, EBannerTier.Toast);
            Replace(c, t, seat, code);
        }

        /// <summary>A bot plays a power only where it is worth giving up a 4 VP score for: Fortify Domain (a unit back),
        /// or Search &amp; Destroy on an enemy it can reach. Everything else it holds on to, to score.</summary>
        private static string? BotPower(IGameContext c, int slot, List<string> usable)
        {
            if (usable.Contains("B6")) return "B6";
            if (usable.Contains("B4")) return "B4";
            return null;
        }

        /// <summary>The units a power can be used on (empty = the power can't be used now).</summary>
        private static List<IUnit> PowerTargets(IGameContext c, int slot, string code)
        {
            int other = Other(c, slot);
            switch (code)
            {
                case "B4":
                {
                    var heroes = UnitsOf(c, slot).Where(MissionCardGame.UnitHasHero).ToList();
                    return UnitsOf(c, other).Where(e => heroes.Any(h => Gap(h, e) <= 12f + 0.01f)).ToList();
                }
                case "B5":
                    return UnitsOf(c, slot).Where(u => MissionCardGame.UnitHasHero(u) && u.Models.Any(m => m.GetIsAlive() && m.WoundsDealt > 0)).ToList();
                case "B6":
                    return c.TableState.Units.Objects.Where(u => SlotOfUnit(c, u) == slot && !u.GetIsAlive() && u is UnitData
                        && !u.Tokens.HasToken(TokenType.Conceded) && FortifyFits(c.Settings.GameSystem, u)).ToList();
                default:
                    return UnitsOf(c, slot);
            }
        }

        /// <summary>"Non-Tough" = no model has more than 1 wound; "multi-model" = 2+ models.</summary>
        public static bool FortifyFits(EGameSystem system, IUnit unit) =>
            FortifyNeedsMultiModel(system) ? unit.Models.Count >= 2 : unit.Models.All(m => m.TotalWounds <= 1f);

        /// <summary>The closest base-to-base gap between two units.</summary>
        private static float Gap(IUnit a, IUnit b)
        {
            float best = float.MaxValue;
            foreach (IModel x in a.Models.Where(m => m.GetIsAlive()))
            foreach (IModel y in b.Models.Where(m => m.GetIsAlive()))
                best = Math.Min(best, Position.GetDistance2D(x.Position, y.Position)
                    - x.BaseShape.BoundingRadiusInches - y.BaseShape.BoundingRadiusInches);
            return best;
        }

        private static readonly Dictionary<string, string> GrantedRule = new()
        {
            ["A1"] = "Strider", ["A2"] = "Fearless", ["A3"] = CampaignEventRuleCatalog.AggressiveTakeoverName,
            ["A4"] = "Relentless", ["A5"] = "Furious", ["A6"] = "Stealth", ["B1"] = "Fast", ["B2"] = "Counter",
            ["B3"] = "Regeneration",
        };

        private static async Task<bool> UsePower(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo seat, string code)
        {
            List<IUnit> targets = PowerTargets(c, seat.SlotID, code);
            string name = Gambits[code].Name;
            string Ask(string what) => $"Gambit - {name}: {what}";
            if (GrantedRule.TryGetValue(code, out string? rule))
            {
                if (await PickUnit(c, seat, targets, Ask($"pick one of your units - it gets {(code == "A3" ? "weapons that ignore Stealth and Regeneration" : rule)} this round."),
                        Priciest(targets)) is not { } unit) return false;
                GrantRuleForTheRound(unit, rule);
                c.Log($"Gambit {name}: {unit.Name} gets {(code == "A3" ? "weapons that ignore Stealth and Regeneration" : rule)} until the end of the round.");
                return true;
            }
            switch (code)
            {
                case "B4":
                {
                    if (await PickUnit(c, seat, targets, Ask(SearchDealsHits(c.Settings.GameSystem)
                            ? "pick an enemy unit within 12\" of your Hero - it takes D3 hits." : "pick an enemy unit within 12\" of your Hero - it takes D3 wounds."),
                            Priciest(targets)) is not { } enemy) return false;
                    int d3 = (c.DiceRoller.RollDecisiveFace() + 1) / 2;
                    if (SearchDealsHits(c.Settings.GameSystem))
                    {
                        c.Log($"Gambit {name}: {enemy.Name} takes {d3} hit{(d3 == 1 ? "" : "s")}.");
                        await CampaignEventRunner.DealHits(c, enemy, d3, name);
                    }
                    else
                    {
                        c.Log($"Gambit {name}: {enemy.Name} takes {d3} wound{(d3 == 1 ? "" : "s")}.");
                        await CasualtyPresentation.ApplyUnitWounds(c, enemy, d3);
                    }
                    return true;
                }
                case "B5":
                {
                    if (await PickUnit(c, seat, targets, Ask("pick one of your Hero units - it removes D3 wounds."), targets.FirstOrDefault()) is not { } hero) return false;
                    await new GameOperationServices(c).RestoreD3(hero, name);
                    return true;
                }
                case "B6":
                {
                    if (await PickUnit(c, seat, targets, Ask("pick one of your destroyed units - it comes back with half its models."),
                            Priciest(targets)) is not UnitData back) return false;
                    await Fortify(c, t, back, name);
                    return true;
                }
            }
            return false;
        }

        /// <summary>Fortify Domain: back with half of its models (rounding up), within 6" of any table edge, over 12" from
        /// enemy units - placed like a unit arriving from reserves.</summary>
        private static async Task Fortify(IGameContext c, TwistsGambitsState t, UnitData unit, string source)
        {
            HordePlacement.Revive(unit);
            var models = unit.ModelBindings.Select(b => b.GetValue()).ToList();
            int keep = (models.Count + 1) / 2;
            foreach (ModelData gone in models.Skip(keep)) gone.DealWounds(gone.TotalWounds);
            var back = unit.ModelBindings.Take(keep).ToList();
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"{source}: {unit.Name} returns with {keep} model{(keep == 1 ? "" : "s")}",
                new TableEdgeBandZone(w, h, 6f), back, minDistanceFromEnemiesInches: 12f,
                enemyKeepOutDiscs: AmbushArrivalRules.EnemyEdgeDiscs(unit, c.TableState, 12f),
                displayName: $"Deploying {unit.Name}");
            List<PlacedObjectEntry<ModelData>> placed = await PlacementCommitGuard.RequestClearPlacement(c, request);
            foreach (PlacedObjectEntry<ModelData> p in placed)
            {
                p.Binding.GetValue().SetPosition(p.Position);
                FacingRules.ApplyPlaced(p.Binding.GetValue(), p.Facing, c.Settings);
            }
            if (!t.AliveAtStart.Contains(Id(unit))) t.AliveAtStart.Add(Id(unit));   // destroyed again this round = scored again
            c.Log($"Gambit {source}: {unit.Name} returns with {keep} model{(keep == 1 ? "" : "s")}.");
            await c.Announce($"{unit.Name} returns!", Gold);
        }

        // ---------------------------------------------------------------- during the round

        /// <summary>A unit attacked (a shot, or a melee strike): Silent Assault and Storm Strike read these.</summary>
        public static void NoteAttack(IGameContext c, IUnit attacker, bool melee, bool spell)
        {
            if (spell || StateOf(c) is not { } t) return;
            string id = Id(attacker);
            bool changed = false;
            if (!t.Attacked.Contains(id)) { t.Attacked.Add(id); changed = true; }
            if (melee && !t.Fought.Contains(id)) { t.Fought.Add(id); changed = true; }
            if (changed) ExtraMissions.SaveState(c, ExtraMissions.StateFor(c));
        }

        // ---------------------------------------------------------------- the end of the round

        /// <summary>Whether <paramref name="slot"/> has done what gambit <paramref name="code"/> asks, this round.</summary>
        public static bool Scores(IGameContext c, TwistsGambitsState t, int slot, string code,
            Func<int, string, bool> holds, Func<int, string, bool> seized)
        {
            int other = Other(c, slot);
            EGameSystem system = c.Settings.GameSystem;
            bool Take(string? key) => key != null && (ScoredByControl(system, code) ? holds(slot, key) : seized(slot, key));
            List<string> all = c.TableState.Objectives.Objects.Select(o => Key(o.Position)).ToList();
            bool WasEnemys(string key) => t.OwnerAtStart.GetValueOrDefault(key, -1) == other;
            int destroyed(int side) => c.TableState.Units.Objects.Count(u => SlotOfUnit(c, u) == side && !u.GetIsAlive() && t.AliveAtStart.Contains(Id(u)));
            string? Numbered(int i) => t.Numbered.Count > i ? t.Numbered[i] : null;
            switch (code)
            {
                case "A1": return Take(t.ZoneMarker.GetValueOrDefault(other));
                case "A2": return ValiantDrive(c, t, slot);
                case "A3": return all.Count(k => WasEnemys(k) && holds(slot, k)) >= 2;
                case "A4": return all.Count(k => ScoredByControl(system, code) ? holds(slot, k) : seized(slot, k)) >= 3;
                case "A5": return all.Any(k => WasEnemys(k) && holds(slot, k) && QuietHolders(c, t, slot, k));
                case "A6": return destroyed(other) >= 1 && destroyed(other) > destroyed(slot);
                case "B1": return c.TableState.Units.Objects.Any(u => SlotOfUnit(c, u) == slot && t.Fought.Contains(Id(u)))
                                   && destroyed(slot) == 0;
                case "B2": return Take(Numbered(0));
                case "B3": return Take(Numbered(1));
                case "B4": return Take(Numbered(2));
                case "B5": return DistributedViolence(c, slot);
                case "B6": return Take(t.ZoneMarker.GetValueOrDefault(slot));
            }
            return false;
        }

        private static float EdgeGapZ(IModel m, float edgeZ) => MathF.Abs(edgeZ - m.Position.z) - m.BaseShape.BoundingRadiusInches;
        private static float EdgeGapX(IModel m, float edgeX) => MathF.Abs(edgeX - m.Position.x) - m.BaseShape.BoundingRadiusInches;

        /// <summary>Valiant Drive: a model within 3" of the enemy's own long table edge (the one behind its zone).</summary>
        private static bool ValiantDrive(IGameContext c, TwistsGambitsState t, int slot)
        {
            float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            int other = Other(c, slot);
            if (!t.ZoneMarker.TryGetValue(other, out string? key) || ObjectiveAt(c, key) is not { } marker) return false;
            float edge = marker.Position.z > h / 2f ? h : 0f;
            return UnitsOf(c, slot).Any(u => u.Models.Any(m => m.GetIsAlive() && EdgeGapZ(m, edge) <= 3f + 0.01f));
        }

        /// <summary>Distributed Violence: one unit within 3" of each short edge, in the band between the deployment zones.</summary>
        private static bool DistributedViolence(IGameContext c, int slot)
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deep = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            bool Out(IModel m) => m.Position.z > deep && m.Position.z < h - deep;
            var units = UnitsOf(c, slot);
            var left = units.Where(u => u.Models.Any(m => m.GetIsAlive() && Out(m) && EdgeGapX(m, 0f) <= 3f + 0.01f)).ToList();
            var right = units.Where(u => u.Models.Any(m => m.GetIsAlive() && Out(m) && EdgeGapX(m, w) <= 3f + 0.01f)).ToList();
            return left.Any(a => right.Any(b => !ReferenceEquals(a, b)));
        }

        /// <summary>Silent Assault: every friendly unit within 3" of the marker stayed out of the shooting and the melee.</summary>
        private static bool QuietHolders(IGameContext c, TwistsGambitsState t, int slot, string key)
        {
            if (ObjectiveAt(c, key) is not { } o) return false;
            var near = UnitsOf(c, slot).Where(u => u.Models.Any(m => m.GetIsAlive()
                && BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing, o.Position) <= 3f + 0.01f)).ToList();
            return near.Count > 0 && near.All(u => !t.Attacked.Contains(Id(u)));
        }

        public const string NoScore = "Don't score a gambit";

        /// <summary>Scores one gambit for 4 VP if the seat didn't use a power this round; returns the VP.</summary>
        private static async Task<int> ScoreGambit(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo seat,
            Func<int, string, bool> holds, Func<int, string, bool> seized, int round)
        {
            if (!t.Dealt || t.PowerUsed.Contains(seat.SlotID)) return 0;
            List<string> done = HandOf(t, seat.SlotID).Distinct().Where(g => Scores(c, t, seat.SlotID, g, holds, seized)).ToList();
            if (done.Count == 0) return 0;
            string code = done[0];
            if (done.Count > 1)
            {
                var labels = done.Select(g => $"{Gambits[g].Name}: {ScoringText(c.Settings.GameSystem, g)}").ToList();
                string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                    $"Gambits: you've done {done.Count} of your gambits - score one for {GambitVp} VP (once per round).", labels,
                    new List<StringSelectionRequest.InvalidOption>(), displayName: "Twists & Gambits - score a gambit")
                { Recommended = labels[0] });
                int idx = labels.IndexOf(answer);
                if (idx >= 0) code = done[idx];
            }
            c.Log($"Twists & Gambits, round {round}: {seat.Name} scores {Gambits[code].Name} - +{GambitVp} VP.");
            await c.Announce($"{seat.Name} scores {Gambits[code].Name} (+{GambitVp} VP)", Gold, EBannerTier.Toast);
            Replace(c, t, seat, code);
            return GambitVp;
        }

        public const string KeepAll = "Keep all my gambits";

        /// <summary>"At the end of each round, players may also discard one of their gambits and roll for a new one."
        /// Not offered after the last round. A bot throws away the gambit it is least likely to score.</summary>
        private static async Task OfferDiscards(IGameContext c, TwistsGambitsState t, int round)
        {
            if (!t.Dealt || round >= HordeRules.RoundLimit(c.Settings, c.GameDataStore)) return;
            foreach (IPlayerSlotInfo seat in Seats(c))
            {
                List<string> hand = HandOf(t, seat.SlotID);
                if (hand.Count == 0) continue;
                var options = new List<string> { KeepAll };
                var codes = hand.Distinct().ToList();
                options.AddRange(codes.Select(g => $"Discard {Gambits[g].Name} ({ScoringText(c.Settings.GameSystem, g)})"));
                string? botDiscard = codes.FirstOrDefault(g => g is "B5" or "A2");
                string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                    "Gambits: discard one and roll a new one?", options, new List<StringSelectionRequest.InvalidOption>(),
                    displayName: "Twists & Gambits - discard a gambit")
                { Recommended = botDiscard != null ? options[codes.IndexOf(botDiscard) + 1] : KeepAll });
                int idx = options.IndexOf(answer);
                if (idx <= 0) continue;
                string code = codes[idx - 1];
                c.Log($"Twists & Gambits: {seat.Name} discards {Gambits[code].Name}.");
                Replace(c, t, seat, code);
            }
        }
    }
}
