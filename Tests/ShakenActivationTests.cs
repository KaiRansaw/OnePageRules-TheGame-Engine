using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #008 — Shaken activation behavior. A unit that begins its activation Shaken stays idle the whole
    // activation and recovers (the token clears). A unit that becomes Shaken *mid*-activation keeps the
    // token for next time. Shaken units can neither seize nor contest objectives.
    [TestFixture]
    public class ShakenActivationTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        // --- Activation: force-idle + recovery ---

        [Test]
        public async Task ShakenAtActivationStart_StaysIdleAndRecovers()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeShaken(unit);
            var unitCtx = NewActivation(unit); // fresh: !HasMoved, !HasAttacked

            bool reconciled = await RunChooseAction(unitCtx);

            Assert.That(reconciled, Is.True, "a Shaken unit should skip straight to end-of-activation.");
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False,
                "it recovers — the Shaken token clears at the end of its idle activation.");
        }

        // #278: the idle-and-recover activation announces itself with a Toast (tier-2) banner, not just
        // a log line — recoveries were invisible on the banner layer.
        [Test]
        public async Task ShakenRecovery_PresentsToastBanner()
        {
            var sink = new RecordingPresentationSink();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4),
                presenter: new LocalPresenter(sink, new InstantPresentationClock()));
            var unit = MakeUnit(new Position(0, 0));
            MakeShaken(unit);

            await RunChooseAction(NewActivation(unit));

            BannerBeat? banner = sink.Beats.OfType<BannerBeat>().SingleOrDefault();
            Assert.That(banner, Is.Not.Null, "recovering from Shaken presents a banner beat.");
            Assert.That(banner!.Tier, Is.EqualTo(EBannerTier.Toast), "the recovery banner is a Toast (tier 2).");
            Assert.That(banner.BannerText, Does.Contain("recovers"));
        }

        // The recover-or-keep decision reads the activation-start snapshot, NOT the action flags. This
        // is the robustness guarantee: a unit that becomes Shaken after its activation began keeps the
        // token even with no move/attack recorded — the snapshot is frozen at Reset and not re-read.
        // (Tested directly on the context to avoid driving ChooseActionStage into a player request.)
        [Test]
        public void StartedActivationShaken_IsSnapshotAtReset_AndIsFlagIndependent()
        {
            var unit = MakeUnit(new Position(0, 0));

            var notShakenCtx = NewActivation(unit);
            Assert.That(notShakenCtx.StartedActivationShaken, Is.False,
                "a unit not Shaken when its activation began snapshots false.");

            MakeShaken(unit); // becomes Shaken mid-activation, no move/attack recorded
            Assert.That(notShakenCtx.StartedActivationShaken, Is.False,
                "the snapshot is frozen at activation start — applying Shaken afterward does not flip it, so the unit keeps the token for next time.");

            var shakenCtx = NewActivation(unit); // a fresh activation that begins Shaken
            Assert.That(shakenCtx.StartedActivationShaken, Is.True,
                "a unit Shaken when its activation begins snapshots true and will recover.");
        }

        // A unit that became Shaken before its activation but has already acted (e.g. it was Shaken as a
        // defender, then on its activation it... here simulated with the attacked flag) still recovers
        // via the snapshot — and a unit Shaken only mid-activation does not. Drives the real stage on the
        // no-actions path (HasAttacked gates every action off, so no player request is made).
        [Test]
        public async Task ShakenMidActivation_DrivenThroughStage_KeepsToken()
        {
            var unit = MakeUnit(new Position(0, 0));
            var unitCtx = NewActivation(unit);    // snapshot: not Shaken
            unitCtx.RegisterAttackedFinished();   // it acted...
            MakeShaken(unit);                     // ...then became Shaken

            await RunChooseAction(unitCtx);

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True,
                "Shaken applied after activation start keeps the token — it idles on its *next* activation.");
        }

        // --- Objectives: a Shaken unit can neither seize nor contest ---

        [Test]
        public async Task ShakenUnit_AloneOnObjective_DoesNotSeize()
        {
            var objective = CreateObjective(new Position(5, 5));
            var unit = CreateUnit(new PlayerID(Guid.NewGuid()), new Position(5, 5));
            MakeShaken(unit);

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.Null,
                "a Shaken unit cannot seize the objective it stands on.");
        }

        [Test]
        public async Task ShakenUnit_DoesNotContest_EnemySeizes()
        {
            var objective = CreateObjective(new Position(5, 5));
            var enemy = new PlayerID(Guid.NewGuid());
            var shaken = CreateUnit(new PlayerID(Guid.NewGuid()), new Position(5.0f, 5));
            MakeShaken(shaken);
            CreateUnit(enemy, new Position(5.5f, 5));

            await RunReconcileOnce();

            Assert.That(objective.OwnerID, Is.EqualTo(enemy),
                "the Shaken unit can't contest, so the enemy seizes uncontested.");
        }

        // --- Helpers ---

        // --- #422: Aircraft is the exception to force-idle ---------------------------------------------
        //
        // Aircraft reads "may only use Advance actions, moving in a straight line, and adding 30" to its
        // total move (even if Shaken)". The parenthesis is the rule: a plane that stops flying falls out
        // of the sky, so Shaken does not ground it. The engine hovered it in place, which cannot happen
        // on a table.

        [Test]
        public async Task ShakenAircraft_FliesItsForcedAdvance_InsteadOfIdling()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeAircraft(unit);
            MakeShaken(unit);

            (bool reconciled, bool moved) = await RunChooseActionWatchingMovement(NewActivation(unit));

            Assert.Multiple(() =>
            {
                Assert.That(moved, Is.True, "a Shaken Aircraft is sent to its forced Advance.");
                Assert.That(reconciled, Is.False, "and must NOT be skipped straight to end-of-activation.");
                Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False,
                    "it still recovers - flying out the Shaken is what the activation was spent on.");
            });
        }

        // The other half, and the one that keeps the fix honest: flying is not an escape from Shaken into
        // a full activation. The forced move returns to this stage, and on that second pass the unit must
        // end its activation rather than be offered a shot.
        [Test]
        public async Task ShakenAircraft_AfterItsForcedMove_EndsActivationWithoutShooting()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeAircraft(unit);
            MakeShaken(unit);

            UnitActionContext unitCtx = NewActivation(unit);
            unitCtx.RegisterMoveFinished(30f, 30f);   // the forced Advance has happened

            (bool reconciled, bool moved) = await RunChooseActionWatchingMovement(unitCtx);

            Assert.Multiple(() =>
            {
                Assert.That(reconciled, Is.True,
                    "Shaken still costs the activation - no shooting after the forced move.");
                Assert.That(moved, Is.False, "and it does not fly twice.");
            });
        }

        // A Shaken NON-aircraft is untouched by the exception: it idles, as it always did.
        [Test]
        public async Task ShakenGroundUnit_StillIdles()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeShaken(unit);

            (bool reconciled, bool moved) = await RunChooseActionWatchingMovement(NewActivation(unit));

            Assert.Multiple(() =>
            {
                Assert.That(reconciled, Is.True);
                Assert.That(moved, Is.False, "only Aircraft may not stay idle.");
            });
        }

        // An immobile Shaken Aircraft (nothing left that can move) falls back to idling rather than being
        // routed into a movement it cannot make.
        [Test]
        public async Task ShakenAircraft_Immobile_FallsBackToIdling()
        {
            var unit = MakeUnit(new Position(0, 0));
            MakeAircraft(unit);
            MakeShaken(unit);

            UnitActionContext unitCtx = NewActivation(unit);
            unitCtx.RegisterAttackedFinished();   // GetCanMove says no once the unit has attacked

            (bool reconciled, bool moved) = await RunChooseActionWatchingMovement(unitCtx);

            Assert.Multiple(() =>
            {
                Assert.That(reconciled, Is.True);
                Assert.That(moved, Is.False);
            });
        }

        private async Task<(bool Reconciled, bool Moved)> RunChooseActionWatchingMovement(
            UnitActionContext unitCtx)
        {
            var stage = new ChooseActionStage(_ctx, new NoOpLayer<IUnitActionContext>());
            bool reconciled = false, moved = false;
            stage.ToReconcileEndOfActivation.Bind("ToReconcileEndOfActivation");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            stage.ToMovement.Bind("ToMovement");
            stage.ToMovement.OnWillActivate += _ => moved = true;
            await stage.Enter(unitCtx);
            return (reconciled, moved);
        }

        private static void MakeAircraft(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule(
                "Aircraft", FDG.Rules.Dispatch.CoreRuleCatalog.Aircraft));

        private async Task<bool> RunChooseAction(UnitActionContext unitCtx)
        {
            var stage = new ChooseActionStage(_ctx, new NoOpLayer<IUnitActionContext>());
            bool reconciled = false;
            stage.ToReconcileEndOfActivation.Bind("ToReconcileEndOfActivation");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => reconciled = true;
            await stage.Enter(unitCtx);
            return reconciled;
        }

        private UnitActionContext NewActivation(DataBinding<UnitData> unit)
        {
            var unitCtx = new UnitActionContext(_ctx, unit);
            unitCtx.Reset(unit);
            return unitCtx;
        }

        private static void MakeShaken(DataBinding<UnitData> unit) => MakeShaken(unit.GetValue());

        private static void MakeShaken(UnitData unit) =>
            unit.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

        private DataBinding<UnitData> MakeUnit(Position position)
        {
            var model = new ModelData(
                baseRadiusInches: 0.5f,
                weapons: new List<Weapon>(),
                initialPosition: position,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Test Unit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private ObjectiveData CreateObjective(Position position)
        {
            var obj = new ObjectiveData(position, _store);
            _store.Create(obj);
            return obj;
        }

        private UnitData CreateUnit(PlayerID playerID, Position modelPosition)
        {
            var model = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: modelPosition,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(playerID, "TestUnit", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            _store.Create(unit);
            return unit;
        }

        private Task RunReconcileOnce()
        {
            var stage = new ReconcileObjectivesStage(_ctx, new NoOpLayer<IMainPhaseContext>());
            stage.ToReconcileEndOfTurn.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_RECONCILE_NEW_TURN);
            stage.ToVictoryCalculation.Bind(ReconcileObjectivesStage.RECONCILE_OBJECTIVES_TO_VICTORY_CALCULATION_TRANSITION);
            return stage.Enter(new StubMainPhaseContext(_ctx));
        }
    }
}
