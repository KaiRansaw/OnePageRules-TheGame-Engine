using FDG.SaveLoad;
using FDG.StateMachine;

namespace FDG.Campaign
{
    /// <summary>
    /// One unit on an army's Memorial page: who it was and what it did, kept after it leaves the army.
    /// Homebrew (Kai, 2026-11-27; house rule B26) - no OPR book has a memorial.
    /// </summary>
    public sealed class FallenUnit
    {
        public string Name { get; set; } = "";
        public string Nickname { get; set; } = "";
        /// <summary>What it is in its book ("Battle Tank").</summary>
        public string UnitType { get; set; } = "";
        public bool IsHero { get; set; }
        public int Level { get; set; }
        public int Xp { get; set; }
        /// <summary>Its traits, injuries, talents and skills, as shown on the campaign screen.</summary>
        public List<string> Honours { get; set; } = new();
        /// <summary>Missions it played, the one it fell in included.</summary>
        public int Missions { get; set; }
        public int Kills { get; set; }
        public int HeroKills { get; set; }
        /// <summary>The mission it fell in, counting from 1.</summary>
        public int Mission { get; set; }
        /// <summary>The enemy unit that destroyed it, or empty when nothing did (a rout, Dangerous terrain).</summary>
        public string FellTo { get; set; } = "";
        /// <summary>The army it fought against in that mission, or empty for a one-off opponent.</summary>
        public string Against { get; set; } = "";
        /// <summary>The army it had been salvaged from, or empty.</summary>
        public string SalvagedFrom { get; set; } = "";
        /// <summary>The campaign relic it carried (homebrew relic quests, house rule B28), or empty.</summary>
        public string Relic { get; set; } = "";

        /// <summary>Its name with the nickname, as the memorial shows it.</summary>
        public string Title => CampaignVeterans.Title(Name, Nickname);
    }

    /// <summary>
    /// Named veterans (homebrew, Kai 2026-11-27, inspired by XCOM; house rule B26): a campaign unit's service
    /// record, the nickname the game suggests when it levels up, and the Memorial page for units that die.
    /// Nothing here changes a rule - it is all record keeping, so none of it touches play.
    /// </summary>
    public static class CampaignVeterans
    {
        /// <summary>
        /// The dice for nickname suggestions. Deliberately NOT the campaign's d6: a suggestion is flavour, and
        /// taking rolls from the campaign's dice would shift every casualty and trait roll after a level-up.
        /// Tests may replace it with a seeded one.
        /// </summary>
        public static Random Rng { get; set; } = new();

        /// <summary>The longest nickname kept. Long enough for "the Unbreakable Wall", short enough for a card.</summary>
        public const int MaxNicknameLength = 28;

        /// <summary>A unit's name as shown with its nickname: <c>Vargas Squad "the Butcher"</c>.</summary>
        public static string Title(string name, string? nickname) =>
            string.IsNullOrWhiteSpace(nickname) ? name : $"{name} \"{nickname.Trim()}\"";

        /// <summary>The name to show for a campaign unit, nickname included.</summary>
        public static string Title(UnitFileEntry unit) => Title(unit.Name, CampaignRules.Sheet(unit)?.Nickname);

