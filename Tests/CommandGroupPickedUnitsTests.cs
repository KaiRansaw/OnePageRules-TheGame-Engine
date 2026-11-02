using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.ArmyBuilding;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #439 - Banner and Musician confer on two picked friendly units in the four games whose books say
    /// so, and the picked units lose the rule when the bearer dies.
    ///
    /// <para>
    /// Source, all from Army Forge's verbatim text (corroborated by the two core books that print a
    /// Command Groups section): <b>narrow</b> in Grimdark Future and Age of Fantasy - <i>"This model and
    /// its unit get ..."</i> - and <b>wide</b> in GF: Firefight, AoF: Skirmish, GF: Star Quest and
    /// AoF: Quest - <i>"This model's unit and up to 2 friendly units picked before the start of the game
    /// get ... If this model is killed, picked models that are deployed lose the rule."</i>
    /// </para>
    ///
    /// <para>
    /// The engine shipped ONE shared definition for all six systems, so the wide form was simply absent.
    /// What these tests pin is the three parts that can each fail independently: the per-system pick
    /// allowance, the grant actually reaching the picked unit's rules, and the revocation being tied to
    /// <b>that</b> bearer rather than to any death.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CommandGroupPickedUnitsTests
    {
        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _us;
        private PlayerID _them;

        /// <summary>
        /// Banner as the books gate it after #438 - the combat-only morale bonus. Registered on the
        /// resolver so a RuleGrant token naming "Banner" resolves back to real behaviour, which is the
        /// whole point of granting by token.
        /// </summary>
        private static SpecialRuleDefinition BannerDefinition() => new("Banner",
            new[]
            {
                new HookEntry(EHookID.Morale_OnPreMoraleTest,
                    new Condition.MoraleTriggeredByCombat(),
                    new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>());

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(BannerDefinition());
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // -- The per-system allowance ------------------------------------------------------------------

        [TestCase(EGameSystem.GrimdarkFuture, 0)]
        [TestCase(EGameSystem.AgeOfFantasy, 0)]
        [TestCase(EGameSystem.GFFirefight, 2)]
        [TestCase(EGameSystem.AoFSkirmish, 2)]
        [TestCase(EGameSystem.GFStarQuest, 2)]
        [TestCase(EGameSystem.AoFQuest, 2)]
        public void TheNumberOfPicks_IsItsOwnBooksNumber(EGameSystem system, int expected)
        {
            Assert.That(CoreRulesProfile.For(system).CommandGroupExtraPicks, Is.EqualTo(expected),
                expected == 0
                    ? $"{system}: its Banner reads \"this model AND ITS UNIT\" - nobody else."
                    : $"{system}: its Banner reads \"and up to 2 friendly units picked before the game\".");
        }

        /// <summary>
        /// <b>AoF: Skirmish alone</b> caps Command Groups per army (<i>"Each army may only have one model
        /// with one of the following upgrades"</i>). Firefight's core book has no Command Groups section
        /// at all and Army Forge's summaries carry no cap, so there is no source for an army-wide cap
        /// there - and reading AoFS's clause across to Firefight because both are skirmish games is the
        /// mistake CLAUDE.md names as the easiest one to make in this codebase. Pinned so the shared
        /// skirmish profile cannot quietly absorb it.
        /// </summary>
        [Test]
        public void OnlyAoFSkirmish_CapsCommandGroupsPerArmy()
        {
            Assert.That(CoreRulesProfile.For(EGameSystem.AoFSkirmish).CommandGroupCapIsPerArmy, Is.True,
                "AoFS v3.6.0: \"Each army may only have one model with one of the following upgrades.\"");

            foreach (EGameSystem other in new[]
                     {
                         EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight,
                         EGameSystem.GFStarQuest, EGameSystem.AoFQuest,
                     })
            {
                Assert.That(CoreRulesProfile.For(other).CommandGroupCapIsPerArmy, Is.False,
                    $"{other}: no book of its own caps Command Groups per army.");
            }
        }

        /// <summary>
        /// Sergeant is the third Command Group upgrade and confers on nobody - <i>"This model gets AP(+1)
        /// when attacking"</i> in every system. Pinned because adding it to the conferring list would
        /// hand every picked unit a free AP bonus, and the list is the only thing standing between the
        /// two shapes.
        /// </summary>
        [Test]
        public void OnlyBannerAndMusicianConfer_NotSergeant()
        {
            Assert.That(CoreRulesProfile.ConferringCommandGroupRules,
                Is.EquivalentTo(new[] { "Banner", "Musician" }));
        }

        // -- The grant reaching the picked unit's rules -------------------------------------------------

        /// <summary>
        /// The mechanism test, and the one that would catch a grant that looked right and did nothing: a
        /// picked unit holding the token must get the actual morale bonus, through the real morale engine.
        /// </summary>
        [Test]
        public async Task APickedUnit_GetsTheRealMoraleBonus()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> picked = MakeUnit("Riflemen", _us, quality: 5);

            GrantBannerFrom(bearer, picked);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), picked.GetValue(), baseRollNeeded: 5, EMoraleTrigger.ShootingOrMelee);

            Assert.That(outcome.RollNeeded, Is.EqualTo(4),
                "the granted Banner must ease the picked unit's threshold exactly as it eases the "
                + "bearer's own unit - a token that resolves to nothing is the failure mode here.");
            Assert.That(outcome.Passed, Is.True);
        }

        /// <summary>And a unit that was NOT picked gets nothing, so the test above is the grant's doing.</summary>
        [Test]
        public async Task AnUnpickedUnit_GetsNothing()
        {
            DataBinding<UnitData> unpicked = MakeUnit("Riflemen", _us, quality: 5);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), unpicked.GetValue(), baseRollNeeded: 5, EMoraleTrigger.ShootingOrMelee);

            Assert.That(outcome.RollNeeded, Is.EqualTo(5));
            Assert.That(outcome.Passed, Is.False);
        }

        /// <summary>
        /// #438's gate still applies to the granted copy: a picked unit's Banner is no help against a
        /// spell or a campaign event either. Pinned because the grant goes through a different path
        /// (token resolution) from the bearer's static attachment, and only one of the two was tested.
        /// </summary>
        [Test]
        public async Task APickedUnitsBanner_IsStillCombatOnly()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> picked = MakeUnit("Riflemen", _us, quality: 5);

            GrantBannerFrom(bearer, picked);

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), picked.GetValue(), baseRollNeeded: 5, EMoraleTrigger.OtherSource);

            Assert.That(outcome.RollNeeded, Is.EqualTo(5),
                "granted or native, the books gate Banner on \"triggered by shooting or melee\".");
        }

        // -- "If this model is killed, picked models ... lose the rule" ---------------------------------

        [Test]
        public async Task WhenTheBearerDies_ThePickedUnitLosesIt()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> picked = MakeUnit("Riflemen", _us, quality: 5);
            GrantBannerFrom(bearer, picked);

            KillEveryModel(bearer);
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(die: 4), bearer.GetValue(), killer: null);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(picked.GetValue(), "Banner"), Is.False,
                "the book: \"If this model is killed, picked models that are deployed lose the rule.\"");

            MoraleUtilities.MoraleTestOutcome outcome = await MoraleUtilities.TakeMoraleTest(
                Ctx(die: 4), picked.GetValue(), baseRollNeeded: 5, EMoraleTrigger.ShootingOrMelee);

            Assert.That(outcome.RollNeeded, Is.EqualTo(5),
                "and the bonus is really gone, not just the token - checked through the morale engine.");
        }

        /// <summary>
        /// The revocation is tied to the BEARER, not to deaths in general. Without
        /// <c>OwnerUnitID</c> on the token this would pass anyway for the wrong reason, so the unrelated
        /// unit here is in the same army and dies the same way.
        /// </summary>
        [Test]
        public async Task WhenSomeOtherUnitDies_ThePickedUnitKeepsIt()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> picked = MakeUnit("Riflemen", _us, quality: 5);
            DataBinding<UnitData> bystander = MakeUnit("Scouts", _us);
            GrantBannerFrom(bearer, picked);

            KillEveryModel(bystander);
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(die: 4), bystander.GetValue(), killer: null);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(picked.GetValue(), "Banner"), Is.True,
                "only the bearer's death takes the rule away.");
        }

        /// <summary>
        /// Two bearers, two picks, one dies: the survivor's grant must stand. This is what the
        /// <c>OwnerUnitID</c> on the token buys over the cheaper "is any banner still alive in this army"
        /// test, which would have revoked both.
        /// </summary>
        [Test]
        public async Task WithTwoBearers_OnlyTheDeadOnesPickLosesIt()
        {
            DataBinding<UnitData> firstBearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> secondBearer = MakeUnit("Honour Guard", _us, attachBanner: true);
            DataBinding<UnitData> firstPick = MakeUnit("Riflemen", _us, quality: 5);
            DataBinding<UnitData> secondPick = MakeUnit("Gunners", _us, quality: 5);

            GrantBannerFrom(firstBearer, firstPick);
            GrantBannerFrom(secondBearer, secondPick);

            KillEveryModel(firstBearer);
            await UnitDestructionNotifier.NotifyUnitDestroyed(Ctx(die: 4), firstBearer.GetValue(), killer: null);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(firstPick.GetValue(), "Banner"), Is.False);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(secondPick.GetValue(), "Banner"), Is.True,
                "the second banner is still standing, so its pick keeps the rule.");
        }

        // -- Who may be picked --------------------------------------------------------------------------

        [Test]
        public void CarriesRule_MatchesCaseInsensitively_AndTheRequestedName()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);

            Assert.That(PickCommandGroupUnitsStage.CarriesRule(bearer.GetValue(), "banner"), Is.True,
                "same matching as Condition.UnitHasRule - case-insensitive, so an aliased Banner counts.");
            Assert.That(PickCommandGroupUnitsStage.CarriesRule(bearer.GetValue(), "Musician"), Is.False);
        }

        /// <summary>
        /// A Banner held on a MODEL still makes its unit a bearer. Command Group upgrades normally fold
        /// onto the unit, because the effect is unit-wide - but the hero merge relocates a joined hero's
        /// own unit-scoped rules onto the hero model (#183/#197), so a hero carrying a Banner into a
        /// squad would be invisible to a unit-level check and its banner would confer on nobody at all.
        /// Exactly the shape of failure the Villain bug was (a rule fully implemented and completely
        /// unreachable), so it is checked rather than assumed away.
        /// </summary>
        [Test]
        public void CarriesRule_AlsoSeesARuleHeldOnAModel()
        {
            DataBinding<UnitData> unit = MakeUnit("Warband", _us);
            unit.GetValue().ModelBindings[0].GetValue()
                .AttachRuleDefinition(new ResolvedRule("Banner", BannerDefinition()));

            Assert.That(PickCommandGroupUnitsStage.CarriesRule(unit.GetValue(), "Banner"), Is.True,
                "a model-level Banner is still a Banner in the unit.");
        }

        [Test]
        public void HoldsGrantFor_SeesAGrantedCopy_AndDoesNotConfuseRules()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> picked = MakeUnit("Riflemen", _us);
            GrantBannerFrom(bearer, picked);

            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(picked.GetValue(), "Banner"), Is.True);
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(picked.GetValue(), "Musician"), Is.False,
                "a Banner grant is not a Musician grant - the payload's rule name is what distinguishes "
                + "them, and both ride the same token type.");
        }

        // -- Guarding against a reply the front end should not have sent --------------------------------

        /// <summary>
        /// The allowance is enforced engine-side, not taken on trust from whoever answered. A reply with
        /// more units than the book allows is clamped - and it matters more than it looks, because the
        /// roll-modifier sinks ADD, so an over-long or duplicated reply would silently multiply the bonus
        /// rather than fail somewhere visible.
        /// </summary>
        [Test]
        public void MorePicksThanAllowed_AreClamped()
        {
            DataBinding<UnitData> a = MakeUnit("A", _us);
            DataBinding<UnitData> b = MakeUnit("B", _us);
            DataBinding<UnitData> c = MakeUnit("C", _us);
            var candidates = new[] { a, b, c };

            List<DataBinding<UnitData>> accepted = PickCommandGroupUnitsStage.AcceptablePicks(
                candidates, new[] { a, b, c }, maxPicks: 2, "Banner");

            Assert.That(accepted, Is.EqualTo(new[] { a, b }),
                "the first two, in the order replied - not an arbitrary subset.");
        }

        [Test]
        public void TheSameUnitTwice_IsGrantedOnce()
        {
            DataBinding<UnitData> a = MakeUnit("A", _us);
            DataBinding<UnitData> b = MakeUnit("B", _us);

            List<DataBinding<UnitData>> accepted = PickCommandGroupUnitsStage.AcceptablePicks(
                new[] { a, b }, new[] { a, a }, maxPicks: 2, "Banner");

            Assert.That(accepted, Is.EqualTo(new[] { a }),
                "a duplicate would double the bonus, since the modifier sinks add.");
        }

        [Test]
        public void AUnitThatWasNeverOffered_IsRefused()
        {
            DataBinding<UnitData> offered = MakeUnit("Offered", _us);
            DataBinding<UnitData> notOffered = MakeUnit("Not offered", _us);

            List<DataBinding<UnitData>> accepted = PickCommandGroupUnitsStage.AcceptablePicks(
                new[] { offered }, new[] { notOffered, offered }, maxPicks: 2, "Banner");

            Assert.That(accepted, Is.EqualTo(new[] { offered }),
                "a unit the stage never put on offer - an enemy, or the bearer itself - is not grantable "
                + "just because a reply named it.");
        }

        [Test]
        public void AUnitAnotherBearerAlreadyCovered_IsRefused()
        {
            DataBinding<UnitData> bearer = MakeUnit("Colour Party", _us, attachBanner: true);
            DataBinding<UnitData> alreadyCovered = MakeUnit("Riflemen", _us);
            GrantBannerFrom(bearer, alreadyCovered);

            List<DataBinding<UnitData>> accepted = PickCommandGroupUnitsStage.AcceptablePicks(
                new[] { alreadyCovered }, new[] { alreadyCovered }, maxPicks: 2, "Banner");

            Assert.That(accepted, Is.Empty,
                "two banners covering one unit would stack to +2 morale, which no book grants.");
        }

        // -- AoF: Skirmish's army-wide cap -------------------------------------------------------------

        /// <summary>
        /// <i>"Each army may only have one model with one of the following upgrades."</i> Two Banners in
        /// one Skirmish army is a list the book forbids, and the book data's own <c>maxPicks: 1</c> only
        /// enforces the per-UNIT form, so nothing caught it.
        /// </summary>
        [Test]
        public void InSkirmish_TwoCommandGroupUpgrades_AreWarnedAbout()
        {
            ArmyListFile army = ArmyWithCommandGroups(GameSystems.AoFSkirmish, "Banner", "Banner");

            Assert.That(ForceOrgValidator.Validate(army),
                Has.Some.Contains("Too many Command Group upgrades: 2"));
        }

        /// <summary>One is fine, and the cap must not fire on a legal list.</summary>
        [Test]
        public void InSkirmish_OneCommandGroupUpgrade_IsFine()
        {
            ArmyListFile army = ArmyWithCommandGroups(GameSystems.AoFSkirmish, "Banner");

            Assert.That(ForceOrgValidator.Validate(army),
                Has.None.Contains("Command Group"));
        }

        /// <summary>
        /// The cap counts UPGRADES, not units carrying one: each is bought for a single model, so a
        /// Banner model and a Sergeant model are two models with Command Group upgrades even in one
        /// squad - which is what a cap worded "one model with one of the following" forbids. Sergeant
        /// counts here although it confers on nobody.
        /// </summary>
        [Test]
        public void InSkirmish_TheCapCountsUpgrades_NotUnits()
        {
            var army = new ArmyListFile
            {
                Name = "One Squad", GameSystem = GameSystems.AoFSkirmish, PointsLimit = 300,
                Units = new List<UnitFileEntry>
                {
                    new UnitFileEntry
                    {
                        Name = "Spearmen", ModelCount = 5, Quality = 4, Defense = 4,
                        SpecialRules = new List<SpecialRuleEntry>
                        {
                            new SpecialRuleEntry_Core("Banner"),
                            new SpecialRuleEntry_Core("Sergeant"),
                        },
                    },
                },
            };

            Assert.That(ForceOrgValidator.Validate(army),
                Has.Some.Contains("Too many Command Group upgrades: 2"));
        }

        /// <summary>
        /// And it fires for AoF: Skirmish ONLY. Age of Fantasy caps them per unit, Firefight's book has
        /// no Command Groups section at all, and no Quest book prints a cap - so a two-Banner list is
        /// legal in all of them and warning about it would be another game's rule leaking in.
        /// </summary>
        [TestCase(GameSystems.AgeOfFantasy)]
        [TestCase(GameSystems.GrimdarkFuture)]
        [TestCase(GameSystems.GFFirefight)]
        [TestCase(GameSystems.GFStarQuest)]
        [TestCase(GameSystems.AoFQuest)]
        public void InEveryOtherGame_TwoCommandGroupUpgrades_AreNotWarnedAbout(string system)
        {
            ArmyListFile army = ArmyWithCommandGroups(system, "Banner", "Banner");

            Assert.That(ForceOrgValidator.Validate(army), Has.None.Contains("Command Group"),
                $"{system}: no book of its own caps Command Groups per army.");
        }

        private static ArmyListFile ArmyWithCommandGroups(string system, params string[] rules)
        {
            var army = new ArmyListFile
            {
                Name = "Test", GameSystem = system, PointsLimit = 2000,
                Units = new List<UnitFileEntry>(),
            };

            for (int i = 0; i < rules.Length; i++)
            {
                army.Units.Add(new UnitFileEntry
                {
                    Name = $"Squad {i}", ModelCount = 5, Quality = 4, Defense = 4,
                    SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core(rules[i]) },
                });
            }

            return army;
        }

        // -- The bot's answer ---------------------------------------------------------------------------

        /// <summary>
        /// No book tells the AI how to pick, so the resolver takes the first options offered (army
        /// order). Pinned not because the choice is important but because it must be DETERMINISTIC: a bot
        /// that picked at random would make every AI-vs-AI benchmark irreproducible.
        /// </summary>
        [Test]
        public async Task TheBot_TakesItsFullAllowance_InTheOrderOffered()
        {
            DataBinding<UnitData> first = MakeUnit("Riflemen", _us);
            DataBinding<UnitData> second = MakeUnit("Gunners", _us);
            DataBinding<UnitData> third = MakeUnit("Scouts", _us);

            var request = new PickCommandGroupUnitsRequest(_us, "Colour Party", "Banner", maxPicks: 2,
                new[]
                {
                    new PickCommandGroupUnitsRequest.ValidOption(first, "Riflemen"),
                    new PickCommandGroupUnitsRequest.ValidOption(second, "Gunners"),
                    new PickCommandGroupUnitsRequest.ValidOption(third, "Scouts"),
                });

            CancellableResult<List<DataBinding<UnitData>>> reply =
                await new AiPickCommandGroupUnitsResolver().Resolve(request);

            Assert.That(reply, Is.InstanceOf<Selected<List<DataBinding<UnitData>>>>(),
                "a free buff is never declined by the bot.");

            List<DataBinding<UnitData>> picks = ((Selected<List<DataBinding<UnitData>>>)reply).Value;
            Assert.That(picks, Is.EqualTo(new[] { first, second }),
                "the first two offered, and never more than the allowance even with three on offer.");
        }

        [Test]
        public async Task TheBot_PicksNothing_WhenThereIsNothingToPick()
        {
            var request = new PickCommandGroupUnitsRequest(_us, "Colour Party", "Banner", maxPicks: 2,
                Array.Empty<PickCommandGroupUnitsRequest.ValidOption>());

            CancellableResult<List<DataBinding<UnitData>>> reply =
                await new AiPickCommandGroupUnitsResolver().Resolve(request);

            Assert.That(((Selected<List<DataBinding<UnitData>>>)reply).Value, Is.Empty,
                "the stage never issues an empty request, but a bot that cannot answer a free buff must "
                + "not end the game over it.");
        }

        // -- Helpers -----------------------------------------------------------------------------------

        /// <summary>
        /// Grants through the stage's OWN code, deliberately. Building the token here instead would make
        /// every test below a test of the token mechanism rather than of what the stage does with it -
        /// and the thing most likely to go wrong is the stage choosing the wrong clear trigger or
        /// forgetting the owner, both of which this way catch.
        /// </summary>
        private static void GrantBannerFrom(DataBinding<UnitData> bearer, DataBinding<UnitData> picked) =>
            PickCommandGroupUnitsStage.Grant(picked, "Banner", bearer.GetValue());

        private TriggeredMoveTestContext Ctx(int die) =>
            new(_store, new NoRequester(), ruleResolver: _resolver, diceRoller: new FixedDiceRoller(die));

        /// <summary>
        /// These tests grant and revoke directly rather than through the stage, so nothing here should
        /// ever ask a player anything. Throwing rather than returning a canned answer means a future
        /// change that starts prompting from one of these paths fails loudly instead of silently taking
        /// the default.
        /// </summary>
        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply> =>
                throw new InvalidOperationException(
                    "no prompt is expected on this path: " + request.GetType().Name);
        }

        private static void KillEveryModel(DataBinding<UnitData> unit)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }
        }

        private DataBinding<UnitData> MakeUnit(string name, PlayerID owner, int quality = 4,
            bool attachBanner = false, int modelCount = 2)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: quality, defense: 4, modelBindings: models);
            if (attachBanner)
            {
                unit.AttachRuleDefinition(new ResolvedRule("Banner", BannerDefinition()));
            }

            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
