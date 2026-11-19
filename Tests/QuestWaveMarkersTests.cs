using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>#468: the Quest games' Frenzy / Growth markers are "removed once the next wave of reinforcements is
    /// deployed"; other tokens stay.</summary>
    [TestFixture]
    public class QuestWaveMarkersTests
    {
        [Test]
        public void ANewWave_RemovesEveryFrenzyAndGrowthMarker_AndNothingElse()
        {
            GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(1f, 1f), store);
            var models = new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) };
            var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Hero", quality: 4, defense: 4, modelBindings: models);
            DataBinding<UnitData> binding = store.GetDataBinding<UnitData>(store.Create(unit));
            UnitData u = binding.GetValue();
            u.Tokens.AddToken(new Token(new TokenType("PiercingFrenzyMarker"), 4, new TokenClearTrigger.ManualOnly()));
            u.Tokens.AddToken(new Token(new TokenType("DefensiveGrowthMarker"), 2, new TokenClearTrigger.ManualOnly()));
            u.Tokens.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            Assert.That(QuestWaveMarkers.Clear(store), Is.EqualTo(6));
            Assert.That(u.Tokens.GetTokenCount(new TokenType("PiercingFrenzyMarker")), Is.Zero);
            Assert.That(u.Tokens.GetTokenCount(new TokenType("DefensiveGrowthMarker")), Is.Zero);
            Assert.That(u.Tokens.HasToken(TokenType.Shaken), Is.True, "only the wave-scoped markers go");
        }
    }
}
