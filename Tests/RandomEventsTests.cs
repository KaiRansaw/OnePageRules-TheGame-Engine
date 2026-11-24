using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Battlefield Effects - Random Events (Advanced Rulebooks 3.6.0: GF p40-41, GFF p37-38, AoF p40-41, AoFS p37-38).</summary>
    [TestFixture]
    public class RandomEventsTests
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

        private void Build(int face, EGameSystem system = EGameSystem.GrimdarkFuture, Func<object, object>? answer = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = EAdvancedRules.RandomEvents;
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: answer == null ? null : new Answer(answer));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        private UnitData Unit(PlayerID owner, Position at, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        [Test]
        public void TheTable_Has36Events_NamedPerGame()
        {
            int named = 0;
            for (int a = 1; a <= 6; a++)
                for (int b = 1; b <= 6; b++)
                {
                    Assert.That(BattlefieldEffects.EventName(a * 10 + b, EGameSystem.GrimdarkFuture), Is.Not.EqualTo("None"), $"{a}{b}");
                    Assert.That(BattlefieldEffects.EventText(a * 10 + b, EGameSystem.GrimdarkFuture), Is.Not.Empty, $"{a}{b}");
                    named++;
                }
            Assert.That(named, Is.EqualTo(36));
            Assert.That(BattlefieldEffects.EventName(21, EGameSystem.AgeOfFantasy), Is.EqualTo("Magic Anomaly"));
            Assert.That(BattlefieldEffects.EventName(33, EGameSystem.AoFSkirmish), Is.EqualTo("Pied Piper"));
            Assert.That(BattlefieldEffects.EventName(65, EGameSystem.GFFirefight), Is.EqualTo("Anti-Grav Boots"));
            Assert.That(BattlefieldEffects.EventText(31, EGameSystem.AoFSkirmish), Does.Contain("Tough(9)"));
            Assert.That(BattlefieldEffects.EventText(23, EGameSystem.GFFirefight), Does.Contain("D3+4"));
            Assert.That(AdvancedRules.Catalog.Any(o => o.Flag == EAdvancedRules.RandomEvents && o.Applies(EGameSystem.GrimdarkFuture)), Is.True);
        }

        [Test]
        public async Task BelowA5_NoEventFires()
        {
            Build(face: 4);
            Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.RollEvent(_context);
            Assert.That(BattlefieldEffects.State(_context)?.RoundEvent ?? 0, Is.EqualTo(0));
        }

        [Test]
        public async Task AFive_FiresTheTwoDiceEvent()
        {
            Build(face: 5);   // 5+ fires; then 5 and 5 = 55, Get back in the Fight!
            Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.RollEvent(_context);
            Assert.That(BattlefieldEffects.State(_context)!.RoundEvent, Is.EqualTo(55));
        }

        [Test]
        public async Task HeavyFog_Minus1Shooting_ThisRoundOnly()
        {
            Build(face: 4);
            UnitData me = Unit(_me, new Position(10f, 10f)), them = Unit(_them, new Position(30f, 10f));
            await BattlefieldEffects.Fire(_context, 24);
            Assert.That(BattlefieldEffects.EventHitModifiers(_context, me, them, melee: false).Sum(m => m.Delta), Is.EqualTo(-1));
            Assert.That(BattlefieldEffects.EventHitModifiers(_context, me, them, melee: true), Is.Empty);
            await BattlefieldEffects.EventsRoundEnd(_context);
            Assert.That(BattlefieldEffects.EventHitModifiers(_context, me, them, melee: false), Is.Empty);
        }

        [Test]
        public async Task BrokenMorale_IsForTheGame()
        {
            Build(face: 4);
            Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 13);
            UnitData hit = _store.GetAllValues<UnitData>().Single(u => u.Tokens.HasToken(TokenType.BrokenMorale));
            await BattlefieldEffects.EventsRoundEnd(_context);
            Assert.That(BattlefieldEffects.EventMoraleModifier(_context, hit), Is.EqualTo(-1));
        }

        [Test]
        public async Task TheEndIsNigh_FailsEverything_ButNotInFirefight()
        {
            Build(face: 4);
            UnitData me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 25);
            Assert.That(BattlefieldEffects.MoraleOverride(_context, me), Is.False);
            var outcome = await MoraleUtilities.TakeMoraleTest(_context, me, 2, EMoraleTrigger.OtherSource);
            Assert.That(outcome.Passed, Is.False);

            Build(face: 4, system: EGameSystem.GFFirefight);
            me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 25);
            Assert.That(BattlefieldEffects.MoraleOverride(_context, me), Is.Null, "\"taken at the end of the round\" - none are, in Firefight");
        }

        [Test]
        public async Task LowSupplies_StopsShooting_ForTheMission()
        {
            Build(face: 4);   // D3 = 2 units
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(30f, 10f));
            await BattlefieldEffects.Fire(_context, 12);
            Assert.That(_store.GetAllValues<UnitData>().Count(u => u.Tokens.HasToken(TokenType.LowSupplies)), Is.EqualTo(2));
            await BattlefieldEffects.EventsRoundEnd(_context);
            Assert.That(_store.GetAllValues<UnitData>().Count(u => u.Tokens.HasToken(TokenType.LowSupplies)), Is.EqualTo(2), "until the end of the mission");
        }

        [Test]
        public async Task ChippedWeapons_EndsWithTheRound()
        {
            Build(face: 4);
            Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 15);
            Assert.That(_store.GetAllValues<UnitData>().Count(u => u.Tokens.HasToken(TokenType.ChippedWeapons)), Is.EqualTo(1));
            await BattlefieldEffects.EventsRoundEnd(_context);
            Assert.That(_store.GetAllValues<UnitData>().Count(u => u.Tokens.HasToken(TokenType.ChippedWeapons)), Is.EqualTo(0));
        }

        [Test]
        public async Task FirstTimeEvents_GoOffOnce()
        {
            Build(face: 4);
            UnitData me = Unit(_me, new Position(10f, 10f)), them = Unit(_them, new Position(12f, 10f));
            await BattlefieldEffects.Fire(_context, 42);
            Assert.That(await BattlefieldEffects.AfterHitRoll(_context, me, them, melee: false, ones: 0, sixes: 2), Is.EqualTo(0), "Berserker is melee only");
            Assert.That(await BattlefieldEffects.AfterHitRoll(_context, me, them, melee: true, ones: 0, sixes: 2), Is.EqualTo(2));
            Assert.That(await BattlefieldEffects.AfterHitRoll(_context, me, them, melee: true, ones: 0, sixes: 3), Is.EqualTo(0), "only the first time");
        }

        [Test]
        public async Task GodlyBlessing_IsAp1InMelee()
        {
            Build(face: 4);
            Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 53);
            Assert.That(BattlefieldEffects.MeleeApBonus(_context), Is.EqualTo(1));
            await BattlefieldEffects.EventsRoundEnd(_context);
            Assert.That(BattlefieldEffects.MeleeApBonus(_context), Is.EqualTo(0));
        }

        [Test]
        public async Task TheBoots_AddMovement_ForTheMission()
        {
            Build(face: 4);   // D3 = 2, 2D3 = 4
            UnitData me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 65);
            Assert.That(BattlefieldEffects.EventMoveChange(_context, me), Is.EqualTo((2f, 4f)));
        }

        [Test]
        public async Task ZombieHorde_MovesTowardTheClosestUnit_AtRoundEnd()
        {
            Build(face: 4);
            UnitData me = Unit(_me, new Position(2f, 2f));
            await BattlefieldEffects.Fire(_context, 31);
            EventMarker m = BattlefieldEffects.State(_context)!.Markers.Single();
            float before = MathF.Sqrt((m.X - 2f) * (m.X - 2f) + (m.Z - 2f) * (m.Z - 2f));
            await BattlefieldEffects.EventsRoundEnd(_context);
            m = BattlefieldEffects.State(_context)!.Markers.Single();
            float after = MathF.Sqrt((m.X - 2f) * (m.X - 2f) + (m.Z - 2f) * (m.Z - 2f));
            Assert.That(after, Is.LessThan(before - 1f));
            Assert.That(me.GetIsAlive(), Is.True);
        }

        [Test]
        public async Task ShimmeringMist_GivesCover_InItsQuarter()
        {
            Build(face: 4);
            UnitData me = Unit(_me, new Position(10f, 10f));
            await BattlefieldEffects.Fire(_context, 56);
            ZoneRect q = BattlefieldEffects.State(_context)!.MistQuarters.Single();
            bool inside = q.Contains(me.Models.First().Position);
            Assert.That(BattlefieldEffects.CoverBonus(_context, me, inCoverPiece: false, bonus: 0), Is.EqualTo(inside ? 1 : 0));
        }
    
        [Test]
        public void AUnitWithNoLivingModels_MovesNowhere()
        {
            // Sweep 3.16: an Aircraft killed by a terrain effect as it activated still tried its forced flight and
            // the empty path list crashed the game.
            Assert.That(FDG.Stages.MovementUtilities.GetMaxMoveDistance(new List<FDG.StageResolution.Requests.ModelMoveEntry>()), Is.EqualTo(0f));
        }
    }
}
