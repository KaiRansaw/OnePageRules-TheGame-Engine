using FDG.Quest;
using NUnit.Framework;

namespace FDG.Tests
{
    // Batch E (2026-11-07) - the Quest campaign's planned structure: Generate everything / Build it
    // myself / Start from a saved campaign, editing what is still unplayed, the journal's records, and
    // old (FileVersion 1) campaign files still loading.
    [TestFixture]
    public class QuestCampaignStructureTests
    {
        private static QuestCampaign Generated(int seed = 3) =>
            QuestCampaign.Create("Test", new[] { "a.fdghero", "b.fdghero" }, new Random(seed));

        [Test]
        public void Generate_RollsTheBookStructureAndEveryObjective()
        {
            for (int seed = 0; seed < 40; seed++)
            {
                QuestCampaign c = Generated(seed);
                Assert.That(c.Chapters.Count, Is.InRange(3, 5), "D3+2 chapters");
                Assert.That(c.Chapters.All(ch => ch.MissionPlan.Count is >= 2 and <= 4), "D3+1 missions per chapter");
                Assert.That(c.Chapters.All(ch => ch.Missions == ch.MissionPlan.Count));
                Assert.That(c.Chapters.SelectMany(ch => ch.MissionPlan).All(m => m.Primary is >= 1 and <= 6 && m.Secondary is >= 1 and <= 6),
                    "Generate everything rolls each mission's objectives on the D6 tables");
            }
        }

        [Test]
        public void VersionOneFile_LoadsWithItsMissionCountsAsUnplannedMissions()
        {
            const string v1 = @"{
  ""FileVersion"": 1, ""Name"": ""Old"", ""GameType"": ""GF Star Quest"",
  ""HeroFiles"": [""a.fdghero"", ""b.fdghero""],
  ""Chapters"": [
    { ""Number"": 1, ""Missions"": 3, ""Difficulty"": 2, ""MissionsWon"": 1, ""Done"": false, ""Restarts"": 0 },
    { ""Number"": 2, ""Missions"": 2, ""Difficulty"": 0, ""MissionsWon"": 0, ""Done"": false, ""Restarts"": 0 }
  ],
  ""CurrentChapter"": 0, ""History"": [""Campaign started: 2 chapters, 5 missions.""]
}";
            QuestCampaign c = QuestCampaign.FromJson(v1)!;
            Assert.That(c, Is.Not.Null);
            Assert.That(c.Chapters[0].MissionPlan.Count, Is.EqualTo(3));
            Assert.That(c.Chapters[1].MissionPlan.Count, Is.EqualTo(2));
            Assert.That(c.Chapters[0].MissionPlan.All(m => m.Primary == 0 && m.Secondary == 0), "old missions roll when they start");
            Assert.That(c.Active, Is.SameAs(c.Chapters[0]));
            Assert.That(c.NextMission, Is.SameAs(c.Chapters[0].MissionPlan[1]), "mission 2 of chapter 1 is next");
            Assert.That(c.Story, Is.EqualTo(""));
            QuestCampaign back = QuestCampaign.FromJson(c.ToJson())!;
            Assert.That(back.FileVersion, Is.EqualTo(QuestCampaign.CurrentFileVersion));
            Assert.That(back.Chapters.Sum(ch => ch.Missions), Is.EqualTo(5));
        }

        [Test]
        public void BuildItMyself_StartsWithOneChapterOfOneMission_AndGrows()
        {
            QuestCampaign c = QuestCampaign.CreateEmpty("Mine", new[] { "a.fdghero", "b.fdghero" });
            Assert.That(c.Chapters.Count, Is.EqualTo(1));
            Assert.That(c.Chapters[0].MissionPlan.Count, Is.EqualTo(1));
            Assert.That(c.Chapters[0].MissionPlan[0].Primary, Is.EqualTo(0), "roll when it starts");
            c.AddChapter();
            c.AddMission(1);
            c.AddMission(1);
            Assert.That(c.Chapters[1].Missions, Is.EqualTo(3));
            Assert.That(c.Chapters[1].Number, Is.EqualTo(2));
            Assert.That(c.RemoveMission(1, 0), Is.True);
            Assert.That(c.RemoveChapter(0), Is.True);
            Assert.That(c.Chapters[0].Number, Is.EqualTo(1), "chapters renumber");
            Assert.That(c.RemoveChapter(0), Is.False, "a campaign keeps at least one chapter");
            Assert.That(c.StructureProblem(), Is.Null);
        }

        [Test]
        public void PlayedMissionsAndStartedChapters_AreLocked()
        {
            QuestCampaign c = Generated();
            c.Chapters[1].PlannedDifficulty = 3;
            c.StartChapter(1, 1);
            Assert.That(c.Chapters[1].Difficulty, Is.EqualTo(3), "a planned difficulty wins over the pick");
            Assert.That(c.HasStarted, Is.True);
            Assert.That(c.Generate(new Random(1)), Is.False, "a started campaign is never re-rolled");
            Assert.That(c.CanEditChapter(1), Is.False, "the chapter being played");
            Assert.That(c.RemoveChapter(1), Is.False);
            c.RecordMission("success");
            Assert.That(c.CanEditMission(1, 0), Is.False, "a won mission");
            Assert.That(c.CanEditMission(1, 1), Is.True, "the next mission is still unplayed");
            Assert.That(c.CanAddMission(1), Is.True);
            int index = c.CurrentChapter;
            c.RemoveChapter(0);
            Assert.That(c.CurrentChapter, Is.EqualTo(index - 1), "removing an earlier chapter keeps the active one");
        }

