using FDG.SaveLoad;
using FDG.StateMachine;

namespace FDG.Campaign
{
    /// <summary>How one strike team unit did in a match: knocked out (a casualty) or not, and what it knocked out.</summary>
    public readonly record struct StrikeUnitResult(string Id, bool Casualty, int Kills, int HeroKills);

    /// <summary>
    /// GF: Firefight Campaign rules (v3.5.1, pages 56-58) that run between games, on the strike team file:
    /// starting a team, checking casualties, earning XP, levelling up (traits, hero skill sets and skills), the
    /// win/lose rewards, and the choices the player makes afterwards. Dice come in as a function so the rolls can
    /// be tested. Upgrades and recruiting are not here yet.
    /// </summary>
    public static class StrikeTeamRules
    {
        public const int StartingPoints = 200;
        public const int MaxSavedAtStart = 40;
        public const int XpPerLevel = 5;
        public const int MaxXp = 30;
        public const int WinVp = 2, WinPoints = 20, LosePoints = 40;
        public const int RegularLevelCost = 5, HeroLevelCost = 15, InjuryCost = -5, TalentCost = 5;

        public const string Specialist = "Specialist";
        public const string FastLearner = "FastLearner";
        public const string SkillSetPick = "SkillSet:";

        public static readonly string[] Injuries = { "Dead", "Chest Wound", "Blinded Eye", "Arm Injury", "Traumatized", "Smashed Leg" };
        public static readonly string[] Talents = { "Natural Talent", "Motivated", "Crazed", "Bitter Rivalry", "Horrible Scars", "Toughened" };

        /// <summary>The skill-set roll: 1-2 Captain or Support, 3-4 Fighter or Shooter, 5-6 Pathfinder or Healer.</summary>
        public static readonly (string A, string B)[] SkillSetPairs =
            { ("Captain", "Support"), ("Fighter", "Shooter"), ("Pathfinder", "Healer") };

        /// <summary>Each skill set's three skills (rolled 1-2, 3-4, 5-6).</summary>
        public static readonly IReadOnlyDictionary<string, string[]> SkillSets = new Dictionary<string, string[]>
        {
            ["Captain"] = new[] { "Leader", "Instigator", "Tactician" },
            ["Support"] = new[] { "Vanguard", "Scavenger", "Mastermind" },
            ["Fighter"] = new[] { "Duellist", "Fanatic", "Berserker" },
            ["Shooter"] = new[] { "Hunter", "Destroyer", "Suppressor" },
            ["Pathfinder"] = new[] { "Runner", "Prowler", "Climber" },
            ["Healer"] = new[] { "Chemist", "Herbalist", "Warden" },
        };

        public static bool IsStrikeTeam(ArmyListFile army) => army.StrikeTeam != null;

        public static StrikeUnit? Sheet(UnitFileEntry unit) => StrikeUnit.FromJson(unit.StrikeUnitJson);

        public static void Save(UnitFileEntry unit, StrikeUnit sheet) => unit.StrikeUnitJson = sheet.ToJson();

        public static bool IsHero(UnitFileEntry unit) =>
            unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core { Name: "Hero" });

        // ── Starting a strike team ──────────────────────────────────────────────────────────────────

        /// <summary>Why this army can't start a strike team (over 200 pts, or more than one hero), or null.</summary>
        public static string? CannotStart(ArmyListFile army)
        {
            if (army.Units.Count == 0) return "The army has no units.";
            if (army.TotalPoints > StartingPoints) return $"The army is {army.TotalPoints} pts; a new strike team gets {StartingPoints} pts.";
            if (army.Units.Count(IsHero) > 1) return "A new strike team may only have one hero.";
            return null;
        }

