using System.Collections.Generic;
using System.Text.Json;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FDG.Tests
{
    // #412 - ValueSourceJsonConverter exists so a saved army written when an effect's numeric value was a
    // plain int still loads after that value becomes a ValueSource. Turning Effect.ExtraAttack.Attacks
    // into one (OPR 3.6.0 rated Takedown Strike / Takedown Shot: "one attack" -> "X attacks") broke every
    // saved list that embedded either definition with a hard JsonException, not a dropped rule. Saved
    // lists are not regenerable, so the old shape has to keep loading.
    //
    // The converter owns the whole "kind" mapping by hand, because a plain JsonConverter over a
    // [JsonPolymorphic] base throws as soon as the base sits nested inside another polymorphic type -
    // which ValueSource always does, inside Effect. That hand-written mapping is the risk this fixture
    // covers: it can silently fall behind the attributes.
    [TestFixture]
    public class ValueSourceJsonConverterTests
    {
        [Test]
        public void EveryDeclaredKind_IsHandledByTheConverter()
        {
            Assert.That(ValueSourceJsonConverter.HandledKinds,
                Is.EquivalentTo(ValueSourceJsonConverter.DeclaredKinds),
                "a ValueSource subtype was added or removed without updating the converter - its Read "
                + "would throw \"Unknown ValueSource kind\" on the next load, and its Write on the next "
                + "save. Add it to FromElement, Write and HandledKinds.");
        }

        // The whole point: the pre-#412 shape. "attacks": 1 instead of a tagged object.
        [TestCase("1", 1)]
        [TestCase("3", 3)]
        [TestCase("2.0", 2)]
        public void ABareNumber_ReadsAsALiteral(string json, int expected)
        {
            ValueSource? value = JsonSerializer.Deserialize<ValueSource>(json, RuleJson.Options);

            Assert.That(value, Is.EqualTo(new ValueSource.Literal(expected)));
        }

        [Test]
        public void AnEffectSavedWithAPlainIntegerAttackCount_StillLoads()
        {
            // Byte-for-byte the shape a pre-3.6.0 army embedded, which is what regression-proofs the fix:
            // a hand-built ValueSource would not have caught the failure, since the break was in reading
            // the OLD text.
            const string json = """
                {
                  "kind": "extraAttack",
                  "weaponName": "Takedown Shot",
                  "attacks": 1,
                  "armorPenetration": 2,
                  "withRules": [ "Reliable", "Deadly(3)", "Takedown" ]
                }
                """;

            Effect? effect = JsonSerializer.Deserialize<Effect>(json, RuleJson.Options);

            Assert.That(effect, Is.InstanceOf<Effect.ExtraAttack>());
            var extra = (Effect.ExtraAttack)effect!;
            Assert.That(extra.Attacks, Is.EqualTo(new ValueSource.Literal(1)),
                "the old integer must arrive as a literal 1, not as a load failure");
            Assert.That(extra.ArmorPenetration, Is.EqualTo(2));
            Assert.That(extra.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }));
        }

        // Every kind survives a round trip, including nested Negate - the one case where the converter
        // re-enters itself.
        private static IEnumerable<ValueSource> RoundTripCases()
        {
            yield return new ValueSource.Literal(4);
            yield return new ValueSource.Arg(0);
            yield return new ValueSource.Arg(2);
            yield return new ValueSource.RuleCarrierCount();
            yield return new ValueSource.Negate(new ValueSource.Arg(1));
            yield return new ValueSource.Negate(new ValueSource.Negate(new ValueSource.Literal(3)));
        }

        [TestCaseSource(nameof(RoundTripCases))]
        public void EveryKind_SurvivesARoundTrip(ValueSource original)
        {
            string json = JsonSerializer.Serialize(original, RuleJson.Options);
            ValueSource? read = JsonSerializer.Deserialize<ValueSource>(json, RuleJson.Options);

            Assert.That(read, Is.EqualTo(original), json);
        }

        [Test]
        public void AnEffectCarryingAValueSource_SurvivesARoundTripInsideItsPolymorphicParent()
        {
            // The case that made the first attempt at this converter fail: ValueSource nested inside
            // Effect, which is itself polymorphic. Delegating to STJ there throws "does not support
            // metadata writes or reads", so the nesting is what has to be tested, not ValueSource alone.
            Effect original = new Effect.ExtraAttack("Takedown Strike", new ValueSource.Arg(0),
                ArmorPenetration: 2, WithRules: new[] { "Reliable", "Deadly(3)", "Takedown" });

            string json = JsonSerializer.Serialize(original, RuleJson.Options);
            Effect? read = JsonSerializer.Deserialize<Effect>(json, RuleJson.Options);

            // Field by field, not record equality: WithRules is an IReadOnlyList, so a string[] in and a
            // List<string> out compare unequal by reference while holding the same rules. The ValueSource
            // is what this test is about, and it does compare by value.
            Assert.That(read, Is.InstanceOf<Effect.ExtraAttack>(), json);
            var roundTripped = (Effect.ExtraAttack)read!;
            Assert.That(roundTripped.Attacks, Is.EqualTo(new ValueSource.Arg(0)), json);
            Assert.That(roundTripped.WeaponName, Is.EqualTo("Takedown Strike"));
            Assert.That(roundTripped.ArmorPenetration, Is.EqualTo(2));
            Assert.That(roundTripped.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }));
        }

        [Test]
        public void AnUnknownKind_FailsLoudly()
        {
            Assert.That(() => JsonSerializer.Deserialize<ValueSource>(
                    """{ "kind": "frobnicate" }""", RuleJson.Options),
                Throws.InstanceOf<JsonException>(),
                "an unrecognised kind must throw rather than resolve to a silent zero");
        }
    }
}
