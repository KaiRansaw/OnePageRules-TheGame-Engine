using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #463 - the special rules another system DEFINES differently from Grimdark Future, found by diffing
    /// every catalog rule's Army Forge text across the six systems (2026-11-05). Each assertion is written
    /// against one named system's text, and the "book guard" tests compare the variant's number against
    /// that text directly, so a data refresh that changes the number fails here instead of drifting.
    /// </summary>
    [TestFixture]
    public class SystemRuleVariantsTests
    {
        private static SpecialRuleDefinition Rule(EGameSystem system, string name) =>
            CoreRuleCatalog.CreateResolver(system).Resolve(name).Definition;

        private static IEnumerable<Condition> Flatten(Condition c)
        {
            yield return c;
            switch (c)
            {
                case Condition.And a:
                    foreach (var x in Flatten(a.Left)) yield return x;
                    foreach (var x in Flatten(a.Right)) yield return x;
                    break;
                case Condition.Not n:
                    foreach (var x in Flatten(n.Inner)) yield return x;
                    break;
            }
        }

        private static IEnumerable<Condition> Conditions(SpecialRuleDefinition d) =>
            d.Passive.SelectMany(h => Flatten(h.Condition));

        private static int SlayerTough(EGameSystem system) =>
            Conditions(Rule(system, "Slayer")).OfType<Condition.TargetMajorityHasTough>().Select(t => t.MinToughValue).Distinct().Single();

        [TestCase(EGameSystem.GrimdarkFuture, 3, "grimdark-future")]
        [TestCase(EGameSystem.AgeOfFantasy, 3, "age-of-fantasy")]
        [TestCase(EGameSystem.GFFirefight, 9, "grimdark-future-firefight")]
        [TestCase(EGameSystem.AoFSkirmish, 9, "age-of-fantasy-skirmish")]
        [TestCase(EGameSystem.GFStarQuest, 27, "grimdark-future-star-quest")]
        [TestCase(EGameSystem.AoFQuest, 27, "age-of-fantasy-quest")]
        public void Slayer_TheToughThresholdIsEachSystemsOwn(EGameSystem system, int tough, string slug)
        {
            Assert.That(SlayerTough(system), Is.EqualTo(tough));
            Assert.That(OfficialRuleText.OwnText("Slayer", slug), Does.Contain($"Tough({tough})"), "book guard");
        }

        [Test]
        public void Indirect_QuestNeedsNoHold_ButTakesMinusOneAfterMoving()
        {
            SpecialRuleDefinition gf = Rule(EGameSystem.GrimdarkFuture, "Indirect");
            Assert.That(Conditions(gf).OfType<Condition.TokenPresent>().Any(), Is.True, "GF: Hold only");
            Assert.That(gf.Passive.Any(h => h.Effect is Effect.RollModifier), Is.False, "GF: no to-hit penalty");

            foreach (EGameSystem quest in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
            {
                SpecialRuleDefinition q = Rule(quest, "Indirect");
                HookEntry penalty = q.Passive.Single(h => h.Effect is Effect.RollModifier);
                Assert.That(((Effect.RollModifier)penalty.Effect).Delta, Is.EqualTo(-1));
                Assert.That(penalty.Condition, Is.EqualTo(new Condition.TokenPresent(TokenType.MovedThisRound)));
                Assert.That(q.Passive.Where(h => h.Effect is Effect.IgnoreLineOfSight).All(h => h.Condition is Condition.Always),
                    Is.True, "no Hold requirement");
            }
            Assert.That(OfficialRuleText.OwnText("Indirect", "grimdark-future-star-quest"), Does.Contain("-1 to hit"));
        }

        [TestCase(EGameSystem.GrimdarkFuture, 1, false)]
        [TestCase(EGameSystem.AgeOfFantasy, 1, false)]
        [TestCase(EGameSystem.AoFSkirmish, 3, false)]
        [TestCase(EGameSystem.AoFQuest, 3, false)]
        [TestCase(EGameSystem.GFStarQuest, 3, true)]
        public void FuriousBuff_HowManyUnitsAndHowOften(EGameSystem system, int maxUnits, bool oncePerGame)
        {
            ActivatedAbility ability = Rule(system, "Furious Buff").Activated.Single();
            Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(maxUnits));
            Assert.That(ability.Cost is Cost.OncePerGame, Is.EqualTo(oncePerGame));
        }

        [Test]
        public void Fanatic_IsADifferentRuleInTheFantasyGames()
        {
            // GF: the after-deployment reposition.
            Assert.That(Rule(EGameSystem.GrimdarkFuture, "Fanatic").Activated.Single().Effect, Is.TypeOf<Effect.RepositionOnDeploy>());

            // AoF: "The Hero and its unit get AP(+1) in melee when charging." - an aura of the charge bonus.
            SpecialRuleDefinition aof = Rule(EGameSystem.AgeOfFantasy, "Fanatic");
            Assert.That(aof.Passive.Single().Effect, Is.EqualTo(new Effect.Aura(SystemRuleVariants.FanaticCharge.Name)));
            Assert.That(CoreRuleCatalog.CreateResolver(EGameSystem.AgeOfFantasy).TryResolve(SystemRuleVariants.FanaticCharge.Name, out _),
                Is.True, "the aura's rule resolves");

            // AoF: Skirmish: "The Hero gets AP(+1) in melee when charging." - the bonus on the Hero alone.
            SpecialRuleDefinition aofs = Rule(EGameSystem.AoFSkirmish, "Fanatic");
            HookEntry charge = aofs.Passive.Single();
            Assert.That(((Effect.RollModifier)charge.Effect).Delta, Is.EqualTo(-1));
            Assert.That(Conditions(aofs).OfType<Condition.IsCharging>().Any() && Conditions(aofs).OfType<Condition.IsMelee>().Any(), Is.True);
            Assert.That(OfficialRuleText.OwnText("Fanatic", "age-of-fantasy-skirmish"), Does.Contain("AP(+1)"), "book guard");
        }

        [Test]
        public void CounterAttack_IsSwitchedOffByFatigue()
        {
            Assert.That(Conditions(Rule(EGameSystem.AoFSkirmish, "Counter-Attack")).OfType<Condition.TokenPresent>()
                .Any(t => t.TType == TokenType.Fatigued), Is.True);
            Assert.That(OfficialRuleText.OwnText("Counter-Attack", "age-of-fantasy-skirmish"), Does.Contain("unless this unit is Fatigued"));
        }

        [Test]
        public void Profile_FlyingAndElite()
        {
            Assert.That(CoreRulesProfile.For(EGameSystem.GrimdarkFuture).FlyingKeepsTerrainItStartsIn, Is.False);
            Assert.That(CoreRulesProfile.For(EGameSystem.AgeOfFantasy).FlyingKeepsTerrainItStartsIn, Is.False);
            foreach (EGameSystem s in new[] { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish, EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
                Assert.That(CoreRulesProfile.For(s).FlyingKeepsTerrainItStartsIn, Is.True, s.ToString());
            Assert.That(OfficialRuleText.OwnText("Flying", "grimdark-future-firefight"), Does.Contain("doesn't start its move in"));

            Assert.That(CoreRulesProfile.For(EGameSystem.GrimdarkFuture).HordeEliteIsPerRound, Is.False);
            Assert.That(CoreRulesProfile.For(EGameSystem.GFFirefight).HordeEliteIsPerRound, Is.True);
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFSkirmish).HordeEliteIsPerRound, Is.True);
            Assert.That(OfficialRuleText.OwnText("Trait: Elite", "age-of-fantasy-skirmish")
                        ?? OfficialRuleText.OwnText("Elite", "age-of-fantasy-skirmish"), Does.Contain("once per round"));
        }

        [Test]
        public void TheDefaultResolver_IsGrimdarkFutures()
        {
            Assert.That(CoreRuleCatalog.CreateResolver().Resolve("Slayer").Definition.Passive
                .SelectMany(h => Flatten(h.Condition)).OfType<Condition.TargetMajorityHasTough>().First().MinToughValue, Is.EqualTo(3));
        }
    }
}
