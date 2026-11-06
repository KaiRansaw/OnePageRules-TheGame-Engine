using FDG.Campaign;
using FDG.Missions;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #465 - campaign mission plans (Kai, 2026-11-05): each mission planned when you reach it, generated
    /// from the books or picked by hand, saved as a campaign that can be played again with fresh armies.
    /// </summary>
    [TestFixture]
    public class CampaignMissionPlanTests
    {
        private static Func<int> Dice(params int[] faces)
        {
            int i = 0;
            return () => faces[i++ % faces.Length];
        }

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void Generate_EveryRollGivesAPlayableMission(EGameSystem system)
        {
            var styles = new HashSet<EDeploymentStyle>();
            var types = new HashSet<EMissionCardGame>();
            var rng = new Random(462);
            for (int i = 0; i < 400; i++)
            {
                CampaignMissionPlan p = CampaignMissionGenerator.Generate(3, system, () => rng.Next(1, 7));
                Assert.That(p.Mission, Is.EqualTo(3));
                Assert.That(p.DeploymentStyle, Is.Not.AnyOf(EDeploymentStyle.RandomStandard, EDeploymentStyle.RandomStrategic, EDeploymentStyle.RandomAsymmetric),
                    "a plan names a real style, not a roll");
                Assert.That(p.Difficulty, Is.AnyOf(EMissionDifficulty.Easy, EMissionDifficulty.Normal, EMissionDifficulty.Hard));
                Assert.That(p.Title.All(c => c < 128), Is.True, p.Title);
                styles.Add(p.DeploymentStyle);
                types.Add(p.MissionCards);
            }
            Assert.That(styles.Count, Is.EqualTo(18), "every Advanced deployment style can come up");
            Assert.That(types.Count, Is.EqualTo(9), "core objectives and all eight Mission Card games");
        }

        [Test]
        public void MissionType_TheHouseRuleOdds()
        {
            Assert.That(CampaignMissionGenerator.RollMissionType(EGameSystem.GrimdarkFuture, Dice(2)), Is.EqualTo(EMissionCardGame.Off));
            Assert.That(CampaignMissionGenerator.RollMissionType(EGameSystem.GrimdarkFuture, Dice(3, 1)), Is.EqualTo(EMissionCardGame.BattleOfWits));
            Assert.That(CampaignMissionGenerator.RollMissionType(EGameSystem.GrimdarkFuture, Dice(4, 2)), Is.EqualTo(EMissionCardGame.TotalDomination));
            Assert.That(CampaignMissionGenerator.RollMissionType(EGameSystem.GrimdarkFuture, Dice(6, 6)), Is.EqualTo(EMissionCardGame.TacticalParagons));
            Assert.That(CampaignMissionGenerator.RollMissionType(EGameSystem.GFStarQuest, Dice(6, 6)), Is.EqualTo(EMissionCardGame.Off),
                "no Mission Cards outside the four battle games");
        }

        [Test]
        public void ASavedCampaign_RoundTrips_AndCarriesItsNotes()
        {
            var info = new StrikeTeamInfo { Name = "Iron Reavers", GameSystem = EGameSystem.AoFSkirmish, Journal = "A feud." };
            CampaignPlans.Set(info, new CampaignMissionPlan { Mission = 1, Title = "The Ford", DeploymentStyle = EDeploymentStyle.Spearhead,
                MissionCards = EMissionCardGame.PlannedGambit, Difficulty = EMissionDifficulty.Hard, MapName = "River" });
            CampaignPlans.Set(info, new CampaignMissionPlan { Mission = 2, Title = "The Hill" });
            CampaignJournal.SetNote(info, 1, "They hold the crossing.");

            CampaignScript script = CampaignScript.FromJson(CampaignScript.From(info, "Feud").ToJson())!;
            Assert.That(script.Missions.Count, Is.EqualTo(2));
            Assert.That(script.Missions[0].Notes, Is.EqualTo("They hold the crossing."));
            Assert.That(script.Missions[0].MissionCards, Is.EqualTo(EMissionCardGame.PlannedGambit));
            Assert.That(script.Missions[0].Difficulty, Is.EqualTo(EMissionDifficulty.Hard));

            var fresh = new StrikeTeamInfo { Name = "New", GameSystem = EGameSystem.AoFSkirmish };
            script.ApplyTo(fresh);
            Assert.That(CampaignPlans.Next(fresh)!.Title, Is.EqualTo("The Ford"), "mission 1 is next");
            Assert.That(CampaignJournal.NoteFor(fresh, 1), Is.EqualTo("They hold the crossing."));
            Assert.That(fresh.Journal, Is.EqualTo("A feud."));
            Assert.That(fresh.Plans.All(p => p.Notes.Length == 0), Is.True, "a live campaign keeps notes in its journal");
        }

        [Test]
        public void Next_IsThePlanForTheMissionAfterTheLastPlayed()
        {
            var info = new StrikeTeamInfo { Matches = 1 };
            Assert.That(CampaignPlans.Next(info), Is.Null);
            CampaignPlans.Set(info, new CampaignMissionPlan { Mission = 2, Title = "a" });
            CampaignPlans.Set(info, new CampaignMissionPlan { Mission = 2, Title = "b" });
            Assert.That(info.Plans.Count, Is.EqualTo(1), "replaced, not added");
            Assert.That(CampaignPlans.Next(info)!.Title, Is.EqualTo("b"));
        }
    }
}
