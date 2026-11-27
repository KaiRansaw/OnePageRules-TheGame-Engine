using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using NUnit.Framework;

namespace FDG.Tests
{
    // Bug hunt 2026-11-27 (GF, Terrain & Objective Effects): a marker's rolled effect was announced as "Objective
    // seized: Holy Monument" and logged at a position - nothing said WHICH of the numbered markers it was.
    [TestFixture]
    public class ObjectiveEffectNamingTests
    {
        private sealed class Lines : ITextOutput
        {
            public readonly List<string> All = new();
            public void Log(string message, TextColor? color = null) => All.Add(message);
        }

        [Test]
        public async Task TheSeizedMarker_IsNamedByItsNumber()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = EAdvancedRules.TerrainObjectiveEffects;
            var log = new Lines();
            var context = new TestGameContext(store, new FixedFaceDiceRoller(6), textOutput: log, settings: settings);
            var me = new PlayerID(Guid.NewGuid());
            store.Create(new TeamData(1, new List<PlayerID> { me }));
            store.Create(new ObjectiveData(new Position(18f, 18f), store));
            store.Create(new ObjectiveData(new Position(51f, 18f), store));
            context.TableState.Objectives.Objects.ElementAt(1).SetOwner(me);

            await BattlefieldEffects.AfterObjectives(context);

            Assert.That(log.All, Has.Some.StartsWith("Objective 2 seized: Holy Monument"));
            Assert.That(log.All, Has.Some.Contains("objective 2 (at 51,18)"));
        }
    }
}
