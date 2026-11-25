using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Blast's "ignores cover" facet. The cover bonus is
    // computed by the REAL CoverCheckStage; with a Blast attacker it is dropped to 0. Also checks the
    // shared SightRuleQueries derivation that feeds the per-weapon flags on the targeting + movement
    // resolver requests. Geometry mirrors CoverMajorityTests: attacker at (0,5), Cover terrain at
    // x:8-12 z:3-7, a defender at (20,5) whose sight line passes through the cover.
    [TestFixture]
    public class BlastCoverRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position InCoverPos  = new Position(20, 5);
        private static readonly RectangularZone CoverRect = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private DataBinding<UnitData> _attacker = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _store.Create(new TerrainData(ETerrainType.Cover, CoverRect));
            _attacker = MakeUnit(new[] { AttackerPos });
        }

        [Test]
        public async Task NoBlast_DefenderInCover_KeepsCoverBonus()
        {
            CoverCheckResults result = await RunStage(MakeUnit(new[] { InCoverPos }));
            Assert.That(result.DefenseRollBonus, Is.EqualTo(1), "a defender in cover gets +1 defense.");
        }

        [Test]
        public async Task Blast_DefenderInCover_DropsCoverBonus()
        {
            AttachBlast(_attacker);
            CoverCheckResults result = await RunStage(MakeUnit(new[] { InCoverPos }));
            Assert.That(result.DefenseRollBonus, Is.EqualTo(0), "Blast ignores the target's cover — no bonus.");
        }

        [Test]
        public async Task DivineMaelstrom_DefenderInTheOpen_GetsCover()
        {
            // Living Spells (build 3.41): "units within 6\" count as being in Cover terrain".
            var open = new Position(20, 30);
            CoverCheckResults before = await RunStage(MakeUnit(new[] { open }));
            Assert.That(before.DefenseRollBonus, Is.EqualTo(0), "out in the open");
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = EAdvancedRules.LivingSpells;
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
            var state = new LivingSpellsState();
            state.OnTable.Add(new LivingSpellPiece { Owner = "x", OwnerName = "X", Name = LivingSpells.DivineMaelstrom, X = 24f, Z = 30f });
            LivingSpells.Save(_ctx, state);
            CoverCheckResults after = await RunStage(MakeUnit(new[] { open }));
            Assert.That(after.DefenseRollBonus, Is.EqualTo(1), "within 6\" of the Maelstrom");
        }

        [Test]
        public void SightRuleQueries_DeriveIgnoresCover_FromBlast()
        {
            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0);

            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, _ctx.RuleEvaluator), Is.False,
                "no rule → does not ignore cover.");

            AttachBlast(_attacker);
            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, _ctx.RuleEvaluator), Is.True,
                "Blast → ignores cover (this is what flags the targeting/movement resolver requests).");
        }

        private async Task<CoverCheckResults> RunStage(DataBinding<UnitData> defender)
        {
            var layer = new NoOpLayer<ICombatMetadata>();
            var stage = new CoverCheckStage(_ctx, layer);
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, _attacker, defender, weapon, weaponCount: 1);

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out CoverCheckResults result), Is.True,
                "Stage must store a CoverCheckResults in metadata.");
            return result;
        }

        private static void AttachBlast(DataBinding<UnitData> unit) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(3) }));

        private DataBinding<UnitData> MakeUnit(IEnumerable<Position> positions)
        {
            var models = positions.Select(pos =>
            {
                var md = new ModelData(
                    baseRadiusInches: 0.75f,
                    weapons: new List<Weapon>(),
                    initialPosition: pos,
                    gameDataStore: _store);
                return _store.GetDataBinding<ModelData>(_store.Create(md));
            }).ToList();

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
