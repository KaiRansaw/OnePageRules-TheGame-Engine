using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #423 - the targeting half of the General Special Rules clause in <b>Solo &amp; Co-Op Rules
    /// v3.6.0</b>: "AI units must always use their special rules as soon as they are activated,
    /// <b>targeting the closest unit that makes sense</b>."
    ///
    /// <para>
    /// The baseline answers these picks with <c>AiChooseMeleeDefenderResolver</c> - the unit with the
    /// fewest living models - which is a melee-defender heuristic rather than this clause. So every case
    /// here puts the weakest unit FURTHEST away: if the distance key were missing, the old answer would win
    /// and the test would fail, which is the only way to prove the clause is doing the work.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiAbilityTargetTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _store.Create(new TeamData(1, new List<PlayerID> { _player }));
        }

        [Test]
        public void TheClosestOfferedUnitIsPicked()
        {
            DataBinding<UnitData> actor = Unit(_ai, "Sorcerer", x: 0f, models: 1);
            DataBinding<UnitData> near = Unit(_player, "Near Blob", x: 6f, models: 5);
            DataBinding<UnitData> farButWeakest = Unit(_player, "Far Loner", x: 30f, models: 1);

            IUnit picked = Pick(actor, near, farButWeakest);

            Assert.That(picked.Name, Is.EqualTo("Near Blob"),
                "closest wins - and the far unit is the one the baseline's fewest-models heuristic would "
                + "have taken, which is what makes this case worth having.");
        }

        // Same board, options the other way round, so the answer is the geometry and not list order.
        [Test]
        public void ListOrderDoesNotDecideIt()
        {
            DataBinding<UnitData> actor = Unit(_ai, "Sorcerer", x: 0f, models: 1);
            DataBinding<UnitData> farButWeakest = Unit(_player, "Far Loner", x: 30f, models: 1);
            DataBinding<UnitData> near = Unit(_player, "Near Blob", x: 6f, models: 5);

            IUnit picked = Pick(actor, farButWeakest, near);

            Assert.That(picked.Name, Is.EqualTo("Near Blob"));
        }

        // Measured from the ACTIVATING unit, which is the unit whose ability raised the request. Moving
        // the actor to the other end of the table flips the answer without touching the options.
        [Test]
        public void DistanceIsMeasuredFromTheActivatingUnit()
        {
            DataBinding<UnitData> actor = Unit(_ai, "Sorcerer", x: 40f, models: 1);
            DataBinding<UnitData> near = Unit(_player, "Near Blob", x: 6f, models: 5);
            DataBinding<UnitData> farFromOrigin = Unit(_player, "Far Loner", x: 30f, models: 1);

            IUnit picked = Pick(actor, near, farFromOrigin);

            Assert.That(picked.Name, Is.EqualTo("Far Loner"),
                "from 40\" away the 'far' unit is the near one; the reference point is the actor, not "
                + "the table's origin.");
        }

        // A friendly target (Mend, a buff) is the same question. The clause says "the closest unit that
        // makes sense", and which units make sense has already been decided by the ability's own target
        // selector before the request is raised.
        [Test]
        public void FriendlyTargetsAreTreatedTheSameWay()
        {
            DataBinding<UnitData> actor = Unit(_ai, "Apothecary", x: 0f, models: 1);
            DataBinding<UnitData> nearAlly = Unit(_ai, "Near Squad", x: 4f, models: 5);
            DataBinding<UnitData> farAlly = Unit(_ai, "Far Squad", x: 20f, models: 1);

            IUnit picked = Pick(actor, nearAlly, farAlly);

            Assert.That(picked.Name, Is.EqualTo("Near Squad"));
        }

        // No activating unit means no reference point, so the baseline's answer stands rather than a
        // guess. (Reached by tests and by any request raised outside an activation.)
        [Test]
        public void WithNoActivatingUnit_TheBaselineAnswerStands()
        {
            DataBinding<UnitData> near = Unit(_player, "Near Blob", x: 6f, models: 5);
            DataBinding<UnitData> farButWeakest = Unit(_player, "Far Loner", x: 30f, models: 1);

            var context = new TestGameContext(_store, new FixedDiceRoller(4));
            var resolver = new SoloCoOpAbilityTargetResolver(context.TableState);

            IUnit picked = Resolved(resolver, Request(near, farButWeakest));

            Assert.That(picked.Name, Is.EqualTo("Far Loner"),
                "fewest living models - AiChooseMeleeDefenderResolver's answer, unchanged.");
        }

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        private IUnit Pick(DataBinding<UnitData> actor, params DataBinding<UnitData>[] options)
        {
            // The resolver reads the actor off IGameProgress.ActivatingUnit, which lives on the store's
            // single progress record - written through the engine's own writer, since a second Create
            // would make which record answers arbitrary.
            GameProgressUtilities.WriteProgress(_store, new GameProgressData(EResumeStage.MainPhase, 1,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault(), activatingUnit: actor));

            var context = new TestGameContext(_store, new FixedDiceRoller(4));
            return Resolved(new SoloCoOpAbilityTargetResolver(context.TableState), Request(options));
        }

        private static IUnit Resolved(SoloCoOpAbilityTargetResolver resolver,
            CancellableSelectionRequest<UnitData> request)
        {
            CancellableResult<DataBinding<UnitData>> reply = resolver.Resolve(request).GetAwaiter().GetResult();
            Assert.That(reply, Is.InstanceOf<Selected<DataBinding<UnitData>>>(),
                "the AI never backs out of an ability's target pick.");
            return ((Selected<DataBinding<UnitData>>)reply).Value.GetValue();
        }

        private CancellableSelectionRequest<UnitData> Request(params DataBinding<UnitData>[] options)
        {
            List<CancellableSelectionRequest<UnitData>.ValidOption> valid = options
                .Select(o => new CancellableSelectionRequest<UnitData>.ValidOption(o, o.GetValue().Name))
                .ToList();

            return new CancellableSelectionRequest<UnitData>(_ai, "Pick a target",
                valid, Array.Empty<CancellableSelectionRequest<UnitData>.InvalidOption>());
        }

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, i * 1.2f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
