using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Stages;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Rules.Dispatch;
using FDG.StateMachine;
using FDG.Utilities;

namespace FDG
{
    /// <summary>
    /// Critical Hits (Advanced Rulebooks 3.6.0: GF Firefight p45 and AoF Skirmish p45 - only those two print this
    /// chapter; GF / AoF have the one-line Brutal Damage version instead, see <see cref="BrutalDamage"/>).
    /// "When a unit rolls at least one result of 6 to hit with melee or ranged weapons, then it deals a critical hit.
    /// Only a single critical hit can be dealt per shooting or melee, and if the unit needed a 6+ to hit then it can't
    /// deal critical hits. Choose the critical hit section corresponding to the weapon type, and roll a die."
    /// Judgement calls in house rules C, "Critical Hits readings".
    /// </summary>
    public static class CriticalHits
    {
        public const string Generic = "Generic";
        public const string ByWeaponType = "By weapon type";
        public const string Label = "Critical Hit";

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (Generic, EAdvancedRules.SkirmishCriticalHits),
            (ByWeaponType, EAdvancedRules.CriticalHitsByWeaponType),
        };

        /// <summary>Only GF: Firefight and AoF: Skirmish print this chapter.</summary>
        public static bool BooksHaveIt(EGameSystem system) =>
            system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static string BookText(string name) => name switch
        {
            Generic => "When a unit rolls at least one result of 6 to hit with melee or ranged weapons, it deals a critical hit. Only a single critical hit can be dealt per shooting or melee, and if the unit needed a 6+ to hit it can't deal critical hits. Roll a die on the Generic Melee or Generic Ranged table:\nMelee - 1-3 Vital Area: if the hit isn't blocked the target takes 1 wound. 4-5 Exposed Spot: the hit gets AP(+4), and if it isn't blocked the target takes 1 wound. 6 Master Strike: as Exposed Spot, and the target counts as having taken +2 wounds when checking who won melee.\nRanged - 1-3 Weak Spot: the hit gets AP(+4). 4-5 Ricochet: AP(+4), and the closest enemy unit within 6\" takes 1 hit without special rules. 6 Master Shot: as Ricochet, and the target counts as having taken +2 wounds when checking if shooting wounds would trigger a Morale Test.",
            ByWeaponType => "Use the weapon type specific critical hits sections instead of the generic ones. You and your opponent agree on which weapons are of which type before the game begins.\nFirefight: Bladed and Bludgeoning melee; Heat, Energy and Explosive ranged. Skirmish: Bladed, Bludgeoning and Thrusting melee; Missile and Gunpowder ranged. Weapons that don't have a clear type use the generic tables.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            Generic => "Unmodified 6s, not spells. \"The hit\" is one of the hits that landed; an extra wound when it isn't blocked can't be ignored. \"The closest enemy unit within 6\"\" is the closest other enemy within 6\" of the target. Hits \"without special rules\" roll Defense with no AP.",
            ByWeaponType => "The type is read from the weapon's name (sword, axe, hammer, spear, flamer, plasma, missile, bow, rifle...). Anything else uses the generic table. A ranged name on a melee weapon (or the other way round) uses the generic table for how it is being used. Ticking this replaces the generic tables.",
            _ => "",
        };

        public static bool On(IGameContext c) =>
            AdvancedRules.IsOn(c, EAdvancedRules.SkirmishCriticalHits | EAdvancedRules.CriticalHitsByWeaponType);

        public static bool TypedOn(IGameContext c) => AdvancedRules.IsOn(c, EAdvancedRules.CriticalHitsByWeaponType);

        /// <summary>For screens that hold the lobby's settings rather than a game: is the chapter on in this game?</summary>
        public static bool OnFor(EGameSystem system, EAdvancedRules rules) => BooksHaveIt(system)
            && (rules & (EAdvancedRules.SkirmishCriticalHits | EAdvancedRules.CriticalHitsByWeaponType)) != 0;

