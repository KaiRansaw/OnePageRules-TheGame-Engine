using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG
{
    /// <summary>
    /// Political Intrigue, part 2: the politicians (GF p49, AoF p49 - word for word the same). "Before the game begins,
    /// pick one of the following playstyles, or roll a D3 to randomly get one: 1. Open - Each player selects two
    /// politicians openly. 2. Open + Hidden - one openly, and one in secret. 3. Hidden - two in secret. When a player uses
    /// a directive, they may declare that it is being used by one of their politicians of that directive type that hasn't
    /// been used yet this round. Secret politicians are revealed when they are used for the first time."
    /// </summary>
    public static partial class PoliticalIntrigue
    {
        public const string PoliticiansOption = "Politicians";

        public const string Warmonger = "Warmonger", Ambassador = "Ambassador", Mastermind = "Mastermind", Deceiver = "Deceiver",
            Negotiator = "Negotiator", Trader = "Trader", Manipulator = "Manipulator", Warden = "Warden", Visionary = "Visionary",
            Escalator = "Escalator";

        public sealed record Politician(string Name, string TypeA, string TypeB, bool OncePerGame, string Text);

        public static readonly IReadOnlyList<Politician> PoliticianList = new Politician[]
        {
            new(Warmonger, Influence, Propaganda, true, "Once per game, when using this directive, the target enemy unit takes D3 wounds, or one enemy unit within 3\" of the chosen objective takes D3 wounds."),
            new(Ambassador, Influence, Recruitment, false, "When using this directive, the closest friendly unit to the target unit or objective may move 3\" directly towards it."),
            new(Mastermind, Influence, Industry, false, "When using this directive, the target friendly unit gets +1 to hit rolls until the end of the round, or friendly units get +1 to hit rolls when attacking enemies within 3\" of the chosen objective until the end of the round."),
            new(Deceiver, Influence, Mobilization, false, "When using this directive, the target friendly unit may ignore Difficult and Dangerous terrain until the end of the round, or the area within 3\" of the chosen objective counts as Difficult and Dangerous terrain until the end of the round."),
            new(Negotiator, Propaganda, Recruitment, true, "Once per game, when using this directive, the target friendly unit may restore D3 models/wounds, or the target enemy unit takes D3 wounds."),
            new(Trader, Propaganda, Industry, false, "When using this directive, the target friendly unit gets AP(+1) until the end of the round, or friendly units get AP(+1) when attacking the target enemy unit once (next time the effect would apply)."),
            new(Manipulator, Propaganda, Mobilization, true, "Once per game, when using this directive, you may move the target unit by up to 6\" in any direction."),
            new(Warden, Recruitment, Industry, false, "When using this directive, enemy units get AP(-1), to a min. of AP(0), when attacking the target friendly unit until the end of the round."),
            new(Visionary, Recruitment, Mobilization, false, "When using this directive, the target friendly unit stops being Shaken."),
            new(Escalator, Industry, Mobilization, true, "Once per game, when using this directive, the target friendly unit may move twice or shoot twice (even at different targets)."),
        };

        public static Politician PoliticianNamed(string name) => PoliticianList.First(p => p.Name == name);

        public static readonly string[] Playstyles = { "Open", "Open + Hidden", "Hidden" };

        public const string PoliticiansBookText =
            "Before the game begins, pick one of the following playstyles, or roll a D3 to randomly get one: 1. Open - Each player selects two politicians openly. "
            + "2. Open + Hidden - Each player selects one politician openly, and one politician in secret. 3. Hidden - Each player selects two politicians in secret.\n"
            + "When a player uses a directive, they may declare that it is being used by one of their politicians of that directive type that hasn't been used yet this round. "
            + "Secret politicians are revealed when they are used for the first time, but note that you may choose directives without using any politician, so as to keep them secret.";

        public const string PoliticiansGameText =
            "Turns on the directives too. Before deployment the first player picks the playstyle (or rolls), then each player picks two politicians "
            + "(bots: Mastermind and Warmonger). When you use a directive you're asked whether one of your politicians of that type uses it. "
            + "Secret politicians show only on your own Mission panel until you use them.";

        public static bool PoliticiansOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.Politicians);

        private static List<string> ListOf(Dictionary<string, List<string>> d, PlayerID p) =>
            d.TryGetValue(p.ToString(), out var l) ? l : d[p.ToString()] = new List<string>();

        // ---------------------------------------------------------------- before the game

        public const string RollPlaystyle = "Roll a D3";

        /// <summary>Before deployment: the playstyle (picked by the first player, or rolled), then each player's two
        /// politicians. Bots roll, and pick Mastermind and Warmonger.</summary>
        public static async Task Settle(IGameContext c)
        {
            if (!PoliticiansOn(c)) return;
            PoliticalIntrigueState s = State(c.GameDataStore) ?? new PoliticalIntrigueState();
            if (s.Playstyle > 0) return;
            var seats = c.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
            if (seats.Count == 0) return;
            var options = new List<string> { RollPlaystyle };
            options.AddRange(Playstyles);
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seats[0].PlayerID,
                "Political Intrigue: pick how politicians are chosen (Open: both seen; Open + Hidden: one each way; Hidden: both secret).",
                options, new List<StringSelectionRequest.InvalidOption>(), displayName: "Political Intrigue - playstyle") { Recommended = RollPlaystyle });
            int idx = Array.IndexOf(Playstyles, pick);
            s.Playstyle = idx >= 0 ? idx + 1 : (c.DiceRoller.RollDecisiveFace() + 1) / 2;
            c.Log($"Political Intrigue: politicians are picked {Playstyles[s.Playstyle - 1]}{(idx < 0 ? " (rolled)" : "")}.");

            foreach (var seat in seats)
            {
                var mine = ListOf(s.Politicians, seat.PlayerID);
                var hidden = ListOf(s.Hidden, seat.PlayerID);
                mine.Clear();
                hidden.Clear();
                string[] botPicks = { Mastermind, Warmonger };
                for (int n = 0; n < 2; n++)
                {
                    bool secret = s.Playstyle == 3 || (s.Playstyle == 2 && n == 1);
                    var left = PoliticianList.Where(p => !mine.Contains(p.Name)).ToList();
                    var labels = left.Select(p => $"{p.Name} ({p.TypeA}/{p.TypeB}){(p.OncePerGame ? " - once per game" : "")}: {p.Text}").ToList();
                    string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(seat.PlayerID,
                        $"Political Intrigue: pick your {(n == 0 ? "first" : "second")} politician{(secret ? " (in secret)" : " (openly)")}.",
                        labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Political Intrigue - politician")
                    { Recommended = labels[left.FindIndex(p => p.Name == botPicks[n])] });
                    int i = labels.IndexOf(answer);
                    string name = left[i >= 0 ? i : 0].Name;
                    mine.Add(name);
                    if (secret) hidden.Add(name);
                    c.Log(secret ? $"Political Intrigue: {seat.Name} picks a politician in secret."
                                 : $"Political Intrigue: {seat.Name} picks the {name}.");
                }
            }
            Save(c, s);
        }

        // ---------------------------------------------------------------- using one

        public const string NoPolitician = "No politician (keep them unused / secret)";

        /// <summary>The player's politicians that could use this directive now.</summary>
        public static List<string> Eligible(PoliticalIntrigueState s, PlayerID player, string directive) =>
            ListOf(s.Politicians, player).Where(n =>
            {
                Politician p = PoliticianNamed(n);
                return (p.TypeA == directive || p.TypeB == directive)
                    && !ListOf(s.PoliticiansUsed, player).Contains(n)
                    && !(p.OncePerGame && ListOf(s.Spent, player).Contains(n));
            }).ToList();

        /// <summary>"They may declare that it is being used by one of their politicians of that directive type". Bots
        /// declare whenever one of theirs fits.</summary>
        private static async Task<string?> Declare(IGameContext c, PoliticalIntrigueState s, PlayerID player, string directive,
            IUnit? targetUnit, IObjective? targetObjective)
        {
            if (!PoliticiansOn(c) || s.Playstyle == 0) return null;
            var eligible = Eligible(s, player, directive);
            if (eligible.Count == 0) return null;
            var options = new List<string> { NoPolitician };
            options.AddRange(eligible.Select(n => $"{n}{(ListOf(s.Hidden, player).Contains(n) ? " (secret - reveals it)" : "")}: {PoliticianNamed(n).Text}"));
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(player,
                $"{directive}: is one of your politicians using it?", options, new List<StringSelectionRequest.InvalidOption>(),
                displayName: "Political Intrigue - politician") { Recommended = options[1] });
            int idx = options.IndexOf(answer);
            if (idx <= 0) return null;
            string name = eligible[idx - 1];
            ListOf(s.PoliticiansUsed, player).Add(name);
            if (PoliticianNamed(name).OncePerGame) ListOf(s.Spent, player).Add(name);
            if (ListOf(s.Hidden, player).Remove(name))
                c.Log($"Political Intrigue: {c.GetPlayerName(player)} reveals the {name}.");
            return name;
        }

        private static int D3(IGameContext c) => (c.DiceRoller.RollDecisiveFace() + 1) / 2;

        private static void GrantForTheRound(IUnit unit, string rule) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.RoundEnd(),
                Payload: new TokenPayload.RuleGrant(rule, ELifetime.ThisRound)));

        public const string DeceiverTerrainName = "Political Intrigue: Deceiver";

        private static async Task PoliticianEffect(IGameContext c, PoliticalIntrigueState s, PlayerID player, string? politician,
            string directive, IUnit? unit, IObjective? objective)
        {
            if (politician == null) return;
            string me = c.GetPlayerName(player);
            switch (politician)
            {
                case Warmonger:
                {
                    IUnit? victim = unit;
                    if (objective != null)
                    {
                        var near = Enemies(c, player).Where(e => WithinOf(e, objective.Position, 3f)).ToList();
                        victim = await PickUnit(c, player, near, "Warmonger: pick an enemy unit within 3\" of the objective - it takes D3 wounds.",
                            near.OrderByDescending(Cost).FirstOrDefault());
                    }
                    if (victim == null) { c.Log("Warmonger: no enemy unit within 3\" of the objective."); break; }
                    int d3 = D3(c);
                    c.Log($"Warmonger: {victim.Name} takes {d3} wound{(d3 == 1 ? "" : "s")}.");
                    await CasualtyPresentation.ApplyUnitWounds(c, victim, d3);
                    break;
                }
                case Negotiator:
                    if (directive == Recruitment && unit != null) await new GameOperationServices(c).RestoreD3(unit, Negotiator);
                    else if (unit != null)
                    {
                        int d3 = D3(c);
                        c.Log($"Negotiator: {unit.Name} takes {d3} wound{(d3 == 1 ? "" : "s")}.");
                        await CasualtyPresentation.ApplyUnitWounds(c, unit, d3);
                    }
                    break;
                case Ambassador:
                {
                    if (unit == null && objective == null) break;
                    (float tx, float tz) = unit != null ? QuestActions.Centre(unit) : (objective!.Position.x, objective.Position.z);
                    IUnit? closest = Mine(c, player).Where(u => !ReferenceEquals(u, unit))
                        .OrderBy(u => { var (x, z) = QuestActions.Centre(u); return Dist(x, z, tx, tz); }).FirstOrDefault();
                    if (closest is not UnitData mover) break;
                    bool go = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player,
                        $"Ambassador: move {mover.Name} up to 3\" directly towards {(unit != null ? unit.Name : "the objective")}?",
                        defaultAnswer: true, displayName: "Political Intrigue - Ambassador"));
                    if (!go) break;
                    (float ux, float uz) = QuestActions.Centre(mover);
                    float d = Dist(ux, uz, tx, tz);
                    float step = MathF.Min(3f, MathF.Max(0f, unit != null ? d - 1.5f : d));
                    if (step < 0.05f || d < 0.01f) break;
                    float moved = BattlefieldEffects.MoveBlock(c, mover, (tx - ux) / d * step, (tz - uz) / d * step, step);
                    c.Log($"Ambassador: {mover.Name} moves {moved:0.0}\" towards it.");
                    break;
                }
                case Mastermind:
                    if (objective != null)
                    {
                        s.MastermindAreas.Add($"{player.ID}|{objective.Position.x:0.00}|{objective.Position.z:0.00}");
                        c.Log("Mastermind: friendly units get +1 to hit against enemies within 3\" of that objective this round.");
                    }
                    else if (unit != null)
                    {
                        unit.Tokens.AddToken(new Token(RollModifierTokens.TypeFor(ERollKind.Hit), 1, new TokenClearTrigger.RoundEnd(),
                            Payload: new TokenPayload.StatModifier(1, Mastermind)));
                        c.Log($"Mastermind: {unit.Name} gets +1 to hit this round.");
                    }
                    break;
                case Deceiver:
                    if (objective != null)
                    {
                        c.GameDataStore.Create(new TerrainData(ETerrainType.Difficult | ETerrainType.Dangerous,
                            new CircularZone(new Float2(objective.Position.x, objective.Position.z), 3f), heightInches: 0f, name: DeceiverTerrainName));
                        c.Log("Deceiver: the area within 3\" of the objective is Difficult and Dangerous terrain this round.");
                    }
                    else if (unit != null)
                    {
                        GrantForTheRound(unit, "Strider");
                        GrantForTheRound(unit, CampaignEventRuleCatalog.DeceiverName);
                        c.Log($"Deceiver: {unit.Name} ignores Difficult and Dangerous terrain this round.");
                    }
                    break;
                case Trader:
                    if (directive == Industry && unit != null)
                    {
                        GrantForTheRound(unit, CampaignEventRuleCatalog.TraderName);
                        c.Log($"Trader: {unit.Name} gets AP(+1) this round.");
                    }
                    else if (unit != null)
                    {
                        unit.Tokens.AddToken(new Token(TokenType.Mark, 1, new TokenClearTrigger.ManualOnly(),
                            Payload: new TokenPayload.RuleGrant(CampaignEventRuleCatalog.TraderName, ELifetime.ThisAttack)));
                        c.Log($"Trader: the next attack on {unit.Name} gets AP(+1).");
                    }
                    break;
                case Manipulator:
                    if (unit != null)
                    {
                        c.Log($"Manipulator: {me} may move {unit.Name} up to 6\".");
                        await new GameOperationServices(c).MoveUnit(unit, MobilizationInches, isOptional: true, controller: player);
                    }
                    break;
                case Warden:
                    if (unit != null)
                    {
                        GrantForTheRound(unit, CampaignEventRuleCatalog.WardenName);
                        c.Log($"Warden: enemies get AP(-1) attacking {unit.Name} this round.");
                    }
                    break;
                case Visionary:
                    if (unit != null && unit.Tokens.HasToken(TokenType.Shaken))
                    {
                        unit.Tokens.RemoveTokens(TokenType.Shaken, unit.Tokens.GetTokenCount(TokenType.Shaken));
                        c.Log($"Visionary: {unit.Name} stops being Shaken.");
                    }
                    break;
                case Escalator:
                    if (directive == Industry && unit != null)
                    {
                        s.SecondShot = unit.ID.ID.ToString();
                        c.Log($"Escalator: {unit.Name} shoots twice.");
                    }
                    else if (unit != null)
                    {
                        c.Log($"Escalator: {unit.Name} moves twice.");
                        await new GameOperationServices(c).MoveUnit(unit, MobilizationInches, isOptional: true);
                    }
                    break;
            }
            await c.Announce($"{me}: the {politician}", BattlefieldEffects.Amber, EBannerTier.Toast);
        }

        private static float Dist(float ax, float az, float bx, float bz) => MathF.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        private static bool WithinOf(IUnit u, Position p, float inches) =>
            u.Models.Any(m => m.GetIsAlive() && BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing, p) <= inches + 0.01f);

        // ---------------------------------------------------------------- during the round

        /// <summary>Mastermind on Influence: +1 to hit against enemies within 3" of the objective, this round.</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext c, IUnit attacker, IUnit defender, bool spell)
        {
            if (spell || !PoliticiansOn(c) || State(c.GameDataStore) is not { MastermindAreas.Count: > 0 } s) return Array.Empty<GrantedModifier>();
            int round = GameProgressUtilities.TryGetProgress(c.GameDataStore)?.RoundCount ?? 1;
            if (s.Round != round) return Array.Empty<GrantedModifier>();
            foreach (string area in s.MastermindAreas)
            {
                string[] parts = area.Split('|');
                if (parts.Length != 3 || !SameSide(c, attacker.PlayerID, new PlayerID(Guid.Parse(parts[0])))) continue;
                var at = new Position(float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                if (WithinOf(defender, at, 3f)) return new[] { new GrantedModifier(Mastermind, 1) };
            }
            return Array.Empty<GrantedModifier>();
        }

        /// <summary>Deceiver on Mobilization: the unit ignores Dangerous terrain this round (Strider covers Difficult).</summary>
        public static bool IgnoresDangerous(IUnit unit) =>
            unit.Tokens.GetAllTokens(TokenType.RuleGrant).Any(t => t.Payload is TokenPayload.RuleGrant g && g.RuleName == CampaignEventRuleCatalog.DeceiverName);

        /// <summary>Escalator on Industry: the second shooting - the player picks its target (any enemy within 12").</summary>
        public static async Task<UnitID?> TakeSecondShot(IGameContext c, IUnit shooter)
        {
            if (State(c.GameDataStore) is not { SecondShot: { } id } s || id != shooter.ID.ID.ToString()) return null;
            s.SecondShot = null;
            Save(c, s);
            var targets = IndustryTargets(c, shooter);
            if (targets.Count == 0) return null;
            IUnit? pick = await PickUnit(c, shooter.PlayerID, targets, $"Escalator: pick what {shooter.Name} shoots at the second time.",
                targets.OrderBy(t => UnitCompareUtilities.MinDistanceBetweenUnits(shooter, t, out _, out _, includeVertical: true)).FirstOrDefault());
            return pick?.ID;
        }

        /// <summary>End of round: Deceiver's areas go.</summary>
        public static void RoundEnd(IGameContext c)
        {
            if (!PoliticiansOn(c)) return;
            foreach (DataReference r in c.GameDataStore.GetAllDataReferences<TerrainData>().ToList())
                if (c.GameDataStore.IsValid(r, out _) && c.GameDataStore.GetValue<TerrainData>(r).Name == DeceiverTerrainName)
                    c.GameDataStore.Destroy(r);
        }

        /// <summary>The politicians a viewer may see: open ones, revealed ones, and their own secret ones.</summary>
        public static IEnumerable<string> VisiblePoliticians(PoliticalIntrigueState s, PlayerID owner, IReadOnlyCollection<PlayerID>? viewers)
        {
            bool own = viewers == null || viewers.Contains(owner);
            var hidden = s.Hidden.GetValueOrDefault(owner.ToString()) ?? new List<string>();
            foreach (string n in s.Politicians.GetValueOrDefault(owner.ToString()) ?? new List<string>())
            {
                if (!hidden.Contains(n)) yield return n;
                else if (own) yield return n + " (secret)";
                else yield return "a secret politician";
            }
        }
    }
}
