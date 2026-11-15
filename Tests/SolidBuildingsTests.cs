using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.SaveLoad;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #460 - Fast-Play Simplified Terrain: every piece is a solid building (GF / AoF Advanced Rulebooks 3.6.0, p17
    /// and p33). Kai, 2026-11-15: capacity by size (6 / 11 / 21), every side has firing ports, no destruction.
    /// </summary>
    [TestFixture]
    public class SolidBuildingsTests
    {
        private GameDataStore _store = null!;
        private PlayerID _me, _them;
        private readonly List<UnitData> _units = new();

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _units.Clear();
        }

        private static TerrainData House(float left, float bottom, float size = 6f) =>
            new(SolidBuildings.Simplified(ETerrainType.Difficult | ETerrainType.Cover), new RectangularZone(left, left + size, bottom, bottom + size));

        private UnitData Unit(PlayerID owner, float x, float z, int models = 3, float tough = 1f)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { new Weapon("CCW", 0f, 1, 0) }, new Position(x + i * 1.1f, z), _store);
                model.SetMaxWounds((int)tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, "Squad", quality: 4, defense: 4, modelBindings: bindings);
            UnitData made = _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
            _units.Add(made);
            return made;
        }

        private List<ModelMoveEntry> MoveBy(UnitData unit, float dx, float dz) =>
            unit.ModelBindings.Select(b => new ModelMoveEntry(b,
                new List<Position> { new Position(b.GetValue().Position.x + dx, b.GetValue().Position.z + dz) })).ToList();

        private string? Problem(UnitData unit, List<ModelMoveEntry> moves, params TerrainData[] terrain) =>
            SolidBuildings.MoveProblem(unit, moves, terrain, _units, u => u.PlayerID == unit.PlayerID);

        [Test]
        public void EveryPiece_BecomesABuilding_CoverAndDenseSight_NoLongerDifficult()
        {
            ETerrainType t = SolidBuildings.Simplified(ETerrainType.Difficult | ETerrainType.Cover | ETerrainType.Blocking);
            Assert.That(t.HasFlag(ETerrainType.SolidBuilding) && t.HasFlag(ETerrainType.Impassible), Is.True);
            Assert.That(t.HasFlag(ETerrainType.Cover) && t.HasFlag(ETerrainType.Dense), Is.True, "in Cover; sight into and out, not through");
            Assert.That(t.HasFlag(ETerrainType.Difficult) || t.HasFlag(ETerrainType.Blocking), Is.False);
        }

        [Test]
        public void Capacity_BySize()
        {
            Assert.That(SolidBuildings.Capacity(House(0, 0, 4f)), Is.EqualTo(6), "16 sq in: a shed");
            Assert.That(SolidBuildings.Capacity(House(0, 0, 6f)), Is.EqualTo(11), "36 sq in: a house");
            Assert.That(SolidBuildings.Capacity(House(0, 0, 12f)), Is.EqualTo(21), "144 sq in: a fortification");
        }

        [Test]
        public void SightPassesIntoAndOut_ButNotThrough()
        {
            TerrainData house = House(10, 10);
            Assert.That(house.EvaluateSightLine(new Position(13, 13), new Position(30, 13)), Is.Not.EqualTo(ESightLineEffect.Blocking), "out of it");
            Assert.That(house.EvaluateSightLine(new Position(0, 13), new Position(30, 13)), Is.EqualTo(ESightLineEffect.Blocking), "through it");
        }

        [Test]
        public void TheWholeUnitGoesIn_OrNoneOfIt()
        {
            TerrainData house = House(10, 10);
            UnitData squad = Unit(_me, 4f, 12f);
            Assert.That(Problem(squad, MoveBy(squad, 7.5f, 0), house), Is.Null, "all three inside");
            Assert.That(Problem(squad, MoveBy(squad, 5.5f, 0), house), Does.Contain("whole unit"), "one left on the doorstep");
        }

        [Test]
        public void ABuildingHasRoomForItsCapacity_AndTheEnemyKeepsItsOwn()
        {
            TerrainData shed = House(10, 10, 4.5f);   // 20 sq in: 6 spaces
            Unit(_me, 10.5f, 11f, models: 4);          // 4 spaces already inside
            UnitData more = Unit(_me, 4f, 12f, models: 3);
            Assert.That(Problem(more, MoveBy(more, 6.6f, 0), shed), Does.Contain("holds 6"));

            TerrainData house = House(30, 10);
            Unit(_them, 31f, 12f, models: 2);
            UnitData mine = Unit(_me, 24f, 12f, models: 2);
            Assert.That(Problem(mine, MoveBy(mine, 8f, 0), house), Does.Contain("enemy holds"));
        }

        [Test]
        public void BigModels_TakeThreeSpaces_AndTooBigOnesCantGoIn()
        {
            TerrainData house = House(10, 10);   // 11
            UnitData ogres = Unit(_me, 4f, 12f, models: 3, tough: 3f);   // 9 spaces
            Assert.That(Problem(ogres, MoveBy(ogres, 7.5f, 0), house), Is.Null);
            UnitData monster = Unit(_me, 4f, 20f, models: 1, tough: 9f);
            Assert.That(Problem(monster, MoveBy(monster, 8f, -7f), house), Does.Contain("too big"));
        }

        [Test]
        public void Leaving_StaysWithin6_AndNoBuildingHopping()
        {
            TerrainData a = House(10, 10), b = House(20, 10);
            UnitData squad = Unit(_me, 11f, 12f);
            Assert.That(Problem(squad, MoveBy(squad, 0, 9f), a, b), Is.Null, "just outside, within 6\"");
            Assert.That(Problem(squad, MoveBy(squad, 0, 14f), a, b), Does.Contain("within 6"));
            Assert.That(Problem(squad, MoveBy(squad, 10f, 0), a, b), Does.Contain("same activation"));
        }

        [Test]
        public void AModelMayWalkIntoTheBuilding_ButNotThroughOne()
        {
            TerrainData house = House(10, 10);
            UnitData squad = Unit(_me, 4f, 12f, models: 1);
            List<ModelMoveEntry> into = MoveBy(squad, 8f, 1f);
            List<ModelMoveEntry> through = MoveBy(squad, 14f, 1f);

            bool Valid(List<ModelMoveEntry> moves, bool entry) => MovementUtilities.ValidatePaths(moves,
                _ => new ModelMoveBudget(12f, 12f), new List<EnemyModelFootprint>(), false, false, false,
                new List<ITerrain> { house }, out _, buildingEntry: entry);

            Assert.That(Valid(into, entry: true), Is.True, "entering");
            Assert.That(Valid(into, entry: false), Is.False, "the bots' planners keep every building a wall");
            Assert.That(Valid(through, entry: true), Is.False, "through and out the other side");
        }

        [Test]
        public void NoBuildings_NoProblem()
        {
            UnitData squad = Unit(_me, 4f, 12f);
            Assert.That(Problem(squad, MoveBy(squad, 7.5f, 0)), Is.Null);
            Assert.That(Problem(squad, MoveBy(squad, 7.5f, 0), new TerrainData(ETerrainType.Cover, new RectangularZone(10, 16, 10, 16))), Is.Null);
        }

        [TestCase(EGameSystem.GrimdarkFuture, 4600)]
        [TestCase(EGameSystem.AgeOfFantasy, 4601)]
        public async Task AGameWithSimplifiedTerrain_PlaysToTheEnd_OnBuildings(EGameSystem system, int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(EAiProfile.SoloCoOp, $"AI {i}",
                    slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.FastPlaySimplifiedTerrain = true;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            var pieces = new TableState(store).Terrain.Objects.ToList();
            Assert.That(pieces, Is.Not.Empty);
            Assert.That(pieces.All(SolidBuildings.Is), Is.True, "every piece is a building");
        }
    }
}
