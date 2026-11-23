using FDG.Data;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Brutal Damage (Advanced Rulebooks 3.6.0, GF p48 / AoF p48).</summary>
    [TestFixture]
    public class BrutalDamageTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private void Build(EAdvancedRules rules, int die = 4, EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedDiceRoller(die), settings: settings);
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        private UnitData Unit(PlayerID owner, Position at, int models = 1, int tough = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.1f, at.z), _store);
                if (tough > 1) model.SetMaxWounds(tough);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4,
                modelBindings: bindings);
            _store.Create(unit);
            return unit;
        }

        [Test]
        public void OnlyGrimdarkFutureAndAgeOfFantasyPrintIt()
        {
            Assert.That(BrutalDamage.BooksHaveIt(EGameSystem.GrimdarkFuture), Is.True);
            Assert.That(BrutalDamage.BooksHaveIt(EGameSystem.AgeOfFantasy), Is.True);
            Assert.That(BrutalDamage.BooksHaveIt(EGameSystem.GFFirefight), Is.False);
            Assert.That(BrutalDamage.BooksHaveIt(EGameSystem.AoFSkirmish), Is.False);
            GameSettings s = GameSettings.GetDefault();
            s.GameSystem = EGameSystem.GFFirefight;
            s.AdvancedRules = EAdvancedRules.HeavyDamage;
            Assert.That(AdvancedRules.Enabled(s), Is.EqualTo(EAdvancedRules.None), "Firefight's book has no Brutal Damage");
        }

        [Test]
        public void HeavyDamage_FollowsTheBooksTable_ForTough6AndUp()
        {
            Build(EAdvancedRules.HeavyDamage);
            UnitData monster = Unit(_me, new Position(10f, 10f), tough: 24);
            ModelData m = (ModelData)monster.Models[0];
            Assert.That(BrutalDamage.HeavyPenalty(monster), Is.EqualTo((0f, 0, 0)));
            m.DealWounds(3);
            Assert.That(BrutalDamage.HeavyPenalty(monster), Is.EqualTo((-2f, 0, 0)));
            m.DealWounds(6);
            Assert.That(BrutalDamage.HeavyPenalty(monster), Is.EqualTo((-2f, -1, -1)), "9 wounds");
            m.DealWounds(12);
            Assert.That(BrutalDamage.HeavyPenalty(monster), Is.EqualTo((-6f, -2, -2)), "21 wounds");

            UnitData small = Unit(_me, new Position(20f, 10f), tough: 5);
            ((ModelData)small.Models[0]).DealWounds(4);
            Assert.That(BrutalDamage.HeavyPenalty(small), Is.EqualTo((0f, 0, 0)), "Tough(5) is not affected");

            Build(EAdvancedRules.None);
            monster = Unit(_me, new Position(10f, 10f), tough: 24);
            ((ModelData)monster.Models[0]).DealWounds(21);
            Assert.That(BrutalDamage.HeavyPenalty(_context.Settings, monster), Is.EqualTo((0f, 0, 0)), "off");
        }

        [Test]
        public void DirectionalDamage_FrontRearAndSide_FromTheShooters()
        {
            Build(EAdvancedRules.DirectionalDamage);
            UnitData big = Unit(_them, new Position(30f, 30f), tough: 12);
            ((ModelData)big.Models[0]).SetFacing(new Float2(0f, 1f)); // faces +z
            UnitData ahead = Unit(_me, new Position(30f, 40f), models: 3);
            UnitData behind = Unit(_me, new Position(30f, 20f), models: 3);
            UnitData beside = Unit(_me, new Position(40f, 30f), models: 1);
            Assert.That(BrutalDamage.FacingHit(ahead, big), Is.EqualTo(BrutalDamage.EFacing.Front));
            Assert.That(BrutalDamage.FacingHit(behind, big), Is.EqualTo(BrutalDamage.EFacing.Rear));
            Assert.That(BrutalDamage.FacingHit(beside, big), Is.EqualTo(BrutalDamage.EFacing.Side));
            UnitData squad = Unit(_them, new Position(50f, 30f), models: 5, tough: 1);
            Assert.That(BrutalDamage.FacingHit(ahead, squad), Is.EqualTo(BrutalDamage.EFacing.None), "only big units");

            var mods = ExtraActions.SaveModifiers(big, isMelee: false, isSpell: false, _context.Settings, behind);
            Assert.That(ExtraActions.Net(mods), Is.EqualTo(-1), "rear: -1 to Defense rolls");
            mods = ExtraActions.SaveModifiers(big, isMelee: false, isSpell: false, _context.Settings, ahead);
            Assert.That(ExtraActions.Net(mods), Is.EqualTo(1), "front: +1");
        }

        [Test]
        public void MishapsAndCriticals_RollOnePerDie()
        {
            Build(EAdvancedRules.HorribleMishaps | EAdvancedRules.BrutalCriticalHits, die: 3);
            Assert.That(BrutalDamage.Mishaps(_context, 4), Is.EqualTo(4), "1-3 wounds the shooter");
            Assert.That(BrutalDamage.Criticals(_context, 4), Is.EqualTo(0), "1-3: nothing");
            Build(EAdvancedRules.HorribleMishaps | EAdvancedRules.BrutalCriticalHits, die: 4);
            Assert.That(BrutalDamage.Mishaps(_context, 4), Is.EqualTo(0));
            Assert.That(BrutalDamage.Criticals(_context, 4), Is.EqualTo(4), "4-6: a wound each");
            Build(EAdvancedRules.None, die: 4);
            Assert.That(BrutalDamage.Criticals(_context, 4), Is.EqualTo(0), "off");
        }

        [Test]
        public void FriendlyFire_StraysOnOneToThree_IntoAFriendWithin3()
        {
            Build(EAdvancedRules.FriendlyFire, die: 2);
            UnitData shooter = Unit(_me, new Position(10f, 10f), models: 2);
            UnitData target = Unit(_them, new Position(30f, 10f), models: 2);
            UnitData friend = Unit(_me, new Position(30f, 12.5f), models: 1);
            Unit(_me, new Position(30f, 25f), models: 1); // too far
            List<UnitData> near = BrutalDamage.FriendsNear(_context, shooter, target);
            Assert.That(near, Is.EquivalentTo(new[] { friend }));
            Assert.That(BrutalDamage.StrayHits(_context, 5, near), Has.Count.EqualTo(5));
            Build(EAdvancedRules.FriendlyFire, die: 5);
            shooter = Unit(_me, new Position(10f, 10f));
            target = Unit(_them, new Position(30f, 10f));
            friend = Unit(_me, new Position(30f, 12.5f));
            Assert.That(BrutalDamage.StrayHits(_context, 5, BrutalDamage.FriendsNear(_context, shooter, target)), Is.Empty);
        }
    
        [Test]
        public async Task DeathZone_ATough12UnitDying_TestsEveryUnitWithin6()
        {
            Build(EAdvancedRules.DeathZone, die: 1);
            UnitData monster = Unit(_them, new Position(30f, 30f), tough: 12);
            UnitData near = Unit(_me, new Position(30f, 35f), models: 2);
            UnitData far = Unit(_me, new Position(30f, 45f), models: 2);
            ((ModelData)monster.Models[0]).DealWounds(12);
            await Stages.UnitDestructionNotifier.NotifyUnitDestroyed(_context, monster, null);
            Assert.That(near.Models.Count(m => m.GetIsAlive()), Is.EqualTo(0), "every die a 1: the near unit's wounds all go");
            Assert.That(far.Models.Count(m => m.GetIsAlive()), Is.EqualTo(2), "over 6\" away: no test");
        }
}
}
