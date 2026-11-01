using System.Linq;
using FDG.Players;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - "<i>Players alternate in placing X units, starting with the player that activates next.</i>"
    ///
    /// <para>
    /// Scout, Ambush and Re-Deployment all carry that sentence; the engine honoured it only in
    /// Re-Deployment, which is an interactive round-robin of prompts. The other two are non-interactive
    /// sweeps over a collection built team -&gt; player -&gt; army -&gt; unit, so one player placed every
    /// unit they owned before the other placed any. All the placements were legal and nothing was
    /// logged; the only symptom was that the second player got to see the whole enemy commitment first.
    /// </para>
    /// <para>
    /// These cases are on the ordering itself, away from either stage, because the same helper now
    /// serves both rules and a fault here would be wrong in two places at once.
    /// </para>
    /// </summary>
    [TestFixture]
    public class AlternatingPlacementTests
    {
        private readonly PlayerID _first = new(Guid.NewGuid());
        private readonly PlayerID _second = new(Guid.NewGuid());
        private readonly PlayerID _third = new(Guid.NewGuid());

        private sealed record Unit(string Name, PlayerID Owner);

        private static List<string> Order(IEnumerable<Unit> units, params PlayerID[] turnOrder) =>
            AlternatingPlacement.Interleave(units, u => u.Owner, turnOrder)
                .Select(u => u.Name).ToList();

        /// <summary>The headline case: two units each, placed one at a time, not two then two.</summary>
        [Test]
        public void TwoPlayersAlternate_OneUnitEach()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first),
                new Unit("B1", _second), new Unit("B2", _second),
            };

            Assert.That(Order(units, _first, _second),
                Is.EqualTo(new[] { "A1", "B1", "A2", "B2" }).AsCollection,
                "army-by-army would give A1 A2 B1 B2 - which lets the second player watch the entire "
                + "enemy screen go down before committing anything.");
        }

        /// <summary>"Starting with the player that activates next" - the turn order decides who leads.</summary>
        [Test]
        public void TheStatedOrderDecidesWhoGoesFirst()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first),
                new Unit("B1", _second), new Unit("B2", _second),
            };

            Assert.That(Order(units, _second, _first),
                Is.EqualTo(new[] { "B1", "A1", "B2", "A2" }).AsCollection,
                "the same units with the turn order reversed must lead with the other player.");
        }

        /// <summary>
        /// Uneven counts: alternating while both have units, then the player with more takes the rest.
        /// The alternative - interleaving evenly across the whole list - would have the larger force
        /// placing late units after the smaller one has finished, which is the same information
        /// advantage in reverse.
        /// </summary>
        [Test]
        public void UnevenCounts_TheLongerSideTakesTheRemainingTurns()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first), new Unit("A3", _first),
                new Unit("B1", _second),
            };

            Assert.That(Order(units, _first, _second),
                Is.EqualTo(new[] { "A1", "B1", "A2", "A3" }).AsCollection);
        }

        /// <summary>Three-way games alternate too - the clause says "players", not "both players".</summary>
        [Test]
        public void ThreePlayersTakeTurnsInOrder()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first),
                new Unit("B1", _second), new Unit("B2", _second),
                new Unit("C1", _third), new Unit("C2", _third),
            };

            Assert.That(Order(units, _first, _second, _third),
                Is.EqualTo(new[] { "A1", "B1", "C1", "A2", "B2", "C2" }).AsCollection);
        }

        /// <summary>Each owner's own units keep their relative order - only the interleaving changes.</summary>
        [Test]
        public void EachOwnersUnitsKeepTheirRelativeOrder()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first), new Unit("A3", _first),
                new Unit("B1", _second), new Unit("B2", _second), new Unit("B3", _second),
            };

            List<string> mine = Order(units, _first, _second).Where(n => n.StartsWith("A")).ToList();

            Assert.That(mine, Is.EqualTo(new[] { "A1", "A2", "A3" }).AsCollection);
        }

        /// <summary>
        /// One player owning everything has nobody to alternate with, so the list is unchanged - and a
        /// solo game must not have its deployment order shuffled by a rule about taking turns.
        /// </summary>
        [Test]
        public void ASingleOwnerIsLeftAlone()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first), new Unit("A3", _first),
            };

            Assert.That(Order(units, _first, _second),
                Is.EqualTo(new[] { "A1", "A2", "A3" }).AsCollection);
        }

        /// <summary>
        /// A player missing from the turn order still places - late, but placed. Dropping a unit from
        /// deployment because its owner was absent from a list would be far worse than the ordering
        /// fault this helper exists to fix, so the degenerate case fails safe.
        /// </summary>
        [Test]
        public void AnOwnerMissingFromTheTurnOrder_StillPlaces()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("B1", _second),
            };

            List<string> withOnlyFirstNamed = Order(units, _first);
            List<string> withNobodyNamed = Order(units);

            Assert.Multiple(() =>
            {
                Assert.That(withOnlyFirstNamed, Is.EquivalentTo(new[] { "A1", "B1" }));
                Assert.That(withNobodyNamed, Is.EqualTo(new[] { "A1", "B1" }).AsCollection,
                    "with no turn order at all, the input order stands rather than being dropped.");
            });
        }

        /// <summary>Nothing to place, or one thing - no crash, no reordering.</summary>
        [Test]
        public void EmptyAndSingleAreSafe()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Order(Array.Empty<Unit>(), _first, _second), Is.Empty);
                Assert.That(Order(new[] { new Unit("Only", _first) }, _first, _second),
                    Is.EqualTo(new[] { "Only" }).AsCollection);
            });
        }

        /// <summary>Every input comes out exactly once, whatever the shape.</summary>
        [Test]
        public void NothingIsLostOrDuplicated()
        {
            var units = new[]
            {
                new Unit("A1", _first), new Unit("A2", _first), new Unit("A3", _first),
                new Unit("B1", _second),
                new Unit("C1", _third), new Unit("C2", _third),
            };

            List<string> ordered = Order(units, _second, _third, _first);

            Assert.That(ordered, Is.EquivalentTo(units.Select(u => u.Name)));
        }
    }
}
