using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Assault - <i>"Counts as both a ranged weapon and a melee weapon"</i> (OPR 3.6.0) - reaching the
    /// pool a melee actually swings. Reported by Kai, 2026-11-02, from a played Firefight game.
    ///
    /// <para>
    /// <b>This was already tested, and the test passed the whole time.</b>
    /// <c>Opr360RuleChangeTests.AssaultWeapon_AppearsInBothAttackPools</c> asks
    /// <c>unit.GetMeleeWeapons()</c>, which was correct. The melee itself does not call that: it calls
    /// <c>MeleeRangeUtilities.GetMeleeWeaponsFromModels</c>, a second copy of the same test that never
    /// learned about Assault. So the rule worked everywhere except where it was used, and a unit
    /// carrying an Assault weapon fought every melee of every game short of its real attacks.
    /// </para>
    /// <para>
    /// A <b>third</b> copy turned up in the AI's combat maths. All three carried a comment saying they
    /// mirrored each other; a comment is not a mechanism, and <see cref="WeaponPools"/> is. These tests
    /// go through the real stages rather than the predicate, because the predicate was never the thing
    /// that was wrong - the fifth time in this suite that "a test which calls the new code cannot tell
    /// whether the game reaches it" has cost something (#424, #429/#430, #433, #435, here).
    /// </para>
    /// </summary>
    [TestFixture]
    public class AssaultInMeleeTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new WoundTestContext(_store, new CapturingWoundRequester());
        }

        private static Weapon AssaultRifle() =>
            WithRule(new Weapon("Assault Rifle", rangeInches: 12f, attacks: 2, armorPenetration: 0),
                "Assault", CoreRuleCatalog.Assault);

        private static Weapon PlainRifle() =>
            new("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private static Weapon Blade() => new("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);

        private static Weapon BombRack() =>
            WithRule(new Weapon("Bomb Rack", rangeInches: 0f, attacks: 3, armorPenetration: 1),
                "Strafing", CoreRuleCatalog.Strafing);

        private static Weapon WithRule(Weapon weapon, string name, SpecialRuleDefinition definition)
        {
            weapon.AttachRuleDefinition(new ResolvedRule(name, definition));
            return weapon;
        }

        private DataBinding<UnitData> UnitWith(Position at, params Weapon[] weapons)
        {
            var model = new ModelData(0.75f, weapons.ToList(), at, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        /// <summary>
        /// Runs the real in-range stages, which is what fills the swing pool, and reports whether the
        /// attacker was sent down the <b>unarmed</b> exit.
        ///
        /// <para>
        /// That exit is the bug's exact shape rather than a detail of this fixture: an Assault-only unit
        /// charged, the stage found no melee weapon on any in-range model, and it logged "No in-range
        /// attacking model has a melee weapon; the melee resolves with no attacks from the attacker."
        /// Asserting the pool alone would miss a future change that filled the pool but still took this
        /// branch.
        /// </para>
        /// </summary>
        private async Task<(CombatActionContext Context, bool WentUnarmed)> Fight(
            DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var context = new CombatActionContext(_ctx, attacker, isMelee: true, isCharging: true);
            context.SetDefender(defender);

            bool unarmed = false;
            var attackers = new DetermineInRangeAttackersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            attackers.ToDetermineDefenders.Bind("done");
            attackers.OnNoAttackersInRange.Bind("none");
            attackers.OnAttackersInRangeUnarmed.Bind("unarmed");
            await attackers.Enter(context);
            unarmed = context.AvailableWeapons.Count == 0;
            if (unarmed) return (context, true);

            var defenders = new DetermineInRangeDefendersStage(_ctx, new NoOpLayer<ICombatActionContext>());
            defenders.ToChooseMeleeWeapons.Bind("done");
            await defenders.Enter(context);
            return (context, false);
        }

        /// <summary>
        /// The bug Kai saw: a unit whose only weapon is an Assault weapon charges, and has nothing to
        /// swing with. Before the fix the pool was empty.
        /// </summary>
        [Test]
        public async Task AnAssaultWeaponIsInThePoolTheMeleeActuallySwings()
        {
            DataBinding<UnitData> attacker = UnitWith(new Position(1f, 0f), AssaultRifle());
            DataBinding<UnitData> defender = UnitWith(new Position(0f, 0f), Blade());

            (CombatActionContext context, bool unarmed) = await Fight(attacker, defender);

            Assert.That(unarmed, Is.False,
                "the stage sent an Assault-carrying unit down the \"nothing to swing with\" exit - which "
                + "is the bug Kai saw: it charged and then made no attacks.");
            Assert.That(context.AvailableWeapons.Keys.Select(w => w.Name), Does.Contain("Assault Rifle"),
                "Assault counts as a melee weapon, so it must be offered when the unit fights.");
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.EqualTo(1));
        }

        /// <summary>
        /// And the defender's half, which is worse: the strike-back offer is skipped entirely when the
        /// defender has no melee weapons, so an Assault-only unit was not merely weakened - it could not
        /// fight back at all.
        /// </summary>
        [Test]
        public void AnAssaultOnlyDefenderCanStillStrikeBack()
        {
            DataBinding<UnitData> defender = UnitWith(new Position(0f, 0f), AssaultRifle());
            var models = new List<DataBinding<ModelData>>(defender.GetValue().ModelBindings);

            List<Weapon> pool = MeleeRangeUtilities.GetMeleeWeaponsFromModels(models);

            Assert.That(pool.Select(w => w.Name), Does.Contain("Assault Rifle"),
                "an empty pool here is what makes OfferStrikeBackStage skip the offer completely.");
        }

        /// <summary>A plain ranged weapon is still not swung - Assault is what changes that, not range.</summary>
        [Test]
        public async Task APlainRangedWeaponIsStillNotSwungInMelee()
        {
            DataBinding<UnitData> attacker = UnitWith(new Position(1f, 0f), PlainRifle());
            DataBinding<UnitData> defender = UnitWith(new Position(0f, 0f), Blade());

            (CombatActionContext context, bool unarmed) = await Fight(attacker, defender);

            Assert.That(unarmed, Is.True, "a rifle-only unit genuinely has nothing to swing with.");
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.Zero,
                "without Assault a rifle has no place in a melee.");
        }

        /// <summary>
        /// The mirror-image rule, which the same predicate owns: Strafing says <i>"may only be used in
        /// this way"</i>, and a bomb rack is range 0 - so without the exclusion it would be swung like a
        /// club. Folding Assault in must not have let this through.
        /// </summary>
        [Test]
        public async Task AStrafingWeaponIsStillNeverSwung()
        {
            DataBinding<UnitData> attacker = UnitWith(new Position(1f, 0f), BombRack());
            DataBinding<UnitData> defender = UnitWith(new Position(0f, 0f), Blade());

            (CombatActionContext context, bool unarmed) = await Fight(attacker, defender);

            Assert.That(unarmed, Is.True);
            Assert.That(context.AvailableWeapons.Values.Sum(), Is.Zero);
        }

        /// <summary>
        /// <b>Both</b> pools, which is the whole rule - putting it in melee must not have taken it out of
        /// shooting.
        /// </summary>
        [Test]
        public void AnAssaultWeaponIsStillShootable()
        {
            UnitData unit = UnitWith(new Position(0f, 0f), AssaultRifle()).GetValue();

            Assert.That(unit.GetRangedWeapons().Select(w => w.Name), Does.Contain("Assault Rifle"));
            Assert.That(unit.GetMeleeWeapons().Select(w => w.Name), Does.Contain("Assault Rifle"));
        }

        /// <summary>
        /// The invariant that stops a fourth copy appearing: every path that asks "does this weapon
        /// swing" must give the same answer. Three places asked independently and two of them had
        /// drifted, so this compares them directly on the awkward weapons rather than trusting that
        /// they all call the same method today.
        /// </summary>
        [Test]
        public void EveryWayOfAskingAgreesOnWhatSwings()
        {
            foreach (Weapon weapon in new[] { AssaultRifle(), PlainRifle(), Blade(), BombRack() })
            {
                DataBinding<UnitData> binding = UnitWith(new Position(0f, 0f), weapon);
                UnitData unit = binding.GetValue();

                bool viaUnit = unit.GetMeleeWeapons().Any(w => w.Name == weapon.Name);
                bool viaModels = MeleeRangeUtilities
                    .GetMeleeWeaponsFromModels(new List<DataBinding<ModelData>>(unit.ModelBindings))
                    .Any(w => w.Name == weapon.Name);
                bool viaPredicate = WeaponPools.CountsAsMelee(weapon);

                Assert.That(viaModels, Is.EqualTo(viaUnit),
                    $"\"{weapon.Name}\": the unit-wide pool and the melee's own pool disagree. That is "
                    + "exactly the split that hid Assault.");
                Assert.That(viaPredicate, Is.EqualTo(viaUnit), $"\"{weapon.Name}\": and the predicate.");
            }
        }
    }
}
