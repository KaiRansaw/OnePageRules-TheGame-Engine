using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// A downed Star Quest hero has to stay REACHABLE: Revive, Resurrect, Opulent Revival and the
    /// Reviving Kit all find their target through <see cref="QuestActions.UnconsciousHeroes"/>.
    ///
    /// <para>
    /// The regression these guard (QA 3.24, 9.20/9.21): distance between units is measured between
    /// LIVING models only, so that a corpse lying where it fell cannot put a unit in shooting or melee
    /// range. An unconscious hero has no living models at all, so that measure returned +infinity and
    /// the candidate list came back empty every time - the skill answered "No unconscious hero within
    /// 3"" with a hero down at the reviver's feet, and there was no way to revive anyone.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestReviveReachTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _heroes = default!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _heroes = new PlayerID(Guid.NewGuid());
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        /// <summary>A one-model Star Quest hero at a given spot. Bind it into an army with PutInArmy.</summary>
        private DataBinding<UnitData> Hero(string name, float x, float z)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(x, z), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_heroes, name, quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.QuestHeroJson = new QuestHero { Name = name, Power = 3 }.ToJson();
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private void PutInArmy(params DataBinding<UnitData>[] units)
        {
            _store.Create(new ArmyData(_heroes, new List<DataBinding<UnitData>>(units)));
        }

        /// <summary>Knock a hero out: every model dead, but still lying where it fell.</summary>
        private static void KnockOut(DataBinding<UnitData> hero)
        {
            foreach (IModel model in hero.GetValue().Models)
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
        }

        [Test]
        public void DownedHeroWithinRange_IsOfferedToTheReviver()
        {
            DataBinding<UnitData> reviver = Hero("Reviver", 10f, 10f);
            DataBinding<UnitData> downed = Hero("Downed", 11.5f, 10f);   // ~1.5" centre to centre, well inside 3"
            PutInArmy(reviver, downed);
            KnockOut(downed);

            List<UnitData> found = QuestActions.UnconsciousHeroes(_context, reviver.GetValue(), 3f);

            Assert.That(found, Has.Count.EqualTo(1), "a hero down 1.5\" away must be revivable");
            Assert.That(found[0].Name, Is.EqualTo("Downed"));
        }

        [Test]
        public void DownedHeroOutOfRange_IsNotOffered()
        {
            DataBinding<UnitData> reviver = Hero("Reviver", 10f, 10f);
            DataBinding<UnitData> downed = Hero("Downed", 30f, 10f);     // 20" away
            PutInArmy(reviver, downed);
            KnockOut(downed);

            Assert.That(QuestActions.UnconsciousHeroes(_context, reviver.GetValue(), 3f), Is.Empty);
        }

        [Test]
        public void StandingHero_IsNotOffered()
        {
            DataBinding<UnitData> reviver = Hero("Reviver", 10f, 10f);
            DataBinding<UnitData> upright = Hero("Upright", 11.5f, 10f);
            PutInArmy(reviver, upright);
            // Nobody is knocked out, so there is nobody to revive.

            Assert.That(QuestActions.UnconsciousHeroes(_context, reviver.GetValue(), 3f), Is.Empty);
        }

        /// <summary>A hero that never deployed parks at the origin - it is off-table, not on the floor.</summary>
        [Test]
        public void DownedHeroAtTheOrigin_IsNotOffered()
        {
            DataBinding<UnitData> reviver = Hero("Reviver", 0.5f, 0.5f);
            DataBinding<UnitData> offTable = Hero("OffTable", 0f, 0f);
            PutInArmy(reviver, offTable);
            KnockOut(offTable);

            Assert.That(QuestActions.UnconsciousHeroes(_context, reviver.GetValue(), 3f), Is.Empty);
        }

        /// <summary>The skill's own availability gate is what the player sees greyed out or not.</summary>
        [Test]
        public void ReviveSkill_BecomesAvailableWhenAHeroGoesDownNearby()
        {
            DataBinding<UnitData> reviver = Hero("Reviver", 10f, 10f);
            DataBinding<UnitData> other = Hero("Other", 11.5f, 10f);
            PutInArmy(reviver, other);

            QuestSkill revive = QuestSkill.Find(QuestSkill.ReviveName)!;
            Assert.That(revive, Is.Not.Null);
            Assert.That(revive.Unavailable(_context, reviver.GetValue()), Is.Not.Null,
                "with nobody down, Revive is unavailable");

            KnockOut(other);

            Assert.That(revive.Unavailable(_context, reviver.GetValue()), Is.Null,
                "with a hero down 1.5\" away, Revive must be available");
        }
    }
}
