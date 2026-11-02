using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch.Contexts
{
    /// <summary>
    /// Fires at <see cref="EHookID.Morale_OnPreMoraleTest"/>: a unit is about to take a morale test,
    /// before the die is rolled. The hook the rulebook names for morale-roll modifiers (e.g. a Courage
    /// aura's +1 to morale tests); RollForMoraleStage folds any ApplyRollModifier(Morale) into the
    /// threshold.
    /// <para>
    /// Carries the testing unit and <b>why it is testing</b>. The trigger is here because every book's
    /// Banner is worded <i>"+1 to Morale Test rolls triggered by shooting or melee"</i> - this hook
    /// fires for spell-driven and campaign-event morale tests too, so a rule that reads only the unit
    /// cannot tell the book's case from the others. See <see cref="EMoraleTrigger"/>.
    /// </para>
    /// <para>
    /// #442: it also carries <b>how far away the attack came from</b>, because the Hold the Line family
    /// (Hold the Line, Valiant, Hive Bond, Battle-Hardened) is worded <i>"+1 to Morale Test rolls when
    /// shot/charged from over 9"/6" away"</i> - a band, not a blanket. That made this context the third
    /// place in this codebase where a correct condition sat unreachable because nothing fed it:
    /// <see cref="Condition.AttackedFromOverInches"/> and
    /// <see cref="IHasAttackOriginDistance"/> already existed for the attack hooks, and this hook simply
    /// never implemented the capability, so the whole family was granting its bonus unconditionally.
    /// </para>
    /// </summary>
    /// <param name="AttackerLaunchDistanceInches">
    /// How far away the attack that caused this test was <b>launched</b> from: the shooter's distance to
    /// this unit for a shooting-driven test, and the distance the charge was declared from for a melee
    /// one (see <see cref="IHasAttackOriginDistance"/> for why those are different measurements).
    /// <para>
    /// <b>0 means "no launch distance applies"</b>, and that is the honest answer in three cases: a
    /// morale test from neither shooting nor melee, a melee the unit was not charged into (a unit that
    /// charged and then lost was not "charged from" anywhere, and neither was one already locked in
    /// combat), and a melee whose charger cannot be identified. All three leave the band unsatisfied,
    /// which is the conservative direction - the bonus is withheld rather than invented.
    /// </para>
    /// </param>
    public sealed record PreMoraleTestContext(
        IUnit Unit,
        EMoraleTrigger Trigger,
        float AttackerLaunchDistanceInches = 0f)
        : IHookContext, IHasMoraleTrigger, IHasAttackOriginDistance
    {
        public EHookID Hook => EHookID.Morale_OnPreMoraleTest;

        public EMoraleTrigger MoraleTrigger => Trigger;

        /// <summary>
        /// The launch distance as <see cref="IHasAttackOriginDistance"/> defines it. This context already
        /// receives the right measurement for the cause (the caller picks shooting distance or charge
        /// origin), so there is nothing to branch on here - unlike the attack contexts, which hold both
        /// and choose between them.
        /// </summary>
        public float AttackOriginDistanceInches => AttackerLaunchDistanceInches;
    }
}
