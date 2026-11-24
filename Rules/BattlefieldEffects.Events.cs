using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Campaign;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Utilities;

namespace FDG
{
    /// <summary>A marker a random event put on the table (Anomaly, Zombie Horde, Sniper, Swarm, Havoc Spawn, Robot, Rebels).</summary>
    [Serializable]
    public sealed class EventMarker
    {
        public int Code { get; set; }
        public float X { get; set; }
        public float Z { get; set; }
    }

    /// <summary>
    /// Battlefield Effects - Random Events (Advanced Rulebooks 3.6.0: GF p40-41, GFF p37-38, AoF p40-41, AoFS p37-38).
    /// "At the beginning of each round, roll one die. On a 5+ an event is triggered, and you must roll two dice to
    /// determine which one is triggered ... if a random event calls for randomly selecting a unit from any army, you
    /// must first randomly select an army for the event, and then randomly select a unit to be the target."
    /// Judgement calls: house rules C, "Random Events readings".
    /// </summary>
    public static partial class BattlefieldEffects
    {
        public const string RandomEventsName = "Random Events";

        private const string EventsBookText = "At the beginning of each round, roll one die. On a 5+ an event is triggered, and you must roll two dice to determine which one (11-66). If an event calls for randomly selecting a unit from any army, first randomly select an army, then randomly select a unit. 36 events: units hit or wounded, markers that move and hurt, round-long bonuses and penalties, and first-time-this-round surprises.";
        private const string EventsGameText = "Rolled at the start of every round (after reserves arrive), announced, and listed on the Mission panel with the book's text on hover. Event markers (Zombie Horde, Havoc Spawn, Rebel Squad...) can't be attacked - see the house rules. Bots move the markers toward your units.";

        public static bool EventsOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.RandomEvents);

        private static bool Skirmish(IGameContext c) => c.Settings.GameSystem is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        // ================================================================= the table

        public static string EventName(int code, EGameSystem s)
        {
            bool f = Fantasy(s);
            return code switch
            {
                11 => "Monstrous Spawn", 12 => "Low Supplies", 13 => "Broken Morale", 14 => "Surprise Attack",
                15 => "Chipped Weapons", 16 => "Mutated Beast",
                21 => f ? "Magic Anomaly" : "Psychic Anomaly", 22 => "Carnivorous Fauna", 23 => "Toxic Fumes",
                24 => "Heavy Fog", 25 => "The End is Nigh", 26 => "Havoc God's Playthings",
                31 => "The Walking Dead", 32 => "Crazed Sniper", 33 => f ? "Pied Piper" : "Alien Plague",
                34 => "Havoc Nuisance", 35 => f ? "Arcane Defense System" : "Ancient Defense System", 36 => "Rebel Uprising",
                41 => "Grave Miscalculation", 42 => "Berserker", 43 => "Rigged to Blow", 44 => "Panic Attack",
                45 => "Suppressive Fire", 46 => "From Bad to Worse",
                51 => "Fighting Vigour", 52 => f ? "Magical Gravity" : "Gravitational Shift", 53 => "Godly Blessing",
                54 => "Land of the Brave", 55 => "Get back in the Fight!", 56 => "Shimmering Mist",
                61 => f ? "Flash Bombs" : "Flash Grenades", 62 => "Suppressing Fire", 63 => "War Shout",
                64 => "Covered in Blood", 65 => f ? "Magical Boots" : "Anti-Grav Boots", 66 => "Deep Rage",
                _ => "None",
            };
        }