        public static bool TypedFor(EAdvancedRules rules) => (rules & EAdvancedRules.CriticalHitsByWeaponType) != 0;

        // ================================================================= weapon types

        public enum EType { GenericMelee, GenericRanged, Bladed, Bludgeoning, Thrusting, Heat, Energy, Explosive, Missile, Gunpowder }

        private static readonly string[] BladedWords = { "sword", "blade", "battleaxe", "greataxe", "dagger", "knife", "knives", "axe", "sabre", "saber", "scythe", "sickle", "cleaver", "razor", "claw", "talon", "scimitar", "katana", "falchion", "glaive", "rapier", "cutlass", "machete" };
        private static readonly string[] BludgeonWords = { "hammer", "club", "flail", "mace", "maul", "fist", "morningstar", "morning star", "crusher", "gauntlet", "cudgel", "staff", "stave", "smasher", "bludgeon" };
        private static readonly string[] ThrustWords = { "spear", "halberd", "pike", "lance", "trident", "polearm", "harpoon", "partisan" };
        private static readonly string[] HeatWords = { "flame", "flamer", "fire", "burn", "fusion", "melta", "heat", "incinerat", "inferno", "pyre", "thermal", "magma", "napalm" };
        private static readonly string[] EnergyWords = { "plasma", "laser", "las", "beam", "lightning", "energy", "ion", "gauss", "particle", "pulse", "volt", "arc", "ray", "disintegrat", "photon", "tesla", "neutron", "lance" };
        private static readonly string[] ExplosiveWords = { "missile", "rocket", "cannon", "grenade", "launcher", "mortar", "bomb", "artillery", "howitzer", "frag", "shell", "mine", "demolition", "torpedo", "explosive" };
        private static readonly string[] MissileWords = { "bow", "longbow", "shortbow", "javelin", "crossbow", "sling", "throwing", "dart", "shuriken", "harpoon", "spear thrower", "bolas" };
        private static readonly string[] GunpowderWords = { "rifle", "pistol", "gun", "musket", "handgun", "cannon", "bomb", "grenade", "mortar", "blunderbuss", "arquebus", "explosive", "rocket", "carbine", "volley" };

