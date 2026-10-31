using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #414 - the failed hero-skill payment, pinned against GFSQ Core Rules v2.1.0: "When using Hero
    /// Skills, take a test of that Skill's stat. If passed the effect is resolved, and if failed you
    /// **may** spend 1 Power to count as having passed it instead."
    ///
    /// <para>
    /// Star Quest was built against v2.0.1, where the sentence ended "...1 Power and then the effect is
    /// resolved" - no choice, and the skill always worked. So the old behaviour was wrong twice over: it
    /// took power the player never agreed to spend, and a hero skill could never actually fail. Both
    /// halves are tested here, because fixing only the prompt (ask, then resolve anyway) would look
    /// right in play right up until someone declined.
    /// </para>
    ///
    /// <para>
    /// Driven through <see cref="QuestSkill.Use"/> rather than <see cref="QuestActions.HeroSkillTest"/>
    /// alone: the decision that matters is whether the EFFECT resolves, and that lives in the caller.
    /// "Rally" is the probe - a non-common, non-feat skill with a Wil test whose whole effect is removing
    /// the Shaken token, so "did the skill resolve" is one observable flag with no table needed.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestFailedSkillPaymentTests
    {
        /// <summary>Answers every yes/no with a fixed reply and records what it was asked.</summary>
        private sealed class ScriptedRequester : IPlayerRequestByID
        {
            private readonly bool _answer;
            public ScriptedRequester(bool answer) => _answer = answer;

            public List<string> YesNoPrompts { get; } = new();
            public List<bool> YesNoDefaults { get; } = new();
            public List<string> OtherRequests { get; } = new();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply>
            {
                if (request is YesNoRequest yn)
                {
                    YesNoPrompts.Add(yn.QuestionText);
                    YesNoDefaults.Add(yn.DefaultAnswer);
                    return Task.FromResult((TReply)(object)_answer);
                }
                // Rally asks for nothing else, so anything here is a skill resolving when it should not.
                OtherRequests.Add(request.GetType().Name);
                throw new InvalidOperationException($"unexpected request: {request.GetType().Name}");
            }
        }

        private const string Probe = "Rally";

        // A roll of 1 fails every stat test (the target is clamped to 2..6), a 6 passes every one.
        private static (TestGameContext Context, UnitData Hero, ScriptedRequester Requester) Table(
            int die, bool payWhenAsked, int power = 3,
            EGameSystem system = EGameSystem.GFStarQuest)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var owner = new PlayerID(Guid.NewGuid());
            store.Create(new PlayerSlotInfo(owner, slotID: 0, teamNumber: 0, name: "Host", isFilled: true));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            settings.GameSystem = system;

            var requester = new ScriptedRequester(payWhenAsked);
            var context = new TestGameContext(store, new FixedDiceRoller(die), settings: settings,
                playerRequester: requester);

            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(10f, 10f), gameDataStore: store);
            DataBinding<ModelData> modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
            var unit = new UnitData(owner, "Hero", quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.QuestHeroJson = new QuestHero
            {
                Name = "Hero", Power = Math.Max(power, 1), Willpower = 4,
                Skills = new List<string> { Probe },
            }.ToJson();
            unit.QuestPower = power;
            DataBinding<UnitData> unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));
            store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { unitBinding }));

            UnitData hero = unitBinding.GetValue();
            hero.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            return (context, hero, requester);
        }

        private static QuestSkill TheProbe()
        {
            QuestSkill? skill = QuestSkill.Find(Probe);
            Assert.That(skill, Is.Not.Null, $"'{Probe}' is the probe for this fixture.");
            Assert.That(skill!.Common, Is.False, "a common skill takes the other branch (2 power to pass).");
            Assert.That(skill.Feat, Is.False, "a feat takes no test at all.");
            Assert.That(skill.Stat, Is.GreaterThanOrEqualTo(0), "the probe has to be a skill that tests a stat.");
            return skill;
        }

        [Test]
        public async Task APassedTest_ResolvesTheSkill_AndAsksForNothing()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) = Table(die: 6, payWhenAsked: false);

            await TheProbe().Use(context, hero);

            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False, "a passed test resolves the skill.");
            Assert.That(hero.QuestPower, Is.EqualTo(3), "a passed test costs nothing.");
            Assert.That(requester.YesNoPrompts, Is.Empty,
                "the power offer is for a FAILED test only - asking on a pass would be a free tax on a good roll.");
        }

        [Test]
        public async Task AFailedTest_OffersThePayment_AndResolvesTheSkillWhenPaid()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) = Table(die: 1, payWhenAsked: true);

            await TheProbe().Use(context, hero);

            Assert.That(requester.YesNoPrompts.Count, Is.EqualTo(1), "exactly one offer, and only on the fail.");
            Assert.That(requester.YesNoPrompts[0], Does.Contain("1 power"),
                "the prompt has to name the price - this is the number the book charges.");
            Assert.That(hero.QuestPower, Is.EqualTo(2), "paying costs FailedSkillPower (1).");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False,
                "paying counts the test as passed, so the effect resolves.");
        }

        [Test]
        public async Task AFailedTest_FizzlesWhenThePaymentIsDeclined_AndCostsNothing()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) = Table(die: 1, payWhenAsked: false);

            await TheProbe().Use(context, hero);

            Assert.That(requester.YesNoPrompts.Count, Is.EqualTo(1));
            Assert.That(hero.QuestPower, Is.EqualTo(3),
                "declining must not charge anything - under v2.0.1 this was taken without asking.");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.True,
                "THE regression guard: a declined payment means the skill does not happen. Resolving anyway "
                + "would make the prompt a lie - the player would pay or not and get the effect either way.");
            Assert.That(requester.OtherRequests, Is.Empty, "a fizzled skill asks the player nothing further.");
        }

        // The default is what every automated answerer takes - bots, EOF-piped headless runs, and a player
        // who dismisses the dialog. Spending power a hero does not have costs it a wound per missing point,
        // so a broke hero must not be defaulted into hurting itself over a failed roll.
        [Test]
        public async Task TheDefaultAnswer_IsYesOnlyWhileTheHeroCanAffordIt()
        {
            (TestGameContext rich, UnitData richHero, ScriptedRequester richAsk) = Table(die: 1, payWhenAsked: false, power: 1);
            await TheProbe().Use(rich, richHero);
            Assert.That(richAsk.YesNoDefaults, Is.EqualTo(new[] { true }),
                "1 power is exactly the price, so yes is the sensible default.");

            (TestGameContext broke, UnitData brokeHero, ScriptedRequester brokeAsk) = Table(die: 1, payWhenAsked: false, power: 0);
            await TheProbe().Use(broke, brokeHero);
            Assert.That(brokeAsk.YesNoDefaults, Is.EqualTo(new[] { false }),
                "with no power left the payment would cost a wound, so the default must be no.");
        }

        [Test]
        public async Task AFeatOrAStatlessSkill_TakesNoTestAndIsNeverCharged()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) = Table(die: 1, payWhenAsked: true);

            bool resolve = await QuestActions.HeroSkillTest(context, hero, statType: -1, skill: "no stat");

            Assert.That(resolve, Is.True, "a skill with no stat has nothing to fail, so it always resolves.");
            Assert.That(requester.YesNoPrompts, Is.Empty);
            Assert.That(hero.QuestPower, Is.EqualTo(3));
        }
        // ── #418: the two Quest systems are on the SAME book generation ─────────────────────────────
        //
        // This block used to assert the opposite, and the story is worth keeping because it is a trap
        // anyone reading two OPR books will fall into.
        //
        // #418 was built against AoFQ Core Rules **v2.0.1**, the only AoF: Quest book available at the
        // time, which says: "if failed you must first spend 1 power and then the effect is resolved" -
        // forced payment, and a hero skill that cannot fail. That was implemented, tested and shipped,
        // with a discriminating pair proving the two systems disagreed.
        //
        // Then the **v2.1.0** books arrived (Beginners Guide + Campaign Builder) and AoF: Quest reads
        // "if failed you may spend 1 Power to count as having passed it instead" - the same optional
        // payment as Star Quest, along with the same 3-power extra action, the same flat 3" melee reach,
        // the same 9" Scout and the same 6" mandatory Villain aura. OPR had simply brought AoF: Quest up
        // a generation one book at a time.
        //
        // So the tests now assert AGREEMENT, and the claim they defend is the useful one: **a hero skill
        // is optional-and-can-fail in every Quest system, and the two systems read one profile until a
        // book says otherwise.**

        [TestCase(EGameSystem.GFStarQuest)]
        [TestCase(EGameSystem.AoFQuest)]
        public async Task InEveryQuestSystem_AFailedTest_OffersThePayment(EGameSystem system)
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) =
                Table(die: 1, payWhenAsked: true, system: system);

            await TheProbe().Use(context, hero);

            Assert.That(requester.YesNoPrompts.Count, Is.EqualTo(1),
                $"{system}: 2.1.0 makes the payment optional, so it has to be offered");
            Assert.That(hero.QuestPower, Is.EqualTo(2), "paying costs 1 power");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False, "and the skill then resolves");
        }

        [TestCase(EGameSystem.GFStarQuest)]
        [TestCase(EGameSystem.AoFQuest)]
        public async Task InEveryQuestSystem_DecliningMakesTheSkillFail(EGameSystem system)
        {
            // The half that only exists from 2.1.0 onwards: under 2.0.1 no hero skill could ever fail.
            (TestGameContext context, UnitData hero, ScriptedRequester requester) =
                Table(die: 1, payWhenAsked: false, system: system);

            await TheProbe().Use(context, hero);

            Assert.That(requester.YesNoPrompts.Count, Is.EqualTo(1));
            Assert.That(hero.QuestPower, Is.EqualTo(3), $"{system}: declining costs nothing");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.True,
                $"{system}: a declined skill FAILS, so the Shaken token it would have removed stays");
        }

        [Test]
        public void TheTwoQuestSystems_ReadOneProfile_UntilABookSaysOtherwise()
        {
            // The seam is still there (QuestRulesProfile.For switches on the system) and that matters:
            // OPR moved one Quest system to 2.1.0 before the other, so the next generation can split
            // them again. What must not happen is a system quietly playing the OTHER one's numbers -
            // which is what every per-system profile in this codebase exists to prevent.
            Assert.That(QuestRulesProfile.For(EGameSystem.AoFQuest),
                Is.SameAs(QuestRulesProfile.For(EGameSystem.GFStarQuest)),
                "both Quest systems are on 2.1.0 core books as of 2026-10-31");
        }

        [Test]
        public void TheProfilesNumbers_AreTheirBooks()
        {
            // Read straight from the 2.1.0 core books, so a reader can check them without a PDF. Both
            // systems; the AoFQ quotes are in QuestRulesProfile.
            foreach (EGameSystem system in new[] { EGameSystem.GFStarQuest, EGameSystem.AoFQuest })
            {
                QuestRulesProfile p = QuestRulesProfile.For(system);
                Assert.That(p.ExtraActionPower, Is.EqualTo(3), $"{system}: 3 power for an extra action");
                Assert.That(p.RevivePower, Is.EqualTo(3), $"{system}: 3 power to revive");
                Assert.That(p.PassStatTestPower, Is.EqualTo(2), $"{system}");
                Assert.That(p.IgnoreShakenPower, Is.EqualTo(2), $"{system}");
                Assert.That(p.FailedSkillPower, Is.EqualTo(1), $"{system}");
                Assert.That(p.FailedSkillPaymentIsOptional, Is.True, $"{system}: optional from 2.1.0");
                Assert.That(p.ScoutForwardInches, Is.EqualTo(9f), $"{system}: 9\" from 2.1.0");
                Assert.That(p.VillainAuraInches, Is.EqualTo(6f), $"{system}: 6\" from 2.1.0");
                Assert.That(p.VillainAuraIsMandatory, Is.True, $"{system}: \"use\", not \"may use\"");
                Assert.That(p.MeleeReachHorizontalInches, Is.EqualTo(3f), $"{system}: flat 3\"");
                Assert.That(p.MeleeReachVerticalInches, Is.EqualTo(3f), $"{system}: no vertical clause");
            }

            // And a non-Quest system never reaches a Quest number, but reads the Quest profile if it asks.
            Assert.That(QuestRulesProfile.For(EGameSystem.GrimdarkFuture),
                Is.SameAs(QuestRulesProfile.Quest));
        }

    }
}
