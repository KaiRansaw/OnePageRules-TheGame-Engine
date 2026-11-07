using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// Extra Actions - Overwatch (Advanced Rulebooks 3.6.0): "until its next activation it may react once to an
    /// enemy unit's activation at any point as that enemy unit moves or shoots. The unit may react to two
    /// things: Movement: Shoot at an enemy at any point during its movement, but gets -1 to hit rolls.
    /// Shooting: Shoot back at an enemy within 24" that shot at it, but gets -1 to hit rolls."
    ///
    /// <para>Two instances sit in the activation: one after the activating unit's move, one after its
    /// shooting. Each offers every enemy unit on Overwatch that can see the activating unit (in turn, each
    /// owner asked) a full shooting action restricted to that one target, through the ordinary shooting
    /// pipeline - weapon choice, range, line of sight, cover, saves, wounds, morale. The -1 comes from the
    /// <see cref="TokenType.OverwatchFiring"/> marker the shooter wears while it fires.</para>
    ///
    /// <para><b>"At any point during its movement"</b> is resolved at the end of the move (house rule): the
    /// engine moves a unit as one path, so there is no mid-path moment to stop it at. A unit that ends its
    /// move out of sight cannot be overwatched; one that ends it in contact (a charge) is shot before the
    /// melee. See claude/house-rules.md.</para>
    /// </summary>
    public class OverwatchStage : ParentStage<IUnitActionContext, ICombatActionContext>
    {
        public enum ETrigger { Movement, Shooting }

        /// <summary>No (more) Overwatch reactions - back to the activating unit's menu.</summary>
        public StageBinding ToFinished;

        /// <summary>One reaction's shooting finished; bound back to this stage to offer the next one.</summary>
        public StageBinding OnShotDone;

        private readonly ETrigger _trigger;

        // One unit activates at a time, so the per-trigger state lives on the stage (the StormStage shape).
        private DataBinding<UnitData>? _shooter;
        private readonly HashSet<UnitID> _asked = new();
        private bool _inProgress;

        public OverwatchStage(IGameContext gameContext, IStateMachineLayer<IUnitActionContext> parent, ETrigger trigger)
            : base(gameContext, parent)
        {
            _trigger = trigger;
        }

        public override string Name => base.Name + _trigger;

        public override async Task Enter(IUnitActionContext context)
        {
            if (!_inProgress)
            {
                _asked.Clear();
                _inProgress = true;
            }

            if (_shooter is { } finished)
            {
                UnitData done = finished.GetValue();
                if (done.Tokens.HasToken(TokenType.OverwatchFiring)) done.Tokens.RemoveTokens(TokenType.OverwatchFiring);
                _shooter = null;
            }

            UnitData mover = context.ActivatingUnit.GetValue();
            if (ExtraActions.IsOn(GameContext, EExtraActions.Overwatch) && mover.GetIsAlive() && mover.GetIsOnBattlefield())
            {
                foreach (DataBinding<UnitData> candidate in Candidates(context))
                {
                    UnitData watcher = candidate.GetValue();
                    _asked.Add(watcher.ID);
                    if (!ChooseRangedAttackStage.HasAnyFireableTarget(candidate, GameContext, onlyTarget: mover.ID)) continue;

                    string what = _trigger == ETrigger.Movement ? "moves" : "shoots at it";
                    bool react = await GameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(
                        watcher.PlayerID,
                        $"{watcher.Name} is on Overwatch and {mover.Name} {what}. Shoot at {mover.Name} now (-1 to hit)?",
                        defaultAnswer: true, displayName: $"{watcher.Name}: Overwatch"));
                    if (!react) continue;

                    watcher.Tokens.RemoveTokens(TokenType.Overwatch);
                    ExtraActions.Mark(watcher, TokenType.OverwatchFiring);
                    _shooter = candidate;
                    GameContext.Log($"{watcher.Name} reacts from Overwatch, shooting at {mover.Name}.");
                    await GameContext.Announce($"{watcher.Name} fires from Overwatch!", tier: EBannerTier.Toast);
                    await base.Enter(context);
                    return;
                }
            }

            _inProgress = false;
            _asked.Clear();
            if (_trigger == ETrigger.Shooting)
            {
                // "Shot at" is about this shooting action only.
                foreach (IUnit unit in GameContext.TableState.Units.Objects)
                {
                    if (unit.Tokens.HasToken(TokenType.OverwatchShotAt)) unit.Tokens.RemoveTokens(TokenType.OverwatchShotAt);
                }
            }
            await ToFinished.Activate(context);
        }

        /// <summary>The enemy units still able to react to this trigger, not yet asked.</summary>
        private IEnumerable<DataBinding<UnitData>> Candidates(IUnitActionContext context)
        {
            UnitData mover = context.ActivatingUnit.GetValue();
            PlayerID moverPlayer = mover.PlayerID;
            ITeam? moverTeam = GameContext.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(moverPlayer));

            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>().ToList())
            {
                if (army.PlayerID.Equals(moverPlayer) || (moverTeam != null && moverTeam.IsPlayerOnTeam(army.PlayerID))) continue;
                foreach (DataBinding<UnitData> binding in army.UnitBindings)
                {
                    UnitData watcher = binding.GetValue();
                    if (_asked.Contains(watcher.ID)) continue;
                    if (!watcher.Tokens.HasToken(TokenType.Overwatch)) continue;
                    if (!watcher.GetIsAlive() || !watcher.GetIsOnBattlefield()) continue;
                    if (watcher.Tokens.HasToken(TokenType.Shaken) || TransportUtilities.IsEmbarked(watcher)) continue;
                    if (_trigger == ETrigger.Shooting)
                    {
                        if (!watcher.Tokens.HasToken(TokenType.OverwatchShotAt)) continue;
                        float distance = UnitCompareUtilities.MinDistanceBetweenUnits(watcher, mover, out _, out _,
                            includeVertical: true);
                        if (distance > ExtraActions.OverwatchShootBackInches + 0.001f) continue;
                    }
                    yield return binding;
                }
            }
        }

        protected override ICombatActionContext GetNewChildContext(IUnitActionContext contextSelf)
        {
            return new CombatActionContext(contextSelf.GameContext, _shooter!, isMelee: false, attackerMoved: false)
            {
                OnlyTargetUnit = contextSelf.ActivatingUnit.GetValue().ID,
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

            // The shooting pipeline of ShootStage, minus the post-shoot move: a reaction is a shot, not an
            // action, so Hit & Run and Retreating Strike do not follow it.
            chooseRangedWeapon.OnChoseWeapon.Bind(resolveExtraAttack);
            resolveExtraAttack.OnExtraAttackResolved.Bind(fire);
            // Backing out of the weapon menu is declining the reaction.
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
