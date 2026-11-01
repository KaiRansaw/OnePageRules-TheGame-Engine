using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the activation order from <b>Solo &amp; Co-Op Rules v3.6.0</b>: "roll a D3 to see which
    /// section it will activate a unit in. If there are no eligible units in that section, move onto the
    /// next section with an eligible unit, going clockwise. Then roll a die to randomly determine which unit
    /// from that section is activated next. Shaken AI units only activate after all non-shaken AI units
    /// have activated."
    ///
    /// <para>
    /// The old bot took the first option the engine offered, which in practice is army build order. That
    /// is not cosmetic: a player who can name the next unit to move can stand just outside its reach all
    /// game, and the randomised section roll is the book's answer.
    /// </para>
    /// <para>
    /// The rolls are injected here rather than drawn from a seeded stream, so each clause can be tested on
    /// its own: the fake returns whatever the test says the dice said.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiActivationOrderTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;

        private const float Width = 48f;
        private const float Height = 48f;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
        }

        private DataBinding<UnitData> Unit(string name, float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_ai, name, quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private static void Shake(DataBinding<UnitData> unit) =>
            unit.GetValue().Tokens.AddToken(
                new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

        /// <summary>Dice that answer with a fixed script: first call the section, then the unit index.</summary>
        private static Func<int, int> Rolls(params int[] script)
        {
            int index = 0;
            return _ => script[Math.Min(index++, script.Length - 1)];
        }

        private DataBinding<UnitData> Pick(IReadOnlyList<DataBinding<UnitData>> units, Func<int, int> rolls) =>
            SoloAiActivationOrder.Pick(units, b => b.GetValue(), Width, Height, rolls);

        [Test]
        public void SectionsDivideTheLongerAxisIntoThree()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SoloAiActivationOrder.SectionOf(new Position(4f, 24f), Width, Height), Is.EqualTo(1));
                Assert.That(SoloAiActivationOrder.SectionOf(new Position(24f, 24f), Width, Height), Is.EqualTo(2));
                Assert.That(SoloAiActivationOrder.SectionOf(new Position(44f, 24f), Width, Height), Is.EqualTo(3));
            });
        }

        [Test]
        public void SectionOf_ClampsTheFarEdgeIntoTheLastSection()
        {
            // A model exactly on the table edge divides to band 4; the book has three.
            Assert.That(SoloAiActivationOrder.SectionOf(new Position(Width, 24f), Width, Height), Is.EqualTo(3));
            Assert.That(SoloAiActivationOrder.SectionOf(new Position(0f, 0f), Width, Height), Is.EqualTo(1));
        }

        [Test]
        public void TheD3PicksTheSection()
        {
            DataBinding<UnitData> left = Unit("Left", 4f, 24f);        // section 1
            DataBinding<UnitData> middle = Unit("Middle", 24f, 24f);   // section 2
            DataBinding<UnitData> right = Unit("Right", 44f, 24f);     // section 3
            var all = new[] { left, middle, right };

            Assert.Multiple(() =>
            {
                Assert.That(Pick(all, Rolls(1, 1)).GetValue().Name, Is.EqualTo("Left"));
                Assert.That(Pick(all, Rolls(2, 1)).GetValue().Name, Is.EqualTo("Middle"));
                Assert.That(Pick(all, Rolls(3, 1)).GetValue().Name, Is.EqualTo("Right"));
            });
        }

        [Test]
        public void AnEmptySection_MovesOnToTheNextOneAlong()
        {
            // "If there are no eligible units in that section, move onto the next section with an eligible
            // unit, going clockwise." Nothing in section 1, so a roll of 1 lands on section 2.
            DataBinding<UnitData> middle = Unit("Middle", 24f, 24f);
            DataBinding<UnitData> right = Unit("Right", 44f, 24f);

            Assert.That(Pick(new[] { middle, right }, Rolls(1, 1)).GetValue().Name, Is.EqualTo("Middle"));
        }

        [Test]
        public void TheSearchWrapsPastTheLastSection()
        {
            // A roll of 3 with units only in section 1 has to come back round to it.
            DataBinding<UnitData> left = Unit("Left", 4f, 24f);
            DataBinding<UnitData> alsoLeft = Unit("AlsoLeft", 6f, 24f);

            Assert.That(Pick(new[] { left, alsoLeft }, Rolls(3, 1)).GetValue().Name, Is.EqualTo("Left"));
        }

        [Test]
        public void TheSecondRollPicksTheUnitWithinTheSection()
        {
            DataBinding<UnitData> first = Unit("First", 4f, 10f);
            DataBinding<UnitData> second = Unit("Second", 6f, 20f);
            DataBinding<UnitData> third = Unit("Third", 8f, 30f);
            var all = new[] { first, second, third };   // all in section 1

            Assert.Multiple(() =>
            {
                Assert.That(Pick(all, Rolls(1, 1)).GetValue().Name, Is.EqualTo("First"));
                Assert.That(Pick(all, Rolls(1, 2)).GetValue().Name, Is.EqualTo("Second"));
                Assert.That(Pick(all, Rolls(1, 3)).GetValue().Name, Is.EqualTo("Third"));
            });
        }

        [Test]
        public void ShakenUnitsWaitUntilEverythingElseHasGone()
        {
            DataBinding<UnitData> shaken = Unit("Shaken", 4f, 24f);     // section 1
            DataBinding<UnitData> steady = Unit("Steady", 44f, 24f);    // section 3
            Shake(shaken);

            // Even with the D3 landing on the Shaken unit's own section, it is skipped.
            Assert.That(Pick(new[] { shaken, steady }, Rolls(1, 1)).GetValue().Name, Is.EqualTo("Steady"));
        }

        [Test]
        public void WithOnlyShakenUnitsLeft_OneOfThemActivates()
        {
            // "only activate AFTER all non-shaken units have activated" - not never. When they are all
            // that is left the pick must still return one, or the round cannot end.
            DataBinding<UnitData> a = Unit("ShakenA", 4f, 24f);
            DataBinding<UnitData> b = Unit("ShakenB", 6f, 24f);
            Shake(a);
            Shake(b);

            DataBinding<UnitData> pick = Pick(new[] { a, b }, Rolls(1, 1));

            Assert.That(pick.GetValue().Name, Does.StartWith("Shaken"));
        }

        [Test]
        public void ASingleCandidate_IsReturnedWithoutRolling()
        {
            DataBinding<UnitData> only = Unit("Only", 24f, 24f);
            bool rolled = false;

            DataBinding<UnitData> pick = SoloAiActivationOrder.Pick(new[] { only }, b => b.GetValue(),
                Width, Height, _ => { rolled = true; return 1; });

            Assert.That(pick.GetValue().Name, Is.EqualTo("Only"));
            Assert.That(rolled, Is.False, "no choice to make, so no dice spent.");
        }

        [Test]
        public void NoCandidatesAtAll_Throws()
        {
            // The engine never sends an empty activation request; if it ever did, failing loudly beats
            // returning null into a stage that will dereference it.
            Assert.Throws<InvalidOperationException>(() =>
                SoloAiActivationOrder.Pick(Array.Empty<DataBinding<UnitData>>(), b => b.GetValue(),
                    Width, Height, _ => 1));
        }

        [Test]
        public void ADeadUnitStillGetsASection_RatherThanThrowing()
        {
            // Units with no living models should not reach an activation request, but the section lookup
            // has to answer for one anyway - it reads the first LIVING model and falls back to section 1.
            DataBinding<UnitData> dead = Unit("Dead", 44f, 24f);
            foreach (IModel model in dead.GetValue().Models)
                model.DealWounds(model.TotalWounds - model.WoundsDealt);
            DataBinding<UnitData> alive = Unit("Alive", 44f, 30f);

            Assert.That(Pick(new[] { dead, alive }, Rolls(3, 1)).GetValue().Name, Is.EqualTo("Alive"),
                "the living unit is in section 3; the dead one defaulted to section 1.");
        }
    }
}
