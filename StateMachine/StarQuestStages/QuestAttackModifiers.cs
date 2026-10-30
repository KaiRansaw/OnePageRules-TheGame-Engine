using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Dispatch;

namespace FDG.StateMachine
{
    /// <summary>
    /// #407: what the Star Quest flags contributed to an attack in flight, itemised by the skill that
    /// contributed it.
    ///
    /// <para><c>QuestFlags.OnAttack</c> used to return a single pre-summed <c>int</c>, which the hit-roll
    /// stage folded into one generic "buff" chip. That made it impossible to tell from play whether a
    /// given skill had fired - the thing Kai's QA note on Vengeful Strike asked for, and which had put
    /// several Star Quest test items on standby. Keeping the contributions apart all the way to the
    /// Battle Report is the whole point, so the type carries the list and offers the total rather than
    /// the other way round.</para>
    /// </summary>
    public sealed record QuestAttackModifiers(IReadOnlyList<GrantedModifier> HitModifiers)
    {
        /// <summary>No Star Quest flags applied (not a Quest game, or the unit carries none).</summary>
        public static QuestAttackModifiers None { get; } =
            new QuestAttackModifiers(System.Array.Empty<GrantedModifier>());

        /// <summary>The net to-hit change - what the roll stage applies to the threshold.</summary>
        public int Net => HitModifiers.Sum(modifier => modifier.Delta);
    }
}
