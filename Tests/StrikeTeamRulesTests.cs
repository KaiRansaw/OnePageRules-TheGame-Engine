using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>GF: Firefight Campaign: starting a strike team, the after-game rolls, and the in-game skills.</summary>
    [TestFixture]
    public class StrikeTeamRulesTests
    {
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        private static ArmyListFile Team(int points = 150)
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
        public void Start_NamesUnitsUniquely_SavesUpTo40()
        {
            ArmyListFile team = Team(150);
            Assert.That(StrikeTeamRules.CannotStart(team), Is.Null);
            StrikeTeamRules.Start(team, "Red Team", "red.fdgarmy");
            Assert.That(team.Units.Select(u => u.Name), Is.EqualTo(new[] { "Captain", "Squad", "Squad 2" }));
            Assert.That(team.StrikeTeam!.BankedPoints, Is.EqualTo(40), "200 - 150 = 50, max 40 saved");
            Assert.That(U(team, "Captain").Sheet.IsHero, Is.True);
            Assert.That(U(team, "Squad").Sheet.TeamFile, Is.EqualTo("red.fdgarmy"));
        }

        [Test]
        public void CannotStart_OverPointsOrTwoHeroes()
        {
            Assert.That(StrikeTeamRules.CannotStart(Team(250)), Does.Contain("200"));
            ArmyListFile two = Team();
            two.Units[1].SpecialRules.Add(new SpecialRuleEntry_Core("Hero"));
            Assert.That(StrikeTeamRules.CannotStart(two), Does.Contain("one hero"));
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
            Assert.That(U(team, "Squad").Sheet.Xp, Is.EqualTo(4), "+1 not a casualty, +1 unit, +2 hero");
            Assert.That(team.Units.Any(u => u.Name == "Squad 2"), Is.False, "casualty rolled 1: dead");
            Assert.That(team.StrikeTeam!.Vp, Is.EqualTo(2));
            Assert.That(team.StrikeTeam.BankedPoints, Is.EqualTo(40 + 20));
            Assert.That(log, Has.Some.Contains("dead"));
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
            Assert.That(unit.PointCost, Is.EqualTo(cost + 5));
            Assert.That(team.StrikeTeam!.BankedPoints, Is.EqualTo(40 + 40), "lost: +40");
        }

        [Test]
        public void HeroLevelUp_PicksSkillSet_ThenRollsSkill()
        {
            ArmyListFile team = Team();
            StrikeTeamRules.Start(team, "Red", "red");
            var (hero, sheet) = U(team, "Captain");
            int cost = hero.PointCost;
            // Trait roll 2 (Headstrong), skill-set roll 3 (Fighter or Shooter).
            StrikeTeamRules.AddXp(hero, sheet, 5, Dice(2, 3));
            StrikeTeamRules.Save(hero, sheet);
            Assert.That(hero.PointCost, Is.EqualTo(cost + 15));
            Assert.That(sheet.Pending, Is.EqualTo(new[] { "SkillSet:Fighter|Shooter" }));

            // Pick Fighter, skill roll 3 -> Fanatic: +1 attack on the melee weapon.
            string line = StrikeTeamRules.Choose(hero, "SkillSet:Fighter|Shooter", "Fighter", Dice(3));
            (hero, sheet) = U(team, "Captain");
            Assert.That(line, Does.Contain("Fanatic"));
            Assert.That(sheet.Skills, Is.EqualTo(new[] { "Fanatic" }));
            Assert.That(hero.Weapons.First(w => w.Name == "Blade").Attacks, Is.EqualTo(3));
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
            // Casualty 1, injury roll 2 = Chest Wound.
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(1, 2));
            var (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Injuries, Is.EqualTo(new[] { "Chest Wound" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_Core("Injury: Chest Wound")));
            Assert.That(hero.PointCost, Is.EqualTo(cost - 5));
            // Casualty 6, talent roll 6 = Toughened: Tough(3) -> Tough(4).
            StrikeTeamRules.AfterGame(team, new[] { new StrikeUnitResult(id, true, 0, 0) }, false, Dice(6, 6));
            (hero, sheet) = U(team, "Captain");
            Assert.That(sheet.Talents, Is.EqualTo(new[] { "Toughened" }));
            Assert.That(hero.SpecialRules, Does.Contain(new SpecialRuleEntry_CoreNumeric("Tough", 4)));
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
            foreach (string i in StrikeTeamRules.Injuries.Skip(1)) Assert.That(names, Does.Contain("Injury: " + i));
            foreach (string t in StrikeTeamRules.Talents) Assert.That(names, Does.Contain("Talent: " + t));
            foreach (string s in StrikeTeamRules.SkillSets.Values.SelectMany(v => v)) Assert.That(names, Does.Contain("Skill: " + s));
        }

        // ── In game ──────────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Kills_AreCountedOnTheSheet()
        {
            var killer = new UnitData(new PlayerID(Guid.NewGuid()), "Squad", 4, 5, new List<DataBinding<ModelData>>())
            { StrikeUnitJson = new StrikeUnit().ToJson() };
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Enemy", 4, 5, new List<DataBinding<ModelData>>());
            FirefightCampaign.OnUnitDestroyed(dead, killer);
            Assert.That(StrikeUnit.FromJson(killer.StrikeUnitJson)!.Kills, Is.EqualTo(1));
        }

        [Test]
        public void Motivated_PassesFirstMoraleTestOnly()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var c = new TestGameContext(store, new FixedDiceRoller(1));
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(10f, 10f), gameDataStore: store);
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Hero", 4, 4,
                new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) });
            store.Create(unit);
            QuestTreasure.GrantRule(c, unit, unit, CampaignRuleCatalog.Motivated.Name, ELifetime.UntilEndOfGame);
            Assert.That(FirefightCampaign.UseMotivated(c, unit), Is.True);
            Assert.That(FirefightCampaign.UseMotivated(c, unit), Is.False);
        }
    }
}
