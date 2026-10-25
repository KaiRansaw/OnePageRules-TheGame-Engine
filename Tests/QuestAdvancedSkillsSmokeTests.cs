using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Uses every higher-tier Star Quest skill once on a small table (a caster hero, a friendly hero, two enemy
    /// units close by), answering every prompt automatically. A skill must never throw; skills that need the
    /// player to move a unit or click the table are counted as "needs the table" rather than failed.
    /// </summary>
    [TestFixture]
    public class QuestAdvancedSkillsSmokeTests
    {
        private sealed class NotOnThisTable : Exception
        {
            public NotOnThisTable(string what) : base(what) { }
        }

        /// <summary>Last option of a menu (so "spend X" picks 3), the first unit of a unit pick, yes to yes/no.</summary>
        private sealed class AutoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                object reply = request switch
                {
                    StringSelectionRequest s when s.ValidOptions.Count > 0 => s.ValidOptions[^1],
                    SelectionRequest<UnitData> u when u.ValidOptions.Count > 0 => u.ValidOptions[0].Option,
                    YesNoRequest => true,
                    _ => throw new NotOnThisTable(request.GetType().Name),
                };
                return Task.FromResult((TReply)reply);
            }
        }

        [Test]
        public async Task EveryAdvancedSkill_RunsWithoutThrowing()
        {
            var problems = new List<string>();
            var needsTable = new List<string>();
            int ran = 0;
            foreach (QuestSkill skill in QuestAdvancedSkills.All)
            {
                (TestGameContext c, UnitData hero) = Table();
                if (skill.Unavailable(c, hero) is { } why) { needsTable.Add($"{skill.Name} ({why})"); continue; }
                try
                {
                    await skill.Use(c, hero);
                    ran++;
                }
                catch (NotOnThisTable e) { needsTable.Add($"{skill.Name} ({e.Message})"); }
                catch (Exception e) { problems.Add($"{skill.Name}: {e.GetType().Name} {e.Message}"); }
            }
            TestContext.Progress.WriteLine($"Ran {ran} skills; needed the table: {string.Join(", ", needsTable)}");
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
            Assert.That(ran, Is.GreaterThan(QuestAdvancedSkills.All.Count / 2), "most skills run without the table");
        }

        private static (TestGameContext, UnitData) Table()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var heroes = new PlayerID(Guid.NewGuid());
            var enemies = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { heroes }));
            store.Create(new TeamData(1, new List<PlayerID> { enemies }));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            var c = new TestGameContext(store, new FixedDiceRoller(4), settings: settings, playerRequester: new AutoRequester());

            DataBinding<UnitData> Make(PlayerID owner, string name, float x, QuestHero? sheet)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(x, 20f), gameDataStore: store);
                var mb = store.GetDataBinding<ModelData>(store.Create(model));
                var unit = new UnitData(owner, name, quality: 4, defense: 5, modelBindings: new List<DataBinding<ModelData>> { mb });
                if (sheet != null) { unit.QuestHeroJson = sheet.ToJson(); unit.QuestPower = sheet.Power; }
                return store.GetDataBinding<UnitData>(store.Create(unit));
            }
            var hero = Make(heroes, "Mage", 20f, new QuestHero { Name = "Mage", Power = 6, Caster = 2, Conditions = new() { "Impaired" } });
            var friend = Make(heroes, "Friend", 21.5f, new QuestHero { Name = "Friend", Power = 3, Conditions = new() { "Crippled" } });
            var e1 = Make(enemies, "Raiders", 22.5f, null);
            var e2 = Make(enemies, "Brutes", 18f, null);
            store.Create(new ArmyData(heroes, new List<DataBinding<UnitData>> { hero, friend }));
            store.Create(new ArmyData(enemies, new List<DataBinding<UnitData>> { e1, e2 }));
            return (c, hero.GetValue());
        }
    }
}
