using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #409 - the behaviour OPR 3.6.0 ADDED, which by definition had no 3.5.1 test to inherit. The 3.6
    // changes to rules that already existed are pinned in place, next to their 3.5.1 originals
    // (HitRollRuleIntegrationTests for Artillery/Indirect, ImpactRuleIntegrationTests for Counter,
    // AllModelsRuleGateIntegrationTests for the all-models -> most-models move, ScoutRuleIntegrationTests
    // for the 9" forward range). What lands here is the genuinely new:
    //
    //   * the Tough(9)+ Hero morale aura
    //   * Scout's "over 9\" from non-Scout enemy units" keep-out, including the Scout-vs-Scout exemption
    //   * Counter's Fatigue gate
    [TestFixture]
    public class Opr360RuleChangeTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _player;
        private PlayerID _ally;
        private PlayerID _enemy;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
            _player = new PlayerID(System.Guid.NewGuid());
            _ally = new PlayerID(System.Guid.NewGuid());
            _enemy = new PlayerID(System.Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _player, _ally }));
            _store.Create(new TeamData(1, new List<PlayerID> { _enemy }));
        }

        // ── The Tough(9)+ Hero morale aura ──────────────────────────────────────────────────────────
        //
        // "The Hero's unit, and units within 6" of friendly Heroes with Tough(9) or higher, may use the
        // Hero's Quality for Morale Tests as long as the Hero isn't Shaken."

        [Test]
        public void BigHeroAura_LendsItsQuality_ToAFriendlyUnitWithinSixInches()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeBigHero(_player, quality: 2, at: new Position(4, 0));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(2),
                "a Tough(9)+ Hero 4\" away lends its Quality 2 to the unit's Morale Tests.");
        }

        [Test]
        public void BigHeroAura_DoesNotReachBeyondSixInches()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            // Centre-to-centre 10" minus two 0.75" bases = 8.5" apart.
            MakeBigHero(_player, quality: 2, at: new Position(10, 0));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(5),
                "8.5\" is outside the 6\" aura, so the unit tests at its own Quality.");
        }

        [Test]
        public void BigHeroAura_IsSuppressedWhileTheHeroIsShaken()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            UnitData hero = MakeBigHero(_player, quality: 2, at: new Position(4, 0));
            hero.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(5),
                "the rule says 'as long as the Hero isn't Shaken' - a Shaken Hero lends nothing.");
        }

        [Test]
        public void BigHeroAura_ReachesAcrossATeam_ButNotAcrossTheTable()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeBigHero(_ally, quality: 2, at: new Position(4, 0));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(2),
                "'friendly' is team-relative, so a teammate's big Hero counts.");
        }

        [Test]
        public void BigHeroAura_IgnoresEnemyHeroes()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeBigHero(_enemy, quality: 2, at: new Position(4, 0));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(5),
                "an enemy Hero obviously does not steady your troops.");
        }

        [Test]
        public void BigHeroAura_NeedsToughNine_NotJustAnyHero()
        {
            UnitData grunts = MakeUnit(_player, quality: 5, at: new Position(0, 0), modelCount: 3);
            UnitData smallHero = MakeUnit(_player, quality: 2, at: new Position(4, 0), modelCount: 1);
            smallHero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            smallHero.AttachRuleDefinition(new ResolvedRule("Tough", CoreRuleCatalog.Tough,
                new RuleArgument[] { new RuleArgument.Int(6) }));

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, _context.TableState), Is.EqualTo(5),
                "Tough(6) is below the Tough(9) the aura requires - an ordinary joining Hero projects "
                + "nothing to units it has not joined.");
        }

        [Test]
        public void BigHeroAura_NeverWorsensAUnitsQuality()
        {
            UnitData veterans = MakeUnit(_player, quality: 3, at: new Position(0, 0), modelCount: 3);
            MakeBigHero(_player, quality: 5, at: new Position(4, 0));

            Assert.That(HeroStatRules.GetMoraleQuality(veterans, _context.TableState), Is.EqualTo(3),
                "the rule says the unit MAY use the Hero's Quality; a worse one is never taken.");
        }

        // ── Scout's new enemy keep-out ──────────────────────────────────────────────────────────────
        //
        // "may be deployed anywhere fully within 9\" of their deployment zone, over 9\" away from
        // non-Scout enemy units."

        [Test]
        public void ScoutKeepOut_RadiatesFromEveryModelOfANonScoutEnemy()
        {
            UnitData scout = MakeScout(_player, at: new Position(0, 0), modelCount: 1);
            MakeUnit(_enemy, quality: 4, at: new Position(20, 0), modelCount: 3);

            IReadOnlyList<PlacementDisc> discs =
                ScoutDeploymentRules.EnemyKeepOutDiscs(scout, _context.TableState);

            Assert.That(discs.Count, Is.EqualTo(3), "one disc per living enemy model.");
            Assert.That(discs[0].RadiusInches, Is.EqualTo(9f + 0.75f + 0.75f).Within(0.001f),
                "9\" edge-to-edge, widened by both bases so a centre-to-centre test means the same thing.");
        }

        [Test]
        public void ScoutKeepOut_ExemptsEnemyScouts()
        {
            UnitData scout = MakeScout(_player, at: new Position(0, 0), modelCount: 1);
            MakeScout(_enemy, at: new Position(20, 0), modelCount: 3);

            Assert.That(ScoutDeploymentRules.EnemyKeepOutDiscs(scout, _context.TableState), Is.Empty,
                "the rule says NON-Scout enemy units, so two forward screens may still meet.");
        }

        [Test]
        public void ScoutKeepOut_IgnoresFriendlyUnitsAndTheScoutItself()
        {
            UnitData scout = MakeScout(_player, at: new Position(0, 0), modelCount: 2);
            MakeUnit(_player, quality: 4, at: new Position(5, 0), modelCount: 3);
            MakeUnit(_ally, quality: 4, at: new Position(6, 0), modelCount: 3);

            Assert.That(ScoutDeploymentRules.EnemyKeepOutDiscs(scout, _context.TableState), Is.Empty,
                "only enemies push a Scout back.");
        }

        [Test]
        public void ScoutKeepOut_IgnoresDeadEnemyModels()
        {
            UnitData scout = MakeScout(_player, at: new Position(0, 0), modelCount: 1);
            UnitData enemies = MakeUnit(_enemy, quality: 4, at: new Position(20, 0), modelCount: 3);
            IModel casualty = enemies.Models.First();
            casualty.DealWounds(casualty.TotalWounds);

            Assert.That(ScoutDeploymentRules.EnemyKeepOutDiscs(scout, _context.TableState).Count,
                Is.EqualTo(2), "a dead model has no presence to keep away from.");
        }

        // ── Counter's Fatigue gate ──────────────────────────────────────────────────────────────────

        [Test]
        public void Counter_StrikesFirst_WhenNotFatigued()
        {
            UnitData charger = MakeUnit(_enemy, quality: 4, at: new Position(0, 0), modelCount: 1);
            UnitData defender = MakeCounterUnit(at: new Position(2, 0));

            Assert.That(StrikeFirstOps(charger, defender), Is.Not.Empty,
                "an unfatigued Counter unit still strikes first in 3.6.");
        }

        [Test]
        public void Counter_DoesNotStrikeFirst_WhileFatigued()
        {
            UnitData charger = MakeUnit(_enemy, quality: 4, at: new Position(0, 0), modelCount: 1);
            UnitData defender = MakeCounterUnit(at: new Position(2, 0));
            defender.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Fatigued));

            Assert.That(StrikeFirstOps(charger, defender), Is.Empty,
                "3.6 added 'unless this unit is Fatigued' - a unit that already swung this round loses it.");
        }

        // ── Rules new in 3.6.0 ──────────────────────────────────────────────────────────────────────

        // Cumbersome: "May only use Hold or Advance actions, but can't shoot when moving."
        // This is where 3.5.1 Artillery's Hold-only restriction went.

        [Test]
        public void Cumbersome_RestrictsActionsToHoldAndAdvance()
        {
            UnitData unit = MakeCumbersomeUnit();

            IReadOnlySet<EActionType> allowed = AllowedActions(unit);

            Assert.Multiple(() =>
            {
                Assert.That(allowed, Does.Contain(EActionType.Hold));
                Assert.That(allowed, Does.Contain(EActionType.Advance));
                Assert.That(allowed, Does.Not.Contain(EActionType.Rush), "Rush drops out.");
                Assert.That(allowed, Does.Not.Contain(EActionType.Charge), "Charge drops out.");
            });
        }

        [Test]
        public void Cumbersome_ForbidsShootingAfterMoving()
        {
            UnitData unit = MakeCumbersomeUnit();

            Assert.That(CumbersomeRules.CannotShootAfterMoving(unit, _context.RuleEvaluator), Is.True,
                "an Advance-and-shoot is exactly what Cumbersome takes away; the unit must pick one.");
        }

        [Test]
        public void Cumbersome_NamesItselfAsTheReason()
        {
            UnitData unit = MakeCumbersomeUnit();

            Assert.That(CumbersomeRules.ShootAfterMoveBlockSource(unit, _context.RuleEvaluator),
                Is.EqualTo("Cumbersome"),
                "the action menu says which rule closed the option, in the army's own wording.");
        }

        [Test]
        public void OrdinaryUnit_IsNotBlockedFromShootingAfterMoving()
        {
            UnitData plain = MakeUnit(_player, quality: 4, at: new Position(0, 0), modelCount: 1);

            Assert.That(CumbersomeRules.CannotShootAfterMoving(plain, _context.RuleEvaluator), Is.False,
                "the ordinary advance-and-shoot stays; only Cumbersome withdraws it.");
        }

        // Spell Amplifier: "Gets +1 to casting rolls." The first passive cast-roll modifier - everything
        // else that moves a casting roll is one-shot token state.

        [Test]
        public void SpellAmplifier_AddsOneToTheCastingRoll()
        {
            UnitData caster = MakeUnit(_player, quality: 4, at: new Position(0, 0), modelCount: 1);
            caster.AttachRuleDefinition(
                new ResolvedRule("Spell Amplifier", CoreRuleCatalog.SpellAmplifier));

            Assert.That(CastModifier(caster), Is.EqualTo(+1));
        }

        [Test]
        public void OrdinaryCaster_GetsNoPassiveCastModifier()
        {
            UnitData caster = MakeUnit(_player, quality: 4, at: new Position(0, 0), modelCount: 1);

            Assert.That(CastModifier(caster), Is.EqualTo(0));
        }

        // Assault: "Counts as both a ranged weapon and a melee weapon." Pool membership is decided from
        // the raw weapon list by range, so this one lives in the pool helpers rather than in dispatch.

        [Test]
        public void AssaultWeapon_AppearsInBothAttackPools()
        {
            UnitData unit = MakeAssaultUnit();

            Assert.Multiple(() =>
            {
                Assert.That(unit.GetRangedWeapons().Select(w => w.Name), Does.Contain("Assault Rifle"),
                    "it has a range, so it was always in the shooting pool.");
                Assert.That(unit.GetMeleeWeapons().Select(w => w.Name), Does.Contain("Assault Rifle"),
                    "3.6's Assault puts it in the melee pool as well - that is the whole rule.");
            });
        }

        [Test]
        public void OrdinaryRangedWeapon_StaysOutOfTheMeleePool()
        {
            var rifle = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            UnitData unit = MakeUnitWithWeapon(rifle);

            Assert.That(unit.GetMeleeWeapons(), Is.Empty,
                "without Assault a ranged weapon is not swung in melee.");
        }

        // ── helpers ─────────────────────────────────────────────────────────────────────────────────

        private IReadOnlyList<RuleOperation> StrikeFirstOps(UnitData charger, UnitData defender)
        {
            var evaluator = new RuleEvaluator(new FixedDiceRoller(4));
            return evaluator.EvaluateAll(new CounterTriggerContext(charger, defender),
                DetermineStrikeOrderStage.SubjectWithMeleeWeapons(defender))
                .OfType<RuleOperation.StrikeFirst>().ToList();
        }

        private IReadOnlySet<EActionType> AllowedActions(UnitData unit)
        {
            var allowed = new HashSet<EActionType>();
            foreach (RuleOperation op in _context.RuleEvaluator.EvaluateAll(
                         new ActionChoiceContext(unit), RuleParticipant.Actor(unit)))
            {
                if (op is RuleOperation.RestrictActions restrict)
                {
                    foreach (EActionType action in restrict.Allowed) allowed.Add(action);
                }
            }
            return allowed;
        }

        private int CastModifier(UnitData caster)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(_context.RuleEvaluator.EvaluateAll(
                new SpellCastAttemptContext(caster), RuleParticipant.Actor(caster)));
            return sink.Net(ERollKind.Cast);
        }

        private UnitData MakeCumbersomeUnit()
        {
            UnitData unit = MakeUnit(_player, quality: 4, at: new Position(0, 0), modelCount: 1);
            unit.AttachRuleDefinition(new ResolvedRule("Cumbersome", CoreRuleCatalog.Cumbersome));
            return unit;
        }

        private UnitData MakeAssaultUnit()
        {
            var rifle = new Weapon("Assault Rifle", rangeInches: 12f, attacks: 2, armorPenetration: 0);
            rifle.AttachRuleDefinition(new ResolvedRule("Assault", CoreRuleCatalog.Assault));
            return MakeUnitWithWeapon(rifle);
        }

        private UnitData MakeUnitWithWeapon(Weapon weapon)
        {
            var model = new ModelData(0.75f, new List<Weapon> { weapon }, new Position(0, 0), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Unit", quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        private UnitData MakeCounterUnit(Position at)
        {
            var blade = new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0);
            blade.AttachRuleDefinition(new ResolvedRule("Counter", CoreRuleCatalog.Counter));

            var model = new ModelData(0.75f, new List<Weapon> { blade }, at, _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_player, "Counter Unit", quality: 4, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        /// <summary>A single-model Hero unit with Tough(9) - the aura's minimum.</summary>
        private UnitData MakeBigHero(PlayerID owner, int quality, Position at)
        {
            UnitData hero = MakeUnit(owner, quality, at, modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule("Tough", CoreRuleCatalog.Tough,
                new RuleArgument[] { new RuleArgument.Int(HeroStatRules.BigHeroMinTough) }));
            return hero;
        }

        private UnitData MakeScout(PlayerID owner, Position at, int modelCount)
        {
            UnitData unit = MakeUnit(owner, quality: 4, at: at, modelCount: modelCount);
            unit.AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout));
            return unit;
        }

        private UnitData MakeUnit(PlayerID owner, int quality, Position at, int modelCount)
        {
            var bindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                // Spread models along Z so each is its own disc source without changing the X gap that
                // the distance assertions above are built on.
                var model = new ModelData(0.75f, new List<Weapon>(), new Position(at.x, at.z + i * 2f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, "Unit", quality: quality, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
