using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution.Requests;

namespace FDG.Missions
{
    /// <summary>Twists &amp; Gambits' state, saved with the mission.</summary>
    public sealed class TwistsGambitsState
    {
        /// <summary>Objectives 1-3 (outside the deployment zones), "x,z" keys in number order.</summary>
        public List<string> Numbered { get; set; } = new();
        /// <summary>Each seat's objective inside its own deployment zone ("x,z").</summary>
        public Dictionary<int, string> ZoneMarker { get; set; } = new();
        /// <summary>This round's underdog seat (-1 = none).</summary>
        public int Underdog { get; set; } = -1;
        /// <summary>This round's twist ("A1".."B6"), "" when none applies.</summary>
        public string Twist { get; set; } = "";
        public List<string> PickedUnits { get; set; } = new();
        public List<string> PickedObjectives { get; set; } = new();
        /// <summary>Units alive at the start of the round (unit ids) - "each enemy unit they destroyed".</summary>
        public List<string> AliveAtStart { get; set; } = new();
        /// <summary>Who held each objective as the round began ("x,z" -> seat, -1 neutral) - "seized" this round.</summary>
        public Dictionary<string, int> OwnerAtStart { get; set; } = new();
        /// <summary>This round's twist VP from kills (Fighting Frenzy, Duel of Fate), by seat.</summary>
        public Dictionary<int, int> KillBonus { get; set; } = new();
        /// <summary>The last round's scoring, for the Mission panel ("seat: text").</summary>
        public List<string> LastScore { get; set; } = new();
        /// <summary>Each seat's three gambits ("A1".."B6"), dealt at the start of the game.</summary>
        public Dictionary<int, List<string>> Hand { get; set; } = new();
        public bool Dealt { get; set; }
        /// <summary>Seats that used a gambit's power this round - they can't score one at its end.</summary>
        public List<int> PowerUsed { get; set; } = new();
        /// <summary>Units that shot or fought in melee this round (unit ids) - Silent Assault.</summary>
        public List<string> Attacked { get; set; } = new();
        /// <summary>Units that fought in melee this round (unit ids) - Storm Strike.</summary>
        public List<string> Fought { get; set; } = new();
    }

    /// <summary>
    /// Twists &amp; Gambits (Advanced Rulebooks 3.6.0: GF p44-45, GFF p41-42, AoF p44-45, AoFS p41-42). Played as an Extra
    /// Mission: five markers, VP at the end of each round, 4 rounds, most VP wins; the underdog (fewer VP as the round
    /// begins) picks what each round's random twist affects. Book differences: AoF: Skirmish scores 2 VPs per objective;
    /// AoF's Strategic Divide scores "when controlled" where the others say "when seized".
    /// Judgement calls: house rules C, "Twists &amp; Gambits readings".
    /// </summary>
    public static partial class TwistsGambits
    {
        public static readonly TextColor Gold = new(255, 200, 90, 255);

        public static readonly IReadOnlyDictionary<string, (string Name, string Text)> Twists = new Dictionary<string, (string, string)>
        {
            ["A1"] = ("Heavenly Blessing", "Pick 1 underdog unit, which gets Regeneration."),
            ["A2"] = ("Curse of Sands", "Pick 1 non-underdog unit, which moves -2\" on Advance actions, and -4\" on Rush/Charge actions."),
            ["A3"] = ("Rusting Hex", "Pick 1 non-underdog unit, which gets -1 to Defense rolls."),
            ["A4"] = ("Shield Bubble", "Pick 1 objective, underdog units within 3\" get Stealth."),
            ["A5"] = ("Fighting Frenzy", "Pick 1 underdog unit, which gets +1 to hit rolls in melee and shooting, and which scores +1 VP when destroying units."),
            ["A6"] = ("Duel of Fate", "Pick 1 underdog and 1 non-underdog unit, which score +1 VP when destroyed."),
            ["B1"] = ("Strategic Divide", "Pick 2 objectives, which score +1 VP when seized."),
            ["B2"] = ("Target Priority", "Pick 1 objective, which scores +2 VPs when controlled."),
            ["B3"] = ("All-out-Assault", "Players get +3 VP when seizing the objective marker in the enemy deployment zone."),
            ["B4"] = ("Offensive Manoeuvre", "Pick 1 objective that's not controlled by the underdog, which scores +1 VP when seized by the underdog."),
            ["B5"] = ("Conqueror's Boon", "Pick 1 objective, which scores +1 VP when controlled, and underdog Heroes within 3\" of it at the end of the round may remove D3 wounds."),
            ["B6"] = ("Objective Denied", "Pick 1 objective, which stops being controlled by both armies, can't be seized until the end of the round, and the area within 3\" counts as Dangerous terrain."),
        };

