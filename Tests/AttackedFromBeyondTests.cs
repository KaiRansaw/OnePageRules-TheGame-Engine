using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    // 3.6.0 split the old single 9" band into two numbers: "When this model shoots/charges enemies over 9"/6"
    // away" - 9" when shooting, 6" when charging. Condition.AttackedFromBeyond carries both; the shooting half
    // reads the live distance, the melee half the charge-origin distance (as AttackedFromOverInches does).
    [TestFixture]
    public class AttackedFromBeyondTests
    {
        private static SpecialRuleDefinition Probe(Condition gate) =>
            new("Band",
                new[]
                {
                    new HookEntry(EHookID.Shooting_OnHitRollComplete, gate,
                        new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack),
                },
                Array.Empty<ActivatedAbility>());

        private static bool Fires(float liveInches, bool melee, float chargeOrigin = 0f)
        {
            var harness = new TestRuleHarness();
            harness.Register(Probe(new Condition.AttackedFromBeyond(9f, 6f)));
            IUnit attacker = harness.BuildUnit("P1", 1, "Band");
            IUnit target = harness.BuildUnit("P2", 1);
            var context = melee
                ? new HitRollCompleteContext(attacker, target, TestDice.Faces(4), DistanceInches: 0.5f,
                    IsMelee: true, IsCharging: true, IsSpell: false, ChargeOriginDistanceInches: chargeOrigin)
                : new HitRollCompleteContext(attacker, target, TestDice.Faces(4), DistanceInches: liveInches,
                    IsMelee: false);
            IReadOnlyList<RuleOperation> ops = harness.Evaluate(attacker, ERuleSeat.Actor, context);
            return ops.OfType<RuleOperation.ApplyRollModifier>().Any();
        }

        [TestCase(12f, true)]
        [TestCase(9.5f, true)]
        [TestCase(9f, false)]   // "over 9"" - exactly 9 is not over
        [TestCase(7f, false)]   // inside the shooting band even though it would clear the charge band
        public void Shooting_UsesTheNineInchBand(float inches, bool expected) =>
            Assert.That(Fires(inches, melee: false), Is.EqualTo(expected));

        [TestCase(8f, true)]    // would fail a 9" band - the 3.5 bug this condition exists to fix
        [TestCase(6.5f, true)]
        [TestCase(6f, false)]
        [TestCase(3f, false)]
        public void Charging_UsesTheSixInchBand_FromTheChargeOrigin(float origin, bool expected) =>
            Assert.That(Fires(0.5f, melee: true, chargeOrigin: origin), Is.EqualTo(expected));

        [Test]
        public void RoundTripsThroughTheBookJson()
        {
            Condition c = new Condition.AttackedFromBeyond(9f, 6f);
            string json = System.Text.Json.JsonSerializer.Serialize(c, FDG.Rules.Serialization.RuleJson.Options);
            Assert.That(json, Does.Contain("attackedFromBeyond"));
            Assert.That(System.Text.Json.JsonSerializer.Deserialize<Condition>(json, FDG.Rules.Serialization.RuleJson.Options), Is.EqualTo(c));
        }
    }
}