        /// <summary>A word of 4+ letters matches anywhere in the name ("Greatsword", "Chainblade"); a shorter one only at
        /// the start of a word ("Las Rifle", not "Atlas"; "Ion Cannon", not "Scorpion").</summary>
        private static bool Has(string name, string[] words) =>
            words.Any(w => w.Length >= 4
                ? name.Contains(w, StringComparison.OrdinalIgnoreCase)
                : System.Text.RegularExpressions.Regex.IsMatch(name, $@"(^|[^a-z]){System.Text.RegularExpressions.Regex.Escape(w)}", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

        /// <summary>The critical hit table for a weapon, used in melee or shooting.</summary>
        public static EType TypeOf(EGameSystem system, string weaponName, bool melee, bool typed)
        {
            if (!typed) return melee ? EType.GenericMelee : EType.GenericRanged;
            string n = weaponName ?? "";
            bool skirmish = system == EGameSystem.AoFSkirmish;
            if (melee)
            {
                if (Has(n, BladedWords)) return EType.Bladed;
                if (Has(n, BludgeonWords)) return EType.Bludgeoning;
                if (skirmish && Has(n, ThrustWords)) return EType.Thrusting;
                return EType.GenericMelee;
            }
            if (skirmish)
            {
                if (Has(n, MissileWords)) return EType.Missile;
                if (Has(n, GunpowderWords)) return EType.Gunpowder;
                return EType.GenericRanged;
            }
            if (Has(n, HeatWords)) return EType.Heat;
            if (Has(n, ExplosiveWords)) return EType.Explosive;
            if (Has(n, EnergyWords)) return EType.Energy;
            return EType.GenericRanged;
        }

        public static string TypeName(EType t) => t switch
        {
            EType.GenericMelee => "Generic Melee",
            EType.GenericRanged => "Generic Ranged",
            EType.Bladed => "Bladed",
            EType.Bludgeoning => "Bludgeoning",
            EType.Thrusting => "Thrusting",
            EType.Heat => "Heat",
            EType.Energy => "Energy",
            EType.Explosive => "Explosive",
            EType.Missile => "Missile",
            EType.Gunpowder => "Gunpowder",
            _ => t.ToString(),
        };

        // ================================================================= the tables

        /// <summary>One result on a critical hit table, broken into the pieces the book's entries are made of.</summary>
        public sealed record Effect(string Name, string Text)
        {
            /// <summary>"The hit gets AP(+4)".</summary>
            public int Ap { get; init; }
            /// <summary>"If the hit isn't blocked the target takes 1 wound".</summary>
            public bool WoundIfUnblocked { get; init; }
            /// <summary>"The target takes 2 hits without special rules".</summary>
            public int Hits { get; init; }
            /// <summary>"The closest enemy unit within 6" takes 1 hit without special rules".</summary>
            public bool Ricochet { get; init; }
            /// <summary>"Counts as having taken +X wounds when checking who won melee".</summary>
            public int MeleeWounds { get; init; }
            /// <summary>"Counts as having taken +2 wounds when checking if shooting wounds would trigger a Morale Test".</summary>
            public int MoraleWounds { get; init; }
            /// <summary>"The target gets -1 to hit rolls for this melee".</summary>
            public bool MeleeHitPenalty { get; init; }
            /// <summary>"Gets -1 to hit rolls when shooting until the end of the round".</summary>
            public bool ShootHitPenalty { get; init; }
            /// <summary>"Is Shaken at the end of melee / shooting regardless".</summary>
            public bool Shaken { get; init; }
            /// <summary>"The target must re-roll unmodified Defense rolls of 6".</summary>
            public bool RerollSixes { get; init; }
            /// <summary>"The target loses all melee special rules for this melee".</summary>
            public bool Stabbed { get; init; }
        }

        /// <summary>The book's entry for a die roll on a table, in the Advanced Rulebook's own words (GFF / AoFS p45). Sliced is +2 in Firefight, +1 in Skirmish (as printed).</summary>
        public static Effect Roll(EType type, int die, EGameSystem system)
        {
            int band = die <= 3 ? 0 : die <= 5 ? 1 : 2;
            bool skirmish = system == EGameSystem.AoFSkirmish;
            switch (type)
            {
                case EType.GenericMelee:
                    return band switch
                    {
                        0 => new Effect("Vital Area", "If the hit isn't blocked the target takes 1 wound.") { WoundIfUnblocked = true },
                        1 => new Effect("Exposed Spot", "The hit gets AP(+4), and if it isn't blocked the target takes 1 wound.") { Ap = 4, WoundIfUnblocked = true },
                        _ => new Effect("Master Strike", "The hit gets AP(+4), if it isn't blocked the target takes 1 wound, and counts as having taken +2 wounds when checking who won melee.") { Ap = 4, WoundIfUnblocked = true, MeleeWounds = 2 },
                    };
                case EType.GenericRanged:
                    return band switch
                    {
                        0 => new Effect("Weak Spot", "The hit gets AP(+4).") { Ap = 4 },
                        1 => new Effect("Ricochet", "The hit gets AP(+4), and the closest enemy unit within 6\" takes 1 hit without special rules.") { Ap = 4, Ricochet = true },
                        _ => new Effect("Master Shot", "The hit gets AP(+4), the closest enemy unit within 6\" takes 1 hit without special rules, and the target counts as having taken +2 wounds when checking if shooting wounds would trigger a Morale Test.") { Ap = 4, Ricochet = true, MoraleWounds = 2 },
                    };
                case EType.Bladed:
                    return band switch
                    {
                        0 => new Effect("Unprotected Area", "The target takes 2 hits without special rules.") { Hits = 2 },
                        1 => new Effect("Blade Storm", "The hit gets AP(+4), and the target takes 2 hits without special rules.") { Ap = 4, Hits = 2 },
                        _ => new Effect("Sliced", $"The hit gets AP(+4), the target takes 2 hits without special rules, and counts as having taken +{(skirmish ? "1 wound" : "2 wounds")} when checking who won melee.") { Ap = 4, Hits = 2, MeleeWounds = skirmish ? 1 : 2 },
                    };
                case EType.Bludgeoning:
                    return band switch
                    {
                        0 => new Effect("Lost Balance", "The target gets -1 to hit rolls for this melee.") { MeleeHitPenalty = true },
                        1 => new Effect("Wild Sweep", "The target takes 2 hits without special rules, and gets -1 to hit rolls for this melee.") { Hits = 2, MeleeHitPenalty = true },
                        _ => new Effect("Bludgeoned", "The target takes 2 hits without special rules, gets -1 to hit rolls for this melee, and is Shaken at the end of melee regardless of who won.") { Hits = 2, MeleeHitPenalty = true, Shaken = true },
                    };
                case EType.Thrusting:
                    return band switch
                    {
                        0 => new Effect("Stabbed", "The target loses all melee special rules for this melee.") { Stabbed = true },
                        1 => new Effect("Great Thrust", "The target loses all melee special rules for this melee, and counts as having taken +1 wound when checking who won melee.") { Stabbed = true, MeleeWounds = 1 },
                        _ => new Effect("Kebab", "If the hit isn't blocked the target takes 1 wound, the target loses all melee special rules for this melee, and counts as having taken +1 wounds when checking who won melee.") { WoundIfUnblocked = true, Stabbed = true, MeleeWounds = 1 },
                    };
                case EType.Heat:
                    return band switch
                    {
                        0 => new Effect("Roasted", "The target must re-roll unmodified Defense rolls of 6.") { RerollSixes = true },
                        1 => new Effect("Set Ablaze", "The target must re-roll unmodified Defense rolls of 6, and the closest enemy unit within 6\" takes 1 hit without special rules.") { RerollSixes = true, Ricochet = true },
                        _ => new Effect("Inferno", "The target must re-roll unmodified Defense rolls of 6, counts as having taken +2 wounds when checking if shooting wounds would trigger a Morale Test, and the closest enemy unit within 6\" takes 1 hit without special rules.") { RerollSixes = true, MoraleWounds = 2, Ricochet = true },
                    };
                case EType.Energy:
                case EType.Missile:
                    return band switch
                    {
                        0 => new Effect("Armor Shattered", "If the hit isn't blocked the target takes 1 wound.") { WoundIfUnblocked = true },
                        1 => new Effect("Energy Burst", "The hit gets AP(+4), and if it isn't blocked the target takes 1 wound.") { Ap = 4, WoundIfUnblocked = true },
                        _ => new Effect("Titanic Hit", "The hit gets AP(+4), if it isn't blocked the target takes 1 wound, and the target is Shaken at the end of shooting regardless of wounds.") { Ap = 4, WoundIfUnblocked = true, Shaken = true },
                    };
                case EType.Explosive:
                case EType.Gunpowder:
                    return band switch
                    {
                        0 => new Effect("Deadly Shrapnel", "The target takes 2 hits without special rules.") { Hits = 2 },
                        1 => new Effect("Concussion", "The target takes 2 hits without special rules, and gets -1 to hit rolls when shooting until the end of the round.") { Hits = 2, ShootHitPenalty = true },
                        _ => new Effect("Pulverized", "The hit gets AP(+4), the target takes 2 hits without special rules, and it gets -1 to hit rolls when shooting until the end of the round.") { Ap = 4, Hits = 2, ShootHitPenalty = true },
                    };
            }
            return new Effect("None", "");
        }

        /// <summary>The three rows of a table: "1-3", "4-5", "6".</summary>
        public static IReadOnlyList<(string Range, Effect Effect)> Rows(EType type, EGameSystem system) => new[]
        {
            ("1-3", Roll(type, 1, system)), ("4-5", Roll(type, 4, system)), ("6", Roll(type, 6, system)),
        };

        /// <summary>The tables a game uses with "By weapon type", in the book's order.</summary>
        public static IReadOnlyList<EType> TypedTables(EGameSystem system) => system == EGameSystem.AoFSkirmish
            ? new[] { EType.GenericMelee, EType.GenericRanged, EType.Bladed, EType.Bludgeoning, EType.Thrusting, EType.Missile, EType.Gunpowder }
            : new[] { EType.GenericMelee, EType.GenericRanged, EType.Bladed, EType.Bludgeoning, EType.Heat, EType.Energy, EType.Explosive };

        /// <summary>Which weapon names use a table (what the name is read for), for the hovers.</summary>
        public static string NamesFor(EType type) => type switch
        {
            EType.Bladed => "sword, blade, axe, claw, knife...",
            EType.Bludgeoning => "hammer, club, flail, mace, fist...",
            EType.Thrusting => "spear, halberd, pike, lance...",
            EType.Heat => "flame, fusion, melta, incinerator...",
            EType.Energy => "plasma, laser, beam, lightning...",
            EType.Explosive => "missile, rocket, cannon, grenade...",
            EType.Missile => "bow, javelin, crossbow, sling...",
            EType.Gunpowder => "rifle, pistol, gun, musket...",
            EType.GenericMelee => "anything else, in melee",
            _ => "anything else, shooting",
        };

        /// <summary>One table as hover text: a heading line and its three rows.</summary>
        public static string TableText(EType type, EGameSystem system, bool withNames = false)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(TypeName(type).ToUpperInvariant());
            if (withNames) sb.Append(" (").Append(NamesFor(type)).Append(')');
            foreach (var (range, e) in Rows(type, system))
                sb.Append('\n').Append(range).Append(" ").Append(e.Name).Append(": ").Append(e.Text);
            return sb.ToString();
        }

        /// <summary>A weapon's hover addition: which critical table it rolls on, and the table.</summary>
        public static string WeaponTableText(EGameSystem system, string weaponName, bool ranged, bool melee, bool typed)
        {
            var parts = new List<string>();
            if (ranged) parts.Add(TableText(TypeOf(system, weaponName, melee: false, typed), system));
            if (melee) parts.Add(TableText(TypeOf(system, weaponName, melee: true, typed), system));
            return "Critical table (on a 6 to hit):\n" + string.Join("\n\n", parts);
        }

        /// <summary>Every table a game uses with "By weapon type" - the lobby's hover.</summary>
        public static string AllTablesText(EGameSystem system) =>
            string.Join("\n\n", TypedTables(system).Select(t => TableText(t, system, withNames: true)));

        /// <summary>What a result does to this target, in plain words.</summary>
        public static string Describe(Effect e, string target)
        {
            var bits = new List<string>();
            if (e.Ap > 0) bits.Add(e.WoundIfUnblocked ? $"1 hit at AP(+{e.Ap}), +1 wound if it isn't blocked" : $"1 hit at AP(+{e.Ap})");
            else if (e.WoundIfUnblocked) bits.Add("+1 wound if the hit isn't blocked");
            if (e.Hits > 0) bits.Add($"{target} takes {e.Hits} hits (no AP)");
            if (e.Ricochet) bits.Add("the closest other enemy within 6\" takes 1 hit");
            if (e.MeleeWounds > 0) bits.Add($"{target} counts +{e.MeleeWounds} wound{(e.MeleeWounds == 1 ? "" : "s")} for who won the melee");
            if (e.MoraleWounds > 0) bits.Add($"{target} counts +{e.MoraleWounds} wounds for its morale check");
            if (e.MeleeHitPenalty) bits.Add($"{target} gets -1 to hit this melee");
            if (e.ShootHitPenalty) bits.Add($"{target} gets -1 to hit when shooting this round");
            if (e.RerollSixes) bits.Add($"{target} re-rolls Defense 6s");
            if (e.Stabbed) bits.Add($"{target} loses its melee special rules this melee");
            if (e.Shaken) bits.Add($"{target} is Shaken at the end");
            return bits.Count == 0 ? e.Text : char.ToUpperInvariant(bits[0][0]) + string.Join(", ", bits)[1..];
        }

        // ================================================================= dealing one

        /// <summary>What the hit-roll stage must still do with the hits themselves.</summary>
        public sealed record Dealt(Effect Effect, EType Type);

        /// <summary>
        /// Checks for a critical hit after a hit roll and resolves the parts that act on units (extra hits, the ricochet,
        /// the markers read later). Returns null when none is dealt. The caller peels the critical hit off for AP / the
        /// unblocked wound and carries the Defense re-roll.
        /// </summary>
        public static async Task<Dealt?> Check(IGameContext c, IUnit attacker, IUnit defender, IWeapon weapon,
            bool melee, int sixesRolled, int hitRollNeeded)
        {
            if (!On(c) || sixesRolled <= 0 || hitRollNeeded >= 6) return null;
            if (attacker.Tokens.HasToken(TokenType.CritDealt)) return null;
            attacker.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.CritDealt));

