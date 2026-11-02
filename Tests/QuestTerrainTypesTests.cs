using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #440 / audit finding 8 - <b>Dense and Unstable terrain</b>, the two types AoF: Quest defines and
    /// the engine had no equivalent for.
    ///
    /// <para>
    /// Sources, and the scoping that matters: AoFQ's Beginners Guide v2.1.0 defines Dense
    /// (<i>"Units may draw line of sight into and out of dense terrain, but not through it to targets on
    /// the other side"</i>) and Unstable (<i>"If any model in a unit moves over 6" when moving into or
    /// through unstable terrain, then it counts as Dangerous terrain"</i>). <b>GF: Star Quest's Core
    /// Rules v2.1.0 was read end to end and lists only Cover, Difficult and Dangerous</b>, as do the four
    /// standard books - so this is ONE system's rule, not "the Quest systems'", which is the grouping
    /// that would have been assumed.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestTerrainTypesTests
    {
        // -- Which systems play with them ---------------------------------------------------------------

        [Test]
        public void OnlyAoFQuest_PlaysWithDenseAndUnstable()
        {
            Assert.That(QuestTerrainTypes.AppliesTo(EGameSystem.AoFQuest), Is.True,
                "its Beginners Guide v2.1.0 defines both.");

            foreach (EGameSystem other in new[]
                     {
                         EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight,
                         EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest,
                     })
            {
                Assert.That(QuestTerrainTypes.AppliesTo(other), Is.False,
                    $"{other}: its own book's terrain section lists only Cover, Difficult and Dangerous. "
                    + "Star Quest especially - being the other Quest system is not a source.");
            }
        }

        // -- The name mapping, and the guard that keeps it honest ---------------------------------------

        /// <summary>
        /// The mapping is keyed on piece NAME, because the guide keys it on what the piece is ("Forests -
        /// Difficult + Cover + Dense", "Ruins - Cover + Unstable") and the engine's flags cannot tell a
        /// forest from a ruin - both are Cover + Difficult. Name matching rots silently, so this is the
        /// guard: rename a pool piece without updating the list and this fails, instead of the terrain
        /// type quietly stopping being applied to anything.
        /// </summary>
        [Test]
        public void EveryMappedPieceName_StillExistsInThePool()
        {
            var poolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (TerrainPieceEntry piece in DefaultTerrainPool.GetPalette())
            {
                if (!string.IsNullOrWhiteSpace(piece.Name)) poolNames.Add(piece.Name!);
            }

            foreach (TerrainPieceEntry piece in DefaultTerrainPool.Get().Pieces)
            {
                if (!string.IsNullOrWhiteSpace(piece.Name)) poolNames.Add(piece.Name!);
            }

            var missing = QuestTerrainTypes.DensePieceNames
                .Concat(QuestTerrainTypes.UnstablePieceNames)
                .Where(name => !poolNames.Contains(name))
                .ToList();

            Assert.That(missing, Is.Empty,
                "these names are mapped to a Quest terrain type but no longer exist in the terrain pool, "
                + $"so the type is applied to nothing: {string.Join(", ", missing)}");
        }

        [TestCase("Forest", ETerrainType.Dense)]
        [TestCase("Ancient wood", ETerrainType.Dense)]
        [TestCase("Copse", ETerrainType.Dense)]
        [TestCase("Ruined building", ETerrainType.Unstable)]
        [TestCase("Cathedral shell", ETerrainType.Unstable)]
        [TestCase("Collapsed wall", ETerrainType.Unstable)]
        [TestCase("Rubble", ETerrainType.Unstable)]
        public void MappedPieces_GainTheirType(string pieceName, ETerrainType expected)
        {
            Assert.That(QuestTerrainTypes.ExtraFlagsFor(pieceName), Is.EqualTo(expected));
        }

        /// <summary>
        /// Pieces the guide's mapping does not cover gain nothing. "Crater" and "Wreckage" are the
        /// tempting ones - they would be reasonable guesses, and guessing is what the audit that produced
        /// this finding exists to stop.
        /// </summary>
        [TestCase("Crater")]
        [TestCase("Crater rim")]
        [TestCase("Wreckage")]
        [TestCase("Stream")]
        [TestCase("Marsh")]
        [TestCase("Bunker")]
        [TestCase("")]
        [TestCase(null)]
        public void UnmappedPieces_GainNothing(string? pieceName)
        {
            Assert.That(QuestTerrainTypes.ExtraFlagsFor(pieceName), Is.EqualTo(ETerrainType.None));
        }

        [Test]
        public void TheMappingIsCaseInsensitive()
        {
            Assert.That(QuestTerrainTypes.ExtraFlagsFor("forest"), Is.EqualTo(ETerrainType.Dense));
            Assert.That(QuestTerrainTypes.ExtraFlagsFor("RUBBLE"), Is.EqualTo(ETerrainType.Unstable));
        }

        /// <summary>
        /// No piece is both. Not a rule from the book - the guide's Forests and Ruins are different
        /// pieces - but if a future edit put a name in both lists the result would be a forest that is
        /// also rubble, which is a mapping mistake rather than a design choice.
        /// </summary>
        [Test]
        public void NoPieceIsBothDenseAndUnstable()
        {
            Assert.That(QuestTerrainTypes.DensePieceNames
                    .Intersect(QuestTerrainTypes.UnstablePieceNames, StringComparer.OrdinalIgnoreCase),
                Is.Empty);
        }

        // -- Dense: what it actually does to a sight line -----------------------------------------------

        /// <summary>
        /// <i>"Units may draw line of sight into and out of dense terrain, but not THROUGH it to targets
        /// on the other side."</i> The whole rule is this one distinction, and it is the thing a plain
        /// Blocking flag cannot express.
        /// </summary>
        [Test]
        public void Dense_BlocksALineThatCrossesIt_WithBothEndsOutside()
        {
            var wood = new TerrainData(ETerrainType.Dense, new CircularZone(10f, 10f, 3f));

            ESightLineEffect effect = wood.EvaluateSightLine(
                new Position(0f, 0f, 10f), new Position(20f, 0f, 10f));

            Assert.That(effect, Is.EqualTo(ESightLineEffect.Blocking),
                "shooting across the wood is what the rule forbids.");
        }

        [Test]
        public void Dense_DoesNotBlockShootingOutOfIt()
        {
            var wood = new TerrainData(ETerrainType.Dense, new CircularZone(10f, 10f, 3f));

            ESightLineEffect effect = wood.EvaluateSightLine(
                new Position(10f, 0f, 10f), new Position(20f, 0f, 10f));

            Assert.That(effect, Is.EqualTo(ESightLineEffect.Clear),
                "\"may draw line of sight ... OUT OF dense terrain\" - a unit standing in it shoots out.");
        }

        [Test]
        public void Dense_DoesNotBlockShootingIntoIt()
        {
            var wood = new TerrainData(ETerrainType.Dense, new CircularZone(10f, 10f, 3f));

            ESightLineEffect effect = wood.EvaluateSightLine(
                new Position(0f, 0f, 10f), new Position(10f, 0f, 10f));

            Assert.That(effect, Is.EqualTo(ESightLineEffect.Clear),
                "\"may draw line of sight INTO ... dense terrain\" - a unit in it can be shot at.");
        }

        /// <summary>
        /// A line that misses the piece entirely is unaffected, which sounds obvious but is the case that
        /// catches a containment test written the wrong way round.
        /// </summary>
        [Test]
        public void Dense_DoesNotBlockALineThatMissesIt()
        {
            var wood = new TerrainData(ETerrainType.Dense, new CircularZone(10f, 10f, 3f));

            ESightLineEffect effect = wood.EvaluateSightLine(
                new Position(0f, 0f, 0f), new Position(20f, 0f, 0f));

            Assert.That(effect, Is.EqualTo(ESightLineEffect.Clear));
        }

        /// <summary>
        /// A forest is Cover + Difficult + Dense in the guide's mapping, so the Cover it already granted
        /// must survive the addition - a unit shooting INTO the wood still faces a +1 Defense roll.
        /// </summary>
        [Test]
        public void AForest_StillGivesCover_ToSomethingInsideIt()
        {
            var forest = new TerrainData(
                ETerrainType.Cover | ETerrainType.Difficult | ETerrainType.Dense,
                new CircularZone(10f, 10f, 3f));

            ESightLineEffect effect = forest.EvaluateSightLine(
                new Position(0f, 0f, 10f), new Position(10f, 0f, 10f));

            Assert.That(effect, Is.EqualTo(ESightLineEffect.Cover),
                "Dense does not block a line into it, so the piece's Cover is what applies.");
        }

        /// <summary>
        /// A piece carrying both Blocking and Dense is simply Blocking. Pinned because the order of the
        /// two checks is what decides it, and the stricter reading is the right one.
        /// </summary>
        [Test]
        public void BlockingWins_OverDense()
        {
            var piece = new TerrainData(ETerrainType.Blocking | ETerrainType.Dense,
                new CircularZone(10f, 10f, 3f));

            Assert.That(piece.EvaluateSightLine(new Position(10f, 0f, 10f), new Position(20f, 0f, 10f)),
                Is.EqualTo(ESightLineEffect.Blocking),
                "a wall with undergrowth is still a wall - you cannot see out of it.");
        }

        // -- Unstable: Dangerous, but only above 6" ----------------------------------------------------

        /// <summary>
        /// <i>"If any model in a unit moves OVER 6" when moving into or through unstable terrain, then it
        /// counts as Dangerous terrain."</i> So the same ruin is harmless on an Advance and dangerous on
        /// a Rush, and the trigger is this model's own distance.
        /// </summary>
        [TestCase(5f, false, TestName = "Unstable_A5InchMoveIsSafe")]
        [TestCase(6f, false, TestName = "Unstable_Exactly6InchesIsSafe_BecauseTheBookSaysOVER6")]
        [TestCase(6.5f, true, TestName = "Unstable_JustOver6InchesIsDangerous")]
        [TestCase(12f, true, TestName = "Unstable_ARushIsDangerous")]
        public void Unstable_CountsAsDangerous_OnlyAboveSixInches(float distance, bool expectDangerous)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f, 0f), store);
            DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));

            // A straight run from the origin, ending inside the ruin, so the path certainly touches it.
            var move = new ModelMoveEntry(binding,
                new List<Position> { new Position(distance, 0f, 0f) });
            var ruin = new TerrainData(ETerrainType.Cover | ETerrainType.Unstable,
                new CircularZone(distance, 0f, 2f));

            bool dangerous = MovementUtilities.DoesPathCrossDangerousTerrain(
                move, new List<ITerrain> { ruin });

            Assert.That(dangerous, Is.EqualTo(expectDangerous),
                expectDangerous
                    ? $"a {distance}\" move through unstable ground counts as Dangerous."
                    : $"a {distance}\" move picks its way across and counts as nothing.");
        }

        /// <summary>
        /// Real Dangerous terrain is unconditional - the distance rule must not have leaked onto it. A
        /// minefield is a minefield however carefully you step.
        /// </summary>
        [Test]
        public void RealDangerousTerrain_IsStillDangerousOnAShortMove()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f, 0f), store);
            DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));

            var move = new ModelMoveEntry(binding, new List<Position> { new Position(2f, 0f, 0f) });
            var mines = new TerrainData(ETerrainType.Dangerous, new CircularZone(2f, 0f, 2f));

            Assert.That(
                MovementUtilities.DoesPathCrossDangerousTerrain(move, new List<ITerrain> { mines }),
                Is.True, "a 2\" move into a minefield still tests.");
        }

        /// <summary>
        /// And an unstable piece the path never touches does nothing, however far the model ran - the
        /// trigger is "moving into or through" it, not "moving far".
        /// </summary>
        [Test]
        public void Unstable_DoesNothingWhenThePathMissesIt()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(0.75f, new List<Weapon>(), new Position(0f, 0f, 0f), store);
            DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));

            var move = new ModelMoveEntry(binding, new List<Position> { new Position(12f, 0f, 0f) });

            // Well off the line of travel.
            var farRuin = new TerrainData(ETerrainType.Unstable, new CircularZone(5f, 40f, 2f));

            Assert.That(
                MovementUtilities.DoesPathCrossDangerousTerrain(move, new List<ITerrain> { farRuin }),
                Is.False, "a 12\" run nowhere near the rubble is safe.");
        }
    }
}
