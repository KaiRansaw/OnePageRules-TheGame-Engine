using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch.Contexts;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Critical Hits (Advanced Rulebooks 3.6.0, GF: Firefight p45 / AoF: Skirmish p45).</summary>
    [TestFixture]
    public class CriticalHitsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private RecordingPresenter _beats = null!;

        private void Build(EAdvancedRules rules, int die, EGameSystem system = EGameSystem.GFFirefight)
        {
            _beats = new RecordingPresenter();
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedDiceRoller(die), presenter: _beats, settings: settings);
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
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4,
                modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        private static int Alive(IUnit u) => u.Models.Count(m => m.GetIsAlive());

        [Test]
        public void OnlyFirefightAndSkirmishPrintIt()
        {
            Assert.That(CriticalHits.BooksHaveIt(EGameSystem.GFFirefight), Is.True);
            Assert.That(CriticalHits.BooksHaveIt(EGameSystem.AoFSkirmish), Is.True);
            Assert.That(CriticalHits.BooksHaveIt(EGameSystem.GrimdarkFuture), Is.False, "GF has Brutal Damage's version");
            Assert.That(CriticalHits.BooksHaveIt(EGameSystem.GFStarQuest), Is.False);
            GameSettings s = GameSettings.GetDefault();
            s.GameSystem = EGameSystem.AgeOfFantasy;
            s.AdvancedRules = EAdvancedRules.SkirmishCriticalHits;
            Assert.That(AdvancedRules.Enabled(s), Is.EqualTo(EAdvancedRules.None));
            Assert.That(AdvancedRules.Chapters(EGameSystem.AoFSkirmish), Does.Contain(AdvancedRules.CriticalHitsChapter));
        }

        [TestCase(EGameSystem.GFFirefight, "Plasma Rifle", false, CriticalHits.EType.Energy)]
        [TestCase(EGameSystem.GFFirefight, "Heavy Flamethrower", false, CriticalHits.EType.Heat)]
        [TestCase(EGameSystem.GFFirefight, "Fusion Rifle", false, CriticalHits.EType.Heat)]
        [TestCase(EGameSystem.GFFirefight, "Missile Launcher", false, CriticalHits.EType.Explosive)]
        [TestCase(EGameSystem.GFFirefight, "Assault Rifle", false, CriticalHits.EType.GenericRanged)]
        [TestCase(EGameSystem.GFFirefight, "Energy Sword", true, CriticalHits.EType.Bladed)]
        [TestCase(EGameSystem.GFFirefight, "Thunder Hammer", true, CriticalHits.EType.Bludgeoning)]
        [TestCase(EGameSystem.GFFirefight, "Spear", true, CriticalHits.EType.GenericMelee)]
        [TestCase(EGameSystem.GFFirefight, "CCW", true, CriticalHits.EType.GenericMelee)]
        [TestCase(EGameSystem.AoFSkirmish, "Spear", true, CriticalHits.EType.Thrusting)]
        [TestCase(EGameSystem.AoFSkirmish, "Greatsword", true, CriticalHits.EType.Bladed)]
        [TestCase(EGameSystem.AoFSkirmish, "Longbow", false, CriticalHits.EType.Missile)]
        [TestCase(EGameSystem.AoFSkirmish, "Handgun", false, CriticalHits.EType.Gunpowder)]
        [TestCase(EGameSystem.AoFSkirmish, "Spell Bolt", false, CriticalHits.EType.GenericRanged)]
        [TestCase(EGameSystem.AoFSkirmish, "Arcane Rifle", false, CriticalHits.EType.Gunpowder)]
        public void WeaponTypes_ReadFromTheName(EGameSystem system, string name, bool melee, CriticalHits.EType expected)
        {
            Assert.That(CriticalHits.TypeOf(system, name, melee, typed: true), Is.EqualTo(expected));
            Assert.That(CriticalHits.TypeOf(system, name, melee, typed: false),
                Is.EqualTo(melee ? CriticalHits.EType.GenericMelee : CriticalHits.EType.GenericRanged), "generic tables");
        }

        [Test]
        public void Tables_FollowTheBook()
        {
            var ff = EGameSystem.GFFirefight;
            Assert.That(CriticalHits.Roll(CriticalHits.EType.GenericRanged, 2, ff), Has.Property("Name").EqualTo("Weak Spot").And.Property("Ap").EqualTo(4));
            Assert.That(CriticalHits.Roll(CriticalHits.EType.GenericRanged, 6, ff).MoraleWounds, Is.EqualTo(2));
            Assert.That(CriticalHits.Roll(CriticalHits.EType.GenericMelee, 1, ff).WoundIfUnblocked, Is.True);
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Bladed, 6, ff).MeleeWounds, Is.EqualTo(2), "Firefight: Sliced +2");
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Bladed, 6, EGameSystem.AoFSkirmish).MeleeWounds, Is.EqualTo(1), "Skirmish: Sliced +1");
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Heat, 3, ff).RerollSixes, Is.True);
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Explosive, 5, ff), Has.Property("Hits").EqualTo(2).And.Property("ShootHitPenalty").EqualTo(true));
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Thrusting, 6, EGameSystem.AoFSkirmish), Has.Property("Stabbed").EqualTo(true).And.Property("WoundIfUnblocked").EqualTo(true));
            Assert.That(CriticalHits.Roll(CriticalHits.EType.Energy, 6, ff).Shaken, Is.True);
        }

        [Test]
        public async Task ABladedCritical_DealsTwoHits_AndOnlyOnePerActivation()
        {
            Build(EAdvancedRules.CriticalHitsByWeaponType, die: 1); // table 1-3: Unprotected Area; Defense rolls of 1 fail
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData them = Unit(_them, new Position(11.5f, 10f), models: 5);
            var sword = new Weapon("Energy Sword", rangeInches: 0f, attacks: 2, armorPenetration: 0);

            CriticalHits.Dealt? dealt = await CriticalHits.Check(_context, me, them, sword, melee: true, sixesRolled: 1, hitRollNeeded: 4);
            Assert.That(dealt?.Effect.Name, Is.EqualTo("Unprotected Area"));
            Assert.That(Alive(them), Is.EqualTo(3), "2 hits without special rules, both unsaved");

            Assert.That(await CriticalHits.Check(_context, me, them, sword, melee: true, sixesRolled: 2, hitRollNeeded: 4), Is.Null,
                "only a single critical hit per shooting or melee");
            await CriticalHits.EndOfActivation(_context);
            Assert.That(await CriticalHits.Check(_context, me, them, sword, melee: true, sixesRolled: 1, hitRollNeeded: 4), Is.Not.Null,
                "a new activation, a new fight");
        }

        [Test]
        public async Task NoCritical_WhenItNeeded6s_OrRolledNo6_OrIsOff()
        {
            Build(EAdvancedRules.SkirmishCriticalHits, die: 1);
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData them = Unit(_them, new Position(20f, 10f));
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            Assert.That(await CriticalHits.Check(_context, me, them, rifle, false, sixesRolled: 1, hitRollNeeded: 6), Is.Null, "needed 6+");
            Assert.That(await CriticalHits.Check(_context, me, them, rifle, false, sixesRolled: 0, hitRollNeeded: 4), Is.Null, "no 6");
            Build(EAdvancedRules.None, die: 1);
            me = Unit(_me, new Position(10f, 10f));
            them = Unit(_them, new Position(20f, 10f));
            Assert.That(await CriticalHits.Check(_context, me, them, rifle, false, 1, 4), Is.Null, "off");
            Build(EAdvancedRules.SkirmishCriticalHits, die: 1, system: EGameSystem.GrimdarkFuture);
            me = Unit(_me, new Position(10f, 10f));
            them = Unit(_them, new Position(20f, 10f));
            Assert.That(await CriticalHits.Check(_context, me, them, rifle, false, 1, 4), Is.Null, "GF doesn't print this chapter");
        }

        [Test]
        public async Task Bludgeoned_PenalisesTheTarget_AndShakesItAtTheEnd()
        {
            Build(EAdvancedRules.CriticalHitsByWeaponType, die: 6);
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData them = Unit(_them, new Position(11.5f, 10f), models: 5);
            var hammer = new Weapon("Hammer", rangeInches: 0f, attacks: 2, armorPenetration: 0);
            await CriticalHits.Check(_context, me, them, hammer, melee: true, sixesRolled: 1, hitRollNeeded: 4);
            Assert.That(CriticalHits.HitPenalty(them, melee: true), Is.EqualTo(-1));
            Assert.That(CriticalHits.HitPenalty(them, melee: false), Is.EqualTo(0));
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(them, me, isMelee: true, isSpell: false, 1f)), Is.EqualTo(-1));
            await CriticalHits.EndOfActivation(_context);
            Assert.That(them.Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.True, "Shaken regardless of who won");
            Assert.That(CriticalHits.HitPenalty(them, melee: true), Is.EqualTo(0), "for this melee only");
        }

        [Test]
        public async Task Ricochet_HitsTheClosestOtherEnemyWithin6()
        {
            Build(EAdvancedRules.SkirmishCriticalHits, die: 4); // Ricochet; Defense 4+ saves a 4
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData target = Unit(_them, new Position(30f, 10f));
            UnitData close = Unit(_them, new Position(33f, 10f));
            Unit(_them, new Position(30f, 30f)); // too far
            Assert.That(CriticalHits.ClosestOtherEnemy(_context, me, target), Is.SameAs(close));
            Unit(_me, new Position(31f, 12f)); // a friend of the shooter is never the ricochet target
            Assert.That(CriticalHits.ClosestOtherEnemy(_context, me, target), Is.SameAs(close));
        }

        [Test]
        public void Stabbed_StripsTheRulesItFightsWith_NotItsSaves()
        {
            Build(EAdvancedRules.CriticalHitsByWeaponType, die: 1, system: EGameSystem.AoFSkirmish);
            UnitData unit = Unit(_me, new Position(10f, 10f));
            UnitData other = Unit(_them, new Position(11f, 10f));
            var dice = new DiceResults(new float[] { 0, 0, 0, 0, 0, 1 }, 1);
            var meleeHit = new HitRollCompleteContext(unit, other, dice, IsMelee: true);
            Assert.That(CriticalHits.LosesMeleeRules(unit, meleeHit), Is.False, "not stabbed yet");
            unit.Tokens.AddToken(Rules.Tokens.TokenDefinitionCatalog.Create(Rules.Foundation.TokenType.CritStabbed));
            Assert.That(CriticalHits.LosesMeleeRules(unit, meleeHit), Is.True);
            Assert.That(CriticalHits.LosesMeleeRules(unit, new HitRollCompleteContext(unit, other, dice, IsMelee: false)), Is.False);
            Assert.That(CriticalHits.LosesMeleeRules(unit, new MeleeResolutionContext(unit, other)), Is.True, "its Fear");
        }
    
        [Test]
        public async Task ACritical_ShowsItsWholeTable_WithTheRollPickedOut()
        {
            Build(EAdvancedRules.CriticalHitsByWeaponType, die: 4);
            UnitData me = Unit(_me, new Position(10f, 10f));
            UnitData them = Unit(_them, new Position(11.5f, 10f), models: 5);
            var sword = new Weapon("Energy Sword", rangeInches: 0f, attacks: 2, armorPenetration: 0);
            await CriticalHits.Check(_context, me, them, sword, melee: true, sixesRolled: 1, hitRollNeeded: 4);

            var beat = _beats.Beats.OfType<Presentation.Beats.CriticalHitBeat>().Single();
            Assert.That(beat.TableName, Is.EqualTo("Bladed"));
            Assert.That(beat.Die, Is.EqualTo(4));
            Assert.That(beat.Rows.Select(r => $"{r.Range} {r.Name}"), Is.EqualTo(new[] { "1-3 Unprotected Area", "4-5 Blade Storm", "6 Sliced" }));
            Assert.That(beat.Picked, Is.EqualTo(1));
            Assert.That(beat.Result, Does.Contain("AP(+4)").And.Contain("2 hits"));
            Assert.That(beat.AttackerUnit, Is.EqualTo(me.ID));
        }

        [Test]
        public void TheHovers_NameTheTable()
        {
            string sword = CriticalHits.WeaponTableText(EGameSystem.GFFirefight, "Energy Sword", ranged: false, melee: true, typed: true);
            Assert.That(sword, Does.Contain("BLADED").And.Contain("4-5 Blade Storm"));
            string generic = CriticalHits.WeaponTableText(EGameSystem.GFFirefight, "Energy Sword", ranged: false, melee: true, typed: false);
            Assert.That(generic, Does.Contain("GENERIC MELEE"));
            string assault = CriticalHits.WeaponTableText(EGameSystem.GFFirefight, "Flamer", ranged: true, melee: true, typed: true);
            Assert.That(assault, Does.Contain("HEAT").And.Contain("GENERIC MELEE"), "an Assault weapon: both tables");
            string lobby = CriticalHits.AllTablesText(EGameSystem.AoFSkirmish);
            Assert.That(lobby, Does.Contain("THRUSTING").And.Contain("GUNPOWDER").And.Not.Contain("HEAT"));
            Assert.That(lobby.All(ch => ch < 128), Is.True, "game text is ASCII-only");
            Assert.That(CriticalHits.AllTablesText(EGameSystem.GFFirefight).All(ch => ch < 128), Is.True);
        }
    }
}