            EType type = TypeOf(c.Settings.GameSystem, weapon.Name, melee, TypedOn(c));
            int die = HordePlacement.RollD6(c);
            Effect e = Roll(type, die, c.Settings.GameSystem);
            c.Log($"Critical Hit! {attacker.Name}'s {weapon.Name} ({TypeName(type)}) rolls {die}: {e.Name} - {e.Text}");
            var rows = Rows(type, c.Settings.GameSystem);
            int picked = die <= 3 ? 0 : die <= 5 ? 1 : 2;
            await c.Presenter.Present(new CriticalHitBeat(attacker.ID, defender.ID, attacker.Name, defender.Name, weapon.Name,
                TypeName(type), die, rows.Select(r => new CriticalRow(r.Range, r.Effect.Name, r.Effect.Text)).ToList(), picked,
                Describe(e, defender.Name)), default);

            if (e.MeleeWounds > 0) Add(defender, TokenType.CritMeleeWounds, e.MeleeWounds);
            if (e.MoraleWounds > 0) Add(defender, TokenType.CritMoraleWounds, e.MoraleWounds);
            if (e.MeleeHitPenalty && !defender.Tokens.HasToken(TokenType.CritMeleeHitPenalty)) Add(defender, TokenType.CritMeleeHitPenalty, 1);
            if (e.ShootHitPenalty && !defender.Tokens.HasToken(TokenType.CritShootHitPenalty)) Add(defender, TokenType.CritShootHitPenalty, 1);
            if (e.Shaken && !defender.Tokens.HasToken(TokenType.CritShaken)) Add(defender, TokenType.CritShaken, 1);
            if (e.Stabbed && !defender.Tokens.HasToken(TokenType.CritStabbed)) Add(defender, TokenType.CritStabbed, 1);