        [Test]
        public void Plan_KeepsTheStructureAndDropsTheProgress()
        {
            QuestCampaign c = Generated();
            c.Story = "Why we fight.";
            c.Chapters[0].Theme = "The sewers";
            c.Chapters[0].MissionPlan[0].Notes = "Briefing";
            c.StartChapter(0, 2);
            c.RecordMission("success", new CampaignMissionRecord { Outcome = "success" });

            QuestCampaign plan = QuestCampaign.FromJson(c.ToPlan().ToJson())!;
            Assert.That(plan.HeroFiles, Is.Empty);
            Assert.That(plan.History, Is.Empty);
            Assert.That(plan.CurrentChapter, Is.EqualTo(-1));
            Assert.That(plan.HasStarted, Is.False);
            Assert.That(plan.Chapters.SelectMany(ch => ch.MissionPlan).All(m => m.Records.Count == 0));
            Assert.That(plan.Chapters[0].Theme, Is.EqualTo("The sewers"));
            Assert.That(plan.Chapters[0].MissionPlan[0].Notes, Is.EqualTo("Briefing"));
            Assert.That(plan.Story, Is.EqualTo("Why we fight."));

            QuestCampaign fresh = QuestCampaign.CreateEmpty("Replay", new[] { "c.fdghero", "d.fdghero" });
            Assert.That(fresh.ApplyPlan(plan), Is.True);
            Assert.That(fresh.Chapters.Count, Is.EqualTo(c.Chapters.Count));
            Assert.That(fresh.Chapters[0].MissionPlan[0].Primary, Is.EqualTo(c.Chapters[0].MissionPlan[0].Primary));
            fresh.Chapters[0].Theme = "changed";
            Assert.That(plan.Chapters[0].Theme, Is.EqualTo("The sewers"), "applying copies the plan");
        }

        [Test]
        public void RecordMission_FilesTheRecordOnTheMissionPlayed()
        {
            QuestCampaign c = Generated();
            c.StartChapter(0, 1);
            c.RecordMission("failed", new CampaignMissionRecord { Outcome = "failed" });
            c.RecordMission("success", new CampaignMissionRecord { Outcome = "success" });
            CampaignMission first = c.Chapters[0].MissionPlan[0];
            Assert.That(first.Records.Select(r => r.Outcome), Is.EqualTo(new[] { "failed", "success" }),
                "the restart replays mission 1, so it holds both attempts");
            Assert.That(first.Won, Is.True);
            Assert.That(c.Chapters[0].MissionPlan[1].Records, Is.Empty);
        }

        [Test]
        public void Record_IsBuiltFromTheGameAndTheSheets()
        {
            var quest = new QuestProgress
            {
                Difficulty = 2, Primary = 3, PrimaryDone = true, Secondary = 5, SecondaryDone = false, RoundsDone = 6,
                EventLog = new List<string> { "33 - Dazing Spores" },
            };
            var before = new QuestHero { Name = "Vex", Xp = 2, Credits = 10, Items = { "Med Kit" } };
            var after = new QuestHero { Name = "Vex", Xp = 6, Credits = 20, Items = { "Med Kit", "Stun Grenade" }, Injuries = { "Broken Arm" } };
            var otherBefore = new QuestHero { Name = "Kai", Xp = 0 };
            var otherAfter = new QuestHero { Name = "Kai", Xp = 0 };
            CampaignMissionRecord r = CampaignMissionRecord.From(quest,
                new[] { ((QuestHero?)before, after, true), ((QuestHero?)otherBefore, otherAfter, false) },
                "success", new DateTime(2026, 11, 7, 20, 30, 0));
            Assert.That(r.Outcome, Is.EqualTo("success"));
            Assert.That(r.Difficulty, Is.EqualTo(2));
            Assert.That(r.Primary, Is.EqualTo(3));
            Assert.That(r.PrimaryDone, Is.True);
            Assert.That(r.Secondary, Is.EqualTo(5));
            Assert.That(r.SecondaryDone, Is.False);
            Assert.That(r.KnockedOut, Is.EqualTo(new[] { "Vex" }));
            Assert.That(r.Injuries, Is.EqualTo(new[] { "Vex: Broken Arm" }));
            Assert.That(r.Xp, Is.EqualTo(new[] { "Vex +4" }), "no line for a hero that gained nothing");
            Assert.That(r.Money, Is.EqualTo(new[] { "Vex +10" }));
            Assert.That(r.Loot, Is.EqualTo(new[] { "Vex: Stun Grenade" }));
            Assert.That(r.Events, Is.EqualTo(new[] { "33 - Dazing Spores" }));
            Assert.That(r.PlayedAt, Is.EqualTo("2026-11-07 20:30"));
        }

        [Test]
        public void QuestProgress_CloneKeepsTheEventLog()
        {
            var q = new QuestProgress { EventLog = new List<string> { "11 - X" } };
            QuestProgress copy = q.Clone();
            copy.EventLog.Add("12 - Y");
            Assert.That(q.EventLog.Count, Is.EqualTo(1));
        }

        [Test]
        public void AoFShop_SellsUnderItsOwnNamesForGold()
        {
            QuestItemInfo kit = QuestItems.All.First(i => i.Name == "Med Kit");
            var hero = new QuestHero { Name = "Bran", Level = 9, Credits = 0 };
            string? why = QuestShop.WhyCannotBuy(hero, kit, EGameSystem.AoFQuest);
            Assert.That(why, Does.Contain($"{kit.Cost}g").And.Contain("0g"));
            hero.Credits = 1000;
            Assert.That(QuestShop.Buy(hero, kit, EGameSystem.AoFQuest), Is.True);
            Assert.That(hero.Items, Is.EqualTo(new[] { "Health Potion" }));
            Assert.That(QuestShop.Sell(hero, "Health Potion"), Is.True);
            Assert.That(hero.Credits, Is.EqualTo(1000));
        }
    }
}
