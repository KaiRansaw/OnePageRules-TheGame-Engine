using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Missions;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.StateMachine;
using FDG.Utilities;
using FDG.Rules.Dispatch;

namespace FDG
{
    /// <summary>
    /// Brutal Damage (Advanced Rulebooks 3.6.0, GF p48 / AoF p48 - only those two books print it): "Brutal Damage rules
    /// make the game deadlier by modifying how units are damaged during combat. Pick any of the following". Judgement
    /// calls in house rules C, "Brutal Damage readings".
    /// </summary>
    public static class BrutalDamage
    {
        public const string DeathZone = "Death Zone";
        public const string HeavyDamage = "Heavy Damage";
        public const string DirectionalDamage = "Directional Damage";
        public const string UnitAttrition = "Unit Attrition";
        public const string FriendlyFire = "Friendly Fire";
        public const string HorribleMishaps = "Horrible Mishaps";
        public const string CriticalHits = "Critical Hits";

        public static readonly IReadOnlyList<(string Name, EAdvancedRules Flag)> All = new (string, EAdvancedRules)[]
        {
            (DeathZone, EAdvancedRules.DeathZone),
            (HeavyDamage, EAdvancedRules.HeavyDamage),
            (DirectionalDamage, EAdvancedRules.DirectionalDamage),
            (UnitAttrition, EAdvancedRules.UnitAttrition),
            (FriendlyFire, EAdvancedRules.FriendlyFire),
            (HorribleMishaps, EAdvancedRules.HorribleMishaps),
            (CriticalHits, EAdvancedRules.BrutalCriticalHits),
        };

