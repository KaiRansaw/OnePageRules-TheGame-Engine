using System.Collections.Generic;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Political Intrigue - Industry (GF / AoF Advanced Rulebooks 3.6.0, p49): "Pick one friendly unit, which may
    /// immediately shoot at one enemy unit within 12"." Used instead of activating a unit, so it runs here, between
    /// picking the directive and the end of the turn: one full shooting action through the ordinary shooting pipeline
    /// (weapon choice, range, line of sight, cover, saves, wounds, morale), held to the one target. The shooter is not
    /// activated by it. Built like <see cref="OverwatchStage"/>.
    /// </summary>
    public class DirectiveShootStage : ParentStage<ISingleTurnContext, ICombatActionContext>
    {
        public StageBinding ToFinished;
        public StageBinding OnShotDone;

        private DataBinding<UnitData>? _shooter;
        private UnitID? _target;

        public DirectiveShootStage(IGameContext gameContext, IStateMachineLayer<ISingleTurnContext> parent)
            : base(gameContext, parent)
        {
        }

        public override async Task Enter(ISingleTurnContext context)
        {
            if (_shooter != null)
            {
                // The shot is done (OnShotDone comes back here).
                _shooter = null;
                _target = null;
                await ToFinished.Activate(context);
                return;
            }
            if (PoliticalIntrigue.TakePendingShot(GameContext) is not { } shot
                || !ChooseRangedAttackStage.HasAnyFireableTarget(shot.Shooter, GameContext, onlyTarget: shot.Target))
            {
                await ToFinished.Activate(context);
                return;
            }
            _shooter = shot.Shooter;
            _target = shot.Target;
            await base.Enter(context);
        }

        protected override ICombatActionContext GetNewChildContext(ISingleTurnContext contextSelf)
        {
            return new CombatActionContext(contextSelf.GameContext, _shooter!, isMelee: false, attackerMoved: false)
            {
                OnlyTargetUnit = _target,
            };
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            ToFinished = new StageBinding(this);
            OnShotDone = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedAttackStage(GameContext, this), out var chooseRangedWeapon)
                .AddChild(new ResolveRangedExtraAttackStage(GameContext, this), out var resolveExtraAttack)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddSibling(nameof(ToFinished), ToFinished, out string _)
                .AddSibling(nameof(OnShotDone), OnShotDone, out string shotDoneEvent)
                .Build();

            startingChild = chooseRangedWeapon;

            chooseRangedWeapon.OnChoseWeapon.Bind(resolveExtraAttack);
            resolveExtraAttack.OnExtraAttackResolved.Bind(fire);
            chooseRangedWeapon.BackToChooseAction.Bind(shotDoneEvent);
            chooseRangedWeapon.OnNoValidShots.Bind(resolveRangedMorale);
            fire.OnFinishedFiring.Bind(determineCanKeepShooting);
            determineCanKeepShooting.ReturnToChooseWeapon.Bind(chooseRangedWeapon);
            determineCanKeepShooting.ToFinishShooting.Bind(resolveRangedMorale);
            resolveRangedMorale.ToFinished.Bind(shotDoneEvent);

            return dictionary;
        }
    }
}
