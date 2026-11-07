using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// QA 9.16. GFSQ / AoFQ 2.1.0: "Heroes may use any one action plus one Skill action (in any order)". Holding
    /// without shooting is the hero's action, so it must leave the Skill action available - it used to be
    /// Pass, which ended the activation.
    /// </summary>
    [TestFixture]
    public class QuestHoldWithoutShootingTests
    {
        private sealed class Picker : IPlayerRequestByID
        {
            private readonly Queue<string> _picks;
            public readonly List<IReadOnlyList<string>> Offered = new();
            public Picker(params string[] picks) => _picks = new Queue<string>(picks);
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                IReadOnlyList<string> options = request switch
                {
                    StringSelectionRequest s => s.ValidOptions,
                    ChooseActionRequest a => a.ValidOptions,
                    _ => throw new InvalidOperationException(request.GetType().Name),
                };
                Offered.Add(options);
                return Task.FromResult((TReply)(object)_picks.Dequeue());
            }
        }

        [TearDown]
        public void TearDown() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        [Test]
        public async Task HoldingWithoutShooting_KeepsTheSkillAction()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            store.Create(new PlayerSlotInfo(me, slotID: 0, teamNumber: 1, name: "Kai", isFilled: true));
            store.Create(new PlayerSlotInfo(them, slotID: 1, teamNumber: 2, name: "Bot", isFilled: true));
            store.Create(new TeamData(1, new List<PlayerID> { me }));
            store.Create(new TeamData(2, new List<PlayerID> { them }));
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var heroModel = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon> { rifle }, new Position(20f, 20f), store)));
            var heroData = new UnitData(me, "Vex", 4, 4, new List<DataBinding<ModelData>> { heroModel })
            {
                QuestHeroJson = new QuestHero { Name = "Vex", Power = 3 }.ToJson(),
                QuestPower = 3,
            };
            DataBinding<UnitData> hero = store.GetDataBinding<UnitData>(store.Create(heroData));
            var enemyModel = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(30f, 20f), store)));
            store.Create(new UnitData(them, "Raider", 5, 5, new List<DataBinding<ModelData>> { enemyModel }));

            var settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = EGameMode.StarQuestMission;
            var picker = new Picker(QuestActions.HoldWithoutShootingOption, QuestActions.SkillOption);
            var ctx = new TestGameContext(store, new FixedDiceRoller(4), settings: settings, playerRequester: picker);

            var unitCtx = new UnitActionContext(ctx, hero);
            unitCtx.Reset(hero);

            var choose = new ChooseActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            bool toQuest = false;
            choose.ToQuestAction.Bind("q");
            choose.ToQuestAction.OnWillActivate += _ => toQuest = true;
            await choose.Enter(unitCtx);
            Assert.That(picker.Offered[0], Does.Contain(QuestActions.HoldWithoutShootingOption));
            Assert.That(toQuest, Is.True);

            var quest = new QuestActionStage(ctx, new NoOpLayer<IUnitActionContext>());
            bool finished = false, ended = false;
            quest.OnFinished.Bind("f");
            quest.OnFinished.OnWillActivate += _ => finished = true;
            quest.ToEndActivation.Bind("e");
            quest.ToEndActivation.OnWillActivate += _ => ended = true;
            await quest.Enter(unitCtx);
            Assert.That(finished, Is.True, "back to the action menu");
            Assert.That(ended, Is.False, "the activation is not over");
            Assert.That(unitCtx.CastingWindowClosed, Is.False, "holding is not attacking");
            Assert.That(unitCtx.HasAttacked, Is.False, "holding is not attacking - the hero may still cast");

            // The menu again: the Skill action is still there; no move, no shot, no second hold.
            await choose.Enter(unitCtx);
            IReadOnlyList<string> second = picker.Offered[1];
            Assert.That(second, Does.Contain(QuestActions.SkillOption));
            Assert.That(second, Does.Not.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(second, Does.Not.Contain(ChooseActionStage.SHOOT_CHOICE_NAME));
            Assert.That(second, Does.Not.Contain(ChooseActionStage.CHARGE_CHOICE_NAME));
            Assert.That(second, Does.Not.Contain(QuestActions.HoldWithoutShootingOption));
        }
    }
}
