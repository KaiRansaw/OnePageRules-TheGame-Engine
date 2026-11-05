using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>The outcome of a treasury operation: whether it happened, the line to log, and the price paid.</summary>
    /// <param name="Ok">The operation went through.</param>
    /// <param name="Message">What to show the player, whether it worked or not.</param>
    /// <param name="Points">Points taken out of the treasury (negative when points came back in).</param>
    public readonly record struct CampaignPurchase(bool Ok, string Message, int Points = 0)
    {
        public static CampaignPurchase No(string why) => new(false, why);
    }

    /// <summary>
    /// Steps 5 and 6 of the campaign loop from Campaign Rules v3.6.0 - Upgrade Armies and Recruit Units -
    /// run between missions on a campaign army file and its treasury
    /// (<see cref="StrikeTeamInfo.BankedPoints"/>).
    /// <para>
    /// Everything here is file-level: it edits an <see cref="ArmyListFile"/>, never the table. The prices
    /// are not summed by hand - a unit is re-compiled from the book through <see cref="ListCompiler"/>
    /// and the difference in its cost is the price, which is how the Horde shop prices its upgrades too.
    /// That keeps unpriced options, per-model costs and "charged once for the whole unit" sections honest
    /// without this class knowing anything about them.
    /// </para>
    /// <para>
    /// The one subtlety is that a campaign unit is not just its book entry. Levels, injuries, talents and
    /// skills add rules, Tough and points that the book has never heard of, so re-compiling would wipe
    /// them. Every re-compile here is followed by
    /// <see cref="ReapplyCampaignChanges"/>, which puts them back.
    /// </para>
    /// </summary>
    public static class CampaignEconomy
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

        // -- The treasury ----------------------------------------------------------------------------

        /// <summary>The army's treasury, or 0 if it isn't a campaign army.</summary>
        public static int Treasury(ArmyListFile army) => army.StrikeTeam?.BankedPoints ?? 0;

        private static StrikeTeamInfo Info(ArmyListFile army) =>
            army.StrikeTeam ??= new StrikeTeamInfo { Name = army.Name };

        // -- Upgrades --------------------------------------------------------------------------------

        /// <summary>
        /// The unit's upgrade selections, or null when it was never built from a book and so can't be
        /// upgraded (a hand-written army, or one imported from Army Forge without selections).
        /// </summary>
        public static BuilderUnit? Loadout(UnitFileEntry unit)
        {
            if (CampaignRules.Sheet(unit)?.LoadoutJson is not { } json || string.IsNullOrWhiteSpace(json))
                return null;
            try { return JsonSerializer.Deserialize<BuilderUnit>(json); }
            catch (JsonException) { return null; }
        }

        /// <summary>Records the unit's upgrade selections on its sheet.</summary>
        public static void SaveLoadout(UnitFileEntry unit, BuilderUnit loadout)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) return;
            sheet.LoadoutJson = JsonSerializer.Serialize(loadout, Json);
            CampaignRules.Save(unit, sheet);
        }

        /// <summary>
        /// What changing this unit to <paramref name="wanted"/> would cost. Positive is a price, negative
        /// is a refund. Works off the compiled cost of both loadouts, so it is right even for options OPR
        /// publishes no price for.
        /// </summary>
        public static int PriceOfChange(BookFile book, UnitFileEntry unit, BuilderUnit wanted)
        {
            BuilderUnit? current = Loadout(unit);
            int now = current == null ? BookCost(unit) : ListCompiler.CompileUnitDetailed(book, current).Unit.PointCost;
            int then = ListCompiler.CompileUnitDetailed(book, wanted).Unit.PointCost;
            return then - now;
        }

        /// <summary>
        /// The unit's cost before campaign changes: what the book would charge for it, which is its
        /// current cost less everything levels, injuries and talents added.
        /// </summary>
        public static int BookCost(UnitFileEntry unit) =>
            unit.PointCost - (CampaignRules.Sheet(unit)?.CostChange ?? 0);

        /// <summary>
        /// Buys (or sells back) upgrades by swapping the unit to <paramref name="wanted"/>. The unit keeps
        /// its name, its sheet and every campaign change; only what the book sells it changes.
        /// <para>
        /// Selling is handled by passing a cheaper loadout: the book's own rule is that an upgrade sells
        /// for half what it cost, rounded down to the nearest 5, which <see cref="SellValue"/> works out.
        /// </para>
        /// </summary>
        public static CampaignPurchase ChangeUpgrades(ArmyListFile army, UnitFileEntry unit, BookFile book,
                                                      BuilderUnit wanted)
        {
            if (CampaignRules.Sheet(unit) is not { } sheet) return CampaignPurchase.No("That unit has no campaign sheet.");
            if (Loadout(unit) == null) return CampaignPurchase.No($"{unit.Name} wasn't built from a book, so its upgrades can't be changed.");

            int price = PriceOfChange(book, unit, wanted);
            StrikeTeamInfo info = Info(army);
            if (price > info.BankedPoints)
                return CampaignPurchase.No($"That costs {price} pts and the treasury holds {info.BankedPoints}.");

            UnitFileEntry rebuilt = ListCompiler.CompileUnitDetailed(book, wanted).Unit;
            SwapInRebuiltUnit(army, unit, rebuilt, sheet);
            SaveLoadout(unit, wanted);
            MergeBookRules(army, book);

            info.BankedPoints -= price;
            string what = price >= 0 ? $"-{price} pts" : $"+{-price} pts back";
            return new CampaignPurchase(true, $"{unit.Name}: upgrades changed ({what}).", price);
        }

        /// <summary>
        /// What an upgrade sells back for: half its original price, rounded down to the nearest multiple
        /// of 5. A 5 pt item is the book's special case - it needs a 4+ to sell at all, and may only be
        /// tried once after each mission - so this returns 5 for it and the caller makes the roll.
        /// </summary>
        public static int SellValue(int originalCost) =>
            originalCost <= 0 ? 0 : originalCost <= 5 ? 5 : originalCost / 2 / 5 * 5;

        /// <summary>
        /// Whether a 5 pt item may be sold: the book asks for a 4+, once per item after each mission.
        /// </summary>
        public static bool SellsFivePointItem(Func<int> d6) => Math.Clamp(d6(), 1, 6) >= 4;

        // -- Recruiting ------------------------------------------------------------------------------

        /// <summary>How many Heroes the army has. The book caps a campaign army at three.</summary>
        public static int HeroCount(ArmyListFile army) => army.Units.Count(CampaignRules.IsHero);

        /// <summary>
        /// Why this unit can't be recruited, or null. The book: one new Hero per mission, at most three
        /// Heroes in the army, unlimited regular units, and you must be able to afford it.
        /// </summary>
        public static string? CannotRecruit(ArmyListFile army, BookFile book, BuilderUnit wanted,
                                            bool heroAlreadyRecruitedThisMission)
        {
            UnitFileEntry preview;
            try { preview = ListCompiler.CompileUnitDetailed(book, wanted).Unit; }
            catch (InvalidOperationException e) { return e.Message; }

            if (CampaignRules.IsHero(preview))
            {
                if (heroAlreadyRecruitedThisMission) return "Only one new Hero may be recruited after each mission.";
                if (HeroCount(army) >= CampaignNumbers.MaxHeroes)
                    return $"The army already has {CampaignNumbers.MaxHeroes} Heroes, which is the limit.";
            }
            return preview.PointCost > Treasury(army)
                ? $"{preview.Name} costs {preview.PointCost} pts and the treasury holds {Treasury(army)}."
                : null;
        }

        /// <summary>
        /// Recruits a unit into the army, paying for it out of the treasury. With
        /// <paramref name="veteran"/> it also pays the book's surcharge and the unit starts with 5 XP -
        /// which the book allows for one new recruit after each mission, so the caller enforces "one".
        /// </summary>
        public static CampaignPurchase Recruit(ArmyListFile army, BookFile book, BuilderUnit wanted,
                                               CampaignNumbers n, bool veteran = false,
                                               bool heroAlreadyRecruitedThisMission = false,
                                               Func<int>? d6 = null)
        {
            if (CannotRecruit(army, book, wanted, heroAlreadyRecruitedThisMission) is { } why)
                return CampaignPurchase.No(why);

            UnitFileEntry unit = ListCompiler.CompileUnitDetailed(book, wanted).Unit;
            int price = unit.PointCost + (veteran ? n.VeteranRecruitCost : 0);
            StrikeTeamInfo info = Info(army);
            if (price > info.BankedPoints)
                return CampaignPurchase.No($"That costs {price} pts and the treasury holds {info.BankedPoints}.");

            string type = unit.Name;
            unit.Name = UniqueName(army, unit.Name);
            var sheet = new StrikeUnit
            {
                UnitType = type,   // #453: a recruit is renameable like any other unit
                IsHero = CampaignRules.IsHero(unit),
                TeamFile = army.Units.Select(u => CampaignRules.Sheet(u)?.TeamFile).FirstOrDefault(f => f != null),
            };
            CampaignRules.Save(unit, sheet);
            SaveLoadout(unit, wanted);
            army.Units.Add(unit);
            MergeBookRules(army, book);
            info.BankedPoints -= price;

            string line = $"Recruited {unit.Name} for {price} pts.";
            if (veteran && d6 != null)
            {
                // The veteran recruit starts at 5 XP, which is a level, so it rolls its trait now.
                foreach (string l in CampaignRules.AddXp(unit, sheet, CampaignNumbers.XpPerLevel, n, d6))
                    line += " " + l;
                CampaignRules.Save(unit, sheet);
            }
            return new CampaignPurchase(true, line, price);
        }

        // -- Disbanding ------------------------------------------------------------------------------

        /// <summary>
        /// Disbands a unit: it leaves the army with its XP and upgrades, and nothing comes back. The book
        /// is explicit that there is no benefit - it is only a way to be rid of a unit you don't want.
        /// Any Hero that had joined it is detached so the army file keeps no dangling link.
        /// </summary>
        public static CampaignPurchase Disband(ArmyListFile army, UnitFileEntry unit)
        {
            if (!army.Units.Contains(unit)) return CampaignPurchase.No("That unit isn't in the army.");
            army.Units.Remove(unit);
            int detached = 0;
            foreach (UnitFileEntry other in army.Units)
            {
                if (unit.Id != null && string.Equals(other.JoinsUnitId, unit.Id, StringComparison.OrdinalIgnoreCase))
                {
                    other.JoinsUnitId = null;
                    detached++;
                }
            }
            string line = $"Disbanded {unit.Name}; its XP and upgrades are gone.";
            if (detached > 0) line += $" {detached} Hero(es) no longer join it.";
            return new CampaignPurchase(true, line);
        }

        /// <summary>
        /// Disbands the whole army: every unit, all VP and the treasury, so the player can start again
        /// from scratch. The book offers this and it cannot be undone, so the caller must confirm first.
        /// </summary>
        public static CampaignPurchase DisbandArmy(ArmyListFile army)
        {
            int units = army.Units.Count;
            army.Units.Clear();
            StrikeTeamInfo info = Info(army);
            info.Vp = 0;
            info.BankedPoints = 0;
            info.Matches = 0;
            info.History.Add($"The army was disbanded: {units} unit(s), all VP and all points lost.");
            return new CampaignPurchase(true, $"Disbanded the whole army: {units} unit(s), all VP and all points lost.");
        }

        // -- Keeping campaign changes through a re-compile --------------------------------------------

        /// <summary>
        /// Replaces a unit's book-given half (stats, weapons, rules, cost, model count) with a freshly
        /// compiled one, then puts the campaign's own changes back on top. Everything the sheet records -
        /// its name, its id and hero link, its level cost, its traits, injuries, talents and skills, and
        /// the Tough a Toughened talent added - survives.
        /// </summary>
        private static void SwapInRebuiltUnit(ArmyListFile army, UnitFileEntry unit, UnitFileEntry rebuilt,
                                              StrikeUnit sheet)
        {
            unit.ModelCount = rebuilt.ModelCount;
            unit.Base = rebuilt.Base;
            unit.Quality = rebuilt.Quality;
            unit.Defense = rebuilt.Defense;
            unit.Weapons = rebuilt.Weapons;
            unit.SpecialRules = rebuilt.SpecialRules;
            unit.PointCost = rebuilt.PointCost;
            ReapplyCampaignChanges(unit, sheet);
        }

        /// <summary>
        /// Puts a unit's campaign changes back after it has been re-compiled from the book: the rules its
        /// traits, injuries, talents and skills grant, the Tough(+3) per Toughened talent, and the points
        /// those cost. Safe to call twice - every step checks before it adds.
        /// </summary>
        public static void ReapplyCampaignChanges(UnitFileEntry unit, StrikeUnit sheet)
        {
            foreach (string trait in sheet.Traits) CampaignRules.AddRule(unit, trait);
            foreach (string injury in sheet.Injuries) CampaignRules.AddRule(unit, CampaignTables.InjuryPrefix + injury);
            foreach (string talent in sheet.Talents) CampaignRules.AddRule(unit, CampaignTables.TalentPrefix + talent);
            foreach (string skill in sheet.Skills) CampaignRules.AddRule(unit, CampaignTables.SkillPrefix + skill);

            int toughened = sheet.Talents.Count(t => t == "Toughened");
            if (toughened > 0) AddTough(unit, 3 * toughened);

            unit.PointCost = Math.Max(0, unit.PointCost + sheet.CostChange);
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

        /// <summary>
        /// Copies any rule or spell definition the book has and the army file doesn't, so a newly bought
        /// upgrade or a recruit's faction rules actually fire. Matched by name, case-insensitively; the
        /// army's own definition always wins, because a campaign army may have retuned one.
        /// </summary>
        private static void MergeBookRules(ArmyListFile army, BookFile book)
        {
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                if (!army.RuleDefinitions.Any(d => string.Equals(d.Name, definition.Name, StringComparison.OrdinalIgnoreCase)))
                    army.RuleDefinitions.Add(definition);

            foreach (SpellDefinition spell in book.Spells)
                if (!army.Spells.Any(s => string.Equals(s.Name, spell.Name, StringComparison.OrdinalIgnoreCase)))
                    army.Spells.Add(spell);
        }

        /// <summary>
        /// A name no other unit in the army has, so the campaign screen can tell two of the same unit
        /// apart. <see cref="CampaignRules.Start"/> does the same at the start, and the sheet-to-unit
        /// match depends on it.
        /// </summary>
        private static string UniqueName(ArmyListFile army, string wanted)
        {
            var taken = new HashSet<string>(army.Units.Select(u => u.Name), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(wanted)) return wanted;
            for (int i = 2; ; i++)
            {
                string candidate = $"{wanted} {i}";
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
