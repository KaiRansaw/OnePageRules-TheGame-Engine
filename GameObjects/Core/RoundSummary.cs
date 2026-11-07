using System.Collections.Generic;

namespace FDG
{
    /// <summary>
    /// What happened in one finished round, side by side for every player: objectives held at the end of it
    /// and how that changed, victory points where the game keeps them (Mission Cards), and which of their
    /// units were destroyed during it. Kai's round summary card (decision-making plan, 2026-10; built
    /// 2026-11-07). Recorded by the host at the end of each round onto the progress record, so a joined
    /// player reads exactly the same card.
    /// </summary>
    public sealed class RoundSummary
    {
        public int Round { get; set; }
        public List<RoundSideSummary> Sides { get; set; } = new();
    }

    public sealed class RoundSideSummary
    {
        public int SlotId { get; set; }
        public string Name { get; set; } = "";
        public int ObjectivesHeld { get; set; }
        /// <summary>Change in objectives held since the start of the round.</summary>
        public int ObjectivesChange { get; set; }
        /// <summary>Victory points after the round, or null in a game that keeps none.</summary>
        public int? Vp { get; set; }
        public int VpChange { get; set; }
        /// <summary>This side's units destroyed during the round (names). Null when it could not be known -
        /// a game resumed part-way through the round has no record of what was alive at its start.</summary>
        public List<string>? UnitsLost { get; set; } = new();
    }
}
