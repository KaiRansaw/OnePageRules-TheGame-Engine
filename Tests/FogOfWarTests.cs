using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Fog of War (Advanced Rulebooks 3.6.0: GF p46, GFF p43, AoF p46, AoFS p43).</summary>
    [TestFixture]
    public class FogOfWarTests
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

        private void Build(int face, EAdvancedRules rules, EGameSystem system = EGameSystem.GrimdarkFuture, Func<object, object>? answer = null)
        {
            GameWideConstants.ConfigureTable(system);
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: answer == null ? null : new Answer(answer));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        [TearDown]
        public void Reset() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private UnitData Unit(PlayerID owner, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        [Test]
        public void TheChapter_IsInAllFourGames_NotQuest()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.Chapters(s), Does.Contain(AdvancedRules.FogOfWarChapter), s.ToString());
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.FogOfWarChapter));
            Assert.That(AdvancedRules.In(AdvancedRules.FogOfWarChapter, EGameSystem.GrimdarkFuture).Count(), Is.EqualTo(4));
            Assert.That((long)EAdvancedRules.EbbAndFlow & (long)BattlefieldEffects.PickMask, Is.EqualTo(0), "clear of the Special Condition pick bits");
        }

        [Test]
        public void TheTables_AreTheBooks()
        {
            Assert.That(new[] { 1, 2, 3, 4, 5 }.Select(FogOfWar.PassOn), Is.EqualTo(new int?[] { 5, 4, 3, 2, null }));
            Assert.That(new[] { 4, 5, 6, 7 }.Select(FogOfWar.ContinueOn), Is.EqualTo(new[] { 4, 5, 6, 6 }));
            Assert.That(FogOfWar.Sections(EGameSystem.AgeOfFantasy), Is.EqualTo(3));
            Assert.That(FogOfWar.Sections(EGameSystem.GFFirefight), Is.EqualTo(2));
            Assert.That(Enumerable.Range(1, 6).Select(d => FogOfWar.SectionFromDie(EGameSystem.GrimdarkFuture, d)), Is.EqualTo(new[] { 1, 1, 2, 2, 3, 3 }));
            Assert.That(Enumerable.Range(1, 6).Select(d => FogOfWar.SectionFromDie(EGameSystem.AoFSkirmish, d)), Is.EqualTo(new[] { 1, 1, 1, 2, 2, 2 }));
        }

        [Test]
        public async Task ProlongedBattle_ASuccessAddsARound_AFailureEndsIt()
        {
            Build(face: 4, EAdvancedRules.ProlongedBattle);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4));
            await FogOfWar.ProlongedRoll(_context, 3, 4);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4), "nothing before round 4");
            await FogOfWar.ProlongedRoll(_context, 4, 4);   // 4 on a 4+: continue
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(5));
            await FogOfWar.ProlongedRoll(_context, 5, 5);   // 4 on a 5+: the game ends
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(5));
            Assert.That(FogOfWar.State(_store)!.Ended, Is.True);

            Build(face: 4, EAdvancedRules.None);
            await FogOfWar.ProlongedRoll(_context, 4, 4);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4), "off");
        }

        [Test]
        public async Task CombatHesitation_RollsAndPasses_OnlyWhenBehind()
        {
            Build(face: 4, EAdvancedRules.CombatHesitation, answer: _ => true);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 1, opponentHasMore: true), Is.False, "4 doesn't make 5+");
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: true), Is.True, "4 makes 4+");
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: false), Is.False, "not behind");
            Build(face: 1, EAdvancedRules.CombatHesitation, answer: _ => true);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 5, opponentHasMore: true), Is.True, "no roll after round 4");
            Build(face: 6, EAdvancedRules.CombatHesitation, answer: _ => false);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: true), Is.False, "declined");
        }

        [Test]
        public void ShiftingFocus_PicksTheRolledSection_ThenMovesOn()
        {
            Build(face: 3, EAdvancedRules.ShiftingFocus);   // D3 from a 3 = section 2
            float w = HordePlacement.TableWidth;
            UnitData left = Unit(_me, new Position(w * 0.1f, 6f)), middle = Unit(_me, new Position(w * 0.5f, 6f));
            var focus = FogOfWar.Focus(_context, _me, new List<IUnit> { left, middle });
            Assert.That(focus!.Value.Section, Is.EqualTo(2));
            Assert.That(focus.Value.Units, Is.EquivalentTo(new[] { middle.ID }));

            Build(face: 3, EAdvancedRules.ShiftingFocus);
            UnitData only = Unit(_me, new Position(w * 0.9f, 6f));
            focus = FogOfWar.Focus(_context, _me, new List<IUnit> { only });
            Assert.That(focus!.Value.Section, Is.EqualTo(3), "section 2 empty - on to 3");

            Build(face: 3, EAdvancedRules.None);
            Assert.That(FogOfWar.Focus(_context, _me, new List<IUnit> { Unit(_me, new Position(1f, 1f)) }), Is.Null);
        }
    }
}
