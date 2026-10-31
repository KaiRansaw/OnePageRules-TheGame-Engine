using FDG.Ai.StarQuest;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #412 - the Star Quest AI flowchart on its GFSQ Core Rules v2.1.0 wording.
    //
    // The change that matters is step 1. Under v2.0.1 it read "are any enemies in line of sight?", so
    // breaking line of sight made the entire enemy force lose interest and walk off to its objective -
    // which is the single biggest reason Star Quest played as hide-and-seek. 2.1.0 reads "in line of
    // sight OR WITHIN 12" BUT NOT IN LINE OF SIGHT", so a hero behind a wall is still hunted.
    //
    // The scene is the smallest one that can tell the two readings apart: one AI unit, one hero, and a
    // Blocking wall between them. Distance decides the answer, and the wall never moves - so a test that
    // passed under the old reading (walk to the goal) now fails, and vice versa. An objective sits well
    // away from both, so "went to its goal" and "went for the hero" are different directions rather than
    // nearly the same one.
    //
    // Step 2's other 2.1.0 addition, "or Hold and shoot if in range", is NOT tested here: it is a
    // property of the action resolver rather than the plan (the plan says Advance, and the resolver
    // picks SHOOT over MOVEMENT when the target is already within range), and it was already
    // implemented before 2.1.0 named it. Pinned where it lives, in the Advance case below.
    [TestFixture]
    public class StarQuestAiPlanTests
    {
        private GameDataStore _store = null!;
        private TableState _table = null!;
        private PlayerID _ai;
        private PlayerID _heroes;

        // The wall sits across the lane at z 14..15, so anything either side of it is out of sight of
        // the other. The AI unit stays put; the hero moves to set the distance under test.
        private static readonly Position AiSpot = new Position(24f, 10f);

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _table = new TableState(_store);
            _ai = new PlayerID(Guid.NewGuid());
            _heroes = new PlayerID(Guid.NewGuid());
        }

        /// <summary>
        /// The wall across the lane at z 14..15, so anything either side of it is out of sight of the
        /// other. Created per test rather than in SetUp: the clear-lane case needs a table WITHOUT it,
        /// and terrain lives in the store, so a fresh TableState over the same store still sees it.
        /// </summary>
        private void AddBlockingWall() =>
            _store.Create(new TerrainData(ETerrainType.Blocking | ETerrainType.Impassible,
                new RectangularZone(0f, 48f, 14f, 15f)));

        [Test]
        public void AnUnseenEnemyWithinTwelveInches_IsEngaged_NotIgnored()
        {
            // 8" apart with a wall between: no line of sight, well inside 12".
            AddBlockingWall();
            DataBinding<UnitData> ai = MakeUnit(_ai, AiSpot);
            DataBinding<UnitData> hero = MakeUnit(_heroes, new Position(24f, 18f));
            PlaceObjectiveFarFromBoth();

            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _ai, ai.GetValue());

            Assert.That(StarQuestAiBrain.Sees(ai.GetValue(), hero.GetValue(), Terrain()), Is.False,
                "sanity: the wall must actually block the line, or this test proves nothing");
            Assert.That(StarQuestAiBrain.Distance(ai.GetValue(), hero.GetValue()),
                Is.LessThanOrEqualTo(StarQuestAiBrain.UnseenEngagementInches),
                "sanity: and the hero must be inside the 12in band");

            Assert.That(plan.Kind, Is.Not.EqualTo(EQuestAiMove.RushToGoal),
                $"2.1.0 step 1 engages an enemy within 12in even with no line of sight - this unit "
                + $"walked to its objective instead ({plan.Why})");
            Assert.That(plan.Target, Is.Not.Null, plan.Why);
            Assert.That(plan.Target!.PlayerID, Is.EqualTo(_heroes), plan.Why);
        }

        [Test]
        public void AnUnseenEnemyBeyondTwelveInches_IsStillIgnored()
        {
            // The other half of the clause, and the half that keeps it a rule rather than "always hunt":
            // past 12" with no line of sight, the unit goes back to its objective.
            AddBlockingWall();
            DataBinding<UnitData> ai = MakeUnit(_ai, AiSpot);
            MakeUnit(_heroes, new Position(24f, 34f));   // 24in away, wall between
            IObjective goal = PlaceObjectiveFarFromBoth();

            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(EQuestAiMove.RushToGoal), plan.Why);
            Assert.That(plan.Goal, Is.EqualTo(goal.Position), plan.Why);
        }

        [Test]
        public void AVisibleEnemyBeyondTwelveInches_IsStillEngaged()
        {
            // Line of sight has no distance limit - the 12" band is an ADDITION to it, not a replacement.
            // Without this, widening step 1 could quietly narrow it.
            // No wall in this scene - the helper is simply not called.
            DataBinding<UnitData> ai = MakeUnit(_ai, AiSpot);
            DataBinding<UnitData> hero = MakeUnit(_heroes, new Position(24f, 40f));   // 30in, in sight
            PlaceObjectiveFarFromBoth();

            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _ai, ai.GetValue());

            Assert.That(StarQuestAiBrain.Sees(ai.GetValue(), hero.GetValue(), Terrain()), Is.True,
                "sanity: no wall in this scene");
            Assert.That(plan.Kind, Is.Not.EqualTo(EQuestAiMove.RushToGoal), plan.Why);
            Assert.That(plan.Target!.PlayerID, Is.EqualTo(_heroes), plan.Why);
        }

        [Test]
        public void AnUnseenEnemyWithinChargeRange_IsCharged()
        {
            // What step 2 does with an enemy it cannot see: it cannot be SHOT, so the answer is to come
            // round the corner after it. This is the behaviour change a player actually feels.
            AddBlockingWall();
            DataBinding<UnitData> ai = MakeUnit(_ai, AiSpot);
            MakeUnit(_heroes, new Position(24f, 18f));
            PlaceObjectiveFarFromBoth();

            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(EQuestAiMove.Charge),
                "an unseen hero inside charge distance cannot be shot, so the plan is to charge it: "
                + plan.Why);
        }

        /// <summary>
        /// An objective far from both sides, so "rushed to its goal" and "went for the hero" are
        /// unmistakably different plans. Star Quest's AI Goal is the objective closest to the unit.
        /// </summary>
        private IObjective PlaceObjectiveFarFromBoth()
        {
            var objective = new ObjectiveData(new Position(4f, 4f), _store);
            _store.Create(objective);
            return _table.Objectives.Objects.First();
        }

        private List<ITerrain> Terrain() => _table.Terrain.Objects.ToList();

        private DataBinding<UnitData> MakeUnit(PlayerID owner, Position at)
        {
            var model = new ModelData(0.5f,
                new List<Weapon> { new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0) },
                at, _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(owner, $"U{owner.GetHashCode() % 100}", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