        /// <summary>Turns an army into a new strike team: a sheet on every unit (with a unique name) and up to 40 of the
        /// unspent starting points saved.</summary>
        public static void Start(ArmyListFile army, string teamName, string teamFile)
        {
            army.Name = teamName;
            army.StrikeTeam = new StrikeTeamInfo
            {
                Name = teamName,
                BankedPoints = Math.Clamp(StartingPoints - army.TotalPoints, 0, MaxSavedAtStart),
            };
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UnitFileEntry unit in army.Units)
            {
                string name = unit.Name;
                for (int n = 2; !names.Add(name); n++) name = $"{unit.Name} {n}";
                unit.Name = name;
                Save(unit, new StrikeUnit { IsHero = IsHero(unit), TeamFile = teamFile });
            }
        }

        /// <summary>Points the strike team is worth (every unit's current cost).</summary>
        public static int TeamValue(ArmyListFile army) => army.TotalPoints;

        // ── After a match ───────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Steps 3 and 4 of the campaign plus the mission reward: casualty rolls, XP, level ups, then VP and points.
        /// Units that didn't play are left alone. Returns the log lines (also added to the team history).
        /// </summary>
        public static List<string> AfterGame(ArmyListFile team, IReadOnlyList<StrikeUnitResult> results, bool won, Func<int> d6)
        {
            var log = new List<string>();
            foreach (StrikeUnitResult result in results)
            {
                UnitFileEntry? unit = team.Units.FirstOrDefault(u => Sheet(u)?.Id == result.Id);
                if (unit == null || Sheet(unit) is not { } sheet) continue;
                sheet.Games++;
                sheet.Kills = sheet.HeroKills = 0;
                int xp = 0;

                if (result.Casualty)
                {
                    int roll = d6();
                    if (!sheet.IsHero)
                    {
                        if (roll == 1)
                        {
                            log.Add($"{unit.Name} was knocked out and rolled 1: dead, removed from the strike team.");
                            team.Units.Remove(unit);
                            continue;
                        }
                        if (roll == 6) { xp++; log.Add($"{unit.Name} was knocked out and rolled 6: Natural Talent, +1 XP."); }
                        else log.Add($"{unit.Name} was knocked out and rolled {roll}: recovered.");
                    }
                    else if (roll == 1)
                    {
                        string injury = RollUnique(d6, Injuries, sheet.Injuries);
                        if (injury == "Dead")
                        {
                            log.Add($"{unit.Name} was knocked out: rolled 1, then Dead on the injury roll - removed from the strike team.");
                            team.Units.Remove(unit);
                            continue;
                        }
                        sheet.Injuries.Add(injury);
                        AddRule(unit, FDG.Rules.Dispatch.CampaignRuleCatalog.InjuryPrefix + injury);
                        ChangeCost(unit, sheet, InjuryCost);
                        log.Add($"{unit.Name} was knocked out: rolled 1, injury {injury} (-5 pts).");
                    }
                    else if (roll == 6)
                    {
                        string talent = RollUnique(d6, Talents, sheet.Talents);
                        sheet.Talents.Add(talent);
                        AddRule(unit, FDG.Rules.Dispatch.CampaignRuleCatalog.TalentPrefix + talent);
                        if (talent == "Natural Talent") xp++;
                        if (talent == "Toughened") AddTough(unit, 1);
                        ChangeCost(unit, sheet, TalentCost);
                        log.Add($"{unit.Name} was knocked out: rolled 6, talent {talent} (+5 pts).");
                    }
                    else log.Add($"{unit.Name} was knocked out and rolled {roll}: recovered.");
                }
                else xp++;

                xp += result.Kills + 2 * result.HeroKills;
                if (xp > 0) log.AddRange(AddXp(unit, sheet, xp, d6));
                Save(unit, sheet);
            }

            StrikeTeamInfo info = team.StrikeTeam ??= new StrikeTeamInfo { Name = team.Name };
            info.Matches++;
            if (won) { info.Vp += WinVp; info.BankedPoints += WinPoints; log.Add($"Won the mission: +{WinVp} VP, +{WinPoints} pts."); }
            else { info.BankedPoints += LosePoints; log.Add($"Lost the mission: +{LosePoints} pts."); }
            info.History.Add($"Match {info.Matches}: {(won ? "won" : "lost")} - " + string.Join(" ", log));
            return log;
        }

        /// <summary>Adds XP (capped at 30) and levels the unit up once for every 5 XP crossed.</summary>
        public static List<string> AddXp(UnitFileEntry unit, StrikeUnit sheet, int amount, Func<int> d6)
        {
            var log = new List<string>();
            int before = sheet.Xp;
            sheet.Xp = Math.Min(MaxXp, before + amount);
            log.Add($"{unit.Name} +{sheet.Xp - before} XP ({sheet.Xp} XP).");
            int levels = sheet.Xp / XpPerLevel - before / XpPerLevel;
            for (int i = 0; i < levels; i++) log.AddRange(LevelUp(unit, sheet, d6));
            return log;
        }

        /// <summary>One level up: the cost goes up (5 pts, heroes 15), a trait is rolled, and a hero also gets a skill.</summary>
        public static List<string> LevelUp(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6)
        {
            var log = new List<string>();
            ChangeCost(unit, sheet, sheet.IsHero ? HeroLevelCost : RegularLevelCost);
            log.Add($"{unit.Name} levels up (+{(sheet.IsHero ? HeroLevelCost : RegularLevelCost)} pts).");
            log.Add(RollTrait(unit, sheet, d6));
            if (sheet.IsHero) log.Add(RollSkill(unit, sheet, d6));
            return log;
        }

        private static string RollTrait(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6)
        {
            for (int guard = 0; guard < 60; guard++)
            {
                switch (d6())
                {
                    case 1 when !sheet.Traits.Contains(HordeExperience.Agile): return GainTrait(unit, sheet, HordeExperience.Agile);
                    case 2 when !sheet.Traits.Contains(HordeExperience.Headstrong): return GainTrait(unit, sheet, HordeExperience.Headstrong);
                    case 3 when SpecialistLeft(sheet).Count > 0:
                        if (SpecialistLeft(sheet).Count == 1) return GainTrait(unit, sheet, SpecialistLeft(sheet)[0]);
                        sheet.Pending.Add(Specialist);
                        return $"{unit.Name} rolled Specialist: pick melee or shooting on the strike team screen.";
                    case 4 when !sheet.Traits.Contains(HordeExperience.Resilient): return GainTrait(unit, sheet, HordeExperience.Resilient);
                    case 5 when !sheet.Traits.Contains(HordeExperience.Elite): return GainTrait(unit, sheet, HordeExperience.Elite);
                    case 6 when TraitsLeft(sheet).Count > 0:
                        sheet.Pending.Add(FastLearner);
                        return $"{unit.Name} rolled Fast Learner: pick any other trait on the strike team screen.";
                }
                if (TraitsLeft(sheet).Count == 0) return $"{unit.Name} already has every trait.";
            }
            return $"{unit.Name}: no new trait.";
        }

        private static string RollSkill(UnitFileEntry unit, StrikeUnit sheet, Func<int> d6)
        {
            string? set = sheet.SkillSets.LastOrDefault(s => SkillSets[s].Any(k => !sheet.Skills.Contains(k)));
            if (set == null)
            {
                // No skill set yet, or every skill of its sets owned: roll a (new) pair of skill sets to pick from.
                var pairs = SkillSetPairs.Where(p => !sheet.SkillSets.Contains(p.A) || !sheet.SkillSets.Contains(p.B)).ToList();
                if (pairs.Count == 0) return $"{unit.Name} already knows every skill.";
                (string a, string b) = SkillSetPairs[(d6() - 1) / 2];
                for (int guard = 0; guard < 30 && sheet.SkillSets.Contains(a) && sheet.SkillSets.Contains(b); guard++)
                    (a, b) = SkillSetPairs[(d6() - 1) / 2];
                if (sheet.SkillSets.Contains(a)) { sheet.SkillSets.Add(b); return GainSkill(unit, sheet, b, d6, $"skill set {b}"); }
                if (sheet.SkillSets.Contains(b)) { sheet.SkillSets.Add(a); return GainSkill(unit, sheet, a, d6, $"skill set {a}"); }
                sheet.Pending.Add($"{SkillSetPick}{a}|{b}");
                return $"{unit.Name} rolled the {a} or {b} skill sets: pick one on the strike team screen (its skill is rolled then).";
            }
            return GainSkill(unit, sheet, set, d6, null);
        }

        private static string GainSkill(UnitFileEntry unit, StrikeUnit sheet, string set, Func<int> d6, string? prefix)
        {
            string[] skills = SkillSets[set];
            string skill = RollUnique(d6, new[] { skills[0], skills[0], skills[1], skills[1], skills[2], skills[2] }, sheet.Skills);
            sheet.Skills.Add(skill);
            AddRule(unit, FDG.Rules.Dispatch.CampaignRuleCatalog.SkillPrefix + skill);
            if (skill == "Fanatic") AddMeleeAttack(unit);
            return $"{unit.Name} " + (prefix != null ? $"takes {prefix} and " : "") + $"learns {skill}.";
        }

        private static string GainTrait(UnitFileEntry unit, StrikeUnit sheet, string trait)
        {
            sheet.Traits.Add(trait);
            AddRule(unit, trait);
            return $"{unit.Name} gains {trait.Replace("Trait: ", "")}.";
        }

        public static List<string> SpecialistLeft(StrikeUnit sheet) =>
            new[] { HordeExperience.MeleeSpecialist, HordeExperience.ShootingSpecialist }.Where(t => !sheet.Traits.Contains(t)).ToList();

        /// <summary>The traits a Fast Learner may still pick.</summary>
        public static List<string> TraitsLeft(StrikeUnit sheet) =>
            new[] { HordeExperience.Agile, HordeExperience.Headstrong, HordeExperience.MeleeSpecialist,
                    HordeExperience.ShootingSpecialist, HordeExperience.Resilient, HordeExperience.Elite }
                .Where(t => !sheet.Traits.Contains(t)).ToList();

        /// <summary>The options of a pending choice.</summary>
        public static List<string> Options(StrikeUnit sheet, string pending) => pending switch
        {
            Specialist => SpecialistLeft(sheet),
            FastLearner => TraitsLeft(sheet),
            _ when pending.StartsWith(SkillSetPick) => pending[SkillSetPick.Length..].Split('|').ToList(),
            _ => new List<string>(),
        };

        /// <summary>Makes a pending choice: a trait (Specialist / Fast Learner) or a skill set (whose skill is rolled now).</summary>
        public static string Choose(UnitFileEntry unit, string pending, string choice, Func<int> d6)
        {
            if (Sheet(unit) is not { } sheet || !sheet.Pending.Contains(pending)) return "Nothing to choose.";
            if (!Options(sheet, pending).Contains(choice)) return $"{choice} can't be picked.";
            sheet.Pending.Remove(pending);
            string line = pending.StartsWith(SkillSetPick)
                ? AddSet(unit, sheet, choice, d6)
                : GainTrait(unit, sheet, choice);
            Save(unit, sheet);
            return line;
        }

        private static string AddSet(UnitFileEntry unit, StrikeUnit sheet, string set, Func<int> d6)
        {
            sheet.SkillSets.Add(set);
            return GainSkill(unit, sheet, set, d6, $"skill set {set}");
        }

        // ── File edits ──────────────────────────────────────────────────────────────────────────────

        private static string RollUnique(Func<int> d6, string[] table, ICollection<string> owned)
        {
            for (int guard = 0; guard < 60; guard++)
            {
                string pick = table[Math.Clamp(d6(), 1, 6) - 1];
                if (!owned.Contains(pick)) return pick;
            }
            return table.FirstOrDefault(t => !owned.Contains(t)) ?? table[^1];
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

        private static void AddMeleeAttack(UnitFileEntry unit)
        {
            WeaponFileEntry? melee = unit.Weapons.Where(w => w.RangeInches == 0).OrderByDescending(w => w.Attacks).FirstOrDefault();
            if (melee != null) melee.Attacks++;
        }
    }
}