            if (e.Hits > 0 && defender.GetIsAlive())
                await CampaignEventRunner.DealHits(c, defender, e.Hits, $"{Label} - {e.Name}");
            if (e.Ricochet && ClosestOtherEnemy(c, attacker, defender) is { } other)
            {
                c.Log($"{e.Name}: {other.Name} is the closest enemy within 6\" - it takes 1 hit.");
                await CampaignEventRunner.DealHits(c, other, 1, $"{Label} - {e.Name}");
            }
            return new Dealt(e, type);
        }

        private static void Add(IUnit unit, TokenType type, int n)
        {
            for (int i = 0; i < n; i++) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(type));
        }

        private static int TeamOf(IGameContext c, IUnit unit) =>
            c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(unit.PlayerID))?.TeamNumber ?? -1;

        /// <summary>"The closest enemy unit within 6"": the attacker's closest other enemy within 6" of the target.</summary>
        public static UnitData? ClosestOtherEnemy(IGameContext c, IUnit attacker, IUnit target)
        {
            int team = TeamOf(c, attacker);
            UnitData? best = null;
            float bestD = float.MaxValue;
            foreach (UnitData u in c.TableState.Units.Objects.OfType<UnitData>())
            {
                if (!u.GetIsAlive() || u.ID.Equals(target.ID) || u.ID.Equals(attacker.ID) || ReserveRules.IsInReserve(u)) continue;
                if (TeamOf(c, u) == team) continue;
                float d = UnitCompareUtilities.MinDistanceBetweenUnits(u, target, out _, out _, includeVertical: true);
                if (d <= 6f + 0.001f && d < bestD) { bestD = d; best = u; }
            }
            return best;
        }

        // ================================================================= read back later

        /// <summary>Extra wounds this unit counts as having taken when checking who won its melee.</summary>
        public static int MeleeWoundsCounted(IUnit unit) => unit.Tokens.GetTokenCount(TokenType.CritMeleeWounds);

        /// <summary>Extra wounds this unit counts as having taken for its shooting morale check.</summary>
        public static int MoraleWoundsCounted(IUnit unit) => unit.Tokens.GetTokenCount(TokenType.CritMoraleWounds);

        /// <summary>The -1 to hit a critical hit put on this unit, for this melee or for shooting this round.</summary>
        public static int HitPenalty(IUnit unit, bool melee) =>
            melee ? (unit.Tokens.HasToken(TokenType.CritMeleeHitPenalty) ? -1 : 0)
                  : (unit.Tokens.HasToken(TokenType.CritShootHitPenalty) ? -1 : 0);

        /// <summary>
        /// Thrusting weapons: "the target loses all melee special rules for this melee". True when this unit's own rules
        /// are being asked about as it fights (its attacks, its Fear, its Counter). Its rules for being attacked (saves,
        /// wound-ignore) and for morale stay - they aren't the rules it fights with.
        /// </summary>
        public static bool LosesMeleeRules(IUnit unit, IHookContext context)
        {
            if (!unit.Tokens.HasToken(TokenType.CritStabbed)) return false;
            return context switch
            {
                HitRollCompleteContext h => h.IsMelee,
                HitRollModifierContext m => m.IsMelee,
                SaveRollCompleteContext s => s.IsMelee,
                AttackWindowContext a => a.IsMelee,
                MeleeResolutionContext => true,
                CounterTriggerContext => true,
                PreApplyWoundContext => true,
                _ => false,
            };
        }

        /// <summary>End of every activation: "at the end of melee / shooting" Shaken, then the per-fight markers go.</summary>
        public static async Task EndOfActivation(IGameContext c)
        {
            foreach (UnitData unit in c.TableState.Units.Objects.OfType<UnitData>().ToList())
            {
                if (unit.Tokens.HasToken(TokenType.CritShaken))
                {
                    unit.Tokens.RemoveTokens(TokenType.CritShaken, unit.Tokens.GetTokenCount(TokenType.CritShaken));
                    if (unit.GetIsAlive() && !unit.Tokens.HasToken(TokenType.Shaken) && UnitPsychology.BindingOf(c, unit) is { } binding)
                    {
                        c.Log($"{Label}: {unit.Name} is Shaken, whatever the result.");
                        await MoraleUtilities.ApplyShakenWithPresentation(c, binding);
                    }
                }
                foreach (TokenType t in new[] { TokenType.CritDealt, TokenType.CritMeleeWounds, TokenType.CritMoraleWounds,
                             TokenType.CritMeleeHitPenalty, TokenType.CritStabbed })
                {
                    int n = unit.Tokens.GetTokenCount(t);
                    if (n > 0) unit.Tokens.RemoveTokens(t, n);
                }
            }
        }

        /// <summary>End of the round: "-1 to hit rolls when shooting until the end of the round" ends.</summary>
        public static void RoundEnd(IGameContext c)
        {
            foreach (IUnit unit in c.TableState.Units.Objects)
            {
                int n = unit.Tokens.GetTokenCount(TokenType.CritShootHitPenalty);
                if (n > 0) unit.Tokens.RemoveTokens(TokenType.CritShootHitPenalty, n);
            }
        }
    }
}
