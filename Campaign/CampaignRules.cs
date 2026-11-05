using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.StateMachine;

namespace FDG.Campaign
{
    /// <summary>How one campaign unit did in a match: whether it was a casualty, and what it killed.</summary>
    /// <param name="Id">The unit's sheet id (<see cref="StrikeUnit.Id"/>).</param>
    /// <param name="Casualty">The unit was fully destroyed or Routed.</param>
    /// <param name="Kills">Enemy units this unit destroyed.</param>
    /// <param name="HeroKills">Enemy Hero units this unit destroyed.</param>
    public readonly record struct CampaignUnitResult(string Id, bool Casualty, int Kills, int HeroKills);

    /// <summary>
    /// The campaign loop shared by all four Campaign Rules v3.6.0 books, driven by a
    /// <see cref="CampaignNumbers"/> price list so one implementation serves Grimdark Future,
    /// GF: Firefight, Age of Fantasy and AoF: Skirmish.
    /// <para>
    /// This covers the post-game half of the six-step structure - step 3 (Check for Casualties),
    /// step 4 (Earn Experience) and the mission reward from step 2. The pre-game half (army selection,
    /// underdog points, special objectives) and steps 5-6 (Upgrade Armies, Recruit Units) are not here
    /// yet; see WorkItems/410.
    /// </para>
    /// <para>
    /// Dice come in as a <c>Func&lt;int&gt;</c> so every table can be tested without a game.
    /// </para>
    /// </summary>
    public static class CampaignRules
    {
        // -- Starting an army ------------------------------------------------------------------------

        /// <summary>
        /// Why this army can't start a campaign under <paramref name="n"/>, or null if it can.
        /// The book: spend at least <see cref="CampaignNumbers.MinimumSpend"/> of the treasury, no more
        /// than the treasury itself, and at most one Hero bought with those points.
        /// </summary>
        public static string? CannotStart(ArmyListFile army, CampaignNumbers n)
        {
            if (army.Units.Count == 0) return "The army has no units.";
            if (army.TotalPoints > n.Treasury)
                return $"The army is {army.TotalPoints} pts; a new campaign army has a {n.Treasury} pt treasury.";
            if (army.TotalPoints < n.MinimumSpend)
                return $"The army is {army.TotalPoints} pts; a new campaign army must spend at least {n.MinimumSpend} pts of its {n.Treasury} pt treasury.";
            if (army.Units.Count(IsHero) > 1) return "A new campaign army may only buy one Hero to start with.";
            return null;
        }

