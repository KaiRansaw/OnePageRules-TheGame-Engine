using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // Dangerous terrain deals wounds but is NOT a morale-test source (corrected 2026-07-02). Shooting, melee,
    // transport destruction, etc. cause morale tests; crossing dangerous terrain never does. The earlier #009
    // wiring that ran a half-strength morale test from dangerous terrain was a rules bug and was removed.
    //
    // #414 / 3.6.0 reshaped WHO rolls, and these two tests had to be rewritten for it rather than repaired.
    // The test is now per UNIT ("as many dice as the number of wounds it would take to fully destroy them"),
    // so one model clipping the zone puts the whole unit's remaining wounds on the dice, and models that
    // never went near the terrain can die. Both tests used to assert the opposite ("a model that never
    // crossed is untouched"), which is exactly the sentence 3.6.0 deleted.
    //
    // That also kills FixedFaceDiceRoller(1) as the roller here: with dice == remaining wounds, an all-1s
    // batch now ALWAYS destroys the unit outright, and a dead unit can't demonstrate "was not Shaken". So
    // these two use PartialOnesDiceRoller to land a chosen number of wounds and leave the unit standing,
    // which is the only state in which the morale claim is observable at all.
    [TestFixture]
    public class DangerousTerrainMoraleTests
    {
        private GameDataStore _store = null!;
        private static readonly RectangularZone DangerZone = new RectangularZone(3, 5, -2, 2);

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        [Test]
        public async Task ReducedToHalfByDangerousTerrain_TakesNoMoraleTest_NotShaken()
        {
            // 4 one-wound models, ONE of them clipping the dangerous zone. Under 3.6.0 that puts 4 dice on
            // the table - the unit's remaining wounds - not 1. Two come up 1, so the unit loses half its
            // models. Half strength from shooting forces a morale test; dangerous terrain never does.
            var crossing = MakeModel(new Position(0, 0));
            var clear1 = MakeModel(new Position(0, 5));
            var clear2 = MakeModel(new Position(0, 6));
            var clear3 = MakeModel(new Position(0, 7));
            var unit = MakeUnit(crossing, clear1, clear2, clear3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(8, 5) }),
                new ModelMoveEntry(clear2, new List<Position> { new Position(8, 6) }),
                new ModelMoveEntry(clear3, new List<Position> { new Position(8, 7) }),
            };

            await RunStage(unit, paths, new PartialOnesDiceRoller(ones: 2));

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(2),
                "two 1s came up, so the unit took two wounds and lost two one-wound models.");
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "THE claim: dangerous terrain never triggers a morale test, so the unit is not Shaken even "
                + "at half strength.");
        }

        [Test]
        public async Task DangerousTerrainButStaysAboveHalf_NoMoraleTest_Survives()
        {
            // Same shape, one wound instead of two: above half strength afterwards, and still no morale test.
            var crossing = MakeModel(new Position(0, 0));
            var clear1 = MakeModel(new Position(0, 5));
            var clear2 = MakeModel(new Position(0, 6));
            var clear3 = MakeModel(new Position(0, 7));
            var unit = MakeUnit(crossing, clear1, clear2, clear3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(8, 5) }),
                new ModelMoveEntry(clear2, new List<Position> { new Position(8, 6) }),
                new ModelMoveEntry(clear3, new List<Position> { new Position(8, 7) }),
            };

            await RunStage(unit, paths, new PartialOnesDiceRoller(ones: 1));

            Assert.That(unit.GetValue().GetIsAlive(), Is.True);
            Assert.That(unit.GetValue().Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.False,
                "dangerous terrain never triggers a morale test.");
            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(3),
                "one 1 came up, so one model died; the other three live.");
        }

        [Test]
        public async Task AnEntirelyUncrossedModel_CanStillDie_BecauseTheTestIsUnitWide()
        {
            // The 3.6.0 change, stated on its own so nobody reads the two tests above as being only about
            // morale. One model skirts the minefield, the other three walk nowhere near it, and a model that
            // never touched the terrain is the one that dies: the unit rolled four dice, and the wounds land
            // on the unit. Before 3.6.0 only the crossing model was ever at risk.
            var crossing = MakeModel(new Position(0, 0));
            var clear1 = MakeModel(new Position(0, 20));
            var clear2 = MakeModel(new Position(0, 21));
            var clear3 = MakeModel(new Position(0, 22));
            var unit = MakeUnit(crossing, clear1, clear2, clear3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(1, 20) }),
                new ModelMoveEntry(clear2, new List<Position> { new Position(1, 21) }),
                new ModelMoveEntry(clear3, new List<Position> { new Position(1, 22) }),
            };

            // Four dice, all 1s: every remaining wound in the unit is lost, so the unit is wiped. That is
            // the honest consequence of the new wording and worth pinning - an all-1s dangerous test is now
            // lethal to any unit, however big, because the pool is sized to exactly destroy it.
            await RunStage(unit, paths, new PartialOnesDiceRoller(ones: 4));

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(0),
                "4 remaining wounds, 4 dice, 4 ones - the pool is sized to destroy the unit exactly.");
        }

        [Test]
        public async Task AMauledUnitRollsFewerDice_BecauseThePoolIsRemainingWounds()
        {
            // "The number of wounds it would take to fully destroy them" is measured from where the unit IS,
            // not from full strength - the same reading Falling and Regeneration use. A 4-model unit already
            // down to 2 living models rolls 2 dice, so a 2-ones batch wipes it; a fresh one would have
            // survived the same roll with half its models.
            var crossing = MakeModel(new Position(0, 0));
            var clear1 = MakeModel(new Position(0, 5));
            var dead1 = MakeModel(new Position(0, 6));
            var dead2 = MakeModel(new Position(0, 7));
            var unit = MakeUnit(crossing, clear1, dead1, dead2);
            dead1.GetValue().DealWounds(1f);
            dead2.GetValue().DealWounds(1f);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(crossing, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(clear1, new List<Position> { new Position(8, 5) }),
            };

            await RunStage(unit, paths, new PartialOnesDiceRoller(ones: 2));

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(0),
                "2 living models = 2 dice, and both came up 1.");
        }

        [Test]
        public async Task ProbabilisticRoller_DealsFractionalWoundsAcrossTheBatch()
        {
            // 3 models all cross the dangerous zone. Under the probabilistic roller a single batch of 3 d6
            // yields the expected 3/6 = 0.5 ones, spread evenly (0.5/3 wound each) -- fractional wounds that
            // don't kill a 1-wound model. This is exactly what N separate decisive per-model rolls could not
            // produce, and why batching matters for the probabilistic roller.
            var m1 = MakeModel(new Position(0, 0));
            var m2 = MakeModel(new Position(0, 1));
            var m3 = MakeModel(new Position(0, -1));
            var unit = MakeUnit(m1, m2, m3);

            var paths = new List<ModelMoveEntry>
            {
                new ModelMoveEntry(m1, new List<Position> { new Position(8, 0) }),
                new ModelMoveEntry(m2, new List<Position> { new Position(8, 1) }),
                new ModelMoveEntry(m3, new List<Position> { new Position(8, -1) }),
            };

            var settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            var ctx = new TestGameContext(_store, new ProbabilisticDiceRoller(), settings: settings);
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) };
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);

            Assert.That(unit.GetValue().Models.Count(m => m.GetIsAlive()), Is.EqualTo(3),
                "fractional wounds from the probabilistic batch don't kill a 1-wound model.");
            float total = m1.GetValue().WoundsDealt + m2.GetValue().WoundsDealt + m3.GetValue().WoundsDealt;
            Assert.That(total, Is.EqualTo(0.5f).Within(0.001f),
                "the batch of 3 dice deals an expected 3/6 = 0.5 wounds total, spread across the models.");
        }

        // Helpers

        // The stage rolls the batch; ExecuteMoveStage lands it after the move beat (so a casualty falls at
        // its destination instead of vanishing off the start line). Both halves run here, which is what the
        // real movement flow does, so these assertions stay about the wounds and the morale consequence.
        private async Task RunStage(DataBinding<UnitData> unit, List<ModelMoveEntry> paths, IDiceRoller roller)
        {
            var ctx = new TestGameContext(_store, roller);
            var stage = new ApplyNonMovementTerrainEffectsStage(ctx, new NoOpLayer<IMovementActionContext>());
            stage.OnAppliedNonMovementTerrainEffects.Bind("done");
            var terrain = new List<ITerrain> { new TerrainData(ETerrainType.Dangerous, DangerZone) };
            var movCtx = new StubMovementContext(ctx, unit, paths, terrain);
            await stage.Enter(movCtx);
            await MovementExecutor.ResolveDangerousTerrain(ctx, movCtx.PendingDangerousTerrain);
        }

        private DataBinding<ModelData> MakeModel(Position initialPosition)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), initialPosition, _store);
            return _store.GetDataBinding<ModelData>(_store.Create(model));
        }

        private DataBinding<UnitData> MakeUnit(params DataBinding<ModelData>[] models)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit", quality: 4, defense: 4,
                modelBindings: models.ToList());
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        /// <summary>
        /// Lands exactly <c>ones</c> dice on face 1 and the rest on face 6, in a batch that honours
        /// rollCount. Needed because the dangerous-terrain pool is now sized to destroy the unit exactly
        /// (#414), so <c>FixedFaceDiceRoller(1)</c> can only ever produce a wipe - there is no roll count at
        /// which it leaves a survivor, and a destroyed unit cannot show whether it was asked for a morale
        /// test. Asking for a specific number of wounds is the only way to observe the survivor states.
        /// </summary>
        private sealed class PartialOnesDiceRoller : IDiceRoller
        {
            private readonly float _ones;
            public PartialOnesDiceRoller(float ones) => _ones = ones;

            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                float ones = MathF.Min(_ones, rollCount);
                perSide[0] = ones;                      // face 1 - a wound each
                perSide[sideCount - 1] = rollCount - ones;  // the rest are safe
                return new DiceResults(perSide, 1);
            }
        }
    }
}
