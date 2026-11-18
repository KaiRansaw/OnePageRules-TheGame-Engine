using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Campaign Rules 3.6.0: Idol "Once per game, the Hero and its unit may ignore becoming Shaken", Berserker
    // "... may ignore becoming Fatigued" (placeholders until 2026-11-18), plus the two joined-hero fixes found
    // wiring them: GF/AoF skills cover "the Hero and its unit", and a joined hero's own rules reach its unit's
    // morale tests.
    [TestFixture]
    public class CampaignOncePerGameIgnoreTests
    {
        private GameDataStore _store = null!;
        private PlayerID _us;
        private RuleResolver _resolver = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _resolver = new RuleResolver();
            foreach (SpecialRuleDefinition d in Campaign36RuleCatalog.All) _resolver.Register(d);
        }

        [Test]
        public async Task Idol_IgnoresBecomingShaken_OncePerGame()
        {
            DataBinding<UnitData> unit = MakeUnit("Squad", Campaign36RuleCatalog.Idol);
            var asker = new Asker(true);

            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(asker), unit);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False, "first time: ignored");
            Assert.That(asker.Asked, Is.EqualTo(1));

            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(asker), unit);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True, "once per game");
            Assert.That(asker.Asked, Is.EqualTo(1), "not asked again");
        }

        [Test]
        public async Task Idol_Declined_KeepsTheUse_AndAUnitWithoutIt_IsNeverAsked()
        {
            DataBinding<UnitData> unit = MakeUnit("Squad", Campaign36RuleCatalog.Idol);
            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(new Asker(false)), unit);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
            Assert.That(unit.GetValue().Tokens.HasToken(RuleEvaluator.UsedMarker("Skill: Idol")), Is.False);

            DataBinding<UnitData> plain = MakeUnit("Plain");
            var asker = new Asker(true);
            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(asker), plain);
            Assert.That(asker.Asked, Is.EqualTo(0));
            Assert.That(plain.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public async Task Berserker_IgnoresBecomingFatigued_OncePerGame()
        {
            DataBinding<UnitData> unit = MakeUnit("Squad", Campaign36RuleCatalog.Berserker);
            IUnit u = unit.GetValue();
            var ctx = Ctx(new Asker(true));
            Assert.That(await OncePerGameIgnore.Offer(ctx, u, OncePerGameIgnore.Berserker, TokenType.Fatigued, "Fatigued"),
                Is.True);
            Assert.That(await OncePerGameIgnore.Offer(ctx, u, OncePerGameIgnore.Berserker, TokenType.Fatigued, "Fatigued"),
                Is.False, "once per game");
        }

        [Test]
        public async Task ServicesApplyFatigue_HonoursBerserker()
        {
            DataBinding<UnitData> unit = MakeUnit("Squad", Campaign36RuleCatalog.Berserker);
            await new GameOperationServices(Ctx(new Asker(true))).ApplyFatigue(unit.GetValue());
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Fatigued), Is.False);
        }

        [Test]
        public async Task JoinedHeroIdol_CoversItsUnit()
        {
            DataBinding<UnitData> host = Joined(Campaign36RuleCatalog.Idol, skillsCoverHostUnit: false);
            await MoraleUtilities.ApplyShakenWithPresentation(Ctx(new Asker(true)), host);
            Assert.That(host.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False,
                "the skill sits on the hero model and is still found");
        }

        [Test]
        public void JoinedHeroSkills_StayUnitWide_InGfAndAof_HeroOnly_InTheSkirmishGames()
        {
            UnitData wide = Joined(Campaign36RuleCatalog.Suppressor, skillsCoverHostUnit: true).GetValue();
            Assert.That(wide.RuleDefinitions.Any(r => r.Definition.Name == "Skill: Suppressor"), Is.True,
                "GF/AoF: 'the Hero and its unit get AP(+1)'");

            UnitData narrow = Joined(Campaign36RuleCatalog.Suppressor, skillsCoverHostUnit: false).GetValue();
            Assert.That(narrow.RuleDefinitions.Any(r => r.Definition.Name == "Skill: Suppressor"), Is.False);
            Assert.That(narrow.Models.Any(m => m.RuleDefinitions.Any(r => r.Definition.Name == "Skill: Suppressor")),
                Is.True, "GFF/AoFS: 'the Hero gets' - on the hero model");

            Assert.That(CoreRulesProfile.For(EGameSystem.GrimdarkFuture).CampaignSkillsCoverHeroUnit, Is.True);
            Assert.That(CoreRulesProfile.For(EGameSystem.AgeOfFantasy).CampaignSkillsCoverHeroUnit, Is.True);
            Assert.That(CoreRulesProfile.For(EGameSystem.GFFirefight).CampaignSkillsCoverHeroUnit, Is.False);
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFSkirmish).CampaignSkillsCoverHeroUnit, Is.False);
        }

        [Test]
        public async Task JoinedHeroMotivator_ReachesTheUnitsMoraleTest()
        {
            DataBinding<UnitData> host = Joined(Campaign36RuleCatalog.Motivator, skillsCoverHostUnit: false);
            MoraleUtilities.MoraleTestOutcome test = await MoraleUtilities.TakeMoraleTest(
                Ctx(new Asker(true), die: 4), host.GetValue(), 5, EMoraleTrigger.Shooting);
            Assert.That(test.RollNeeded, Is.EqualTo(4), "Quality 5, +1 from the joined hero's Motivator");
        }

        // -- helpers -------------------------------------------------------------------------------------

        private DataBinding<UnitData> Joined(SpecialRuleDefinition skill, bool skillsCoverHostUnit)
        {
            UnitData host = MakeUnit("Host").GetValue();
            var heroModel = new ModelData(0.5f, new List<Weapon>(), new Position(14f, 10f), _store);
            var hero = new UnitData(_us, "Captain", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(heroModel)) });
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule(skill.Name, skill));
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            }, skillsCoverHostUnit: skillsCoverHostUnit);
            return _store.GetDataBinding<UnitData>(_store.Create(host));
        }

        private DataBinding<UnitData> MakeUnit(string name, params SpecialRuleDefinition[] rules)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(
                    new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store))));
            var unit = new UnitData(_us, name, quality: 5, defense: 4, modelBindings: models);
            foreach (SpecialRuleDefinition r in rules) unit.AttachRuleDefinition(new ResolvedRule(r.Name, r));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private TestGameContext Ctx(IPlayerRequestByID asker, int die = 4) =>
            new(_store, new FixedDiceRoller(die), ruleResolver: _resolver, playerRequester: asker);

        private sealed class Asker : IPlayerRequestByID
        {
            private readonly bool _answer;
            public int Asked;
            public Asker(bool answer) => _answer = answer;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is YesNoRequest) { Asked++; return Task.FromResult((TReply)(object)_answer); }
                throw new InvalidOperationException("Unexpected request: " + request.GetType());
            }
        }
    }
}
