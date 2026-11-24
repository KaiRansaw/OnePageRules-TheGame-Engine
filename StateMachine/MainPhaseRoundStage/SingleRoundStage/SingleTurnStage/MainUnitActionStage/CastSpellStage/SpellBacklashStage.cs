namespace FDG.Stages
{
    /// <summary>
    /// Advanced Casting - 2D6 Casting: after a damage spell's last target resolves, a cast that rolled doubles takes its
    /// backlash ("after resolving any spell effects"). The other spell paths apply it inline in <see cref="CastSpellStage"/>.
    /// </summary>
    public class SpellBacklashStage : StageBase<SpellDamageRunContext>
    {
        public StageBinding OnFinished;

        public SpellBacklashStage(IGameContext gameContext, IStateMachineLayer<SpellDamageRunContext> parent)
            : base(gameContext, parent)
        {
            OnFinished = new StageBinding(this);
        }

        public override async Task Enter(SpellDamageRunContext context)
        {
            if (context.Backlash)
            {
                context.Backlash = false;
                await AdvancedCasting.Backlash(GameContext, context.Caster.GetValue());
            }
            await OnFinished.Activate(context);
        }
    }
}
