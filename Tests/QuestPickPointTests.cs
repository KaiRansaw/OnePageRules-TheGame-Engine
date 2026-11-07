using FDG.Data;
using FDG.Network;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using Newtonsoft.Json;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// QA 3.11, 3.12, 3.17-3.20: "within X\" of the hero" (grenades, Fog Cloud, Trickster) is offered as a
    /// circle round the hero, not the square around it.
    /// </summary>
    [TestFixture]
    public class QuestPickPointTests
    {
        private sealed class Capture : IPlayerRequestByID
        {
            public PlaceObjectiveRequest? Seen;
            private readonly Position _answer;
            public Capture(Position answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                Seen = request as PlaceObjectiveRequest;
                return Task.FromResult((TReply)(object)_answer);
            }
        }

        [TearDown]
        public void TearDown() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        [Test]
        public async Task TheLegalArea_IsACircleCentredOnTheHero()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(20f, 20f), store);
            var binding = store.GetDataBinding<ModelData>(store.Create(model));
            UnitData hero = store.GetDataBinding<UnitData>(store.Create(
                new UnitData(new PlayerID(Guid.NewGuid()), "Rogue", 4, 4, new List<DataBinding<ModelData>> { binding }))).GetValue();

            var capture = new Capture(new Position(28f, 28f));   // the square's corner: 11.3" away
            var ctx = new TestGameContext(store, new FixedDiceRoller(4), playerRequester: capture);
            Position p = await QuestTreasure.PickPoint(ctx, hero, "Smoke Grenade", 9f);

            PlaceObjectiveRequest req = capture.Seen!;
            Assert.That(req.Reach, Is.TypeOf<CircularZone>());
            var circle = (CircularZone)req.Reach!;
            Assert.That(circle.Center.X, Is.EqualTo(20f));
            Assert.That(circle.Center.Y, Is.EqualTo(20f));
            Assert.That(circle.Radius, Is.EqualTo(9f));
            Assert.That(req.IsInLegalArea(new Position(28f, 28f)), Is.False, "the square's corner is out of reach");
            Assert.That(req.IsInLegalArea(new Position(28f, 20f)), Is.True);
            Assert.That(Position.GetDistance2D(p, new Position(20f, 20f)), Is.LessThanOrEqualTo(9.001f),
                "a stale answer from outside is still pulled back to 9\"");

            // The circle survives the trip to a networked player.
            JsonSerializerSettings wire = WireJsonSettings.For(store);
            var back = (PlaceObjectiveRequest)JsonConvert.DeserializeObject(
                JsonConvert.SerializeObject(req, typeof(PlaceObjectiveRequest), wire), typeof(PlaceObjectiveRequest), wire)!;
            Assert.That(back.Reach, Is.TypeOf<CircularZone>());
            Assert.That(((CircularZone)back.Reach!).Radius, Is.EqualTo(9f));
        }
    }
}
