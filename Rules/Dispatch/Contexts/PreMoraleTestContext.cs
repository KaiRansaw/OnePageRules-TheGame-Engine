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
    /// </summary>
    public sealed record PreMoraleTestContext(IUnit Unit, EMoraleTrigger Trigger)
        : IHookContext, IHasMoraleTrigger
    {
        public EHookID Hook => EHookID.Morale_OnPreMoraleTest;

        public EMoraleTrigger MoraleTrigger => Trigger;
    }
}
