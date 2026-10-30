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
        public static void Start(ArmyListFile army, string armyName, string armyFile, CampaignNumbers n,
                                 EGameSystem system, ECampaignType type = ECampaignType.Endless)
        {
            army.Name = armyName;
            army.StrikeTeam = new StrikeTeamInfo
            {
                Name = armyName,
                BankedPoints = Math.Max(0, n.Treasury - army.TotalPoints),
                GameSystem = system,
                CampaignType = type,
            };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnitFileEntry unit in army.Units)
            {
                string name = unit.Name;
                for (int i = 2; !names.Add(name); i++) name = $"{unit.Name} {i}";
                unit.Name = name;
                Save(unit, new StrikeUnit { IsHero = IsHero(unit), TeamFile = armyFile });
            }
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
            foreach (CampaignUnitResult result in results)
            {
                UnitFileEntry? unit = army.Units.FirstOrDefault(u => Sheet(u)?.Id == result.Id);
                if (unit == null || Sheet(unit) is not { } sheet) continue;
                sheet.Games++;
                sheet.Kills = sheet.HeroKills = 0;
                int xp = 0;

                if (result.Casualty)
                {
                    if (!ResolveCasualty(army, unit, sheet, n, d6, log, ref xp)) continue;  // dead, removed
                }
                else xp++;  // +1 XP: not a casualty

                xp += result.Kills + 2 * result.HeroKills;
                if (xp > 0) log.AddRange(AddXp(unit, sheet, xp, n, d6));
                Save(unit, sheet);
            }

            StrikeTeamInfo info = army.StrikeTeam ??= new StrikeTeamInfo { Name = army.Name };
            info.Matches++;
            if (won)
            {
                info.Vp += CampaignNumbers.WinVp;
                info.BankedPoints += n.WinPoints;
                log.Add($"Won the mission: +{CampaignNumbers.WinVp} VP, +{n.WinPoints} pts.");
            }
            else
            {
                info.BankedPoints += n.LosePoints;
                log.Add($"Lost the mission: +{n.LosePoints} pts.");
            }
            info.History.Add($"Match {info.Matches}: {(won ? "won" : "lost")} - " + string.Join(" ", log));
            return log;
        }

        /// <summary>
        /// One casualty roll. Returns false if the unit died and was removed from the army, true if it
        /// is still there. <paramref name="xp"/> gains 1 for a Natural Talent result.
        /// </summary>
        private static bool ResolveCasualty(ArmyListFile army, UnitFileEntry unit, StrikeUnit sheet,
                                            CampaignNumbers n, Func<int> d6, List<string> log, ref int xp)
        {
            int roll = d6();
            if (!sheet.IsHero)
            {
                switch (roll)
                {
                    case 1:
                        log.Add($"{unit.Name} was a casualty and rolled 1: dead, removed from the army.");
                        army.Units.Remove(unit);
                        return false;
                    case 6:
                        xp++;
                        log.Add($"{unit.Name} was a casualty and rolled 6: Natural Talent, +1 XP.");
                        return true;
                    default:
                        log.Add($"{unit.Name} was a casualty and rolled {roll}: recovered.");
                        return true;
                }
            }

            if (roll == 1)
            {
                string injury = RollTrait(d6, CampaignTables.Injuries, sheet.Injuries);
                if (injury == "Dead")
                {
                    log.Add($"{unit.Name} was a casualty: rolled 1, then Dead on the injury roll - removed from the army.");
                    army.Units.Remove(unit);
                    return false;
                }
                if (injury == "Lucky Break")
                {
                    sheet.Pending.Add(CampaignTables.LuckyBreak);
                    ChangeCost(unit, sheet, -n.HeroInjuryTalentCost);
                    log.Add($"{unit.Name} was a casualty: rolled 1, then Lucky Break - pick any other injury trait on the campaign screen (-{n.HeroInjuryTalentCost} pts).");
                    return true;
                }
                GainInjury(unit, sheet, injury);
                ChangeCost(unit, sheet, -n.HeroInjuryTalentCost);
                log.Add($"{unit.Name} was a casualty: rolled 1, injury {injury} (-{n.HeroInjuryTalentCost} pts).");
                return true;
            }

            if (roll == 6)
            {
                string talent = RollTrait(d6, CampaignTables.Talents, sheet.Talents);
                if (talent == "Learned Lesson")
                {
                    sheet.Pending.Add(CampaignTables.LearnedLesson);
                    ChangeCost(unit, sheet, n.HeroInjuryTalentCost);
                    log.Add($"{unit.Name} was a casualty: rolled 6, then Learned Lesson - pick any other talent trait on the campaign screen (+{n.HeroInjuryTalentCost} pts).");
                    return true;
                }
                GainTalent(unit, sheet, talent, ref xp);
                ChangeCost(unit, sheet, n.HeroInjuryTalentCost);
                log.Add($"{unit.Name} was a casualty: rolled 6, talent {talent} (+{n.HeroInjuryTalentCost} pts).");
                return true;
            }

            log.Add($"{unit.Name} was a casualty and rolled {roll}: recovered.");
            return true;
        }

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

        public static bool IsHero(UnitFileEntry unit) =>
            unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });

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
