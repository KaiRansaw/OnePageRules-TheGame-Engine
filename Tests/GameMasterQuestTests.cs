using FDG.Data;
using FDG.GameMasterTools;
using FDG.Players;
using FDG.Quest;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Game Master tools for Star Quest: mission state, challenges, ending the game, and hero edits.</summary>
    [TestFixture]
    public class GameMasterQuestTests
    {
        private GameDataStore _store = null!;
        private GameMasterHub _hub = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_player, slotID: 0, teamNumber: 0, name: "Host", isFilled: true));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            settings.PlayStyle = EPlayStyle.GameMaster;
            _hub = new GameMasterHub();
            _hub.Attach(new TestGameContext(_store, new FixedDiceRoller(4), settings: settings));
        }

        private QuestProgress Quest => GameProgressUtilities.GetQuestProgress(_store);

        private UnitData MakeHero()
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(10f, 10f), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_player, "Hero", quality: 5, defense: 6,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.QuestHeroJson = new QuestHero { Name = "Hero", Power = 3, Credits = 30 }.ToJson();
            unit.QuestPower = 3;
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        [Test]
        public async Task MissionState_AndChallenges()
        {
            Assert.That(_hub.IsQuest && _hub.IsQuestMission, Is.True);
            _hub.QuestSetAlertness(9);
            _hub.QuestSetDifficulty(3);
            _hub.QuestToggleChallenge(QuestChallenges.WeatherName);
            _hub.QuestSetTimeOfDay("Night");
            await _hub.DrainAsync();
            Assert.That(Quest.Alertness, Is.EqualTo(6), "clamped to 6");
            Assert.That(Quest.Difficulty, Is.EqualTo(3));
            Assert.That(Quest.Weather, Is.EqualTo("Worsening"), "turning Weather on starts it at its first step");
            Assert.That(Quest.TimeOfDay, Is.EqualTo("Night"));
            Assert.That(Quest.Challenges, Does.Contain(QuestChallenges.TimeOfDayName), "setting a time turns the challenge on");

            _hub.QuestToggleChallenge(QuestChallenges.WeatherName);
            await _hub.DrainAsync();
            Assert.That(Quest.Weather, Is.EqualTo(""));
        }

        [Test]
        public async Task WinAndLose_SetTheOutcome_AndEndTheGame()
        {
            UnitData hero = MakeHero();
            _hub.QuestFinish(win: true);
            await _hub.DrainAsync();
            Assert.That(Quest.Outcome, Is.EqualTo("success"));
            Assert.That(_hub.ConsumeEndGame(), Is.True);
            Assert.That(_hub.ConsumeEndRound(), Is.True);

            _hub.QuestFinish(win: false);
            await _hub.DrainAsync();
            Assert.That(Quest.Outcome, Is.EqualTo("failed"));
            Assert.That(hero.GetIsAlive(), Is.False, "a loss knocks the heroes out");
        }

        [Test]
        public async Task HeroEdits()
        {
            UnitData hero = MakeHero();
            string skill = QuestSkill.All.First(s => !s.Common && !s.Feat).Name;
            _hub.HeroPower(hero.ID, -2);
            _hub.HeroCredits(hero.ID, 50);
            _hub.HeroToggleCondition(hero.ID, QuestTreasure.Crippled);
            _hub.HeroToggleInjury(hero.ID, "Broken Arm");
            _hub.HeroToggleSkill(hero.ID, skill);
            _hub.HeroGiveItem(hero.ID, "Med Kit");
            await _hub.DrainAsync();

            QuestHero sheet = QuestHero.FromJson(hero.QuestHeroJson)!;
            Assert.That(hero.QuestPower, Is.EqualTo(1));
            Assert.That(sheet.Credits, Is.EqualTo(80));
            Assert.That(sheet.Conditions, Does.Contain(QuestTreasure.Crippled));
            Assert.That(sheet.Injuries, Does.Contain("Broken Arm"));
            Assert.That(sheet.Skills, Does.Contain(skill));
            Assert.That(sheet.Items, Does.Contain("Med Kit"));

            _hub.HeroFullPower(hero.ID);
            _hub.HeroToggleCondition(hero.ID, QuestTreasure.Crippled);
            _hub.HeroToggleSkill(hero.ID, skill);
            _hub.HeroRemoveItem(hero.ID, "Med Kit");
            _hub.HeroKnockOut(hero.ID);
            await _hub.DrainAsync();
            sheet = QuestHero.FromJson(hero.QuestHeroJson)!;
            Assert.That(hero.QuestPower, Is.EqualTo(3));
            Assert.That(sheet.Conditions, Is.Empty);
            Assert.That(sheet.Skills, Is.Empty);
            Assert.That(sheet.Items, Is.Empty);
            Assert.That(hero.GetIsAlive(), Is.False);

            _hub.HeroRevive(hero.ID);
            await _hub.DrainAsync();
            Assert.That(hero.GetIsAlive(), Is.True);
        }

        [Test]
        public async Task Tokens_PlaceAndClear()
        {
            _hub.QuestPlaceSearchTokens();
            await _hub.DrainAsync();
            Assert.That(Quest.Tokens, Has.Count.EqualTo(4));
            _hub.QuestClearTokens();
            await _hub.DrainAsync();
            Assert.That(Quest.Tokens, Is.Empty);
        }
    }
}