        public static string EventText(int code, EGameSystem s)
        {
            bool f = Fantasy(s), skirmish = s is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;
            return code switch
            {
                11 => "Select one random unit, which takes 2D3 hits.",
                12 => "Select D3 random units, which can't shoot any of their weapons until the end of the mission.",
                13 => "All units from one random army get -1 to Morale Test rolls until the end of the game.",
                14 => "Select one random unit that is inside a piece of terrain. That unit is Shaken.",
                15 => "Select D3 random units, which can't strike in melee until the end of the round.",
                16 => "Select one random unit, which takes D3 wounds.",
                21 => "Place an anomaly marker in the center of one random table quarter. All units that move within 3\" of it take 2D3 hits.",
                22 => "Until the end of the mission when a unit enters terrain roll one die, on a 1 it takes a Dangerous Terrain Test.",
                23 => $"Select one random table quarter. All of the units within that table quarter take {ToxicDice(s)} hits.",
                24 => "All units get -1 to hit rolls when shooting until the end of the round.",
                25 => skirmish ? "All units fail all Morale Tests taken at the end of the round." : "All units fail all Morale Tests until the end of the round.",
                26 => "Select one random table quarter. All units within that table quarter count as Fatigued until the end of the round.",
                31 => $"Place one Zombie Horde marker in the centre of one random table quarter, which counts having Def 6+ and Tough({(s == EGameSystem.AoFSkirmish ? 9 : 12)}). At the end of each round, the marker moves D6+6\" towards the closest unit. The area within 3\" of the marker counts as Dangerous terrain.",
                32 => "Place a Sniper on the highest piece of terrain with most line of sight on the table. At the end of each round the closest unit in line of sight of the sniper takes D3 wounds.",
                33 => f ? "Place one Rat Swarm marker in the centre of one random table quarter. At the end of each round, the marker moves D6+6\" towards the closest unit. The area within 3\" of the marker counts as Difficult terrain."
                        : "Place one Alien Swarm marker in the centre of one random table quarter. At the end of each round, the marker moves D6+6\" towards the closest unit. The area within 3\" of the marker counts as Difficult terrain.",
                34 => $"Place one Havoc Spawn marker in the centre of one random table quarter, which counts as having Def 4+ and Tough({(s == EGameSystem.AoFSkirmish ? 12 : 6)}). At the end of each round, one random player may move the marker by up to D6+6\". The area within 3\" of the marker counts as Dangerous terrain, but deals wounds on rolls of 1-2.",
                35 => f ? "Place an Ancient Guardian in the center of one random table quarter. At the end of each round the closest unit in line of sight of the ancient guardian within 12\" takes D3 wounds."
                        : "Place an Ancient Robot in the center of one random table quarter. At the end of each round the closest unit in line of sight of the ancient robot within 12\" takes D3 wounds.",
                36 => "Place one Rebel Squad marker in the centre of one random table quarter, which counts as having Def 5+ and Tough(6). At the end of each round, one random player may move the marker by up to D6+6\", and then the closest unit in line of sight takes 2D3 hits.",
                41 => "The first time during this round when a unit rolls one or more results of 1 to hit when shooting it takes D3 hits for each.",
                42 => "The first time during this round when a unit rolls one or more results of 6 to hit in melee the target takes 1 wound for each.",
                43 => "Select one random player, who may note down one piece of terrain in secret. The first unit to enter that terrain takes 2D3 hits.",
                44 => "The first time during this round when a unit rolls a 1 when taking a Morale Test, that unit Routs.",
                45 => "The first time during this round when a unit rolls one or more results of 6 to hit for shooting the target must take a Morale Test, and gets -1 to its roll for each result of 6.",
                46 => "The first time during this round when a unit rolls one or more results of 1 to hit in melee, it counts as having taken +1 wound for each when checking who won melee.",
                51 => "Select one random table quarter. All units within that table quarter get +1 to hit rolls when shooting until the end of the round.",
                52 => "All units within 18\" of the centre of the table move +1\" on Advance and +2\" on Rush or Charge actions until the end of the round.",
                53 => "All units get AP(+1) in melee until the end of the round.",
                54 => skirmish ? "Select one random table quarter. All units within that table quarter pass all Morale Tests taken at the end of the round."
                        : "Select one random table quarter. All units within that table quarter pass all Morale Tests until the end of the round.",
                55 => "All units within 18\" of any table corner may immediately move by up to 6\" in any direction.",
                56 => "Select one random table quarter. All units within that table quarter count as being in Cover until the end of the mission.",
                61 => "Select one random army. Enemy units get -1 to hit rolls in melee when attacking units from that army until the end of the round.",
                62 => "Select one random unit, which doubles its shooting attacks during this round.",
                63 => "Select one random unit that is Shaken, which stops being Shaken.",
                64 => "Select one random unit. Enemy units within 6\" of it get -1 to morale until the end of the mission.",
                65 => "Select one random unit, which moves +D3\" on Advance and +2D3\" on Rush or Charge actions until the end of the mission.",
                66 => "Select one random unit, which gets +1 to hit rolls in melee until the end of the round.",
                _ => "",
            };
        }

        private static string ToxicDice(EGameSystem s) => s switch
        {
            EGameSystem.GFFirefight => "D3+4",
            EGameSystem.AoFSkirmish => "D3",
            _ => "2D3",
        };

        // ================================================================= dice and picks

        private static int D2D3(IGameContext c) => D3(c) + D3(c);
        private static Func<int> Die(IGameContext c) => () => D6(c);
        private static float W => HordePlacement.TableWidth;
        private static float H => HordePlacement.TableHeight;

        private static bool OnTable(IUnit u) => u.GetIsAlive() && !ReserveRules.IsInReserve(u)
            && !TransportUtilities.IsEmbarked(u);

