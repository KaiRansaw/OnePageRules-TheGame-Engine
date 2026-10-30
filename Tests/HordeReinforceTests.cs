using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #409 - Reinforcing Units, new in OPR 3.6.0 Horde Mode:
    // "Defenders may spend points to reinforce units that are down to half or less of their total size or
    //  Tough value (for single-model units) and that are within 9" of a seized objective. Spend half as
    //  many points as the unit's total point value (rounding up), and then restore up to half of its
    //  total models or wounds (for single-model units)."
    //
    // The range half of the eligibility test is the Horde shop's existing one (HordeShop.UpgradableUnits);
    // what is new, and what these pin, is the half-strength gate, the price and the restoration.
    [TestFixture]
    public class HordeReinforceTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // ── price ───────────────────────────────────────────────────────────────────────────────────

        [TestCase(100, 50)]
        [TestCase(0, 0)]
        [TestCase(1, 1)]      // rounds UP
        [TestCase(155, 78)]   // 77.5 -> 78
        public void Cost_IsHalfThePointValue_RoundingUp(int pointCost, int expected)
        {
            Assert.That(HordeReinforce.Cost(pointCost), Is.EqualTo(expected));
        }

        // ── the half-strength gate ──────────────────────────────────────────────────────────────────

        [TestCase(10, 6, false)]  // 6 of 10 is over half
        [TestCase(10, 5, true)]   // exactly half qualifies - the rule says "half or LESS"
        [TestCase(10, 1, true)]
        [TestCase(5, 3, false)]
        [TestCase(5, 2, true)]
        public void MultiModelUnit_MeasuresInModels(int total, int living, bool eligible)
        {
            Assert.That(HordeReinforce.IsAtHalfStrength(total, living, totalWounds: total, remainingWounds: living),
                Is.EqualTo(eligible));
        }

        [TestCase(12f, 7f, false)]
        [TestCase(12f, 6f, true)]
        [TestCase(12f, 1f, true)]
        public void SingleModelUnit_MeasuresInWounds(float total, float remaining, bool eligible)
        {
            Assert.That(HordeReinforce.IsAtHalfStrength(1, 1, total, remaining), Is.EqualTo(eligible));
        }

        // ── how much comes back ─────────────────────────────────────────────────────────────────────

        [TestCase(10, 3, 5)]   // up to half its total: 10/2 = 5, back to 8
        [TestCase(10, 5, 5)]
        [TestCase(10, 1, 5)]   // still 5, not 9 - "up to half of its TOTAL models"
        [TestCase(3, 1, 1)]    // half of 3 rounds down
        [TestCase(10, 9, 1)]   // never more than it is missing
        public void ModelsRestored_IsHalfTheTotal_CappedByWhatIsMissing(int total, int living, int expected)
        {
            Assert.That(HordeReinforce.ModelsRestored(total, living), Is.EqualTo(expected));
        }

        [TestCase(12f, 4f, 6f)]
        [TestCase(12f, 11f, 1f)]  // capped by what is missing
        [TestCase(9f, 1f, 4f)]    // half of 9 rounds down
        public void WoundsRestored_IsHalfTheToughValue_CappedByWhatIsMissing(
            float total, float remaining, float expected)
        {
            Assert.That(HordeReinforce.WoundsRestored(total, remaining), Is.EqualTo((int)expected));
        }

        // ── applying it to a real unit ──────────────────────────────────────────────────────────────

        [Test]
        public void Apply_BringsBackWholeModels_DeadOnesFirst()
        {
            UnitData unit = MakeUnit(modelCount: 10, woundsPerModel: 1);
            List<ModelData> models = Own(unit);
            foreach (ModelData model in models.Take(7)) model.DealWounds(model.TotalWounds);

            string summary = HordeReinforce.Apply(unit, models);

            Assert.Multiple(() =>
            {
                Assert.That(models.Count(m => m.GetIsAlive()), Is.EqualTo(8), "3 survivors + 5 restored.");
                Assert.That(summary, Is.EqualTo("+5 models"));
            });
        }

        [Test]
        public void Apply_RestoresWounds_OnASingleModelUnit()
        {
            UnitData unit = MakeUnit(modelCount: 1, woundsPerModel: 12);
            List<ModelData> models = Own(unit);
            models[0].DealWounds(8); // 4 of 12 left - at half or less

            string summary = HordeReinforce.Apply(unit, models);

            Assert.Multiple(() =>
            {
                Assert.That(models[0].RemainingWoundsBinding.GetValue(), Is.EqualTo(10f), "4 + half of 12.");
                Assert.That(summary, Is.EqualTo("+6 wounds"));
            });
        }

        [Test]
        public void Apply_NeverTakesAUnitOverFullStrength()
        {
            UnitData unit = MakeUnit(modelCount: 4, woundsPerModel: 1);
            List<ModelData> models = Own(unit);
            models[0].DealWounds(models[0].TotalWounds);

            HordeReinforce.Apply(unit, models);

            Assert.That(models.Count(m => m.GetIsAlive()), Is.EqualTo(4));
        }

        [Test]
        public void Apply_DoesNotClearShaken()
        {
            UnitData unit = MakeUnit(modelCount: 6, woundsPerModel: 1);
            List<ModelData> models = Own(unit);
            foreach (ModelData model in models.Take(4)) model.DealWounds(model.TotalWounds);
            unit.Tokens.AddToken(Rules.Tokens.TokenDefinitionCatalog.Create(Rules.Foundation.TokenType.Shaken));

            HordeReinforce.Apply(unit, models);

            Assert.That(unit.Tokens.HasToken(Rules.Foundation.TokenType.Shaken), Is.True,
                "the rule restores models and wounds, nothing else - a reinforced unit is still Shaken.");
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────

        private static List<ModelData> Own(UnitData unit) =>
            unit.ModelBindings.Select(b => b.GetValue()).ToList();

        private UnitData MakeUnit(int modelCount, int woundsPerModel)
        {
            var bindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(i * 2f, 0f), _store);
                if (woundsPerModel > 1) model.SetMaxWounds(woundsPerModel);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Defenders",
                quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