        public static bool IsOn(IGameContext c) => ExtraMissions.Current(c) == EExtraMission.TwistsAndGambits;

        public static TwistsGambitsState? StateOf(IGameContext c) =>
            IsOn(c) ? GameProgressUtilities.ExtraMission(c.GameDataStore)?.Twists : null;

        /// <summary>"1 VP for each objective they control" - AoF: Skirmish prints 2 VPs.</summary>
        public static int ObjectiveVp(EGameSystem system) => system == EGameSystem.AoFSkirmish ? 2 : 1;

        /// <summary>AoF prints Strategic Divide "when controlled"; GF, Firefight and Skirmish "when seized".</summary>
        public static bool StrategicDivideWhenControlled(EGameSystem system) => system == EGameSystem.AgeOfFantasy;

        public static string Key(Position p) => $"{p.x:0.00},{p.z:0.00}";

        /// <summary>The three outside markers go between the deployment zones (the core 12" zones).</summary>
        public static RectangularZone OutsideBand()
        {
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            float deep = GameWideConstants.DEPLOYMENT_DISTANCE_INCHES;
            return new RectangularZone(0.5f, w - 0.5f, deep + 0.5f, h - deep - 0.5f);
        }

        private static IEnumerable<IPlayerSlotInfo> Seats(IGameContext c) =>
            c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID);

        private static IPlayerSlotInfo? Seat(IGameContext c, int slot) => Seats(c).FirstOrDefault(p => p.SlotID == slot);

        private static int Other(IGameContext c, int slot) => Seats(c).Select(p => p.SlotID).FirstOrDefault(s => s != slot, -1);

        private static int SlotOfUnit(IGameContext c, IUnit u) => MissionCardGame.SlotOf(c, u.PlayerID);

        private static string Id(IUnit u) => u.ID.ID.ToString();

        // ---------------------------------------------------------------- set-up

