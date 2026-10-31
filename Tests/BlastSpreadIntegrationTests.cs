using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - Blast's spread through the REAL RollToHitStage, in the three systems that have it and the two
    // that don't.
    //
    // BlastSpreadRulesTests pins the arithmetic in isolation. This is the seam test: that the stage reads
    // the right profile, hands the target the right share, and queues the rest for ResolveBlastSpillStage
    // to deliver. Both halves have to be right - the arithmetic was correct and unreachable for an hour
    // before the branch existed, which is exactly the shape this project keeps finding.
    //
    // FixedFaceDiceRoller(6), not FixedDiceRoller: every die shows 6 AND the roll count is honoured, so an
    // A3 volley really reports 3 hits. FixedDiceRoller collapses any roll to one, which would hide the
    // per-hit arithmetic these tests exist to measure.
    [TestFixture]
    public class BlastSpreadIntegrationTests
    {
        private static readonly Position AttackerPos = new Position(0f, 5f);
        private static readonly Position TargetPos = new Position(10f, 5f);
        private static readonly Position BystanderPos = new Position(10f, 7f);    // 2in from the target
        private static readonly Position FarPos = new Position(10f, 20f);         // well clear

        private GameDataStore _store = null!;
        private PlayerID _us;
        private PlayerID _them;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _us = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        // ── The two full-scale games are untouched ──────────────────────────────────────────────────

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        public async Task InAFullScaleGame_EveryHitStillLandsOnTheTarget(EGameSystem system)
        {
            // The regression guard. GF/AoF's Blast has no spread clause at all, so a bystander standing
            // right beside the target must take nothing - and the target must take exactly what it took
            // before any of this existed: 1 hit x min(3, 5 models) = 3.
            TestGameContext ctx = Context(system);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 5);
            Unit(ctx, _them, BystanderPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target);

            Assert.That(TotalHits(hits), Is.EqualTo(3f));
            Assert.That(spill, Is.Null, "nothing spreads in a full-scale game");
        }

        // ── The three skirmish-scale games spread ───────────────────────────────────────────────────

        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        [TestCase(EGameSystem.GFStarQuest)]
        public async Task InASkirmishGame_WithNothingNearby_NothingChanges(EGameSystem system)
        {
            // A lone target must play identically in all five systems: the multiplier is capped by its own
            // models because there is no cluster, and every hit stays on it. Without this the spread would
            // be a silent buff or nerf to every blast fired at an isolated unit.
            TestGameContext ctx = Context(system);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 5);
            Unit(ctx, _them, FarPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target);

            Assert.That(TotalHits(hits), Is.EqualTo(3f));
            Assert.That(spill, Is.Null);
        }

        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        [TestCase(EGameSystem.GFStarQuest)]
        public async Task InASkirmishGame_HitsSplitWithTheBystander(EGameSystem system)
        {
            // The rule. 1 hit, Blast(3), a 2-model target and a 1-model bystander within 3": the cluster
            // is 3 models so the multiplier is not trimmed, 1 x 3 = 3 hits, split 2 to the target (it
            // keeps the remainder) and 1 to the bystander.
            TestGameContext ctx = Context(system);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 2);
            DataBinding<UnitData> bystander = Unit(ctx, _them, BystanderPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target);

            Assert.That(TotalHits(hits), Is.EqualTo(2f), "the target's share, remainder included");
            Assert.That(spill, Is.Not.Null, "and the rest is queued for the bystander");
            Assert.That(spill!.Count, Is.EqualTo(1));
            (DataBinding<UnitData> unit, float spilled) = spill.ConsumeNext();
            Assert.That(unit.GetValue().ID.ID, Is.EqualTo(bystander.GetValue().ID.ID));
            Assert.That(spilled, Is.EqualTo(1f));
        }

        [Test]
        public async Task TheClusterLiftsTheMultiplierCap()
        {
            // What makes the spread a buff rather than a nerf, and the half Kai chose. The same 2-model
            // target: alone, Blast(3) is trimmed to x2 and deals 2 hits. With two bystanders the cluster
            // is 4 models, so x3 stands and 3 hits become 9 - spread 3/3/3.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 2);
            Unit(ctx, _them, BystanderPos);
            Unit(ctx, _them, new Position(8f, 5f));

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target, attacks: 3);

            float delivered = TotalHits(hits);
            Assert.That(spill, Is.Not.Null);
            while (spill!.HasMore) delivered += spill.ConsumeNext().Hits;

            Assert.That(delivered, Is.EqualTo(9f), "3 hits x3, uncapped because the cluster is 4 models");
            Assert.That(TotalHits(hits), Is.EqualTo(3f), "split three ways");
        }

        [Test]
        public async Task ABlastOfOne_StillSharesItsHitsOut()
        {
            // The case that forced the stage to be able to REMOVE hits as well as add them. Blast(1)
            // multiplies nothing, but the spread clause has no "only if X is over 1" condition, so 4 hits
            // across two units is 2 each - the target ends up with FEWER hits than it was dealt.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 1);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 3);
            Unit(ctx, _them, BystanderPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target, attacks: 4);

            Assert.That(TotalHits(hits), Is.EqualTo(2f), "half the volley moved off the target");
            Assert.That(spill, Is.Not.Null);
            Assert.That(spill!.ConsumeNext().Hits, Is.EqualTo(2f));
        }

        [Test]
        public async Task TheAttackersOwnSideIsNeverCaught()
        {
            // "all ENEMY units within 3"". A friendly squad hugging the target is not in the blast, which
            // the obvious "every other unit nearby" implementation would get wrong.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 5);
            Unit(ctx, _us, BystanderPos);

            (_, BlastSpillResults? spill) = await RunStage(ctx, attacker, target);

            Assert.That(spill, Is.Null);
        }

        [Test]
        public async Task AWeaponWithNoBlast_NeverSpreads()
        {
            // The spread belongs to Blast, not to shooting. An ordinary volley into a crowd stays on its
            // target in every system.
            TestGameContext ctx = Context(EGameSystem.GFStarQuest);
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 3);
            Unit(ctx, _them, BystanderPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target, attacks: 2);

            Assert.That(TotalHits(hits), Is.EqualTo(2f));
            Assert.That(spill, Is.Null);
        }

        [Test]
        public async Task NoHitsMeansNoSpread()
        {
            // Every die misses, so there is nothing to multiply or share. Guards against a blast that
            // sprays bystanders on a volley that hit nothing at all.
            var ctx = new TestGameContext(_store, new FixedFaceDiceRoller(1), settings: Settings(EGameSystem.GFStarQuest));
            DataBinding<UnitData> attacker = Unit(ctx, _us, AttackerPos);
            AttachBlast(attacker, 3);
            DataBinding<UnitData> target = Unit(ctx, _them, TargetPos, models: 3);
            Unit(ctx, _them, BystanderPos);

            (RollToHitResults hits, BlastSpillResults? spill) = await RunStage(ctx, attacker, target, attacks: 3);

            Assert.That(TotalHits(hits), Is.EqualTo(0f));
            Assert.That(spill, Is.Null);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private static GameSettings Settings(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            if (system == EGameSystem.GFStarQuest) settings.GameMode = EGameMode.StarQuestMission;
            return settings;
        }

        private TestGameContext Context(EGameSystem system) =>
            new TestGameContext(_store, new FixedFaceDiceRoller(6), settings: Settings(system));

        private async Task<(RollToHitResults Hits, BlastSpillResults? Spill)> RunStage(
            TestGameContext ctx, DataBinding<UnitData> attacker, DataBinding<UnitData> defender, int attacks = 1)
        {
            var stage = new RollToHitStage<ICombatMetadata>(ctx, new NoOpLayer<ICombatMetadata>());
            stage.NextStage.Bind("done");

            var weapon = new Weapon("Test", rangeInches: 48f, attacks: attacks, armorPenetration: 0);
            var metadata = new CombatMetadata(ctx, attacker, defender, weapon, weaponCount: 1);
            metadata.AddResult(new DetermineHitRollResults(4, attackCount: attacks));

            await stage.Enter(metadata);

            Assert.That(metadata.QueryForResult(out RollToHitResults hits), Is.True);
            BlastSpillResults? spill = metadata.QueryForResult(out BlastSpillResults found) ? found : null;
            return (hits, spill);
        }

        private static float TotalHits(RollToHitResults results) =>
            results.SuccessfulHitList.Sum(group => group.HitCount);

        private static void AttachBlast(DataBinding<UnitData> unit, int rating)
        {
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Blast", CoreRuleCatalog.Blast,
                new RuleArgument[] { new RuleArgument.Int(rating) }));
        }

        private DataBinding<UnitData> Unit(TestGameContext ctx, PlayerID owner, Position at, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f,
                    new List<Weapon> { new Weapon("Test", rangeInches: 48f, attacks: 1, armorPenetration: 0) },
                    new Position(at.x + i * 0.1f, at.z), _store);
                model.SetMaxWounds(3);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, $"U{_store.GetAllValues<UnitData>().Count()}",
                quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
