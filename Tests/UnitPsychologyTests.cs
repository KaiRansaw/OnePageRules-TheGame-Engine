using FDG.Data;
using FDG.Missions;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Unit Psychology (Advanced Rulebooks 3.6.0, GF p47 / GFF p44 / AoF p47 / AoFS p44).</summary>
    [TestFixture]
    public class UnitPsychologyTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private void Build(EAdvancedRules rules, int die = 4)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedDiceRoller(die), settings: settings);
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        private UnitData Unit(PlayerID owner, Position at, int points, int models = 3)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4,
                modelBindings: bindings) { PointCost = points };
            _store.Create(unit);
            return unit;
        }

        private Dictionary<IModel, IUnit> ModelMap() =>
            _context.TableState.Units.Objects.SelectMany(u => u.Models.Select(m => (m, u))).ToDictionary(p => p.m, p => p.u);

        [Test]
        public void Intimidation_FiftyPercentMorePoints_Seizes_HalfStrengthCountsHalf()
        {
            Build(EAdvancedRules.Intimidation);
            var objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            Unit(_me, new Position(35f, 24.5f), points: 150);
            UnitData theirs = Unit(_them, new Position(35f, 23.0f), points: 100);
            var near = new HashSet<PlayerID> { _me, _them };

            Assert.That(UnitPsychology.Intimidate(_context, objective, ModelMap(), near), Is.EqualTo(_me), "150 vs 100: exactly 50% more");

            Build(EAdvancedRules.Intimidation);
            objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            Unit(_me, new Position(35f, 24.5f), points: 149);
            Unit(_them, new Position(35f, 23.0f), points: 100);
            Assert.That(UnitPsychology.Intimidate(_context, objective, ModelMap(), new HashSet<PlayerID> { _me, _them }), Is.Null,
                "149 vs 100 is not 50% more");

            // The book's example: 200 pts at half strength (worth 100) against 150 - the 150 seizes it.
            Build(EAdvancedRules.Intimidation);
            objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            UnitData mine = Unit(_me, new Position(35f, 24.5f), points: 200, models: 4);
            foreach (ModelData m in mine.Models.Take(2).Cast<ModelData>()) m.DealWounds(m.TotalWounds);
            Unit(_them, new Position(35f, 23.0f), points: 150);
            Assert.That(UnitPsychology.Intimidate(_context, objective, ModelMap(), new HashSet<PlayerID> { _me, _them }), Is.EqualTo(_them));

            // Off: no seizing.
            Build(EAdvancedRules.None);
            objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            Unit(_me, new Position(35f, 24.5f), points: 500);
            Unit(_them, new Position(35f, 23.0f), points: 100);
            Assert.That(UnitPsychology.Intimidate(_context, objective, ModelMap(), new HashSet<PlayerID> { _me, _them }), Is.Null);
        }

        [Test]
        public async Task Panic_OnAOne_PanicsInsteadOfShaken_OtherwiseNot()
        {
            Build(EAdvancedRules.Panic, die: 1);
            UnitData unit = Unit(_me, new Position(10f, 10f), 100);
            Assert.That(await UnitPsychology.TryPanic(_context, unit), Is.True);
            Assert.That(unit.Tokens.HasToken(TokenType.Panicking), Is.True);

            Build(EAdvancedRules.Panic, die: 2);
            unit = Unit(_me, new Position(10f, 10f), 100);
            Assert.That(await UnitPsychology.TryPanic(_context, unit), Is.False);

            Build(EAdvancedRules.None, die: 1);
            unit = Unit(_me, new Position(10f, 10f), 100);
            Assert.That(await UnitPsychology.TryPanic(_context, unit), Is.False, "off");
        }

        [Test]
        public void Frenzy_OnAOne_AfterDestroyingAUnit_ByShootingOrMelee()
        {
            Build(EAdvancedRules.Frenzy, die: 1);
            UnitData killer = Unit(_me, new Position(10f, 10f), 100);
            UnitData dead = Unit(_them, new Position(20f, 10f), 100);
            UnitPsychology.UnitDestroyed(_context, dead, killer, EKillCause.Spell);
            Assert.That(UnitPsychology.IsFrenzied(killer), Is.False, "spells don't count");
            UnitPsychology.UnitDestroyed(_context, dead, killer, EKillCause.Shooting);
            Assert.That(UnitPsychology.IsFrenzied(killer), Is.True);

            Build(EAdvancedRules.Frenzy, die: 2);
            killer = Unit(_me, new Position(10f, 10f), 100);
            dead = Unit(_them, new Position(20f, 10f), 100);
            UnitPsychology.UnitDestroyed(_context, dead, killer, EKillCause.Melee);
            Assert.That(UnitPsychology.IsFrenzied(killer), Is.False);
        }

        [Test]
        public void Suppression_FriendsWithin6_GetAMarker_AndEachMarkerCosts()
        {
            Build(EAdvancedRules.Suppression);
            UnitData dead = Unit(_me, new Position(10f, 10f), 100);
            UnitData near = Unit(_me, new Position(10f, 15f), 100);
            UnitData far = Unit(_me, new Position(10f, 30f), 100);
            UnitData enemy = Unit(_them, new Position(13f, 10f), 100);
            UnitPsychology.UnitDestroyed(_context, dead, enemy, EKillCause.Shooting);
            Assert.That(UnitPsychology.Markers(near), Is.EqualTo(1));
            Assert.That(UnitPsychology.Markers(far), Is.EqualTo(0), "over 6\" away");
            Assert.That(UnitPsychology.Markers(enemy), Is.EqualTo(0), "only friendly units");

            UnitPsychology.AddMarker(_context, near, "test");
            Assert.That(UnitPsychology.HitModifier(near, isSpell: false), Is.EqualTo(-2));
            Assert.That(UnitPsychology.HitModifier(near, isSpell: true), Is.EqualTo(0));
            Assert.That(UnitPsychology.MoraleModifier(near), Is.EqualTo(-2));
            Assert.That(UnitPsychology.MoveChange(near), Is.EqualTo((-2f, -4f)));
            Assert.That(ExtraActions.MoraleModifier(near), Is.EqualTo(-2), "folded into the morale test's modifiers");

            UnitPsychology.Rested(_context, near);
            UnitPsychology.RoundEnd(_context);
            Assert.That(UnitPsychology.Markers(near), Is.EqualTo(0), "Rest removes all markers at the end of the round");
        }

        [Test]
        public void Suppression_Off_GivesNoMarkers()
        {
            Build(EAdvancedRules.None);
            UnitData unit = Unit(_me, new Position(10f, 10f), 100);
            UnitPsychology.AddMarker(_context, unit, "test");
            Assert.That(UnitPsychology.Markers(unit), Is.EqualTo(0));
        }

        [Test]
        public void AdvancedRules_NotInTheQuestGames()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = EAdvancedRules.Intimidation | EAdvancedRules.Suppression;
            Assert.That(AdvancedRules.Enabled(settings), Is.EqualTo(settings.AdvancedRules));
            settings.GameSystem = EGameSystem.GFStarQuest;
            Assert.That(AdvancedRules.Enabled(settings), Is.EqualTo(EAdvancedRules.None));
        }
    }
}
