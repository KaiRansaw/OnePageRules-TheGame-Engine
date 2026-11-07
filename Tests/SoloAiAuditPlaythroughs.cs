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
            internal LogTap(IPlayerController inner, List<string> lines) { _inner = inner; _lines = lines; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { lock (_lines) _lines.Add(logMessage); _inner.SendLogMessage(logMessage, color); }
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
            string[] armies = { armyA, armyB };
            for (int i = 0; i < 2; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), LoadArmy(armies[i]), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                // The official bot - the one the lobby seats - not CreateSoloRulesController's frozen baseline.
                var registry = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, aiGame.TableState,
                    slots[i].PlayerID, seed, slots[i].SlotID);
                IPlayerController ai = new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame, registry);
                slots[i].AssignPlayerController(i == 0 ? new LogTap(ai, log) : ai);
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            Ai.Solo.SoloAiBrain.DecisionTrace = (unit, plan) =>
            {
                lock (log) log.Add($"PLAN {unit.Name}: {plan.Kind} -> {plan.Target?.Name ?? "-"} ({plan.Why})");
            };
            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(240)));
            Ai.Solo.SoloAiBrain.DecisionTrace = null;
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            lock (log) return log.ToList();
        }

        /// <summary>Lines the books forbid for the official bot, with what each one breaks.</summary>
        internal static IEnumerable<string> Audit(IReadOnlyList<string> log)
        {
            var lastPlan = new Dictionary<string, string>();
            string? activating = null;
            foreach (string l in log)
            {
                if (l.StartsWith("PLAN ", StringComparison.Ordinal))
                {
                    int colon = l.IndexOf(": ", StringComparison.Ordinal);
                    lastPlan[l[5..colon]] = l[(colon + 2)..];
                    continue;
                }
                if (l.StartsWith("Activating: ", StringComparison.Ordinal)) activating = l["Activating: ".Length..].TrimEnd('.');

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
                    if (planned != fought)
                        yield return $"WRONG CHARGE TARGET: {activating} planned {planned}, fought {fought}";
                }
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

        [Test]
        public async Task SoloAiAudit_RealArmies()
        {
            var report = new List<string>();
            int games = 0;
            foreach (var (system, a, b) in (System.Environment.GetEnvironmentVariable("AUDIT_QUICK") != null ? Matchups.Take(1) : Matchups))
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
