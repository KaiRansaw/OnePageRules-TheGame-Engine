using NUnit.Framework;
using FDG.StateMachine;

namespace FDG.Tests
{
    [TestFixture]
    public class HordeRulesTests
    {
        [Test]
        public void RoundLimit_IsTwelveForHordeAndFourForStandard()
        {
            Assert.That(HordeRules.RoundLimit(EGameMode.Horde), Is.EqualTo(12));
            Assert.That(HordeRules.RoundLimit(EGameMode.Standard), Is.EqualTo(GameWideConstants.NUMBER_OF_ROUNDS));
        }

        [TestCase(1, 1)]
        [TestCase(3, 1)]
        [TestCase(4, 2)]
        [TestCase(6, 2)]
        [TestCase(7, 3)]
        [TestCase(9, 3)]
        [TestCase(10, 4)]
        [TestCase(12, 4)]
        public void WaveOfRound_GroupsRoundsInThrees(int round, int expectedWave)
        {
            Assert.That(HordeRules.WaveOfRound(round), Is.EqualTo(expectedWave));
        }

        // #409 (OPR 3.6.0): the high-score multiplier counts ROUNDS, not waves. 3.5.0 could only ever
        // score x1 to x4; 3.6 runs x1 to x12, so where the defenders fell now matters much more.
        [TestCase(6, 5)]  // the 3.6 rulebook's example: overrun DURING round 6 -> multiplier five
        [TestCase(12, 11)]
        [TestCase(1, 0)]
        [TestCase(0, 0)]
        public void RoundsFinishedForScore_WhenOverrun_ExcludesTheRoundInProgress(
            int roundsFinished, int expected)
        {
            Assert.That(HordeRules.RoundsFinishedForScore(roundsFinished, defendersSurvived: false),
                Is.EqualTo(expected));
        }

        [Test]
        public void RoundsFinishedForScore_WhenSurvived_IsEveryRoundOfTheMission()
        {
            Assert.That(HordeRules.RoundsFinishedForScore(12, defendersSurvived: true), Is.EqualTo(12));
        }

        [Test]
        public void ObjectiveAnchors_AreCentreThenTheFourQuarterCentres()
        {
            var anchors = HordeRules.ObjectiveAnchors(72f, 48f);
            Assert.That(anchors.Count, Is.EqualTo(5));
            Assert.That((anchors[0].x, anchors[0].z), Is.EqualTo((36f, 24f)));
            Assert.That((anchors[1].x, anchors[1].z), Is.EqualTo((18f, 12f)));
            Assert.That((anchors[2].x, anchors[2].z), Is.EqualTo((54f, 12f)));
            Assert.That((anchors[3].x, anchors[3].z), Is.EqualTo((18f, 36f)));
            Assert.That((anchors[4].x, anchors[4].z), Is.EqualTo((54f, 36f)));
        }
            [Test]
        public void PriorityAndWaveRounds_FollowTheThreeRoundCycle()
        {
            Assert.That(Enumerable.Range(1, 12).Where(HordeRules.IsPriorityRound), Is.EqualTo(new[] { 1, 4, 7, 10 }));
            Assert.That(Enumerable.Range(1, 12).Where(HordeRules.IsWaveRound), Is.EqualTo(new[] { 4, 7, 10 }));
        }

        [TestCase(36f, 24f, 1)]
        [TestCase(54f, 36f, 5)]
        [TestCase(21f, 15f, 2)] // nudged off its anchor by terrain
        public void ObjectiveNumber_IsTheNearestAnchor(float x, float z, int expected)
        {
            Assert.That(HordeRules.ObjectiveNumber(new Position(x, z), 72f, 48f), Is.EqualTo(expected));
        }

        [TestCase(1, 1)]
        [TestCase(4, 4)]
        [TestCase(5, 2)]
        [TestCase(6, 2)]
        public void CornerForRoll_OneToFourIsThatCorner_FiveSixIsTheDefendersPick(int roll, int expected)
        {
            Assert.That(HordeRules.CornerForRoll(roll, () => 2), Is.EqualTo(expected));
        }

        private static Func<int> Rolls(params int[] faces)
        {
            var queue = new Queue<int>(faces);
            return () => queue.Dequeue();
        }

        [Test]
        public void ChoosePriority_RerollsThePreviousPriorityAndMissingObjectives()
        {
            Assert.That(HordeRules.ChoosePriority(new[] { 1, 2, 3, 4, 5 }, 3, Rolls(3, 3, 2), e => -1), Is.EqualTo(2));
            Assert.That(HordeRules.ChoosePriority(new[] { 1, 2, 5 }, null, Rolls(4, 3, 5), e => -1), Is.EqualTo(5));
        }

        [Test]
        public void ChoosePriority_SixLetsDefendersPickFromTheEligibleObjectives()
        {
            IReadOnlyList<int>? offered = null;
            int? chosen = HordeRules.ChoosePriority(new[] { 1, 2, 5 }, 1, Rolls(6), e => { offered = e; return e[^1]; });
            Assert.That(offered, Is.EqualTo(new[] { 2, 5 }));
            Assert.That(chosen, Is.EqualTo(5));
        }

