using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The rules-accuracy pass against Grimdark Future v3.5.1 core rules: each test pins one rule the
    /// engine used to get wrong, quoting the rule it now follows.
    /// </summary>
    [TestFixture]
    public class RulesAccuracyTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
        }

        // --- Unit coherency: "forming an uninterrupted chain of models in 1" coherency" ---------------

        [Test]
        public void Coherency_TwoSeparatePairs_AreNotAChain()
        {
            // 1" bases. Each pair is in base contact, but the pairs are 4" apart: every model has a
            // neighbour within 1", yet the unit is two clumps, not one chain.
            var models = new[]
            {
                MakeModel(new Position(10, 10)), MakeModel(new Position(11, 10)),
                MakeModel(new Position(16, 10)), MakeModel(new Position(17, 10)),
            };

            bool ok = MovementUtilities.ValidatePaths(StayInPlace(models), 12f, terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.False, "two separate clumps break coherency");
            Assert.That(errors.Count(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel),
                Is.EqualTo(2), "the two models outside the main clump (ties keep the first) are flagged");
        }

        [Test]
        public void Coherency_AnUnbrokenLine_IsAChain()
        {
            // Four models in a row, each 0.5" from the next: one chain, 4.5" end to end.
            var models = new[]
            {
                MakeModel(new Position(10, 10)), MakeModel(new Position(11.5f, 10)),
                MakeModel(new Position(13, 10)), MakeModel(new Position(14.5f, 10)),
            };

            bool ok = MovementUtilities.ValidatePaths(StayInPlace(models), 12f, terrain: null,
                out List<ReasonForInvalidMove> errors);

            Assert.That(ok, Is.True, string.Join(", ", errors.Select(e => e.ErrorReasonType)));
        }

        // --- Difficult terrain: "if any model ... moves in or through difficult terrain ... then all
        // --- models in the unit may not move more than 6"" ------------------------------------------

        [Test]
        public void DifficultTerrain_OneModelCrossing_CapsTheWholeUnit()
        {
            // Two models side by side moving 8" east. Only the top one's path crosses the wood.
            DataBinding<ModelData> inWood = MakeModel(new Position(10, 20));
            DataBinding<ModelData> inOpen = MakeModel(new Position(10, 21));
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(inWood, new List<Position> { new Position(18, 20) }),
                new ModelMoveEntry(inOpen, new List<Position> { new Position(18, 21) }),
            };
            var terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(13, 15, 18, 20)),
            };

            MovementUtilities.ValidatePaths(moves, 12f, terrain, out List<ReasonForInvalidMove> errors);

            var capped = errors.Where(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit)
                .Select(e => e.RelevantModel).ToList();
            Assert.That(capped, Does.Contain(inWood), "the model in the wood is over 6\"");
            Assert.That(capped, Does.Contain(inOpen), "and so is its squad-mate in the open - the cap is unit-wide");
        }

        [Test]
        public void DifficultTerrain_NobodyCrossing_NoCap()
        {
            DataBinding<ModelData> a = MakeModel(new Position(10, 30));
            DataBinding<ModelData> b = MakeModel(new Position(10, 31));
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(a, new List<Position> { new Position(18, 30) }),
                new ModelMoveEntry(b, new List<Position> { new Position(18, 31) }),
            };
            var terrain = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Difficult, new RectangularZone(13, 15, 18, 20)),
            };

            MovementUtilities.ValidatePaths(moves, 12f, terrain, out List<ReasonForInvalidMove> errors);

            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit),
                Is.False);
        }

        // --- Dangerous terrain: "roll one die (or as many dice as the model's Tough value)" -------------

        [Test]
        public void DangerousTerrain_ToughModel_RollsToughDice()
        {
            DataBinding<UnitData> unit = MakeUnit("Walker", 1, new Position(0f, 0f));
            unit.GetValue().Models[0].SetMaxWounds(6);

            MovementExecutor.DangerousTerrainResult rolled = CrossMinefield(unit, new FixedFaceDiceRoller(4));

            Assert.That(rolled.Roll, Is.Not.Null);
            Assert.That(rolled.Roll!.TotalRolls, Is.EqualTo(6f).Within(0.001f), "Tough(6) rolls six dice");
            Assert.That(rolled.Wounds, Is.EqualTo(0f), "no 1s, no wounds");
        }

        [Test]
        public void DangerousTerrain_ToughModel_TakesAWoundPerOne()
        {
            DataBinding<UnitData> unit = MakeUnit("Walker", 1, new Position(0f, 0f));
            unit.GetValue().Models[0].SetMaxWounds(3);

            MovementExecutor.DangerousTerrainResult rolled = CrossMinefield(unit, new FixedFaceDiceRoller(1));

            Assert.That(rolled.Wounds, Is.EqualTo(3f).Within(0.001f), "three dice, three 1s, three wounds");
            Assert.That(rolled.PendingWounds.Sum(p => p.Wounds), Is.EqualTo(3f).Within(0.001f));
        }

        [Test]
        public void DangerousTerrain_OrdinaryModels_StillRollOneDieEach()
        {
            DataBinding<UnitData> unit = MakeUnit("Grunts", 3, new Position(0f, 0f));

            MovementExecutor.DangerousTerrainResult rolled = CrossMinefield(unit, new FixedFaceDiceRoller(4));

            Assert.That(rolled.Roll!.TotalRolls, Is.EqualTo(3f).Within(0.001f));
        }

        // --- Horde shop: "within 9" of a seized objective" measured from the base edge -----------------

        [Test]
        public void HordeShop_RangeIsMeasuredFromTheBaseEdge()
        {
            // 1" base, centre 9.4" from the objective: the base edge is 8.9" away - in range.
            DataBinding<UnitData> unit = MakeUnit("Grunts", 1, new Position(20f, 20f));
            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { unit });
            _store.Create(army);
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var objective = new HordeShopObjective(1, 29.4f, 20f, isPriority: false);

            List<HordeShopUnit> units = HordeShop.UpgradableUnits(ctx, army, new[] { objective });

            Assert.That(units, Has.Count.EqualTo(1));
            Assert.That(units[0].NotEligibleReason, Is.Null, "8.9\" from the base edge is within 9\"");
        }

        // --- Friendly units: "Models may never be within 1" of models from other units, unless they are
        // --- taking a Charge action, and may never move through other models or units (friendly or enemy)"

        private static readonly Position FriendAt = new Position(20, 10); // a 1"-base friendly model

        private bool FriendlyFault(Position start, Position end, params EnemyModelFootprint[] enemies)
        {
            DataBinding<ModelData> mover = MakeModel(start);
            var moves = new List<ModelMoveEntry> { new ModelMoveEntry(mover, new List<Position> { end }) };
            var friends = new List<EnemyModelFootprint> { new EnemyModelFootprint(FriendAt, 0.5f, 1) };
            MovementUtilities.ValidatePaths(moves, _ => new ModelMoveBudget(12f, 12f), enemies,
                canMoveThroughEnemies: false, ignoresDifficultTerrain: false, ignoresImpassibleTerrain: false,
                terrain: null, out List<ReasonForInvalidMove> errors, friendlyFootprints: friends);
            return errors.Any(e => e.ErrorReasonType == EErrorReasonType.EndedOnFriendlyUnit);
        }

        [Test]
        public void Friendly_MovingThroughAFriendlyUnit_IsRejected()
            => Assert.That(FriendlyFault(new Position(15, 10), new Position(25, 10)), Is.True);

        [Test]
        public void Friendly_EndingWithinOneInch_IsRejected()
            // ends 0.5" from the friendly base (centres 1.5" apart, two 0.5" radii)
            => Assert.That(FriendlyFault(new Position(15, 10), new Position(18.5f, 10)), Is.True);

        [Test]
        public void Friendly_EndingOverOneInchAway_IsFine()
            => Assert.That(FriendlyFault(new Position(15, 10), new Position(17.9f, 10)), Is.False);

        [Test]
        public void Friendly_GoingAround_IsFine()
            => Assert.That(FriendlyFault(new Position(15, 10), new Position(15, 14)), Is.False);

        [Test]
        public void Friendly_AlreadyClose_MayMoveAway()
            => Assert.That(FriendlyFault(new Position(18.5f, 10), new Position(14, 10)), Is.False);

        [Test]
        public void Friendly_AChargeMayEndWithinOneInch()
            // an enemy just beyond the friendly: ending in contact with it is a charge
            // (ends 0.72" from the friendly and 0.02" from the enemy)
            => Assert.That(FriendlyFault(new Position(14, 11.4f), new Position(19, 11.4f),
                new EnemyModelFootprint(new Position(20, 11.6f), 0.5f, 2)), Is.False);

        [Test]
        public void Friendly_TheSameEndSpotWithoutACharge_IsRejected()
            => Assert.That(FriendlyFault(new Position(14, 11.4f), new Position(19, 11.4f)), Is.True);

        [Test]
        public void Coherency_AMoveThatSplitsTheUnit_IsRejectedByTheLenientCheck()
        {
            // A tidy line of four splits into two pairs 4" apart.
            var models = new[]
            {
                MakeModel(new Position(10, 40)), MakeModel(new Position(11, 40)),
                MakeModel(new Position(12, 40)), MakeModel(new Position(13, 40)),
            };
            var moves = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(models[0], new List<Position> { new Position(10, 40) }),
                new ModelMoveEntry(models[1], new List<Position> { new Position(11, 40) }),
                new ModelMoveEntry(models[2], new List<Position> { new Position(16, 40) }),
                new ModelMoveEntry(models[3], new List<Position> { new Position(17, 40) }),
            };
            MovementUtilities.ValidatePaths(moves, _ => new ModelMoveBudget(12f, 12f),
                new List<EnemyModelFootprint>(), false, false, false, null, out List<ReasonForInvalidMove> errors,
                lenientCoherency: true);
            Assert.That(errors.Any(e => e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel), Is.True);
        }

        // --- Deployment: "places one unit fully within 12" of their table edge" ------------------------

        [Test]
        public void DeploymentZones_Are12InchesDeep()
        {
            Assert.That(GameWideConstants.DEPLOYMENT_DISTANCE_INCHES, Is.EqualTo(12f));
        }

        // ------------------------------------------------------------------------------------------------

        private MovementExecutor.DangerousTerrainResult CrossMinefield(DataBinding<UnitData> unit, IDiceRoller roller)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester(), roller);
            var danger = new List<ITerrain>
            {
                new TerrainData(ETerrainType.Dangerous, new RectangularZone(3, 5, -2, 2)),
            };
            var paths = unit.GetValue().ModelBindings
                .Select(m => new ModelMoveEntry(m, new List<Position> { new Position(8f, 0f) }))
                .ToList();
            return MovementExecutor.RollDangerousTerrain(ctx, paths, danger, unit.GetValue());
        }

        private static List<ModelMoveEntry> StayInPlace(params DataBinding<ModelData>[] models) =>
            models.Select(m => new ModelMoveEntry(m, new List<Position> { m.GetValue().PositionBinding.GetValue() }))
                .ToList();

        private DataBinding<ModelData> MakeModel(Position position)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(string name, int modelCount, Position pos)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
                modelBindings.Add(MakeModel(pos));
            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
