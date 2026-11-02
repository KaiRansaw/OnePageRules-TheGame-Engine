using FDG.Data;
using FDG.Players;
using FDG.Rules.Tokens;
using FDG.Rules.Foundation;
using FDG.Stages;
using FDG.GameMasterTools;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// GF: Firefight and AoF: Skirmish, **on OPR 3.6.0**.
    ///
    /// <para>
    /// **#414 rewrote this fixture, and what it deleted is the point.** It used to pin Firefight's
    /// v3.5.1 subsystems: wound markers on the last model with a Tough-gated wound-effect roll, and an
    /// army-wide morale test at the end of every round *instead of* ordinary morale. **GFF Core Rules
    /// v3.6.0 contains neither** - it reads like the full game, with standard wounds, standard Morale
    /// Tests and standard Melee Results. So those tests are gone rather than updated, and the cases
    /// below pin the opposite: that Firefight now behaves like Grimdark Future everywhere those two
    /// systems used to intervene.
    /// </para>
    ///
    /// <para>
    /// What is still Firefight: the 4'x4' table, 6" coherency, the Horde Mode sizes and rewards, and
    /// **Pushing**, which survived 3.6.0 but had to be re-triggered - it used to hang off the
    /// wound-effect roll that no longer exists.
    /// </para>
    /// </summary>
    [TestFixture]
    public class FirefightRulesTests
    {
        [TearDown]
        public void Reset() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        [Test]
        public void Firefight_Uses4x4Table_And6InchCoherency()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFFirefight);
            Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(48f));
            Assert.That(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, Is.EqualTo(48f));
            Assert.That(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES, Is.EqualTo(6f));

            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            Assert.That(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, Is.EqualTo(72f));
            Assert.That(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES, Is.EqualTo(9f));
        }

        // ── Wounds: the same as the full game now ────────────────────────────────────────────────────

        [Test]
        public async Task Wounds_KillAModelEach_WithNoMarkersLeftBehind()
        {
            // 3.6.0: "Once a model has taken as many wounds as its Tough value, it is killed (remove from
            // play)." Under 3.5.1 this unit would have lost two models and the third would be standing
            // with a wound marker on it, waiting on a roll.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1,
                count: 1, models: 3);
            UnitData unit = units[0].GetValue();

            await CasualtyPresentation.ApplyUnitWounds(c, unit, 3f);

            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.Zero,
                "three wounds kill three one-wound models, exactly as in Grimdark Future.");
            Assert.That(unit.Tokens.GetAllTokens().Any(t => t.Type.Id.Contains("WoundMarker")), Is.False,
                "and nothing marker-shaped is left on the unit - that system is deleted, not disabled.");
        }

        [Test]
        public async Task Wounds_StopAtTheLastModel_InsteadOfRollingForIt()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1,
                count: 1, models: 2);
            UnitData unit = units[0].GetValue();

            await CasualtyPresentation.ApplyUnitWounds(c, unit, 1f);

            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(1));
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False,
                "the 3.5.1 wound-effect roll could Shake a unit off a single wound. 3.6.0 has no such "
                + "roll - a wound is a wound, and morale is tested in the ordinary places.");
        }

        // ── Morale: also the same as the full game now ───────────────────────────────────────────────

        [Test]
        public async Task AUnitLeftAtHalfStrength_TakesAWoundDrivenMoraleTest()
        {
            // THE morale case. `ResolveWoundDrivenMorale` used to return null on sight of a skirmish game
            // ("Firefight: army morale at round end only"), so a Firefight unit shot to half strength
            // never tested. 3.6.0 restored the ordinary rule, so it tests - and on a 1 it is Shaken.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1,
                count: 1, models: 4);
            UnitData unit = units[0].GetValue();
            foreach (IModel model in unit.Models.Take(2)) model.DealWounds(model.TotalWounds);

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(c, units[0],
                remainingWoundsBefore: 4f, EMoraleTrigger.Shooting, attackerLaunchDistanceInches: 12f);

            Assert.That(result, Is.False, "it tested (not null) and failed on a 1.");
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public async Task TheSameUnitInGrimdarkFuture_BehavesIdentically()
        {
            // The other half of the claim: Firefight is not merely testing now, it is testing the SAME
            // way. If these two ever diverge, one of them has grown a special case.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GrimdarkFuture,
                die: 1, count: 1, models: 4);
            foreach (IModel model in units[0].GetValue().Models.Take(2)) model.DealWounds(model.TotalWounds);

            bool? result = await MoraleUtilities.ResolveWoundDrivenMorale(c, units[0],
                remainingWoundsBefore: 4f, EMoraleTrigger.Shooting, attackerLaunchDistanceInches: 12f);

            Assert.That(result, Is.False);
        }

        // ── Pushing: kept by 3.6.0, re-triggered by #414 ─────────────────────────────────────────────

        [Test]
        public void Pushing_OffersTheMove_WhenTheAttackerRolls4Plus()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 4,
                count: 2);
            UnitData target = units[0].GetValue();
            target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            // ShouldPush, not TryPush: the push itself raises a move request, which in a unit test has
            // nobody to answer it and hangs the run. The gate and the die are the rules; where the model
            // ends up is the player's.
            bool pushed = FirefightRules.ShouldPush(c, target, units[1].GetValue());

            Assert.That(pushed, Is.True, "a 4+ lets the attacker push a Shaken target up to 2\".");
        }

        [Test]
        public void Pushing_DoesNothing_OnAThreeOrLess()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 3,
                count: 2);
            UnitData target = units[0].GetValue();
            target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(FirefightRules.ShouldPush(c, target, units[1].GetValue()), Is.False);
        }

        [Test]
        public void Pushing_NeedsTheTargetToActuallyBeShaken()
        {
            // The book's trigger is "whenever a unit IS Shaken after being attacked", not "fails a test".
            // A unit rescued by Fearless or No Retreat failed its test and is not Shaken, so there is
            // nothing to push.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 6,
                count: 2);

            Assert.That(FirefightRules.ShouldPush(c, units[0].GetValue(), units[1].GetValue()), Is.False);
        }

        [Test]
        public void Pushing_IsSkirmishOnly()
        {
            // Pushing is in the GFF and AoFS books and in neither full-game book, so a Grimdark Future
            // game must never roll for it - including never consuming a die, which would desync a seeded
            // run against the same game played in Firefight.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GrimdarkFuture,
                die: 6, count: 2);
            UnitData target = units[0].GetValue();
            target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            Assert.That(FirefightRules.ShouldPush(c, target, units[1].GetValue()), Is.False);
        }

        [Test]
        public void Pushing_NeedsALivingAttacker()
        {
            // A unit wiped out by the strike-back it provoked cannot push what it Shook on its way out.
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 6,
                count: 2);
            UnitData target = units[0].GetValue();
            target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            Kill(units[1]);

            Assert.That(FirefightRules.ShouldPush(c, target, units[1].GetValue()), Is.False);
            Assert.That(FirefightRules.ShouldPush(c, target, attacker: null), Is.False,
                "and a wound with no attacker at all - dangerous terrain, a random event - pushes nothing.");
        }

        // ── Game Master, on the standard wound path ──────────────────────────────────────────────────

        [Test]
        public async Task GameMaster_WoundAndHeal_UseOrdinaryWoundsInFirefight()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1,
                count: 1, models: 2);
            UnitData unit = units[0].GetValue();
            var hub = new GameMasterHub();
            hub.Attach(c);

            hub.WoundUnit(unit.ID, 2);
            await hub.DrainAsync();
            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.Zero,
                "two wounds kill both one-wound models; the old path left one standing with a marker.");

            hub.HealUnit(unit.ID, 1);
            await hub.DrainAsync();
            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(1),
                "and a heal brings a model back rather than peeling a marker off first.");

            hub.ReviveUnit(unit.ID);
            await hub.DrainAsync();
            Assert.That(unit.Models.All(m => m.GetIsAlive()), Is.True);
        }

        [Test]
        public void FirefightHorde_UsesSmallerAreasAndRewards()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GFFirefight);
            Assert.That(FDG.StateMachine.HordeRules.DeploymentRadiusInches, Is.EqualTo(6f));
            Assert.That(FDG.StateMachine.HordeRules.ShopRangeInches, Is.EqualTo(6f));
            // #409 (OPR 3.6.0): the skirmish reward is 30 per objective and +15 for the priority one.
            // It was 10 and +5 in the 3.5.0 Firefight book - 3.6 tripled both.
            Assert.That(FDG.StateMachine.HordeRules.WaveReward(2, prioritySeized: true), Is.EqualTo(75), "30 + 30 + 15");
            Assert.That(HordePlacement.CrowdedRadiiInches[0], Is.EqualTo(6f));

            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            Assert.That(FDG.StateMachine.HordeRules.DeploymentRadiusInches, Is.EqualTo(9f));
            Assert.That(FDG.StateMachine.HordeRules.WaveReward(2, prioritySeized: true), Is.EqualTo(100), "40 + 40 + 20");
            Assert.That(HordePlacement.CrowdedRadiiInches[0], Is.EqualTo(9f));
        }

        private static void Kill(DataBinding<UnitData> u)
        {
            foreach (var m in u.GetValue().Models) m.DealWounds(m.TotalWounds - m.WoundsDealt);
        }

        private static (TestGameContext, List<DataBinding<UnitData>>) Army(EGameSystem system, int die, int count, int models = 1)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var me = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(0, new List<PlayerID> { me }));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            var c = new TestGameContext(store, new FixedDiceRoller(die), settings: settings);
            var units = new List<DataBinding<UnitData>>();
            for (int i = 0; i < count; i++)
            {
                var mbs = new List<DataBinding<ModelData>>();
                for (int m = 0; m < models; m++)
                {
                    var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                        initialPosition: new Position(10f + i * 3f + m, 10f), gameDataStore: store);
                    mbs.Add(store.GetDataBinding<ModelData>(store.Create(model)));
                }
                var unit = new UnitData(me, $"Squad {i}", quality: 4, defense: 5, modelBindings: mbs);
                units.Add(store.GetDataBinding<UnitData>(store.Create(unit)));
            }
            store.Create(new ArmyData(me, units));
            return (c, units);
        }
    }
}