        [Test]
        public void RollPriority_SixHandsTheChoiceToTheDefenders_WithoutThePreviousPriority()
        {
            (int? rolled, IReadOnlyList<int>? pickFrom) = HordeRules.RollPriority(new[] { 1, 2, 4, 5 }, 4, Rolls(6));
            Assert.That(rolled, Is.Null);
            Assert.That(pickFrom, Is.EqualTo(new[] { 1, 2, 5 }));
        }

        [Test]
        public void RollPriority_ANumberOnTheTableIsTheNewPriority()
        {
            (int? rolled, IReadOnlyList<int>? pickFrom) = HordeRules.RollPriority(new[] { 1, 2, 4, 5 }, 4, Rolls(3, 4, 2));
            Assert.That(rolled, Is.EqualTo(2));
            Assert.That(pickFrom, Is.Null);
        }

        [TestCase(1, "bottom-left")]
        [TestCase(2, "bottom-right")]
        [TestCase(3, "top-left")]
        [TestCase(4, "top-right")]
        public void CornerName_MatchesWhereTheCornerIsOnScreen(int corner, string name)
        {
            Assert.That(HordeRules.CornerName(corner), Is.EqualTo(name));
        }

        [Test]
        public void ChoosePriority_OneOrNoObjectivesLeft_MeansNoPriority()
        {
            Assert.That(HordeRules.ChoosePriority(new[] { 4 }, null, Rolls(4), e => -1), Is.Null);
            Assert.That(HordeRules.ChoosePriority(Array.Empty<int>(), null, Rolls(4), e => -1), Is.Null);
        }

        [Test]
        public void HordeObjective_TracksDamageAndPriority_StandardObjectiveDoesNot()
        {
            var store = FDG.Data.GameDataStore.GameDataStoreBuilder.GetDefault();

            var horde = new ObjectiveData(new Position(36f, 24f), store, hordeNumber: 3);
            store.Create(horde);
            Assert.That(horde.HordeNumber, Is.EqualTo(3));
            Assert.That(horde.HordeDamage, Is.EqualTo(0));
            horde.SetHordeDamage(2);
            horde.SetHordePriority(true);
            Assert.That(horde.HordeDamage, Is.EqualTo(2));
            Assert.That(horde.IsHordePriority, Is.True);

            var standard = new ObjectiveData(new Position(10f, 10f), store);
            store.Create(standard);
            standard.SetHordeDamage(2);
            Assert.That(standard.HordeNumber, Is.Null);
            Assert.That(standard.HordeDamage, Is.EqualTo(0));
            Assert.That(standard.IsHordePriority, Is.False);
        }

        [Test]
        public void TableCornerZone_BoundsStopAtTheTableEdges()
        {
            IBoundedZone zone = new TableCornerZone(new Float2(72f, 48f), 9f, 72f, 48f);
            Assert.That((zone.Bounds.Left, zone.Bounds.Right, zone.Bounds.Bottom, zone.Bounds.Top),
                Is.EqualTo((63f, 72f, 39f, 48f)));
            Assert.That(zone.IsPointWithinZone(new Float2(66f, 42f)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(63.5f, 39.5f)), Is.False, "box corner is outside the circle");
            Assert.That(PlacementUtilities.IsBaseWithinZone(new Position(73f, 47f), 0.5f, zone), Is.False, "off the table");
            Assert.That(PlacementUtilities.IsBaseWithinZone(new Position(70f, 46f), 0.5f, zone), Is.True);
        }
            [Test]
        public void RewardRounds_AreTheLastRoundOfEachWave()
        {
            Assert.That(Enumerable.Range(1, 12).Where(HordeRules.IsRewardRound), Is.EqualTo(new[] { 3, 6, 9, 12 }));
        }

        [TestCase(0, false, 0)]
        [TestCase(0, true, 0)]   // nothing held, so no priority bonus either
        [TestCase(1, false, 40)]
        [TestCase(2, true, 100)]
        [TestCase(5, true, 220)]
        public void WaveReward_Is40PerObjectivePlus20ForThePriority(int seized, bool priority, int expected)
        {
            Assert.That(HordeRules.WaveReward(seized, priority), Is.EqualTo(expected));
        }

        [Test]
        public void HordeScore_SurvivesThePerTurnProgressRewrite()
        {
            var store = FDG.Data.GameDataStore.GameDataStoreBuilder.GetDefault();
            GameProgressData Snapshot() => new GameProgressData(EResumeStage.MainPhase, 3, new List<int>(),
                new List<int>(), 0, new Dictionary<int, int>(), new List<FDG.Data.DataBinding<UnitData>>(),
                GameSettings.GetDefault());

            GameProgressUtilities.WriteProgress(store, Snapshot());
            GameProgressUtilities.AddHordePoints(store, 100);
            GameProgressUtilities.WriteProgress(store, Snapshot());   // the next activation's snapshot
            GameProgressUtilities.AddHordePoints(store, 40);

            HordeScore? score = GameProgressUtilities.TryGetProgress(store)!.Horde;
            Assert.That(score!.PointsEarned, Is.EqualTo(140));
            Assert.That(score.PointsAvailable, Is.EqualTo(140));
        }
    }
}
