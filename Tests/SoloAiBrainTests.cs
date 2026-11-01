using FDG.Ai;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the AI decision tree from <b>Solo &amp; Co-Op Rules v3.6.0</b>, step by step.
    ///
    /// <para>
    /// This is the spec the Horde books point at ("controlled by the AI (using the Solo &amp; Co-Op
    /// rules)") and it is what the by-the-book bot should have been following all along. The old solo bot
    /// ranked Charge &gt; Move &gt; Shoot &gt; Pass with no notion of objectives, so it played every mode as
    /// a brawl - and objectives decide the winner in all of them.
    /// </para>
    /// <para>
    /// One test per branch of the tree, named for what the book says, so a future reader can check the code
    /// against the PDF without re-deriving which step is which.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiBrainTests
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
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        private ITableState Table => _context.TableState;

        /// <summary>A unit with a rifle (24") and a melee weapon, so both branches are available.</summary>
        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, float z,
            float rangeInches = 24f, int attacks = 2, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var weapons = new List<Weapon>();
                if (rangeInches > 0f) weapons.Add(new Weapon("Rifle", rangeInches, attacks, 0));
                weapons.Add(new Weapon("CCW", 0f, attacks, 0));
                var model = new ModelData(0.5f, weapons, new Position(x + i * 1.2f, z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        /// <summary>A unit whose melee far outweighs its shooting, for the charge-or-shoot comparison.</summary>
        private DataBinding<UnitData> Brute(PlayerID owner, string name, float x, float z)
        {
            var weapons = new List<Weapon>
            {
                new Weapon("Pistol", 12f, 1, 0),
                new Weapon("Great Axe", 0f, 6, 2),
            };
            var model = new ModelData(0.5f, weapons, new Position(x, z), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        /// <summary>An objective marker, optionally already held by someone.</summary>
        private ObjectiveData Objective(float x, float z, PlayerID? owner = null, int number = 1)
        {
            var objective = new ObjectiveData(new Position(x, z), _store, hordeNumber: number);
            _store.Create(objective);
            if (owner != null) objective.SetOwner(owner.Value);
            return objective;
        }

        // ── Step 1 / 2: on a contested objective ──────────────────────────────────────────────────────

        [Test]
        public void Step2_OnAContestedObjectiveWithNothingInRange_HoldsPosition()
        {
            // "No - Hold position". The old bot had no Hold at all for this case: with an enemy anywhere
            // on the table it walked off the objective to go and find it.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 46f, 46f);   // far away, out of range of a 24" rifle

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.Hold));
            Assert.That(plan.Why, Does.Contain("holding"));
        }

        [Test]
        public void Step2_OnAContestedObjectiveWithATargetInRange_ShootsWithoutLeaving()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f);
            DataBinding<UnitData> prey = Unit(_player, "Troops", 36f, 24f);   // 12" away, inside 24"

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.AdvanceAndShoot));
            Assert.That(plan.Target?.Name, Is.EqualTo(prey.GetValue().Name));
            Assert.That(plan.Goal, Is.Not.Null, "the objective it is holding, so the move can stay on it.");
        }

        [Test]
        public void Step1_AnObjectiveTheAiAlreadyHolds_IsNotWorthStandingOn()
        {
            // Step 1 says "an objective that is NOT controlled by the AI". A unit sitting on its own
            // objective with nothing to shoot should move on to take another, not sit there.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f, owner: _ai, number: 1);
            Objective(40f, 24f, number: 2);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.RushToObjective));
            Assert.That(plan.Goal!.Value.x, Is.EqualTo(40f).Within(0.01f),
                "it goes for the one it does not hold.");
        }

        [Test]
        public void Step2_PrefersACharge_WhenMeleeWouldHurtMore()
        {
            // "Advance and shoot closest, or charge closest" - the engine reads the choice the way step 4
            // spells it out, by comparing the damage.
            DataBinding<UnitData> ai = Brute(_ai, "Brutes", 24f, 24f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 26f, 24f);   // 2" away: chargeable

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.Charge));
        }

        // ── Step 3 / 4: going to take an unheld objective ─────────────────────────────────────────────

        [Test]
        public void Step4_WithNothingInRange_RushesForTheUnheldObjective()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(30f, 30f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.RushToObjective));
            Assert.That(plan.Goal!.Value.x, Is.EqualTo(30f).Within(0.01f));
        }

        [Test]
        public void Step4_WithATargetOnTheWay_AdvancesOnTheObjectiveAndShoots()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(30f, 30f);
            DataBinding<UnitData> prey = Unit(_player, "Troops", 20f, 6f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.AdvanceToObjective));
            Assert.That(plan.Target?.Name, Is.EqualTo(prey.GetValue().Name));
            Assert.That(plan.Goal!.Value.x, Is.EqualTo(30f).Within(0.01f),
                "it is still heading for the objective - the shot is on the way.");
        }

        [Test]
        public void Step3_AnObjectiveWithAFriendlyUnitAlreadyOnIt_IsNotChased()
        {
            // "any objective ... that has NO non-Shaken AI units within 3" of it". One unit per objective
            // is the point of the step; without it the whole army piles onto the nearest marker.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Unit(_ai, "Guards", 30f, 30f);          // already sitting on the near objective
            Objective(30f, 30f, number: 1);
            Objective(44f, 6f, number: 2);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.RushToObjective));
            Assert.That(plan.Goal!.Value.x, Is.EqualTo(44f).Within(0.01f),
                "the attended one is someone else's job.");
        }

        [Test]
        public void Step3_AShakenFriendOnAnObjective_DoesNotCountAsHoldingIt()
        {
            // The book says "non-Shaken" for exactly this reason: a Shaken unit is about to spend its
            // activation recovering, so it is not really holding anything.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            DataBinding<UnitData> shaken = Unit(_ai, "Guards", 30f, 30f);
            shaken.GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
            Objective(30f, 30f, number: 1);
            Objective(44f, 6f, number: 2);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Goal!.Value.x, Is.EqualTo(30f).Within(0.01f),
                "a Shaken friend leaves the objective unattended, so this unit goes there.");
        }

        // ── Step 5: every objective attended ──────────────────────────────────────────────────────────

        [Test]
        public void Step5_EveryObjectiveAttended_FightsWhatIsInRange()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 10f, 10f);
            Unit(_ai, "Guards", 30f, 30f);
            Objective(30f, 30f);
            DataBinding<UnitData> prey = Unit(_player, "Troops", 20f, 10f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.AdvanceAndShoot));
            Assert.That(plan.Target?.Name, Is.EqualTo(prey.GetValue().Name));
            Assert.That(plan.Goal, Is.Null, "step 5's shooting branch is not heading anywhere.");
        }

        [Test]
        public void Step5_NothingAttendedAndNothingInRange_RushesTheClosestObjective()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 10f, 10f);
            Unit(_ai, "Guards", 30f, 30f);
            Objective(30f, 30f, number: 1);
            Unit(_player, "Troops", 46f, 46f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.RushToObjective));
            Assert.That(plan.Why, Does.Contain("closest"));
        }

        [Test]
        public void ADeadUnitHoldsStill_RatherThanThrowing()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 10f, 10f);
            foreach (IModel model in ai.GetValue().Models)
                model.DealWounds(model.TotalWounds - model.WoundsDealt);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.Hold));
        }

        [Test]
        public void NoObjectivesAtAll_StillPlaysTheFightingBranches()
        {
            // Some modes put nothing on the table (a scenario with no markers). The tree must still fight
            // rather than fall over looking for an objective.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 10f, 10f);
            DataBinding<UnitData> prey = Unit(_player, "Troops", 20f, 10f);

            SoloAiPlan plan = SoloAiBrain.Plan(Table, _ai, ai.GetValue());

            Assert.That(plan.Kind, Is.EqualTo(ESoloAiMove.AdvanceAndShoot));
            Assert.That(plan.Target?.Name, Is.EqualTo(prey.GetValue().Name));
        }
    }
}
