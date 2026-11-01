using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Tests
{
    /// <summary>
    /// #423 - the Blast and Deadly clauses of <b>Solo &amp; Co-Op Rules v3.6.0</b>, which are the only two
    /// places the four books disagree about who the AI shoots at. Quoted rather than summarised:
    ///
    /// <para>
    /// <b>Blast</b> - "AI units with Blast weapons always target multi-model units <b>without Tough</b>
    /// first, and multi-model units with Tough second" (GF, AoF) / "...always target multi-model units
    /// first, and single-model units second" (Firefight, AoF: Skirmish).
    /// </para>
    /// <para>
    /// <b>Deadly</b> - "AI units with Deadly weapons always target single-model units <b>with Tough</b>
    /// first, and multi-model units with Tough second" (GF, AoF) / "...always target single-model units
    /// first, and multi-model units second" (Firefight, AoF: Skirmish).
    /// </para>
    /// <para>
    /// Every case here puts the "wrong" unit CLOSER, because closest-first is the general shooting clause
    /// and the whole question is whether the specific clause outranks it. A test where the preferred target
    /// is also the nearest would pass with the clause deleted.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiBlastDeadlyTargetingTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _store.Create(new TeamData(1, new List<PlayerID> { _player }));
        }

        /// <summary>
        /// A context playing the given system. The resolver reads the system off
        /// <c>TableState.Progress</c> - the same place every other per-system rule in the engine reads it
        /// - and Progress comes from a <see cref="GameProgressData"/> in the STORE rather than from the
        /// context's settings, so the record has to be created. (Setting it on <c>GameSettings</c> alone
        /// looks right, compiles, and silently leaves the system as Grimdark Future.)
        /// </summary>
        private TestGameContext Context(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            _store.Create(new GameProgressData(EResumeStage.MainPhase, 1,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), settings));
            return new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        // ── Blast ─────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Blast_GrimdarkFuture_PrefersAMultiModelUnitWithoutTough_OverTheNearerToughOne()
        {
            DataBinding<UnitData> shooter = Shooter("Bombard", "Blast");
            DataBinding<UnitData> nearToughSquad = Enemy("Tough Squad", x: 10f, models: 3, wounds: 3);
            DataBinding<UnitData> farSoftSquad = Enemy("Soft Squad", x: 30f, models: 3, wounds: 1);

            IUnit picked = await Pick(EGameSystem.GrimdarkFuture, shooter, nearToughSquad, farSoftSquad);

            Assert.That(picked.Name, Is.EqualTo("Soft Squad"),
                "multi-model WITHOUT Tough first, even though the Tough squad is twenty inches nearer.");
        }

        [Test]
        public async Task Blast_GrimdarkFuture_TakesAMultiModelToughUnit_OverASingleModel()
        {
            DataBinding<UnitData> shooter = Shooter("Bombard", "Blast");
            DataBinding<UnitData> nearSingle = Enemy("Walker", x: 10f, models: 1, wounds: 6);
            DataBinding<UnitData> farToughSquad = Enemy("Tough Squad", x: 30f, models: 3, wounds: 3);

            IUnit picked = await Pick(EGameSystem.GrimdarkFuture, shooter, nearSingle, farToughSquad);

            Assert.That(picked.Name, Is.EqualTo("Tough Squad"),
                "the GF clause ranks multi-model-with-Tough second and says nothing about single models, "
                + "so a lone model comes after both.");
        }

        // Firefight words the same clause differently, and this is the pair that proves the engine reads
        // the right book: the identical board gives a different answer.
        [Test]
        public async Task Blast_Firefight_TakesTheNearestMultiModelUnit_ToughOrNot()
        {
            DataBinding<UnitData> shooter = Shooter("Bombard", "Blast");
            DataBinding<UnitData> nearToughSquad = Enemy("Tough Squad", x: 10f, models: 3, wounds: 3);
            DataBinding<UnitData> farSoftSquad = Enemy("Soft Squad", x: 30f, models: 3, wounds: 1);

            IUnit picked = await Pick(EGameSystem.GFFirefight, shooter, nearToughSquad, farSoftSquad);

            Assert.That(picked.Name, Is.EqualTo("Tough Squad"),
                "Firefight's clause is just 'multi-model first', so both are band 0 and the general "
                + "closest-first key decides - the opposite answer to Grimdark Future on the same board.");
        }

        // ── Deadly ────────────────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task Deadly_GrimdarkFuture_PrefersASingleToughModel_OverANearerSquad()
        {
            DataBinding<UnitData> shooter = Shooter("Tank Hunter", "Deadly");
            DataBinding<UnitData> nearSquad = Enemy("Grunts", x: 10f, models: 5, wounds: 1);
            DataBinding<UnitData> farWalker = Enemy("Walker", x: 30f, models: 1, wounds: 6);

            IUnit picked = await Pick(EGameSystem.GrimdarkFuture, shooter, nearSquad, farWalker);

            Assert.That(picked.Name, Is.EqualTo("Walker"),
                "single-model WITH Tough first - Deadly's multiplier is wasted on one-wound models.");
        }

        [Test]
        public async Task Deadly_Firefight_PrefersAnySingleModel_OverANearerSquad()
        {
            DataBinding<UnitData> shooter = Shooter("Tank Hunter", "Deadly");
            DataBinding<UnitData> nearSquad = Enemy("Grunts", x: 10f, models: 5, wounds: 1);
            DataBinding<UnitData> farLoner = Enemy("Scout", x: 30f, models: 1, wounds: 1);

            IUnit picked = await Pick(EGameSystem.GFFirefight, shooter, nearSquad, farLoner);

            Assert.That(picked.Name, Is.EqualTo("Scout"),
                "Firefight's clause drops the Tough qualifier, so a lone one-wound model still comes "
                + "first - where Grimdark Future would rank it last.");
        }

        // The same board under the GF book, to pin that the Tough qualifier is what separates the two
        // readings rather than the model count.
        [Test]
        public async Task Deadly_GrimdarkFuture_RanksASingleSoftModelLast()
        {
            DataBinding<UnitData> shooter = Shooter("Tank Hunter", "Deadly");
            DataBinding<UnitData> nearLoner = Enemy("Scout", x: 10f, models: 1, wounds: 1);
            DataBinding<UnitData> farToughSquad = Enemy("Tough Squad", x: 30f, models: 3, wounds: 3);

            IUnit picked = await Pick(EGameSystem.GrimdarkFuture, shooter, nearLoner, farToughSquad);

            Assert.That(picked.Name, Is.EqualTo("Tough Squad"),
                "multi-model-with-Tough is the GF clause's second band; a single soft model is in neither.");
        }

        // ── The control ───────────────────────────────────────────────────────────────────────────────

        // A weapon with neither rule puts every target in the same band, so the general keys decide alone -
        // which is the behaviour every other shot in the game has, and what these tests must not disturb.
        [Test]
        public async Task PlainWeapon_StillShootsTheClosestTarget()
        {
            DataBinding<UnitData> shooter = Shooter("Rifle", ruleName: null);
            DataBinding<UnitData> nearSquad = Enemy("Grunts", x: 10f, models: 5, wounds: 1);
            DataBinding<UnitData> farWalker = Enemy("Walker", x: 30f, models: 1, wounds: 6);

            IUnit picked = await Pick(EGameSystem.GrimdarkFuture, shooter, nearSquad, farWalker);

            Assert.That(picked.Name, Is.EqualTo("Grunts"), "no Blast and no Deadly - closest, as always.");
        }

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        private async Task<IUnit> Pick(EGameSystem system, DataBinding<UnitData> shooter,
            params DataBinding<UnitData>[] targets)
        {
            TestGameContext context = Context(system);
            var resolver = new SoloCoOpRangedAttackResolver(context.TableState);
            CancellableResult<RangedAttackChoice> reply = await resolver.Resolve(Request(shooter, targets));

            Assert.That(reply, Is.InstanceOf<Selected<RangedAttackChoice>>(),
                "the AI always picks a shot when it has options.");
            return ((Selected<RangedAttackChoice>)reply).Value.TargetUnit.GetValue();
        }

        private ChooseRangedAttackRequest Request(DataBinding<UnitData> shooter,
            DataBinding<UnitData>[] targets)
        {
            Weapon weapon = shooter.GetValue().GetRangedWeapons()[0];
            var shooters = shooter.GetValue().ModelBindings.ToHashSet();
            List<WeaponTargetStats> stats = targets
                .Select(t => new WeaponTargetStats(t, shooters,
                    new HashSet<DataBinding<ModelData>>(), false, null))
                .ToList();

            return new ChooseRangedAttackRequest(_ai, "Choose Ranged Weapon", shooter,
                new List<WeaponOption> { new(weapon, stats, false, false, null, null) });
        }

        private DataBinding<UnitData> Shooter(string weaponName, string? ruleName)
        {
            var weapon = new Weapon(weaponName, rangeInches: 48f, attacks: 3, armorPenetration: 0);
            if (ruleName != null)
            {
                weapon.AttachRuleDefinition(new ResolvedRule(ruleName,
                    new SpecialRuleDefinition(ruleName, Array.Empty<HookEntry>(),
                        Array.Empty<ActivatedAbility>())));
            }

            var model = new ModelData(0.5f, new List<Weapon> { weapon }, new Position(0f, 0f), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_ai, "AI Gunners", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_ai, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        /// <param name="wounds">Max wounds per model: 1 for no Tough, 3+ for Tough.</param>
        private DataBinding<UnitData> Enemy(string name, float x, int models, int wounds)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, i * 1.2f), _store);
                if (wounds > 1) model.SetMaxWounds(wounds);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
