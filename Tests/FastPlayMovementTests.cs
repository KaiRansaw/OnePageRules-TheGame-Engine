using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #459 Fast-Play Leader-Based Movement and the leader-only charge it shares with #458 (GF / AoF Advanced
    /// Rulebook 3.6.0, p17): "only measure distances from the leader, and move it first ... No distances are
    /// measured for any other models except the leader, so you may freely move them to their new position."
    /// </summary>
    [TestFixture]
    public class FastPlayMovementTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private DataBinding<ModelData> MakeModel(Position at)
        {
            var model = new ModelData(new CircleBase(0.5f), weapons: new List<Weapon>(), initialPosition: at,
                gameDataStore: _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private static DefineMovementPathRequest Request(ModelID? leader, bool movementRule) =>
            new DefineMovementPathRequest(new PlayerID(Guid.NewGuid()), "test", null!,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                measuredModelId: leader, leaderOnlyMovement: movementRule);

        [Test]
        public void PlayerBudget_OnlyTheLeaderIsLimited()
        {
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: true);

            Assert.That(r.PlayerBudgetFor(leader.GetValue().ID).maxDistance, Is.EqualTo(12f));
            Assert.That(r.PlayerBudgetFor(mate.GetValue().ID).maxDistance, Is.EqualTo(FastPlay.UnmeasuredMoveInches));
            Assert.That(r.BudgetFor(mate.GetValue().ID).maxDistance, Is.EqualTo(12f),
                "the AI keeps the ordinary budget - it must never be handed unlimited moves");
        }

        [Test]
        public void RuleOff_EveryModelIsMeasured()
        {
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(null, movementRule: false);
            Assert.That(r.IsMeasured(mate.GetValue().ID), Is.True);
            Assert.That(r.PlayerBudgetFor(mate.GetValue().ID).maxDistance, Is.EqualTo(12f));
        }

        private static List<EnemyModelFootprint> EnemyAt(float x) =>
            new() { new EnemyModelFootprint(new Position(x, 0), 0.5f, 0) };

        [Test]
        public void Movement_AFreelyPlacedModelMayNotStartAMeleeTheLeaderDidNotReach()
        {
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: true);
            // Leader advances 5"; the mate is placed 29" away, in contact with an enemy at 30".
            var moves = new List<ModelMoveEntry>
            {
                new(leader, new List<Position> { new Position(5, 0) }),
                new(mate, new List<Position> { new Position(29, 0) }),
            };
            Assert.That(MovementUtilities.FastPlayChargeProblem(r, moves, EnemyAt(30f)), Is.Not.Null);
        }

        [Test]
        public void Movement_TheFreedomWithNoEnemyNearby_IsFine()
        {
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: true);
            var moves = new List<ModelMoveEntry>
            {
                new(leader, new List<Position> { new Position(5, 0) }),
                new(mate, new List<Position> { new Position(-14, 0) }),   // 15" - beyond its ordinary 12"
            };
            Assert.That(MovementUtilities.FastPlayChargeProblem(r, moves, EnemyAt(60f)), Is.Null);
        }

        [Test]
        public void Movement_ALeaderLedCharge_MayPlaceTheRestFreely()
        {
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(-14, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: true);
            var moves = new List<ModelMoveEntry>
            {
                new(leader, new List<Position> { new Position(10, 0) }),
                new(mate, new List<Position> { new Position(10, 1.2f) }),   // 24"+ - far beyond 12"
            };
            Assert.That(MovementUtilities.FastPlayChargeProblem(r, moves, EnemyAt(11.5f)), Is.Null);
        }

        [Test]
        public void MeleeAlone_TheFreedomIsOnlyForACharge()
        {
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: false);
            var moves = new List<ModelMoveEntry>
            {
                new(leader, new List<Position> { new Position(5, 0) }),
                new(mate, new List<Position> { new Position(-14, 0) }),
            };
            Assert.That(MovementUtilities.FastPlayChargeProblem(r, moves, EnemyAt(60f)), Is.Not.Null,
                "Leader-Based Melee alone frees only a charge's models, not an ordinary move's");
        }

        [Test]
        public void AnOrdinaryMove_IsNeverAffected()
        {
            // Within every model's ordinary budget: legal exactly as before, whatever ends where - the AI's
            // moves are always of this kind, which is what keeps them valid.
            var leader = MakeModel(new Position(0, 0));
            var mate = MakeModel(new Position(1, 0));
            DefineMovementPathRequest r = Request(leader.GetValue().ID, movementRule: true);
            var moves = new List<ModelMoveEntry>
            {
                new(leader, new List<Position> { new Position(5, 0) }),
                new(mate, new List<Position> { new Position(10.5f, 0) }),
            };
            Assert.That(MovementUtilities.FastPlayChargeProblem(r, moves, EnemyAt(11.5f)), Is.Null);
        }
    }
}
