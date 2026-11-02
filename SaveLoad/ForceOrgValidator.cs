using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.SaveLoad
{
    /// <summary>
    /// Optional force-organization check (#003). Computes army-composition warnings but is purely
    /// advisory — it never blocks saving or launching. Pure and side-effect-free so it can be unit-tested
    /// and reused (e.g. by the lobby) without any UI.
    ///
    /// <para>#409 (OPR 3.6.0) brought this in line with the books in two ways. The caps are now read from
    /// a per-system <see cref="ForceOrgProfile"/> instead of being hard-coded to Grimdark Future's
    /// numbers — Firefight and the two fantasy systems have always had their own, and an army carries its
    /// system slug. And the three caps the validator never implemented are now checked: the 1+X copy
    /// formula (it used a flat 3), the 35% single-unit ceiling, and the unit-count cap — plus 3.6's new
    /// model/Tough density cap, which is the only one that is actually new rules rather than catch-up.</para>
    /// </summary>
    public static class ForceOrgValidator
    {
        /// <summary>
        /// Grimdark Future's hero cap, kept as a public constant because callers outside this class
        /// referenced it before the profiles existed. Prefer <see cref="ForceOrgProfile.PointsPerHero"/>,
        /// which is right for every system rather than just one.
        /// </summary>
        public const int POINTS_PER_HERO = 500;

        /// <summary>
        /// The flat copy cap this validator used before #409. The books say 1+X copies where X scales
        /// with army size, which is what <see cref="Validate"/> now applies; at Grimdark Future's
        /// 2000pts that works out to 3, which is where this number came from.
        /// </summary>
        public const int MAX_COPIES_PER_UNIT = 3;

        /// <summary>
        /// Returns one human-readable warning for each force-org cap the army exceeds, in display order.
        /// An empty list means the army is within every cap. Never throws and never blocks.
        /// </summary>
        public static IReadOnlyList<string> Validate(ArmyListFile army)
        {
            List<string> warnings = new List<string>();
            ForceOrgProfile profile = ForceOrgProfile.For(army.GameSystem);

            // Cost cap — total points must not exceed the army's limit.
            if (army.TotalPoints > army.PointsLimit)
            {
                warnings.Add($"Over points limit: {army.TotalPoints} / {army.PointsLimit} pts.");
            }

            // Hero cap — 1 Hero per N pts of the limit, N per system.
            int heroCount = army.Units.Count(IsHero);
            int heroAllowance = army.PointsLimit / profile.PointsPerHero;
            if (heroCount > heroAllowance)
            {
                warnings.Add($"Too many Heroes: {heroCount} (max {heroAllowance} at {army.PointsLimit} pts; "
                    + $"1 per {profile.PointsPerHero}).");
            }

            // Copy cap — "up to 1+X copies of the same unit, where X is 1 per N pts in their army".
            // Before #409 this was a flat 3 regardless of system or army size.
            //
            // A merged combined pair is ONE squad toward the cap (Chris, 2026-07-10: doubled-up squads
            // must not count twice) — the books say the same ("combined units count as one"). The
            // compiler no longer suffixes merged names (Chris, 2026-07-24), but army files written before
            // then still contain "X (Combined)" entries; the suffix strip keeps those grouping with plain
            // "X" squads. Blank names are skipped, since several half-built (unnamed) entries shouldn't
            // read as duplicates of each other.
            int copyAllowance = 1 + army.PointsLimit / profile.PointsPerExtraCopy;
            foreach (IGrouping<string, UnitFileEntry> group in army.Units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name))
                .GroupBy(u => BaseUnitName(u.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > copyAllowance))
            {
                warnings.Add($"Too many copies of \"{group.Key}\": {group.Count()} (max {copyAllowance} "
                    + $"at {army.PointsLimit} pts).");
            }

            // Unique cap - NEW IN #414: "this model may only be taken once per army" (3.6.0's Unique
            // Hero, carried by every named character). This was enforced NOWHERE, and the copy cap above
            // does not catch it: 1+X copies works out to 3 or more at ordinary army sizes, so two of the
            // same named character sat comfortably inside it. Grouped by name the same way, so a pre-#409
            // "X (Combined)" entry still groups with plain "X".
            foreach (IGrouping<string, UnitFileEntry> group in army.Units
                .Where(u => !string.IsNullOrWhiteSpace(u.Name) && IsUnique(u))
                .GroupBy(u => BaseUnitName(u.Name), StringComparer.Ordinal)
                .Where(g => g.Count() > 1))
            {
                warnings.Add($"\"{group.Key}\" may only be taken once per army: {group.Count()} taken.");
            }

            // Single-unit ceiling — "no single unit may be worth over 35% of total points". Measured
            // against the army's LIMIT rather than what it has actually spent, so a half-built list is
            // judged against the game it is being built for and the warning doesn't flicker as units are
            // added. Skipped for a limitless (0) army, where a percentage means nothing.
            if (army.PointsLimit > 0)
            {
                int ceiling = army.PointsLimit * profile.MaxSingleUnitPercent / 100;
                foreach (UnitFileEntry unit in army.Units.Where(u => u.PointCost > ceiling))
                {
                    warnings.Add($"\"{DisplayName(unit)}\" is {unit.PointCost} pts, over the "
                        + $"{profile.MaxSingleUnitPercent}% single-unit cap ({ceiling} pts).");
                }
            }

            // Unit-count cap — "armies may only have max. 1 unit per N pts".
            int unitAllowance = army.PointsLimit / profile.PointsPerUnit;
            if (army.Units.Count > unitAllowance)
            {
                warnings.Add($"Too many units: {army.Units.Count} (max {unitAllowance} at "
                    + $"{army.PointsLimit} pts; 1 per {profile.PointsPerUnit}).");
            }

            // Density cap — NEW IN 3.6: "max. 1 model or Tough per N pts" (and its skirmish wording,
            // "max. 3 total Tough per N pts"). Both count the same quantity: each model contributes its
            // Tough value, or 1 if it has none, so a Tough(6) walker eats six bodies' worth of the cap.
            int density = army.Units.Sum(ModelsOrTough);
            int densityAllowance = army.PointsLimit / profile.DensityPointsPerBlock
                * profile.DensityAllowancePerBlock;
            if (density > densityAllowance)
            {
                warnings.Add($"Too many models/Tough: {density} (max {densityAllowance} at "
                    + $"{army.PointsLimit} pts; {profile.DensityAllowancePerBlock} per "
                    + $"{profile.DensityPointsPerBlock}).");
            }

            // An army of only Heroes has no rank-and-file unit for them to lead. (An entirely empty army
            // is left alone — it's just unfinished, not mis-composed.) Not from the Force Org box; it is
            // the engine's own sanity check and predates it.
            if (army.Units.Count > 0 && army.Units.All(IsHero))
            {
                warnings.Add("Army has no non-Hero units.");
            }

            // #439 - AoF: Skirmish alone: "Each army may only have one model with one of the following
            // upgrades" (Sergeant / Musician / Banner). Every other system's book caps them per UNIT,
            // which the book data's own maxPicks: 1 already enforces, so this fires for one system.
            //
            // Counted as UPGRADES TAKEN, not units carrying one: each is an Affects.One section, so a
            // squad with a Banner model and a Sergeant model is two models with Command Group upgrades
            // and breaks a cap worded "one model with one of the following".
            if (GameSystemInfo.FromSlug(army.GameSystem) is { } system
                && Rules.Dispatch.CoreRulesProfile.For(system).CommandGroupCapIsPerArmy)
            {
                int commandGroupUpgrades = army.Units.Sum(CommandGroupUpgradeCount);
                if (commandGroupUpgrades > 1)
                {
                    warnings.Add($"Too many Command Group upgrades: {commandGroupUpgrades} "
                        + "(this game allows one model in the whole army to have a Sergeant, Musician or "
                        + "Banner).");
                }
            }

            return warnings;
        }

        /// <summary>
        /// How many Command Group upgrades a unit carries. Sergeant counts here even though it confers on
        /// nobody - the cap is on the upgrades, not on the ones that buff other units.
        /// </summary>
        private static int CommandGroupUpgradeCount(UnitFileEntry unit) =>
            unit.SpecialRules.Count(EntryIsCommandGroup);

        private static bool EntryIsCommandGroup(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => IsCommandGroupName(core.Name),
            SpecialRuleEntry_CoreNumeric num => IsCommandGroupName(num.Name),
            SpecialRuleEntry_Alias alias => IsCommandGroupName(alias.Name),
            _ => false,
        };

        private static bool IsCommandGroupName(string? name) =>
            name is not null
            && (string.Equals(name, "Sergeant", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Musician", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "Banner", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A unit's contribution to the 3.6 density cap: its Tough value per model where it has one, else
        /// one per model. Tough is a per-MODEL property in OPR, so a unit of 3 models with Tough(3) is 9,
        /// matching how the cap is counted on a physical army list.
        /// </summary>
        public static int ModelsOrTough(UnitFileEntry unit)
        {
            int models = Math.Max(unit.ModelCount, 1);
            int perModel = Math.Max(ToughValue(unit), 1);
            return models * perModel;
        }

        /// <summary>The unit's Tough(X) argument, or 0 if it has no Tough rule.</summary>
        public static int ToughValue(UnitFileEntry unit)
        {
            foreach (SpecialRuleEntry entry in unit.SpecialRules)
            {
                int value = ToughValue(entry);
                if (value > 0) return value;
            }
            return 0;
        }

        private static int ToughValue(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_CoreNumeric num
                when string.Equals(num.Name, "Tough", StringComparison.OrdinalIgnoreCase)
                => num.NumericValue,
            SpecialRuleEntry_Alias alias => ToughValue(alias.AliasedRule),
            _ => 0,
        };

        private static string DisplayName(UnitFileEntry unit) =>
            string.IsNullOrWhiteSpace(unit.Name) ? "(unnamed unit)" : unit.Name;

        /// <summary>The copy-cap group key: the unit's name with the legacy " (Combined)" merge
        /// suffix stripped (written by pre-2026-07-24 compiles/imports), so an old file's combined
        /// pair still groups with plain squads of the same unit.</summary>
        private static string BaseUnitName(string name) =>
            name.EndsWith(" (Combined)", StringComparison.Ordinal)
                ? name[..^" (Combined)".Length]
                : name;

        /// <summary>True if the unit carries the Hero rule (recursing through alias entries). The single
        /// definition of "is this army-file unit a Hero", shared with the Army Builder UI.</summary>
        public static bool IsHero(UnitFileEntry unit) => unit.SpecialRules.Any(EntryIsHero);

        // #414: "Hero" OR "Unique Hero" - 3.6.0's named characters carry the latter INSTEAD of the former,
        // so a name test against "Hero" alone answered no for every named character in every book.
        private static bool EntryIsHero(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(core.Name),
            SpecialRuleEntry_CoreNumeric num => Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(num.Name),
            SpecialRuleEntry_Alias alias => EntryIsHero(alias.AliasedRule),
            _ => false,
        };

        /// <summary>
        /// True if this unit may only be taken once per army - 3.5's "Unique" or 3.6's "Unique Hero".
        /// <para>
        /// #414. The authoritative check is <see cref="ArmyBuilding.ListValidator"/>, which raises a
        /// blocking Error while a list is being built against its book. This one covers a different
        /// input: an already-compiled <c>.fdgarmy</c> loaded in the lobby or the Army Builder, which
        /// never passes through ListValidator because it no longer has a book or its picks. Hence a
        /// warning here and an Error there - the same rule, judged on what the caller can still change.
        /// </para>
        /// </summary>
        public static bool IsUnique(UnitFileEntry unit) => unit.SpecialRules.Any(EntryIsUnique);

        private static bool EntryIsUnique(SpecialRuleEntry entry) => entry switch
        {
            SpecialRuleEntry_Core core => IsUniqueName(core.Name),
            SpecialRuleEntry_CoreNumeric num => IsUniqueName(num.Name),
            SpecialRuleEntry_Alias alias => EntryIsUnique(alias.AliasedRule),
            _ => false,
        };

        private static bool IsUniqueName(string? name) =>
            Rules.Dispatch.CoreRuleCatalog.IsUniqueRuleName(name);
    }
}