        /// <summary>
        /// Turns an army into a campaign army: a sheet on every unit (with a unique name), and whatever
        /// is left of the treasury kept as spendable points. There is no cap on what carries over - the
        /// treasury IS the mechanic, unlike the 40-point cap the old Firefight implementation used.
        /// </summary>
        /// <param name="noPermadeath">
        /// The books' No Permadeath option, fixed when the campaign is created: a unit that would be
        /// killed survives instead, loses that match's XP and sits out D3 missions.
        /// </param>
        /// <param name="vpTarget">
        /// #445 - the VP that wins a <see cref="ECampaignType.PointLimited"/> campaign. Ignored by
        /// Endless, and 0 means no target. Pass the SAME value for both armies of one campaign: see
        /// <see cref="StrikeTeamInfo.VpTarget"/> for why the two blocks must not disagree.
        /// </param>
        public static void Start(ArmyListFile army, string armyName, string armyFile, CampaignNumbers n,
                                 EGameSystem system, ECampaignType type = ECampaignType.Endless,
                                 bool noPermadeath = false, bool isBot = false, int vpTarget = 0)
        {
            army.Name = armyName;
            army.StrikeTeam = new StrikeTeamInfo
            {
                Name = armyName,
                BankedPoints = Math.Max(0, n.Treasury - army.TotalPoints),
                GameSystem = system,
                CampaignType = type,
                // Kept even when the type is Endless. A player who switches a campaign's type later (or a
                // future screen that offers it) then still has the number they chose, and storing it
                // costs nothing; HasTarget gates on the TYPE, so a stored target is inert until the type
                // is PointLimited.
                VpTarget = Math.Max(0, vpTarget),
                NoPermadeath = noPermadeath,
                IsBot = isBot,
            };
            // If the army was built in the Army Forge it carries the builder list it was compiled from.
            // Each unit's selections move onto its own sheet, so it can be re-opened between missions to
            // buy and sell upgrades without re-compiling the whole army and wiping its campaign changes.
            // Matched by the author-stable id where there is one, and otherwise by position, which holds
            // whenever the compiler produced one unit per builder unit. A unit with no match simply can't
            // be upgraded - it still plays, levels and takes injuries normally.
            var selections = (army as ArmyBuilding.BuiltArmyFile)?.Selections?.Units;
            bool positional = selections != null && selections.Count == army.Units.Count;

            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < army.Units.Count; index++)
            {
                UnitFileEntry unit = army.Units[index];
                string name = unit.Name;
                for (int i = 2; !names.Add(name); i++) name = $"{unit.Name} {i}";
                unit.Name = name;

                var sheet = new StrikeUnit { IsHero = IsHero(unit), TeamFile = armyFile };
                Save(unit, sheet);

                ArmyBuilding.BuilderUnit? loadout = selections?.FirstOrDefault(
                    b => b.Id != null && string.Equals(b.Id, unit.Id, StringComparison.OrdinalIgnoreCase));
                if (loadout == null && positional) loadout = selections![index];
                if (loadout != null) CampaignEconomy.SaveLoadout(unit, loadout);
            }
        }

        // -- The win/loss record --------------------------------------------------------------------

        /// <summary>
        /// The one line <see cref="AfterGame"/> adds to <see cref="StrikeTeamInfo.History"/> per mission:
        /// "Match 3: won - " followed by that mission's log. The format has not changed since the first
        /// campaign code (2026-10-24), which is what makes <see cref="IsWonHistoryLine"/> safe to run on
        /// every campaign file that exists.
        /// </summary>
        internal static string HistoryLine(int match, bool won, IEnumerable<string> log) =>
            $"Match {match}: {(won ? "won" : "lost")} - " + string.Join(" ", log);

