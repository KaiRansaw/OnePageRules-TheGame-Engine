using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - the LINK test: <c>PlaceDeferredUnitsStage</c> actually places Scouts in alternating order.
    ///
    /// <para>
    /// "<i>Players alternate in placing Scout units, starting with the player that activates next.</i>"
    /// <see cref="AlternatingPlacementTests"/> proves the ordering helper is right; this proves the
    /// stage calls it. Those are different claims, and the project has been bitten repeatedly by a rule
    /// that is correct in isolation and never reached in play - a working helper and a stage that still
    /// walks its own list look identical from the outside.
    /// </para>
    /// <para>
    /// The fixture needs two players' Scouts in one deployment, which no existing Scout test has: the
    /// bug could only ever show up with more than one owner, which is presumably why it survived.
    /// </para>
    /// </summary>
    [TestFixture]
    public class ScoutAlternatingPlacementTests
    {
        private GameDataStore _store = null!;
        private PlayerID _playerA;
        private PlayerID _playerB;
        private TeamData _teamA = null!;
        private TeamData _teamB = null!;
        private Dictionary<ITeam, DataBinding<RectangularZone>> _zones = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _playerA = new PlayerID(Guid.NewGuid());
            _playerB = new PlayerID(Guid.NewGuid());
            _teamA = new TeamData(0, new List<PlayerID> { _playerA });
            _teamB = new TeamData(1, new List<PlayerID> { _playerB });

            var bottom = new RectangularZone(0f, GameWideConstants.STANDARD_TABLE_WIDTH_INCHES,
                0f, GameWideConstants.DEPLOYMENT_DISTANCE_INCHES);
            var top = new RectangularZone(0f, GameWideConstants.STANDARD_TABLE_WIDTH_INCHES,
                GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES - GameWideConstants.DEPLOYMENT_DISTANCE_INCHES,
                GameWideConstants.STANDARD_TABLE_HEIGHT_INCHES);

            _zones = new Dictionary<ITeam, DataBinding<RectangularZone>>
            {
                [_teamA] = _store.GetDataBinding<RectangularZone>(_store.Create(bottom)),
                [_teamB] = _store.GetDataBinding<RectangularZone>(_store.Create(top)),
            };
        }

        /// <summary>
        /// Two Scouts a side. Army-by-army gives A1 A2 B1 B2; the rule gives A1 B1 A2 B2.
        /// </summary>
        [Test]
        public async Task TwoScoutsEachArePlacedAlternately()
        {
            MakeArmy(_playerA, MakeScout(_playerA, "A1"), MakeScout(_playerA, "A2"));
            MakeArmy(_playerB, MakeScout(_playerB, "B1"), MakeScout(_playerB, "B2"));

            List<string> order = await RunPlacement(_teamA, _teamB);

            Assert.That(order, Is.EqualTo(new[] { "A1", "B1", "A2", "B2" }).AsCollection,
                "placing A1 A2 B1 B2 lets the second player see the whole enemy forward screen before "
                + "committing either of theirs, which is what the alternation exists to prevent.");
        }

        /// <summary>
        /// "Starting with the player that activates next" - the deployment roll order decides, and it is
        /// the same order the main phase activates in. Reversing it must reverse who leads.
        /// </summary>
        [Test]
        public async Task TheDeploymentRollOrderDecidesWhoPlacesFirst()
        {
            MakeArmy(_playerA, MakeScout(_playerA, "A1"), MakeScout(_playerA, "A2"));
            MakeArmy(_playerB, MakeScout(_playerB, "B1"), MakeScout(_playerB, "B2"));

            List<string> order = await RunPlacement(_teamB, _teamA);

            Assert.That(order, Is.EqualTo(new[] { "B1", "A1", "B2", "A2" }).AsCollection);
        }

        /// <summary>
        /// Uneven counts still alternate while both sides have Scouts left, and every Scout is placed -
        /// the reordering must not drop one, which would be a far worse bug than the one it fixes.
        /// </summary>
        [Test]
        public async Task UnevenCountsAlternateThenFinish()
        {
            MakeArmy(_playerA, MakeScout(_playerA, "A1"), MakeScout(_playerA, "A2"), MakeScout(_playerA, "A3"));
            MakeArmy(_playerB, MakeScout(_playerB, "B1"));

            List<string> order = await RunPlacement(_teamA, _teamB);

            Assert.That(order, Is.EqualTo(new[] { "A1", "B1", "A2", "A3" }).AsCollection);
        }

        /// <summary>
        /// One player's Scouts in a solo deployment are untouched - a rule about taking turns must not
        /// reshuffle a list when there is nobody to take turns with.
        /// </summary>
        [Test]
        public async Task ASinglePlayersScoutsKeepTheirOrder()
        {
            MakeArmy(_playerA, MakeScout(_playerA, "A1"), MakeScout(_playerA, "A2"), MakeScout(_playerA, "A3"));

            List<string> order = await RunPlacement(_teamA);

            Assert.That(order, Is.EqualTo(new[] { "A1", "A2", "A3" }).AsCollection);
        }

        // ── Harness ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Drives the real stage and returns the units in the order it asked the player to place them.
        /// </summary>
        private async Task<List<string>> RunPlacement(params ITeam[] rollOrder)
        {
            var requester = new OrderRecordingPlaceRequester();
            var ctx = new TriggeredMoveTestContext(_store, requester);
            var deployment = new DeploymentTurnContext(ctx, rollOrder.ToList(), _zones);

            var stage = new PlaceDeferredUnitsStage(ctx, new NoOpLayer<IDeploymentTurnContext>());
            stage.OnFinish.Bind("done");
            await stage.Enter(deployment);

            return requester.Order;
        }

        private DataBinding<UnitData> MakeScout(PlayerID owner, string name)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            var unit = new UnitData(owner, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>
                {
                    _store.GetDataBinding<ModelData>(_store.Create(model)),
                });

            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            binding.GetValue().AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout));
            return binding;
        }

        private void MakeArmy(PlayerID owner, params DataBinding<UnitData>[] units) =>
            _store.Create(new ArmyData(owner, units.ToList()));
    }

    /// <summary>
    /// Records which unit each placement request was for, in the order the stage asked - the display
    /// name carries it ("Deploying &lt;name&gt; (Scout)"), which is also what a player sees.
    /// </summary>
    internal sealed class OrderRecordingPlaceRequester : IPlayerRequestByID
    {
        public readonly List<string> Order = new();

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is PlaceObjectsRequest<ModelData> place)
            {
                // "Deploying A1 (Scout)" -> "A1"
                string label = place.DisplayName ?? string.Empty;
                int start = label.IndexOf(' ') + 1;
                int end = label.LastIndexOf(" (", StringComparison.Ordinal);
                Order.Add(end > start ? label[start..end] : label);

                var dest = new Position(place.DeploymentZone.Bounds.CenterX,
                    place.DeploymentZone.Bounds.CenterZ);
                var entries = place.ModelsToPlace
                    .Select(m => new PlacedObjectEntry<ModelData>(m, dest))
                    .ToList();
                return Task.FromResult((TReply)(object)new Selected<List<PlacedObjectEntry<ModelData>>>(entries));
            }

            throw new InvalidOperationException("Unexpected request type: " + request.GetType());
        }
    }
}
