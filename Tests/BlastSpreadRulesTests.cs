using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - Blast(X) in the three skirmish-scale games, where the hits spread off the target.
    //
    // The two scales word the rule differently and the engine only had the big games' version:
    //
    //   GF / AoF 3.6.0:            "each hit is multiplied by X, where X is up to as many hits as models
    //                               in the target unit."  (all on the target)
    //   GFF / AoFS / GFSQ:         "each hit is multiplied by X. Hits must be split evenly between the
    //                               target and all enemy units within 3" of any model from the target."
    //
    // Which cap applies is a real fork, recorded in BlastSpreadRules and settled by Kai on 2026-10-31:
    // the 3.6.0 sentence read absolutely literally makes Blast WORSE than no Blast against a small lone
    // unit, while the older wording of the same rule caps the multiplier by the cluster's models and ships
    // worked examples that match. The cluster-capped reading is implemented. The literal one is not a
    // misreading on anyone's part, which is why both are written down.
    //
    // This fixture is the arithmetic and the geometry only. The dice live in the stages.
    [TestFixture]
    public class BlastSpreadRulesTests
    {
        private GameDataStore _store = null!;
        private TableState _table = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _table = new TableState(_store);
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // ── Who the hits spread to ──────────────────────────────────────────────────────────────────

        [Test]
        public void AnEnemyWithinThreeInches_IsCaught()
        {
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(20f, 20f));
            IUnit bystander = Unit(_them, "Bystander", new Position(20f, 22f));   // 2in, less base radii

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target),
                Is.EquivalentTo(new[] { bystander }));
        }

        [Test]
        public void AnEnemyFurtherOut_IsNotCaught()
        {
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(20f, 20f));
            Unit(_them, "Distant", new Position(20f, 26f));

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target), Is.Empty);
        }

        [Test]
        public void TheRangeIsMeasuredFromTheTarget_NotTheAttacker()
        {
            // "within 3" of any model from the TARGET". A unit hugging the firer is not in the blast.
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(30f, 0f));
            Unit(_them, "Next to the gunner", new Position(0f, 2f));

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target), Is.Empty);
        }

        [Test]
        public void TheAttackersOwnSideIsNeverCaught()
        {
            // "all ENEMY units within 3"" - a blast landing beside the firer's own squad does not scatter
            // into it. Worth its own test: the obvious implementation is "every other unit nearby".
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(20f, 20f));
            Unit(_us, "Our own squad", new Position(20f, 22f));

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target), Is.Empty);
        }

        [Test]
        public void ADeadUnitIsNotCaught()
        {
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(20f, 20f));
            IUnit corpse = Unit(_them, "Wiped", new Position(20f, 22f));
            foreach (IModel model in corpse.Models) model.DealWounds(model.TotalWounds);

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target), Is.Empty);
        }

        [Test]
        public void AUnitStillInReserveIsNotCaught()
        {
            // An Ambush unit waiting off-table sits at the unplaced (0,0) sentinel, which is within 3" of
            // anything else unplaced - so without this check a blast could scatter into a unit that is
            // not on the table at all.
            IUnit attacker = Unit(_us, "Gunner", new Position(0f, 0f));
            IUnit target = Unit(_them, "Squad", new Position(0f, 1f));
            IUnit waiting = Unit(_them, "Ambushers", new Position(0f, 2f));
            ReserveRules.PlaceInReserve((UnitData)waiting);

            Assert.That(BlastSpreadRules.UnitsInSpreadRange(_table, attacker, target), Is.Empty);
        }

        // ── How the hits are divided ────────────────────────────────────────────────────────────────

        [Test]
        public void WithNothingNearby_EveryHitStaysOnTheTarget()
        {
            // And the multiplier is capped by the target's own models, which is the big games' rule - so
            // a lone target plays identically in all five systems. 3 hits x Blast(3) on a 2-model squad
            // is 3 x 2 = 6, exactly what the engine did before this existed.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 2);

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, Array.Empty<IUnit>(), hits: 3f, authoredMultiplier: 3);

            Assert.That(spread.EffectiveMultiplier, Is.EqualTo(2), "capped at the target's 2 models");
            Assert.That(spread.TotalHits, Is.EqualTo(6f));
            Assert.That(spread.Target.Hits, Is.EqualTo(6f));
            Assert.That(spread.Spills, Is.False);
        }

        [Test]
        public void TheMultiplierIsCappedByTheWholeCluster_NotJustTheTarget()
        {
            // The difference the spread makes, and the reason it is not a nerf. The same 2-model squad,
            // now with two lone enemies beside it: the cluster is 4 models, so Blast(3) is no longer
            // trimmed to 2, and 3 hits become 9 instead of 6.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 2);
            IUnit a = Unit(_them, "Lone A", new Position(0f, 2f));
            IUnit b = Unit(_them, "Lone B", new Position(2f, 0f));

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, new[] { a, b }, hits: 3f, authoredMultiplier: 3);

            Assert.That(spread.EffectiveMultiplier, Is.EqualTo(3));
            Assert.That(spread.TotalHits, Is.EqualTo(9f));
            Assert.That(spread.Spills, Is.True);
        }

        [Test]
        public void HitsAreSplitEvenlyAcrossTheCluster()
        {
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 2);
            IUnit a = Unit(_them, "Lone A", new Position(0f, 2f));
            IUnit b = Unit(_them, "Lone B", new Position(2f, 0f));

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, new[] { a, b }, hits: 3f, authoredMultiplier: 3);

            Assert.That(spread.Target.Hits, Is.EqualTo(3f));
            Assert.That(spread.Others.Select(o => o.Hits), Is.EqualTo(new[] { 3f, 3f }).AsCollection);
        }

        [Test]
        public void TheRemainderGoesToTheTarget()
        {
            // 7 hits across 2 units is 3 each with 1 left over. It goes to the unit the player aimed at,
            // which should never come out of a blast worse off than a bystander.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 4);
            IUnit a = Unit(_them, "Lone A", new Position(0f, 2f), models: 3);

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, new[] { a }, hits: 7f, authoredMultiplier: 1);

            Assert.That(spread.TotalHits, Is.EqualTo(7f));
            Assert.That(spread.Target.Hits, Is.EqualTo(4f));
            Assert.That(spread.Others[0].Hits, Is.EqualTo(3f));
        }

        [Test]
        public void EveryHitIsAccountedFor_WhateverTheShape()
        {
            // The property that matters: a distribution must neither lose hits nor invent them. Stated
            // over a spread of shapes because the even-split-plus-remainder arithmetic is exactly where
            // an off-by-one hides, and a lost hit is a save die the defender never has to roll.
            foreach (int nearbyCount in new[] { 0, 1, 2, 3, 5 })
            {
                foreach (float hits in new[] { 1f, 2f, 3f, 5f, 8f })
                {
                    IUnit target = Unit(_them, $"T{nearbyCount}-{hits}", new Position(0f, 0f), models: 3);
                    var nearby = new List<IUnit>();
                    for (int i = 0; i < nearbyCount; i++)
                        nearby.Add(Unit(_them, $"N{nearbyCount}-{hits}-{i}", new Position(0f, 2f)));

                    BlastSpreadRules.Spread spread =
                        BlastSpreadRules.Distribute(target, nearby, hits, authoredMultiplier: 3);

                    float delivered = spread.Target.Hits + spread.Others.Sum(o => o.Hits);
                    Assert.That(delivered, Is.EqualTo(spread.TotalHits).Within(1e-4f),
                        $"{hits} hits, {nearbyCount} nearby: {delivered} delivered of {spread.TotalHits}");
                }
            }
        }

        [Test]
        public void AUnitThatWouldTakeNoHitsIsNotListed()
        {
            // 1 hit with no multiplier across 3 units gives each 0 - nobody should get a save prompt for
            // zero dice, and the leftover hit goes to the target.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f));
            IUnit a = Unit(_them, "Lone A", new Position(0f, 2f));
            IUnit b = Unit(_them, "Lone B", new Position(2f, 0f));

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, new[] { a, b }, hits: 1f, authoredMultiplier: 1);

            Assert.That(spread.Others, Is.Empty);
            Assert.That(spread.Target.Hits, Is.EqualTo(1f));
        }

        [Test]
        public void AMultiplierOfOneChangesNothingButStillSplits()
        {
            // Blast(1) is a real rating in the books. It must not multiply, but the spread clause has no
            // "only if X is over 1" condition, so the hits still share out.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 3);
            IUnit a = Unit(_them, "Lone A", new Position(0f, 2f));

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, new[] { a }, hits: 4f, authoredMultiplier: 1);

            Assert.That(spread.EffectiveMultiplier, Is.EqualTo(1));
            Assert.That(spread.TotalHits, Is.EqualTo(4f));
            Assert.That(spread.Target.Hits, Is.EqualTo(2f));
            Assert.That(spread.Others[0].Hits, Is.EqualTo(2f));
        }

        [Test]
        public void DeadModelsDoNotPadTheMultiplierCap()
        {
            // The cap counts models, and a casualty is not one. A squad shot down to its last model
            // stops boosting the blast that finishes it.
            IUnit target = Unit(_them, "Squad", new Position(0f, 0f), models: 5);
            foreach (IModel model in target.Models.Skip(1)) model.DealWounds(model.TotalWounds);

            BlastSpreadRules.Spread spread =
                BlastSpreadRules.Distribute(target, Array.Empty<IUnit>(), hits: 2f, authoredMultiplier: 4);

            Assert.That(spread.EffectiveMultiplier, Is.EqualTo(1), "one model left, so no multiplication");
            Assert.That(spread.TotalHits, Is.EqualTo(2f));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private IUnit Unit(PlayerID owner, string name, Position at, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(at.x + i * 0.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }
    }
}
