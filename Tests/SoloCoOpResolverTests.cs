using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the official AI's decision tree actually driving a unit: the action it picks and where it
    /// walks.
    ///
    /// <para>
    /// <see cref="SoloAiBrainTests"/> pins the tree's reasoning; this pins the two resolvers that carry it
    /// out, which is where the hero AI's three worst bugs lived earlier today. In particular the
    /// per-activation latch: a move that cannot be carried out reopens the action menu, and an AI that
    /// re-picks from an unchanged plan spins until the engine's decision watchdog fires.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloCoOpResolverTests
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

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, float z,
            float rangeInches = 24f, int meleeAttacks = 2)
        {
            var weapons = new List<Weapon> { new Weapon("CCW", 0f, meleeAttacks, 0) };
            if (rangeInches > 0f) weapons.Insert(0, new Weapon("Rifle", rangeInches, 2, 0));
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

        private void Objective(float x, float z, PlayerID? owner = null, int number = 1)
        {
            var objective = new ObjectiveData(new Position(x, z), _store, hordeNumber: number);
            _store.Create(objective);
            if (owner != null) objective.SetOwner(owner.Value);
        }

        private SoloCoOpActionResolver NewActionResolver(SoloMoveDeclineLatch? latch = null) =>
            new(Table, _ai, latch, new AiStringSelectionResolver(Table, _ai, latch));

        private ChooseActionRequest Menu(DataBinding<UnitData> unit, params string[] options) =>
            new(_ai, unit.GetValue().ID, options,
                Array.Empty<StringSelectionRequest.InvalidOption>());

        private static readonly string[] FullMenu =
        {
            ChooseActionStage.MOVEMENT_CHOICE_NAME,
            ChooseActionStage.CHARGE_CHOICE_NAME,
            ChooseActionStage.SHOOT_CHOICE_NAME,
            ChooseActionStage.PASS_CHOICE_NAME,
        };

        // ── #423 Ambush: "must always be kept in reserve" ────────────────────────────────────────────
        //
        // The clause: "AI units with Ambush must always be kept in reserve, and must always deploy at the
        // start of the second round." The arrival half needed no code - Ambush defers with
        // MinArrivalRound 2 and AiYesNoResolver answers the arrival prompt with its declared default of
        // yes - so these cases are about the hold.
        //
        // The engine deliberately answered this prompt with "Deploy Normally" before, because the
        // BASELINE bot's reserve placement drops a unit at the first legal row from a table edge. The
        // official bot has placed through SoloCoOpPlaceObjectsResolver since #419, which aims at the
        // closest objective, so following the book no longer strands the unit.

        [Test]
        public async Task AmbushUnit_IsHeldInReserve()
        {
            var resolver = new SoloCoOpStringSelectionResolver(
                new AiStringSelectionResolver(Table, _ai, null));

            string pick = await resolver.Resolve(HoldOrDeploy("Ambush"));

            Assert.That(pick, Is.EqualTo(ChooseUnitToDeployStage.HoldChoiceFor("Ambush")),
                "\"must always be kept in reserve\" - the official bot holds every Ambush unit.");
        }

        // The same prompt serves every rule that defers a deployment, and the book's clause names Ambush
        // only. Anything else keeps the old answer rather than being guessed at.
        [Test]
        public async Task ADifferentDeferringRule_StillDeploysNormally()
        {
            var resolver = new SoloCoOpStringSelectionResolver(
                new AiStringSelectionResolver(Table, _ai, null));

            string pick = await resolver.Resolve(HoldOrDeploy("Hidden Route"));

            Assert.That(pick, Is.EqualTo(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE),
                "the clause is about Ambush; another rule's hold option is not silently adopted.");
        }

        // And the baseline is untouched - plan D1 freezes it, and its own comment explains why it
        // declines.
        [Test]
        public async Task TheBaselineResolver_StillDeploysAnAmbushUnitNormally()
        {
            var baseline = new AiStringSelectionResolver(Table, _ai, null);

            string pick = await baseline.Resolve(HoldOrDeploy("Ambush"));

            Assert.That(pick, Is.EqualTo(ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE));
        }

        private StringSelectionRequest HoldOrDeploy(string ruleName) =>
            new(_ai, $"Deploy Raiders now, or hold it in {ruleName}?",
                new List<string>
                {
                    ChooseUnitToDeployStage.HoldChoiceFor(ruleName),
                    ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE,
                    ChooseUnitToDeployStage.BACK_TO_LIST_CHOICE,
                },
                new List<StringSelectionRequest.InvalidOption>());

        // ── #423 Indirect / Relentless: "always use Hold actions and shoot" ──────────────────────────
        //
        // "AI units with Indirect weapons that are in range of enemies always use Hold actions and
        // shoot", and the same sentence for Relentless. It overrides the decision tree, so the board
        // below is one where the tree does something else: a brawler with a big close-combat weapon and
        // an enemy six inches away, which the tree charges because melee is its better attack.
        //
        // It matters most for Indirect itself: since 3.6.0 that rule only ignores line of sight ON A HOLD
        // ACTION, so an AI that charged (or walked) and fired threw the rule away entirely.

        [Test]
        public async Task IndirectWeaponInRange_HoldsAndShoots_InsteadOfCharging()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Mortar Brutes", 24f, 24f, meleeAttacks: 8);
            GiveWeaponRule(ai, "Rifle", "Indirect");
            Unit(_player, "Troops", 30f, 24f);   // 6" - inside charge reach AND the 24" weapon

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.SHOOT_CHOICE_NAME),
                "the Indirect clause outranks the tree: stand still and fire rather than charge.");
        }

        [Test]
        public async Task RelentlessUnitInRange_HoldsAndShoots_InsteadOfCharging()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Veterans", 24f, 24f, meleeAttacks: 8);
            GiveUnitRule(ai, "Relentless");
            Unit(_player, "Troops", 30f, 24f);

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.SHOOT_CHOICE_NAME),
                "Relentless is worded for the unit rather than the weapon, and its clause is the same.");
        }

        // The control that gives the two above their meaning: the identical board and the identical unit
        // WITHOUT either rule charges, because melee is its better attack. So the Shoot picks above are
        // the clause doing the work and not the fixture.
        [Test]
        public async Task TheSameBrawlerWithoutTheRule_Charges()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Brutes", 24f, 24f, meleeAttacks: 8);
            Unit(_player, "Troops", 30f, 24f);

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME),
                "no Indirect and no Relentless - the tree charges with eight attacks.");
        }

        // And the reason the clause is written "that are in range": an Indirect weapon with nothing in
        // reach has no shot to hold for, so the tree gets the unit back.
        [Test]
        public async Task IndirectWeaponOutOfRange_GoesBackToTheDecisionTree()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Mortar Team", 10f, 24f);
            GiveWeaponRule(ai, "Rifle", "Indirect");
            Objective(46f, 24f);
            Unit(_player, "Troops", 90f, 90f);   // far outside the 24" weapon

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "nothing in range, so the clause does not apply and the objective run stands.");
        }

        private static void GiveUnitRule(DataBinding<UnitData> unit, string ruleName) =>
            unit.GetValue().AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule(ruleName,
                new FDG.Rules.Definitions.SpecialRuleDefinition(ruleName,
                    Array.Empty<FDG.Rules.Definitions.HookEntry>(),
                    Array.Empty<FDG.Rules.Definitions.ActivatedAbility>())));

        private static void GiveWeaponRule(DataBinding<UnitData> unit, string weaponName, string ruleName)
        {
            foreach (IModel model in unit.GetValue().Models)
            foreach (IWeapon weapon in model.Weapons)
            {
                if (weapon.Name != weaponName) continue;
                ((Weapon)weapon).AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule(ruleName,
                    new FDG.Rules.Definitions.SpecialRuleDefinition(ruleName,
                        Array.Empty<FDG.Rules.Definitions.HookEntry>(),
                        Array.Empty<FDG.Rules.Definitions.ActivatedAbility>())));
            }
        }

        // ── The action it picks ───────────────────────────────────────────────────────────────────────

        [Test]
        public async Task HoldingAnObjectiveWithNothingInRange_Passes()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 46f, 46f);

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME),
                "step 2's Hold position - and holding means not walking off the objective.");
        }

        [Test]
        public async Task HoldingAnObjectiveWithATargetAlreadyInRange_ShootsWithoutMoving()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 36f, 24f);   // 12", inside the 24" rifle

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.SHOOT_CHOICE_NAME),
                "already in range, so moving would leave cover for nothing.");
        }

        [Test]
        public async Task AnUnheldObjectiveAcrossTheTable_Moves()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(40f, 40f);

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
        }

        [Test]
        public async Task AnEnemyInChargeReachWithNoGun_Charges()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Brutes", 24f, 24f, rangeInches: 0f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 26f, 24f);

            string pick = await NewActionResolver().Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME));
        }

        [Test]
        public async Task WithOnlyPassOffered_Passes()
        {
            // The fallback must stay inside ValidOptions: ChooseActionStage faults on an option it did not
            // offer, which is a crash rather than a bad move.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(40f, 40f);

            string pick = await NewActionResolver()
                .Resolve(Menu(ai, ChooseActionStage.PASS_CHOICE_NAME));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.PASS_CHOICE_NAME));
        }

        [Test]
        public async Task MoveIsNeverPickedTwiceInOneActivation()
        {
            // THE loop guard. A unit gets one move per activation; when that move cannot be carried out the
            // engine reopens this menu with Move still on it, and the plan has not changed. Re-picking it
            // spins until the ~1.5M-decision watchdog - which is exactly what happened to the hero AI
            // earlier today, turning a one-second Horde game into one that would not finish in ten minutes.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(40f, 40f);
            SoloCoOpActionResolver resolver = NewActionResolver();

            string first = await resolver.Resolve(Menu(ai, FullMenu));
            string second = await resolver.Resolve(Menu(ai, FullMenu));

            Assert.That(first, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(second, Is.Not.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME),
                "the second visit must not re-pick a move the unit has already been given.");
        }

        [Test]
        public async Task ADifferentUnitGetsItsOwnMovePick()
        {
            // The latch is per activation, not per resolver - one unit using its move must not stop the
            // next unit from moving at all.
            DataBinding<UnitData> first = Unit(_ai, "First", 6f, 6f);
            DataBinding<UnitData> second = Unit(_ai, "Second", 8f, 6f);
            Objective(40f, 40f);
            SoloCoOpActionResolver resolver = NewActionResolver();

            await resolver.Resolve(Menu(first, FullMenu));
            string pick = await resolver.Resolve(Menu(second, FullMenu));

            Assert.That(pick, Is.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
        }

        [Test]
        public async Task AfterOurOwnMoveWasDeclined_TheMovementFamilyIsSkipped()
        {
            // #358's latch, which the engine sets when the path resolver had no legal answer.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(40f, 40f);
            var latch = new SoloMoveDeclineLatch();
            latch.Arm();

            string pick = await NewActionResolver(latch).Resolve(Menu(ai, FullMenu));

            Assert.That(pick, Is.Not.EqualTo(ChooseActionStage.MOVEMENT_CHOICE_NAME));
            Assert.That(pick, Is.Not.EqualTo(ChooseActionStage.CHARGE_CHOICE_NAME));
        }

        // ── Where it walks ───────────────────────────────────────────────────────────────────────────

        private DefineMovementPathRequest MoveRequest(DataBinding<UnitData> unit) =>
            new(_ai, "Move", unit, maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                mainActivationMove: true);

        private static List<ModelMoveEntry> Unwrap(CancellableResult<List<ModelMoveEntry>> result) =>
            result is Selected<List<ModelMoveEntry>> selected
                ? selected.Value
                : throw new AssertionException("the resolver declined the move instead of returning one.");

        private static void Apply(List<ModelMoveEntry> move)
        {
            foreach (ModelMoveEntry entry in move)
            {
                if (entry.Positions.Count == 0) continue;
                entry.Model.GetValue().SetPosition(entry.Positions[^1]);
            }
        }

        [Test]
        public async Task RushingAnObjective_ClosesOnIt()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(30f, 30f);
            var resolver = new SoloCoOpMovementResolver(Table, _ai, null);

            float before = AiCombatMath.DistanceToPoint(ai.GetValue(), new Position(30f, 30f));
            Apply(Unwrap(await resolver.Resolve(MoveRequest(ai))));
            float after = AiCombatMath.DistanceToPoint(ai.GetValue(), new Position(30f, 30f));

            Assert.That(after, Is.LessThan(before - 5f),
                "a Rush toward an objective should use most of its 12\".");
        }

        [Test]
        public async Task RepeatedActivations_ReachTheObjective()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(30f, 30f);
            var resolver = new SoloCoOpMovementResolver(Table, _ai, null);

            for (int activation = 0; activation < 8; activation++)
            {
                Apply(Unwrap(await resolver.Resolve(MoveRequest(ai))));
                if (AiCombatMath.DistanceToPoint(ai.GetValue(), new Position(30f, 30f))
                    <= SoloAiBrain.ObjectiveReachInches) break;
            }

            Assert.That(AiCombatMath.DistanceToPoint(ai.GetValue(), new Position(30f, 30f)),
                Is.LessThanOrEqualTo(SoloAiBrain.ObjectiveReachInches),
                "it has to actually arrive, or no objective is ever contested.");
        }

        [Test]
        public async Task HoldingPosition_StandsStill()
        {
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 24f, 24f);
            Objective(24f, 24f);
            Unit(_player, "Troops", 46f, 46f);
            var resolver = new SoloCoOpMovementResolver(Table, _ai, null);
            Position before = ai.GetValue().Models.First().Position;

            Apply(Unwrap(await resolver.Resolve(MoveRequest(ai))));
            Position after = ai.GetValue().Models.First().Position;

            Assert.That(Position.GetDistance2D(before, after), Is.LessThan(0.5f),
                "Hold position means hold position - and it must still be a valid move, not a decline.");
        }

        [Test]
        public async Task AdvancingOnATarget_StaysWithinTheAdvanceBudget()
        {
            // The Advance cap is what keeps the promised shot legal: a Rush would move further and the
            // engine would read the action differently.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 24f);
            Unit(_ai, "Guards", 30f, 30f);       // holds the only objective, so step 3 does not fire
            Objective(30f, 30f);
            // 28" away: outside the 24" rifle, but inside rifle-plus-Advance, which is precisely the
            // "Advance to get in range and shoot" case. Further than that and the tree Rushes instead,
            // which is what this test caught the first time it was written.
            Unit(_player, "Troops", 34f, 24f);
            var resolver = new SoloCoOpMovementResolver(Table, _ai, null);

            Position before = ai.GetValue().Models.First().Position;
            Apply(Unwrap(await resolver.Resolve(MoveRequest(ai))));
            Position after = ai.GetValue().Models.First().Position;

            Assert.That(Position.GetDistance2D(before, after), Is.LessThanOrEqualTo(6.01f),
                "an Advance is 6\" here; going further would turn the move into a Rush.");
        }

        [Test]
        public async Task AForcedMoveIsLeftToTheBaselineMover()
        {
            // Consolidations, forced moves and anything that is not the activation move: the tree has no
            // opinion and must not interfere.
            DataBinding<UnitData> ai = Unit(_ai, "Raiders", 6f, 6f);
            Objective(30f, 30f);
            var resolver = new SoloCoOpMovementResolver(Table, _ai, null);
            var forced = new DefineMovementPathRequest(_ai, "Push", ai,
                maxAdvanceDistance: 3f, maxRushDistance: 3f, maxDistanceInches: 3f,
                mainActivationMove: false);

            CancellableResult<List<ModelMoveEntry>> result = await resolver.Resolve(forced);

            Assert.That(result, Is.Not.Null, "it still answers - it just answers the baseline's way.");
        }
    }
}