        /// <summary>"First randomly select an army, then randomly select a unit."</summary>
        private static IUnit? RandomUnit(IGameContext c, Func<IUnit, bool>? filter = null)
        {
            var armies = c.TableState.Units.Objects.Where(u => OnTable(u) && (filter == null || filter(u)))
                .Select(u => u.PlayerID).Distinct().ToList();
            if (armies.Count == 0) return null;
            PlayerID army = armies[CampaignEventTargeting.Index(armies.Count, Die(c))];
            var units = c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(army) && OnTable(u) && (filter == null || filter(u))).ToList();
            return units.Count == 0 ? null : units[CampaignEventTargeting.Index(units.Count, Die(c))];
        }

        private static PlayerID? RandomArmy(IGameContext c)
        {
            var armies = CampaignEventTargeting.PlayersInPlay(c.TableState);
            return armies.Count == 0 ? null : armies[CampaignEventTargeting.Index(armies.Count, Die(c))];
        }

        private static ZoneRect RandomQuarter(IGameContext c) =>
            CampaignEventTargeting.PickQuarter(W, H, Die(c)).Area;

        private static bool InQuarter(IUnit u, ZoneRect q) => u.Models.Any(m => m.GetIsAlive() && q.Contains(m.Position));

        private static (float X, float Z) Centre(ZoneRect q) => ((q.Left + q.Right) / 2f, (q.Bottom + q.Top) / 2f);

        private static void Tag(IUnit u, TokenType t, int n = 1)
        {
            for (int i = 0; i < n; i++) u.Tokens.AddToken(TokenDefinitionCatalog.Create(t));
        }

        private static void Untag(IUnit u, TokenType t)
        {
            int n = u.Tokens.GetTokenCount(t);
            if (n > 0) u.Tokens.RemoveTokens(t, n);
        }

        // ================================================================= start of round

        /// <summary>Start of every round: a 5+ fires one of the 36 events.</summary>
        public static async Task RollEvent(IGameContext c)
        {
            if (!EventsOn(c)) return;
            int trigger = D6(c);
            if (trigger < 5)
            {
                c.Log($"Random Events: rolled {trigger} - no event this round (needs 5+).");
                return;
            }
            int code = D6(c) * 10 + D6(c);
            await Fire(c, code);
        }

        /// <summary>Resolves one event (public for the tests and the headless tools).</summary>
        public static async Task Fire(IGameContext c, int code)
        {
            EGameSystem sys = c.Settings.GameSystem;
            BattlefieldEffectsState s = Ensure(c);
            string name = EventName(code, sys);
            c.Log($"Random Event {code} - {name}: {EventText(code, sys)}");
            s.RoundEvent = code;
            s.RoundEventSpent = false;
            string what = "";
            switch (code)
            {
                case 11:
                case 16:
                    if (RandomUnit(c) is { } hit)
                    {
                        what = $"{hit.Name}";
                        if (code == 11) await CampaignEventRunner.DealHits(c, hit, D2D3(c), name);
                        else await CasualtyPresentation.ApplyUnitWounds(c, hit, D3(c));
                    }
                    break;
                case 12:
                case 15:
                {
                    var names = new List<string>();
                    int n = D3(c);
                    for (int i = 0; i < n; i++)
                        if (RandomUnit(c, u => !u.Tokens.HasToken(code == 12 ? TokenType.LowSupplies : TokenType.ChippedWeapons)) is { } u)
                        {
                            Tag(u, code == 12 ? TokenType.LowSupplies : TokenType.ChippedWeapons);
                            names.Add(u.Name);
                        }
                    what = string.Join(", ", names);
                    break;
                }
                case 13:
                    if (RandomArmy(c) is { } army)
                    {
                        what = c.GetPlayerName(army) + "'s army";
                        foreach (IUnit u in c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(army)))
                            if (!u.Tokens.HasToken(TokenType.BrokenMorale)) Tag(u, TokenType.BrokenMorale);
                        s.BrokenMoraleArmies.Add(army.ID.ToString());
                    }
                    break;
                case 14:
                {
                    var pieces = c.TableState.Terrain.Objects.ToList();
                    if (RandomUnit(c, u => !u.Tokens.HasToken(TokenType.Shaken) && pieces.Any(p => InPiece(u, p))) is { } u
                        && UnitPsychology.BindingOf(c, u) is { } b)
                    {
                        what = u.Name;
                        await MoraleUtilities.ApplyShakenWithPresentation(c, b);
                    }
                    break;
                }
                case 21: case 31: case 33: case 34: case 35: case 36:
                {
                    (float x, float z) = Centre(RandomQuarter(c));
                    s.Markers.Add(new EventMarker { Code = code, X = x, Z = z });
                    what = $"marker at ({x:0}, {z:0})";
                    break;
                }
                case 32:
                {
                    var high = c.TableState.Terrain.Objects.Where(t => t.Shape is IBoundedZone).OrderByDescending(t => t.HeightInches)
                        .ThenByDescending(t => SightCount(c, t)).FirstOrDefault();
                    if (high?.Shape is IBoundedZone z)
                    {
                        s.Markers.Add(new EventMarker { Code = 32, X = z.Bounds.CenterX, Z = z.Bounds.CenterZ });
                        what = $"on {Describe(high)}";
                    }
                    else
                    {
                        s.Markers.Add(new EventMarker { Code = 32, X = W / 2f, Z = H / 2f });
                        what = "in the middle of the table (no terrain)";
                    }
                    break;
                }
                case 22: s.Fauna = true; break;
                case 23:
                {
                    ZoneRect q = RandomQuarter(c);
                    foreach (IUnit u in c.TableState.Units.Objects.Where(u => OnTable(u) && InQuarter(u, q)).ToList())
                    {
                        int hits = sys switch { EGameSystem.GFFirefight => D3(c) + 4, EGameSystem.AoFSkirmish => D3(c), _ => D2D3(c) };
                        await CampaignEventRunner.DealHits(c, u, hits, name);
                    }
                    what = QuarterName(q);
                    break;
                }
                case 26:
                {
                    ZoneRect q = RandomQuarter(c);
                    foreach (IUnit u in c.TableState.Units.Objects.Where(u => OnTable(u) && InQuarter(u, q)))
                        if (!u.Tokens.HasToken(TokenType.Fatigued)) Tag(u, TokenType.Fatigued);
                    what = QuarterName(q);
                    break;
                }
                case 51:
                case 54:
                    s.RoundQuarter = RandomQuarter(c);
                    what = QuarterName(s.RoundQuarter);
                    break;
                case 56:
                {
                    ZoneRect q = RandomQuarter(c);
                    s.MistQuarters.Add(q);
                    what = QuarterName(q);
                    break;
                }
                case 43:
                    what = await RigTerrain(c, s);
                    break;
                case 55:
                    await GetBackInTheFight(c);
                    break;
                case 61:
                    if (RandomArmy(c) is { } flash)
                    {
                        what = c.GetPlayerName(flash) + "'s army";
                        foreach (IUnit u in c.TableState.Units.Objects.Where(u => u.PlayerID.Equals(flash)))
                            Tag(u, TokenType.FlashGrenades);
                    }
                    break;
                case 62:
                    if (RandomUnit(c) is { } shooter) { Tag(shooter, TokenType.DoubleShots); what = shooter.Name; }
                    break;
                case 63:
                    if (RandomUnit(c, u => u.Tokens.HasToken(TokenType.Shaken)) is { } shaken)
                    {
                        Untag(shaken, TokenType.Shaken);
                        what = shaken.Name;
                    }
                    break;
                case 64:
                    if (RandomUnit(c) is { } bloody) { Tag(bloody, TokenType.CoveredInBlood); what = bloody.Name; }
                    break;
                case 65:
                    if (RandomUnit(c) is { } boots)
                    {
                        int adv = D3(c), rush = D2D3(c);
                        Tag(boots, TokenType.BootsAdvance, adv);
                        Tag(boots, TokenType.BootsRush, rush);
                        what = $"{boots.Name} (+{adv}\" Advance, +{rush}\" Rush/Charge)";
                    }
                    break;
                case 66:
                    if (RandomUnit(c) is { } rage) { Tag(rage, TokenType.DeepRage); what = rage.Name; }
                    break;
            }
            Save(c, s);
            await c.Announce($"Random Event: {name}{(what.Length > 0 ? $" - {what}" : "")}.", Amber, EBannerTier.Notice);
        }

        private static string QuarterName(ZoneRect q) =>
            $"the {(q.Bottom > 0.01f ? "far" : "near")} {(q.Left > 0.01f ? "right" : "left")} quarter";

        private static int SightCount(IGameContext c, ITerrain t)
        {
            if (t.Shape is not IBoundedZone z) return 0;
            var at = new Position(z.Bounds.CenterX, z.Bounds.CenterZ);
            var terrain = c.TableState.Terrain.Objects.Where(o => !ReferenceEquals(o, t)).ToList();
            int n = 0;
            for (int i = 1; i < 8; i++)
                for (int j = 1; j < 6; j++)
                    if (LineOfSightUtilities.HasLineOfSight(at, new Position(W * i / 8f, H * j / 6f), terrain)) n++;
            return n;
        }

        // ================================================================= 43 Rigged to Blow

        private static async Task<string> RigTerrain(IGameContext c, BattlefieldEffectsState s)
        {
            var pieces = c.TableState.Terrain.Objects.ToList();
            if (pieces.Count == 0 || RandomArmy(c) is not { } player) return "no terrain to rig";
            // The bot's pick (first): the piece nearest the middle of the table, the ground both sides cross.
            var order = Enumerable.Range(0, pieces.Count).OrderBy(i => pieces[i].Shape is IBoundedZone z
                ? MathF.Abs(z.Bounds.CenterX - W / 2f) + MathF.Abs(z.Bounds.CenterZ - H / 2f) : 999f).ToList();
            var labels = new List<string>();
            foreach (int i in order)
            {
                string label = $"{Describe(pieces[i])}" + (pieces[i].Shape is IBoundedZone z ? $" at ({z.Bounds.CenterX:0}, {z.Bounds.CenterZ:0})" : $" #{i + 1}");
                while (labels.Contains(label)) label += "'";
                labels.Add(label);
            }
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                "Rigged to Blow: note down one piece of terrain in secret. The first unit to enter it takes 2D3 hits.",
                labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Rigged to Blow") { Recommended = labels[0] });
            int idx = labels.IndexOf(pick);
            s.RiggedPiece = order[idx < 0 ? 0 : idx];
            return $"{c.GetPlayerName(player)} rigged a piece in secret";
        }

        // ================================================================= 55 Get back in the Fight!

        public const string FightTowardObjective = "Toward the nearest objective";
        public const string FightTowardEnemy = "Toward the nearest enemy";
        public const string FightStay = "Stay where it is";

        private static async Task GetBackInTheFight(IGameContext c)
        {
            var corners = new[] { (0f, 0f), (W, 0f), (0f, H), (W, H) };
            var units = c.TableState.Units.Objects.OfType<UnitData>().Where(u => OnTable(u) && u.Models.Any(m => m.GetIsAlive()
                && corners.Any(k => MathF.Sqrt((m.Position.x - k.Item1) * (m.Position.x - k.Item1) + (m.Position.z - k.Item2) * (m.Position.z - k.Item2)) <= 18f))).ToList();
            foreach (UnitData unit in units)
            {
                var options = new List<string>();
                if (c.TableState.Objectives.Objects.Any()) options.Add(FightTowardObjective);
                if (UnitPsychology.ClosestEnemy(c, unit) != null) options.Add(FightTowardEnemy);
                options.Add(FightStay);
                string pick = options.Count == 1 ? FightStay : await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(
                    new StringSelectionRequest(unit.PlayerID,
                        $"Get back in the Fight!: {unit.Name} is within 18\" of a table corner and may move up to 6\" now.",
                        options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Get back in the Fight!") { Recommended = options[0] });
                if (pick == FightStay) continue;
                (float ux, float uz) = QuestActions.Centre(unit);
                float tx, tz;
                if (pick == FightTowardObjective)
                {
                    IObjective o = c.TableState.Objectives.Objects.OrderBy(o => Dist(ux, uz, o.Position.x, o.Position.z)).First();
                    (tx, tz) = (o.Position.x, o.Position.z);
                }
                else
                {
                    IUnit e = UnitPsychology.ClosestEnemy(c, unit)!;
                    (tx, tz) = QuestActions.Centre(e);
                }
                float d = Dist(ux, uz, tx, tz);
                float go = MathF.Min(6f, MathF.Max(0f, d - 1.5f));
                if (go < 0.05f) continue;
                MoveBlock(c, unit, (tx - ux) / d * go, (tz - uz) / d * go, go);
                c.Log($"Get back in the Fight!: {unit.Name} moves {pick.ToLowerInvariant()}.");
            }
        }

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        /// <summary>Moves a whole unit one straight block, stopping at the table edge and at anything in the way.</summary>
        private static float MoveBlock(IGameContext c, UnitData unit, float mx, float mz, float inches)
        {
            var living = unit.Models.OfType<ModelData>().Where(m => m.GetIsAlive()).ToList();
            float t = 1f;
            foreach (ModelData m in living)
            {
                float r = m.BaseShape.CircumscribedRadiusInches;
                t = MathF.Min(t, QuestActions.Reach(m.Position.x, mx, r, W - r));
                t = MathF.Min(t, QuestActions.Reach(m.Position.z, mz, r, H - r));
            }
            t = MathF.Min(MathF.Max(0f, t), QuestActions.FreeTravel(c, unit, living, mx, mz, MathF.Max(0f, t), inches));
            foreach (ModelData m in living) m.SetPosition(new Position(m.Position.x + mx * t, m.Position.z + mz * t));
            return inches * t;
        }

        // ================================================================= end of round

        /// <summary>End of round: the markers move and shoot; this round's events end.</summary>
        public static async Task EventsRoundEnd(IGameContext c)
        {
            if (!EventsOn(c) || State(c) is not { } s) return;
            foreach (EventMarker m in s.Markers.ToList())
            {
                switch (m.Code)
                {
                    case 31:
                    case 33:
                        if (ClosestUnit(c, m.X, m.Z, losOnly: false) is { } near)
                            MoveMarkerToward(c, m, near, D6(c) + 6);
                        break;
                    case 34:
                    case 36:
                        await PlayerMovesMarker(c, m);
                        if (m.Code == 36 && ClosestUnit(c, m.X, m.Z, losOnly: true) is { } target)
                        {
                            c.Log($"Rebel Uprising: the rebels fire on {target.Name}.");
                            await CampaignEventRunner.DealHits(c, target, D2D3(c), EventName(36, c.Settings.GameSystem));
                        }
                        break;
                    case 32:
                    case 35:
                        if (ClosestUnit(c, m.X, m.Z, losOnly: true, within: m.Code == 35 ? 12f : float.MaxValue) is { } shot)
                        {
                            int wounds = D3(c);
                            c.Log($"{EventName(m.Code, c.Settings.GameSystem)}: {shot.Name} takes {wounds} wound{(wounds == 1 ? "" : "s")}.");
                            await CasualtyPresentation.ApplyUnitWounds(c, shot, wounds);
                        }
                        break;
                }
            }
            s.RoundEvent = 0;
            s.RoundEventSpent = false;
            s.RoundQuarter = null;
            foreach (IUnit u in c.TableState.Units.Objects)
                foreach (TokenType t in new[] { TokenType.ChippedWeapons, TokenType.FlashGrenades, TokenType.DoubleShots, TokenType.DeepRage })
                    Untag(u, t);
            Save(c, s);
        }

        private static IUnit? ClosestUnit(IGameContext c, float x, float z, bool losOnly, float within = float.MaxValue)
        {
            var terrain = c.TableState.Terrain.Objects.ToList();
            IUnit? best = null;
            float bestD = float.MaxValue;
            foreach (IUnit u in c.TableState.Units.Objects.Where(OnTable))
                foreach (IModel m in u.Models.Where(m => m.GetIsAlive()))
                {
                    float d = Dist(x, z, m.Position.x, m.Position.z) - m.BaseRadiusInches;
                    if (d >= bestD || d > within) continue;
                    if (losOnly && !LineOfSightUtilities.HasLineOfSight(new Position(x, z), m.Position, terrain)) continue;
                    bestD = d;
                    best = u;
                }
            return best;
        }

        private static void MoveMarkerToward(IGameContext c, EventMarker m, IUnit target, float inches)
        {
            IModel near = target.Models.Where(mm => mm.GetIsAlive()).OrderBy(mm => Dist(m.X, m.Z, mm.Position.x, mm.Position.z)).First();
            float d = Dist(m.X, m.Z, near.Position.x, near.Position.z);
            float go = MathF.Min(inches, MathF.Max(0f, d - near.BaseRadiusInches - 1f));
            if (d < 0.01f || go <= 0f) return;
            m.X = Math.Clamp(m.X + (near.Position.x - m.X) / d * go, 0f, W);
            m.Z = Math.Clamp(m.Z + (near.Position.z - m.Z) / d * go, 0f, H);
            c.Log($"{EventName(m.Code, c.Settings.GameSystem)}: the marker moves {go:0.#}\" toward {target.Name}.");
        }

        public const string MarkerTowardEnemy = "Toward your nearest enemy";
        public const string MarkerAway = "Away from your nearest unit";
        public const string MarkerStay = "Leave it";

        private static async Task PlayerMovesMarker(IGameContext c, EventMarker m)
        {
            if (RandomArmy(c) is not { } player) return;
            int inches = D6(c) + 6;
            IUnit? enemy = c.TableState.Units.Objects.Where(u => OnTable(u) && !u.PlayerID.Equals(player)
                    && TeamOf(c, u.PlayerID) != TeamOf(c, player))
                .OrderBy(u => u.Models.Where(mm => mm.GetIsAlive()).Min(mm => Dist(m.X, m.Z, mm.Position.x, mm.Position.z))).FirstOrDefault();
            IUnit? mine = c.TableState.Units.Objects.Where(u => OnTable(u) && u.PlayerID.Equals(player))
                .OrderBy(u => u.Models.Where(mm => mm.GetIsAlive()).Min(mm => Dist(m.X, m.Z, mm.Position.x, mm.Position.z))).FirstOrDefault();
            var options = new List<string>();
            if (enemy != null) options.Add(MarkerTowardEnemy);
            if (mine != null) options.Add(MarkerAway);
            options.Add(MarkerStay);
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                $"{EventName(m.Code, c.Settings.GameSystem)}: you may move the marker up to {inches}\".",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: EventName(m.Code, c.Settings.GameSystem)) { Recommended = options[0] });
            if (pick == MarkerTowardEnemy && enemy != null) MoveMarkerToward(c, m, enemy, inches);
            else if (pick == MarkerAway && mine != null)
            {
                IModel near = mine.Models.Where(mm => mm.GetIsAlive()).OrderBy(mm => Dist(m.X, m.Z, mm.Position.x, mm.Position.z)).First();
                float d = MathF.Max(0.01f, Dist(m.X, m.Z, near.Position.x, near.Position.z));
                m.X = Math.Clamp(m.X - (near.Position.x - m.X) / d * inches, 0f, W);
                m.Z = Math.Clamp(m.Z - (near.Position.z - m.Z) / d * inches, 0f, H);
                c.Log($"{EventName(m.Code, c.Settings.GameSystem)}: {c.GetPlayerName(player)} moves the marker away from {mine.Name}.");
            }
        }

        private static int TeamOf(IGameContext c, PlayerID p) =>
            c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(p))?.TeamNumber ?? -1;

        // ================================================================= markers on the table

        private static bool NearPoint(IUnit u, float x, float z, float inches = 3f) => u.Models.Any(m => m.GetIsAlive()
            && Dist(x, z, m.Position.x, m.Position.z) - m.BaseRadiusInches <= inches + 0.001f);

        /// <summary>A unit activates or ends a move: the Dangerous areas round the Zombie Horde (31) and Havoc Spawn (34).</summary>
        private static async Task MarkerDanger(IGameContext c, IUnit unit)
        {
            if (!EventsOn(c) || State(c) is not { } s) return;
            foreach (EventMarker m in s.Markers.Where(m => m.Code is 31 or 34))
                if (unit.GetIsAlive() && NearPoint(unit, m.X, m.Z))
                {
                    c.Log($"{EventName(m.Code, c.Settings.GameSystem)}: {unit.Name} is within 3\" of the marker - Dangerous terrain{(m.Code == 34 ? " (wounds on 1-2)" : "")}.");
                    var result = MovementExecutor.RollForcedDangerousTerrain(c, unit,
                        MovementRuleQueries.IgnoresAllTerrain(unit, c.RuleEvaluator), woundsOn: m.Code == 34 ? 2 : 1);
                    await MovementExecutor.ResolveDangerousTerrain(c, result);
                }
        }

        /// <summary>A unit activates: event markers' Dangerous areas.</summary>
        public static Task EventsOnActivation(IGameContext c, IUnit unit) => MarkerDanger(c, unit);

        /// <summary>After a move: the Anomaly (21), the Dangerous markers, Carnivorous Fauna (22), Rigged to Blow (43).</summary>
        public static async Task EventsAfterMove(IGameContext c, UnitData unit, IReadOnlyList<int> enteredPieces)
        {
            if (!EventsOn(c) || State(c) is not { } s) return;
            foreach (EventMarker m in s.Markers.Where(m => m.Code == 21))
                if (unit.GetIsAlive() && NearPoint(unit, m.X, m.Z))
                {
                    c.Log($"{EventName(21, c.Settings.GameSystem)}: {unit.Name} moved within 3\" of the anomaly.");
                    await CampaignEventRunner.DealHits(c, unit, D2D3(c), EventName(21, c.Settings.GameSystem));
                }
            await MarkerDanger(c, unit);
            // Aircraft ignore terrain: Carnivorous Fauna and a rigged piece only catch units that go into it.
            if (enteredPieces.Count == 0 || MovementRuleQueries.IgnoresAllTerrain(unit, c.RuleEvaluator)) return;
            var pieces = c.TableState.Terrain.Objects.ToList();
            if (s.RiggedPiece is int rigged && enteredPieces.Contains(rigged) && unit.GetIsAlive())
            {
                s.RiggedPiece = null;
                Save(c, s);
                await c.Announce($"Rigged to Blow: {Describe(pieces[rigged])} explodes under {unit.Name}!", Amber, EBannerTier.Notice);
                await CampaignEventRunner.DealHits(c, unit, D2D3(c), "Rigged to Blow");
            }
            if (s.Fauna)
                foreach (int i in enteredPieces)
                {
                    if (!unit.GetIsAlive()) break;
                    int die = D6(c);
                    c.Log($"Carnivorous Fauna: {unit.Name} enters {Describe(pieces[i])} - rolled {die}{(die == 1 ? ", a Dangerous Terrain Test" : "")}.");
                    if (die == 1)
                        await MovementExecutor.ResolveDangerousTerrain(c, MovementExecutor.RollForcedDangerousTerrain(c, unit,
                            MovementRuleQueries.IgnoresAllTerrain(unit, c.RuleEvaluator)));
                }
        }

        /// <summary>The Swarm (33): within 3" counts as Difficult terrain (a move starting there is capped at 6").</summary>
        public static bool InSwarm(IGameContext c, IUnit unit) =>
            EventsOn(c) && !MovementRuleQueries.IgnoresAllTerrain(unit, c.RuleEvaluator) && State(c) is { } s && s.Markers.Any(m => m.Code == 33 && NearPoint(unit, m.X, m.Z));

        // ================================================================= modifiers read where they act

        /// <summary>Heavy Fog -1 shooting, Fighting Vigour +1 shooting in the quarter, Flash Grenades -1 melee vs that army,
        /// Deep Rage +1 melee.</summary>
        public static IReadOnlyList<GrantedModifier> EventHitModifiers(IGameContext c, IUnit attacker, IUnit defender, bool melee)
        {
            var list = new List<GrantedModifier>();
            if (!EventsOn(c) || State(c) is not { } s) return list;
            EGameSystem sys = c.Settings.GameSystem;
            if (!melee && s.RoundEvent == 24) list.Add(new GrantedModifier(EventName(24, sys), -1));
            if (!melee && s.RoundEvent == 51 && s.RoundQuarter is { } q && InQuarter(attacker, q)) list.Add(new GrantedModifier(EventName(51, sys), 1));
            if (melee && defender.Tokens.HasToken(TokenType.FlashGrenades) && !attacker.PlayerID.Equals(defender.PlayerID))
                list.Add(new GrantedModifier(EventName(61, sys), -1));
            if (melee && attacker.Tokens.HasToken(TokenType.DeepRage)) list.Add(new GrantedModifier(EventName(66, sys), 1));
            return list;
        }

        /// <summary>Godly Blessing: "All units get AP(+1) in melee until the end of the round" - every melee Defense roll -1.</summary>
        public static int MeleeApBonus(IGameContext c) => EventsOn(c) && State(c)?.RoundEvent == 53 ? 1 : 0;

        /// <summary>Shimmering Mist: units in a misted quarter count as in Cover.</summary>
        public static bool InMist(IGameContext c, IUnit unit) =>
            EventsOn(c) && State(c) is { } s && s.MistQuarters.Any(q => InQuarter(unit, q));

        /// <summary>Gravitational Shift (+1"/+2" within 18" of the centre this round) and the Boots (+D3"/+2D3").</summary>
        public static (float Advance, float Rush) EventMoveChange(IGameContext c, IUnit unit)
        {
            float adv = unit.Tokens.GetTokenCount(TokenType.BootsAdvance), rush = unit.Tokens.GetTokenCount(TokenType.BootsRush);
            if (EventsOn(c) && State(c)?.RoundEvent == 52 && NearPoint(unit, W / 2f, H / 2f, 18f)) { adv += 1f; rush += 2f; }
            return (adv, rush);
        }

        /// <summary>Broken Morale -1, Covered in Blood -1 (an enemy of the marked unit within 6").</summary>
        public static int EventMoraleModifier(IGameContext c, IUnit unit)
        {
            int m = unit.Tokens.HasToken(TokenType.BrokenMorale) ? -1 : 0;
            if (c.TableState.Units.Objects.Any(b => b.Tokens.HasToken(TokenType.CoveredInBlood) && b.GetIsAlive()
                    && TeamOf(c, b.PlayerID) != TeamOf(c, unit.PlayerID)
                    && UnitCompareUtilities.MinDistanceBetweenUnits(b, unit, out _, out _, includeVertical: true) <= 6f + 0.001f))
                m -= 1;
            return m;
        }

        /// <summary>The End is Nigh (fail) / Land of the Brave (pass). Firefight and Skirmish word both "taken at the end of the
        /// round", and no Morale Test is taken then in these games, so there they never apply.</summary>
        public static bool? MoraleOverride(IGameContext c, IUnit unit)
        {
            if (!EventsOn(c) || State(c) is not { } s || Skirmish(c)) return null;
            if (s.RoundEvent == 25) return false;
            if (s.RoundEvent == 54 && s.RoundQuarter is { } q && InQuarter(unit, q)) return true;
            return null;
        }

        /// <summary>Panic Attack: "The first time during this round when a unit rolls a 1 when taking a Morale Test, that unit Routs."</summary>
        public static async Task<bool> PanicAttack(IGameContext c, IUnit unit, int face)
        {
            if (face != 1 || !EventsOn(c) || State(c) is not { RoundEvent: 44, RoundEventSpent: false } s) return false;
            s.RoundEventSpent = true;
            Save(c, s);
            c.Log($"Panic Attack: {unit.Name} rolls a 1 on its Morale Test - it Routs.");
            if (UnitPsychology.BindingOf(c, unit) is { } b) await MoraleUtilities.RoutWithPresentation(c, b);
            return true;
        }

        /// <summary>The hit-roll events: Grave Miscalculation (41), Berserker (42), Suppressive Fire (45), From Bad to Worse (46).
        /// Returns wounds the target takes on top (Berserker).</summary>
        public static async Task<int> AfterHitRoll(IGameContext c, IUnit attacker, IUnit defender, bool melee, int ones, int sixes)
        {
            if (!EventsOn(c) || State(c) is not { RoundEventSpent: false } s) return 0;
            EGameSystem sys = c.Settings.GameSystem;
            int code = s.RoundEvent;
            bool fires = code switch
            {
                41 => !melee && ones > 0,
                42 => melee && sixes > 0,
                45 => !melee && sixes > 0,
                46 => melee && ones > 0,
                _ => false,
            };
            if (!fires) return 0;
            s.RoundEventSpent = true;
            Save(c, s);
            switch (code)
            {
                case 41:
                {
                    int hits = 0;
                    for (int i = 0; i < ones; i++) hits += D3(c);
                    c.Log($"Grave Miscalculation: {attacker.Name} rolled {ones} result(s) of 1 - it takes {hits} hits.");
                    await CampaignEventRunner.DealHits(c, attacker, hits, EventName(41, sys));
                    return 0;
                }
                case 42:
                    c.Log($"Berserker: {attacker.Name} rolled {sixes} six(es) in melee - {defender.Name} takes {sixes} wound(s).");
                    return sixes;
                case 45:
                    c.Log($"Suppressive Fire: {sixes} six(es) - {defender.Name} must take a Morale Test at -{sixes}.");
                    if (defender.GetIsAlive() && UnitPsychology.BindingOf(c, defender) is { } b)
                    {
                        MoraleUtilities.MoraleTestOutcome test = await MoraleUtilities.TakeMoraleTest(c, defender,
                            Math.Clamp(defender.Quality + sixes, 2, 7), EMoraleTrigger.OtherSource, failureCausesShakenOrRout: true);
                        if (!test.Passed && !defender.Tokens.HasToken(TokenType.Shaken)) await MoraleUtilities.ApplyShakenWithPresentation(c, b);
                    }
                    return 0;
                case 46:
                    c.Log($"From Bad to Worse: {attacker.Name} rolled {ones} result(s) of 1 in melee - it counts +{ones} wound(s) for who won.");
                    Tag(attacker, TokenType.CritMeleeWounds, ones);
                    return 0;
            }
            return 0;
        }
    }
}
