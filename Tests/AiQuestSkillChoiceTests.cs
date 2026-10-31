using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Stages;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - the AI uses a Star Quest hero's Skill action, exactly once per activation.
    //
    // Why it matters: a hero gets "any one action PLUS one Skill action", so the Skill is free - taking it
    // does not consume the hero's action, and Choose Action reopens with Charge/Move/Shoot still on the
    // menu. The AI never took one, and because working an objective marker and revealing a search token
    // are BOTH Skill actions, an automated Star Quest mission could never complete an objective. The party
    // would walk to the markers and stand next to them; a five-wave run scored 0 XP. With this, a seeded
    // mission run logs "uses Objective" and "uses Discover" eight or nine times and reveals its search
    // tokens.
    //
    // Why exactly once: when no skill is usable, QuestActions does NOT increment the activation's skill
    // counter - there was nothing to spend - so the menu reopens still offering Skill. An AI that preferred
    // Skill unconditionally would pick it forever, until the decision watchdog faults the game. That is the
    // failure these tests mostly exist to prevent, and it is the reason the preference carries a latch at
    // all.
    [TestFixture]
    public class AiQuestSkillChoiceTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private AiStringSelectionResolver _resolver = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _resolver = new AiStringSelectionResolver(new TableState(_store), _player);
        }

        [Test]
        public async Task AFreshHeroUsesItsSkill()
        {
            // Ranked above Charge and Move deliberately: the skill is free, so there is nothing to trade
            // off. Charge is in the options here to prove that.
            string choice = await _resolver.Resolve(Menu(FreshHeroMenu()));

            Assert.That(choice, Is.EqualTo(QuestActions.SkillOption));
        }

        [Test]
        public async Task ItOnlyTriesOncePerActivation()
        {
            // The loop guard. The second menu has no Rest (the hero has acted), so it is the same
            // activation - and the AI must move on rather than ask for a skill again.
            UnitID hero = NewUnitId();

            string first = await _resolver.Resolve(Menu(FreshHeroMenu(), hero));
            string second = await _resolver.Resolve(Menu(MidActivationHeroMenu(), hero));

            Assert.That(first, Is.EqualTo(QuestActions.SkillOption));
            Assert.That(second, Is.Not.EqualTo(QuestActions.SkillOption),
                "no skill was usable, so the counter did not move and Skill is still on the menu - "
                + "picking it again is the infinite loop this latch exists to stop");
            Assert.That(second, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
        }

        [Test]
        public async Task ManyRepeatsInOneActivation_NeverPickSkillAgain()
        {
            // Stated as the watchdog would see it. One activation, the menu reopening over and over
            // because nothing was usable: the AI must settle on a real action and stay there.
            UnitID hero = NewUnitId();
            await _resolver.Resolve(Menu(FreshHeroMenu(), hero));

            for (int i = 0; i < 50; i++)
            {
                string choice = await _resolver.Resolve(Menu(MidActivationHeroMenu(), hero));
                Assert.That(choice, Is.Not.EqualTo(QuestActions.SkillOption), $"pick {i + 2}");
            }
        }

        [Test]
        public async Task TheNextActivationGetsItsOwnSkill()
        {
            // The latch must not be a one-skill-per-game rule. Rest back on the menu is the signal: it is
            // offered only to a hero that has not yet moved, attacked or acted, which is a fresh
            // activation. This is the case that matters for a single-hero party, where the same unit
            // activates again and again with nobody else in between.
            UnitID hero = NewUnitId();

            await _resolver.Resolve(Menu(FreshHeroMenu(), hero));
            await _resolver.Resolve(Menu(MidActivationHeroMenu(), hero));
            string nextRound = await _resolver.Resolve(Menu(FreshHeroMenu(), hero));

            Assert.That(nextRound, Is.EqualTo(QuestActions.SkillOption));
        }

        [Test]
        public async Task AnotherHeroGetsItsOwnSkill()
        {
            // A party activates one hero after another. The second must not inherit the first's latch.
            string first = await _resolver.Resolve(Menu(FreshHeroMenu(), NewUnitId()));
            string second = await _resolver.Resolve(Menu(FreshHeroMenu(), NewUnitId()));

            Assert.That(first, Is.EqualTo(QuestActions.SkillOption));
            Assert.That(second, Is.EqualTo(QuestActions.SkillOption));
        }

        [Test]
        public async Task AnOrdinaryUnitIsUnaffected()
        {
            // The regression guard for every other game. A non-Quest unit is never offered the Skill
            // option at all, so the ranking above Charge must not change what it picks.
            var options = new List<string>
            {
                ChooseActionStage.CHARGE_CHOICE_NAME,
                ChooseActionStage.MOVEMENT_CHOICE_NAME,
                ChooseActionStage.PASS_CHOICE_NAME,
            };

            Assert.That(await _resolver.Resolve(Menu(options)),
                Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME), "Charge still wins when it is offered");
        }

        [Test]
        public async Task AHeroThatHasAlreadySpentItsSkill_PicksARealAction()
        {
            // ChooseActionStage stops offering Skill once the counter has moved, so the AI has nothing to
            // decide - but the ranking must not then fall through to something worse than Charge.
            var options = new List<string>
            {
                QuestActions.RestOption,
                ChooseActionStage.CHARGE_CHOICE_NAME,
                ChooseActionStage.MOVEMENT_CHOICE_NAME,
                ChooseActionStage.PASS_CHOICE_NAME,
            };

            Assert.That(await _resolver.Resolve(Menu(options)),
                Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>A hero that has done nothing yet: Rest is offered, which is the "fresh activation"
        /// signal, and so is its Skill. Charge is present to prove the Skill outranks it.</summary>
        private static List<string> FreshHeroMenu() => new()
        {
            QuestActions.RestOption,
            QuestActions.SkillOption,
            ChooseActionStage.CHARGE_CHOICE_NAME,
            ChooseActionStage.MOVEMENT_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        /// <summary>The same activation after the skill attempt found nothing usable: no Rest any more,
        /// and Skill STILL offered because the counter never moved.</summary>
        private static List<string> MidActivationHeroMenu() => new()
        {
            QuestActions.SkillOption,
            ChooseActionStage.MOVEMENT_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        private static UnitID NewUnitId() => new UnitID(Guid.NewGuid());

        private ChooseActionRequest Menu(List<string> options, UnitID? unit = null) =>
            new ChooseActionRequest(_player, unit ?? NewUnitId(), options,
                new List<StringSelectionRequest.InvalidOption>());
    }
}
