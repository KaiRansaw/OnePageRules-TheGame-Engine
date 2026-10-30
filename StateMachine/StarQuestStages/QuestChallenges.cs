using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>GF: Star Quest optional challenges (Horde Mode v2.0.0 / Beginner's Guide v2.0.1).</summary>
    public static class QuestChallenges
    {
        /// <summary>Villain: AI units within 12" of a Villain that isn't Shaken may use its Quality for
        /// morale tests if it's better than their own.</summary>
        public static int VillainMorale(IGameContext context, IUnit unit, int rollNeeded)
        {
            if (unit is UnitData { IsQuestHero: true }) return rollNeeded;
            int best = rollNeeded;
            foreach (UnitData villain in QuestActions.Units(context))
            {
                if (ReferenceEquals(villain, unit) || villain.IsQuestHero || !villain.GetIsAlive() || !villain.GetIsOnBattlefield()) continue;
                if (!QuestActions.AreFriends(context, villain, unit)) continue;
                if (villain.Tokens.GetTokenCount(TokenType.Shaken) > 0) continue;
                if (!HasRule(villain, "Villain")) continue;
                if (QuestActions.Distance(villain, unit) > 12f) continue;
                if (villain.Quality < best)
                {
                    best = villain.Quality;
                    context.Log($"{unit.Name} tests morale on {villain.Name}'s Quality ({best}+) - Villain.");
                }
            }
            return best;
        }

        // ── AI Ambush ────────────────────────────────────────────────────────────────────────────────────

        private static string Id(IUnit u) => u.ID.ID.ToString();

        public static bool IsWaitingToAmbush(IGameContext context, IUnit unit) =>
            GameProgressUtilities.GetQuestProgress(context.GameDataStore).Ambushers.Any(a => a.StartsWith(Id(unit) + ":"));

        /// <summary>"Ambush [AI Units]: only deploys D3 rounds after its wave is deployed." True if the unit was
        /// held back (it stays off the table until <see cref="DeployAmbushers"/>).</summary>
        public static bool HoldForAmbush(IGameContext context, UnitData unit)
        {
            if (!HasRule(unit, "Ambush")) return false;
            QuestProgress q = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            int d = (HordePlacement.RollD6(context) + 1) / 2;
            q.Ambushers.Add($"{Id(unit)}:{q.RoundsDone + d}");
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, q);
            HordePlacement.Revive(unit);
            ReserveRules.PlaceInReserve(unit);
            context.Log($"Star Quest: {unit.Name} has Ambush - it arrives in {d} round{(d == 1 ? "" : "s")}, near the AI Goal.");
            return true;
        }

        /// <summary>Start of a round: Ambush units that are due arrive within 6" of the AI Goal (a random
        /// objective when there are several).</summary>
        public static async Task DeployAmbushers(IGameContext context)
        {
            QuestProgress q = GameProgressUtilities.GetQuestProgress(context.GameDataStore);
            var due = q.Ambushers.Where(a => int.TryParse(a.Split(':')[1], out int r) && q.RoundsDone >= r).ToList();
            if (due.Count == 0) return;
            q.Ambushers.RemoveAll(due.Contains);
            GameProgressUtilities.SetQuestProgress(context.GameDataStore, q);
            foreach (string entry in due)
            {
                string id = entry.Split(':')[0];
                UnitData? unit = HordePlacement.AllUnits(context).FirstOrDefault(u => Id(u) == id);
                if (unit == null) continue;
                Position goal = QuestMission.AiGoal(context.TableState) ?? RandomObjective(context) ?? StarQuestMissionRules.Centre;
                await context.Announce($"Ambush! {unit.Name} appears", new TextColor(255, 120, 80, 255), EBannerTier.Toast);
                HordePlacement.Revive(unit);
                await HordePlacement.PlaceUnitNear(context, unit, r => new CircularZone(goal.x, goal.z, r),
                    HordePlacement.WaveTaskName, $"Ambush: {unit.Name}", radii: StarQuestMissionRules.EnemyRadii);
            }
        }

        private static Position? RandomObjective(IGameContext context)
        {
            var objectives = context.TableState.Objectives.Objects.ToList();
            if (objectives.Count == 0) return null;
            return objectives[(HordePlacement.RollD6(context) - 1) % objectives.Count].Position;
        }

        /// <summary>The unit (or one of its models) has a rule by this name.</summary>
        public static bool HasRule(IUnit unit, string ruleName)
        {
            bool Matches(IEnumerable<Rules.Dispatch.ResolvedRule> rules) => rules.Any(r =>
                string.Equals(r.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(r.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase));
            return Matches(unit.RuleDefinitions) || unit.Models.Any(m => Matches(m.RuleDefinitions));
        }


        // ── Choosing the challenges ──────────────────────────────────────────────────────────────────────

        public const string BossFights = "Boss Fights";
        public const string HeroInjuries = "Hero Injuries";
        public const string IncreasedAlertness = "Increased Alertness";
        public const string TimeOfDayName = "Time of Day";
        public const string WeatherName = "Weather";
        public const string RandomEvents = "Random Events";
        private const string StartOption = "Start";

        /// <summary>The tick in front of a challenge that is switched on. ASCII only - the font has no box glyphs.</summary>
        private const string OnMark = "[x] ";
        private const string OffMark = "[ ] ";

        private static QuestProgress Q(IGameContext c) => GameProgressUtilities.GetQuestProgress(c.GameDataStore);
        private static void Save(IGameContext c, QuestProgress q) => GameProgressUtilities.SetQuestProgress(c.GameDataStore, q);
        public static bool IsOn(IGameContext c, string challenge) => Q(c).Challenges.Contains(challenge);

        private static string Describe(string challenge, bool horde) => challenge switch
        {
            BossFights => horde
                ? "At the start of waves 4, 7 and 10 you may trigger a boss fight: a double-size wave. Destroy all of it for double XP that wave."
                : "After all objectives are done you may trigger a boss fight: one double-size wave, then no more reinforcements. Destroy it all for bonus XP.",
            HeroInjuries => "A hero unconscious at the end of a wave (with an objective held) may come back with an injury that lasts until the end of the next wave.",
            IncreasedAlertness => "Full Assault / Alarmed: alertness may rise by an extra 1 at the end of each round.",
            TimeOfDayName => "Dusk, Night (-1 to hit when shooting), Dawn (-1 to hit when shooting) or Day; it moves on with each new wave.",
            WeatherName => "Worsening, Bad (half movement, -1 to hit in melee), Improving (-1 to hit in melee) or Good; it moves on with each new wave.",
            RandomEvents => "Roll on the 36 random events whenever a new wave arrives (missions: also at the start).",
            _ => "",
        };

        /// <summary>Set-up: the heroes' player switches optional challenges on, then picks (or rolls) the
        /// alertness, time of day and weather. A bot party plays without challenges.</summary>
        /// <summary>Tests only: play with these challenges (rolling for alertness / time / weather) instead of asking.</summary>
        public static List<string>? ForceForTests;

        public static async Task ChooseChallenges(IGameContext context, List<UnitData> heroes, bool horde)
        {
            if (heroes.Count == 0) return;
            if (ForceForTests != null)
            {
                QuestProgress fq = Q(context);
                fq.Challenges = ForceForTests.Where(c => horde || c != HeroInjuries).ToList();
                int r1 = HordePlacement.RollD6(context), r2 = HordePlacement.RollD6(context), r3 = HordePlacement.RollD6(context);
                if (fq.Challenges.Contains(IncreasedAlertness)) fq.AlertLevel = r1 == 1 ? "Full Assault" : r1 <= 3 ? "Alarmed" : "Vigilant";
                if (fq.Challenges.Contains(TimeOfDayName)) fq.TimeOfDay = r2 switch { 1 => "Dusk", 2 => "Night", 3 => "Dawn", _ => "Day" };
                if (fq.Challenges.Contains(WeatherName)) fq.Weather = r3 switch { 1 => "Worsening", 2 => "Bad", 3 => "Improving", _ => "Good" };
                Save(context, fq);
                return;
            }
            PlayerID player = heroes[0].PlayerID;
            string[] all = horde
                ? new[] { BossFights, HeroInjuries, IncreasedAlertness, TimeOfDayName, WeatherName, RandomEvents }
                : new[] { BossFights, IncreasedAlertness, TimeOfDayName, WeatherName, RandomEvents };
            var picked = new List<string>();
            for (int guard = 0; guard < 20; guard++)
            {
                var options = new List<string> { StartOption };
                var descriptions = new Dictionary<string, string>
                {
                    [StartOption] = picked.Count == 0
                        ? "Start the mission with no optional challenges."
                        : "Start the mission with: " + string.Join(", ", picked) + ".",
                };
                // Each challenge is one row that shows whether it is on, and clicking it flips it. The row
                // used to read "Turn on: Weather" / "Turn off: Weather", which says what the click does but
                // never what the state IS - you had to read the verb backwards to know what you had picked.
                foreach (string c in all)
                {
                    string o = (picked.Contains(c) ? OnMark : OffMark) + c;
                    options.Add(o);
                    descriptions[o] = Describe(c, horde) + " (Optional - makes the game harder.)";
                }
                var request = new StringSelectionRequest(player, "Optional challenges (not recommended for beginners)",
                    options, Array.Empty<StringSelectionRequest.InvalidOption>(), optionDescriptions: descriptions,
                    displayName: "Optional challenges");
                string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                string? challenge = all.FirstOrDefault(c => pick == OnMark + c || pick == OffMark + c);
                if (challenge == null) break;
                if (!picked.Remove(challenge)) picked.Add(challenge);
            }

            QuestProgress q = Q(context);
            q.Challenges = picked;
            if (picked.Contains(IncreasedAlertness))
                q.AlertLevel = await PickOrRoll(context, player, IncreasedAlertness,
                    new[] { "Full Assault", "Alarmed", "Vigilant" }, r => r == 1 ? "Full Assault" : r <= 3 ? "Alarmed" : "Vigilant");
            if (picked.Contains(TimeOfDayName))
                q.TimeOfDay = await PickOrRoll(context, player, TimeOfDayName,
                    new[] { "Dusk", "Night", "Dawn", "Day" }, r => r switch { 1 => "Dusk", 2 => "Night", 3 => "Dawn", _ => "Day" });
            if (picked.Contains(WeatherName))
                q.Weather = await PickOrRoll(context, player, WeatherName,
                    new[] { "Worsening", "Bad", "Improving", "Good" }, r => r switch { 1 => "Worsening", 2 => "Bad", 3 => "Improving", _ => "Good" });
            Save(context, q);
            if (picked.Count > 0)
            {
                string extras = string.Join(", ", new[] { q.AlertLevel, q.TimeOfDay, q.Weather }.Where(s => s.Length > 0));
                context.Log($"Star Quest: optional challenges - {string.Join(", ", picked)}" + (extras.Length > 0 ? $" ({extras})." : "."));
                await context.Announce("Challenges: " + string.Join(", ", picked) + (extras.Length > 0 ? $" - {extras}" : ""),
                    new TextColor(255, 150, 90, 255), EBannerTier.Notice);
            }
        }

        private static async Task<string> PickOrRoll(IGameContext context, PlayerID player, string what, string[] values, Func<int, string> roll)
        {
            const string RollIt = "Roll for it";
            var options = new List<string> { RollIt };
            options.AddRange(values);
            var request = new StringSelectionRequest(player, $"{what}: pick one or roll for it", options,
                Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: what);
            string pick = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            if (values.Contains(pick)) return pick;
            int r = HordePlacement.RollD6(context);
            string result = roll(r);
            context.Log($"Star Quest: {what} - rolled {r}: {result}.");
            return result;
        }

        // ── Alertness, time of day, weather ──────────────────────────────────────────────────────────────

        /// <summary>Increased Alertness: Full Assault +1 on a 1-2, Alarmed +1 on a 1 (rolled each round end).</summary>
        public static int ExtraAlertness(IGameContext context)
        {
            string level = Q(context).AlertLevel;
            if (level != "Full Assault" && level != "Alarmed") return 0;
            int roll = HordePlacement.RollD6(context);
            bool extra = level == "Full Assault" ? roll <= 2 : roll == 1;
            if (extra) context.Log($"Star Quest: {level} - rolled {roll}, alertness +1 more.");
            return extra ? 1 : 0;
        }

        /// <summary>A new wave: time of day and weather move on (Dusk > Night > Dawn > Day; Worsening > Bad >
        /// Improving > Good).</summary>
        public static async Task AdvanceTimeAndWeather(IGameContext context)
        {
            QuestProgress q = Q(context);
            string t = q.TimeOfDay switch { "Dusk" => "Night", "Night" => "Dawn", "Dawn" => "Day", var x => x };
            string w = q.Weather switch { "Worsening" => "Bad", "Bad" => "Improving", "Improving" => "Good", var x => x };
            bool changed = t != q.TimeOfDay || w != q.Weather;
            q.TimeOfDay = t;
            q.Weather = w;
            Save(context, q);
            if (changed)
                await context.Announce(string.Join(", ", new[] { t, w }.Where(s => s.Length > 0)), new TextColor(200, 200, 255, 255), EBannerTier.Toast);
        }

        /// <summary>Bad weather halves all movement.</summary>
        public static bool HalvesMovement(IGameContext context) => Q(context).Weather == "Bad";

        /// <summary>Heavy Rain (event 34): a unit in that table quarter moves like in difficult terrain (6" max).</summary>
        public static bool InDifficultQuarter(IGameContext context, IUnit unit)
        {
            QuestProgress q = Q(context);
            return (EventNumber(q) == 34 && InQuarter(unit, q.EventQuarter))
                || QuestTreasure.NearMarker(q, unit, "grease") // Grease Grenade
                || (unit is not UnitData { IsQuestHero: true } && QuestTreasure.NearMarker(q, unit, "spikes")); // Spike Growth, Trapper
        }

        private static bool InQuarter(IUnit unit, int quarter)
        {
            var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0 || quarter < 1) return false;
            float x = living.Average(m => m.Position.x), z = living.Average(m => m.Position.z);
            int qx = x < StarQuestMissionRules.W / 2f ? 0 : 1, qz = z < StarQuestMissionRules.H / 2f ? 0 : 1;
            return qx + 2 * qz + 1 == quarter;
        }

        private static List<UnitData> OnTable(IGameContext c) =>
            QuestActions.Units(c).Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();

        /// <summary>Start of every round: this round's time-of-day, weather and event effects on every unit
        /// (granted for the round, so they end with it and are re-applied next round).</summary>
        public static async Task ApplyRoundEffects(IGameContext context)
        {
            await QuestTreasure.ItemMarkerRoundEffects(context);
            QuestProgress q = Q(context);
            List<UnitData> units = OnTable(context);
            if (q.TimeOfDay is "Night" or "Dawn")
                foreach (UnitData u in units) QuestTreasure.GrantRule(context, u, u, "-1 to hit rolls when shooting", ELifetime.ThisRound);
            if (q.Weather is "Bad" or "Improving")
                foreach (UnitData u in units) QuestTreasure.GrantRule(context, u, u, "-1 to hit rolls in melee", ELifetime.ThisRound);

            int ev = EventNumber(q);
            UnitData? hero = units.FirstOrDefault(u => u.ID.ID.ToString() == q.EventHero);
            switch (ev)
            {
                case 32: foreach (UnitData u in units) QuestActions.Grant(u, u, ERollKind.Save, -1, ELifetime.ThisRound, context, EventNames[32]); break;
                case 33: foreach (UnitData u in units) QuestActions.Grant(u, u, ERollKind.Hit, -1, ELifetime.ThisRound, context, EventNames[33]); break;
                case 35: foreach (UnitData u in units) QuestTreasure.GrantRule(context, u, u, "-6\" range when shooting", ELifetime.ThisRound); break;
                case 36: foreach (UnitData u in units) QuestActions.Grant(u, u, ERollKind.Morale, -1, ELifetime.ThisRound, context, EventNames[36]); break;
                case 31:
                    // Crumbling Earth: units that start the round in that quarter take a dangerous terrain test.
                    foreach (UnitData u in units.Where(u => InQuarter(u, q.EventQuarter)))
                    {
                        int dice = u.Models.Where(m => m.GetIsAlive()).Sum(m => Math.Max(1, (int)MathF.Round(m.TotalWounds)));
                        int ones = 0;
                        for (int i = 0; i < dice; i++) if (HordePlacement.RollD6(context) == 1) ones++;
                        if (ones > 0)
                        {
                            await QuestActions.Toast(context, $"Crumbling Earth: {u.Name} takes {ones} wound{(ones == 1 ? "" : "s")}.");
                            await CasualtyPresentation.ApplyUnitWounds(context, u, ones);
                        }
                    }
                    break;
            }
            if (hero == null) return;
            switch (ev)
            {
                case 41: QuestActions.Grant(hero, hero, ERollKind.Morale, 1, ELifetime.ThisRound, context, EventNames[41]); break;
                case 44: QuestTreasure.GrantRule(context, hero, hero, "Increased Shooting Range", ELifetime.ThisRound); break;
                case 52: QuestTreasure.GrantRule(context, hero, hero, "Fast", ELifetime.ThisRound); break;
                case 53: QuestTreasure.GrantRule(context, hero, hero, "+1 to hit rolls in melee", ELifetime.ThisRound); break;
                case 55: QuestTreasure.GrantRule(context, hero, hero, "+1 to hit rolls when shooting", ELifetime.ThisRound); break;
                case 56: QuestActions.Grant(hero, hero, ERollKind.Save, 1, ELifetime.ThisRound, context, EventNames[56]); break;
            }
        }

        /// <summary>Random events that change one hero's Str/Dex/Wil rolls until the next event.</summary>
        public static int EventStatBonus(IGameContext context, UnitData hero, int statType) =>
            EventStatBonusNamed(context, hero, statType).Delta;

        /// <summary>
        /// #407: the same modifier, with the event that caused it (from <see cref="EventNames"/>), so a
        /// stat test can say "Debilitating Static -1" rather than a bare "penalty -1".
        /// </summary>
        public static (int Delta, string? Event) EventStatBonusNamed(IGameContext context, UnitData hero,
            int statType)
        {
            QuestProgress q = Q(context);
            if (q.EventHero != hero.ID.ID.ToString()) return (0, null);
            int number = EventNumber(q);
            int delta = (number, statType) switch
            {
                (13, 0) => -1, (16, 2) => -1, (25, 1) => -1,
                (42, 1) => 1, (51, 2) => 1, (54, 0) => 1,
                _ => 0,
            };
            if (delta == 0) return (0, null);
            return (delta, EventNames.TryGetValue(number, out string? name) ? name : $"Event {number}");
        }

        // ── Random events ────────────────────────────────────────────────────────────────────────────────

        public static int EventNumber(QuestProgress q) => q.Event.Length >= 2 && int.TryParse(q.Event[..2], out int n) ? n : 0;

        public static readonly IReadOnlyDictionary<int, string> EventNames = new Dictionary<int, string>
        {
            [11] = "Lightning Strike", [12] = "Stuck in the Mud", [13] = "Debilitating Static", [14] = "Falling Debris",
            [15] = "Razor Vines", [16] = "Shifting Illusion", [21] = "Explosive Trap", [22] = "Alien Shriek",
            [23] = "Anomalous Gale", [24] = "Swarm Attack", [25] = "Sticky Foliage", [26] = "Venomous Moss",
            [31] = "Crumbling Earth", [32] = "Solar Flare", [33] = "Dazing Spores", [34] = "Heavy Rain",
            [35] = "Thick Clouds", [36] = "Terrifying Quakes", [41] = "Psychic Pool", [42] = "Robot Guide",
            [43] = "Treasure Holopad", [44] = "Spotter Drone", [45] = "Refreshment Kit", [46] = "Sinkhole",
            [51] = "Psychic Fog", [52] = "Haste Stims", [53] = "Invigorating Fumes", [54] = "Support Robot",
            [55] = "Enhancing Force-Field", [56] = "Shield-Projector Drone", [61] = "Injector Pouch", [62] = "Prophetic Vision",
            [63] = "Grenade Bag", [64] = "Credits Stash", [65] = "Kit Box", [66] = "Godly Blessing",
        };

        private static readonly Dictionary<int, string> EventText = new()
        {
            [11] = "becomes Unconscious", [12] = "becomes Impaired", [13] = "gets -1 to Str rolls until the next event",
            [14] = "takes D3 wounds", [15] = "becomes Crippled", [16] = "gets -1 to Wil rolls until the next event",
            [21] = "takes D3 hits", [22] = "loses D3 power", [23] = "becomes Afflicted", [24] = "becomes Shaken",
            [25] = "gets -1 to Dex rolls until the next event", [26] = "becomes Diseased",
            [31] = "One random table quarter counts as Dangerous terrain until the next event",
            [32] = "All heroes and enemies get -1 to Defense rolls until the next event",
            [33] = "All heroes and enemies get -1 to hit rolls until the next event",
            [34] = "One random table quarter counts as Difficult terrain until the next event",
            [35] = "All heroes and enemies get -6\" range when shooting until the next event",
            [36] = "All heroes and enemies get -1 to morale test rolls until the next event",
            [41] = "gets +1 to morale test rolls until the next event", [42] = "gets +1 to Dex rolls until the next event",
            [43] = "D3 search tokens appear near the centres of the table quarters",
            [44] = "gets +6\" range when shooting until the next event", [45] = "restores D3 power",
            [46] = "One random enemy unit becomes Shaken", [51] = "gets +1 to Wil rolls until the next event",
            [52] = "moves +2\" on Advance and +4\" on Rush/Charge until the next event",
            [53] = "gets +1 to hit in melee until the next event", [54] = "gets +1 to Str rolls until the next event",
            [55] = "gets +1 to hit when shooting until the next event", [56] = "gets +1 to Defense rolls until the next event",
            [61] = "gets a Power Injector", [62] = "D3 search tokens show what they hold", [63] = "gets a Stun Grenade",
            [64] = "gets 15c", [65] = "gets a Med Kit", [66] = "gets D3 XP",
        };

        /// <summary>Rolls two dice (tens, then ones) and resolves that random event.</summary>
        /// <param name="forced">Game Master: this event (11-66) instead of a roll, even with Random Events off.</param>
        public static async Task RollRandomEvent(IGameContext context, int? forced = null)
        {
            if (forced == null && !IsOn(context, RandomEvents)) return;
            int tens = HordePlacement.RollD6(context), ones = HordePlacement.RollD6(context);
            if (forced is int f && EventNames.ContainsKey(f)) { tens = f / 10; ones = f % 10; }
            int n = tens * 10 + ones;
            QuestProgress q = Q(context);
            List<UnitData> heroes = StarQuestHordeRules.Heroes(context).Where(h => h.GetIsAlive() && h.GetIsOnBattlefield()).ToList();
            UnitData? hero = heroes.Count == 0 ? null : heroes[(HordePlacement.RollD6(context) * 5 + ones) % heroes.Count];
            q.Event = $"{n} - {EventNames[n]}";
            q.EventHero = hero?.ID.ID.ToString() ?? "";
            q.EventQuarter = 1 + (HordePlacement.RollD6(context) - 1) % 4;
            Save(context, q);

            bool heroEvent = n is < 30 or > 40 && n is not (43 or 46 or 62);
            string text = heroEvent && hero != null ? $"{hero.Name} {EventText[n]}" : EventText[n];
            if (n is 31 or 34) text += $" (quarter {q.EventQuarter}: {HordeRules.CornerName(q.EventQuarter)})";
            context.Log($"Star Quest: random event {n} - {EventNames[n]}: {text}.");
            await context.Announce($"Event {n} - {EventNames[n]}: {text}", new TextColor(220, 160, 255, 255), EBannerTier.Notice);
            int d3 = (HordePlacement.RollD6(context) + 1) / 2;

            if (hero != null)
                switch (n)
                {
                    case 11:
                        float left = hero.Models.Where(m => m.GetIsAlive()).Sum(m => m.TotalWounds - m.WoundsDealt);
                        await CasualtyPresentation.ApplyUnitWounds(context, hero, left);
                        break;
                    case 12: QuestTreasure.AddCondition(hero, QuestTreasure.Impaired); break;
                    case 15: QuestTreasure.AddCondition(hero, QuestTreasure.Crippled); break;
                    case 23: QuestTreasure.AddCondition(hero, QuestTreasure.Afflicted); break;
                    case 26: QuestTreasure.AddCondition(hero, QuestTreasure.Diseased); break;
                    case 14: await CasualtyPresentation.ApplyUnitWounds(context, hero, d3); break;
                    case 21: await QuestActions.DealHits(context, hero, hero, d3, 0, "Explosive Trap"); break;
                    case 22: await QuestActions.SpendPower(context, hero, d3, "Alien Shriek"); break;
                    case 24: if (Binding(context, hero) is { } hb) MoraleUtilities.ApplyShaken(hb); break;
                    case 45: QuestActions.RestorePower(hero, d3); break;
                    case 61: await QuestTreasure.Give(context, hero, new[] { "Power Injector" }); break;
                    case 63: await QuestTreasure.Give(context, hero, new[] { "Stun Grenade" }); break;
                    case 64: await QuestTreasure.Give(context, hero, new[] { "15c" }); break;
                    case 65: await QuestTreasure.Give(context, hero, new[] { "Med Kit" }); break;
                    case 66: await QuestLevelling.GiveXp(context, hero, d3); break;
                }
            switch (n)
            {
                case 43:
                    var spots = Enumerable.Range(0, d3).Select(i => (1 + (q.EventQuarter - 1 + i) % 4, false)).ToList();
                    QuestTreasure.PlaceTokensAt(context, spots, "Treasure Holopad");
                    break;
                case 46:
                    var enemies = StarQuestHordeRules.Enemies(context).Where(e => e.GetIsAlive() && e.GetIsOnBattlefield()).ToList();
                    if (enemies.Count > 0 && Binding(context, enemies[(d3 + tens) % enemies.Count]) is { } eb) MoraleUtilities.ApplyShaken(eb);
                    break;
                case 62: QuestTreasure.ForeseeTokens(context, d3); break;
            }
        }

        private static DataBinding<UnitData>? Binding(IGameContext context, UnitData unit) =>
            context.GameDataStore.GetAllDataBindings<UnitData>().FirstOrDefault(b => ReferenceEquals(b.GetValue(), unit));

        // ── Boss fights ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>Asks whether to trigger a boss fight now.</summary>
        public static async Task<bool> OfferBossFight(IGameContext context, string when, bool askOnce = false)
        {
            if (!IsOn(context, BossFights) || Q(context).BossFight != 0) return false;
            List<UnitData> heroes = StarQuestHordeRules.Heroes(context);
            if (heroes.Count == 0) return false;
            bool yes = ForceForTests != null || await context.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(heroes[0].PlayerID,
                $"{when} Trigger a boss fight? A double-size wave arrives - destroy all of it for bonus XP.",
                defaultAnswer: false, displayName: "Boss fight?"));
            if (!yes && askOnce)
            {
                QuestProgress q = Q(context);
                q.BossFight = -1; // declined: not asked again this mission
                Save(context, q);
            }
            return yes;
        }

        public static void StartBossWave(IGameContext context, IEnumerable<UnitData> wave)
        {
            QuestProgress q = Q(context);
            q.BossFight = 1;
            q.BossWave = wave.Select(u => u.ID.ID.ToString()).ToList();
            Save(context, q);
        }

        /// <summary>True when the boss wave was destroyed (and clears the boss fight).</summary>
        public static bool BossWaveDestroyed(IGameContext context, bool clear)
        {
            QuestProgress q = Q(context);
            if (q.BossFight != 1) return false;
            bool all = HordePlacement.AllUnits(context).Where(u => q.BossWave.Contains(u.ID.ID.ToString())).All(u => !u.GetIsAlive());
            if (clear) { q.BossFight = 0; q.BossWave.Clear(); Save(context, q); }
            return all;
        }
    }
}
