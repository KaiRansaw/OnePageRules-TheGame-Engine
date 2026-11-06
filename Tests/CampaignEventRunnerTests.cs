using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #432 - the campaign random event table happening on the table.
    ///
    /// <para>
    /// The books roll one die per player at the start of each round and fire an event on a 5+, from a
    /// 36-entry table read as two digits. The table has been data since the campaign engine landed and
    /// nothing rolled it; these tests cover the half that makes it happen.
    /// </para>
    ///
    /// <para>
    /// The shape worth knowing: <b>an event with no legal target does nothing, and that is correct.</b>
    /// "One random non-Shaken unit becomes Shaken" in a game where every unit is already Shaken has no
    /// target; so does "one random captured objective marker becomes neutral" with nothing held. Each of
    /// those returns a line saying so rather than throwing or silently picking an illegal target, and
    /// there is a test for each.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignEventRunnerTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me;
        private PlayerID _them;

        /// <summary>A die that walks the given faces, then repeats the last one.</summary>
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _context = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        /// <summary>A unit, with a campaign sheet unless told otherwise.</summary>
        private DataBinding<UnitData> Unit(PlayerID owner, Position at, bool campaign = true, int models = 3)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(),
                    new Position(at.x + i * 1.1f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}",
                quality: 4, defense: 4, modelBindings: bindings);
            if (campaign) unit.StrikeUnitJson = new StrikeUnit { TeamFile = "army.fdgarmy" }.ToJson();
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private ObjectiveData Objective(Position at, PlayerID? owner)
        {
            var objective = new ObjectiveData(at, _store);
            if (owner.HasValue) objective.SetOwner(owner.Value);
            _store.Create(objective);
            return objective;
        }

        private static StrikeUnit Sheet(DataBinding<UnitData> unit) =>
            StrikeUnit.FromJson(unit.GetValue().StrikeUnitJson)!;

        /// <summary>
        /// The names of the rules a unit has been granted for the round, each checked against the live
        /// catalog resolver. A grant naming a rule nobody registered would be dropped silently at the
        /// hook, so the name being present is not enough on its own - it has to resolve.
        /// </summary>
        private static List<string> GrantedRules(DataBinding<UnitData> unit)
        {
            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            var names = new List<string>();
            foreach (Token token in unit.GetValue().Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.Payload is not TokenPayload.RuleGrant grant) continue;
                Assert.That(resolver.TryResolve(grant.RuleName, out _), Is.True,
                    $"the granted rule \"{grant.RuleName}\" must resolve, or the event does nothing.");
                names.Add(grant.RuleName);
            }
            return names;
        }

        private Task<string> Apply(int code, Func<int> d6) =>
            CampaignEventRunner.Apply(_context, CampaignEvents.ByCode(code), d6);

        // -- Only in a campaign game -----------------------------------------------------------------

        [Test]
        public void IsCampaignGame_IsTrueOnlyWhenAUnitCarriesACampaignSheet()
        {
            Unit(_me, new Position(10f, 10f), campaign: false);
            Assert.That(CampaignEventRunner.IsCampaignGame(_context.TableState), Is.False,
                "an ordinary game must not roll campaign events.");

            Unit(_them, new Position(40f, 10f));
            Assert.That(CampaignEventRunner.IsCampaignGame(_context.TableState), Is.True);
        }

        [Test]
        public async Task RollForRound_InAnOrdinaryGame_DoesNothingAtAll()
        {
            Unit(_me, new Position(10f, 10f), campaign: false);

            List<string> log = await CampaignEventRunner.RollForRound(_context);

            Assert.That(log, Is.Empty);
        }

        /// <summary>
        /// The trigger itself: a 5 or 6 fires, anything less does not. Pinned because the whole table
        /// hangs off this one roll and an off-by-one would make events either constant or impossible.
        /// </summary>
        [TestCase(1, false)]
        [TestCase(4, false)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        public void ShouldFire_OnFiveOrBetter(int roll, bool fires)
        {
            Assert.That(CampaignEventTargeting.ShouldFire(Dice(roll)), Is.EqualTo(fires));
        }

        // -- Wounds ----------------------------------------------------------------------------------

        [Test]
        public async Task FallingDebris_WoundsARandomUnit()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f), models: 1);
            float before = unit.GetValue().Models.Sum(m => m.WoundsDealt);

            // The unit pick, then the D3: a 5 reads as 3 wounds.
            string line = await Apply(11, Dice(1, 5));

            Assert.That(unit.GetValue().Models.Sum(m => m.WoundsDealt), Is.GreaterThan(before),
                "the event must actually land its wounds.");
            Assert.That(line, Does.Contain("wound"));
        }

        [Test]
        public async Task Sinkhole_OnlyPicksAUnitNearAnObjective()
        {
            Objective(new Position(20f, 20f), null);
            DataBinding<UnitData> near = Unit(_me, new Position(20f, 21f), models: 1);
            DataBinding<UnitData> far = Unit(_me, new Position(60f, 40f), models: 1);

            await Apply(31, Dice(1, 5));

            Assert.That(near.GetValue().Models.Sum(m => m.WoundsDealt), Is.GreaterThan(0f),
                "the unit within 3\" of the marker is the only legal target.");
            Assert.That(far.GetValue().Models.Sum(m => m.WoundsDealt), Is.Zero);
        }

        [Test]
        public async Task Sinkhole_WithNobodyNearAnObjective_DoesNothing()
        {
            Objective(new Position(20f, 20f), null);
            Unit(_me, new Position(60f, 40f), models: 1);

            string line = await Apply(31, Dice(1, 5));

            Assert.That(line, Does.Contain("nothing happened"));
        }

        // -- Shaken ----------------------------------------------------------------------------------

        [Test]
        public async Task SwarmAttack_ShakesANonShakenUnit()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(21, Dice(1));

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public async Task SwarmAttack_WithEverybodyAlreadyShaken_DoesNothing()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            string line = await Apply(21, Dice(1));

            Assert.That(line, Does.Contain("already Shaken"));
        }

        [Test]
        public async Task RefreshmentKit_UnshakesAShakenUnit_AndNeedsOne()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));
            Assert.That((await Apply(56, Dice(1))), Does.Contain("No unit was Shaken"),
                "with nobody Shaken there is nothing to un-shake.");

            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            await Apply(56, Dice(1));

            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        // -- Roll modifiers --------------------------------------------------------------------------

        /// <summary>
        /// "-1 to Defense rolls until the end of the round", as a granted roll-modifier token with a
        /// round-end clear. Asserted through <see cref="GrantedRollModifiers.PeekNet"/>, which is what
        /// the save stage itself reads - so a pass means the penalty really will apply to a roll.
        /// </summary>
        [Test]
        public async Task DebilitatingStatic_GivesMinusOneToDefenceForTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(22, Dice(1));

            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Save), Is.EqualTo(-1));
        }

        [Test]
        public async Task ShieldProjectorDrone_GivesPlusOneToDefenceForTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(55, Dice(1));

            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Save), Is.EqualTo(+1));
        }

        [Test]
        public async Task AlienShriek_GivesMinusOneToMoraleForTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(25, Dice(1));

            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Morale), Is.EqualTo(-1));
        }

        /// <summary>
        /// A round modifier must be gone next round. Granted with a round-end clear trigger, which
        /// <c>TokenClearService</c> sweeps - the same sweep every other "until the end of the round"
        /// effect in the game rides.
        /// </summary>
        [Test]
        public async Task ARoundModifier_IsSweptAtTheEndOfTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));
            await Apply(22, Dice(1));
            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Save), Is.EqualTo(-1));

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd,
                new List<ITokenContainer> { unit.GetValue().Tokens });

            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Save), Is.Zero,
                "\"until the end of the round\" has to actually end.");
        }

        // -- Movement --------------------------------------------------------------------------------

        /// <summary>
        /// "+1\" on Advance and +2\" on Rush/Charge", as a grant of #432's own rule. The rule has to be
        /// resolvable by name, which is why it is registered through the core catalog - a grant naming a
        /// rule nobody registered would be dropped silently.
        /// </summary>
        [Test]
        public async Task HasteStims_GrantsTheHastenedRuleForTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            string line = await Apply(51, Dice(1));

            Assert.That(GrantedRules(unit), Does.Contain(CampaignEventRuleCatalog.HastenedName));
            Assert.That(line, Does.Contain("+1"));
        }

        [Test]
        public async Task StickyFoliage_GrantsTheSlowedRuleForTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(26, Dice(1));

            Assert.That(GrantedRules(unit), Does.Contain(CampaignEventRuleCatalog.SlowedName));
        }

        /// <summary>
        /// The four hit-roll events are the ones worth pinning hardest. A hit-roll modifier TOKEN carries
        /// no melee/shooting distinction, so granting one for "-1 to hit in melee" would also penalise the
        /// unit's shooting - a different and strictly worse rule than the one the table printed. These
        /// grant the catalog's melee- and shooting-gated definitions instead, and the names below are what
        /// prove the right one landed.
        /// </summary>
        [TestCase(24, "-1 to hit rolls in melee", TestName = "Anomalous Gale (-1 melee)")]
        [TestCase(53, "+1 to hit rolls in melee", TestName = "Invigorating Fumes (+1 melee)")]
        [TestCase(23, "-1 to hit rolls when shooting", TestName = "Dazing Spores (-1 shooting)")]
        [TestCase(54, "+1 to hit rolls when shooting", TestName = "Enhancing Force-Field (+1 shooting)")]
        public async Task AHitModifierEvent_GrantsTheGatedRule_NotAGenericHitToken(int code, string rule)
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(code, Dice(1));

            Assert.That(GrantedRules(unit), Does.Contain(rule));
            Assert.That(GrantedRollModifiers.PeekNet(unit.GetValue(), ERollKind.Hit), Is.Zero,
                "a bare Hit token would apply in melee AND when shooting - which is not the printed rule.");
        }

        [Test]
        public async Task AHitModifierEvent_IsAlsoGoneAtTheEndOfTheRound()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));
            await Apply(24, Dice(1));
            Assert.That(GrantedRules(unit), Is.Not.Empty, "precondition.");

            new TokenClearService().ClearForHook(EHookID.Round_OnRoundEnd,
                new List<ITokenContainer> { unit.GetValue().Tokens });

            Assert.That(GrantedRules(unit), Is.Empty);
        }

        // -- Objectives ------------------------------------------------------------------------------

        [Test]
        public async Task CrumblingEarth_ReleasesAHeldMarker()
        {
            Unit(_me, new Position(10f, 10f));
            ObjectiveData held = Objective(new Position(20f, 20f), _me);

            await Apply(33, Dice(1));

            Assert.That(CampaignEventTargeting.IsCaptured(held), Is.False);
        }

        [Test]
        public async Task CrumblingEarth_WithNothingHeld_DoesNothing()
        {
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), null);

            string line = await Apply(33, Dice(1));

            Assert.That(line, Does.Contain("No objective marker was held"));
        }

        [Test]
        public async Task SpotterDrone_GivesALooseMarkerToSomebodyInTheGame()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 10f));
            ObjectiveData loose = Objective(new Position(20f, 20f), null);

            await Apply(34, Dice(1));

            Assert.That(CampaignEventTargeting.IsCaptured(loose), Is.True);
            Assert.That(new[] { _me, _them }, Does.Contain(loose.OwnerID!.Value),
                "the marker must go to a player who is actually playing.");
        }

        /// <summary>
        /// "becomes captured by the opponent" - so it has to CHANGE hands. Picking the holder again would
        /// satisfy a careless reading and do nothing.
        /// </summary>
        [Test]
        public async Task RobotGuide_HandsAHeldMarkerToSomebodyElse()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 10f));
            ObjectiveData held = Objective(new Position(20f, 20f), _me);

            await Apply(35, Dice(1));

            Assert.That(held.OwnerID!.Value, Is.EqualTo(_them), "it must leave the player who held it.");
        }

        // -- Rewards ---------------------------------------------------------------------------------

        [Test]
        public async Task InjectorPouch_PutsXpOnTheUnitsOwnSheet()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(62, Dice(1));

            Assert.That(Sheet(unit).EventXp, Is.EqualTo(1));
        }

        [Test]
        public async Task KitBox_RollsItsXp()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            // One candidate, so no die is spent on the pick (Index short-circuits) - the 5 is the D3,
            // and a 5 reads as 3.
            await Apply(65, Dice(5));

            Assert.That(Sheet(unit).EventXp, Is.EqualTo(3));
        }

        [Test]
        public async Task CreditsStash_PutsTreasuryOnASheetToCarryOutOfTheGame()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(61, Dice(1));

            Assert.That(Sheet(unit).EventPoints, Is.EqualTo(25),
                "the army's treasury is not on the table, so the reward rides a sheet out of the game.");
        }

        [Test]
        public async Task TreasureHolopad_RollsItsPoints()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            // One candidate, so the 5 is the D3: it reads as 3, times 25pts.
            await Apply(64, Dice(5));

            Assert.That(Sheet(unit).EventPoints, Is.EqualTo(75));
        }

        [Test]
        public async Task GrenadeBag_PutsVpOnASheet()
        {
            DataBinding<UnitData> unit = Unit(_me, new Position(10f, 10f));

            await Apply(63, Dice(1));

            Assert.That(Sheet(unit).EventVp, Is.EqualTo(1));
        }

        [Test]
        public async Task ARewardEvent_WithNoCampaignUnit_DoesNothing()
        {
            Unit(_me, new Position(10f, 10f), campaign: false);

            string line = await Apply(61, Dice(1));

            Assert.That(line, Does.Contain("nothing happened"));
        }

        // -- Terrain ---------------------------------------------------------------------------------

        /// <summary>
        /// The area events become REAL terrain for the round, which is what the book describes ("the area
        /// counts as Dangerous terrain") and means the engine's movement asks the terrain where it is -
        /// so a unit that walks in later is affected and one that leaves is not, with nothing
        /// re-evaluating anything.
        /// </summary>
        [Test]
        public async Task VenomousMoss_PlacesDangerousGroundAroundAMarker()
        {
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), null);
            int before = _context.TableState.Terrain.Objects.Count();

            await Apply(13, Dice(1));

            var added = _context.TableState.Terrain.Objects.ToList();
            Assert.That(added.Count, Is.EqualTo(before + 1));
            Assert.That(added.Any(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)
                                       && t.Shape.IsPointWithinZone(new Float2(20f, 20f))), Is.True,
                "the hazard must actually cover the marker it was placed around.");
        }

        [Test]
        public async Task RazorVines_PlacesDifficultGroundInstead()
        {
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), null);

            await Apply(14, Dice(1));

            Assert.That(_context.TableState.Terrain.Objects.Any(t =>
                t.TerrainType.HasFlag(ETerrainType.Difficult)), Is.True);
        }

        [Test]
        public async Task TerrifyingQuakes_CoversAWholeTableQuarter()
        {
            Unit(_me, new Position(10f, 10f));

            await Apply(41, Dice(1));

            ITerrain placed = _context.TableState.Terrain.Objects.Single();
            Assert.That(placed.TerrainType.HasFlag(ETerrainType.Dangerous), Is.True);
            ZoneBounds bounds = ((IBoundedZone)placed.Shape).Bounds;
            Assert.That(bounds.Right - bounds.Left, Is.EqualTo(36f).Within(0.01f),
                "half of Grimdark Future's 72\" table.");
            Assert.That(bounds.Top - bounds.Bottom, Is.EqualTo(24f).Within(0.01f),
                "and half of its 48\".");
        }

        /// <summary>
        /// And it has to GO at the end of the round. An event that said "until the end of the round" and
        /// then left a minefield on the table for the rest of the game would be worse than one that did
        /// nothing at all.
        /// </summary>
        [Test]
        public async Task TemporaryGround_IsClearedAtTheEndOfTheRound()
        {
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), null);
            await Apply(13, Dice(1));
            Assert.That(_context.TableState.Terrain.Objects, Is.Not.Empty, "precondition.");

            int cleared = CampaignEventRunner.ClearTemporaryTerrain(_context);

            Assert.That(cleared, Is.EqualTo(1));
            Assert.That(_context.TableState.Terrain.Objects, Is.Empty);
        }

        /// <summary>
        /// ...and only the event's own ground. Clearing the map's real terrain would be catastrophic and
        /// completely invisible until somebody played a campaign mission on a map with a wood.
        /// </summary>
        [Test]
        public async Task ClearingTemporaryGround_LeavesTheMapsOwnTerrainAlone()
        {
            _store.Create(new TerrainData(ETerrainType.Cover,
                new RectangularZone(5f, 10f, 5f, 10f), heightInches: 3f, name: "A real wood"));
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), null);
            await Apply(13, Dice(1));

            int cleared = CampaignEventRunner.ClearTemporaryTerrain(_context);

            Assert.That(cleared, Is.EqualTo(1), "only the event's own piece.");
            Assert.That(_context.TableState.Terrain.Objects.Single().Name, Is.EqualTo("A real wood"));
        }

        // -- All 36 resolve (2026-11-06) ---------------------------------------------------------------

        /// <summary>
        /// Every entry of the table now does something. The last three - the two hit events and Thick
        /// Clouds - were the "not applied yet" ones; nothing in the runner may say that any more.
        /// </summary>
        [Test]
        public async Task EveryEventResolves_NoneSayNotAppliedYet()
        {
            Unit(_me, new Position(10f, 10f));
            Objective(new Position(20f, 20f), _me);
            Assert.That(CampaignEvents.All.Count(), Is.EqualTo(36));
            foreach (CampaignEvent e in CampaignEvents.All)
            {
                string line = await CampaignEventRunner.Apply(_context, e, Dice(1));
                Assert.That(line, Does.Not.Contain("not applied yet").IgnoreCase, $"event {e.Code}");
                Assert.That(line, Does.Not.Contain("not implemented").IgnoreCase, $"event {e.Code}");
            }
        }

        [Test]
        public async Task ExplosiveTrap_HitsRollDefense_AndFailuresWound()
        {
            // Every die a 1: every Defense roll fails. 2D3 on 6s = 6 hits, three 1-wound models.
            var always1 = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            DataBinding<UnitData> victim = Unit(_me, new Position(10f, 10f));

            string line = await CampaignEventRunner.Apply(always1, CampaignEvents.ByCode(12), Dice(6));

            Assert.That(line, Does.Contain("6 hit(s)"));
            Assert.That(victim.GetValue().GetIsAlive(), Is.False);
        }

        [Test]
        public async Task SolarFlare_OnlyHitsAUnitNearAnObjective()
        {
            var always6 = new TestGameContext(_store, new FixedFaceDiceRoller(6),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            Objective(new Position(20f, 20f), null);
            Unit(_me, new Position(50f, 40f));          // far from the marker
            DataBinding<UnitData> near = Unit(_them, new Position(21f, 20f));

            string line = await CampaignEventRunner.Apply(always6, CampaignEvents.ByCode(32), Dice(1));

            Assert.That(line, Does.StartWith(near.GetValue().Name));
            Assert.That(near.GetValue().GetIsAlive(), Is.True, "all sixes: every hit saved");
        }

        [Test]
        public async Task ThickClouds_HalvesRange_OnlyWhilstFullyInsideTheQuarter()
        {
            var (_, inside, outside) = QuarterForDieOne();
            DataBinding<UnitData> shooter = Unit(_me, inside);
            DataBinding<UnitData> target = Unit(_them, outside);
            var rifle = new Weapon("Rifle", 24f, 1, 0);

            await Apply(43, Dice(1));
            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), rifle, target.GetValue(), _context.RuleEvaluator),
                Is.EqualTo(12f));
            Assert.That(RangeRuleQueries.EffectiveRange(target.GetValue(), rifle, shooter.GetValue(), _context.RuleEvaluator),
                Is.EqualTo(24f), "the unit outside shoots normally");

            foreach (IModel m in shooter.GetValue().Models) ((ModelData)m).SetPosition(outside);
            Assert.That(RangeRuleQueries.EffectiveRange(shooter.GetValue(), rifle, target.GetValue(), _context.RuleEvaluator),
                Is.EqualTo(24f), "walking out: full range again");
        }

        // -- The four "whilst inside an area" events (36, 44, 45, 46) ---------------------------------

        /// <summary>The quarter a die of 1 picks on this game's table, and a point well inside it.</summary>
        private (ZoneRect Quarter, Position Inside, Position Outside) QuarterForDieOne()
        {
            (float w, float h) = GameWideConstants.TableSizeFor(EGameSystem.GrimdarkFuture);
            ZoneRect q = CampaignEventTargeting.PickQuarter(w, h, Dice(1)).Area;
            var inside = new Position((q.Left + q.Right) / 2f - 1f, (q.Bottom + q.Top) / 2f);
            float ox = q.Left < 1f ? q.Right + 6f : q.Left - 6f;
            return (q, inside, new Position(ox, (q.Bottom + q.Top) / 2f));
        }

        private static int Net(DataBinding<UnitData> unit, ERollKind roll) =>
            GrantedRollModifiers.PeekNet(unit.GetValue(), roll);

        [Test]
        public async Task SupportRobots_PlusOneDefense_OnlyWhilstFullyInsideTheQuarter()
        {
            var (_, inside, outside) = QuarterForDieOne();
            DataBinding<UnitData> inUnit = Unit(_me, inside);
            DataBinding<UnitData> outUnit = Unit(_them, outside);

            string line = await Apply(46, Dice(1));
            Assert.That(line, Does.Contain("quarter").And.Contain("+1 to Defense"));

            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Save);
            CampaignEventAreas.RefreshRollBonus(_context, outUnit.GetValue(), ERollKind.Save);
            Assert.That(Net(inUnit, ERollKind.Save), Is.EqualTo(1));
            Assert.That(Net(outUnit, ERollKind.Save), Is.EqualTo(0));

            // It walks out: "whilst" - the bonus goes with it, and does not stack on a second check.
            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Save);
            Assert.That(Net(inUnit, ERollKind.Save), Is.EqualTo(1), "refreshing twice does not stack");
            foreach (IModel m in inUnit.GetValue().Models) ((ModelData)m).SetPosition(outside);
            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Save);
            Assert.That(Net(inUnit, ERollKind.Save), Is.EqualTo(0));
        }

        [Test]
        public async Task ShiftingIllusions_PlusOneMorale_AndNotDefense()
        {
            var (_, inside, _) = QuarterForDieOne();
            DataBinding<UnitData> inUnit = Unit(_me, inside);

            await Apply(45, Dice(1));
            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Morale);
            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Save);
            Assert.That(Net(inUnit, ERollKind.Morale), Is.EqualTo(1));
            Assert.That(Net(inUnit, ERollKind.Save), Is.EqualTo(0));
        }

        [Test]
        public async Task PsychicPool_PlusOneDefense_WithinThreeInchesOfTheMarker()
        {
            Objective(new Position(20f, 20f), null);
            DataBinding<UnitData> near = Unit(_me, new Position(21f, 20f));
            DataBinding<UnitData> far = Unit(_them, new Position(40f, 40f));

            string line = await Apply(36, Dice(1));
            Assert.That(line, Does.Contain("objective marker 1"));

            CampaignEventAreas.RefreshRollBonus(_context, near.GetValue(), ERollKind.Save);
            CampaignEventAreas.RefreshRollBonus(_context, far.GetValue(), ERollKind.Save);
            Assert.That(Net(near, ERollKind.Save), Is.EqualTo(1));
            Assert.That(Net(far, ERollKind.Save), Is.EqualTo(0));
        }

        [Test]
        public async Task PsychicFog_FasterOnlyForAUnitThatActivatesInside()
        {
            var (_, inside, outside) = QuarterForDieOne();
            DataBinding<UnitData> inUnit = Unit(_me, inside);
            DataBinding<UnitData> outUnit = Unit(_them, outside);

            await Apply(44, Dice(1));
            CampaignEventAreas.OnActivationStart(_context, inUnit.GetValue());
            CampaignEventAreas.OnActivationStart(_context, outUnit.GetValue());

            Assert.That(GrantedRules(inUnit), Does.Contain(CampaignEventRuleCatalog.HastenedName));
            Assert.That(GrantedRules(outUnit), Does.Not.Contain(CampaignEventRuleCatalog.HastenedName));
        }

        [Test]
        public async Task TheAreasEndWithTheRound()
        {
            var (_, inside, _) = QuarterForDieOne();
            DataBinding<UnitData> inUnit = Unit(_me, inside);
            await Apply(46, Dice(1));

            CampaignEventRunner.ClearTemporaryTerrain(_context);
            CampaignEventAreas.RefreshRollBonus(_context, inUnit.GetValue(), ERollKind.Save);
            Assert.That(Net(inUnit, ERollKind.Save), Is.EqualTo(0));
        }

        [Test]
        public async Task AHalfInsideUnit_IsNotFullyWithin()
        {
            var (q, _, _) = QuarterForDieOne();
            // Three models in a row straddling the quarter's vertical edge.
            float edgeX = q.Left < 1f ? q.Right : q.Left;
            DataBinding<UnitData> straddler = Unit(_me, new Position(edgeX - 1.1f, (q.Bottom + q.Top) / 2f));

            await Apply(46, Dice(1));
            CampaignEventAreas.RefreshRollBonus(_context, straddler.GetValue(), ERollKind.Save);
            Assert.That(Net(straddler, ERollKind.Save), Is.EqualTo(0));
        }

        // -- The round roll --------------------------------------------------------------------------

        /// <summary>
        /// End to end: in a campaign game with a die that always rolls 6, every player fires an event and
        /// the log says which, with the book's own wording.
        /// </summary>
        [Test]
        public async Task RollForRound_WithEveryRollFiring_FiresOnePerPlayer()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_them, new Position(40f, 10f));
            var always6 = new TestGameContext(_store, new FixedFaceDiceRoller(6),
                ruleResolver: CoreRuleCatalog.CreateResolver());

            List<string> log = await CampaignEventRunner.RollForRound(always6);

            Assert.That(log.Count(l => l.StartsWith("Campaign event ")), Is.EqualTo(2),
                "two players, both rolling a 6, is two events.");
            Assert.That(log.Any(l => l.Contains("Godly Blessing")), Is.True,
                "a 6 then a 6 is event 66, and its name and text should both be in the log.");
        }

        [Test]
        public async Task RollForRound_WithEveryRollMissing_FiresNothing()
        {
            Unit(_me, new Position(10f, 10f));
            var always1 = new TestGameContext(_store, new FixedFaceDiceRoller(1),
                ruleResolver: CoreRuleCatalog.CreateResolver());

            Assert.That(await CampaignEventRunner.RollForRound(always1), Is.Empty);
        }
    }
}