        private static readonly System.Text.RegularExpressions.Regex WonHistoryLine =
            new(@"^Match \d+: won - ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

        /// <summary>
        /// Whether a history line is <see cref="AfterGame"/>'s record of a WON mission. Paired with
        /// <see cref="HistoryLine"/>, which writes it, so the reader and the writer live side by side.
        /// <para>
        /// #449 - this replaces #446's first version, which looked for lines starting "Won the mission
        /// on". That is the start of a LOG line, and log lines are folded into the history after
        /// "Match N: won - ", so it never matched a real file and every pre-#446 campaign recovered zero
        /// wins. Its test passed because the test added the log lines to the history itself.
        /// </para>
        /// </summary>
        internal static bool IsWonHistoryLine(string line) => WonHistoryLine.IsMatch(line);

        /// <summary>
        /// #446 - this army's win/loss record (campaign-flow-spec section 2).
        ///
        /// <para>
        /// Normally just <see cref="StrikeTeamInfo.Wins"/> and the rest of
        /// <see cref="StrikeTeamInfo.Matches"/>. A campaign saved before <c>Wins</c> existed reads back
        /// with it at zero however many missions it won, so in that one case the wins are recovered by
        /// counting the history lines <see cref="AfterGame"/> wrote. That is a migration path, not the
        /// mechanism: once a mission is played under this build the stored count is authoritative.
        /// </para>
        ///
        /// <para>
        /// Safe to run whenever <c>Wins</c> is zero, including for an army that genuinely has lost every
        /// mission - there are then no winning lines to find and the answer is zero either way.
        /// The history line is written by <see cref="HistoryLine"/> and recognised by
        /// <see cref="IsWonHistoryLine"/>, side by side, and
        /// <c>CampaignRecordTests.AWinWrittenByAfterGameIsFoundByTheBackfill</c> drives a real
        /// <c>AfterGame</c> - without touching the history itself - and asserts the backfill recovers it.
        /// </para>
        ///
        /// <para>
        /// #449 - <see cref="AfterGame"/> stamps this recovered count into <c>Wins</c> before it counts
        /// the new mission. Without that, the first mission played under this build set <c>Wins</c> to
        /// 1, the backfill stopped running (it only runs at zero), and a campaign that had won three
        /// missions read as one win from then on.
        /// </para>
        /// </summary>
        public static (int Wins, int Losses) RecordOf(StrikeTeamInfo info)
        {
            int wins = info.Wins;
            if (wins == 0 && info.Matches > 0)
                wins = info.History.Count(IsWonHistoryLine);

            wins = Math.Clamp(wins, 0, Math.Max(0, info.Matches));
            return (wins, Math.Max(0, info.Matches - wins));
        }

        // -- After a match ---------------------------------------------------------------------------

        /// <summary>
        /// Steps 3 and 4 plus the mission reward: casualty rolls, XP, level ups, then VP and treasury.
        /// Units that did not play are left untouched. Returns the log lines, which are also folded into
        /// the army's history.
        /// </summary>
        public static List<string> AfterGame(ArmyListFile army, IReadOnlyList<CampaignUnitResult> results,
                                             bool won, CampaignNumbers n, Func<int> d6)
        {
            var log = new List<string>();
            // #432: campaign random events hand out XP, treasury and VP during a game, carried out of it
            // on the units' own sheets. Totalled as the results are read and banked once, below.
            int eventPoints = 0, eventVp = 0;

            // No Permadeath: a unit recovering from a killing blow has now missed a mission. Counted
            // down BEFORE this match's casualties are resolved, so a unit knocked out today starts its
            // full D3 from today rather than losing a mission of it immediately. A unit sitting out
            // never appears in results - it was not on the table - so this is the only place it moves.
            foreach (UnitFileEntry resting in army.Units)
            {
                if (Sheet(resting) is not { MissionsOut: > 0 } sheet) continue;
                sheet.MissionsOut--;
                log.Add(sheet.MissionsOut == 0
                    ? $"{resting.Name} has recovered and can fight again."
                    : $"{resting.Name} is still recovering: {sheet.MissionsOut} more mission(s) out.");
                Save(resting, sheet);
            }

            foreach (CampaignUnitResult result in results)
            {
                UnitFileEntry? unit = army.Units.FirstOrDefault(u => Sheet(u)?.Id == result.Id);
                if (unit == null || Sheet(unit) is not { } sheet) continue;
                sheet.Games++;
                sheet.Kills = sheet.HeroKills = 0;
                // #430's in-game tallies are per-mission too. Cleared here with the kills so a recovery
                // save (which serializes the live table, sheets and all) cannot carry one mission's
                // spell count or first-blood stamp into the next one.
                sheet.SpellsCast = 0;
                sheet.DrewFirstBlood = false;
                // #432: what a campaign random event handed out during the game, carried out of it on
                // this sheet. The XP is THIS unit's ("one random unit gets 1 XP"), so it joins the unit's
                // own total below; treasury and VP belong to the army and are banked once, after the
                // loop. Cleared either way, like the kills.
                int xp = sheet.EventXp;
                eventPoints += sheet.EventPoints;
                eventVp += sheet.EventVp;
                sheet.EventXp = sheet.EventPoints = sheet.EventVp = 0;

                if (result.Casualty)
                {
                    switch (ResolveCasualty(army, unit, sheet, n, d6, log, ref xp))
                    {
                        case ECasualtyOutcome.Removed: continue;   // dead, and out of the army
                        case ECasualtyOutcome.KnockedOut:
                            // The book's No Permadeath wording: it "loses the XP it earned for that
                            // match". All of it - the +1 for surviving it never earned, and the XP for
                            // whatever it killed before it fell.
                            Save(unit, sheet);
                            continue;
                    }
                }
                else xp++;  // +1 XP: not a casualty

                xp += result.Kills + 2 * result.HeroKills;
                if (xp > 0) log.AddRange(AddXp(unit, sheet, xp, n, d6));
                Save(unit, sheet);
            }

            StrikeTeamInfo info = army.StrikeTeam ??= new StrikeTeamInfo { Name = army.Name };
            // #449 - a campaign saved before Wins existed carries its wins only in its history. Fold them
            // into the counter now, while the backfill can still see them, because once this mission
            // makes Wins non-zero RecordOf stops looking at the history at all.
            info.Wins = RecordOf(info).Wins;
            info.Matches++;
            if (won)
            {
                // #435: a win scales with the mission's difficulty - 1 VP and half the book's treasury
                // per level, so Normal pays exactly what the book prints and Easy deliberately pays
                // less. Read off the army's own strike-team block rather than taken as a parameter, so no
                // caller can pay the wrong rate by forgetting to pass it.
                int vp = MissionDifficulty.WinVp(info.Difficulty);
                int points = MissionDifficulty.WinPoints(info.Difficulty, n);
                info.Vp += vp;
                info.BankedPoints += points;
                info.Wins++;   // #446 - the win half of the record; losses are Matches minus this.
                log.Add($"Won the mission on {MissionDifficulty.Name(info.Difficulty)}: "
                        + $"+{vp} VP, +{points} pts.");
            }
            else
            {
                // A loss pays the book's flat figure at every setting (Kai, 2026-11-02). See
                // MissionDifficulty.LosePoints for why scaling it would invert the books' catch-up.
                int points = MissionDifficulty.LosePoints(info.Difficulty, n);
                info.BankedPoints += points;
                log.Add($"Lost the mission: +{points} pts.");
            }
            // #432: and whatever the random events handed the army, banked once.
            if (eventPoints > 0)
            {
                info.BankedPoints += eventPoints;
                log.Add($"Random events during the mission: +{eventPoints} pts.");
            }
            if (eventVp > 0)
            {
                info.Vp += eventVp;
                log.Add($"Random events during the mission: +{eventVp} VP.");
            }

            info.History.Add(HistoryLine(info.Matches, won, log));
            // #450 - and the same account onto this mission's journal page, where the player reads it
            // above their own writing. Separate from History, which is the game's log and is parsed.
            CampaignJournal.SetRecord(info, info.Matches, log);
            return log;
        }

        /// <summary>What a casualty roll did to the unit.</summary>
        private enum ECasualtyOutcome
        {
            /// <summary>It is still in the army and may be taken to the next mission.</summary>
            Survived,
            /// <summary>It was killed and is out of the army for good.</summary>
            Removed,
            /// <summary>
            /// It was killed in a No Permadeath campaign: it stays in the army, earns no XP for this
            /// match, and sits out <see cref="StrikeUnit.MissionsOut"/> missions.
            /// </summary>
            KnockedOut,
        }

        /// <summary>
        /// One casualty roll. <paramref name="xp"/> gains 1 for a Natural Talent result; the caller
        /// throws that away if the outcome is <see cref="ECasualtyOutcome.KnockedOut"/>.
        /// </summary>
        private static ECasualtyOutcome ResolveCasualty(ArmyListFile army, UnitFileEntry unit, StrikeUnit sheet,
                                                        CampaignNumbers n, Func<int> d6, List<string> log, ref int xp)
        {
            int roll = d6();
            if (!sheet.IsHero)
            {
                switch (roll)
                {
                    case 1:
                        return Kill(army, unit, sheet, d6, log,
                            $"{unit.Name} was a casualty and rolled 1: dead");
                    case 6:
                        xp++;
                        log.Add($"{unit.Name} was a casualty and rolled 6: Natural Talent, +1 XP.");
                        return ECasualtyOutcome.Survived;
                    default:
                        log.Add($"{unit.Name} was a casualty and rolled {roll}: recovered.");
                        return ECasualtyOutcome.Survived;
                }
            }

            if (roll == 1)
            {
                string injury = RollTrait(d6, CampaignTables.Injuries, sheet.Injuries);
                if (injury == "Dead")
                {
                    return Kill(army, unit, sheet, d6, log,
                        $"{unit.Name} was a casualty: rolled 1, then Dead on the injury roll");
                }
                if (injury == "Lucky Break")
                {
                    sheet.Pending.Add(CampaignTables.LuckyBreak);
                    ChangeCost(unit, sheet, -n.HeroInjuryTalentCost);
                    log.Add($"{unit.Name} was a casualty: rolled 1, then Lucky Break - pick any other injury trait on the campaign screen (-{n.HeroInjuryTalentCost} pts).");
                    return ECasualtyOutcome.Survived;
                }
                GainInjury(unit, sheet, injury);
                ChangeCost(unit, sheet, -n.HeroInjuryTalentCost);
                log.Add($"{unit.Name} was a casualty: rolled 1, injury {injury} (-{n.HeroInjuryTalentCost} pts).");
                return ECasualtyOutcome.Survived;
            }

            if (roll == 6)
            {
                string talent = RollTrait(d6, CampaignTables.Talents, sheet.Talents);
                if (talent == "Learned Lesson")
                {
                    sheet.Pending.Add(CampaignTables.LearnedLesson);
                    ChangeCost(unit, sheet, n.HeroInjuryTalentCost);
                    log.Add($"{unit.Name} was a casualty: rolled 6, then Learned Lesson - pick any other talent trait on the campaign screen (+{n.HeroInjuryTalentCost} pts).");
                    return ECasualtyOutcome.Survived;
                }
                GainTalent(unit, sheet, talent, ref xp);
                ChangeCost(unit, sheet, n.HeroInjuryTalentCost);
                log.Add($"{unit.Name} was a casualty: rolled 6, talent {talent} (+{n.HeroInjuryTalentCost} pts).");
                return ECasualtyOutcome.Survived;
            }

            log.Add($"{unit.Name} was a casualty and rolled {roll}: recovered.");
            return ECasualtyOutcome.Survived;
        }

        /// <summary>
        /// The killing blow, resolved under whichever option the campaign was created with.
        /// <para>
        /// Permadeath on (the books' default): the unit leaves the army, with everything it had.
        /// </para>
        /// <para>
        /// <b>No Permadeath</b>: "the unit is not killed, but instead loses all of the XP it earned for
        /// that match, and must sit out of the next D3 matches". It keeps the levels, traits, injuries
        /// and talents it already had - only this match's earnings are lost, which the caller does by
        /// throwing the XP away.
        /// </para>
        /// <para>
        /// <b>Judgement call, recorded in claude/house-rules.md:</b> a Hero reaches this through "Dead"
        /// on the injury table, and the knock-out REPLACES that result rather than being an injury on
        /// top of it - so a hero who survives by this option takes no injury trait and loses no points.
        /// The book replaces the death, and Dead is the death.
        /// </para>
        /// </summary>
        private static ECasualtyOutcome Kill(ArmyListFile army, UnitFileEntry unit, StrikeUnit sheet,
                                             Func<int> d6, List<string> log, string prefix)
        {
            if (army.StrikeTeam?.NoPermadeath != true)
            {
                log.Add($"{prefix} - removed from the army.");
                army.Units.Remove(unit);
                return ECasualtyOutcome.Removed;
            }

            sheet.MissionsOut = RollD3(d6);
            log.Add($"{prefix}, but this campaign has No Permadeath: {unit.Name} survives, loses the XP it "
                    + $"earned this match and sits out the next {sheet.MissionsOut} mission(s).");
            return ECasualtyOutcome.KnockedOut;
        }

        /// <summary>A D3, rolled as half a D6 rounded up - which is how every OPR book rolls one.</summary>
        private static int RollD3(Func<int> d6) => (Math.Clamp(d6(), 1, 6) + 1) / 2;

        /// <summary>
        /// Whether this unit may be taken to the next mission. False only while it is recovering under
        /// the No Permadeath option (<see cref="StrikeUnit.MissionsOut"/>).
        /// </summary>
        public static bool CanBringToMission(UnitFileEntry unit) => (Sheet(unit)?.MissionsOut ?? 0) <= 0;

        /// <summary>Why this unit can't come to the next mission, or null.</summary>
        public static string? CannotBringToMission(UnitFileEntry unit) =>
            Sheet(unit) is { MissionsOut: > 0 } sheet
                ? $"Recovering - out for {sheet.MissionsOut} more mission(s)."
                : null;

        /// <summary>Adds XP (capped at 30) and levels the unit up once for every 5 XP crossed.</summary>
        public static List<string> AddXp(UnitFileEntry unit, StrikeUnit sheet, int amount,
                                         CampaignNumbers n, Func<int> d6)
        {
            var log = new List<string>();
            int before = sheet.Xp;
            sheet.Xp = Math.Min(CampaignNumbers.MaxXp, before + amount);
            if (sheet.Xp == before) return log;
            log.Add($"{unit.Name} +{sheet.Xp - before} XP ({sheet.Xp} XP).");
            int levels = sheet.Xp / CampaignNumbers.XpPerLevel - before / CampaignNumbers.XpPerLevel;
            for (int i = 0; i < levels; i++) log.AddRange(LevelUp(unit, sheet, n, d6));
            return log;
        }

        /// <summary>One level up: the unit costs more, a trait is rolled, and a hero also gets a skill trait.</summary>
        public static List<string> LevelUp(UnitFileEntry unit, StrikeUnit sheet, CampaignNumbers n, Func<int> d6)
        {
            int cost = sheet.IsHero ? n.HeroLevelCost : n.RegularLevelCost;
            var log = new List<string> { $"{unit.Name} levels up (+{cost} pts)." };
            ChangeCost(unit, sheet, cost);
            log.Add(RollLevelTrait(unit, sheet, d6));
            if (sheet.IsHero) log.Add(RollSkill(unit, sheet, d6));
            return log;
        }

        // -- Trait and skill rolls -------------------------------------------------------------------

        private static string RollLevelTrait(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6)
        {
            if (TraitsLeft(sheet).Count == 0) return $"{unit.Name} already has every trait.";
            for (int guard = 0; guard < 60; guard++)
            {
                string entry = CampaignTables.Entry(CampaignTables.Traits, d6());
                if (entry == CampaignTables.Specialist)
                {
                    List<string> left = SpecialistLeft(sheet);
                    if (left.Count == 0) continue;
                    if (left.Count == 1) return GainTrait(unit, sheet, left[0]);
                    sheet.Pending.Add(CampaignTables.Specialist);
                    return $"{unit.Name} rolled Specialist: pick melee or shooting on the campaign screen.";
                }
                if (entry == CampaignTables.FastLearner)
                {
                    sheet.Pending.Add(CampaignTables.FastLearner);
                    return $"{unit.Name} rolled Fast Learner: pick any other trait on the campaign screen.";
                }
                if (!sheet.Traits.Contains(entry)) return GainTrait(unit, sheet, entry);
            }
            string? any = TraitsLeft(sheet).FirstOrDefault();
            return any != null ? GainTrait(unit, sheet, any) : $"{unit.Name}: no new trait.";
        }

        private static string RollSkill(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6)
        {
            // Keep using a set the hero already has while it still has a skill left in it.
            string? set = sheet.SkillSets.LastOrDefault(
                s => CampaignTables.SkillSets.TryGetValue(s, out string[]? k) && k.Any(x => !sheet.Skills.Contains(x)));
            if (set != null) return GainSkill(unit, sheet, set, d6, null);

            // Otherwise roll a new pair to pick from: 1-2, 3-4, 5-6.
            var pairs = CampaignTables.SkillSetPairs
                .Where(p => !sheet.SkillSets.Contains(p.A) || !sheet.SkillSets.Contains(p.B)).ToList();
            if (pairs.Count == 0) return $"{unit.Name} already knows every skill.";

            (string a, string b) = pairs[0];
            for (int guard = 0; guard < 30; guard++)
            {
                (string ra, string rb) = CampaignTables.SkillSetPairs[(Math.Clamp(d6(), 1, 6) - 1) / 2];
                if (!sheet.SkillSets.Contains(ra) || !sheet.SkillSets.Contains(rb)) { (a, b) = (ra, rb); break; }
            }
            if (sheet.SkillSets.Contains(a)) { sheet.SkillSets.Add(b); return GainSkill(unit, sheet, b, d6, $"skill set {b}"); }
            if (sheet.SkillSets.Contains(b)) { sheet.SkillSets.Add(a); return GainSkill(unit, sheet, a, d6, $"skill set {a}"); }
            sheet.Pending.Add($"{CampaignTables.SkillSetPick}{a}|{b}");
            return $"{unit.Name} rolled the {a} or {b} skill sets: pick one on the campaign screen (its skill is rolled then).";
        }

        private static string GainSkill(UnitFileEntry unit, StrikeUnit sheet, string set, Func<int> d6, string? prefix)
        {
            string[] skills = CampaignTables.SkillSets[set];
            // The table is rolled 1-2 / 3-4 / 5-6, so each skill fills two faces of the die.
            string[] faces = { skills[0], skills[0], skills[1], skills[1], skills[2], skills[2] };
            string skill = RollTrait(d6, faces, sheet.Skills);
            sheet.Skills.Add(skill);
            AddRule(unit, CampaignTables.SkillPrefix + skill);
            return $"{unit.Name} " + (prefix != null ? $"takes {prefix} and " : "") + $"learns {skill}.";
        }

        private static string GainTrait(UnitFileEntry unit, StrikeUnit sheet, string trait)
        {
            sheet.Traits.Add(trait);
            AddRule(unit, trait);
            return $"{unit.Name} gains {trait.Replace("Trait: ", "")}.";
        }

        private static void GainInjury(UnitFileEntry unit, StrikeUnit sheet, string injury)
        {
            sheet.Injuries.Add(injury);
            AddRule(unit, CampaignTables.InjuryPrefix + injury);
        }

        private static void GainTalent(UnitFileEntry unit, StrikeUnit sheet, string talent, ref int xp)
        {
            sheet.Talents.Add(talent);
            AddRule(unit, CampaignTables.TalentPrefix + talent);
            if (talent == "Natural Talent") xp++;
            if (talent == "Toughened") AddTough(unit, 3);
        }

        // -- Pending choices -------------------------------------------------------------------------

        /// <summary>The melee/shooting specialist traits this unit hasn't taken yet.</summary>
        public static List<string> SpecialistLeft(StrikeUnit sheet) =>
            new[] { HordeExperience.MeleeSpecialist, HordeExperience.ShootingSpecialist }
                .Where(t => !sheet.Traits.Contains(t)).ToList();

        /// <summary>The level-up traits a Fast Learner may still pick.</summary>
        public static List<string> TraitsLeft(StrikeUnit sheet) =>
            new[] { HordeExperience.Agile, HordeExperience.Headstrong, HordeExperience.MeleeSpecialist,
                    HordeExperience.ShootingSpecialist, HordeExperience.Resilient, HordeExperience.Elite }
                .Where(t => !sheet.Traits.Contains(t)).ToList();

        /// <summary>The options of a pending choice on this unit's sheet.</summary>
        public static List<string> Options(StrikeUnit sheet, string pending) => pending switch
        {
            CampaignTables.Specialist => SpecialistLeft(sheet),
            CampaignTables.FastLearner => TraitsLeft(sheet),
            CampaignTables.LuckyBreak => CampaignTables.PickableInjuries.Where(i => !sheet.Injuries.Contains(i)).ToList(),
            CampaignTables.LearnedLesson => CampaignTables.PickableTalents.Where(t => !sheet.Talents.Contains(t)).ToList(),
            _ when pending.StartsWith(CampaignTables.SkillSetPick) =>
                pending[CampaignTables.SkillSetPick.Length..].Split('|').ToList(),
            _ => new List<string>(),
        };

        /// <summary>Makes a pending choice. A skill set choice rolls its skill straight away.</summary>
        public static string Choose(UnitFileEntry unit, string pending, string choice, Func<int> d6)
        {
            if (Sheet(unit) is not { } sheet || !sheet.Pending.Contains(pending)) return "Nothing to choose.";
            if (!Options(sheet, pending).Contains(choice)) return $"{choice} can't be picked.";
            sheet.Pending.Remove(pending);

            string line;
            if (pending.StartsWith(CampaignTables.SkillSetPick))
            {
                sheet.SkillSets.Add(choice);
                line = GainSkill(unit, sheet, choice, d6, $"skill set {choice}");
            }
            else if (pending == CampaignTables.LuckyBreak)
            {
                GainInjury(unit, sheet, choice);
                line = $"{unit.Name} takes the {choice} injury (Lucky Break).";
            }
            else if (pending == CampaignTables.LearnedLesson)
            {
                int ignored = 0;
                GainTalent(unit, sheet, choice, ref ignored);
                line = $"{unit.Name} takes the {choice} talent (Learned Lesson).";
            }
            else line = GainTrait(unit, sheet, choice);

            Save(unit, sheet);
            return line;
        }

        // -- Shared helpers --------------------------------------------------------------------------

        public static bool IsCampaignArmy(ArmyListFile army) => army.StrikeTeam != null;

        public static StrikeUnit? Sheet(UnitFileEntry unit) => StrikeUnit.FromJson(unit.StrikeUnitJson);

        public static void Save(UnitFileEntry unit, StrikeUnit sheet) => unit.StrikeUnitJson = sheet.ToJson();

        // #414: "Hero" or 3.6.0's "Unique Hero", which the book defines as counting as having Hero. A
        // named character in a campaign army used to be read as an ordinary unit, so it got none of the
        // campaign's hero handling. ForceOrgValidator.IsHero is the same question for force org.
        public static bool IsHero(UnitFileEntry unit) =>
            unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core core
                                       && Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(core.Name));

