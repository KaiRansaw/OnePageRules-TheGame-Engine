using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// The round summary card (2026-11-07): one summary per finished round, for every side, recorded on the
    /// progress record. Played as a real bot game so the start and end hooks are proved reachable, and the
    /// losses add up to exactly the units that died.
    /// </summary>
    [TestFixture]
    public class RoundSummaryTests
    {
        [Test]
        [CancelAfter(180_000)]
        public async Task AFullGame_RecordsEveryRound_AndEveryLoss()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, 7311, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 7311;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");

            var table = new TableState(store);
            IReadOnlyList<RoundSummary> rounds = table.Progress.RoundSummaries;
            Assert.That(rounds, Is.Not.Empty, "every finished round leaves a summary");
            Assert.That(rounds.Select(r => r.Round), Is.EqualTo(Enumerable.Range(1, rounds.Count)),
                "numbered 1, 2, 3 ... with none missing");
            Assert.That(rounds.All(r => r.Sides.Count == 2), Is.True, "one entry per side");

            int dead = table.Units.Objects.Count(u => !u.GetIsAlive());
            int lost = rounds.Sum(r => r.Sides.Sum(s => s.UnitsLost?.Count ?? 0));
            Assert.That(lost, Is.EqualTo(dead), "every destroyed unit is in exactly one round's losses");

            RoundSummary last = rounds[^1];
            foreach (RoundSideSummary side in last.Sides)
                Assert.That(side.ObjectivesHeld, Is.EqualTo(table.Progress.Scores.Single(s =>
                    table.Players.Objects.Single(p => p.SlotID == side.SlotId).PlayerID.Equals(s.PlayerID)).ObjectiveCount),
                    "the last card's objectives match the table");
        }
    }
}
