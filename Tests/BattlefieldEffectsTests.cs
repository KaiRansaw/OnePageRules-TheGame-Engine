using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Battlefield Effects - Special Conditions (Advanced Rulebooks 3.6.0: GF p38, GFF p35, AoF p38, AoFS p35).</summary>
    [TestFixture]
    public class BattlefieldEffectsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private void Build(int condition, int face, Func<object, object>? answer = null, EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = BattlefieldEffects.WithPick(EAdvancedRules.SpecialConditions, condition);
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: answer == null ? null : new Answer(answer));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        [TearDown]
        public void Reset() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private UnitData Unit(PlayerID owner, Position at, int models = 1, string name = "")
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name.Length > 0 ? name : $"Unit{_store.GetAllValues<UnitData>().Count() + 1}",
                quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        private static int Alive(IUnit u) => u.Models.Count(m => m.GetIsAlive());

        [Test]
        public void TheCatalog_IsInAllFourGames_NotQuest()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.Chapters(s), Does.Contain(AdvancedRules.BattlefieldEffectsChapter), s.ToString());
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.BattlefieldEffectsChapter));
            Assert.That(BattlefieldEffects.Name(BattlefieldEffects.ZeroGravity, EGameSystem.AgeOfFantasy), Is.EqualTo("Magical Winds"));
            GameSettings g = GameSettings.GetDefault();
            g.AdvancedRules = BattlefieldEffects.WithPick(EAdvancedRules.SpecialConditions, 11);
            Assert.That(BattlefieldEffects.Picked(g), Is.EqualTo(11));
            Assert.That(AdvancedRules.Enabled(g), Is.EqualTo(EAdvancedRules.SpecialConditions), "the pick bits are not options");
        }

        [Test]
        public async Task ARolledCondition_FollowsTheTable_AndIsSettledOnce()
        {
            // Roll for it, every die a 5: type 5 = Mysterious, then 5 = Mystical Fog.
            Build(condition: 0, face: 5);
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(BattlefieldEffects.Condition(_context), Is.EqualTo(BattlefieldEffects.MysticalFog));
            await BattlefieldEffects.RoundStart(_context, 2);
            Assert.That(BattlefieldEffects.State(_context)!.Settled, Is.True);
            Assert.That(BattlefieldEffects.Condition(_context), Is.EqualTo(BattlefieldEffects.MysticalFog));

            Build(condition: 0, face: 2);   // 1-2: None
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(BattlefieldEffects.Condition(_context), Is.EqualTo(0));
        }

        [Test]
        public async Task DarkSkies_CutsRange_ToAMinimumOf3()
        {
            Build(BattlefieldEffects.DarkSkies, face: 4);   // 2D6 = 8
            UnitData me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(BattlefieldEffects.State(_context)!.DarkSkies, Is.EqualTo(8));
            Assert.That(BattlefieldEffects.DarkSkiesRange(me, 24f), Is.EqualTo(16f));
            Assert.That(BattlefieldEffects.DarkSkiesRange(me, 9f), Is.EqualTo(3f));
            Assert.That(BattlefieldEffects.DarkSkiesRange(me, 2f), Is.EqualTo(2f), "never raised to 3");
        }

        [Test]
        public async Task MuddyTerrain_MakesDifficultPieces4Inches()
        {
            Build(BattlefieldEffects.MuddyTerrain, face: 4);
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(GameWideConstants.DifficultTerrainMoveCapInches, Is.EqualTo(4f));
            Assert.That(BattlefieldEffects.WholeTableDifficult(_context), Is.True);
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            Assert.That(GameWideConstants.DifficultTerrainMoveCapInches, Is.EqualTo(6f), "a new game starts at the book's 6\"");
        }

        [Test]
        public async Task Damnation_Refused_GivesMinus1ToEverything_ThisRound()
        {
            Build(BattlefieldEffects.Damnation, face: 4, answer: _ => BattlefieldEffects.RefuseOption);
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData them = Unit(_them, new Position(30f, 10f));
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(me, them, isMelee: false, isSpell: false, 10f)), Is.EqualTo(-1));
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(me, isMelee: false, isSpell: false)), Is.EqualTo(-1));
            Assert.That(BattlefieldEffects.MoraleModifier(_context, me), Is.EqualTo(-1));
            BattlefieldEffects.RoundEnd(_context);
            Assert.That(BattlefieldEffects.MoraleModifier(_context, me), Is.EqualTo(0));
        }

        [Test]
        public async Task Damnation_Sacrifice_TakesD3Wounds()
        {
            // The first option is the unit with the most wounds left.
            Build(BattlefieldEffects.Damnation, face: 4,
                answer: r => ((FDG.StageResolution.Requests.StringSelectionRequest)r).ValidOptions.First());
            UnitData big = Unit(_me, new Position(10f, 10f), models: 5, name: "Big");
            Unit(_me, new Position(20f, 10f), models: 1, name: "Small");
            Unit(_them, new Position(40f, 10f), models: 3);
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(Alive(big), Is.EqualTo(3), "D3 on a 4 = 2 wounds");
            Assert.That(big.Tokens.HasToken(TokenType.Damned), Is.False);
        }

        [Test]
        public async Task MysticalFog_IgnoresWoundsOnA6()
        {
            Build(BattlefieldEffects.MysticalFog, face: 6);
            UnitData me = Unit(_me, new Position(10f, 10f), models: 3);
            await BattlefieldEffects.RoundStart(_context, 1);
            await CasualtyPresentation.ApplyUnitWounds(_context, me, 2f);
            Assert.That(Alive(me), Is.EqualTo(3));
            Build(BattlefieldEffects.MysticalFog, face: 5);
            me = Unit(_me, new Position(10f, 10f), models: 3);
            await BattlefieldEffects.RoundStart(_context, 1);
            await CasualtyPresentation.ApplyUnitWounds(_context, me, 2f);
            Assert.That(Alive(me), Is.EqualTo(1));
        }

        [Test]
        public async Task SinisterEchoes_ShakenTakesD3_AndARoutHurtsFriendsWithin6()
        {
            Build(BattlefieldEffects.SinisterEchoes, face: 6);   // D3 = 3
            UnitData routed = Unit(_me, new Position(10f, 10f), models: 2);
            UnitData near = Unit(_me, new Position(14f, 10f), models: 5);
            UnitData far = Unit(_me, new Position(30f, 10f), models: 5);
            await BattlefieldEffects.RoundStart(_context, 1);
            var friends = BattlefieldEffects.FriendsNear(_context, routed);
            Assert.That(friends, Is.EquivalentTo(new IUnit[] { near }));
            await BattlefieldEffects.OnRouted(_context, routed, friends);
            Assert.That(Alive(near), Is.EqualTo(2));
            Assert.That(Alive(far), Is.EqualTo(5));
            await BattlefieldEffects.OnShaken(_context, far);
            Assert.That(Alive(far), Is.EqualTo(2));
        }

        [Test]
        public async Task DeadlyVortex_OnA1_D3Wounds()
        {
            Build(BattlefieldEffects.DeadlyVortex, face: 1);   // die 1 -> D3 (a 1) = 1 wound
            UnitData me = Unit(_me, new Position(10f, 10f), models: 3);
            await BattlefieldEffects.RoundStart(_context, 1);
            await BattlefieldEffects.OnActivation(_context, me);
            Assert.That(Alive(me), Is.EqualTo(2));
        }

        [Test]
        public async Task Earthquake_EveryActivationTests_OnOpenGround()
        {
            Build(BattlefieldEffects.Earthquake, face: 1);
            UnitData me = Unit(_me, new Position(10f, 10f), models: 2);
            await BattlefieldEffects.RoundStart(_context, 1);
            var result = MovementExecutor.RollActivationDangerousTerrain(_context, me, Array.Empty<ITerrain>());
            Assert.That(result.AnyTested, Is.True, "the entire table counts as Dangerous");
            Build(BattlefieldEffects.HeavyStorm, face: 1);
            me = Unit(_me, new Position(10f, 10f), models: 2);
            await BattlefieldEffects.RoundStart(_context, 1);
            Assert.That(MovementExecutor.RollActivationDangerousTerrain(_context, me, Array.Empty<ITerrain>()).AnyTested, Is.False);
            Assert.That(BattlefieldEffects.CoverBonus(_context, inCoverPiece: false, 0), Is.EqualTo(1));
            Assert.That(BattlefieldEffects.CoverBonus(_context, inCoverPiece: true, 1), Is.EqualTo(2));
        }

        [Test]
        public async Task ZeroGravity_AnAdvanceDriftsOnStraightAhead()
        {
            Build(BattlefieldEffects.ZeroGravity, face: 6);   // D3 = 3
            UnitData me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.RoundStart(_context, 1);
            var starts = new List<Position> { new(6f, 10f) };   // it moved 4" along +x
            await BattlefieldEffects.Drift(_context, me, starts, rush: false);
            Assert.That(me.Models[0].Position.x, Is.EqualTo(13f).Within(0.01f));
            await BattlefieldEffects.OnActivation(_context, me);
            Assert.That(BattlefieldEffects.ChargeBonus(me), Is.EqualTo(12f), "2D6 of 6s");
        }
    }
}
