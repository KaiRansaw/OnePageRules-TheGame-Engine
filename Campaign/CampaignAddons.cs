using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>
    /// Wanted posters (homebrew, Kai 2026-11-27, inspired by Necromunda; house rule B29). The moment a campaign unit
    /// destroys one of the other army's units it is WANTED, live in the battle. Its price is the book's special-objective
    /// bonus for every unit it has destroyed. Destroying it - in the same battle or a later one - claims the price for the
    /// army that did it, paid when the mission is recorded. A wanted unit that dies with nobody to blame (Dangerous
    /// terrain, a rout) pays nobody and its poster comes down.
    /// </summary>
    public static class CampaignWanted
    {
        /// <summary>Both are campaign units of two different campaign armies.</summary>
        private static bool Rivals(StrikeUnit? a, StrikeUnit? b) =>
            a?.TeamFile is { Length: > 0 } fa && b?.TeamFile is { Length: > 0 } fb
            && !string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase);

        /// <summary>The killer destroyed <paramref name="victimTitle"/>: its poster goes up, or its price rises.</summary>
        public static bool Post(StrikeUnit killer, StrikeUnit victim, string victimTitle)
        {
            if (!Rivals(killer, victim)) return false;
            killer.WantedKills++;
            killer.WantedVictims.Add(victimTitle);
            return true;
        }

        /// <summary>A WANTED unit was destroyed by <paramref name="killer"/>: the bounty is claimed for the killer's army.</summary>
        public static bool Claim(StrikeUnit dead, StrikeUnit? killer)
        {
            if (dead.WantedKills <= 0 || !Rivals(dead, killer)) return false;
            dead.BountyClaimedBy = killer!.TeamFile!;
            dead.BountyKills = dead.WantedKills;
            dead.WantedKills = 0;
            dead.WantedVictims.Clear();
            return true;
        }

        /// <summary>The price on a unit's head: the book's special-objective bonus for every unit it destroyed.</summary>
        public static int Price(int kills, CampaignNumbers n) => Math.Max(0, kills) * n.SpecialObjectiveBonus;

        /// <summary>The poster's line: <c>WANTED: Crimson Tank "Bloodfist" - 150 pts (destroyed Vargas Squad, Scouts)</c>.</summary>
        public static string Poster(string title, StrikeUnit sheet, CampaignNumbers n) =>
            $"WANTED: {title} - {Price(sheet.WantedKills, n)} pts" +
            (sheet.WantedVictims.Count > 0 ? $" (destroyed {string.Join(", ", sheet.WantedVictims)})" : "");

        /// <summary>
        /// Folds the battle's posters into the unit's sheet on file (called by <see cref="CampaignRules.AfterGame"/>):
        /// the live price, or the poster coming down when the unit died with nobody to collect.
        /// </summary>
        internal static void Record(UnitFileEntry unit, StrikeUnit sheet, CampaignUnitResult result, CampaignNumbers n, List<string> log)
        {
            if (result.Live is not { } live) return;
            int before = sheet.WantedKills;
            sheet.WantedKills = live.WantedKills;
            sheet.WantedVictims = live.WantedVictims.ToList();
            string title = CampaignVeterans.Title(unit.Name, sheet.Nickname);
            if (live.BountyClaimedBy.Length > 0)
            {
                log.Add($"{title} was WANTED and was destroyed - the enemy collects {Price(live.BountyKills, n)} pts.");
                return;
            }
            if (result.Casualty && sheet.WantedKills > 0)
            {
                log.Add($"{title} fell with nobody to collect its bounty - the poster comes down.");
                sheet.WantedKills = 0;
                sheet.WantedVictims.Clear();
                return;
            }
            if (sheet.WantedKills > before)
                log.Add(before == 0
                    ? $"{title} is now WANTED by the enemy: {Price(sheet.WantedKills, n)} pts."
                    : $"The price on {title} rises to {Price(sheet.WantedKills, n)} pts.");
        }

        /// <summary>Every WANTED unit of an army: (title, sheet).</summary>
        public static IEnumerable<(string Title, StrikeUnit Sheet)> Posters(ArmyListFile army) =>
            army.Units.Select(u => (u, s: CampaignRules.Sheet(u)))
                .Where(x => x.s is { WantedKills: > 0 })
                .Select(x => (CampaignVeterans.Title(x.u.Name, x.s!.Nickname), x.s!));
    }

    /// <summary>
    /// Exploration (homebrew, Kai 2026-11-27, inspired by Mordheim; house rule B28). After every campaign mission each army
    /// searches the field: one roll, one more if it won, one more for each of its Heroes still standing. The table's
    /// results are the reward entries of the campaign books' own Random Events table (61-66), with a Relic Map on a rare
    /// find. Rewards only.
    /// </summary>
    public static class CampaignExploration
    {
        /// <summary>How many rolls: 1, +1 for the winner, +1 for each Hero alive at the end.</summary>
        public static int Rolls(bool won, int heroesStanding) => 1 + (won ? 1 : 0) + Math.Max(0, heroesStanding);

        /// <summary>What a die finds (the rare find's second die is <paramref name="second"/>).</summary>
        public enum EFind { Credits, InjectorPouch, TreasureHolopad, KitBox, GrenadeBag, RelicMap, GodlyBlessing }

        public static EFind Find(int die, int second) => Math.Clamp(die, 1, 6) switch
        {
            1 => EFind.Credits,
            2 => EFind.InjectorPouch,
            3 => EFind.TreasureHolopad,
            4 => EFind.KitBox,
            5 => EFind.GrenadeBag,
            _ => Math.Clamp(second, 1, 6) <= 4 ? EFind.RelicMap : EFind.GodlyBlessing,
        };

        /// <summary>The book's name for a find (each game's own names; the Relic Map is ours).</summary>
        public static string Name(EFind find, EGameSystem system)
        {
            int code = find switch
            {
                EFind.Credits => 61, EFind.InjectorPouch => 62, EFind.GrenadeBag => 63, EFind.TreasureHolopad => 64,
                EFind.KitBox => 65, EFind.GodlyBlessing => 66, _ => 0,
            };
            if (code == 0) return "Relic Map";
            if (CampaignEventNames.IsFantasy(system)) return CampaignEventNames.FantasyName(code);
            return code switch
            {
                61 => "Credits Stash", 62 => "Injector Pouch", 63 => "Grenade Bag", 64 => "Treasure Holopad",
                65 => "Kit Box", _ => "Godly Blessing",
            };
        }

        /// <summary>
        /// Searches the field for one army and pays out what it finds. <paramref name="searchers"/> are the units that
        /// played and are still in the army (XP finds go to one of them at random). Returns the log lines.
        /// </summary>
        public static List<string> Explore(ArmyListFile army, int rolls, IReadOnlyList<UnitFileEntry> searchers,
                                           CampaignNumbers n, EGameSystem system, Func<int> d6)
        {
            var log = new List<string>();
            StrikeTeamInfo info = army.StrikeTeam ??= new StrikeTeamInfo { Name = army.Name };
            for (int i = 0; i < rolls; i++)
            {
                int die = Math.Clamp(d6(), 1, 6);
                int second = die == 6 ? Math.Clamp(d6(), 1, 6) : 0;
                EFind find = Find(die, second);
                string name = Name(find, system);
                string head = $"Searching the field ({die}{(second > 0 ? $", then {second}" : "")}): {name}";
                switch (find)
                {
                    case EFind.Credits:
                        info.BankedPoints += n.EventCredits;
                        log.Add($"{head} - +{n.EventCredits} pts.");
                        break;
                    case EFind.TreasureHolopad:
                    {
                        int pts = D3(d6) * n.EventCredits;
                        info.BankedPoints += pts;
                        log.Add($"{head} - +{pts} pts.");
                        break;
                    }
                    case EFind.GrenadeBag:
                        info.Vp++;
                        log.Add($"{head} - +1 VP.");
                        break;
                    case EFind.GodlyBlessing:
                    {
                        int vp = D3(d6);
                        info.Vp += vp;
                        log.Add($"{head} - +{vp} VP.");
                        break;
                    }
                    case EFind.RelicMap:
                        info.RelicMaps++;
                        log.Add($"{head} - a Hero can go on a relic quest with it (Camp).");
                        break;
                    case EFind.InjectorPouch:
                    case EFind.KitBox:
                    {
                        var alive = searchers.Where(u => army.Units.Contains(u) && CampaignRules.Sheet(u) != null).ToList();
                        if (alive.Count == 0) { log.Add($"{head} - nobody left to use it."); break; }
                        UnitFileEntry unit = alive[(d6() - 1 + alive.Count * 6) % alive.Count];
                        StrikeUnit sheet = CampaignRules.Sheet(unit)!;
                        int xp = find == EFind.KitBox ? D3(d6) : 1;
                        log.Add($"{head} - {unit.Name} gets {xp} XP.");
                        log.AddRange(CampaignRules.AddXp(unit, sheet, xp, n, d6));
                        CampaignRules.Save(unit, sheet);
                        break;
                    }
                }
            }
            return log;
        }

        private static int D3(Func<int> d6) => (Math.Clamp(d6(), 1, 6) + 1) / 2;
    }

    /// <summary>
    /// Relic quests (homebrew, Kai 2026-11-27, inspired by Total War; house rule B28). A Relic Map found exploring sends
    /// one Hero on a quest: the next mission is the Advanced Rulebook's Relic Hunt, and winning it with that Hero still
    /// alive wins the Hero a relic for the rest of the campaign, picked by the book's Limited method (a die for the group,
    /// then any item in it). The Hero costs the book's Hero level-up price more. Failing keeps the map.
    /// </summary>
    public static class CampaignRelicQuest
    {
        public const string PickRelic = "Relic:";
        public const string PickWeapon = "RelicWeapon:";
        public const string PickRanger = "RelicRanger:";

        /// <summary>Why this Hero can't go on a quest now, or null.</summary>
        public static string? CannotQuest(ArmyListFile army, UnitFileEntry hero)
        {
            if (army.StrikeTeam is not { } info) return "That isn't a campaign army.";
            if (info.RelicMaps <= 0) return "The army has no Relic Map.";
            if (CampaignRules.Sheet(hero) is not { IsHero: true } sheet) return "Only a Hero can go on a quest.";
            if (sheet.MissionsOut > 0) return $"{hero.Name} is recovering.";
            if (sheet.Relic.Length > 0) return $"{hero.Name} already carries the {sheet.Relic}.";
            return null;
        }

        /// <summary>Sends a Hero on the quest (the next mission). Choosing again replaces the pick; null = cancel.</summary>
        public static string SetQuest(ArmyListFile army, UnitFileEntry? hero)
        {
            if (army.StrikeTeam is not { } info) return "That isn't a campaign army.";
            if (hero == null) { info.QuestHeroId = ""; return "No quest next mission."; }
            if (CannotQuest(army, hero) is { } why) return why;
            info.QuestHeroId = CampaignRules.Sheet(hero)!.Id;
            return $"{hero.Name} goes on a relic quest: the next mission is a Relic Hunt - win it with {hero.Name} alive.";
        }

        /// <summary>The Hero on a quest next mission, or null.</summary>
        public static UnitFileEntry? QuestHero(ArmyListFile army) =>
            army.StrikeTeam is { QuestHeroId.Length: > 0 } info
                ? army.Units.FirstOrDefault(u => CampaignRules.Sheet(u)?.Id == info.QuestHeroId)
                : null;

        /// <summary>
        /// The quest's outcome, once the mission is recorded. On success the map is spent, the group is rolled and the
        /// Hero gets a choice waiting on the Camp (any item of that group the army doesn't carry yet). Returns the log
        /// lines (empty when there was no quest).
        /// </summary>
        public static List<string> Resolve(ArmyListFile army, bool won, bool heroAlive, CampaignNumbers n, EGameSystem system, Func<int> d6)
        {
            var log = new List<string>();
            if (army.StrikeTeam is not { QuestHeroId.Length: > 0 } info) return log;
            UnitFileEntry? hero = army.Units.FirstOrDefault(u => CampaignRules.Sheet(u)?.Id == info.QuestHeroId);
            info.QuestHeroId = "";
            if (hero == null || CampaignRules.Sheet(hero) is not { } sheet)
            {
                log.Add("The relic quest failed: the Hero did not come back. The Relic Map is kept.");
                return log;
            }
            if (!won || !heroAlive)
            {
                log.Add($"{hero.Name}'s relic quest failed ({(!won ? "the mission was lost" : $"{hero.Name} fell")}). The Relic Map is kept for another try.");
                return log;
            }
            info.RelicMaps = Math.Max(0, info.RelicMaps - 1);
            var defs = RelicWargear.Table(system);
            var carried = new HashSet<string>(army.Units.Select(u => CampaignRules.Sheet(u)?.Relic ?? "").Where(r => r.Length > 0));
            int group = -1, die = 0;
            for (int guard = 0; guard < 50 && group < 0; guard++)
            {
                die = Math.Clamp(d6(), 1, 6);
                if (defs.Any(d => d.Category == die - 1 && !carried.Contains(d.Name))) group = die - 1;
            }
            if (group < 0)
            {
                log.Add($"{hero.Name}'s quest succeeded, but the army already carries every relic.");
                return log;
            }
            var options = defs.Where(d => d.Category == group && !carried.Contains(d.Name)).Select(d => d.Name).ToList();
            sheet.Pending.Add($"{PickRelic}{n.HeroLevelCost}:{string.Join("|", options)}");
            CampaignRules.Save(hero, sheet);
            log.Add($"{hero.Name}'s relic quest succeeded! Rolled {die}: {RelicWargear.Categories[group]} - pick the relic on the Camp "
                    + $"(+{n.HeroLevelCost} pts to {hero.Name}).");
            return log;
        }

        // -- the waiting choices (CampaignRules.Options / Choose) -----------------------------------

        internal static bool IsRelicChoice(string pending) =>
            pending.StartsWith(PickRelic) || pending.StartsWith(PickWeapon) || pending.StartsWith(PickRanger);

        internal static List<string> Options(string pending)
        {
            string rest = pending.StartsWith(PickRelic) ? pending[PickRelic.Length..]
                : pending.StartsWith(PickWeapon) ? pending[PickWeapon.Length..] : pending[PickRanger.Length..];
            if (pending.StartsWith(PickRelic) && rest.IndexOf(':') is int colon and >= 0) rest = rest[(colon + 1)..];
            return rest.Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        private static readonly string[] WeaponRelics =
            { RelicWargear.Bloodshed, RelicWargear.Obsidian, RelicWargear.Kingslayer, RelicWargear.Biting, RelicWargear.Spellthieving };

        internal static string Choose(UnitFileEntry unit, StrikeUnit sheet, string pending, string choice, Action<int> changeCost)
        {
            if (pending.StartsWith(PickRelic))
            {
                string rest = pending[PickRelic.Length..];
                int cost = int.TryParse(rest[..Math.Max(0, rest.IndexOf(':'))], out int c) ? c : 0;
                sheet.Relic = choice;
                changeCost(cost);
                var weapons = (choice == RelicWargear.Fencer
                        ? unit.Weapons.Where(w => w.RangeInches <= 0) : WeaponRelics.Contains(choice) ? unit.Weapons : Enumerable.Empty<WeaponFileEntry>())
                    .Select(w => w.Name).Distinct().ToList();
                if (weapons.Count > 1) sheet.Pending.Add(PickWeapon + string.Join("|", weapons));
                else if (weapons.Count == 1) sheet.RelicWeapon = weapons[0];
                if (choice == RelicWargear.Ranger) sheet.Pending.Add($"{PickRanger}{RelicWargear.Scout}|{RelicWargear.Ambush}");
                return $"{unit.Name} takes the {choice} for the rest of the campaign (+{cost} pts).";
            }
            if (pending.StartsWith(PickWeapon))
            {
                sheet.RelicWeapon = choice;
                return $"{unit.Name}'s {sheet.Relic} goes on its {choice}.";
            }
            sheet.RelicChoice = choice;
            return $"{unit.Name}'s {sheet.Relic}: {choice}.";
        }
    }
}
