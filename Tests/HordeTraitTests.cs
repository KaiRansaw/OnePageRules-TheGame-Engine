using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StateMachine;
using FDG.Tests.RulesHarness;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Horde Mode traits: each one, granted to a unit the way levelling up grants it (a permanent rule-grant
    /// token), must produce the right operation at the right hook - and the XP / trait-roll arithmetic.
    /// </summary>
    [TestFixture]
    public class HordeTraitTests
    {
        private static (TestRuleHarness harness, IUnit unit, IUnit enemy) Setup(string trait)
        {
            var harness = new TestRuleHarness();
            foreach (SpecialRuleDefinition def in CoreRuleCatalog.All)
                harness.Register(def);
            IUnit unit = harness.BuildUnit("Defender", modelCount: 3);
            IUnit enemy = harness.BuildUnit("Enemy", modelCount: 3);
            harness.GrantRule(unit, trait, ELifetime.UntilEndOfGame);
            return (harness, unit, enemy);
        }

        [Test]
        public void Agile_AddsOneInchToAdvance_TwoToRushAndCharge()
        {
            var (h, unit, _) = Setup(HordeExperience.Agile);
            h.Evaluate(unit, ERuleSeat.Actor, new MoveActionDeclaredContext(unit, EActionType.Advance, BaseDistanceInches: 6f))
                .HasOperation<RuleOperation.ApplyMovementBonus>(op => op.ActionType == EActionType.Advance && op.DistanceInches == 1f);
            h.Evaluate(unit, ERuleSeat.Actor, new MoveActionDeclaredContext(unit, EActionType.Rush, BaseDistanceInches: 12f))
                .HasOperation<RuleOperation.ApplyMovementBonus>(op => op.ActionType == EActionType.Rush && op.DistanceInches == 2f);
            h.Evaluate(unit, ERuleSeat.Actor, new MoveActionDeclaredContext(unit, EActionType.Charge, BaseDistanceInches: 12f))
                .HasOperation<RuleOperation.ApplyMovementBonus>(op => op.ActionType == EActionType.Charge && op.DistanceInches == 2f);
        }

        [Test]
        public void Headstrong_GivesPlusOneToMorale()
        {
            var (h, unit, _) = Setup(HordeExperience.Headstrong);
            h.Evaluate(unit, ERuleSeat.Actor, new PreMoraleTestContext(unit))
                .HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Morale && op.Delta == 1);
        }

        [Test]
        public void MeleeSpecialist_IsPlusOneToHitInMeleeOnly()
        {
            var (h, unit, enemy) = Setup(HordeExperience.MeleeSpecialist);
            h.Evaluate(unit, ERuleSeat.Actor, new HitRollModifierContext(unit, enemy, 0f, IsMelee: true))
                .HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == 1);
            Assert.That(h.Evaluate(unit, ERuleSeat.Actor, new HitRollModifierContext(unit, enemy, 12f, IsMelee: false))
                .OfType<RuleOperation.ApplyRollModifier>(), Is.Empty);
        }

        [Test]
        public void ShootingSpecialist_IsPlusOneToHitWhenShootingOnly()
        {
            var (h, unit, enemy) = Setup(HordeExperience.ShootingSpecialist);
            h.Evaluate(unit, ERuleSeat.Actor, new HitRollModifierContext(unit, enemy, 12f, IsMelee: false))
                .HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Hit && op.Delta == 1);
            Assert.That(h.Evaluate(unit, ERuleSeat.Actor, new HitRollModifierContext(unit, enemy, 0f, IsMelee: true))
                .OfType<RuleOperation.ApplyRollModifier>(), Is.Empty);
        }

        [Test]
        public void Resilient_GivesPlusOneToDefenseWhenAttacked()
        {
            var (h, unit, enemy) = Setup(HordeExperience.Resilient);
            h.Evaluate(unit, ERuleSeat.Subject, new HitRollCompleteContext(enemy, unit, TestDice.Faces(4), DistanceInches: 12f))
                .HasOperation<RuleOperation.ApplyRollModifier>(op => op.Roll == ERollKind.Save && op.Delta == 1);
        }

        [Test]
        public void Elite_RerollIsOncePerGame()
        {
            var (_, unit, _) = Setup(HordeExperience.Elite);
            Assert.That(HordeExperience.TryUseElite(unit), Is.True);
            Assert.That(HordeExperience.TryUseElite(unit), Is.False, "already used - #409 made it once per GAME");

            var (_, plain, _) = Setup(HordeExperience.Agile);
            Assert.That(HordeExperience.TryUseElite(plain), Is.False, "no Elite trait");
        }

        // #414: the check and the spend are separate so the re-roll can be offered before it is paid for.
        // This is the half that would be silently wrong if they ever merged again: asking the player, and
        // being told no, must leave the unit's one use intact for the rest of the game.
        [Test]
        public void Elite_AskingWithoutAccepting_DoesNotSpendTheUse()
        {
            var (_, unit, _) = Setup(HordeExperience.Elite);

            Assert.That(HordeExperience.CanUseElite(unit), Is.True);
            Assert.That(HordeExperience.CanUseElite(unit), Is.True,
                "checking twice must not cost anything - a declined offer is just a check.");

            HordeExperience.SpendElite(unit);

            Assert.That(HordeExperience.CanUseElite(unit), Is.False, "spent now, and not again this game.");
        }

        [Test]
        public void Elite_CanUse_IsFalseForAUnitWithoutTheTrait()
        {
            var (_, plain, _) = Setup(HordeExperience.Agile);

            Assert.That(HordeExperience.CanUseElite(plain), Is.False);
        }

        [TestCase(false, false, 3)]
        [TestCase(false, true, 5)]
        [TestCase(true, false, 8)]
        public void XpForKill_MatchesTheRules(bool joinedHero, bool soloHero, int expected)
        {
            Assert.That(HordeExperience.XpForKill(joinedHero, soloHero), Is.EqualTo(expected));
        }

        [TestCase(0, 3, 0)]
        [TestCase(3, 6, 1)]
        [TestCase(4, 12, 2)]
        [TestCase(27, 35, 1)]  // capped at 30
        [TestCase(30, 38, 0)]
        public void LevelUps_CountsEveryFiveXpCrossed(int before, int after, int expected)
        {
            Assert.That(HordeExperience.LevelUps(before, after), Is.EqualTo(expected));
        }

        private static Func<int> Rolls(params int[] faces)
        {
            var queue = new Queue<int>(faces);
            return () => queue.Dequeue();
        }

        [Test]
        public void RollTrait_RerollsTraitsAlreadyOwned_ButSpecialistUntilBothPicksTaken()
        {
            var owned = new HashSet<string> { HordeExperience.Agile, HordeExperience.MeleeSpecialist };
            Assert.That(HordeExperience.RollTrait(owned, Rolls(1, 1, 3)), Is.EqualTo(HordeExperience.ETraitRoll.Specialist),
                "Agile is re-rolled; Specialist is still open (shooting pick left)");

            owned.Add(HordeExperience.ShootingSpecialist);
            Assert.That(HordeExperience.RollTrait(owned, Rolls(3, 5)), Is.EqualTo(HordeExperience.ETraitRoll.Elite),
                "both Specialist picks taken - a 3 is re-rolled");
        }

        [Test]
        public void RollTrait_FastLearnerOnlyOnce_AndNothingWhenAllOwned()
        {
            var owned = new HashSet<string> { HordeExperience.FastLearner };
            Assert.That(HordeExperience.RollTrait(owned, Rolls(6, 2)), Is.EqualTo(HordeExperience.ETraitRoll.Headstrong));

            var everything = new HashSet<string>(HordeExperience.AvailableTraits(new HashSet<string>(), includeFastLearner: true));
            Assert.That(HordeExperience.RollTrait(everything, Rolls(1)), Is.Null);
        }
    }
}
