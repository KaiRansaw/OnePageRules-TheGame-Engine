using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Reinforcing Units - new in OPR 3.6.0 Horde Mode (#409):
    ///
    /// <para><i>"Defenders may spend points to reinforce units that are down to half or less of their
    /// total size or Tough value (for single-model units) and that are within 9" of a seized objective.
    /// Spend half as many points as the unit's total point value (rounding up), and then restore up to
    /// half of its total models or wounds (for single-model units)."</i></para>
    ///
    /// <para>The third thing the Horde shop can do, beside recruiting new units and buying upgrades, and
    /// the only one that heals. It fills the gap those two left: a veteran squad carrying 20 XP and two
    /// level-up traits used to be worth no more than its corpse once it was cut down, because the only way
    /// to replace the bodies was to recruit a fresh unit with no history. Reinforcing keeps the unit - and
    /// everything it has earned - on the table.</para>
    ///
    /// <para>Kept free of stage plumbing (like <see cref="FDG.StateMachine.HordeRules"/>) so the
    /// eligibility test and the arithmetic can be unit-tested without a game running.</para>
    /// </summary>
    public static class HordeReinforce
    {
        /// <summary>
        /// Half the unit's own point value, rounding up - what reinforcing it costs. A joined hero's
        /// points are not part of it: the hero is a separate model that was never lost, and the shop's
        /// other prices leave it out the same way.
        /// </summary>
        public static int Cost(int ownPointCost) => (Math.Max(0, ownPointCost) + 1) / 2;

        /// <summary>
        /// Whether a unit is damaged enough to reinforce: "down to half or less of their total size or
        /// Tough value (for single-model units)".
        ///
        /// <para>The same half-strength test the morale rules use, so a unit that is taking Morale Tests
        /// for its losses is exactly a unit that may be reinforced - which is the rule reading the right
        /// way round, and means a player never has to work out two different fractions.</para>
        /// </summary>
        public static bool IsAtHalfStrength(int totalModels, int livingModels,
            float totalWounds, float remainingWounds)
        {
            // Single-model units measure in wounds (its Tough value); multi-model units in bodies.
            if (totalModels <= 1) return remainingWounds * 2f <= totalWounds;
            return livingModels * 2 <= totalModels;
        }

        /// <summary>
        /// How many models a multi-model unit gets back: up to half its total size, and never more than it
        /// is missing. A 10-model squad down to 3 restores 5 (back to 8); down to 1, it restores 5 (back
        /// to 6); a 3-model unit down to 1 restores 2 (rounding its half DOWN, so the unit cannot come
        /// back stronger than the rule's "up to half").
        /// </summary>
        public static int ModelsRestored(int totalModels, int livingModels) =>
            Math.Max(0, Math.Min(totalModels / 2, totalModels - livingModels));

        /// <summary>
        /// How many wounds a single-model unit gets back: up to half its Tough value, and never more than
        /// it has lost.
        /// </summary>
        public static int WoundsRestored(float totalWounds, float remainingWounds)
        {
            int half = (int)MathF.Floor(totalWounds / 2f);
            int missing = (int)MathF.Round(Math.Max(0f, totalWounds - remainingWounds));
            return Math.Max(0, Math.Min(half, missing));
        }

        /// <summary>
        /// Applies the restoration to a unit on the table, and reports what it got back in words for the
        /// log and the banner.
        ///
        /// <para>Multi-model units bring back whole models, dead ones first and fullest-first among the
        /// living, so the squad regains bodies rather than topping up a wounded survivor - which is what
        /// "restore up to half of its total MODELS" means. Single-model units take their wounds back on
        /// the one model they have.</para>
        ///
        /// <para>Does NOT clear Shaken: the rule restores models and wounds, nothing else. A Shaken unit
        /// that is reinforced is still Shaken and still has to spend an activation idle.</para>
        /// </summary>
        public static string Apply(UnitData unit, IReadOnlyList<ModelData> ownModels)
        {
            int totalModels = ownModels.Count;
            int living = ownModels.Count(m => m.GetIsAlive());

            if (totalModels <= 1)
            {
                ModelData model = ownModels[0];
                int wounds = WoundsRestored(model.TotalWounds, model.RemainingWoundsBinding.GetValue());
                if (wounds <= 0) return "nothing to restore";
                model.RemainingWoundsBinding.SetValue(
                    MathF.Min(model.TotalWounds, model.RemainingWoundsBinding.GetValue() + wounds));
                return $"+{wounds} wound{(wounds == 1 ? "" : "s")}";
            }

            int restore = ModelsRestored(totalModels, living);
            if (restore <= 0) return "nothing to restore";

            // Dead models first (that is what a reinforcement IS), then any wounded survivor, so a
            // multi-wound squad's bodies come back before its scratches are healed.
            foreach (ModelData model in ownModels
                         .Where(m => !m.GetIsAlive() || m.WoundsDealt > 0f)
                         .OrderBy(m => m.GetIsAlive() ? 1 : 0)
                         .Take(restore))
            {
                model.RemainingWoundsBinding.SetValue(model.TotalWounds);
            }

            return $"+{restore} model{(restore == 1 ? "" : "s")}";
        }
    }
}
