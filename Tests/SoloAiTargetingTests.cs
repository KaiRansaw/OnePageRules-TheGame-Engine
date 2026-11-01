using FDG.Ai;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - who the AI shoots and who it charges, from <b>Solo &amp; Co-Op Rules v3.6.0</b>.
    ///
    /// <para>
    /// "AI units always shoot at the <b>closest</b> valid target, <b>prioritizing units that haven't
    /// activated yet</b>. If the closest target is in Cover but there is another valid target in the open,
    /// the AI must prioritize the target in the open." Melee is the same minus the Cover clause.
    /// </para>
    /// <para>
    /// Two of those three were missing. Distance was <b>not in the old ordering at all</b> - it sorted by
    /// cover, then by how many of its own models could shoot - so the bot would pick a target across the
    /// table over one at 6". And nothing anywhere preferred a unit that had not activated yet, in shooting
    /// or in melee.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiTargetingTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _ai;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _store.Create(new TeamData(1, new List<PlayerID> { _player }));
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        private ITableState Table => _context.TableState;

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, float z)
        {
            var weapons = new List<Weapon> { new Weapon("Rifle", 24f, 2, 0), new Weapon("CCW", 0f, 2, 0) };
            var model = new ModelData(0.5f, weapons, new Position(x, z), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static void MarkActivated(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(
                new Token(TokenType.ActivatedThisRound, 1, new TokenClearTrigger.ManualOnly()));

        private void CoverAt(float x, float z, float radius = 3f) =>
            _store.Create(new TerrainData(ETerrainType.Cover, new CircularZone(new Float2(x, z), radius)));

        [Test]
        public void Shooting_PicksTheClosestTarget()
        {
            DataBinding<UnitData> shooter = Unit(_ai, "Raiders", 10f, 10f);
            DataBinding<UnitData> far = Unit(_player, "Far", 30f, 10f);
            DataBinding<UnitData> near = Unit(_player, "Near", 16f, 10f);

            IUnit? pick = SoloAiTargeting.PickShootTarget(Table, shooter.GetValue(),
                new[] { far.GetValue(), near.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Near"),
                "distance was missing from the old ordering entirely.");
        }

        [Test]
        public void Shooting_PrefersAUnitThatHasNotActivatedYet_EvenIfItIsFurther()
        {
            // The clause that makes the AI play the clock: shooting a unit before it acts can cost the
            // enemy a whole activation, shooting one that has already gone costs them nothing this round.
            DataBinding<UnitData> shooter = Unit(_ai, "Raiders", 10f, 10f);
            DataBinding<UnitData> nearButSpent = Unit(_player, "Spent", 14f, 10f);
            DataBinding<UnitData> fartherAndFresh = Unit(_player, "Fresh", 22f, 10f);
            MarkActivated(nearButSpent);

            IUnit? pick = SoloAiTargeting.PickShootTarget(Table, shooter.GetValue(),
                new[] { nearButSpent.GetValue(), fartherAndFresh.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Fresh"));
        }

        [Test]
        public void Shooting_PrefersTheTargetInTheOpen_OverACloserOneInCover()
        {
            // "If the closest target is in Cover but there is another valid target in the open, the AI must
            // prioritize the target in the open" - an explicit override of closest, so Cover sorts first.
            DataBinding<UnitData> shooter = Unit(_ai, "Raiders", 10f, 10f);
            DataBinding<UnitData> nearInCover = Unit(_player, "Hidden", 15f, 10f);
            DataBinding<UnitData> farInOpen = Unit(_player, "Exposed", 24f, 10f);
            CoverAt(15f, 10f);

            IUnit? pick = SoloAiTargeting.PickShootTarget(Table, shooter.GetValue(),
                new[] { nearInCover.GetValue(), farInOpen.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Exposed"));
        }

        [Test]
        public void Shooting_WithBothInCover_FallsBackToClosest()
        {
            DataBinding<UnitData> shooter = Unit(_ai, "Raiders", 10f, 10f);
            DataBinding<UnitData> near = Unit(_player, "Near", 15f, 10f);
            DataBinding<UnitData> far = Unit(_player, "Far", 24f, 10f);
            CoverAt(15f, 10f);
            CoverAt(24f, 10f);

            IUnit? pick = SoloAiTargeting.PickShootTarget(Table, shooter.GetValue(),
                new[] { far.GetValue(), near.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Near"),
                "the cover clause only breaks a tie between covered and open - it is not a preference for range.");
        }

        [Test]
        public void Melee_PicksTheClosest_AndPrefersAFreshTarget()
        {
            DataBinding<UnitData> charger = Unit(_ai, "Brutes", 10f, 10f);
            DataBinding<UnitData> nearSpent = Unit(_player, "Spent", 11f, 10f);
            DataBinding<UnitData> farFresh = Unit(_player, "Fresh", 13f, 10f);
            MarkActivated(nearSpent);

            IUnit? pick = SoloAiTargeting.PickChargeTarget(charger.GetValue(),
                new[] { nearSpent.GetValue(), farFresh.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Fresh"));
        }

        [Test]
        public void Melee_IgnoresCover()
        {
            // The book's cover clause is written for shooting, and Cover does not apply in melee anyway -
            // so a charger takes the closest even if it is the one behind a wall.
            DataBinding<UnitData> charger = Unit(_ai, "Brutes", 10f, 10f);
            DataBinding<UnitData> nearInCover = Unit(_player, "Hidden", 11f, 10f);
            DataBinding<UnitData> farInOpen = Unit(_player, "Exposed", 13f, 10f);
            CoverAt(11f, 10f);

            IUnit? pick = SoloAiTargeting.PickChargeTarget(charger.GetValue(),
                new[] { nearInCover.GetValue(), farInOpen.GetValue() });

            Assert.That(pick?.Name, Is.EqualTo("Hidden"));
        }

        [Test]
        public void NoCandidates_IsNoTarget()
        {
            DataBinding<UnitData> shooter = Unit(_ai, "Raiders", 10f, 10f);

            Assert.That(SoloAiTargeting.PickShootTarget(Table, shooter.GetValue(), Array.Empty<IUnit>()), Is.Null);
            Assert.That(SoloAiTargeting.PickChargeTarget(shooter.GetValue(), Array.Empty<IUnit>()), Is.Null);
        }

        [Test]
        public void InCover_ReadsTheUnitsOwnFooting()
        {
            DataBinding<UnitData> inside = Unit(_player, "Inside", 20f, 20f);
            DataBinding<UnitData> outside = Unit(_player, "Outside", 30f, 20f);
            CoverAt(20f, 20f, radius: 2f);

            Assert.That(SoloAiTargeting.InCover(Table, inside.GetValue()), Is.True);
            Assert.That(SoloAiTargeting.InCover(Table, outside.GetValue()), Is.False);
        }
    }
}