        /// <summary>
        /// The two markers inside the deployment zones ("one objective per zone"), placed by each side once the zones are
        /// known, anywhere in its own zone; then the three outside markers are numbered 1-3 from left to right.
        /// </summary>
        public static async Task PlaceZoneMarkers(IGameContext c, IReadOnlyDictionary<int, ZoneRect> zonesByTeam)
        {
            ExtraMissionState state = ExtraMissions.StateFor(c);
            TwistsGambitsState t = state.Twists ??= new TwistsGambitsState();
            if (state.OwnedPlaced) return;
            List<ITerrain> blocked = c.TableState.Terrain.Objects.Where(x => x.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();
            foreach ((int team, ZoneRect zone) in zonesByTeam.OrderBy(kv => kv.Key))
            {
                IPlayerSlotInfo? seat = c.TableState.Players.Objects.Where(p => p.IsFilled && p.TeamNumber == team).OrderBy(p => p.SlotID).FirstOrDefault();
                if (seat == null) continue;
                var band = new RectangularZone(zone.Left + 0.5f, zone.Right - 0.5f, zone.Bottom + 0.5f, zone.Top - 0.5f);
                Position? at = null;
                for (int attempt = 0; attempt < 5 && at == null; attempt++)
                {
                    Position wanted;
                    try
                    {
                        wanted = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(new PlaceObjectiveRequest(seat.PlayerID,
                            "Twists & Gambits: place your objective inside your deployment zone", 1, 1, band, 0f));
                    }
                    catch (InvalidOperationException) { break; }
                    if (ObjectivePlacementValidator.Check(wanted, band, 0f, c.TableState.Objectives.Objects.ToList(), blocked)
                        == ObjectivePlacementValidity.Valid) at = wanted;
                }
                Position p = at ?? new Position((band.Left + band.Right) / 2f, (band.Bottom + band.Top) / 2f);
                c.GameDataStore.Create(new ObjectiveData(p, c.GameDataStore));
                t.ZoneMarker[seat.SlotID] = Key(p);
                c.Log($"  Twists & Gambits: {seat.Name}'s zone objective at ({p.x:0.0}, {p.z:0.0}).");
            }
            t.Numbered = c.TableState.Objectives.Objects.Select(o => o.Position)
                .Where(p => !t.ZoneMarker.ContainsValue(Key(p)))
                .OrderBy(p => p.x).ThenBy(p => p.z).Take(3).Select(Key).ToList();
            for (int i = 0; i < t.Numbered.Count; i++) c.Log($"  Twists & Gambits: objective {i + 1} at ({t.Numbered[i]}).");
            state.OwnedPlaced = true;
            ExtraMissions.SaveState(c, state);
        }

        /// <summary>"Objective 2", "Kai's zone objective" - how a marker is named in questions and the panel.</summary>
        public static string Label(IGameContext c, TwistsGambitsState t, string key)
        {
            int n = t.Numbered.IndexOf(key);
            if (n >= 0) return $"Objective {n + 1}";
            foreach ((int slot, string k) in t.ZoneMarker)
                if (k == key) return $"{Seat(c, slot)?.Name ?? "?"}'s zone objective";
            return "Objective";
        }

        private static IObjective? ObjectiveAt(IGameContext c, string key) =>
            c.TableState.Objectives.Objects.FirstOrDefault(o => Key(o.Position) == key);

        private static int Holder(IGameContext c, IObjective o) =>
            o.OwnerID is PlayerID p ? MissionCardGame.SlotOf(c, p) : -1;

        // ---------------------------------------------------------------- the round

        /// <summary>
        /// The beginning of each round: note who holds what and which units stand (for "destroyed" and "seized"), work out
        /// the underdog, roll the twist and let the underdog pick what it affects.
        /// </summary>
        public static async Task RoundStart(IGameContext c, int round)
        {
            if (!IsOn(c)) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            TwistsGambitsState t = s.Twists ??= new TwistsGambitsState();
            t.AliveAtStart = c.TableState.Units.Objects.Where(u => u.GetIsAlive()).Select(Id).ToList();
            t.OwnerAtStart = c.TableState.Objectives.Objects.ToDictionary(o => Key(o.Position), o => Holder(c, o));
            t.KillBonus.Clear();
            t.PickedUnits.Clear();
            t.PickedObjectives.Clear();
            t.Twist = "";
            t.PowerUsed.Clear();
            t.Attacked.Clear();
            t.Fought.Clear();
            await RollTwist(c, s, t, round);
            await GambitsRoundStart(c, t, round);
            ExtraMissions.SaveState(c, s);
        }

        private static async Task RollTwist(IGameContext c, ExtraMissionState s, TwistsGambitsState t, int round)
        {
            var seats = Seats(c).ToList();
            t.Underdog = -1;
            if (seats.Count == 2)
            {
                int a = s.Vp.GetValueOrDefault(seats[0].SlotID), b = s.Vp.GetValueOrDefault(seats[1].SlotID);
                if (a != b) t.Underdog = a < b ? seats[0].SlotID : seats[1].SlotID;
            }

            int type = c.DiceRoller.RollDecisiveFace(), which = c.DiceRoller.RollDecisiveFace();
            string code = (type <= 3 ? "A" : "B") + which;
            (string name, string text) = Twists[code];
            if (t.Underdog < 0)
            {
                c.Log($"Twists & Gambits, round {round}: twist {code} {name} - no underdog (equal VP), so it is ignored.");
                await c.Announce($"Twist: {name} - ignored (no underdog)", Gold, EBannerTier.Toast);
                return;
            }
            t.Twist = code;
            IPlayerSlotInfo under = Seat(c, t.Underdog)!;
            c.Log($"Twists & Gambits, round {round}: {under.Name} is the underdog. Twist {code} {name}: {text}");
            await c.Announce($"Twist: {name} - {under.Name} is the underdog", Gold, EBannerTier.Notice);
            await ApplyTwist(c, t, under);
        }

        private static List<IUnit> UnitsOf(IGameContext c, int slot) =>
            c.TableState.Units.Objects.Where(u => SlotOfUnit(c, u) == slot && u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();

        private static async Task<IUnit?> PickUnit(IGameContext c, IPlayerSlotInfo who, List<IUnit> units, string question, IUnit? suggest)
        {
            if (units.Count == 0) return null;
            var names = new List<string>();
            foreach (IUnit u in units)
            {
                string n = u.Name;
                for (int i = 2; names.Contains(n); i++) n = $"{u.Name} ({i})";
                names.Add(n);
            }
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(who.PlayerID,
                question, names, new List<StringSelectionRequest.InvalidOption>(), displayName: "Twists & Gambits")
            { Recommended = suggest != null ? names[units.IndexOf(suggest)] : names[0] });
            int idx = names.IndexOf(answer);
            return idx >= 0 ? units[idx] : units[0];
        }

        private static async Task<string?> PickObjective(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo who, List<string> keys,
            string question, string? suggest)
        {
            if (keys.Count == 0) return null;
            var labels = keys.Select(k => Label(c, t, k)).ToList();
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(who.PlayerID,
                question, labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Twists & Gambits")
            { Recommended = suggest != null && keys.Contains(suggest) ? labels[keys.IndexOf(suggest)] : labels[0] });
            int idx = labels.IndexOf(answer);
            return idx >= 0 ? keys[idx] : keys[0];
        }

        /// <summary>A bot's pick of an objective: the one nearest its own units.</summary>
        private static string? NearestTo(IGameContext c, int slot, IEnumerable<string> keys)
        {
            var mine = UnitsOf(c, slot).SelectMany(u => u.Models.Where(m => m.GetIsAlive())).ToList();
            if (mine.Count == 0) return keys.FirstOrDefault();
            return keys.OrderBy(k => ObjectiveAt(c, k) is { } o ? mine.Min(m => Position.GetDistance2D(m.Position, o.Position)) : float.MaxValue)
                .FirstOrDefault();
        }

        private static IUnit? Priciest(List<IUnit> units) => units.OrderByDescending(u => (u as UnitData)?.PointCost ?? 0).FirstOrDefault();

        private static void GrantRuleForTheRound(IUnit unit, string rule) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.RoundEnd(),
                Payload: new TokenPayload.RuleGrant(rule, ELifetime.ThisRound)));

        private static void StatForTheRound(IUnit unit, ERollKind roll, int delta, string source) =>
            unit.Tokens.AddToken(new Token(RollModifierTokens.TypeFor(roll), 1, new TokenClearTrigger.RoundEnd(),
                Payload: new TokenPayload.StatModifier(delta, source)));

        private static async Task ApplyTwist(IGameContext c, TwistsGambitsState t, IPlayerSlotInfo under)
        {
            int u = under.SlotID, other = Other(c, u);
            List<IUnit> mine = UnitsOf(c, u), theirs = UnitsOf(c, other);
            List<string> allObjectives = c.TableState.Objectives.Objects.Select(o => Key(o.Position)).ToList();
            (string name, _) = Twists[t.Twist];
            string Ask(string what) => $"Twist - {name}: {what}";
            switch (t.Twist)
            {
                case "A1":
                    if (await PickUnit(c, under, mine, Ask("pick one of your units - it gets Regeneration this round."), Priciest(mine)) is { } a1)
                    {
                        GrantRuleForTheRound(a1, "Regeneration");
                        t.PickedUnits.Add(Id(a1));
                        c.Log($"  {a1.Name} gets Regeneration until the end of the round.");
                    }
                    break;
                case "A2":
                    if (await PickUnit(c, under, theirs, Ask("pick an enemy unit - -2\" Advance, -4\" Rush and Charge this round."), Priciest(theirs)) is { } a2)
                    {
                        t.PickedUnits.Add(Id(a2));
                        c.Log($"  {a2.Name} is cursed: -2\" Advance, -4\" Rush / Charge until the end of the round.");
                    }
                    break;
                case "A3":
                    if (await PickUnit(c, under, theirs, Ask("pick an enemy unit - -1 to its Defense rolls this round."), Priciest(theirs)) is { } a3)
                    {
                        StatForTheRound(a3, ERollKind.Save, -1, "Rusting Hex");
                        t.PickedUnits.Add(Id(a3));
                        c.Log($"  {a3.Name} gets -1 to Defense rolls until the end of the round.");
                    }
                    break;
                case "A4":
                    if (await PickObjective(c, t, under, allObjectives, Ask("pick an objective - your units within 3\" of it get Stealth this round."),
                            NearestTo(c, u, allObjectives)) is { } a4)
                    {
                        t.PickedObjectives.Add(a4);
                        c.Log($"  {Label(c, t, a4)}: {under.Name}'s units within 3\" get Stealth this round.");
                    }
                    break;
                case "A5":
                    if (await PickUnit(c, under, mine, Ask("pick one of your units - +1 to hit this round, and +1 VP for each unit it destroys."), Priciest(mine)) is { } a5)
                    {
                        StatForTheRound(a5, ERollKind.Hit, 1, "Fighting Frenzy");
                        t.PickedUnits.Add(Id(a5));
                        c.Log($"  {a5.Name} gets +1 to hit this round and scores +1 VP per unit it destroys.");
                    }
                    break;
                case "A6":
                    if (await PickUnit(c, under, mine, Ask("pick one of your units - it scores +1 VP for whoever destroys it this round."), Priciest(mine)) is { } a6a)
                        t.PickedUnits.Add(Id(a6a));
                    if (await PickUnit(c, under, theirs, Ask("pick an enemy unit - it scores +1 VP for whoever destroys it this round."),
                            theirs.OrderBy(x => (x as UnitData)?.PointCost ?? 0).FirstOrDefault()) is { } a6b)
                        t.PickedUnits.Add(Id(a6b));
                    c.Log($"  Duel of Fate: {string.Join(" and ", t.PickedUnits.Select(id => c.TableState.Units.Objects.FirstOrDefault(x => Id(x) == id)?.Name))}.");
                    break;
                case "B1":
                    for (int i = 0; i < 2; i++)
                    {
                        var left = allObjectives.Except(t.PickedObjectives).ToList();
                        if (await PickObjective(c, t, under, left, Ask($"pick objective {i + 1} of 2 - +1 VP when seized this round."), NearestTo(c, u, left)) is { } b1)
                            t.PickedObjectives.Add(b1);
                    }
                    c.Log($"  Strategic Divide: {string.Join(" and ", t.PickedObjectives.Select(k => Label(c, t, k)))}.");
                    break;
                case "B2":
                case "B5":
                    if (await PickObjective(c, t, under, allObjectives,
                            Ask(t.Twist == "B2" ? "pick an objective - +2 VP for holding it at the end of the round."
                                : "pick an objective - +1 VP for holding it, and your Heroes within 3\" heal D3 wounds at the end of the round."),
                            NearestTo(c, u, allObjectives)) is { } b2)
                    {
                        t.PickedObjectives.Add(b2);
                        c.Log($"  {name}: {Label(c, t, b2)}.");
                    }
                    break;
                case "B3":
                    c.Log("  All-out-Assault: +3 VP for seizing the objective in the enemy deployment zone this round (both players).");
                    break;
                case "B4":
                {
                    var notMine = allObjectives.Where(k => ObjectiveAt(c, k) is { } o && Holder(c, o) != u).ToList();
                    if (await PickObjective(c, t, under, notMine, Ask("pick an objective you don't hold - +1 VP if you seize it this round."),
                            NearestTo(c, u, notMine)) is { } b4)
                    {
                        t.PickedObjectives.Add(b4);
                        c.Log($"  Offensive Manoeuvre: {Label(c, t, b4)}.");
                    }
                    break;
                }
                case "B6":
                {
                    string? suggest = allObjectives.FirstOrDefault(k => ObjectiveAt(c, k) is { } o && Holder(c, o) == other)
                                      ?? NearestTo(c, other, allObjectives);
                    if (await PickObjective(c, t, under, allObjectives,
                            Ask("pick an objective - it goes neutral, can't be seized this round, and 3\" around it is Dangerous terrain."), suggest) is { } b6
                        && ObjectiveAt(c, b6) is { } denied)
                    {
                        t.PickedObjectives.Add(b6);
                        denied.SetOwner(null);
                        c.GameDataStore.Create(new TerrainData(ETerrainType.Dangerous,
                            new CircularZone(new Float2(denied.Position.x, denied.Position.z), 3f), heightInches: 0f, name: DeniedTerrainName));
                        c.Log($"  Objective Denied: {Label(c, t, b6)} is neutral and can't be seized this round; 3\" around it is Dangerous terrain.");
                    }
                    break;
                }
            }
        }

        private const string DeniedTerrainName = "Twist: Objective Denied";

        // ---------------------------------------------------------------- during the round

        /// <summary>Curse of Sands: (Advance, Rush/Charge) inches off the picked unit's moves this round.</summary>
        public static (float Advance, float Rush) MoveChange(IGameContext c, IUnit unit) =>
            StateOf(c) is { Twist: "A2" } t && t.PickedUnits.Contains(Id(unit)) ? (-2f, -4f) : (0f, 0f);

        /// <summary>Shield Bubble: the underdog's units within 3\" of the picked objective have Stealth when shot - given
        /// for this one attack, right before its hit roll.</summary>
        public static void BeforeShot(IGameContext c, IUnit target, bool melee, bool spell)
        {
            if (melee || spell || StateOf(c) is not { Twist: "A4" } t || t.PickedObjectives.Count == 0) return;
            if (SlotOfUnit(c, target) != t.Underdog || ObjectiveAt(c, t.PickedObjectives[0]) is not { } o) return;
            bool near = target.Models.Any(m => m.GetIsAlive()
                && BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing, o.Position) <= 3f + 0.01f);
            if (!near) return;
            target.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.AttackEnd(),
                Payload: new TokenPayload.RuleGrant("Stealth", ELifetime.ThisAttack)));
            c.Log($"Shield Bubble: {target.Name} has Stealth near {Label(c, t, t.PickedObjectives[0])}.");
        }

        /// <summary>Objective Denied: the picked marker "can't be seized until the end of the round".</summary>
        public static bool Denied(IGameContext c, IObjective o) =>
            StateOf(c) is { Twist: "B6" } t && t.PickedObjectives.Contains(Key(o.Position));

        /// <summary>A unit was destroyed: Fighting Frenzy's +1 VP for its killer, Duel of Fate's +1 VP for whoever
        /// destroyed a picked unit (the other side, whatever the cause).</summary>
        public static void UnitDestroyed(IGameContext c, IUnit dead, IUnit? killer)
        {
            if (StateOf(c) is not { } t || t.Twist == "") return;
            int victim = SlotOfUnit(c, dead), scorer = Other(c, victim);
            if (scorer < 0) return;
            if (t.Twist == "A5" && killer != null && t.PickedUnits.Contains(Id(killer)) && SlotOfUnit(c, killer) != victim)
            {
                t.KillBonus[scorer] = t.KillBonus.GetValueOrDefault(scorer) + 1;
                c.Log($"Fighting Frenzy: {killer.Name} destroyed {dead.Name} - +1 VP.");
            }
            if (t.Twist == "A6" && t.PickedUnits.Contains(Id(dead)))
            {
                t.KillBonus[scorer] = t.KillBonus.GetValueOrDefault(scorer) + 1;
                c.Log($"Duel of Fate: {dead.Name} destroyed - +1 VP for {Seat(c, scorer)?.Name}.");
            }
            ExtraMissions.SaveState(c, ExtraMissions.StateFor(c));
        }

        // ---------------------------------------------------------------- the end of the round

        /// <summary>
        /// "At the end of each round, players score victory points: 1 VP for each enemy unit they destroyed, 1 VP for each
        /// objective they control, 1 VP if they control more objectives than their opponent" - plus the twist's VP.
        /// Called after objectives are seized.
        /// </summary>
        public static async Task RoundEnd(IGameContext c, ExtraMissionState s, int round)
        {
            TwistsGambitsState t = s.Twists ??= new TwistsGambitsState();
            var seats = Seats(c).ToList();
            var held = new Dictionary<int, List<string>>();
            foreach (IObjective o in c.TableState.Objectives.Objects)
                if (Holder(c, o) is var h and >= 0) (held.TryGetValue(h, out var l) ? l : held[h] = new List<string>()).Add(Key(o.Position));
            bool Holds(int slot, string key) => held.GetValueOrDefault(slot)?.Contains(key) == true;
            bool Seized(int slot, string key) => held.GetValueOrDefault(slot)?.Contains(key) == true && t.OwnerAtStart.GetValueOrDefault(key, -1) != slot;
            int perObjective = ObjectiveVp(c.Settings.GameSystem);
            t.LastScore.Clear();

            foreach (IPlayerSlotInfo seat in seats)
            {
                int slot = seat.SlotID, other = Other(c, slot);
                var parts = new List<string>();
                int destroyed = c.TableState.Units.Objects.Count(u => SlotOfUnit(c, u) == other && !u.GetIsAlive() && t.AliveAtStart.Contains(Id(u)));
                int mine = held.GetValueOrDefault(slot)?.Count ?? 0, theirs = held.GetValueOrDefault(other)?.Count ?? 0;
                int vp = destroyed + mine * perObjective + (mine > theirs ? 1 : 0);
                if (destroyed > 0) parts.Add($"{destroyed} destroyed");
                if (mine > 0) parts.Add($"{mine} objective{(mine == 1 ? "" : "s")}");
                if (mine > theirs) parts.Add("more objectives");

                int twist = t.KillBonus.GetValueOrDefault(slot);
                switch (t.Twist)
                {
                    case "B1":
                        twist += t.PickedObjectives.Count(k => StrategicDivideWhenControlled(c.Settings.GameSystem)
                            ? held.GetValueOrDefault(slot)?.Contains(k) == true : Seized(slot, k));
                        break;
                    case "B2":
                        twist += t.PickedObjectives.Count(k => held.GetValueOrDefault(slot)?.Contains(k) == true) * 2;
                        break;
                    case "B3":
                        if (t.ZoneMarker.TryGetValue(other, out string? enemyZone) && Seized(slot, enemyZone)) twist += 3;
                        break;
                    case "B4":
                        if (slot == t.Underdog) twist += t.PickedObjectives.Count(k => Seized(slot, k));
                        break;
                    case "B5":
                        twist += t.PickedObjectives.Count(k => held.GetValueOrDefault(slot)?.Contains(k) == true);
                        break;
                }
                if (twist > 0) parts.Add($"twist +{twist}");
                vp += twist;
                int gambit = await ScoreGambit(c, t, seat, Holds, Seized, round);
                if (gambit > 0) parts.Add($"gambit +{gambit}");
                vp += gambit;
                s.Vp[slot] = s.Vp.GetValueOrDefault(slot) + vp;
                string line = $"{seat.Name} +{vp} VP ({(parts.Count > 0 ? string.Join(", ", parts) : "nothing")}) - {s.Vp[slot]} in all";
                t.LastScore.Add(line);
                c.Log($"Twists & Gambits, round {round}: {line}.");
            }

            // Conqueror's Boon: "underdog Heroes within 3\" of it at the end of the round may remove D3 wounds".
            if (t.Twist == "B5" && t.PickedObjectives.Count > 0 && ObjectiveAt(c, t.PickedObjectives[0]) is { } boon)
                foreach (IUnit hero in UnitsOf(c, t.Underdog).Where(MissionCardGame.UnitHasHero))
                    if (hero.Models.Any(m => m.GetIsAlive() && BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing, boon.Position) <= 3f + 0.01f))
                        await new GameOperationServices(c).RestoreD3(hero, "Conqueror's Boon");

            await OfferDiscards(c, t, round);

            // Objective Denied's Dangerous area is this round's only.
            foreach (DataReference r in c.GameDataStore.GetAllDataReferences<TerrainData>().ToList())
                if (c.GameDataStore.IsValid(r, out _) && c.GameDataStore.GetValue<TerrainData>(r).Name == DeniedTerrainName)
                    c.GameDataStore.Destroy(r);
        }

        /// <summary>The Mission panel's lines (text, hover).</summary>
        public static IEnumerable<(string Text, string Hover)> PanelLines(ITableState table, TwistsGambitsState t,
            EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            var seats = table.Players.Objects.Where(p => p.IsFilled).ToList();
            string Name(int slot) => seats.FirstOrDefault(p => p.SlotID == slot)?.Name ?? "?";
            string HolderName(IObjective o) => o.OwnerID is PlayerID id && seats.FirstOrDefault(p => p.PlayerID.Equals(id)) is { } p ? p.Name : "neutral";
            string LabelOf(string key)
            {
                int n = t.Numbered.IndexOf(key);
                if (n >= 0) return $"Objective {n + 1}";
                foreach ((int slot, string k) in t.ZoneMarker) if (k == key) return $"{Name(slot)}'s zone objective";
                return "Objective";
            }
            yield return (t.Underdog >= 0 ? $"Underdog: {Name(t.Underdog)}" : "Underdog: none (equal VP)",
                "The player with fewer VP as the round begins. They pick what the twist affects; with no underdog the twist is ignored.");
            if (t.Twist != "" && Twists.TryGetValue(t.Twist, out var tw))
            {
                string what = t.PickedObjectives.Count > 0 ? " - " + string.Join(", ", t.PickedObjectives.Select(LabelOf))
                    : t.PickedUnits.Count > 0 ? " - " + string.Join(", ", t.PickedUnits.Select(id => table.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == id)?.Name))
                    : "";
                yield return ($"Twist: {tw.Name}{what}", tw.Text);
            }
            foreach (IObjective o in table.Objectives.Objects.OrderBy(o => LabelOf(Key(o.Position))))
                yield return ($"{LabelOf(Key(o.Position))}: {HolderName(o)}", "");
            foreach ((int slot, List<string> hand) in t.Hand.OrderBy(kv => kv.Key))
                if (hand.Count > 0)
                    yield return ($"{Name(slot)}'s gambits: {string.Join(", ", hand.Select(g => Gambits[g].Name))}",
                        string.Join("\n\n", hand.Distinct().Select(g =>
                            $"{Gambits[g].Name}\nPower: {PowerText(system, g)}\nScoring ({GambitVp} VP): {ScoringText(system, g)}"))
                        + "\n\nOnce per round: use one power at the start of the round, or score one gambit at its end.");
            foreach (string line in t.LastScore) yield return ("Last round: " + line, "");
        }
    }
}
