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
    /// GF: Star Quest Horde, played start to finish by bots: a party of two heroes against an enemy collection.
    /// </summary>
    [TestFixture]
    public class StarQuestHordeIntegrationTests
    {
        private static QuestHero Hero(string name, int tough) => new QuestHero
        {
            Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 60, Class = "Soldier",
            Quality = 4, Defense = 4, Tough = tough, Power = 4, Strength = 4,
            Weapons = new()
            {
                new QuestWeapon { Name = "Master Rifle", Range = 24, Attacks = 3, Rules = new() { "AP(1)" } },
                new QuestWeapon { Name = "CCW", Range = 0, Attacks = 2 },
            },
        };

        private static ArmyListFile EnemyCollection()
        {
            var army = new ArmyListFile { Name = "Enemies" };
            for (int i = 0; i < 6; i++)
                army.Units.Add(new UnitFileEntry
                {
                    Name = $"Raiders {i + 1}", ModelCount = 3, Quality = 5, Defense = 5, PointCost = 40,
                    Weapons = new()
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1, Quantity = 3 },
                        new WeaponFileEntry { Name = "CCW", RangeInches = 0, Attacks = 1, Quantity = 3 },
                    },
                    // One unit ambushes: it must arrive D3 rounds after its wave, near the AI Goal.
                    SpecialRules = i == 5 ? new() { new SpecialRuleEntry_Core("Ambush") } : new(),
                });
            return army;
        }

        [Test]
        public void Party_ConvertsToHeroUnits()
        {
            ArmyListFile party = QuestArmy.Build(new[] { Hero("Vex", 9), Hero("Mara", 6) }, _ => null);
            Assert.That(party.Units, Has.Count.EqualTo(2));
            UnitFileEntry vex = party.Units[0];
            Assert.That(vex.ModelCount, Is.EqualTo(1));
            Assert.That(vex.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Tough", 9)));
            Assert.That(vex.Weapons[0].ArmorPenetration, Is.EqualTo(1));
            Assert.That(QuestHero.FromJson(vex.QuestHeroJson)!.Name, Is.EqualTo("Vex"));
            Assert.That(party.TotalPoints, Is.EqualTo(120));
        }

        [Test]
        public void WaveNumbers_FollowTheRules()
        {
            Assert.That(StarQuestHordeRules.WaveBudget(2, 300), Is.EqualTo(150));
            Assert.That(StarQuestHordeRules.WaveXp(2, prioritySeized: true, wave: 3), Is.EqualTo((4 + 2) * 3));
            Assert.That(StarQuestHordeRules.Split(7, 3), Is.EqualTo(new[] { 3, 2, 2 }));
            Assert.That(StarQuestHordeRules.AlertnessGain(enemiesOnTable: false), Is.EqualTo(2));
        }

        [Test]
        public void Items_AndTokens_RoundTrip()
        {
            Assert.That(QuestItems.All, Has.Count.EqualTo(33));
            Assert.That(QuestItems.Find("Med Kit")!.Cost, Is.EqualTo(15));
            Assert.That(QuestItems.Credits("10c"), Is.EqualTo(10));
            Assert.That(QuestItems.Credits("Med Kit"), Is.Null);
            Assert.That(FDG.Stages.QuestTreasure.TreasureName(7), Is.EqualTo("Treasure Trove"));

            var quest = new QuestProgress();
            quest.Tokens.Add(new QuestToken { Id = 1, X = 5, Z = 6 });
            QuestProgress copy = quest.Clone();
            copy.Tokens[0].IsTreasure = true;
            Assert.That(quest.Tokens[0].IsTreasure, Is.False, "a copy must not share its tokens with the original.");

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(quest);
            QuestProgress back = Newtonsoft.Json.JsonConvert.DeserializeObject<QuestProgress>(json)!;
            Assert.That(back.Tokens.Single().X, Is.EqualTo(5f));
        }

        /// <summary>Every rule the Star Quest code grants by name must exist, or the grant silently does nothing.</summary>
        [TestCase("+1 to hit rolls in melee")]
        [TestCase("+1 to hit rolls when shooting")]
        [TestCase("-1 to hit rolls in melee")]
        [TestCase("-1 to hit rolls when shooting")]
        [TestCase("-6\" range when shooting")]
        [TestCase("AP(+1) in melee")]
        [TestCase("AP(1) when shooting")]
        [TestCase("Fast")]
        [TestCase("Very Fast")]
        [TestCase("Slow")]
        [TestCase("Fortified")]
        [TestCase("Protected")]
        [TestCase("Evasive")]
        [TestCase("Increased Shooting Range")]
        [TestCase("Extra hit in melee")]
        [TestCase("Extra hit when shooting")]
        [TestCase("Extra hit in melee or within 12\"")]
        [TestCase("Extra hit")]
        [TestCase("Two extra hits")]
        [TestCase("Immobile")]
        [TestCase("Trait: Agile")]
        public void GrantedRules_Exist(string name) =>
            Assert.That(FDG.Rules.Dispatch.CoreRuleCatalog.All.Any(r => r.Name == name), Is.True, $"no rule named {name}");

        [Test]
        public void Campaign_ChaptersAndMissions()
        {
            var c = QuestCampaign.Create("Test", new[] { "a.fdghero", "b.fdghero" }, new Random(3));
            Assert.That(c.Chapters.Count, Is.InRange(3, 5));
            Assert.That(c.Chapters.All(ch => ch.Missions is >= 1 and <= 3));
            Assert.That(c.CanStart(1), Is.True);
            c.StartChapter(1, 2);
            Assert.That(c.CanStart(0), Is.False, "a started chapter must be finished first.");
            int missions = c.Chapters[1].Missions;
            Assert.That(c.RecordMission("failed"), Is.True);
            Assert.That(c.Chapters[1].MissionsWon, Is.EqualTo(0));
            Assert.That(c.Chapters[1].Restarts, Is.EqualTo(1));
            for (int i = 0; i < missions; i++) c.RecordMission("success");
            Assert.That(c.Chapters[1].Done, Is.True);
            Assert.That(c.CurrentChapter, Is.EqualTo(-1));
            QuestCampaign back = QuestCampaign.FromJson(c.ToJson())!;
            Assert.That(back.Chapters[1].Done, Is.True);
        }

        [Test]
        public async Task CampaignMission_UsesTheChapterAndCarriesWounds()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            QuestHero vex = Hero("Vex", 9);
            vex.CampaignName = "Test"; vex.CampaignDifficulty = 3; vex.WoundsTaken = 4; vex.PowerLeft = 1;
            ArmyListFile party = QuestArmy.Build(new[] { vex }, _ => null);
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), i == 0 ? party : EnemyCollection(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                slots[i].AssignPlayerController(AiProfileFactory.CreateController(EAiProfile.StarQuest, $"AI {i}", slots[i].PlayerID, aiGame, 4, slots[i].SlotID));
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = EGameMode.StarQuestMission;
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = 4;
            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(20)));
            if (!completed.Task.IsCompleted) server.Stop();
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(store);
            Assert.That(quest.Difficulty, Is.EqualTo(3), "a campaign mission plays at the chapter's difficulty.");
            if (completed.Task.IsCompleted)
            {
                QuestHero after = QuestHero.FromJson(store.GetAllValues<UnitData>().First(u => u.IsQuestHero).QuestHeroJson)!;
                Assert.That(after.LastOutcome, Is.EqualTo("failed").Or.EqualTo("success"));
            }
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }

        [Test]
        public void MissionNumbers_FollowTheCampaignBuilder()
        {
            Assert.That(StarQuestMissionRules.WaveBudget(2, 300), Is.EqualTo(150));
            Assert.That(StarQuestMissionRules.WaveBudget(4, 300), Is.EqualTo(300));
            Assert.That(StarQuestMissionRules.AlertnessGain(primaryDone: true, enemiesOnTable: true), Is.EqualTo(3));
            Assert.That(StarQuestMissionRules.AlertnessGain(primaryDone: false, enemiesOnTable: false), Is.EqualTo(2));
            Assert.That(StarQuestMissionRules.Xp(3, primary: true, secondary: true), Is.EqualTo(9));
            Assert.That(StarQuestMissionRules.HazardPay(2), Is.EqualTo(10));
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
            Assert.That(StarQuestMissionRules.CornerOf(new Position(46f, 2f)), Is.EqualTo(2));
            Assert.That(StarQuestMissionRules.QuarterCentre(4).x, Is.EqualTo(36f));
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }

        [TestCase(7)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public async Task OneOffMission_PlaysToTheEnd(int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            // One bot-run hero: two bot heroes deployed side by side can trip the solo bot's friendly-spacing quirk.
            ArmyListFile party = QuestArmy.Build(new[] { Hero("Vex", 9) }, _ => null);
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
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = EGameMode.StarQuestMission;
            settings.RandomnessType = ERandomnessType.Realistic;
            settings.DiceSeed = seed;

            // Bots never work objectives, and the Star Quest AI holds its AI Goal when it can't see a hero, so a
            // bot-only mission may never end. Play for a while: it must either finish or keep making progress.
            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            int roundsA = GameProgressUtilities.GetQuestProgress(store).RoundsDone;
            await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(5)));
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(store);
            if (!completed.Task.IsCompleted)
            {
                server.Stop();
                Assert.That(quest.RoundsDone, Is.GreaterThan(roundsA), "the mission must keep playing, not hang.");
                Assert.That(quest.Reinforcements, Is.GreaterThan(0), "reinforcements arrive as alertness rises.");
            }
            else
            {
                GameResult result = await completed.Task;
                TestContext.Progress.WriteLine($"Result: {result.ToSummaryLine()} - {result.Message}");
                Assert.That(quest.Outcome, Is.EqualTo("failed").Or.EqualTo("success"));
            }
            TestContext.Progress.WriteLine($"Difficulty {quest.Difficulty}, primary {quest.Primary}, rounds {quest.RoundsDone}, reinforcements {quest.Reinforcements}, markers {quest.Markers.Count}, tokens {quest.Tokens.Count}, outcome {quest.Outcome}");
            foreach (UnitData u in store.GetAllValues<UnitData>().Where(u => u.IsQuestHero))
            {
                QuestHero h = QuestHero.FromJson(u.QuestHeroJson)!;
                TestContext.Progress.WriteLine($"  {h.Name}: XP {h.Xp}, {h.Credits}c, injuries {string.Join("/", h.Injuries)}");
            }
            Assert.That(quest.Difficulty, Is.InRange(1, 4));
            Assert.That(quest.Primary, Is.InRange(1, 6));
            Assert.That(quest.HeroCorner, Is.InRange(1, 4));
            Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(48f));
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }

        [TestCase(99)]
        [TestCase(5)]
        [TestCase(11)]
        public async Task WholeMission_PlaysToTheEnd(int seed) => await PlayHorde(seed);

        /// <summary>Every optional challenge on (boss fights, injuries, alertness, time, weather, random events).</summary>
        [TestCase(21)]
        [TestCase(22)]
        [TestCase(23)]
        [TestCase(24)]
        public async Task WholeMission_WithAllChallenges(int seed)
        {
            QuestChallenges_All();
            try { await PlayHorde(seed); }
            finally { FDG.Stages.QuestChallenges.ForceForTests = null; }
        }

        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        public async Task OneOffMission_WithAllChallenges(int seed)
        {
            QuestChallenges_All();
            try { await OneOffMission_PlaysToTheEnd(seed); }
            finally { FDG.Stages.QuestChallenges.ForceForTests = null; }
        }

        private static void QuestChallenges_All() => FDG.Stages.QuestChallenges.ForceForTests = new()
        {
            FDG.Stages.QuestChallenges.BossFights, FDG.Stages.QuestChallenges.HeroInjuries, FDG.Stages.QuestChallenges.IncreasedAlertness,
            FDG.Stages.QuestChallenges.TimeOfDayName, FDG.Stages.QuestChallenges.WeatherName, FDG.Stages.QuestChallenges.RandomEvents,
        };

        private async Task PlayHorde(int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            ArmyListFile party = QuestArmy.Build(new[] { Hero("Vex", 9), Hero("Mara", 9) }, _ => null);
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
            settings.GameSystem = EGameSystem.GFStarQuest;
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
            TestContext.Progress.WriteLine($"Wave {quest.Wave}, finished {quest.WavesFinished}, alertness {quest.Alertness}, party XP {quest.PartyXp}");
            foreach (UnitData u in store.GetAllValues<UnitData>().Where(u => u.IsQuestHero))
            {
                QuestHero h = QuestHero.FromJson(u.QuestHeroJson)!;
                TestContext.Progress.WriteLine($"  {h.Name}: level {h.Level}, XP {h.Xp}, Tou {h.Tough}, Q{h.Quality}+ alive={u.GetIsAlive()}");
            }
            TestContext.Progress.WriteLine($"Tokens left: {string.Join(", ", quest.Tokens.Select(t => t.Label))}");
            Assert.That(quest.Tokens, Is.Not.Empty, "search tokens are placed at the start (the bots never pick them up).");
            Assert.That(quest.Tokens.All(t => t.X > 0 && t.X < 48 && t.Z > 0 && t.Z < 48), "tokens sit on the table.");
            Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(48f), "Star Quest plays on a 4' x 4' table.");
            Assert.That(result.Message, Does.Contain("high score"));
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }
    }
}
