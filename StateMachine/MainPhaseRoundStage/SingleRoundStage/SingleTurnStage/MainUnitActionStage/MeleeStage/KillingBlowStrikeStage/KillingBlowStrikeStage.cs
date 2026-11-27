using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Command Points - Killing Blow (Advanced Rulebooks 3.6.0, Shock Doctrine): "Pick one friendly unit that is fighting
    /// in melee. Any friendly model that is killed during this melee may strike one more time with all of its weapons
    /// before it is removed."
    ///
    /// <para>A model killed BEFORE its unit strikes already strikes with it (<see cref="MeleeRangeUtilities"/> lets a dead
    /// model carrying the Killing Blow token count as in range). This covers the other case: the unit that struck first
    /// (usually the charger) loses models to the strike-back, after its own strike is over. Those models - and only
    /// those - strike once more, here, before the melee's winner is worked out. Lane 1, 2026-11-27.</para>
    /// </summary>
    public static class KillingBlowStrikes
    {
        private static readonly ConditionalWeakTable<ICombatActionContext, List<DataBinding<ModelData>>> Snapshots = new();

        /// <summary>Called as the strike-back starts: the first striker's in-range models that are still alive and carry
        /// the Killing Blow token. Any of them dead when the strike-back ends was killed after its unit struck.</summary>
        public static void SnapshotBeforeStrikeBack(ICombatActionContext context)
        {
            var alive = context.InRangeAttackingModels
                .Where(b => b.GetValue() is IModel m && m.GetIsAlive() && CommandPoints.StrikesOnceMore(m))
                .ToList();
            Snapshots.AddOrUpdate(context, alive);
        }

        /// <summary>The first striker's models killed during the strike-back that may strike once more.</summary>
        public static List<DataBinding<ModelData>> KilledAfterStriking(ICombatActionContext context) =>
            Snapshots.TryGetValue(context, out var before)
                ? before.Where(b => b.GetValue() is IModel m && !m.GetIsAlive() && CommandPoints.StrikesOnceMore(m)).ToList()
                : new List<DataBinding<ModelData>>();
    }

    /// <summary>Gate: is anyone owed a Killing Blow strike after the strike-back? See <see cref="KillingBlowStrikes"/>.</summary>
    public class OfferKillingBlowStrikeStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnStrike;
        public StageBinding OnNone;

        public OfferKillingBlowStrikeStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnStrike = new StageBinding(this);
            OnNone = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            List<DataBinding<ModelData>> owed = KillingBlowStrikes.KilledAfterStriking(context);
            if (owed.Count == 0 || MeleeRangeUtilities.GetMeleeWeaponsFromModels(owed).Count == 0
                || !context.DefendingUnit.GetValue().GetIsAlive())
            {
                await OnNone.Activate(context);
                return;
            }
            GameContext.Log($"Killing Blow: {owed.Count} model(s) of {context.AttackingUnit.GetValue().Name} killed after striking strike once more.");
            await GameContext.Announce($"Killing Blow: {context.AttackingUnit.GetValue().Name}'s fallen strike once more!",
                BattlefieldEffects.Amber, Presentation.Beats.EBannerTier.Toast);
            await OnStrike.Activate(context);
        }
    }

    /// <summary>The Killing Blow strike itself: the first striker again, with only the models owed a strike.</summary>
    public class KillingBlowStrikeStage : ParentStage<ICombatActionContext, ICombatActionContext>
    {
        public StageBinding FinishedStriking;
        public StageBinding OnDefenderKilled;

        public KillingBlowStrikeStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
        }

        protected override ICombatActionContext GetNewChildContext(ICombatActionContext contextSelf)
        {
            var meleeContext = new CombatActionContext(contextSelf.GameContext, contextSelf.AttackingUnit, isMelee: true);
            meleeContext.SetDefender(contextSelf.DefendingUnit);
            meleeContext.SetInRangeAttackers(KillingBlowStrikes.KilledAfterStriking(contextSelf));
            meleeContext.SetInRangeDefenders(contextSelf.InRangeDefendingModels);
            return meleeContext;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatActionContext> startingChild)
        {
            FinishedStriking = new StageBinding(this);
            OnDefenderKilled = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new ChooseMeleeWeaponStage(GameContext, this), out var chooseMeleeWeapon)
                .AddChild(new SwingMeleeWeaponStage(GameContext, this), out var swingMeleeWeapon)
                .AddChild(new DetermineCanKeepSwingingStage(GameContext, this), out var determineCanKeepSwinging)
                .AddSibling(nameof(FinishedStriking), FinishedStriking, out string finishedEvent)
                .AddSibling(nameof(OnDefenderKilled), OnDefenderKilled, out string defenderKilledEvent)
                .Build();

            startingChild = chooseMeleeWeapon;
            chooseMeleeWeapon.OnChosen.Bind(swingMeleeWeapon);
            chooseMeleeWeapon.OnNoWeaponsLeftToSwing.Bind(finishedEvent);
            swingMeleeWeapon.FinishedSwinging.Bind(determineCanKeepSwinging);
            determineCanKeepSwinging.ReturnToChooseWeapon.Bind(chooseMeleeWeapon);
            determineCanKeepSwinging.OnOutOfWeapons.Bind(finishedEvent);
            determineCanKeepSwinging.OnDefenderKilled.Bind(defenderKilledEvent);
            return dictionary;
        }
    }
}

namespace FDG.Stages
{
    // The state machine keys child stages by type, so the after-a-wipe-out path needs its own two types.
    /// <summary><see cref="OfferKillingBlowStrikeStage"/> on the path where the strike-back wiped the first striker out.</summary>
    public sealed class OfferKillingBlowAfterWipeStage : OfferKillingBlowStrikeStage
    {
        public OfferKillingBlowAfterWipeStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent) { }
    }

    /// <summary><see cref="KillingBlowStrikeStage"/> on the path where the strike-back wiped the first striker out.</summary>
    public sealed class KillingBlowAfterWipeStrikeStage : KillingBlowStrikeStage
    {
        public KillingBlowAfterWipeStrikeStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent) { }
    }
}
