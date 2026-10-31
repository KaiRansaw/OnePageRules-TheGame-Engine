using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Quest;
using FDG.SaveLoad;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #418 - AoF: Quest Horde, played start to finish by bots.
    ///
    /// <para>
    /// The two Quest systems share every mode and every state machine; only the data and the vocabulary
    /// differ. That is exactly why this needed its own played game: everything that makes an AoF: Quest
    /// game an AoF: Quest game travels on one field - <c>Settings.GameSystem</c> - and a mode that only
    /// ever ran as Star Quest in the suite could have been threading that field nowhere at all, with all
    /// 4,000 tests green. Forcing the challenges on means the party rolls random events, hits the
    /// weather and time-of-day tables and reveals treasure, so the per-system name lookups
    /// (<see cref="QuestEventNames"/>, <see cref="QuestItemSkin"/>) are reached by a real game rather
    /// than by a fixture calling them directly.
    /// </para>
    /// <para>
    /// Mirrors <see cref="StarQuestHordeIntegrationTests"/> deliberately, down to the seeds: when one of
    /// these two fails and the other passes, the difference is the system, which is the whole point.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AoFQuestHordeIntegrationTests
    {
        private static QuestHero Hero(string name, int tough) => new QuestHero
        {
            Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 60, Class = "Warrior",
            Quality = 4, Defense = 4, Tough = tough, Power = 4, Strength = 4,
            GameType = "AoF Quest",
            Weapons = new()
            {
                new QuestWeapon { Name = "Master Bow", Range = 24, Attacks = 3, Rules = new() { "AP(1)" } },
                new QuestWeapon { Name = "CCW", Range = 0, Attacks = 2 },
            },
        };

        private static ArmyListFile EnemyCollection()
        {
            var army = new ArmyListFile { Name = "Enemies" };
            for (int i = 0; i < 6; i++)
                army.Units.Add(new UnitFileEntry
                {
                    Name = $"Marauders {i + 1}", ModelCount = 3, Quality = 5, Defense = 5, PointCost = 40,
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Bow", RangeInches = 24, Attacks = 1, Quantity = 3 },
                        new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1, Quantity = 3 },
                    },
                    SpecialRules = i == 5 ? new() { new SpecialRuleEntry_Core("Ambush") } : new(),
                });
            return army;
        }

        [TestCase(21)]
        [TestCase(22)]
        [TestCase(23)]
        public async Task WholeMission_WithAllChallenges_PlaysToTheEnd(int seed)
        {
            FDG.Stages.QuestChallenges.ForceForTests = new()
            {
                FDG.Stages.QuestChallenges.BossFights, FDG.Stages.QuestChallenges.HeroInjuries,
                FDG.Stages.QuestChallenges.IncreasedAlertness, FDG.Stages.QuestChallenges.TimeOfDayName,
                FDG.Stages.QuestChallenges.WeatherName, FDG.Stages.QuestChallenges.RandomEvents,
            };
            try { await PlayHorde(seed); }
            finally { FDG.Stages.QuestChallenges.ForceForTests = null; }
        }

        private async Task PlayHorde(int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            ArmyListFile party = QuestArmy.Build(new[] { Hero("Brakka", 9), Hero("Ysolde", 9) }, _ => null);
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    i == 0 ? party : EnemyCollection(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(EAiProfile.StarQuest,
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.AoFQuest;
            settings.GameMode = EGameMode.StarQuestHorde;
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = seed;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(240)));
            Assert.That(finished, Is.SameAs(completed.Task), "the mission must play to completion without hanging.");

            GameResult result = await completed.Task;
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(store);
            TestContext.Progress.WriteLine($"Result: {result.ToSummaryLine()}");
            TestContext.Progress.WriteLine($"Wave {quest.Wave}, finished {quest.WavesFinished}, " +
                $"alertness {quest.Alertness}, party XP {quest.PartyXp}");

            // The party's own state is the one place a player-facing name is KEPT rather than printed, so
            // it is the one thing a test can read back after the fact: a hero holding a "Med Kit" at the
            // end of an AoF: Quest game means the skin was bypassed between the treasure table and the
            // sheet. Honest caveat - the bots never walk onto a search token, so the pack is normally
            // empty and this is a backstop against a future AI that does collect, not live proof. What
            // the names are is pinned directly by QuestItemSkinTests and QuestEventNameTests; what this
            // game proves is that the AoF: Quest system reaches those lookups at all.
            var heroes = new List<QuestHero>();
            foreach (UnitData u in store.GetAllValues<UnitData>().Where(u => u.IsQuestHero))
            {
                QuestHero h = QuestHero.FromJson(u.QuestHeroJson)!;
                heroes.Add(h);
                TestContext.Progress.WriteLine($"  {h.Name}: level {h.Level}, XP {h.Xp}, Tou {h.Tough}, " +
                    $"items [{string.Join(", ", h.Items)}], alive={u.GetIsAlive()}");
            }

            Assert.That(heroes, Has.Count.EqualTo(2), "both heroes survive as data even if they died.");
            Assert.That(heroes.All(h => h.GameType == "AoF Quest"),
                "the party stays stamped as AoF: Quest for the whole game.");

            string[] starQuestOnly = { "Med Kit", "Injector", "Grenade", "Credits", "Curing Kit", "Teleport Kit" };
            foreach (QuestHero hero in heroes)
                foreach (string item in hero.Items)
                    Assert.That(starQuestOnly.Any(s => item.Contains(s, StringComparison.OrdinalIgnoreCase)),
                        Is.False, $"{hero.Name} is holding a Star Quest item in an AoF: Quest game: {item}");

            Assert.That(quest.Tokens, Is.Not.Empty, "search tokens are placed at the start (the bots never pick them up).");
            Assert.That(quest.Tokens.All(t => t.X > 0 && t.X < 48 && t.Z > 0 && t.Z < 48), "tokens sit on the table.");
            Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(48f),
                "AoF: Quest plays on the same 4' x 4' table as Star Quest.");
            Assert.That(result.Message, Does.Contain("high score"));
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }
    }
}
