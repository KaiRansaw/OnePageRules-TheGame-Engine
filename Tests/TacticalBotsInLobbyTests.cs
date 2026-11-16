using FDG.Ai;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.GameModel;
using FDG.Missions;
using FDG.Players;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Kai, 2026-11-15: the Tactician ("Tactical") and the Strategist ("Expert (slow)") are pickable in the lobby again,
    /// beside the official AI. They were built before the mission cards (#462), Simplified Terrain buildings (#460) and
    /// Fast-Play, so these play them through every battle game with those switched on, against the official AI - the
    /// guard is that each game finishes, with no stage fault.
    /// </summary>
    [TestFixture]
    public class TacticalBotsInLobbyTests
    {
        private static async Task<GameResult> Play(int seed, EGameSystem system, EAiProfile profile,
            EMissionCardGame cards, bool buildings)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                EAiProfile who = i == 0 ? profile : EAiProfile.SoloCoOp;
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(who, $"AI {i}", slots[i].PlayerID, aiGame,
                    seed, slots[i].SlotID, searchBudget: who == EAiProfile.Strategist ? new UctOptions { BaseBudgetMs = 150, MaxBudgetMs = 300 } : null));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.MissionCardGame = cards;
            settings.FastPlaySimplifiedTerrain = buildings;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(300)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            return completed.Task.Result;
        }

        [TestCase(EGameSystem.GrimdarkFuture, EMissionCardGame.BattleOfWits, true, 4700)]
        [TestCase(EGameSystem.GFFirefight, EMissionCardGame.TotalDomination, false, 4701)]
        [TestCase(EGameSystem.AgeOfFantasy, EMissionCardGame.DirectChallenge, true, 4702)]
        [TestCase(EGameSystem.AoFSkirmish, EMissionCardGame.Off, false, 4703)]
        public async Task Tactical_PlaysEveryBattleGame(EGameSystem system, EMissionCardGame cards, bool buildings, int seed)
        {
            GameResult result = await Play(seed, system, EAiProfile.Tactician, cards, buildings);
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.ToString());
        }

        [TestCase(EGameSystem.GrimdarkFuture, EMissionCardGame.BattleOfWits, true, 4710)]
        public async Task Expert_PlaysAGame(EGameSystem system, EMissionCardGame cards, bool buildings, int seed)
        {
            GameResult result = await Play(seed, system, EAiProfile.Strategist, cards, buildings);
            Assert.That(result.Outcome, Is.Not.EqualTo(EGameOutcome.Fault), result.ToString());
        }
    }
}
