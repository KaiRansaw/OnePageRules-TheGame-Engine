using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Bug hunt 2026-11-28: the unit card headed an Age of Fantasy Hero's item "RELIC"; the AoF and AoF: Skirmish
    /// Advanced Rulebooks (p36 / p33) call them magic items ("each Hero may equip one magic item").</summary>
    [TestFixture]
    public class RelicItemLabelTests
    {
        [TestCase(EGameSystem.AgeOfFantasy, "Magic Item")]
        [TestCase(EGameSystem.AoFSkirmish, "Magic Item")]
        [TestCase(EGameSystem.GrimdarkFuture, "Relic")]
        [TestCase(EGameSystem.GFFirefight, "Relic")]
        public void TheCardNamesTheItemAsThisGamesBookDoes(EGameSystem system, string label) =>
            Assert.That(RelicWargear.ItemLabel(system), Is.EqualTo(label));
    }
}
