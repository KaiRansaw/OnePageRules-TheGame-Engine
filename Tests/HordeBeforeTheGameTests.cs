using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using NUnit.Framework;
using static FDG.Tests.TestArmies;

namespace FDG.Tests
{
    /// <summary>
    /// Bug hunt 2026-11-28 (AoF: Skirmish Horde + Advanced options). The Advanced Rulebooks' "before the game begins"
    /// picks - Spell Books, Living Spells, Relic Wargear / Magic Items - are made by the standard deployment
    /// (<c>DetermineNextDeployPlayerStage</c>), which Horde Mode never runs. So in a Horde game with them ticked no
    /// army got its living spells and no Hero its item: the same armies in a standard game got both. Horde's own
    /// deployment now makes them. Played as a whole AI-vs-AI Horde game (the #424 lesson: a test that calls the new
    /// code itself can't tell whether the game ever reaches it).
    /// </summary>
    [TestFixture]
    public class HordeBeforeTheGameTests
    {
        [Test]
        [CancelAfter(240_000)]
        public async Task AHordeGame_SettlesLivingSpells_AndRelicWargear()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? MakeShooterArmy() : MakeDefenderArmy(), store);
                slots[i].AssignPlayerController(AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, new FDGGame_AsLocal(store, bus), 5113, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = 5113;
            settings.GameMode = EGameMode.Horde;
            settings.AdvancedRules = EAdvancedRules.LivingSpells | EAdvancedRules.RelicWargear;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(200)));
            Assert.That(finished, Is.SameAs(completed.Task), "the Horde game must play to completion.");

            LivingSpellsState? spells = LivingSpells.State(store);
            Assert.That(spells, Is.Not.Null, "Living Spells were never settled in a Horde game");
            Assert.That(spells!.Picks.Keys, Is.EquivalentTo(slots.Select(s => s.PlayerID.ID.ToString())),
                "each army gets its living spells");
            Assert.That(spells.Picks.Values.All(p => p.Count == LivingSpells.Categories.Length), Is.True, "one per category");

            RelicWargearState? relics = RelicWargear.State(store);
            Assert.That(relics?.Method ?? 0, Is.InRange(1, 3), "Relic Wargear's selection method was never settled in a Horde game");
        }
    }
}
