using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The Horde XP kill hook end to end against a real store: a defending killer earns XP, levels up at
    /// 5 XP, and the Specialist / Fast Learner rolls ask the owner to choose.
    /// </summary>
    [TestFixture]
    public class HordeExperienceIntegrationTests
    {
        private GameDataStore _store = null!;
        private PlayerID _defender, _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _defender = new PlayerID(Guid.NewGuid());
            _enemy = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_defender, slotID: 0, teamNumber: 0, name: "Defender", isFilled: true));
            _store.Create(new PlayerSlotInfo(_enemy, slotID: 1, teamNumber: 1, name: "Enemy", isFilled: true));
        }

        private TestGameContext Context(int face, IPlayerRequestByID requester, EGameMode mode = EGameMode.Horde)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = mode;
            return new TestGameContext(_store, new OneFaceRoller(face), settings: settings,
                ruleResolver: CoreRuleCatalog.CreateResolver(), playerRequester: requester);
        }

        private UnitData MakeUnit(PlayerID owner, string name)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(10, 10), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(owner, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        [Test]
        public async Task FastLearner_RecordsItself_AndLetsTheOwnerPickAnotherTrait()
        {
            var requester = new PickRequester("Resilient");
            TestGameContext ctx = Context(face: 6, requester);
            UnitData killer = MakeUnit(_defender, "Veterans");
            killer.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HordeXP, 4));

            await HordeExperience.OnUnitDestroyed(ctx, MakeUnit(_enemy, "Grunts"), killer);

            Assert.That(killer.Tokens.GetTokenCount(TokenType.HordeXP), Is.EqualTo(7));
            Assert.That(HordeExperience.OwnedTraits(killer),
                Is.EquivalentTo(new[] { HordeExperience.FastLearner, HordeExperience.Resilient }));
            Assert.That(requester.LastOptions, Does.Not.Contain("Fast Learner"));
            Assert.That(requester.LastOptions, Does.Contain("Agile"));
        }

        [Test]
        public async Task Specialist_AsksMeleeOrShooting()
        {
            var requester = new PickRequester("Shooting Specialist");
            TestGameContext ctx = Context(face: 3, requester);
            UnitData killer = MakeUnit(_defender, "Gunners");
            killer.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HordeXP, 2));

            await HordeExperience.OnUnitDestroyed(ctx, MakeUnit(_enemy, "Grunts"), killer);

            Assert.That(requester.LastOptions, Is.EquivalentTo(new[] { "Melee Specialist", "Shooting Specialist" }));
            Assert.That(HordeExperience.OwnedTraits(killer), Is.EquivalentTo(new[] { HordeExperience.ShootingSpecialist }));
        }

        [Test]
        public async Task NoXp_InTheStandardGame_OrForTheEnemySide()
        {
            UnitData defender = MakeUnit(_defender, "Defenders");
            UnitData enemy = MakeUnit(_enemy, "Horde");

            await HordeExperience.OnUnitDestroyed(Context(1, new PickRequester(""), EGameMode.Standard), enemy, defender);
            Assert.That(defender.Tokens.GetTokenCount(TokenType.HordeXP), Is.EqualTo(0), "standard game");

            await HordeExperience.OnUnitDestroyed(Context(1, new PickRequester("")), defender, enemy);
            Assert.That(enemy.Tokens.GetTokenCount(TokenType.HordeXP), Is.EqualTo(0), "the horde earns no XP");
        }

        private sealed class OneFaceRoller : IDiceRoller
        {
            private readonly int _face;
            public OneFaceRoller(int face) => _face = face;
            public IDiceResults Roll(int sideCount, float rollCount)
            {
                float[] perSide = new float[sideCount];
                perSide[_face - 1] = rollCount;
                return new DiceResults(perSide);
            }
        }

        private sealed class PickRequester : IPlayerRequestByID
        {
            private readonly string _pick;
            public IReadOnlyList<string> LastOptions { get; private set; } = Array.Empty<string>();
            public PickRequester(string pick) => _pick = pick;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest selection)
                {
                    LastOptions = selection.ValidOptions;
                    return Task.FromResult((TReply)(object)_pick);
                }
                throw new InvalidOperationException("Unexpected request " + request.GetType().Name);
            }
        }
    }
}
