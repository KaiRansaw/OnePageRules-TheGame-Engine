using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #466 - the 3.6.0 campaign books' "Play Mission" length rules, printed identically in GF, GF: Firefight,
    /// AoF and AoF: Skirmish:
    /// <list type="bullet">
    /// <item>"if after 4 rounds there is no winner, the game continues onto the next round until there is
    /// one winner."</item>
    /// <item>"At the beginning of any round players may concede, removing all of their units from the
    /// table, which don't count as casualties for this mission."</item>
    /// </list>
    /// The rule pieces on a hand-built table. <see cref="CampaignMissionLengthInAPlayedGameTests"/> proves
    /// the game actually calls them.
    /// </summary>
    [TestFixture]
    public class CampaignMissionLengthTests
    {
        private GameDataStore _store = null!;
        private PlayerID _me;
        private PlayerID _them;

        /// <summary>Answers the concession check from a set; any other request is a test failure.</summary>
        private sealed class ConcedeRequester : IPlayerRequestByID
        {
            private readonly HashSet<PlayerID> _concede;
            public readonly List<PlayerID> Asked = new();
            public ConcedeRequester(params PlayerID[] concede) => _concede = concede.ToHashSet();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is not ConcedeCheckRequest check)
                    throw new AssertionException($"unexpected request {typeof(TRequest).Name}");
                Asked.Add(check.TargetPlayerID);
                return Task.FromResult((TReply)(object)_concede.Contains(check.TargetPlayerID));
            }
        }

        private sealed class Ctx : TestGameContext
        {
            public GameResult? Result { get; private set; }
            public Ctx(GameDataStore store, IPlayerRequestByID? requester = null)
                : base(store, new FixedDiceRoller(4), ruleResolver: CoreRuleCatalog.CreateResolver(),
                    playerRequester: requester) { }
            public override void NotifyGameCompleted(GameResult result) => Result = result;
        }

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_me, slotID: 0, teamNumber: 1, name: "Kai", isFilled: true));
            _store.Create(new PlayerSlotInfo(_them, slotID: 1, teamNumber: 2, name: "Bot", isFilled: true));
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        private DataBinding<UnitData> Unit(PlayerID owner, Position at, bool campaign = true)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}",
                quality: 4, defense: 4, modelBindings: bindings);
            if (campaign) unit.StrikeUnitJson = new StrikeUnit { TeamFile = "army.fdgarmy" }.ToJson();
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private ObjectiveData Objective(PlayerID? owner, float x = 20f)
        {
            var objective = new ObjectiveData(new Position(x, 20f), _store);
            _store.Create(objective);
            if (owner.HasValue) objective.SetOwner(owner.Value);
            return objective;
        }

        // ── Mission Length ───────────────────────────────────────────────────────────────────────

        [Test]
        public void ATiedCampaignMission_PlaysOn()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            Objective(_me, 10f);
            Objective(_them, 30f);

            var ctx = new Ctx(_store);
            Assert.That(CampaignMissionLength.HasOneWinner(ctx), Is.False);
            Assert.That(CampaignMissionLength.PlaysOn(ctx, 4), Is.True);
        }

        [Test]
        public void ACampaignMissionWithAWinner_Ends()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            Objective(_me, 10f);
            Objective(_me, 20f);
            Objective(_them, 30f);

            Assert.That(CampaignMissionLength.PlaysOn(new Ctx(_store), 4), Is.False);
        }

        [Test]
        public void NobodyHoldingAnything_IsNoWinner_SoItPlaysOn()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            Objective(null);

            Assert.That(CampaignMissionLength.PlaysOn(new Ctx(_store), 4), Is.True);
        }

        [Test]
        public void AnOrdinaryGame_NeverPlaysOn()
        {
            Unit(_me, new Position(10f, 10f), campaign: false);
            Unit(_them, new Position(40f, 30f), campaign: false);
            Objective(_me, 10f);
            Objective(_them, 30f);

            Assert.That(CampaignMissionLength.PlaysOn(new Ctx(_store), 4), Is.False, "a tie, as before");
        }

        [Test]
        public void TheHouseRuleCap_StopsAtTwelve()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            var ctx = new Ctx(_store);

            Assert.That(CampaignMissionLength.PlaysOn(ctx, 11), Is.True);
            Assert.That(CampaignMissionLength.PlaysOn(ctx, CampaignMissionLength.LastRound), Is.False);
        }

        [TestCase(EGameMode.Horde)]
        [TestCase(EGameMode.StarQuestMission)]
        public void OnlyTheStandardGame_PlaysOn(EGameMode mode)
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            var settings = GameSettings.GetDefault();
            settings.GameMode = mode;
            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);

            Assert.That(CampaignMissionLength.PlaysOn(ctx, 4), Is.False);
        }

        // ── Conceding ────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Conceding_TakesTheArmyOffTheTable_AliveAndNotACasualty()
        {
            DataBinding<UnitData> mine = Unit(_me, new Position(10f, 10f));
            DataBinding<UnitData> dead = Unit(_me, new Position(14f, 10f));
            foreach (IModel m in dead.GetValue().Models) ((ModelData)m).DealWounds(((ModelData)m).TotalWounds);
            DataBinding<UnitData> theirs = Unit(_them, new Position(40f, 30f));
            ObjectiveData held = Objective(_me);

            var ctx = new Ctx(_store, new ConcedeRequester(_me));
            bool over = await CampaignMissionLength.RunRoundStart(ctx, 2);

            Assert.That(over, Is.True, "only one side is left");
            UnitData unit = mine.GetValue();
            Assert.That(unit.GetIsAlive(), Is.True, "\"don't count as casualties\" - the casualty check reads alive");
            Assert.That(ReserveRules.IsInReserve(unit), Is.True, "off the table");
            Assert.That(unit.Tokens.HasToken(TokenType.Conceded), Is.True);
            Assert.That(dead.GetValue().GetIsAlive(), Is.False, "a unit already destroyed is still a casualty");
            Assert.That(held.OwnerID, Is.Null, "nobody of theirs is left to hold it");
            Assert.That(CampaignMissionLength.HasConceded(ctx.TableState, _me), Is.True);
            Assert.That(CampaignMissionLength.HasConceded(ctx.TableState, _them), Is.False);
            Assert.That(ReserveRules.IsInReserve(theirs.GetValue()), Is.False);
        }

        [Test]
        public async Task NoConcession_AsksEveryoneStillIn_AndChangesNothing()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            var requester = new ConcedeRequester();

            bool over = await CampaignMissionLength.RunRoundStart(new Ctx(_store, requester), 1);

            Assert.That(over, Is.False);
            Assert.That(requester.Asked, Is.EqualTo(new[] { _me, _them }), "slot order");
        }

        [Test]
        public async Task AnOrdinaryGame_AsksNobody()
        {
            Unit(_me, new Position(10f, 10f), campaign: false);
            Unit(_them, new Position(40f, 30f), campaign: false);
            var requester = new ConcedeRequester(_me);

            Assert.That(await CampaignMissionLength.RunRoundStart(new Ctx(_store, requester), 2), Is.False);
            Assert.That(requester.Asked, Is.Empty);
        }

        [Test]
        public async Task ThreeSides_OneConcedes_TheGameGoesOn_AndTheyAreNotAskedAgain()
        {
            var third = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(third, slotID: 2, teamNumber: 3, name: "Third", isFilled: true));
            _store.Create(new TeamData(3, new List<PlayerID> { third }));
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            Unit(third, new Position(60f, 30f));

            var requester = new ConcedeRequester(_me);
            var ctx = new Ctx(_store, requester);
            Assert.That(await CampaignMissionLength.RunRoundStart(ctx, 2), Is.False, "two sides still fight");
            requester.Asked.Clear();
            await CampaignMissionLength.RunRoundStart(ctx, 3);
            Assert.That(requester.Asked, Does.Not.Contain(_me));
        }

        [Test]
        public async Task TheVictoryStage_GivesTheGameToTheSideLeft_WhateverTheObjectives()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 30f));
            Objective(_me, 10f);
            Objective(_me, 15f);   // Kai leads on objectives, then concedes - both go neutral

            var ctx = new Ctx(_store, new ConcedeRequester(_me));
            await CampaignMissionLength.RunRoundStart(ctx, 3);
            await new VictoryCalculationStage(ctx, new NoOpLayer<IGameContext>()).Enter(ctx);

            Assert.That(ctx.Result!.Outcome, Is.EqualTo(EGameOutcome.Win));
            Assert.That(ctx.Result.Winner, Is.EqualTo(_them));
        }

        [Test]
        public void TheBotNeverConcedes()
        {
            var request = new ConcedeCheckRequest(_them, 3);
            Assert.That(new FDG.Ai.Resolvers.AiConcedeCheckResolver().Resolve(request).Result, Is.False);
        }
    }
}
