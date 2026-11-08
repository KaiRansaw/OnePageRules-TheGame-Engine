using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The official bot (Solo &amp; Co-Op Rules v3.6.0) played against itself with REAL army files, so the
    /// clause checks see real special rules, Tough heroes, casters and mixed weapons rather than a synthetic
    /// list. Kai, 2026-11-07: "check the bot follows the books". The log of every game is the evidence;
    /// <see cref="Audit"/> is the list of things the books forbid, each checked against every line.
    ///
    /// Not part of the normal run (a set of real games takes minutes): <c>--filter SoloAiAudit</c>.
    /// </summary>
    [TestFixture, Explicit("Plays real games - run on demand with --filter SoloAiAudit")]
    public class SoloAiAuditPlaythroughs
    {
        private sealed class LogTap : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string> _lines;
            private readonly Action<string>? _hook;
            internal LogTap(IPlayerController inner, List<string> lines, Action<string>? hook = null) { _inner = inner; _lines = lines; _hook = hook; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { _hook?.Invoke(logMessage); lock (_lines) _lines.Add(logMessage); _inner.SendLogMessage(logMessage, color); }
            public void SendPlayerMessage(string n, EChatMessageType t, string m) => _inner.SendPlayerMessage(n, t, m);
        }

        internal static ArmyListFile LoadArmy(string relative)
        {
            string root = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 8 && !Directory.Exists(Path.Combine(root, "armies", "AoF")); i++)
                root = Path.GetDirectoryName(root)!;
            string path = Path.Combine(root, "armies", relative);
            return JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)
                   ?? throw new InvalidOperationException(path);
        }

        internal static async Task<List<string>> Play(int seed, EGameSystem system, string armyA, string armyB)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();
            var slots = new PlayerSlot[2];
            var table = new TableState(store);
            Action<string> hook = line => SetupAndOrderChecks(table, line, log);
            string[] armies = { armyA, armyB };
            for (int i = 0; i < 2; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), LoadArmy(armies[i]), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                // The official bot - the one the lobby seats - not CreateSoloRulesController's frozen baseline.
                var registry = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, aiGame.TableState,
                    slots[i].PlayerID, seed, slots[i].SlotID);
                IPlayerController ai = new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame, registry);
                slots[i].AssignPlayerController(i == 0 ? new LogTap(ai, log, hook) : ai);
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            // The book's objective clause only runs when players place the markers; the lobby default is
            // the engine's auto-placement. AUDIT_PLACED=1 plays the placed version.
            if (System.Environment.GetEnvironmentVariable("AUDIT_PLACED") != null)
                settings.ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced;
            Ai.Solo.SoloCoOpObjectiveResolver.PlacementTrace = what => { lock (log) log.Add("OBJECTIVE " + what); };
            Ai.Solo.SoloAiBrain.DecisionTrace = (unit, plan) =>
            {
                lock (log) log.Add($"PLAN {unit.Name}: {plan.Kind} -> {plan.Target?.Name ?? "-"} ({plan.Why})");
            };
            Ai.Solo.SoloCoOpRangedAttackResolver.DecisionTrace = (shooter, weapon, target, options) =>
            {
                string opts = string.Join("; ", options.Select(o =>
                    $"{o.Weapon.Name}>{o.Target.Name}#{(o.Target.Models.FirstOrDefault()?.ID.GetHashCode() ?? 0) % 1000}[{(o.Cover ? "cover" : "open")},{(Ai.Solo.SoloAiTargeting.HasActivated(o.Target) ? "act" : "fresh")},{o.Distance:0.0},{o.Target.Models.Count(m => m.GetIsAlive())}m]"));
                lock (log) log.Add($"SHOT {shooter.Name}: {weapon.Name} -> {target.Name}#{(target.Models.FirstOrDefault()?.ID.GetHashCode() ?? 0) % 1000} | {opts}");
            };
            Ai.Resolvers.AiDefineMovementResolver.ChargeTrace = line => { lock (log) log.Add(line); };
            var deploySections = new Dictionary<PlayerID, HashSet<int>>();
            var deployCount = new Dictionary<PlayerID, int>();
            Ai.Solo.SoloCoOpPlaceObjectsResolver<ModelData>.DeployTrace = (player, section, aim) =>
            {
                lock (log)
                {
                    if (!deploySections.TryGetValue(player, out var set)) deploySections[player] = set = new HashSet<int>();
                    set.Add(section);
                    deployCount[player] = deployCount.GetValueOrDefault(player) + 1;
                    log.Add($"DEPLOY section {section} aim {aim.x:0},{aim.z:0}");
                }
            };
            Ai.Solo.SoloCoOpActionResolver.ClauseTrace = (unit, what) => { lock (log) log.Add($"CLAUSE {unit.Name}: {what}"); };
            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(600)));
            Ai.Solo.SoloAiBrain.DecisionTrace = null;
            Ai.Solo.SoloCoOpRangedAttackResolver.DecisionTrace = null;
            Ai.Resolvers.AiDefineMovementResolver.ChargeTrace = null;
            Ai.Solo.SoloCoOpActionResolver.ClauseTrace = null;
            Ai.Solo.SoloCoOpObjectiveResolver.PlacementTrace = null;
            Ai.Solo.SoloCoOpPlaceObjectsResolver<ModelData>.DeployTrace = null;
            lock (log)
                foreach (var (player, set) in deploySections)
                    if (set.Count < 2 && deployCount[player] >= 3) log.Add($"CHECK ALL GROUPS IN ONE SECTION ({set.Single()})");
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            lock (log) return log.ToList();
        }

        /// <summary>
        /// The book's set-up and activation clauses, checked against the table itself as the game runs:
        /// "deploy ... outside of Difficult and Dangerous terrain (unless the unit has either Strider or
        /// Flying)", "re-rolling if all 3 groups would deploy in the same section", and "Shaken AI units only
        /// activate after all non-shaken AI units have activated". Findings go in the log as CHECK lines.
        /// </summary>
        private static void SetupAndOrderChecks(ITableState table, string line, List<string> log)
        {
            void Report(string what) { lock (log) log.Add("CHECK " + what); }
            try
            {
                if (line == "Round 1")
                {
                    foreach (IGrouping<PlayerID, IUnit> side in table.Units.Objects
                                 .Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()).GroupBy(u => u.PlayerID))
                    {
                        var sections = side.Select(u => Ai.Solo.SoloAiActivationOrder.SectionOf(
                            u.Models.First(m => m.GetIsAlive()).Position,
                            GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES))
                            .Distinct().Count();
                        lock (log) log.Add($"SETUP {side.Count()} units in {sections} section(s): " + string.Join(", ", side.Select(u =>
                            $"{u.Name}@{u.Models.First(m => m.GetIsAlive()).Position.x:0},{u.Models.First(m => m.GetIsAlive()).Position.z:0}")));

                        foreach (IUnit u in side)
                        {
                            bool ignores = u.RuleDefinitions.Concat(u.Models.SelectMany(m => m.RuleDefinitions))
                                .Any(r => r.Definition == Rules.Dispatch.CoreRuleCatalog.Strider
                                          || r.Definition == Rules.Dispatch.CoreRuleCatalog.Flying);
                            if (ignores) continue;
                            int inBad = u.Models.Count(m => m.GetIsAlive() && table.Terrain.Objects.Any(t =>
                                (t.TerrainType.HasFlag(ETerrainType.Difficult) || t.TerrainType.HasFlag(ETerrainType.Dangerous))
                                && t.Shape.IsPointWithinZone(new Float2(m.Position.x, m.Position.z))));
                            if (inBad > 0) Report($"DEPLOYED IN BAD GROUND: {u.Name} ({inBad} model(s))");
                        }
                    }
                }

                if (line.StartsWith("Activating: ", StringComparison.Ordinal))
                {
                    string name = line["Activating: ".Length..].TrimEnd('.');
                    // Logged just before the engine records the choice, so find it among the units still to go.
                    var named = table.Progress.UnactivatedUnits.Where(u => u.Name == name).ToList();
                    if (named.Count == 0 || named.Any(u => !u.Tokens.HasToken(Rules.Foundation.TokenType.Shaken))) return;
                    IUnit unit = named[0];
                    IUnit? waiting = table.Progress.UnactivatedUnits
                        .Where(u => u.PlayerID == unit.PlayerID && u.GetIsAlive() && u.GetIsOnBattlefield() && u != unit)
                        .FirstOrDefault(u => !u.Tokens.HasToken(Rules.Foundation.TokenType.Shaken));
                    if (waiting != null) Report($"SHAKEN FIRST: {name} activated before {waiting.Name}");
                    else lock (log) log.Add($"ORDER Shaken {name} activated last, as the book says");
                }
            }
            catch (Exception ex) { Report("check threw: " + ex.Message); }
        }

        /// <summary>Lines the books forbid for the official bot, with what each one breaks.</summary>
        internal static IEnumerable<string> Audit(IReadOnlyList<string> log)
        {
            var lastPlan = new Dictionary<string, string>();
            string? activating = null;
            bool chargeMoved = false;
            foreach (string l in log)
            {
                if (l.StartsWith("PLAN ", StringComparison.Ordinal))
                {
                    // The plan the activation STARTED with: the tree is asked again after the move (and then
                    // sees the unit already in contact), so a later line is a re-read, not the decision.
                    // Before the charge move, though, a new CHARGE plan is a real re-decision - a spell cast
                    // just before charging can kill the planned target, and the tree then picks the next one.
                    int colon = l.IndexOf(": ", StringComparison.Ordinal);
                    string who = l[5..colon];
                    string planText = l[(colon + 2)..];
                    if (!chargeMoved && who == activating && planText.StartsWith("Charge -> ", StringComparison.Ordinal)
                        && lastPlan.TryGetValue(who, out string? first) && first.StartsWith("Charge -> ", StringComparison.Ordinal))
                        lastPlan[who] = planText;
                    else lastPlan.TryAdd(who, planText);
                    continue;
                }
                if (l.StartsWith("CHARGE ", StringComparison.Ordinal)) chargeMoved = true;
                if (l.StartsWith("Activating: ", StringComparison.Ordinal))
                {
                    activating = l["Activating: ".Length..].TrimEnd('.');
                    lastPlan.Remove(activating);
                    chargeMoved = false;
                }

                // A move the tree meant as an Advance (to shoot after) that ran past it: the shot is lost.
                if (l.Contains("[Shoot: Moved ") && l.Contains("when max to move and shoot")
                    && activating != null && lastPlan.TryGetValue(activating, out string? p)
                    && (p.StartsWith("AdvanceAndShoot") || p.StartsWith("AdvanceToObjective")))
                    yield return $"OVER-ADVANCE (plan: {p}): " + l;

                // The tree chose whom to charge; the melee that follows must be against that unit.
                if (l.StartsWith("Chose ", StringComparison.Ordinal) && l.EndsWith(" as defender.", StringComparison.Ordinal)
                    && activating != null && lastPlan.TryGetValue(activating, out string? cp) && cp.StartsWith("Charge -> "))
                {
                    string planned = cp["Charge -> ".Length..cp.IndexOf(" (", StringComparison.Ordinal)];
                    string fought = l["Chose ".Length..^" as defender.".Length];
                    if (planned != fought && fought != planned + " (Combined)")
                        yield return $"WRONG CHARGE TARGET: {activating} planned {planned}, fought {fought}";
                }
                if (l.StartsWith("CHECK ", StringComparison.Ordinal)) yield return l["CHECK ".Length..];
                if (l.StartsWith("OBJECTIVE FELL BACK", StringComparison.Ordinal)) yield return l;
                if (l.Contains("Held fire with every weapon") || l.Contains("Holding fire with"))
                    yield return "HELD FIRE (bots never decline a shot): " + l;
                if (l.Contains("declined to strike back") || l.Contains("does not strike back"))
                    yield return "NO STRIKE BACK: " + l;
            }
        }

        private static readonly (EGameSystem System, string A, string B)[] Matchups =
        {
            (EGameSystem.AgeOfFantasy, "AoF/1k - Duchies of Vinci.fdgarmy", "AoF/1k - Human Empire.fdgarmy"),
            (EGameSystem.AgeOfFantasy, "AoF/1k - Ogres.fdgarmy", "AoF/1k - Ossified Undead.fdgarmy"),
            (EGameSystem.GrimdarkFuture, "GDF/1k - Alien Hives.fdgarmy", "GDF/1k - Human Defense Force.fdgarmy"),
            (EGameSystem.GrimdarkFuture, "GDF/1k - Blood Brothers.fdgarmy", "GDF/1k - Prime Brothers.fdgarmy"),
        };

        /// <summary>
        /// The special-rule clauses need armies that carry them: Indirect, Relentless, Artillery, casters,
        /// transports, Ambush and Scout. Picked on demand with AUDIT_SPECIAL=1 (2k lists, slower games).
        /// </summary>
        private static readonly (EGameSystem System, string A, string B)[] SpecialMatchups =
        {
            (EGameSystem.GrimdarkFuture, "GDF/2k - Battle Brothers - Elite Shooting.fdgarmy", "GDF/2k - High Elf Fleets - Caster-Heavy.fdgarmy"),
            (EGameSystem.GrimdarkFuture, "GDF/2k - Dwarf Guilds - Ambush and Scout-Heavy.fdgarmy", "GDF/2k - Robot Legions - Mixed.fdgarmy"),
        };

        [Test]
        public async Task SoloAiAudit_RealArmies()
        {
            var report = new List<string>();
            int games = 0;
            foreach (var (system, a, b) in (System.Environment.GetEnvironmentVariable("AUDIT_SPECIAL") != null ? SpecialMatchups : System.Environment.GetEnvironmentVariable("AUDIT_QUICK") != null ? Matchups.Take(1) : Matchups))
                foreach (int seed in (System.Environment.GetEnvironmentVariable("AUDIT_SEEDS") is { Length: > 0 } list ? list.Split(',').Select(int.Parse).ToArray() : System.Environment.GetEnvironmentVariable("AUDIT_QUICK") != null ? new[] { 7001 } : new[] { 7001, 7002 }))
                {
                    List<string> log = await Play(seed, system, a, b);
                    games++;
                    string tag = $"[{system} {Path.GetFileNameWithoutExtension(a)} v {Path.GetFileNameWithoutExtension(b)} seed {seed}]";
                    File.WriteAllLines(Path.Combine(Path.GetTempPath(), $"soloaudit-{system}-{seed}-{games}.log"), log);
                    report.AddRange(Audit(log).Select(x => tag + " " + x));
                }
            TestContext.Out.WriteLine($"{games} games, {report.Count} findings");
            foreach (string r in report) TestContext.Out.WriteLine(r);
            Assert.That(report, Is.Empty);
        }
    }
}
