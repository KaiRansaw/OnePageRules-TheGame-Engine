using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>
    /// One enemy unit left on the field after a win, which the winner may buy (house rule B27).
    /// </summary>
    public sealed class SalvageOffer
    {
        /// <summary>What it was called in the enemy army ("Ironclad 2").</summary>
        public string Name { get; set; } = "";
        /// <summary>What it is in its book ("Battle Tank").</summary>
        public string UnitType { get; set; } = "";
        /// <summary>The enemy army it came from.</summary>
        public string FromArmy { get; set; } = "";
        /// <summary>The enemy army's faction (army book).</summary>
        public string FromFaction { get; set; } = "";
        /// <summary>The mission it was won in (the winner's <see cref="StrikeTeamInfo.Matches"/> after that mission).</summary>
        public int Mission { get; set; }
        /// <summary>The price: half its cost before campaign changes, rounded up to 5 pts.</summary>
        public int Price { get; set; }
        /// <summary>The unit, stripped of its campaign changes, as a <see cref="UnitFileEntry"/> in the rules JSON.</summary>
        public string UnitJson { get; set; } = "";
        /// <summary>The enemy army's rule and spell definitions the unit may need, in the rules JSON.</summary>
        public string RulesJson { get; set; } = "";
        public string SpellsJson { get; set; } = "";
    }

    /// <summary>
    /// Salvage (homebrew, Kai 2026-11-27, inspired by BattleTech; house rule B27). After a mission an army WON,
    /// the enemy's vehicles and monsters that were destroyed and rolled Dead on the casualty table are left
    /// on the field. The winner may buy ONE of them, at half its cost before campaign changes, from its
    /// treasury. It joins as a fresh unit: no XP, traits, injuries or skills.
    /// <para>
    /// Kai's calls: "Vehicles &amp; monsters" (not infantry, not Heroes) and "half price, 1 per mission".
    /// "Vehicle or monster" is Army Forge's own roster grouping (FdgApp ForgeRosterCategories): Aircraft, or a
    /// single model with Tough(6)+ - Light Vehicles / Small Monsters, Vehicles / Monsters and Titans. The
    /// skirmish games (Firefight, Skirmish) use three times the Tough, as Army Forge does.
    /// </para>
    /// </summary>
    public static class CampaignSalvage
    {
        private static readonly string[] HeroRules = { "Hero", "Unique Hero" };

        /// <summary>Whether a destroyed enemy unit is the kind that can be salvaged.</summary>
        public static bool IsSalvageable(UnitFileEntry unit, EGameSystem system)
        {
            if (unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core c && HeroRules.Contains(c.Name))) return false;
            if (unit.SpecialRules.Any(r => r is SpecialRuleEntry_Core { Name: "Aircraft" })) return true;
            if (unit.ModelCount != 1) return false;
            int scale = system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish ? 3 : 1;
            return BookTough(unit) >= 6 * scale;
        }

        /// <summary>The unit's Tough before any Toughened talent (heroes only get those, and heroes don't qualify).</summary>
        private static int BookTough(UnitFileEntry unit) =>
            unit.SpecialRules.OfType<SpecialRuleEntry_CoreNumeric>().Where(r => r.Name == "Tough")
                .Select(r => r.NumericValue).DefaultIfEmpty(0).Max();

        /// <summary>Half of <paramref name="cost"/>, rounded UP to the nearest 5 pts (never below 5).</summary>
        public static int PriceFor(int cost) => Math.Max(5, (Math.Max(0, cost) + 9) / 10 * 5);

        /// <summary>
        /// A copy of a dead enemy unit with every campaign change taken off: the rules its traits, injuries,
        /// talents and skills granted, the Tough(+3) per Toughened, the points they cost, its sheet and its
        /// links to other units. What is left is the unit as its book (and its bought upgrades) made it.
        /// </summary>
        public static UnitFileEntry Strip(UnitFileEntry dead)
        {
            UnitFileEntry copy = JsonSerializer.Deserialize<UnitFileEntry>(
                JsonSerializer.Serialize(dead, RuleJson.Options), RuleJson.Options)!;
            StrikeUnit? sheet = StrikeUnit.FromJson(dead.StrikeUnitJson);
            if (sheet != null)
            {
                var granted = new HashSet<string>(sheet.Traits);
                foreach (string i in sheet.Injuries) granted.Add(CampaignTables.InjuryPrefix + i);
                foreach (string t in sheet.Talents) granted.Add(CampaignTables.TalentPrefix + t);
                foreach (string s in sheet.Skills) granted.Add(CampaignTables.SkillPrefix + s);
                copy.SpecialRules.RemoveAll(r => r is SpecialRuleEntry_Core c && granted.Contains(c.Name));

                int toughened = sheet.Talents.Count(t => t == "Toughened");
                if (toughened > 0)
                {
                    int i = copy.SpecialRules.FindIndex(r => r is SpecialRuleEntry_CoreNumeric { Name: "Tough" });
                    if (i >= 0)
                    {
                        var tough = (SpecialRuleEntry_CoreNumeric)copy.SpecialRules[i];
                        copy.SpecialRules[i] = new SpecialRuleEntry_CoreNumeric("Tough", Math.Max(1, tough.NumericValue - 3 * toughened));
                    }
                }
                copy.PointCost = Math.Max(0, copy.PointCost - sheet.CostChange);
                if (!string.IsNullOrWhiteSpace(sheet.UnitType)) copy.Name = sheet.UnitType;
            }
            copy.StrikeUnitJson = null;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.JoinsUnitId = null;
            return copy;
        }

        /// <summary>
        /// The salvage a winning army is offered from the units a losing army lost for good this mission.
        /// Empty when nothing qualifies.
        /// </summary>
        public static List<SalvageOffer> OffersFrom(ArmyListFile loser, IEnumerable<UnitFileEntry> deadUnits,
                                                    EGameSystem system, int mission)
        {
            var offers = new List<SalvageOffer>();
            foreach (UnitFileEntry dead in deadUnits)
            {
                if (!IsSalvageable(dead, system)) continue;
                UnitFileEntry clean = Strip(dead);
                offers.Add(new SalvageOffer
                {
                    Name = dead.Name,
                    UnitType = clean.Name,
                    FromArmy = loser.StrikeTeam?.Name is { Length: > 0 } n ? n : loser.Name,
                    FromFaction = loser.Faction,
                    Mission = mission,
                    Price = PriceFor(clean.PointCost),
                    UnitJson = JsonSerializer.Serialize(clean, RuleJson.Options),
                    RulesJson = JsonSerializer.Serialize(loser.RuleDefinitions, RuleJson.Options),
                    SpellsJson = JsonSerializer.Serialize(loser.Spells, RuleJson.Options),
                });
            }
            return offers;
        }

        /// <summary>Why the army can't claim this offer now, or null.</summary>
        public static string? CannotClaim(ArmyListFile army, SalvageOffer offer)
        {
            if (army.StrikeTeam is not { } info) return "That isn't a campaign army.";
            if (!info.Salvage.Contains(offer)) return "That salvage is no longer on offer.";
            if (info.SalvagedAfterMission == info.Matches) return "Only one salvaged unit may be claimed after each mission.";
            if (offer.Price > info.BankedPoints) return $"It costs {offer.Price} pts and the treasury holds {info.BankedPoints}.";
            return null;
        }

        /// <summary>
        /// Buys a salvaged unit into the army: pays its price, gives it a fresh campaign sheet tagged with where it
        /// came from, and copies over any rule or spell definition of its own army it needs. The other offers stay
        /// listed (greyed by <see cref="CannotClaim"/>) until the next mission clears them.
        /// </summary>
        public static CampaignPurchase Claim(ArmyListFile army, SalvageOffer offer)
        {
            if (CannotClaim(army, offer) is { } why) return CampaignPurchase.No(why);
            StrikeTeamInfo info = army.StrikeTeam!;

            UnitFileEntry unit;
            try { unit = JsonSerializer.Deserialize<UnitFileEntry>(offer.UnitJson, RuleJson.Options)!; }
            catch (JsonException) { return CampaignPurchase.No("That salvage could not be read."); }

            string type = string.IsNullOrWhiteSpace(offer.UnitType) ? unit.Name : offer.UnitType;
            unit.Name = UniqueName(army, $"Salvaged {type}");
            var sheet = new StrikeUnit
            {
                UnitType = type,
                IsHero = false,
                TeamFile = army.Units.Select(u => CampaignRules.Sheet(u)?.TeamFile).FirstOrDefault(f => f != null),
                SalvagedFrom = offer.FromArmy,
                // No loadout: it was built from another army's book, so this army's upgrade screen can't re-price it.
                LoadoutJson = null,
            };
            CampaignRules.Save(unit, sheet);
            army.Units.Add(unit);
            MergeDefinitions(army, offer);

            info.BankedPoints -= offer.Price;
            info.SalvagedAfterMission = info.Matches;
            string line = $"Salvaged {type} from {offer.FromArmy} for {offer.Price} pts - it joins as {unit.Name}.";
            info.History.Add(line);
            return new CampaignPurchase(true, line, offer.Price);
        }

        private static void MergeDefinitions(ArmyListFile army, SalvageOffer offer)
        {
            try
            {
                var rules = JsonSerializer.Deserialize<List<SpecialRuleDefinition>>(offer.RulesJson, RuleJson.Options) ?? new();
                foreach (SpecialRuleDefinition d in rules)
                    if (!army.RuleDefinitions.Any(x => string.Equals(x.Name, d.Name, StringComparison.OrdinalIgnoreCase)))
                        army.RuleDefinitions.Add(d);
                var spells = JsonSerializer.Deserialize<List<SpellDefinition>>(offer.SpellsJson, RuleJson.Options) ?? new();
                foreach (SpellDefinition s in spells)
                    if (!army.Spells.Any(x => string.Equals(x.Name, s.Name, StringComparison.OrdinalIgnoreCase)))
                        army.Spells.Add(s);
            }
            catch (JsonException) { /* the unit still plays on its core rules */ }
        }

        private static string UniqueName(ArmyListFile army, string wanted)
        {
            var taken = new HashSet<string>(army.Units.Select(u => u.Name), StringComparer.OrdinalIgnoreCase);
            if (!taken.Contains(wanted)) return wanted;
            for (int i = 2; ; i++)
                if (!taken.Contains($"{wanted} {i}")) return $"{wanted} {i}";
        }
    }
}
