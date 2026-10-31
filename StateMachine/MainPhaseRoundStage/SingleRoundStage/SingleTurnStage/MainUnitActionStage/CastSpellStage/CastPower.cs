using System;
using FDG.Data;
using FDG.Quest;

namespace FDG.Stages
{
    /// <summary>
    /// #414 - the Power a Hero may put into a cast roll, which is a **v2.1.0 addition**. GFSQ Core Rules
    /// v2.1.0, Caster(X): *"Heroes may spend any number of tokens/Power before rolling to get +1 to the roll
    /// per token/Power, or to give an enemy Caster within 18" -1 to their roll per token/Power."* The POWER
    /// REFERENCE repeats it: *"Heroes with Caster may spend X Power to get +X to casting rolls or give
    /// enemies -X to casting rolls."*
    ///
    /// <para>
    /// v2.0.1 allowed only spell TOKENS before the roll, and let a hero rescue a failed cast afterwards for
    /// <c>X*2</c> Power. 2.1.0 deletes that retroactive rescue and moves the Power to the front, so casting
    /// stops being "roll, then buy the result" and becomes a commitment made before the die is thrown - the
    /// same reshaping 2.1.0 applied to the failed Hero Skill. (The engine never implemented the retroactive
    /// rescue, so there was nothing to remove: what was missing is this half.)
    /// </para>
    ///
    /// <para>
    /// This is the deliberate counterpart to <see cref="SpellPurse"/>, which answers the same question for
    /// tokens. It stays a separate class rather than another pool inside the purse, because the two are not
    /// interchangeable: a token is a caster's renewable per-round currency and can be lent by an
    /// accumulator, while Power is a Hero's whole-mission budget, is refilled only by a Rest action, is
    /// never lent, and is what every OTHER Star Quest decision also competes for. Folding Power into the
    /// purse would let a spell's own cost be paid with it, which the book does not allow - Power buys
    /// modifiers to the roll, never the attempt itself.
    /// </para>
    /// </summary>
    public static class CastPower
    {
        /// <summary>
        /// Power this unit may put into a cast roll right now: a Star Quest Hero's current Power, and zero
        /// for anything else. AI casters and ordinary army casters have no Power - the book's
        /// <c>Caster(X) [AI Units]</c> entry spends tokens only - so outside Star Quest this returns 0
        /// everywhere and casting behaves exactly as it did.
        /// </summary>
        public static int Available(UnitData? unit)
        {
            if (unit == null) return 0;
            // The hero sheet is the test, not a nonzero QuestPower: QuestPower is a plain int on every
            // UnitData, so a non-hero could in principle carry one and must still be offered nothing.
            QuestHero? sheet = QuestActions.Sheet(unit);
            return sheet == null ? 0 : Math.Max(0, unit.QuestPower);
        }

        /// <summary>
        /// Clamps a requested Power spend to what the hero actually holds. **Deliberately never more**, even
        /// though <see cref="QuestActions.SpendPower"/> would honour an overspend by taking a wound per
        /// missing Power (exhaustion). Exhaustion is a price the rules make a hero pay when something costs
        /// Power it does not have; it is not something to offer as a way to buy a bigger bonus, and a resolver
        /// returning a wild number must not be able to wound a hero through a boost spinner.
        /// </summary>
        public static int Clamp(UnitData? unit, int requested) =>
            Math.Clamp(requested, 0, Available(unit));

        /// <summary>
        /// Spends <paramref name="amount"/> Power on a cast roll, as the attempt is committed - win or lose,
        /// like the spell's token cost. Routed through <see cref="QuestActions.SpendPower"/> rather than
        /// decrementing <c>QuestPower</c> here, so the Diseased condition ("takes as many wounds as Power
        /// they spend/lose") keeps applying to Power spent this way too.
        /// </summary>
        public static async Task Spend(IGameContext context, UnitData hero, int amount, string why)
        {
            if (amount <= 0) return;
            await QuestActions.SpendPower(context, hero, amount, why);
        }
    }
}
