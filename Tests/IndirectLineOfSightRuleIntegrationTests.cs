using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // Vertical-slice integration test for #042: proves Indirect's "ignore intervening line of sight /
    // cover" facet (W9). A Blocking terrain piece sits between attacker (0,5) and defender (20,5), so a
    // normal weapon is occluded; with Indirect the REAL OcclusionCheckStage lets the shot through. Also
    // checks the shared SightRuleQueries derivation that feeds the per-weapon LoS-/cover-ignore flags on
    // the targeting + movement resolver requests.
    //
    // #314 regression guard: Takedown carried the same LoS-/cover-ignore hooks from 2026-06-11 until
    // 2026-08-02 (copied off Indirect by the #042 checklist's mapping row), which let snipers shoot
    // through Blocking terrain. Its rule text grants neither, so the Takedown cases here assert the
    // OPPOSITE of Indirect's.
    [TestFixture]
    public class IndirectLineOfSightRuleIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0, 5);
        private static readonly Position BehindWallPos = new Position(20, 5);
        private static readonly RectangularZone WallRect = new RectangularZone(8, 12, 3, 7);

        private GameDataStore _store = null!;
        private TestGameContext _ctx = null!;
        private DataBinding<UnitData> _attacker = null!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ctx = new TestGameContext(_store, new FixedDiceRoller(4));
            _store.Create(new TerrainData(ETerrainType.Blocking, WallRect));
            _attacker = MakeUnit(AttackerPos);
        }

        [Test]
        public async Task NoIndirect_BehindWall_ShotIsOccluded()
        {
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender);

            Assert.That(occluded, Is.True, "a Blocking wall on the sight line occludes a normal shot.");
        }

        [Test]
        public async Task Indirect_BehindWall_ShotNotOccluded()
        {
            AttachRule(_attacker, "Indirect", CoreRuleCatalog.Indirect);
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender);

            Assert.That(occluded, Is.False, "Indirect ignores intervening line of sight — the shot is not occluded.");
        }

        // #314: the bug the user hit - a sniper shooting through a Blocking wall.
        [Test]
        public async Task Takedown_BehindWall_ShotIsOccluded()
        {
            AttachRule(_attacker, "Takedown", CoreRuleCatalog.Takedown);
            DataBinding<UnitData> defender = MakeUnit(BehindWallPos);

            bool occluded = await RunOcclusionStage(_attacker, defender);

            Assert.That(occluded, Is.True,
                "Takedown re-scopes the attack to one model - it does NOT see through a Blocking wall.");
        }

        // #314: the same wrong facet also reached the resolvers' per-weapon flags, so pin the query too -
        // a Takedown weapon must report neither ignore, or the targeting UI offers the blocked shot.
        [Test]
        public void SightRuleQueries_Takedown_IgnoresNeitherLoSNorCover()
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            RuleEvaluator ev = _ctx.RuleEvaluator;

            AttachRule(_attacker, "Takedown", CoreRuleCatalog.Takedown);

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev), Is.False,
                "Takedown -> does not ignore LoS.");
            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, ev), Is.False,
                "Takedown -> does not ignore cover.");
        }

        [Test]
        public void SightRuleQueries_DeriveLoSAndCoverIgnore_FromIndirect()
        {
            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            RuleEvaluator ev = _ctx.RuleEvaluator;

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev), Is.False,
                "no rule → does not ignore LoS.");
            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, ev), Is.False,
                "no rule → does not ignore cover.");

            AttachRule(_attacker, "Indirect", CoreRuleCatalog.Indirect);

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev), Is.True,
                "Indirect → ignores LoS (flags the targeting/movement resolver requests).");
            Assert.That(SightRuleQueries.IgnoresCover(_attacker.GetValue(), weapon, ev), Is.True,
                "Indirect → also ignores cover.");
        }

        // Kai, 2026-11-18: moved a unit with an Indirect weapon and couldn't pick an out-of-sight enemy. Right by the book
        // (GF/GFF/AoF/AoFS 3.6.0: "When using Hold actions and shooting, may target enemies that are not in line of
        // sight") - but the shot screen only said "out of range or out of sight". The weapon now reports its switched-off
        // rule so the screen can name it.
        [Test]
        public void DormantLineOfSightRule_IsNamed_AfterAMove_AndNotOnAHold()
        {
            var weapon = new Weapon("Mortar", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            weapon.AttachRuleDefinition(new ResolvedRule("Indirect", CoreRuleCatalog.Indirect));
            RuleEvaluator ev = _ctx.RuleEvaluator;

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev), Is.True, "control: on a Hold it sees past walls");
            Assert.That(SightRuleQueries.DormantLineOfSightIgnoreSource(_attacker.GetValue(), weapon, ev), Is.Null,
                "a working Indirect is not 'switched off'");

            _attacker.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.MovedThisRound));

            Assert.That(SightRuleQueries.IgnoresTerrain(_attacker.GetValue(), weapon, ev), Is.False, "after moving it needs sight");
            Assert.That(SightRuleQueries.DormantLineOfSightIgnoreSource(_attacker.GetValue(), weapon, ev), Is.EqualTo("Indirect"));

            var plain = new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0);
            Assert.That(SightRuleQueries.DormantLineOfSightIgnoreSource(_attacker.GetValue(), plain, ev), Is.Null,
                "a weapon without the rule has nothing to explain");
        }

        // Returns true if the shot was occluded (OnOccluded fired, no result stored), false otherwise.
        private async Task<bool> RunOcclusionStage(DataBinding<UnitData> attacker, DataBinding<UnitData> defender)
        {
            var stage = new OcclusionCheckStage(_ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");
            stage.OnOccluded.Bind("occluded");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0);
            var metadata = new CombatMetadata(_ctx, attacker, defender, weapon, weaponCount: 1);

            await stage.Enter(metadata);

            return !metadata.QueryForResult(out OcclusionCheckResults _);
        }

        private static void AttachRule(DataBinding<UnitData> unit, string name, SpecialRuleDefinition def) =>
            unit.GetValue().AttachRuleDefinition(new ResolvedRule(name, def));

        private DataBinding<UnitData> MakeUnit(Position pos)
        {
            var md = new ModelData(
                baseRadiusInches: 0.75f,
                weapons: new List<Weapon>(),
                initialPosition: pos,
                gameDataStore: _store);
            var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(md));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "TestUnit",
                quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
