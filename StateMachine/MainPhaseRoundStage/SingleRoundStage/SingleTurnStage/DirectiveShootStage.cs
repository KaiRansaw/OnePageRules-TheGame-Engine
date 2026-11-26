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
        /// <summary>Tactical Phase - Shoot Pathfinders: after the free shot, back to picking a unit (the turn isn't spent).</summary>
        public StageBinding ToChooseUnit;

        private bool _pathfinderShot;

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
                // The shot is done (OnShotDone comes back here). Escalator: a second shooting, maybe at another target.
                if (!_pathfinderShot && await PoliticalIntrigue.TakeSecondShot(GameContext, _shooter.GetValue()) is { } second
                    && ChooseRangedAttackStage.HasAnyFireableTarget(_shooter, GameContext, onlyTarget: second))
                {
                    _target = second;
                    await base.Enter(context);
                    return;
                }
                _shooter = null;
                _target = null;
                if (_pathfinderShot)
                {
                    _pathfinderShot = false;
                    await ToChooseUnit.Activate(context);
                    return;
                }
                await ToFinished.Activate(context);
                return;
            }
            if (TacticalPhase.HasActiveShot(GameContext))
            {
                if (TacticalPhase.TakeActiveShot(GameContext) is not { } free
                    || !ChooseRangedAttackStage.HasAnyFireableTarget(free.Shooter, GameContext, onlyTarget: free.Target))
                {
                    GameContext.Log("Shoot Pathfinders: the shot can no longer be taken (gone, out of range or out of sight).");
                    await ToChooseUnit.Activate(context);
                    return;
                }
                _pathfinderShot = true;
                _shooter = free.Shooter;
                _target = free.Target;
                await base.Enter(context);
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
            ToChooseUnit = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseRangedAttackStage(GameContext, this), out var chooseRangedWeapon)
                .AddChild(new ResolveRangedExtraAttackStage(GameContext, this), out var resolveExtraAttack)
                .AddChild(new FireStage(GameContext, this), out var fire)
                .AddChild(new ResolveRangedMoraleStage(GameContext, this), out var resolveRangedMorale)
                .AddChild(new DetermineCanKeepShootingStage(GameContext, this), out var determineCanKeepShooting)
                .AddSibling(nameof(ToFinished), ToFinished, out string _)
                .AddSibling(nameof(OnShotDone), OnShotDone, out string shotDoneEvent)
                .AddSibling(nameof(ToChooseUnit), ToChooseUnit, out string _)
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
