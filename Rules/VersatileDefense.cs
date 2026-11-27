using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG
{
    /// <summary>
    /// Versatile Defense (Army Forge 3.6.0, every system that has it): "When a unit where most models have this rule is
    /// shot/charged from over 9"/6" away, pick one effect: the unit either gets +1 to Defense rolls, or enemy units get -1
    /// to hit rolls."
    ///
    /// Picked by the defender's player as the attack comes in - once per enemy unit's shooting (or charge) at it, kept for
    /// every weapon of that attack. Until 2026-11-27 the data played the older wording ("when deployed or activated, pick
    /// one effect ... until the unit's next activation"), a stance chosen in advance that covered a whole round.
    /// </summary>
    public static class VersatileDefense
    {
        public const string Name = "Versatile Defense";
        public const string Guard = "+1 to Defense rolls";
        public const string Evasion = "Enemy -1 to hit rolls";
        public const float ShotInches = 9f, ChargedInches = 6f;

        private sealed class Picks { public readonly Dictionary<(UnitID Attacker, UnitID Defender, bool Melee), string> By = new(); }
        private static readonly ConditionalWeakTable<object, Picks> State = new();

        /// <summary>"A unit where most models have this rule" - on the model, granted to the unit, or static on the unit for
        /// a native model (a joined hero does not inherit the host's rules).</summary>
        public static bool MostModelsHave(IUnit unit)
        {
            bool Named(Rules.Dispatch.ResolvedRule r) => string.Equals(r.Definition.Name, Name, StringComparison.OrdinalIgnoreCase);
            bool granted = RuleGrantQueries.UnitHasGrantedRule(unit, Name);
            bool unitStatic = unit.RuleDefinitions.Any(Named);
            int living = 0, carriers = 0;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                living++;
                bool joinedHero = unit.JoinedHeroModelId is ModelID hid && model.ID == hid;
                if (granted || model.RuleDefinitions.Any(Named) || (!joinedHero && unitStatic)) carriers++;
            }
            return living > 0 && carriers * 2 > living;
        }

        /// <summary>"Shot / charged from over 9"/6" away": a shooting from over 9" (spells are not shooting), or a charge
        /// that started over 6" away (a strike-back is not being charged).</summary>
        public static bool InBand(bool melee, bool spell, bool charging, float distance, float chargeOrigin) =>
            !spell && (melee ? charging && chargeOrigin > ChargedInches : distance > ShotInches);

        /// <summary>
        /// The attack's first hit roll: the defender's player picks the effect (once per attacker and attack kind this
        /// activation). Returns the -1 to hit when Evasion is picked.
        /// </summary>
        public static async Task<IReadOnlyList<GrantedModifier>> BeforeHitRoll(IGameContext c, IUnit attacker, IUnit defender,
            bool melee, bool spell, bool charging, float distance, float chargeOrigin, int hitNeeded)
        {
            if (!InBand(melee, spell, charging, distance, chargeOrigin) || !MostModelsHave(defender))
                return Array.Empty<GrantedModifier>();
            Picks picks = State.GetValue(c.GameDataStore, _ => new Picks());
            var key = (attacker.ID, defender.ID, melee);
            if (!picks.By.TryGetValue(key, out string? pick))
            {
                var options = new List<string> { Evasion, Guard };
                // A bot takes the one that changes something: -1 to hit does nothing when only 6s already hit.
                string bot = hitNeeded >= 6 ? Guard : Evasion;
                pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(new StringSelectionRequest(
                    defender.PlayerID,
                    $"{defender.Name} is {(melee ? "charged" : "shot")} from over {(melee ? ChargedInches : ShotInches):0}\" away by {attacker.Name}. Versatile Defense: pick one effect.",
                    options, new List<StringSelectionRequest.InvalidOption>(), displayName: Name) { Recommended = bot });
                if (pick != Guard && pick != Evasion) pick = bot;
                picks.By[key] = pick;
                c.Log($"Versatile Defense: {defender.Name} takes \"{pick}\" against {attacker.Name}.");
            }
            return pick == Evasion ? new[] { new GrantedModifier(Name, -1) } : Array.Empty<GrantedModifier>();
        }

        /// <summary>The Defense roll of the same attack: +1 when Guard was picked.</summary>
        public static IReadOnlyList<GrantedModifier> SaveModifiers(IGameContext c, IUnit attacker, IUnit defender, bool melee) =>
            State.TryGetValue(c.GameDataStore, out Picks? picks) && picks.By.TryGetValue((attacker.ID, defender.ID, melee), out string? pick) && pick == Guard
                ? new[] { new GrantedModifier(Name, +1) }
                : Array.Empty<GrantedModifier>();

        /// <summary>The end of an activation: every pick made during it is spent.</summary>
        public static void EndOfActivation(IGameContext c)
        {
            if (State.TryGetValue(c.GameDataStore, out Picks? picks)) picks.By.Clear();
        }
    }
}