        /// <summary>Only Grimdark Future and Age of Fantasy print Brutal Damage.</summary>
        public static bool BooksHaveIt(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy;

        public static string BookText(string name) => name switch
        {
            DeathZone => "When a unit with Tough(6) or higher is destroyed, all units within X/2\" must take a Dangerous Terrain Test, where X is the unit's Tough(X) value.",
            HeavyDamage => "When a unit with Tough(6) or higher has taken X or more wounds, it gets the following penalties:\n3 wounds = -2\" Move; 6 = -2\" Move, -1 Quality; 9 = -2\" Move, -1 Quality, -1 Defense; 12 = -4\" Move, -1 Quality, -1 Defense; 15 = -4\" Move, -2 Quality, -1 Defense; 18 = -4\" Move, -2 Quality, -2 Defense; 21 = -6\" Move, -2 Quality, -2 Defense.",
            DirectionalDamage => "Players agree which units are big enough (monsters and chariots recommended) and which parts are their front, side and rear facings. When a unit shoots at a big unit, draw a straight line from all shooting models to the center of the target; the facing most lines pass through is the target facing.\nHits from shooting in its front facing: +1 to Defense rolls; in its rear facing: -1 to Defense rolls, and when taking wounds in its rear facing it takes D3 extra wounds.",
            UnitAttrition => "When a unit would have to take a Morale Test, it must take as many Morale Tests as the number of wounds it suffered instead, and for each failed Morale Test: take 2 additional wounds if in melee, 1 otherwise. These wounds can't be ignored in any way, and they don't trigger any additional Morale Tests. Unit attrition replaces regular Morale Tests from wounds, so units are never Shaken or Routed from wounds.",
            FriendlyFire => "When a unit shoots at an enemy that is within 3\" of another friendly unit, then for every hit landed roll one die. On a 1-3 the hit lands on the friendly unit, on a 4-6 the hit lands on the targeted enemy unit. If there are multiple friendly units within 3\", randomly determine which one is hit.",
            HorribleMishaps => "When a unit rolls a 1 whilst shooting, roll one die: on a 1-3 the unit takes 1 wound, on a 4-6 nothing happens. Wounds taken this way don't trigger Morale Tests.",
            CriticalHits => "When a unit rolls a 6 whilst shooting, roll one die: on a 1-3 nothing happens, on a 4-6 the target unit takes 1 wound. This wound is in addition to any regular damage.",
            _ => "",
        };

        public static string GameText(string name) => name switch
        {
            DeathZone => "Checked whenever a Tough(6)+ unit is destroyed (its toughest model's Tough). Every other unit, friend or foe, within half that tests.",
            HeavyDamage => "Units with a Tough(6)+ model. Wounds taken = the unit's lost wounds. Quality penalties apply to hit rolls and morale tests.",
            DirectionalDamage => "\"Big\" = a unit of one model with Tough(6)+. Facings are 90-degree arcs: front and rear centred on the way it faces, sides between. Only shooting.",
            UnitAttrition => "Replaces the morale test for wounds (shooting to half strength, and losing a melee). One test per wound suffered in that attack or melee.",
            FriendlyFire => "Friendly = the shooter's side. A hit that strays rolls Defense on the friendly unit with no AP and no weapon rules.",
            HorribleMishaps => "Each unmodified 1 to hit when shooting.",
            CriticalHits => "Each unmodified 6 to hit when shooting. The extra wound can't be saved.",
            _ => "",
        };

        public static bool IsOn(IGameContext c, EAdvancedRules flag) => AdvancedRules.IsOn(c, flag);

        private static int D6(IGameContext c) => HordePlacement.RollD6(c);

        /// <summary>The unit's Tough value - its toughest living model (1 for an ordinary model).</summary>
        public static int ToughOf(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive()).Select(m => (int)MathF.Round(m.TotalWounds)).DefaultIfEmpty(1).Max();

        private static int ToughEver(IUnit unit) =>
            unit.Models.Select(m => (int)MathF.Round(m.TotalWounds)).DefaultIfEmpty(1).Max();

        // ================================================================= Death Zone

        /// <summary>"When a unit with Tough(6) or higher is destroyed, all units within X/2" must take a Dangerous
        /// Terrain Test."</summary>
        public static async Task UnitDestroyed(IGameContext c, IUnit dead)
        {
            if (!IsOn(c, EAdvancedRules.DeathZone)) return;
            int tough = ToughEver(dead);
            if (tough < 6) return;
            float radius = tough / 2f;
            List<UnitData> near = c.TableState.Units.Objects.OfType<UnitData>()
                .Where(u => u.GetIsAlive() && !u.ID.Equals(dead.ID) && !ReserveRules.IsInReserve(u)
                            && UnitPsychology.DistanceToFallen(u, dead) <= radius + 0.001f)
                .ToList();
            if (near.Count == 0) return;
            await c.Announce($"Death Zone: {dead.Name} goes down - every unit within {radius:0.#}\" takes a Dangerous Terrain Test!",
                new TextColor(255, 120, 60, 255), EBannerTier.Toast);
            foreach (UnitData unit in near) await QuestTreasure.DangerousTest(c, unit, DeathZone);
        }

        // ================================================================= Heavy Damage

        /// <summary>The Heavy Damage penalties for a unit's wounds taken: (move inches, quality, defense) as negatives.</summary>
        public static (float Move, int Quality, int Defense) HeavyPenalty(IUnit unit)
        {
            if (ToughEver(unit) < 6) return (0f, 0, 0);
            int taken = (int)MathF.Floor(unit.MaxWounds - unit.RemainingWounds + 0.001f);
            return taken switch
            {
                >= 21 => (-6f, -2, -2),
                >= 18 => (-4f, -2, -2),
                >= 15 => (-4f, -2, -1),
                >= 12 => (-4f, -1, -1),
                >= 9 => (-2f, -1, -1),
                >= 6 => (-2f, -1, 0),
                >= 3 => (-2f, 0, 0),
                _ => (0f, 0, 0),
            };
        }

        public static (float Move, int Quality, int Defense) HeavyPenalty(GameSettings settings, IUnit unit) =>
            AdvancedRules.IsOn(settings, EAdvancedRules.HeavyDamage) ? HeavyPenalty(unit) : (0f, 0, 0);

        // ================================================================= Directional Damage

        /// <summary>"Big enough": a unit of one model with Tough(6) or more (house rules C).</summary>
        public static bool IsBig(IUnit unit) => unit.Models.Count == 1 && ToughEver(unit) >= 6;

        public enum EFacing { None, Front, Side, Rear }

        /// <summary>The facing most of the shooters' lines to the target's centre pass through.</summary>
        public static EFacing FacingHit(IUnit shooter, IUnit target)
        {
            if (!IsBig(target)) return EFacing.None;
            IModel? big = target.Models.FirstOrDefault(m => m.GetIsAlive());
            if (big == null) return EFacing.None;
            float fx = big.Facing.X, fz = big.Facing.Y;
            float flen = MathF.Sqrt(fx * fx + fz * fz);
            if (flen < 0.001f) return EFacing.None;
            fx /= flen; fz /= flen;
            var counts = new Dictionary<EFacing, int>();
            foreach (IModel m in shooter.Models.Where(m => m.GetIsAlive()))
            {
                float dx = m.Position.x - big.Position.x, dz = m.Position.z - big.Position.z;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len < 0.001f) continue;
                float cos = (dx * fx + dz * fz) / len;     // towards the shooter, against the way it faces
                EFacing f = cos >= MathF.Sqrt(0.5f) ? EFacing.Front : cos <= -MathF.Sqrt(0.5f) ? EFacing.Rear : EFacing.Side;
                counts[f] = counts.GetValueOrDefault(f) + 1;
            }
            if (counts.Count == 0) return EFacing.None;
            int best = counts.Values.Max();
            // A tie between arcs: the side (the middle reading).
            return counts.Count(kv => kv.Value == best) > 1 ? EFacing.Side : counts.First(kv => kv.Value == best).Key;
        }

        // ================================================================= Unit Attrition

