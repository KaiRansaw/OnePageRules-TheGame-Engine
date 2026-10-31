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
        // ── #418: AoF: Quest is on the 2.0.1 wording ────────────────────────────────────────────────
        //
        // "When using hero skills, take a test of that skill's stat. If passed the effect is resolved,
        // and if failed you must first spend 1 power and then the effect is resolved."
        //   - AoFQ Core Rules v2.0.1, Skill actions
        //
        // So in AoF: Quest there is no choice to offer and a hero skill CANNOT fail. That is the
        // behaviour Q3 removed for Star Quest when it moved to 2.1.0, and the pair of readings is
        // exactly why QuestRulesProfile exists: the engine was about to serve 2.1.0's rules to a game
        // whose own book describes 2.0.1's.

        [Test]
        public async Task InAoFQuest_AFailedTest_PaysWithoutAsking_AndTheSkillStillResolves()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) =
                Table(die: 1, payWhenAsked: false, system: EGameSystem.AoFQuest);

            await TheProbe().Use(context, hero);

            Assert.That(requester.YesNoPrompts, Is.Empty,
                "2.0.1 forces the payment, so there is nothing to ask - a prompt here would be Star "
                + "Quest's rule leaking into an AoF: Quest game");
            Assert.That(hero.QuestPower, Is.EqualTo(2), "the forced payment is 1 power");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False,
                "and then the effect is resolved - the skill works either way under 2.0.1");
        }

        [Test]
        public async Task InAoFQuest_APassedTest_StillCostsNothing()
        {
            (TestGameContext context, UnitData hero, ScriptedRequester requester) =
                Table(die: 6, payWhenAsked: false, system: EGameSystem.AoFQuest);

            await TheProbe().Use(context, hero);

            Assert.That(hero.QuestPower, Is.EqualTo(3), "the power is the price of FAILING the test");
            Assert.That(hero.Tokens.HasToken(TokenType.Shaken), Is.False);
            Assert.That(requester.YesNoPrompts, Is.Empty);
        }

        [Test]
        public async Task TheTwoSystemsDisagree_WhichIsTheWholePoint()
        {
            // The discriminating pair, side by side: same roll, same hero, different book. Declining is
            // only possible in one of them, and only in one of them can the skill fail.
            (TestGameContext sqContext, UnitData sqHero, ScriptedRequester sqRequester) =
                Table(die: 1, payWhenAsked: false, system: EGameSystem.GFStarQuest);
            await TheProbe().Use(sqContext, sqHero);

            (TestGameContext aofContext, UnitData aofHero, ScriptedRequester aofRequester) =
                Table(die: 1, payWhenAsked: false, system: EGameSystem.AoFQuest);
            await TheProbe().Use(aofContext, aofHero);

            Assert.That(sqRequester.YesNoPrompts.Count, Is.EqualTo(1), "Star Quest asks");
            Assert.That(aofRequester.YesNoPrompts, Is.Empty, "AoF: Quest does not");

            Assert.That(sqHero.QuestPower, Is.EqualTo(3), "declining in Star Quest costs nothing");
            Assert.That(aofHero.QuestPower, Is.EqualTo(2), "AoF: Quest took the power regardless");

            Assert.That(sqHero.Tokens.HasToken(TokenType.Shaken), Is.True,
                "a declined Star Quest skill FAILS, so the Shaken token it would have removed stays");
            Assert.That(aofHero.Tokens.HasToken(TokenType.Shaken), Is.False,
                "an AoF: Quest skill resolves");
        }

        [Test]
        public void TheProfilesNumbers_AreTheirBooks()
        {
            // Read straight from the two core books, so a reader can check them without a PDF.
            Assert.That(QuestRulesProfile.StarQuest.ExtraActionPower, Is.EqualTo(3));
            Assert.That(QuestRulesProfile.StarQuest.RevivePower, Is.EqualTo(3));
            Assert.That(QuestRulesProfile.StarQuest.FailedSkillPaymentIsOptional, Is.True);
            Assert.That(QuestRulesProfile.StarQuest.FailedCastMayBeBought, Is.False);
            Assert.That(QuestRulesProfile.StarQuest.ScoutForwardInches, Is.EqualTo(9f));
            Assert.That(QuestRulesProfile.StarQuest.VillainAuraInches, Is.EqualTo(6f));
            Assert.That(QuestRulesProfile.StarQuest.VillainAuraIsMandatory, Is.True);
            Assert.That(QuestRulesProfile.StarQuest.MeleeReachHorizontalInches, Is.EqualTo(3f));

            Assert.That(QuestRulesProfile.AoFQuest.ExtraActionPower, Is.EqualTo(2));
            Assert.That(QuestRulesProfile.AoFQuest.RevivePower, Is.EqualTo(2));
            Assert.That(QuestRulesProfile.AoFQuest.FailedSkillPaymentIsOptional, Is.False);
            Assert.That(QuestRulesProfile.AoFQuest.FailedCastMayBeBought, Is.True);
            Assert.That(QuestRulesProfile.AoFQuest.ScoutForwardInches, Is.EqualTo(12f));
            Assert.That(QuestRulesProfile.AoFQuest.VillainAuraInches, Is.EqualTo(12f));
            Assert.That(QuestRulesProfile.AoFQuest.VillainAuraIsMandatory, Is.False);
            Assert.That(QuestRulesProfile.AoFQuest.MeleeReachHorizontalInches, Is.EqualTo(2f));
            Assert.That(QuestRulesProfile.AoFQuest.MeleeReachVerticalInches, Is.EqualTo(4f));

            // Both costs that the two books agree on, so a future edit cannot drift one of them.
            Assert.That(QuestRulesProfile.AoFQuest.PassStatTestPower,
                Is.EqualTo(QuestRulesProfile.StarQuest.PassStatTestPower));
            Assert.That(QuestRulesProfile.AoFQuest.IgnoreShakenPower,
                Is.EqualTo(QuestRulesProfile.StarQuest.IgnoreShakenPower));
            Assert.That(QuestRulesProfile.AoFQuest.FailedSkillPower,
                Is.EqualTo(QuestRulesProfile.StarQuest.FailedSkillPower));

            // And a non-Quest system never reaches a Quest number, but reads Star Quest's if it asks.
            Assert.That(QuestRulesProfile.For(EGameSystem.GrimdarkFuture),
                Is.SameAs(QuestRulesProfile.StarQuest));
        }

    }
}
