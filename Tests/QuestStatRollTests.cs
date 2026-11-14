using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Quest "stat rolls" (2026-11-14). The Quest books define a stat test as any roll against a stat - "Stat tests ...
    /// such as hitting its targets or passing morale tests" - and give Quality as "the score needed for attacks and
    /// morale", Defense as "the score needed for Defense" (AoF: Quest Beginners Guide v2.1.0 p4; GF: Star Quest Core
    /// Rules v2.1.0). So:
    /// <list type="bullet">
    /// <item>Mockery / Spiteful Curse ("-1 to their next stat roll") work on enemies: the first of the unit's hit,
    /// Defense or morale rolls spends it. They were greyed out as "enemies never take stat tests".</item>
    /// <item>Impaired ("-1 to all stat rolls") also hits a hero's hit, Defense and morale rolls, not only Str/Dex/Wil.</item>
    /// </list>
    /// </summary>
    [TestFixture]
    public class QuestStatRollTests
    {
        [Test]
        public void ANextStatRollPenalty_IsSpentByWhicheverStatRollComesFirst()
        {
            (_, _, UnitData enemy, _, _) = Table();
            QuestActions.GrantNextStatRoll(enemy, enemy, -1, "Mockery");

            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Hit), Is.EqualTo(-1));
            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Save), Is.EqualTo(-1));
            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Morale), Is.EqualTo(-1));
            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Cast), Is.EqualTo(0), "a casting roll is not a stat roll");
            Assert.That(GrantedRollModifiers.PeekDetailed(enemy, ERollKind.Hit).Single().Describe(), Is.EqualTo("Mockery -1"));

            (int net, _) = GrantedRollModifiers.ConsumeDetailed(enemy, ERollKind.Save);

            Assert.That(net, Is.EqualTo(-1), "its Defense roll came first and took the -1");
            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Hit), Is.EqualTo(0), "spent - one roll, not one of each kind");
            Assert.That(GrantedRollModifiers.PeekNet(enemy, ERollKind.Morale), Is.EqualTo(0));
        }

        [Test]
        public void AnImpairedHero_GetsMinusOneToHitDefenseAndMorale_ButNotCasting()
        {
            (_, UnitData impaired, _, _, UnitData friend) = Table();

            foreach (ERollKind kind in new[] { ERollKind.Hit, ERollKind.Save, ERollKind.Morale })
            {
                Assert.That(GrantedRollModifiers.PeekNet(impaired, kind), Is.EqualTo(-1), kind.ToString());
                Assert.That(GrantedRollModifiers.ConsumeNet(impaired, kind), Is.EqualTo(-1), "it lasts - Impaired is a condition, not a one-off");
                Assert.That(GrantedRollModifiers.PeekNet(friend, kind), Is.EqualTo(0), "not Impaired");
            }
            Assert.That(GrantedRollModifiers.PeekNet(impaired, ERollKind.Cast), Is.EqualTo(0));
            Assert.That(GrantedRollModifiers.PeekDetailed(impaired, ERollKind.Hit).Select(m => m.Describe()), Does.Contain("Impaired -1"));
        }

        [TestCase("Mockery")]
        [TestCase("Spiteful Curse")]
        public async Task MockeryAndSpitefulCurse_AreUsable_AndMarkTwoEnemies(string name)
        {
            Assert.That(QuestAdvancedSkills.NotPossible.ContainsKey(name), Is.False);
            (TestGameContext c, UnitData hero, UnitData e1, UnitData e2, _) = Table();
            QuestSkill skill = QuestAdvancedSkills.All.Single(s => s.Name == name);

            Assert.That(skill.Unavailable(c, hero), Is.Null);
            await skill.Use(c, hero);

            Assert.That(GrantedRollModifiers.PeekNet(e1, ERollKind.Hit), Is.EqualTo(-1));
            Assert.That(GrantedRollModifiers.PeekNet(e2, ERollKind.Hit), Is.EqualTo(-1));
        }

        private sealed class PickAll : IPlayerRequestByID
        {
            private int _unitPicks;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                object reply = request switch
                {
                    SelectionRequest<UnitData> u => u.ValidOptions[_unitPicks++ % u.ValidOptions.Count].Option,
                    StringSelectionRequest s => s.ValidOptions[0],
                    YesNoRequest => true,
                    _ => throw new InvalidOperationException(request.GetType().Name),
                };
                return Task.FromResult((TReply)reply);
            }
        }

        // An Impaired hero, a friendly hero that isn't, and two enemy units within 6" of the first.
        private static (TestGameContext C, UnitData Hero, UnitData Enemy1, UnitData Enemy2, UnitData Friend) Table()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var heroes = new PlayerID(Guid.NewGuid());
            var enemies = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { heroes }));
            store.Create(new TeamData(1, new List<PlayerID> { enemies }));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            var c = new TestGameContext(store, new FixedDiceRoller(4), settings: settings, playerRequester: new PickAll());

            DataBinding<UnitData> Make(PlayerID owner, string name, float x, QuestHero? sheet)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(x, 20f), gameDataStore: store);
                var mb = store.GetDataBinding<ModelData>(store.Create(model));
                var unit = new UnitData(owner, name, quality: 4, defense: 5, modelBindings: new List<DataBinding<ModelData>> { mb });
                if (sheet != null) { unit.QuestHeroJson = sheet.ToJson(); unit.QuestPower = sheet.Power; }
                return store.GetDataBinding<UnitData>(store.Create(unit));
            }
            var hero = Make(heroes, "Bard", 20f, new QuestHero { Name = "Bard", Power = 6, Conditions = new() { "Impaired" } });
            var friend = Make(heroes, "Scout", 40f, new QuestHero { Name = "Scout", Power = 3 });
            var e1 = Make(enemies, "Raiders", 23f, null);
            var e2 = Make(enemies, "Brutes", 17f, null);
            store.Create(new ArmyData(heroes, new List<DataBinding<UnitData>> { hero, friend }));
            store.Create(new ArmyData(enemies, new List<DataBinding<UnitData>> { e1, e2 }));
            return (c, hero.GetValue(), e1.GetValue(), e2.GetValue(), friend.GetValue());
        }
    }
}