        public static bool AttritionOn(IGameContext c) => IsOn(c, EAdvancedRules.UnitAttrition);

        /// <summary>"It must take as many Morale Tests as the number of wounds it suffered instead, and for each failed
        /// Morale Test take 2 additional wounds if in melee, 1 otherwise."</summary>
        public static async Task Attrition(IGameContext c, IUnit unit, float woundsSuffered, bool melee)
        {
            if (unit is not UnitData data || !unit.GetIsAlive()) return;
            int tests = (int)MathF.Floor(woundsSuffered + 0.001f);
            if (tests <= 0) return;
            int failed = 0;
            for (int i = 0; i < tests; i++)
            {
                var outcome = await MoraleUtilities.TakeMoraleTest(c, unit,
                    HeroStatRules.GetMoraleQuality(data, c.TableState), melee ? EMoraleTrigger.Melee : EMoraleTrigger.Shooting);
                if (!outcome.Passed) failed++;
            }
            int extra = failed * (melee ? 2 : 1);
            c.Log($"Unit Attrition: {unit.Name} takes {tests} test{(tests == 1 ? "" : "s")}, fails {failed} - {extra} more wound{(extra == 1 ? "" : "s")}.");
            if (extra > 0)
            {
                await QuestActions.Toast(c, $"Unit Attrition: {unit.Name} loses {extra} more wound{(extra == 1 ? "" : "s")}.");
                await CasualtyPresentation.ApplyUnitWounds(c, data, extra);
            }
        }

        // ================================================================= Friendly Fire

        /// <summary>The shooter's friends within 3" of the target (its own unit excluded).</summary>
        public static List<UnitData> FriendsNear(IGameContext c, IUnit shooter, IUnit target)
        {
            if (!IsOn(c, EAdvancedRules.FriendlyFire)) return new List<UnitData>();
            int team = c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(shooter.PlayerID))?.TeamNumber ?? -1;
            return c.TableState.Units.Objects.OfType<UnitData>()
                .Where(u => u.GetIsAlive() && !u.ID.Equals(shooter.ID) && !u.ID.Equals(target.ID) && !ReserveRules.IsInReserve(u)
                            && (c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(u.PlayerID))?.TeamNumber ?? -2) == team
                            && UnitCompareUtilities.MinDistanceBetweenUnits(u, target, out _, out _, includeVertical: true) <= 3f + 0.001f)
                .ToList();
        }

        /// <summary>One die per hit landed: how many go astray (1-3), and which friend each one strikes.</summary>
        public static List<UnitData> StrayHits(IGameContext c, int hits, List<UnitData> friends)
        {
            var strays = new List<UnitData>();
            if (friends.Count == 0) return strays;
            for (int i = 0; i < hits; i++)
            {
                if (D6(c) > 3) continue;
                strays.Add(friends.Count == 1 ? friends[0] : friends[(D6(c) - 1) % friends.Count]);
            }
            return strays;
        }

        /// <summary>The stray hits land: Defense rolls with no AP and no weapon rules.</summary>
        public static async Task ResolveStrays(IGameContext c, IUnit shooter, List<UnitData> strays)
        {
            foreach (var group in strays.GroupBy(u => u.ID))
            {
                UnitData friend = group.First();
                int n = group.Count();
                await QuestActions.Toast(c, $"Friendly Fire: {n} of {shooter.Name}'s hits stray into {friend.Name}!");
                await CampaignEventRunner.DealHits(c, friend, n, $"{FriendlyFire} ({shooter.Name})");
            }
        }

        // ================================================================= Horrible Mishaps / Critical Hits

        /// <summary>Per unmodified 1 when shooting, 1-3 on a die: one wound to the shooter (no morale).</summary>
        public static int Mishaps(IGameContext c, int ones)
        {
            if (!IsOn(c, EAdvancedRules.HorribleMishaps) || ones <= 0) return 0;
            int wounds = 0;
            for (int i = 0; i < ones; i++) if (D6(c) <= 3) wounds++;
            if (wounds > 0) c.Log($"Horrible Mishaps: {ones} roll{(ones == 1 ? "" : "s")} of 1 - {wounds} wound{(wounds == 1 ? "" : "s")} to the shooter.");
            return wounds;
        }

        /// <summary>Per unmodified 6 when shooting, 4-6 on a die: one extra wound to the target.</summary>
        public static int Criticals(IGameContext c, int sixes)
        {
            if (!IsOn(c, EAdvancedRules.BrutalCriticalHits) || sixes <= 0) return 0;
            int wounds = 0;
            for (int i = 0; i < sixes; i++) if (D6(c) >= 4) wounds++;
            if (wounds > 0) c.Log($"Critical Hits: {sixes} roll{(sixes == 1 ? "" : "s")} of 6 - {wounds} extra wound{(wounds == 1 ? "" : "s")} to the target.");
            return wounds;
        }
    }
}
