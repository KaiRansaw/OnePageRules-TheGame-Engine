namespace FDG.Stages
{
    /// <summary>
    /// The loop decision between units a blast spread onto: resolve the next one, or the shot is done.
    ///
    /// <para>
    /// #414, and the exact counterpart of <c>DetermineMoreSpellTargetsStage</c>. It exists because
    /// <see cref="ResolveBlastSpillStage"/> resolves ONE receiver per pass - it has to, since each
    /// receiver needs its own metadata, its own save rolls and its own wound assignment - so something has
    /// to send it round again. Without this the blast would only ever reach the first bystander.
    /// </para>
    ///
    /// <para>
    /// Also the stage that makes a non-splitting system cost nothing: with no
    /// <see cref="BlastSpillResults"/> on the metadata at all, which is every shot in Grimdark Future and
    /// Age of Fantasy and every shot without a Blast weapon anywhere, this exits straight to finished.
    /// </para>
    /// </summary>
    public class DetermineMoreBlastSpillStage : StageBase<ICombatMetadata>
    {
        public StageBinding ResolveNextUnit;
        public StageBinding ToFinished;

        public DetermineMoreBlastSpillStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent)
            : base(gameContext, parent)
        {
            ResolveNextUnit = new StageBinding(this);
            ToFinished = new StageBinding(this);
        }

        public override async Task Enter(ICombatMetadata context)
        {
            if (context.QueryForResult(out BlastSpillResults spill) && spill.HasMore)
            {
                await ResolveNextUnit.Activate(context);
                return;
            }

            await ToFinished.Activate(context);
        }
    }
}
