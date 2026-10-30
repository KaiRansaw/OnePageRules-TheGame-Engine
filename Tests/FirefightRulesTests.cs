using FDG.Data;
using FDG.Players;
using FDG.Rules.Tokens;
using FDG.Rules.Foundation;
using FDG.Rules.Definitions;
using FDG.Stages;
using FDG.GameMasterTools;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>GF: Firefight slice 1: table, coherency, and end-of-round army morale.</summary>
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

        [Test]
        public async Task ArmyAtHalf_EveryUnitTests_FailShakens_FailWhileShakenRouts()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 4);
            Kill(units[0]); Kill(units[1]);
            units[3].GetValue().Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            await FirefightRules.ArmyMoraleAtRoundEnd(c);

            Assert.That(units[2].GetValue().Tokens.HasToken(TokenType.Shaken), Is.True, "failed test: Shaken");
            Assert.That(units[3].GetValue().GetIsAlive(), Is.False, "failed while Shaken: routed");
        }

        [Test]
        public async Task ArmyAboveHalf_NoTest()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 4);
            Kill(units[0]);
            await FirefightRules.ArmyMoraleAtRoundEnd(c);
            Assert.That(units.Skip(1).Any(u => u.GetValue().Tokens.HasToken(TokenType.Shaken)), Is.False);
        }

        [Test]
        public async Task FullGame_NoRoundEndArmyMorale()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GrimdarkFuture, die: 1, count: 2);
            Kill(units[0]);
            await FirefightRules.ArmyMoraleAtRoundEnd(c);
            Assert.That(units[1].GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
        }

        [Test]
        public async Task Firefight_NoMoraleFromWounds()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1, models: 2);
            UnitData unit = units[0].GetValue();
            unit.Models.First().DealWounds(unit.Models.First().TotalWounds);
            Assert.That(await MoraleUtilities.ResolveWoundDrivenMorale(c, units[0], remainingWoundsBefore: 2f), Is.Null);
        }

        [Test]
        public async Task WoundEffects_EachWoundKillsAModel_LastModelRolls_LowRollShakens()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1, models: 3);
            UnitData unit = units[0].GetValue();
            int killed = await FirefightRules.ApplyWounds(c, unit, 3f, attacker: null, melee: false);
            Assert.That(killed, Is.EqualTo(2));
            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(1));
            Assert.That(unit.Tokens.GetTokenCount(FirefightRules.WoundMarker), Is.EqualTo(1));
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True, "1 + 1 marker = 2, below 6");
        }

        [Test]
        public async Task WoundEffects_HighRoll_KnocksOut()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 5, count: 1);
            UnitData unit = units[0].GetValue();
            await FirefightRules.ApplyWounds(c, unit, 1f, attacker: null, melee: false);
            Assert.That(unit.GetIsAlive(), Is.False, "5 + 1 marker = 6: Knocked Out");
        }

        [Test]
        public async Task WoundEffects_Tough_RollsOnlyAtXMarkers_OutOn5PlusX()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 4, count: 1);
            UnitData unit = units[0].GetValue();
            ((ModelData)unit.Models.First()).SetMaxWounds(3);
            await FirefightRules.ApplyWounds(c, unit, 2f, attacker: null, melee: false);
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False, "2 markers, Tough(3): no roll yet");
            await FirefightRules.ApplyWounds(c, unit, 1f, attacker: null, melee: false);
            Assert.That(unit.GetIsAlive(), Is.True, "4 + 3 = 7, needs 8");
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True);
            await FirefightRules.ApplyWounds(c, unit, 1f, attacker: null, melee: false);
            Assert.That(unit.GetIsAlive(), Is.False, "4 + 4 + 1 Shaken = 9: Knocked Out");
        }

        [Test]
        public async Task OtherWoundSources_UseWoundEffects()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1, models: 2);
            UnitData unit = units[0].GetValue();
            await CasualtyPresentation.ApplyUnitWounds(c, unit, 2f);
            Assert.That(unit.GetIsAlive(), Is.True);
            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.True);
        }

        [Test]
        public async Task BatchedWounds_UseWoundEffects()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1, models: 3);
            UnitData unit = units[0].GetValue();
            var pending = unit.Models.Select(m => new PendingModelWound(m, 1f)).ToList();
            await CasualtyPresentation.ApplyAndPresent(c, pending, unit.ID, unit.Name);
            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(1), "two die, the last gets a marker");
            Assert.That(unit.Tokens.GetTokenCount(FirefightRules.WoundMarker), Is.EqualTo(1));
        }

        [Test]
        public async Task MarkerSummary_ShowsMarkersAndThreshold()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1);
            UnitData unit = units[0].GetValue();
            ((ModelData)unit.Models.First()).SetMaxWounds(3);
            Assert.That(FirefightRules.MarkerSummary(unit), Is.Null);
            await FirefightRules.ApplyWounds(c, unit, 2f, attacker: null, melee: false);
            Assert.That(FirefightRules.MarkerSummary(unit), Is.EqualTo("Wound markers: 2 (rolls from 3, Knocked Out on 8+)"));
        }

        [Test]
        public async Task GameMaster_WoundHealRevive_UseMarkersInFirefight()
        {
            (TestGameContext c, List<DataBinding<UnitData>> units) = Army(EGameSystem.GFFirefight, die: 1, count: 1, models: 2);
            UnitData unit = units[0].GetValue();
            var hub = new GameMasterHub();
            hub.Attach(c);
            hub.WoundUnit(unit.ID, 2);
            await hub.DrainAsync();
            Assert.That(unit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(1));
            Assert.That(unit.Tokens.GetTokenCount(FirefightRules.WoundMarker), Is.EqualTo(1));
            hub.HealUnit(unit.ID, 1);
            await hub.DrainAsync();
            Assert.That(unit.Tokens.GetTokenCount(FirefightRules.WoundMarker), Is.EqualTo(0), "heal takes a marker off first");
            hub.WoundUnit(unit.ID, 1);
            hub.ReviveUnit(unit.ID);
            await hub.DrainAsync();
            Assert.That(unit.Tokens.HasToken(FirefightRules.WoundMarker), Is.False);
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
