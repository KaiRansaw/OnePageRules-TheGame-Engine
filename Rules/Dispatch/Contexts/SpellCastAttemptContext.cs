using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Casting_OnSpellCastAttempt"/>: the caster has chosen a spell and its
    /// targets, paid the token cost, and is about to roll. Rules react here to modify the casting roll -
    /// Spell Amplifier's "+1 to casting rolls" (#409, OPR 3.6.0) is the first of them.
    ///
    /// <para>The hook ID has existed since #033 but nothing constructed a context for it, so no passive
    /// rule could ever touch a cast. Until now the only things that could move a casting roll were token
    /// spends (the caster's own boost and the 18" assists) and token-granted deltas consumed through
    /// <see cref="GrantedRollModifiers"/> - both one-shot state a rule has to GRANT, never a standing
    /// property of the model. Spell Amplifier is a standing property, which is why it needed the hook
    /// wired rather than another grant.</para>
    ///
    /// <para>Carries only the caster: the modifier is a property of who is casting, not of the spell or
    /// its targets. Rules that need to care about the target belong at
    /// <see cref="EHookID.Casting_OnSpellResolved"/> instead, where the effect lands.</para>
    /// </summary>
    public sealed record SpellCastAttemptContext(IUnit Caster) : IHookContext
    {
        public EHookID Hook => EHookID.Casting_OnSpellCastAttempt;
    }
}
