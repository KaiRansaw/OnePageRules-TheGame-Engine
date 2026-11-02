namespace FDG.Rules.Foundation;

/// <summary>
/// What caused a morale test. Exists for one reason: every current book's <b>Banner</b> grants its +1
/// only to <i>"Morale Test rolls triggered by shooting or melee"</i>, and the engine has morale tests
/// that come from neither.
///
/// <para>
/// Verified wording, 2026-11-01, all six systems. Army Forge serves the same clause for every one of
/// them (<i>"get +1 to Morale Test rolls triggered by shooting/melee"</i>), and the two core books that
/// print a Command Groups section at all agree: AoF v3.6.0 <i>"+1 to Morale Test rolls triggered by
/// shooting or melee"</i>, AoFS v3.6.0 <i>"+1 to Morale Test triggered by shooting or melee"</i>. So
/// unlike most per-system questions in this codebase, this one is genuinely universal - there is no
/// system whose Banner is unconditional, and a future reader should not go looking for one.
/// </para>
/// </summary>
public enum EMoraleTrigger
{
    /// <summary>
    /// Wounds from <b>shooting</b> took the unit to half strength. Split out from melee by #442: Banner
    /// treats the two alike (<i>"triggered by shooting/melee"</i>) but the Hold the Line family does
    /// not - its band is <i>"over 9"/6" away"</i>, and which of the two numbers applies depends on
    /// which of the two causes it was. <see cref="Rules.Definitions.Condition.MoraleTriggeredByCombat"/>
    /// still covers both, so Banner is unaffected by the split.
    /// </summary>
    Shooting,

    /// <summary>
    /// The unit lost a <b>melee</b>, or wounds dealt in one took it to half strength. Together with
    /// <see cref="Shooting"/> this is the overwhelming majority of morale tests in a standard game,
    /// which is why the engine got away with treating Banner as unconditional for so long.
    /// </summary>
    Melee,

    /// <summary>
    /// Anything else - a spell's morale test, a unit rule that forces one (Mind Control, Fatigue
    /// Debuff), a Quest event, a campaign random event (#432). Banner does <b>not</b> help here.
    /// <para>
    /// There is deliberately no default value on the parameter that carries this: a new morale source
    /// added later must say which it is. Silent fall-through to the common case is exactly how the four
    /// per-system defects in #437 hid, and the audit's lesson was to make the compiler ask.
    /// </para>
    /// </summary>
    OtherSource,
}

/// <summary>
/// Capability interface: a hook context that knows why its morale test is being taken. Lets
/// <see cref="Rules.Definitions.Condition.MoraleTriggeredByCombat"/> gate Banner-shaped rules on the
/// pre-morale hook, which fires for every morale test regardless of source, and lets
/// <see cref="Rules.Definitions.Condition.MoraleAttackedFromBeyond"/> pick which of the Hold the Line
/// family's two distances applies.
/// </summary>
public interface IHasMoraleTrigger : ICapability
{
    EMoraleTrigger MoraleTrigger { get; }
}

/// <summary>
/// Convenience over <see cref="EMoraleTrigger"/>, so the "is this one of the two combat causes" test
/// lives in one place rather than being spelled out at each reader.
/// </summary>
public static class MoraleTriggerExtensions
{
    /// <summary>
    /// True for <see cref="EMoraleTrigger.Shooting"/> and <see cref="EMoraleTrigger.Melee"/> - the pair
    /// every book's Banner names as <i>"triggered by shooting/melee"</i>.
    /// </summary>
    public static bool IsCombat(this EMoraleTrigger trigger) =>
        trigger is EMoraleTrigger.Shooting or EMoraleTrigger.Melee;
}
