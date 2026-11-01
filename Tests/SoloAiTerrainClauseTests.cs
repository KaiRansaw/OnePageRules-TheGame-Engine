using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #424 - the Terrain clause of <b>Solo &amp; Co-Op Rules v3.6.0</b>, identical in all four books:
    ///
    /// <para>
    /// "AI units must always try to <b>end their move into or behind Cover terrain</b> if possible,
    /// <b>moving around Difficult and Dangerous terrain</b>. AI units only move into Difficult or Dangerous
    /// terrain if there is an <b>objective inside of it</b>, if they are in <b>charge range of a unit inside
    /// of it</b>, or if they have a <b>special rule that ignores its effects</b>."
    /// </para>
    /// <para>
    /// These cases are on the end-point choice, which is the whole of the clause that was missing: the
    /// shared mover already routes around Impassible and (since #281) around Difficult, but the word
    /// "Dangerous" appears nowhere in it, and nothing anywhere tried to END a move in cover.
    /// </para>
    /// <para>
    /// Everything here is 2D and in inches. The unit starts at the origin and the plain destination - where
    /// it would stop with no terrain preference - is handed in, so each case states the geometry it means
    /// instead of depending on the movement stack to produce it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiTerrainClauseTests
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

        // ── "end their move into or behind Cover terrain" ─────────────────────────────────────────────

        [Test]
        public void AMoveEndsInNearbyCover()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Terrain(ETerrainType.Cover, 18f, 24f, -3f, 3f);   // a wood just past the plain stop

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(20f, 0f));

            Assert.That(InsideCover(end), Is.True,
                "the clause is \"must always try to end their move into ... Cover\", and the wood is "
                + "inside the slack the plan allows.");
        }

        // "If possible" is doing work: the tree's goal outranks the terrain, so cover that would cost
        // more than the allowed slack is left alone. Without this the AI would wander off objectives.
        [Test]
        public void CoverTooFarFromTheGoalIsIgnored()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Terrain(ETerrainType.Cover, 30f, 40f, -3f, 3f);   // well past the goal

            var plain = new Position(16f, 0f);
            Position end = Preferred(unit, plain, goal: new Position(20f, 0f));

            Assert.That(end, Is.EqualTo(plain),
                $"beyond {SoloCoOpTerrainPolicy.GoalSlackInches}\" of slack the objective wins.");
        }

        [Test]
        public void NoTerrainAtAll_LeavesTheMoveAlone()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            var plain = new Position(16f, 0f);

            Assert.That(Preferred(unit, plain, goal: new Position(20f, 0f)), Is.EqualTo(plain));
        }

        // "Behind cover" as well as in it: a blocking piece between the end point and the nearest enemy
        // counts, which is how a unit uses a wall it cannot stand inside.
        [Test]
        public void AMoveEndsBehindABlockingPiece()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Unit(_player, "Shooters", 40f);                        // enemy far to the east
            Terrain(ETerrainType.Blocking, 18f, 19f, -6f, 6f);     // a wall at x=18-19

            // The plain stop at x=20 is on the ENEMY's side of the wall, in the open. The sheltered side
            // is west of it, and the goal is far enough away that stepping back is inside the slack.
            Position end = Preferred(unit, plain: new Position(20f, 0f), goal: new Position(30f, 0f));

            Assert.That(end.x, Is.LessThan(18f),
                "the end point moves to the sheltered side, so the wall is between it and the enemy - "
                + "which is what \"behind Cover\" means.");
        }

        // ── "moving around Difficult and Dangerous terrain" ───────────────────────────────────────────

        // The half that was missing outright: "Dangerous" appears nowhere in the shared mover, so the
        // official bot walked its units through ground that makes them take a wound test.
        [Test]
        public void AMoveDoesNotEndInDangerousTerrain()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Terrain(ETerrainType.Dangerous, 14f, 18f, -3f, 3f);

            // Heading for an objective just past the swamp: the plain stop lands in it, and the clear
            // far side is no further from the goal.
            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(20f, 0f));

            Assert.That(InsideBadGround(end), Is.False,
                "a unit does not stop in Dangerous ground when clear ground is as close to its goal.");
        }

        [Test]
        public void AMoveDoesNotEndInDifficultTerrain()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Terrain(ETerrainType.Difficult, 14f, 18f, -3f, 3f);

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(20f, 0f));

            Assert.That(InsideBadGround(end), Is.False);
        }

        // ── the clause's three exceptions ─────────────────────────────────────────────────────────────

        [Test]
        public void BadGroundIsEnteredForAnObjectiveInsideIt()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            Terrain(ETerrainType.Dangerous, 14f, 18f, -3f, 3f);
            Objective(16f, 0f);                                    // the marker is in the swamp

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(16f, 0f));

            Assert.That(InsideBadGround(end), Is.True,
                "\"AI units only move into Difficult or Dangerous terrain if there is an objective "
                + "inside of it\" - and here there is.");
        }

        [Test]
        public void BadGroundIsEnteredWhenAnEnemyInsideItIsInChargeRange()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 8f);   // 8" from the swamp's occupant
            Terrain(ETerrainType.Dangerous, 14f, 18f, -3f, 3f);
            Unit(_player, "Lurkers", 16f);                           // inside it, within a 12" charge

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(16f, 0f));

            Assert.That(InsideBadGround(end), Is.True,
                "\"...if they are in charge range of a unit inside of it\".");
        }

        // The control for the case above: the same swamp with its occupant well out of charge range, so
        // the exception does not apply and the unit keeps off the ground.
        [Test]
        public void BadGroundIsAvoidedWhenTheEnemyInsideItIsOutOfChargeRange()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", -30f);
            Terrain(ETerrainType.Dangerous, 14f, 18f, -3f, 3f);
            Unit(_player, "Lurkers", 16f);

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(20f, 0f));

            Assert.That(InsideBadGround(end), Is.False);
        }

        // "...or if they have a special rule that ignores its effects" - Strider and Flying, which reach
        // this code as the move request's own IgnoresDifficultTerrain flag.
        [Test]
        public void AUnitThatIgnoresTheEffects_WalksInAnyway()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Striders", 0f);
            Terrain(ETerrainType.Dangerous, 14f, 18f, -3f, 3f);

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(16f, 0f),
                ignoresDifficult: true);

            Assert.That(InsideBadGround(end), Is.True,
                "the rule exists precisely so the unit does not have to walk round.");
        }

        // Bad ground is ranked ahead of cover where the two disagree: the cover bonus is +1 to the
        // enemy's save, while Dangerous ground rolls to wound the unit. A judgement call, recorded in
        // claude/house-rules.md - and pinned here so it cannot drift silently.
        [Test]
        public void CleanGroundBeatsDangerousCover()
        {
            DataBinding<UnitData> unit = Unit(_ai, "Raiders", 0f);
            // One piece that is BOTH cover and dangerous, and plain ground beside it.
            Terrain(ETerrainType.Cover | ETerrainType.Dangerous, 14f, 18f, -3f, 3f);

            Position end = Preferred(unit, plain: new Position(16f, 0f), goal: new Position(20f, 0f));

            Assert.That(InsideBadGround(end), Is.False,
                "a dangerous wood is not worth the save bonus.");
        }

        // ── The clause through the real resolver ──────────────────────────────────────────────────────

        // The cases above drive the policy directly, so they would all still pass with the policy never
        // called. This one drives SoloCoOpMovementResolver - the whole point being that the move the
        // engine receives ends in the wood, and that it is a move the engine accepts.
        [Test]
        public async Task TheMovementResolverEndsItsMoveInCover()
        {
            DataBinding<UnitData> mover = Unit(_ai, "Raiders", 0f);
            Unit(_player, "Troops", 60f);                    // far away: no shooting plan, just the run
            Objective(24f, 0f);                              // step 3's uncontested objective
            // A wood just OFF the straight lane, so the plain stop is in the open and reaching cover
            // means stepping aside. (On the lane it would prove nothing - the unit would end up in it
            // whether or not the clause is wired in at all, which is exactly how the first version of
            // this test passed with the policy disconnected.)
            Terrain(ETerrainType.Cover, 10f, 16f, 1f, 8f);

            var context = new TestGameContext(_store, new FixedDiceRoller(4));
            var resolver = new SoloCoOpMovementResolver(context.TableState, _ai, latch: null);
            var request = new DefineMovementPathRequest(_ai, "Move", mover,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                mainActivationMove: true);

            CancellableResult<List<ModelMoveEntry>> reply = await resolver.Resolve(request);

            Assert.That(reply, Is.InstanceOf<Selected<List<ModelMoveEntry>>>(),
                "the AI always supplies a path rather than declining.");
            List<ModelMoveEntry> move = ((Selected<List<ModelMoveEntry>>)reply).Value;

            Position end = move[0].Positions[^1];
            Assert.That(InsideCover(end), Is.True,
                $"the Rush toward the objective should stop in the wood; ended at ({end.x:0.##}, {end.z:0.##}).");
        }

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        private Position Preferred(DataBinding<UnitData> unit, Position plain, Position goal,
            bool ignoresDifficult = false)
        {
            var context = new TestGameContext(_store, new FixedDiceRoller(4));
            return SoloCoOpTerrainPolicy.PreferredEndPoint(context.TableState, _ai, unit.GetValue(),
                plain, goal, ignoresDifficult);
        }

        private bool InsideCover(Position point) => Inside(ETerrainType.Cover, point);
        private bool InsideBadGround(Position point) =>
            Inside(ETerrainType.Difficult, point) || Inside(ETerrainType.Dangerous, point);

        private bool Inside(ETerrainType type, Position point) =>
            _store.GetAllValues<TerrainData>()
                .Where(t => t.TerrainType.HasFlag(type))
                .Any(t => t.Shape.IsPointWithinZone(new Float2(point.x, point.z)));

        private void Terrain(ETerrainType type, float minX, float maxX, float minZ, float maxZ) =>
            _store.Create(new TerrainData(type, new RectangularZone(minX, maxX, minZ, maxZ)));

        private void Objective(float x, float z) =>
            _store.Create(new ObjectiveData(new Position(x, z), _store));

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, 0f), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