        /// <summary>Points the campaign army is worth right now (every unit's current cost).</summary>
        public static int ArmyValue(ArmyListFile army) => army.TotalPoints;

        /// <summary>
        /// Rolls on a six-face table until it lands on something the unit doesn't already have, as the
        /// books require ("keep re-rolling until you get a new trait you don't have yet"). Falls back to
        /// the first unowned entry if the dice never cooperate, so a test die that always returns the
        /// same face can't spin forever.
        /// </summary>
        private static string RollTrait(Func<int> d6, string[] faces, ICollection<string> owned)
        {
            for (int guard = 0; guard < 60; guard++)
            {
                string pick = CampaignTables.Entry(faces, d6());
                if (!owned.Contains(pick)) return pick;
            }
            return faces.FirstOrDefault(f => !owned.Contains(f)) ?? faces[^1];
        }

        public static void AddRule(UnitFileEntry unit, string rule)
        {
            if (!unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core c && c.Name == rule))
                unit.SpecialRules.Add(new SpecialRuleEntry_Core(rule));
        }

        private static void ChangeCost(UnitFileEntry unit, StrikeUnit sheet, int delta)
        {
            unit.PointCost = Math.Max(0, unit.PointCost + delta);
            sheet.CostChange += delta;
        }

        private static void AddTough(UnitFileEntry unit, int amount)
        {
            int i = unit.SpecialRules.FindIndex(r => r is SpecialRuleEntry_CoreNumeric { Name: "Tough" });
            if (i >= 0)
            {
                var tough = (SpecialRuleEntry_CoreNumeric)unit.SpecialRules[i];
                unit.SpecialRules[i] = new SpecialRuleEntry_CoreNumeric("Tough", tough.NumericValue + amount);
            }
            else unit.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("Tough", 1 + amount));
        }
    }
}
