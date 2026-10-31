using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #414 - Power spent on a cast roll, which GFSQ v2.1.0 added. Caster(X): *"Heroes may spend any number
    /// of tokens/Power before rolling to get +1 to the roll per token/Power, or to give an enemy Caster
    /// within 18" -1 to their roll per token/Power."*
    ///
    /// <para>
    /// v2.0.1 allowed tokens only before the roll, and let a hero buy a failed cast back afterwards for
    /// <c>X*2</c> Power. 2.1.0 deletes that retroactive rescue and moves Power to the front of the roll, so
    /// casting becomes a commitment instead of an insurance policy. The engine never implemented the
    /// retroactive rescue (checked: no such code path existed), so this fixture covers the half that was
    /// genuinely missing - and the <see cref="NoRescueAfterTheRoll"/> case at the bottom is here so nobody
    /// ever adds the 2.0.1 behaviour back from an old book.
    /// </para>
    ///
    /// <para>
    /// Driven through the real <see cref="CastSpellStage"/> rather than against
    /// <see cref="CastPower"/> directly: the whole point is that the Power reaches the ROLL, and a helper
    /// that returns the right number while the stage ignores it would pass a unit test and do nothing in
    /// play. The dice are fixed at a 3, which fails the base 4+ and succeeds at 3+, so one point of Power is
    /// exactly the difference between a failed and a successful cast.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestCastPowerTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;
        private PlayerID _enemy;

        private static readonly Position CasterAt = new Position(20f, 20f);
        private static readonly Position TargetAt = new Position(26f, 20f);
        // Inside the 18" counter-spell range, and nowhere near the (0,0) not-placed sentinel.
        private static readonly Position RivalAt = new Position(30f, 20f);

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
        }

        [Test]
        public async Task OnePowerShiftsTheCastRoll_AndIsSpent()
        {
            (DataBinding<UnitData> hero, DataBinding<UnitData> target) = Table(heroPower: 4);
            var requester = new ScriptedCaster(boostPower: 1);

            await RunCast(requester, hero);

            Assert.That(Slowed(target), Is.True,
                "a 3 fails the base 4+; the one power spent makes it a 3+, so the spell lands. If this "
                + "fails, Power is being taken and not reaching the roll - the worst of both.");
            Assert.That(hero.GetValue().QuestPower, Is.EqualTo(3), "and the power is actually gone.");
        }

        [Test]
        public async Task TheSameRollWithoutThePower_Fails()
        {
            // The control for the case above - without it, a spell that would have landed anyway proves
            // nothing about Power.
            (DataBinding<UnitData> hero, DataBinding<UnitData> target) = Table(heroPower: 4);
            var requester = new ScriptedCaster(boostPower: 0);

            await RunCast(requester, hero);

            Assert.That(Slowed(target), Is.False, "a 3 against the base 4+ is a failed cast.");
            Assert.That(hero.GetValue().QuestPower, Is.EqualTo(4), "and nothing was spent.");
        }

        [Test]
        public async Task ThePickerOffersExactlyTheHerosPower()
        {
            (DataBinding<UnitData> hero, _) = Table(heroPower: 2);
            var requester = new ScriptedCaster(boostPower: 0);

            await RunCast(requester, hero);

            Assert.That(requester.SpellRequest, Is.Not.Null);
            Assert.That(requester.SpellRequest!.AvailablePower, Is.EqualTo(2),
                "the picker shows what the hero can really pay, so a resolver never has to guess.");
        }

        [Test]
        public async Task AnOrdinaryArmyCaster_IsOfferedNoPower()
        {
            // The guard that keeps this change inside Star Quest. QuestPower is a plain int on every
            // UnitData, so a non-hero carrying one must still be offered nothing - otherwise a Grimdark
            // Future wizard would quietly gain a mechanic its book does not have.
            (DataBinding<UnitData> caster, _) = Table(heroPower: 0, asHero: false);
            caster.GetValue().QuestPower = 5;
            var requester = new ScriptedCaster(boostPower: 3);

            await RunCast(requester, caster);

            Assert.That(requester.SpellRequest!.AvailablePower, Is.Zero, "no hero sheet, no power to spend.");
            Assert.That(caster.GetValue().QuestPower, Is.EqualTo(5),
                "and a resolver asking to spend it anyway is refused rather than obeyed.");
        }

        [Test]
        public async Task OverspendingIsClamped_NeverWoundingTheHeroThroughABoost()
        {
            // QuestActions.SpendPower honours an overspend by taking a wound per missing power
            // (exhaustion), which is right when a rule charges a hero more than it has. It must NOT be
            // reachable from a boost spinner: that would turn a misbehaving resolver, or a fat finger, into
            // dead heroes.
            (DataBinding<UnitData> hero, _) = Table(heroPower: 1);
            float woundsBefore = hero.GetValue().Models.Sum(m => m.WoundsDealt);
            var requester = new ScriptedCaster(boostPower: 99);

            await RunCast(requester, hero);

            Assert.That(hero.GetValue().QuestPower, Is.Zero, "it spends what it has...");
            Assert.That(hero.GetValue().Models.Sum(m => m.WoundsDealt), Is.EqualTo(woundsBefore),
                "...and takes no exhaustion wounds for the rest.");
        }

        [Test]
        public async Task AHeroWithNoTokensIsStillAskedToCounterSpell_AndItsPowerHinders()
        {
            // The behaviour change this slice is most likely to be noticed for. Before v2.1.0 a caster with
            // an empty token pool had nothing to contribute, and the stage skipped it without asking. A hero
            // with Power now has a real decision, so an empty pool is no longer a reason to stay silent.
            (DataBinding<UnitData> hero, DataBinding<UnitData> target) = Table(heroPower: 0);
            DataBinding<UnitData> rival = AddRivalCaster(tokens: 0, power: 3);
            // The caster boosts by one token so the roll would otherwise land - the rival's power is what
            // decides this cast.
            var requester = new ScriptedCaster(boostTokens: 1, assistPower: 1);

            await RunCast(requester, hero);

            Assert.That(requester.AssistRequests.Count, Is.EqualTo(1),
                "a hero holding only power must still be offered the counter-spell.");
            Assert.That(requester.AssistRequests[0].AvailablePower, Is.EqualTo(3));
            Assert.That(requester.AssistRequests[0].AvailableTokens, Is.Zero);
            Assert.That(rival.GetValue().QuestPower, Is.EqualTo(2), "the power it spent is gone.");
            Assert.That(Slowed(target), Is.False,
                "+1 token from the caster and -1 power from the rival leaves the base 4+, which a 3 fails.");
        }

        [Test]
        public async Task AnEnemyHerosPowerCountsTowardsTheHinderWarning()
        {
            // The picker greys out boost past the point it can matter, using how much the enemy could
            // still take off the roll. Leaving Power out of that sum would cap the player's boost early in
            // exactly the situation where boosting matters most - a hero duel.
            (DataBinding<UnitData> hero, _) = Table(heroPower: 3);
            AddRivalCaster(tokens: 2, power: 4);
            var requester = new ScriptedCaster(boostPower: 0);

            await RunCast(requester, hero);

            Assert.That(requester.SpellRequest!.HinderTokensInRange, Is.EqualTo(6),
                "2 tokens + 4 power is what that rival can actually bring.");
        }

        [Test]
        public async Task NoRescueAfterTheRoll()
        {
            // v2.0.1: "Whenever a hero fails to cast a spell, it may spend X*2 power to count as having
            // cast it instead." v2.1.0 deleted that sentence. This pins the deletion: a failed cast asks
            // for nothing and costs nothing beyond what was committed before the roll.
            (DataBinding<UnitData> hero, DataBinding<UnitData> target) = Table(heroPower: 6);
            var requester = new ScriptedCaster(boostPower: 0);

            await RunCast(requester, hero);

            Assert.That(Slowed(target), Is.False, "the cast failed...");
            Assert.That(hero.GetValue().QuestPower, Is.EqualTo(6),
                "...and no power was taken to buy it back. If this ever fails, someone has re-implemented "
                + "the v2.0.1 rescue from an old book.");
            Assert.That(requester.PostRollPrompts, Is.Empty,
                "and the player was not asked to - a yes/no after the roll is how the old rule would look.");
        }

        // --- helpers ---------------------------------------------------------------------------------

        // A successful cast lands Effect.AddRule as a RuleGrant token on the target; a failed one lands
        // nothing. Same observable the aura tests use.
        private static bool Slowed(DataBinding<UnitData> target) =>
            target.GetValue().Tokens.GetTokenCount(TokenType.RuleGrant) > 0;

        private async Task RunCast(IPlayerRequestByID requester, DataBinding<UnitData> caster)
        {
            // A fixed 3: fails the base 4+ cast threshold, succeeds at 3+. One point of anything is the
            // whole difference, which is what makes every assertion above a single clean step.
            var ctx = new TriggeredMoveTestContext(_store, requester, new FixedDiceRoller(3));
            var unitCtx = new UnitActionContext(ctx, caster);
            unitCtx.Reset(caster);
            var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
            stage.OnFinished.Bind("OnFinished");
            await stage.Enter(unitCtx);
        }

        /// <summary>
        /// A caster with one Slow-granting spell and one enemy in range. <paramref name="asHero"/> false
        /// builds an ordinary army caster with no Star Quest hero sheet.
        /// </summary>
        private (DataBinding<UnitData> caster, DataBinding<UnitData> target) Table(int heroPower,
            bool asHero = true)
        {
            DataBinding<UnitData> caster = MakeUnit("Psychic", _player, CasterAt, ownArmy: false);
            AttachCaster(caster.GetValue());
            // Two tokens: one pays a threshold-1 spell, one is available as a token boost where a test
            // wants the roll moved without touching Power.
            caster.GetValue().Tokens.AddToken(
                new Token(TokenType.SpellTokens, 2, new TokenClearTrigger.ManualOnly()));
            if (asHero)
            {
                caster.GetValue().QuestHeroJson = new QuestHero
                {
                    Name = "Psychic", Power = System.Math.Max(heroPower, 1), Caster = 1,
                }.ToJson();
                caster.GetValue().QuestPower = heroPower;
            }

            var army = new ArmyData(_player, new List<DataBinding<UnitData>> { caster });
            army.SetSpells(new[] { SlowSpell() });
            _store.Create(army);

            DataBinding<UnitData> target = MakeUnit("Grunts", _enemy, TargetAt);
            return (caster, target);
        }

        /// <summary>An enemy Caster inside the 18" counter-spell range, optionally a Hero with Power.</summary>
        private DataBinding<UnitData> AddRivalCaster(int tokens, int power)
        {
            DataBinding<UnitData> rival = MakeUnit("Rival Psychic", _enemy, RivalAt);
            AttachCaster(rival.GetValue());
            if (tokens > 0)
            {
                rival.GetValue().Tokens.AddToken(
                    new Token(TokenType.SpellTokens, tokens, new TokenClearTrigger.ManualOnly()));
            }
            if (power > 0)
            {
                rival.GetValue().QuestHeroJson = new QuestHero
                {
                    Name = "Rival Psychic", Power = power, Caster = 1,
                }.ToJson();
                rival.GetValue().QuestPower = power;
            }
            return rival;
        }

        private DataBinding<UnitData> MakeUnit(string name, PlayerID owner, Position pos, bool ownArmy = true)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), pos, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (ownArmy) _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private static void AttachCaster(UnitData unit) =>
            unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
                new RuleArgument[] { new RuleArgument.Int(1) }));

        // Foe-affinity and non-damage, so "did the cast succeed" is one rule on the target rather than a
        // trip through the wound pipeline. Costs one token, leaving the second as spare boost.
        private static RuntimeSpell SlowSpell() =>
            new RuntimeSpell(
                new SpellDefinition("Mind Fog", 1,
                    new TargetSelector(18f, 1, 1, ETargetAffinity.Foe, RequireLineOfSight: false),
                    new Effect.AddRule("Slow", ELifetime.NextTrigger)),
                System.Array.Empty<ResolvedRule>());

        /// <summary>
        /// Picks the first castable spell with a scripted boost, the first target, and answers every
        /// counter-spell offer with a scripted spend. Records what it was asked, and keeps any yes/no that
        /// arrives - there should be none, which is what <see cref="NoRescueAfterTheRoll"/> asserts.
        /// </summary>
        private sealed class ScriptedCaster : IPlayerRequestByID
        {
            private readonly int _boostTokens, _boostPower, _assistTokens, _assistPower;

            public ScriptedCaster(int boostTokens = 0, int boostPower = 0, int assistTokens = 0,
                int assistPower = 0)
            {
                _boostTokens = boostTokens;
                _boostPower = boostPower;
                _assistTokens = assistTokens;
                _assistPower = assistPower;
            }

            public ChooseSpellRequest? SpellRequest { get; private set; }
            public List<CastAssistRequest> AssistRequests { get; } = new List<CastAssistRequest>();
            public List<string> PostRollPrompts { get; } = new List<string>();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                switch (request)
                {
                    case ChooseSpellRequest spellPick:
                        SpellRequest = spellPick;
                        int index = Enumerable.Range(0, spellPick.Spells.Count)
                            .First(i => spellPick.Spells[i].Castable);
                        return Task.FromResult((TReply)(object)new ChooseSpellReply(index, _boostTokens,
                            _boostPower));
                    case SelectionRequest<UnitData> targetPick:
                        return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                    case CastAssistRequest assist:
                        AssistRequests.Add(assist);
                        return Task.FromResult((TReply)(object)new CastAssistReply(_assistTokens,
                            _assistPower));
                    case YesNoRequest yesNo:
                        PostRollPrompts.Add(yesNo.QuestionText);
                        return Task.FromResult((TReply)(object)false);
                    default:
                        throw new System.InvalidOperationException("Unexpected request: " + request.GetType());
                }
            }
        }
    }
}
