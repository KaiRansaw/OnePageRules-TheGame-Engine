using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 4), AoF: Quest Horde. Horde Mode v2.0.0, Hero Injuries: a revived hero "may then be
    // placed within 3" of any objective seized by the heroes". The engine always used the first seized objective in
    // table order; the player now picks when the heroes hold more than one.
    [TestFixture]
    public class QuestHordeRevivalSpotTests
    {
        private sealed class PickingRequester : IPlayerRequestByID
        {
            public StringSelectionRequest? Asked;
            public string Answer = "";
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest s) { Asked = s; return Task.FromResult((TReply)(object)Answer); }
                throw new InvalidOperationException("Unexpected request " + request.GetType());
            }
        }

        private static readonly List<(Position Spot, int Number)> TwoSeized = new()
        {
            (new Position(12, 0, 12), 2),
            (new Position(36, 0, 36), 5),
        };

        [Test]
        public async Task TwoSeizedObjectives_ThePlayerPicksWhereTheHeroComesBack()
        {
            var requester = new PickingRequester { Answer = "Objective 5" };
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(GameDataStore.GameDataStoreBuilder.GetDefault(), requester);
            Position spot = await StarQuestHordeRoundHooks.PickRevivalObjective(ctx, new PlayerID(Guid.NewGuid()), "Brakka", TwoSeized);
            Assert.That(requester.Asked, Is.Not.Null);
            Assert.That(requester.Asked!.ValidOptions, Is.EquivalentTo(new[] { "Objective 2", "Objective 5" }));
            Assert.That(spot.x, Is.EqualTo(36f));
            Assert.That(spot.z, Is.EqualTo(36f));
        }

        [Test]
        public async Task OneSeizedObjective_NoQuestion()
        {
            var requester = new PickingRequester();
            var ctx = new ChooseRangedAttackStageTests.TestGameContextWithRequester(GameDataStore.GameDataStoreBuilder.GetDefault(), requester);
            Position spot = await StarQuestHordeRoundHooks.PickRevivalObjective(ctx, new PlayerID(Guid.NewGuid()), "Brakka", TwoSeized.Take(1).ToList());
            Assert.That(requester.Asked, Is.Null);
            Assert.That(spot.x, Is.EqualTo(12f));
        }
    }
}

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (run 4): "divide the total XP as equally as possible among all heroes". The odd points
    // always went to the first heroes in the party, so with 2 XP a wave and 3 heroes the third never earned any.
    [TestFixture]
    public class QuestHordeXpSplitTests
    {
        [Test]
        public void LeftoverXp_GoesToTheHeroesWithTheLeastXpSoFar()
        {
            Assert.That(StarQuestHordeRules.Split(2, new[] { 1, 1, 0 }), Is.EqualTo(new[] { 1, 0, 1 }));
            Assert.That(StarQuestHordeRules.Split(7, new[] { 5, 0, 2 }), Is.EqualTo(new[] { 2, 3, 2 }));
        }

        [Test]
        public void ThreeWavesOfTwoXp_EveryHeroGetsTwo()
        {
            var totals = new[] { 0, 0, 0 };
            for (int wave = 0; wave < 3; wave++)
            {
                int[] s = StarQuestHordeRules.Split(2, totals);
                for (int i = 0; i < 3; i++) totals[i] += s[i];
            }
            Assert.That(totals, Is.EqualTo(new[] { 2, 2, 2 }));
        }
    }
}
