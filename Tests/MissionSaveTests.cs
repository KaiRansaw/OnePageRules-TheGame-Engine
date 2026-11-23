using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Missions;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Extra Missions, Attack &amp; Defend and Side-Missions played to the end by two bots, then written to a save and read
    /// back: the mission's state (roles, carried markers, reserves, side-missions, VP) must come back unchanged. Overnight
    /// queue (Kai, 2026-11-22): "save/load check for every mission".
    /// </summary>
    [TestFixture, NonParallelizable]
    public class MissionSaveTests
    {
        [TearDown]
        public void PutTheTableBack() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private static async Task<(GameDataStore Store, GameResult Result)> PlayAGame(int seed, EGameSystem system,
            EExtraMission mission, ESideMissionPlay side, ESideMissionPick pick = ESideMissionPick.Randomized)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController($"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.ExtraMission = mission;
            settings.SideMissions = side;
            settings.SideMissionPick = pick;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(180)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            return (store, completed.Task.Result);
        }

        // System.Text.Json, not the save's own serializer: a field the save drops must show up as a difference.
        private static string Json(object? o) => System.Text.Json.JsonSerializer.Serialize(o);

        [TestCase(EGameSystem.GrimdarkFuture, EExtraMission.SmashAndGrab, ESideMissionPlay.Off, 5101)]
        [TestCase(EGameSystem.AgeOfFantasy, EExtraMission.LastStand, ESideMissionPlay.Off, 5102)]
        [TestCase(EGameSystem.GFFirefight, EExtraMission.TheRescue, ESideMissionPlay.Off, 5103)]
        [TestCase(EGameSystem.AoFSkirmish, EExtraMission.VipEscort, ESideMissionPlay.Off, 5104)]
        [TestCase(EGameSystem.GrimdarkFuture, EExtraMission.RelicHunt, ESideMissionPlay.SharedAndHidden, 5105)]
        [TestCase(EGameSystem.AgeOfFantasy, EExtraMission.Domination, ESideMissionPlay.CumulativeDash, 5106)]
        public async Task AMissionGame_PlaysToTheEnd_AndItsStateSurvivesASave(EGameSystem system, EExtraMission mission,
            ESideMissionPlay side, int seed)
        {
            (GameDataStore store, GameResult result) = await PlayAGame(seed, system, mission, side);
            ExtraMissionState? extra = GameProgressUtilities.ExtraMission(store);
            Assert.That(extra, Is.Not.Null, "the mission's state is recorded");
            Assert.That(extra!.Mission, Is.EqualTo(mission));
            if (ExtraMissionCatalog.IsAttackDefend(mission))
            {
                Assert.That(extra.AttackerTeam, Is.GreaterThanOrEqualTo(0));
                Assert.That(extra.DefenderTeam, Is.Not.EqualTo(extra.AttackerTeam));
                Assert.That(result.RoundsPlayed, Is.EqualTo(AttackDefend.Rounds), "Attack & Defend lasts 6 rounds");
            }
            SideMissionState? sideState = GameProgressUtilities.SideMissions(store);
            if (side != ESideMissionPlay.Off)
            {
                Assert.That(sideState, Is.Not.Null);
                Assert.That(sideState!.Missions, Is.Not.Empty, "side-missions were chosen or rolled");
            }

            GameDataStore loaded = GameSaveSerializer.Load(GameSaveSerializer.Save(store));
            Assert.That(Json(GameProgressUtilities.ExtraMission(loaded)), Is.EqualTo(Json(extra)), "the mission state survives a save");
            Assert.That(Json(GameProgressUtilities.SideMissions(loaded)), Is.EqualTo(Json(sideState)), "the side-mission state survives a save");
        }
    }
}