        /// <summary>
        /// Sets (or clears, with null or blank) a unit's nickname. Game text is ASCII-only, so anything else
        /// is dropped; quotes are dropped too, since the title wraps the nickname in them. Answers the
        /// pending suggestion either way.
        /// </summary>
        public static string SetNickname(UnitFileEntry unit, string? nickname)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) return "That unit has no campaign sheet.";
            string clean = Clean(nickname);
            sheet.Nickname = clean;
            sheet.NicknameSuggestion = "";
            CampaignRules.Save(unit, sheet);
            return clean.Length == 0 ? $"{unit.Name} has no nickname." : $"{unit.Name} is now known as \"{clean}\".";
        }

        /// <summary>Keeps the current nickname and drops the suggestion.</summary>
        public static void DismissSuggestion(UnitFileEntry unit)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) return;
            sheet.NicknameSuggestion = "";
            CampaignRules.Save(unit, sheet);
        }

        /// <summary>Rolls a fresh suggestion for the unit (the "Re-roll" button) and returns it.</summary>
        public static string Reroll(UnitFileEntry unit, Random rng)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) return "";
            string fresh = Suggest(sheet, rng, avoid: sheet.NicknameSuggestion);
            sheet.NicknameSuggestion = fresh;
            CampaignRules.Save(unit, sheet);
            return fresh;
        }

        internal static string Clean(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            var chars = text.Where(c => c >= ' ' && c <= '~' && c != '"').ToArray();
            string s = string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
            return s.Length > MaxNicknameLength ? s[..MaxNicknameLength].TrimEnd() : s;
        }

        // -- Suggestions ---------------------------------------------------------------------------

        /// <summary>
        /// What the unit has done that a nickname can be about, strongest first. Each theme is a list of
        /// nicknames, picked from at random. The themes come from the sheet only: traits it earned, kills,
        /// Heroes killed, missions survived and casualties come back from.
        /// </summary>
        internal static IEnumerable<string[]> Themes(StrikeUnit sheet)
        {
            int kills = sheet.CareerKills + sheet.Kills;
            int heroKills = sheet.CareerHeroKills + sheet.HeroKills;
            if (heroKills >= 2) yield return KingslayerNames;
            if (sheet.Traits.Contains(HordeExperience.MeleeSpecialist) && kills >= 2) yield return MeleeNames;
            if (sheet.Traits.Contains(HordeExperience.ShootingSpecialist) && kills >= 2) yield return ShootingNames;
            if (sheet.TimesRecovered >= 2) yield return UnkillableNames;
            if (heroKills == 1) yield return KingslayerNames;
            if (kills >= 6) yield return SlayerNames;
            if (sheet.Traits.Contains(HordeExperience.Resilient)) yield return ToughNames;
            if (sheet.Traits.Contains(HordeExperience.Agile)) yield return SwiftNames;
            if (sheet.Traits.Contains(HordeExperience.Headstrong)) yield return SteadfastNames;
            if (sheet.Traits.Contains(HordeExperience.Elite)) yield return EliteNames;
            if (sheet.Traits.Contains(HordeExperience.MeleeSpecialist)) yield return MeleeNames;
            if (sheet.Traits.Contains(HordeExperience.ShootingSpecialist)) yield return ShootingNames;
            if (sheet.MissionsSurvived >= 4) yield return OldGuardNames;
            if (sheet.Injuries.Count > 0) yield return ScarredNames;
            yield return PlainNames;
        }

        /// <summary>
        /// A nickname for the unit: from its strongest theme, mostly, with a one-in-three chance of the next one
        /// so a re-roll can land somewhere new. Never <paramref name="avoid"/> or the current nickname when there
        /// is anything else to give.
        /// </summary>
        public static string Suggest(StrikeUnit sheet, Random rng, string? avoid = null)
        {
            var themes = Themes(sheet).Take(3).ToList();
            var pool = new List<string>();
            int pick = themes.Count > 1 && rng.Next(3) == 0 ? 1 : 0;
            pool.AddRange(themes[pick]);
            pool.RemoveAll(n => n == avoid || n == sheet.Nickname);
            if (pool.Count == 0)
            {
                pool.AddRange(themes.SelectMany(t => t));
                pool.RemoveAll(n => n == avoid || n == sheet.Nickname);
            }
            return pool.Count == 0 ? PlainNames[0] : pool[rng.Next(pool.Count)];
        }

        /// <summary>Called on a level-up: puts a fresh suggestion on the sheet and returns the log line.</summary>
        internal static string OfferOnLevelUp(UnitFileEntry unit, StrikeUnit sheet)
        {
            sheet.NicknameSuggestion = Suggest(sheet, Rng);
            return sheet.Nickname.Length == 0
                ? $"{unit.Name} could be known as \"{sheet.NicknameSuggestion}\" - keep it or change it on the campaign screen."
                : $"New nickname suggested for {Title(unit.Name, sheet.Nickname)}: \"{sheet.NicknameSuggestion}\".";
        }

        internal static readonly string[] KingslayerNames = { "Kingslayer", "Headhunter", "the Crownbreaker", "Chieftain's Bane", "Warlord Killer" };
        internal static readonly string[] MeleeNames = { "the Butcher", "Red Hands", "the Cleaver", "Bloodfist", "the Reaper", "Close Quarters" };
        internal static readonly string[] ShootingNames = { "Deadeye", "Sure Shot", "the Long Arm", "One Shot", "Hawkeye", "the Marksman" };
        internal static readonly string[] UnkillableNames = { "the Unkillable", "Lucky", "Second Chance", "Back Again", "Death Dodger" };
        internal static readonly string[] SlayerNames = { "the Slayer", "Grim Tally", "Body Count", "the Harvester", "No Quarter" };
        internal static readonly string[] ToughNames = { "Iron Wall", "the Anvil", "Stonehide", "the Unbroken", "Bulwark" };
        internal static readonly string[] SwiftNames = { "Quicksilver", "the Hare", "Swiftfoot", "the Outrider", "Fleet" };
        internal static readonly string[] SteadfastNames = { "Steadfast", "the Unflinching", "Stone Nerve", "Never Yield", "Hold Fast" };
        internal static readonly string[] EliteNames = { "the Chosen", "Old Hands", "the Professionals", "Sharp End", "the Finest" };
        internal static readonly string[] OldGuardNames = { "Old Guard", "Grey Beards", "the Veterans", "Long Service", "the Survivors" };
        internal static readonly string[] ScarredNames = { "Scarface", "the Scarred", "Patchwork", "Stitches", "Broken Nose" };
        internal static readonly string[] PlainNames = { "the Lucky Few", "Hard Cases", "the Regulars", "Dog Soldiers", "the Stubborn", "Nobody's Fools" };

        // -- Service record and memorial -----------------------------------------------------------

        /// <summary>
        /// Folds one mission into the unit's service record. Called by <see cref="CampaignRules.AfterGame"/>
        /// before the per-game tallies are cleared.
        /// </summary>
        internal static void RecordMission(StrikeUnit sheet, CampaignUnitResult result)
        {
            sheet.CareerKills += Math.Max(0, result.Kills);
            sheet.CareerHeroKills += Math.Max(0, result.HeroKills);
            if (!result.Casualty) sheet.MissionsSurvived++;
        }

        /// <summary>The unit's honours (traits, injuries, talents, skills) in the words the camp shows.</summary>
        public static List<string> Honours(StrikeUnit sheet) =>
            sheet.Traits.Select(t => t.Replace("Trait: ", ""))
                .Concat(sheet.Talents).Concat(sheet.Skills)
                .Concat(sheet.Injuries.Select(i => "Injury: " + i))
                .ToList();

        /// <summary>Writes a unit that has just died for good onto its army's memorial.</summary>
        internal static FallenUnit Remember(StrikeTeamInfo info, UnitFileEntry unit, StrikeUnit sheet, int mission)
        {
            var fallen = new FallenUnit
            {
                Name = unit.Name,
                Nickname = sheet.Nickname,
                UnitType = string.IsNullOrWhiteSpace(sheet.UnitType) ? unit.Name : sheet.UnitType,
                IsHero = sheet.IsHero,
                Level = sheet.Level,
                Xp = sheet.Xp,
                Honours = Honours(sheet),
                Missions = sheet.Games,
                Kills = sheet.CareerKills,
                HeroKills = sheet.CareerHeroKills,
                Mission = mission,
                FellTo = sheet.FellTo,
                SalvagedFrom = sheet.SalvagedFrom,
                Relic = sheet.Relic,
            };
            info.Memorial.Add(fallen);
            return fallen;
        }

        /// <summary>One line for the memorial page.</summary>
        public static string Epitaph(FallenUnit f)
        {
            // Shown under the unit's name, so it starts with what the unit was rather than repeating the name.
            string what = f.UnitType.Length > 0 ? f.UnitType : "Unit";
            string level = f.Level > 0 ? $", level {f.Level}" : "";
            string salvaged = f.SalvagedFrom.Length > 0 ? $" (salvaged from {f.SalvagedFrom})" : "";
            string kills = f.HeroKills > 0 ? $"{f.Kills} kills and {f.HeroKills} Hero(es)" : $"{f.Kills} kill(s)";
            string how = f.FellTo.Length > 0 ? $"destroyed by {f.FellTo}" : "lost";
            string where = f.Against.Length > 0 ? $" against {f.Against}" : "";
            string relic = f.Relic.Length > 0 ? $" Carried the {f.Relic}." : "";
            return $"{what}{level}{salvaged}. {f.Missions} mission(s), {kills}. Fell in mission {f.Mission}{where}, {how}.{relic}";
        }
    }
}
