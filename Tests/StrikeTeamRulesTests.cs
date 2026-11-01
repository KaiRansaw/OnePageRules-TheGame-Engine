using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// GF: Firefight Campaign through the <see cref="StrikeTeamRules"/> wrapper, which since #410 is
    /// <see cref="CampaignRules"/> with the GFF price list. Starting a team, the after-game rolls, and
    /// the kill counting that feeds them.
    /// </summary>
    [TestFixture]
    public class StrikeTeamRulesTests
    {
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        /// <summary>A 350pt starting army: inside GFF's 400pt treasury and over its 320pt minimum spend.</summary>
        private static ArmyListFile Team(int points = 350)
        {
            var army = new BuiltArmyFile { Name = "Raiders", Faction = "Test", GameSystem = GameSystems.GrimdarkFuture };
            army.Units.Add(new UnitFileEntry
            {
                Name = "Captain", ModelCount = 1, Quality = 4, Defense = 4, PointCost = 50,
                SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero"), new SpecialRuleEntry_CoreNumeric("Tough", 3) },
                Weapons = new List<WeaponFileEntry> { new() { Name = "Blade", Attacks = 2 }, new() { Name = "Pistol", RangeInches = 12, Attacks = 1 } },
            });
            army.Units.Add(new UnitFileEntry { Name = "Squad", ModelCount = 5, Quality = 5, Defense = 5, PointCost = points - 100 });
            army.Units.Add(new UnitFileEntry { Name = "Squad", ModelCount = 5, Quality = 5, Defense = 5, PointCost = 50 });
            return army;
        }

        private static (UnitFileEntry Unit, StrikeUnit Sheet) U(ArmyListFile t, string name)
        {
            UnitFileEntry u = t.Units.First(x => x.Name == name);
            return (u, StrikeTeamRules.Sheet(u)!);
        }

        [Test]
        public void Start_NamesUnitsUniquely_KeepsTheRestOfTheTreasury()
        {
            ArmyListFile team = Team(350);
            Assert.That(StrikeTeamRules.CannotStart(team), Is.Null);
            StrikeTeamRules.Start(team, "Red Team", "red.fdgarmy");
            Assert.That(team.Units.Select(u => u.Name), Is.EqualTo(new[] { "Captain", "Squad", "Squad 2" }));
            Assert.That(team.StrikeTeam!.BankedPoints, Is.EqualTo(50), "400pt treasury - 350pt army, no cap");
            Assert.That(team.StrikeTeam.GameSystem, Is.EqualTo(EGameSystem.GFFirefight));
            Assert.That(team.StrikeTeam.CampaignType, Is.EqualTo(ECampaignType.Endless));
            Assert.That(U(team, "Captain").Sheet.IsHero, Is.True);
            Assert.That(U(team, "Squad").Sheet.TeamFile, Is.EqualTo("red.fdgarmy"));
        }

        [Test]
        public void CannotStart_OverTreasury_UnderMinimumSpend_OrTwoHeroes()
        {
            Assert.That(StrikeTeamRules.CannotStart(Team(450)), Does.Contain("400"));
            Assert.That(StrikeTeamRules.CannotStart(Team(200)), Does.Contain("320"));
            ArmyListFile two = Team();
            two.Units[1].SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            Assert.That(StrikeTeamRules.CannotStart(two), Does.Contain("one Hero"));
        }

        [Test]
        public void AfterGame_Rewards_XpForKills_RegularDiesOn1()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            string squad = U(team, "Squad").Sheet.Id, squad2 = U(team, "Squad 2").Sheet.Id;
            var log = StrikeTeamRules.AfterGame(team, new[]
            {
                new StrikeUnitResult(squad, Casualty: false, Kills: 1, HeroKills: 1),
                new StrikeUnitResult(squad2, Casualty: true, Kills: 0, HeroKills: 0),
            }, won: true, Dice(1));
            Assert.That(U(team, "Squad").Sheet.Xp, Is.EqualTo(4), "+1 not a casualty, +1 unit, +2 Hero");
            Assert.That(team.Units.Any(u => u.Name == "Squad 2"), Is.False, "casualty rolled 1: dead");
            Assert.That(team.StrikeTeam!.Vp, Is.EqualTo(2));
            Assert.That(team.StrikeTeam.BankedPoints, Is.EqualTo(50 + 80), "GFF win reward is 80pts");
            Assert.That(log, Has.Some.Contains("dead"));
        }

        /// <summary>
        /// AfterGame writes this match's history entry from the log <b>as it stands when AfterGame
        /// returns</b>. Pinned because it is a sharp edge that already caught me once: #430 scores the
        /// special objective after AfterGame and appended its verdict to the returned list, which is a
        /// copy as far as the history is concerned - so the line the player reads on the campaign screen
        /// did not contain it. Anything a caller adds afterwards needs its own history entry.
        /// </summary>
        [Test]
        public void AfterGame_WritesItsHistoryEntryFromTheLogItHasAtThatMoment()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            string squad = U(team, "Squad").Sheet.Id;

            List<string> log = StrikeTeamRules.AfterGame(team,
                new[] { new StrikeUnitResult(squad, false, 0, 0) }, won: true, Dice(3));
            int entriesAfterTheMatch = team.StrikeTeam!.History.Count;
            log.Add("something a caller worked out afterwards");

            Assert.That(entriesAfterTheMatch, Is.EqualTo(1), "one entry per match.");
            Assert.That(team.StrikeTeam.History[0], Does.Not.Contain("afterwards"),
                "a line added to the returned log after the fact does NOT reach the history - a caller "
                + "that wants one there must add it.");
            Assert.That(team.StrikeTeam.History.Count, Is.EqualTo(entriesAfterTheMatch));
        }

        [Test]
        public void LevelUp_RegularGetsTraitAndCost()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            var (unit, sheet) = U(team, "Squad");
            sheet.Xp = 4;
            StrikeTeamRules.Save(unit, sheet);
            int cost = unit.PointCost;
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(sheet.Id, false, 0, 0) }, won: false, Dice(1));
            (unit, sheet) = U(team, "Squad");
            Assert.That(sheet.Xp, Is.EqualTo(5));
            Assert.That(sheet.Traits, Does.Contain(HordeExperience.Agile));
            Assert.That(unit.SpecialRules, Does.Contain(new SpecialRuleEntry_Core(HordeExperience.Agile)));
            Assert.That(unit.PointCost, Is.EqualTo(cost + 15), "GFF regular level is +15pts");
            Assert.That(team.StrikeTeam!.BankedPoints, Is.EqualTo(50 + 160), "GFF loss reward is 160pts");
        }

        [Test]
        public void HeroLevelUp_PicksSkillSet_ThenRollsSkill()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            var (hero, sheet) = U(team, "Captain");
            int cost = hero.PointCost;
            // Trait roll 2 (Headstrong), then skill-set roll 3 -> the Warrior or Hunter pair.
            StrikeTeamRules.AddXp(hero, sheet, 5, Dice(2, 3));
            StrikeTeamRules.Save(hero, sheet);
            Assert.That(hero.PointCost, Is.EqualTo(cost + 25), "GFF hero level is +25pts");
            Assert.That(sheet.Pending, Is.EqualTo(new[] { "SkillSet:Warrior|Hunter" }));

            // Pick Warrior; skill roll 3 lands on the second entry, Fanatic.
            string line = StrikeTeamRules.Choose(hero, "SkillSet:Warrior|Hunter", "Warrior", Dice(3));
            (hero, sheet) = U(team, "Captain");
            Assert.That(line, Does.Contain("Fanatic"));
            Assert.That(sheet.Skills, Is.EqualTo(new[] { "Fanatic" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Skill: Fanatic")));
            Assert.That(sheet.Pending, Is.Empty);
        }

        [Test]
        public void HeroCasualty_InjuryOn1_TalentOn6()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            string id = U(team, "Captain").Sheet.Id;
            int cost = U(team, "Captain").Unit.PointCost;
            // Casualty roll 1, then injury roll 2 = Weakened.
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(1, 2));
            var (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Injuries, Is.EqualTo(new[] { "Weakened" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Injury: Weakened")));
            Assert.That(hero.PointCost, Is.EqualTo(cost - 15), "GFF injury is -15pts");
            // Casualty roll 6, then talent roll 5 = Toughened: Tough(3) -> Tough(6).
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(6, 5));
            (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Talents, Is.EqualTo(new[] { "Toughened" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Tough", 6)));
            Assert.That(hero.PointCost, Is.EqualTo(cost), "-15 then +15");
        }

        [Test]
        public void LuckyBreak_AndLearnedLesson_ArePendingChoices()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            string id = U(team, "Captain").Sheet.Id;
            // Casualty roll 1, injury roll 6 = Lucky Break: pick any other injury.
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(1, 6));
            var (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Pending, Is.EqualTo(new[] { "LuckyBreak" }));
            Assert.That(StrikeTeamRules.Options(sheet, "LuckyBreak"),
                Is.EquivalentTo(new[] { "Weakened", "Maimed", "Blinded", "Crippled" }));
            StrikeTeamRules.Choose(hero, "LuckyBreak", "Maimed", Dice(1));
            (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Injuries, Is.EqualTo(new[] { "Maimed" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Injury: Maimed")));

            // Casualty roll 6, talent roll 6 = Learned Lesson: pick any other talent.
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(6, 6));
            (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Pending, Is.EqualTo(new[] { "LearnedLesson" }));
            StrikeTeamRules.Choose(hero, "LearnedLesson", "Vigilant", Dice(1));
            (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Talents, Is.EqualTo(new[] { "Vigilant" }));
        }

        [Test]
        public void SpecialistAndFastLearner_ArePendingChoices()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            var (unit, sheet) = U(team, "Squad");
            StrikeTeamRules.AddXp(unit, sheet, 10, Dice(3, 6));
            StrikeTeamRules.Save(unit, sheet);
            Assert.That(sheet.Pending, Is.EqualTo(new[] { "Specialist", "FastLearner" }));
            StrikeTeamRules.Choose(unit, "Specialist", HordeExperience.ShootingSpecialist, Dice(1));
            StrikeTeamRules.Choose(unit, "FastLearner", HordeExperience.Elite, Dice(1));
            (unit, sheet) = U(team, "Squad");
            Assert.That(sheet.Traits, Is.EquivalentTo(new[] { HordeExperience.ShootingSpecialist, HordeExperience.Elite }));
            Assert.That(sheet.Pending, Is.Empty);
        }

        [Test]
        public void EveryCampaignRuleName_IsInTheCatalog()
        {
            var names = CoreRuleCatalog.All.Select(r => r.Name).ToHashSet();
            // "Dead" removes the unit and "Lucky Break" / "Learned Lesson" are re-rolls, so none of the
            // three is ever written onto a unit as a rule.
            foreach (string i in CampaignTables.PickableInjuries) Assert.That(names, Does.Contain("Injury: " + i));
            foreach (string t in CampaignTables.PickableTalents) Assert.That(names, Does.Contain("Talent: " + t));
            foreach (string s in CampaignTables.SkillSets.Values.SelectMany(v => v)) Assert.That(names, Does.Contain("Skill: " + s));
        }

        [Test]
        public void OnUnitDestroyed_CountsKillsOnTheKillersSheet()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var killer = new UnitData(new PlayerID(Guid.NewGuid()), "Captain", 4, 4, new List<DataBinding<ModelData>>())
            {
                StrikeUnitJson = new StrikeUnit { IsHero = true }.ToJson(),
            };
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Enemy", 4, 5, new List<DataBinding<ModelData>>());
            FirefightCampaign.OnUnitDestroyed(dead, killer);
            Assert.That(StrikeUnit.FromJson(killer.StrikeUnitJson)!.Kills, Is.EqualTo(1));
        }

        /// <summary>
        /// #430 Annihilator: the game's FIRST kill is stamped on the unit that made it. "First" is the
        /// whole question - a finished table records that units died, never in what order - so this has
        /// to be decided as it happens, and it must not move afterwards.
        /// </summary>
        [Test]
        public void OnUnitDestroyed_StampsTheFirstKillOfTheGame_AndOnlyTheFirst()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            UnitData first = CampaignUnit(store, "First Blood");
            UnitData second = CampaignUnit(store, "Late To It");
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Enemy", 4, 5, new List<DataBinding<ModelData>>());

            FirefightCampaign.OnUnitDestroyed(dead, first, store);
            for (int i = 0; i < 3; i++) FirefightCampaign.OnUnitDestroyed(dead, second, store);

            Assert.That(StrikeUnit.FromJson(first.StrikeUnitJson)!.DrewFirstBlood, Is.True,
                "the unit that killed first holds the stamp.");
            Assert.That(StrikeUnit.FromJson(second.StrikeUnitJson)!.DrewFirstBlood, Is.False,
                "and three later kills must not take it away - this is what fails if the stamp is "
                + "written on every kill instead of only the first.");
            Assert.That(StrikeUnit.FromJson(second.StrikeUnitJson)!.Kills, Is.EqualTo(3),
                "while its kills are still counted normally.");
        }

        /// <summary>Without the store there is nothing to check against, so nothing is stamped.</summary>
        [Test]
        public void OnUnitDestroyed_WithNoStore_CountsTheKillButStampsNothing()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            UnitData killer = CampaignUnit(store, "Captain");
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Enemy", 4, 5, new List<DataBinding<ModelData>>());

            FirefightCampaign.OnUnitDestroyed(dead, killer);

            Assert.That(StrikeUnit.FromJson(killer.StrikeUnitJson)!.Kills, Is.EqualTo(1));
            Assert.That(StrikeUnit.FromJson(killer.StrikeUnitJson)!.DrewFirstBlood, Is.False);
        }

        /// <summary>A campaign unit in the store, so the first-blood scan can see it.</summary>
        private static UnitData CampaignUnit(GameDataStore store, string name)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, 4, 4,
                new List<DataBinding<ModelData>>())
            {
                StrikeUnitJson = new StrikeUnit { TeamFile = "army.fdgarmy" }.ToJson(),
            };
            store.Create(unit);
            return unit;
        }
    }
}
