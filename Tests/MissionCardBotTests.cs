using FDG.Ai.Solo;
using FDG.Data;
using FDG.Missions;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #462 - bots play towards their mission cards (Kai, 2026-11-15). The official AI tree (Solo &amp; Co-Op
    /// v3.6.0) is unchanged; the cards only settle what it leaves open - which objective, which target,
    /// shoot or charge, and where a spare unit goes. House rule C, "Bots and their cards".
    /// </summary>
    [TestFixture]
    public class MissionCardBotTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
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
            _store.Create(new PlayerSlotInfo(_ai, slotID: 0, teamNumber: 0, name: "Bot", isFilled: true));
            _store.Create(new PlayerSlotInfo(_player, slotID: 1, teamNumber: 1, name: "Kai", isFilled: true));
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        private ITableState Table => _context.TableState;

        private void Cards(params int[] cards)
        {
            var state = new MissionCardState { Game = EMissionCardGame.BattleOfWits };
            state.Sides[0] = new MissionCardSide { Slot = 0, Table = cards.ToList() };
            state.Sides[1] = new MissionCardSide { Slot = 1 };
            GameProgressUtilities.SetMissionCards(_store, state);
        }

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, float z, float range = 24f,
            int meleeAttacks = 2, Rules.Definitions.SpecialRuleDefinition? rule = null, string? ruleName = null)
        {
            var weapons = new List<Weapon>();
            if (range > 0f) weapons.Add(new Weapon("Rifle", range, 2, 0));
            weapons.Add(new Weapon("CCW", 0f, meleeAttacks, 0));
            var model = new ModelData(0.5f, weapons, new Position(x, z), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            if (rule != null) unit.AttachRuleDefinition(new ResolvedRule(ruleName!, rule));
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private void Objective(float x, float z, int number)
        {
            var objective = new ObjectiveData(new Position(x, z), _store, hordeNumber: number);
            _store.Create(objective);
        }

        // ── Which objective ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void NoCards_TheTreeGoesForTheClosestObjective()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 16f, 1);   // 8" away
            Objective(24f, 38f, 2);   // 14" away
            Unit(_player, "Troops", 70f, 46f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.RushToObjective));
            Assert.That(plan.Goal!.Value.z, Is.EqualTo(16f).Within(0.01f));
        }

        [Test]
        public void ASeizeCard_PullsTheUnitToItsObjective()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 16f, 1);
            Objective(24f, 38f, 2);
            Unit(_player, "Troops", 70f, 46f);
            Cards(12);   // Seize 2

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Goal!.Value.z, Is.EqualTo(38f).Within(0.01f), "6\" further, but it scores");
        }

        [Test]
        public void ACardObjective_FarAcrossTheTable_DoesNotDragTheUnitThere()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 16f, 1);   // 8"
            Objective(60f, 24f, 2);   // 36": 28" further, more than 1 VP's 9" pull
            Unit(_player, "Troops", 70f, 46f);
            Cards(12);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Goal!.Value.z, Is.EqualTo(16f).Within(0.01f));
        }

        // ── Which target ─────────────────────────────────────────────────────────────────────────────

        [Test]
        public void BringThemDown_ShootsTheFlyerOverTheCloserUnit()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f, 1);
            Unit(_player, "Troops", 32f, 24f);   // 8"
            DataBinding<UnitData> flyer = Unit(_player, "Jets", 24f, 40f, rule: CoreRuleCatalog.Flying, ruleName: "Flying");   // 16"

            Assert.That(SoloAiBrain.Plan(Table, _ai, ai.GetValue()).Target?.Name, Is.EqualTo("Troops"), "no card: the closest");

            Cards(63);
            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Target?.Name, Is.EqualTo(flyer.GetValue().Name));
        }

        [Test]
        public void Intent_ReadsTheCards()
        {
            Unit(_ai, "Raiders", 24f, 24f);
            for (int i = 1; i <= 6; i++) Objective(10f * i, 24f, i);
            Cards(13, 34, 51);

            MissionCardIntent? intent = MissionCardIntent.For(Table, _ai);

            Assert.That(intent, Is.Not.Null);
            Assert.That(intent!.ObjectiveVp[3], Is.EqualTo(1));
            Assert.That(intent.ObjectiveVp[4], Is.EqualTo(2));
            Assert.That(intent.WantsShootingKills, Is.True);
            Assert.That(intent.WantsMelee, Is.False);
            Assert.That(MissionCardIntent.For(Table, _player), Is.Null, "the other side holds no cards");
        }

        [Test]
        public void NoMissionCards_NoIntent()
        {
            Unit(_ai, "Raiders", 24f, 24f);
            Assert.That(MissionCardIntent.For(Table, _ai), Is.Null);
        }

        // ── Shoot or charge ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void InTheTrenches_TipsACloseCallTowardsTheCharge()
        {
            // Rifle 2 attacks at range vs CCW 2 attacks: about equal damage - shooting wins a tie.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f, 1);
            Unit(_player, "Troops", 26f, 24f);   // 2" - in charge reach, inside 3" of the objective

            SoloAiPlan before = SoloAiBrain.Plan(Table, _ai, ai.GetValue());
            Cards(52);
            SoloAiPlan after = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(before.Kind, Is.EqualTo(ESoloAiMove.AdvanceAndShoot));
            Assert.That(after.Kind, Is.EqualTo(ESoloAiMove.Charge));
        }

        // ── Infiltrate ───────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Infiltrate_ASpareUnitHeadsIntoTheEnemyZone()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 6f);
            Objective(24f, 6f, 1);
            var zones = new Dictionary<int, ZoneRect>
            {
                [0] = new ZoneRect(0f, 72f, 0f, 12f),
                [1] = new ZoneRect(0f, 72f, 36f, 48f),
            };
            GameProgressUtilities.SetDeploymentZones(_store, zones);
            Unit(_player, "Troops", 70f, 46f);
            Cards(42);

            MissionCardIntent intent = MissionCardIntent.For(Table, _ai)!;
            Position? goal = intent.InfiltrateGoal(Table, _ai, ai.GetValue());

            Assert.That(goal, Is.Not.Null);
            Assert.That(goal!.Value.z, Is.EqualTo(39f).Within(0.01f), "3\" inside the enemy's zone edge");
            Assert.That(goal.Value.x, Is.EqualTo(24f).Within(0.01f), "straight ahead");
        }
    }
}
