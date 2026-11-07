using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>QA 9.15: a Concussive Grenade's 6" push never carries a model off the table.</summary>
    [TestFixture]
    public class QuestShoveTests
    {
        [TearDown]
        public void TearDown() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private static UnitData Unit(GameDataStore store, PlayerID owner, params Position[] at)
        {
            var bindings = at.Select(p => store.GetDataBinding<ModelData>(store.Create(
                new ModelData(0.5f, new List<Weapon>(), p, store)))).ToList();
            return store.GetDataBinding<UnitData>(store.Create(new UnitData(owner, "U", 4, 4, bindings))).GetValue();
        }

        [TestCase(30f, 24f, 34f, 24f)]   // room to spare: the full 6"
        [TestCase(40f, 24f, 46f, 24f)]   // pushed right, off the east edge
        [TestCase(24f, 8f, 24f, 2f)]     // pushed down, off the bottom
        [TestCase(6f, 6f, 2.5f, 2.5f)]   // pushed into the corner
        [TestCase(24f, 40f, 24f, 47f)]   // already touching the top edge
        public async Task APush_StopsAtTheTableEdge(float hx, float hz, float ex, float ez)
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var a = new PlayerID(Guid.NewGuid());
            var b = new PlayerID(Guid.NewGuid());
            UnitData hero = Unit(store, a, new Position(hx, hz));
            UnitData enemy = Unit(store, b, new Position(ex, ez), new Position(ex - 1.1f, ez), new Position(ex, ez - 1.1f));
            var ctx = new TestGameContext(store, new FixedDiceRoller(4));

            var before = enemy.Models.Select(m => m.Position).ToList();
            await QuestActions.Shove(ctx, hero, enemy, 6f);
            var after = enemy.Models.Select(m => m.Position).ToList();

            // The unit moves as one block: every model by the same amount, so nothing piles up at the edge.
            float sx = after[0].x - before[0].x, sz = after[0].z - before[0].z;
            for (int i = 1; i < after.Count; i++)
            {
                Assert.That(after[i].x - before[i].x, Is.EqualTo(sx).Within(0.001f));
                Assert.That(after[i].z - before[i].z, Is.EqualTo(sz).Within(0.001f));
            }

            foreach (IModel m in enemy.Models)
            {
                float r = ((ModelData)m).BaseShape.CircumscribedRadiusInches;
                Assert.That(m.Position.x, Is.InRange(r - 0.001f, 48f - r + 0.001f), $"x {m.Position.x}");
                Assert.That(m.Position.z, Is.InRange(r - 0.001f, 48f - r + 0.001f), $"z {m.Position.z}");
            }
        }

        [Test]
        public async Task WithRoom_TheFullSixInches()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            UnitData hero = Unit(store, new PlayerID(Guid.NewGuid()), new Position(20f, 24f));
            UnitData enemy = Unit(store, new PlayerID(Guid.NewGuid()), new Position(24f, 24f));
            await QuestActions.Shove(new TestGameContext(store, new FixedDiceRoller(4)), hero, enemy, 6f);
            Assert.That(enemy.Models[0].Position.x, Is.EqualTo(30f).Within(0.001f));
        }
    }
}
