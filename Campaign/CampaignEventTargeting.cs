using FDG.Players;

namespace FDG.Campaign
{
    /// <summary>One of the four table quarters, as the rect a unit must be fully inside.</summary>
    /// <param name="Index">0-3, so a log line can name which one without describing it twice.</param>
    /// <param name="Area">The quarter itself.</param>
    public readonly record struct TableQuarter(int Index, ZoneRect Area);

    /// <summary>
    /// #432 - picking what a campaign random event happens to.
    ///
    /// <para>
    /// Split out from the applying because this half is where the mistakes live and the other half needs
    /// a running game to test. "One random unit" has to mean a LIVING unit of someone who is actually
    /// playing; "one random non-Shaken unit" has to come up empty rather than pick a Shaken one when
    /// every unit is Shaken; "one random captured objective marker" must not pick an unheld one. Each of
    /// those is a filter, every filter can be empty, and an empty filter means <b>the event does
    /// nothing</b> - which is the book's own answer and not an error.
    /// </para>
    ///
    /// <para>
    /// Everything here takes its randomness as a <c>Func&lt;int&gt;</c> d6 so a test can aim an event at
    /// a chosen target, and uses the same roll-and-index scheme throughout: a d6 reduced modulo the
    /// candidate count. That is biased for counts that do not divide 6, and deliberately so - the
    /// alternative is burning dice in a reroll loop, and the engine's seeded-replay guarantee cares far
    /// more about a stable dice stream than about a perfectly flat pick among 5 units.
    /// </para>
    /// </summary>
    public static class CampaignEventTargeting
    {
        /// <summary>
        /// Whether an event happens at all: the book has every player roll one die at the start of each
        /// round, and <see cref="CampaignEvents.TriggerRoll"/>+ fires one.
        /// </summary>
        public static bool ShouldFire(Func<int> d6) => Math.Clamp(d6(), 1, 6) >= CampaignEvents.TriggerRoll;

        /// <summary>Picks one of <paramref name="candidates"/>, or null when there are none.</summary>
        public static T? PickOne<T>(IReadOnlyList<T> candidates, Func<int> d6) where T : class =>
            candidates.Count == 0 ? null : candidates[Index(candidates.Count, d6)];

        /// <summary>
        /// The index this roll picks among <paramref name="count"/> candidates. One die, modulo the
        /// count - see the class note on why this is not rerolled for flatness.
        /// </summary>
        public static int Index(int count, Func<int> d6) =>
            count <= 1 ? 0 : (Math.Clamp(d6(), 1, 6) - 1) % count;

        /// <summary>
        /// Every unit an event may pick: alive, and belonging to a player in the game. Dead units are
        /// still on the table, and an event that wounded a corpse would read as the event doing nothing.
        /// </summary>
        public static List<IUnit> EventableUnits(ITableState table, Func<IUnit, bool>? filter = null) =>
            table.Units.Objects
                .Where(u => u.GetIsAlive() && (filter == null || filter(u)))
                .ToList();

        /// <summary>
        /// The units of one army, for an event scoped to a single player ("one random unit" in a game
        /// where the event picked an army first).
        /// </summary>
        public static List<IUnit> UnitsOf(ITableState table, PlayerID player, Func<IUnit, bool>? filter = null) =>
            EventableUnits(table, u => u.PlayerID == player && (filter == null || filter(u)));

        /// <summary>Objectives matching a filter - captured, uncaptured, or any.</summary>
        public static List<IObjective> Objectives(ITableState table, Func<IObjective, bool>? filter = null) =>
            table.Objectives.Objects.Where(o => filter == null || filter(o)).ToList();

        /// <summary>Whether a marker is held by somebody. The unowned sentinel is an empty Guid.</summary>
        public static bool IsCaptured(IObjective objective) =>
            objective.OwnerID is { } owner && owner.ID != Guid.Empty;

        /// <summary>
        /// The units within <paramref name="inches"/> of a point - the shape behind "any unit within 3"
        /// of one random objective marker". Measured per living model, so a unit with one model in range
        /// is in range.
        /// </summary>
        public static List<IUnit> UnitsNear(ITableState table, Position point, float inches) =>
            EventableUnits(table, u => u.Models.Any(m =>
                m.GetIsAlive() && Position.GetDistance2D(m.Position, point) <= inches));

        /// <summary>
        /// One random table quarter. The table is laid out from the origin, so the quarters are the two
        /// halves of each axis; <paramref name="width"/> and <paramref name="height"/> come from the
        /// game's own system, because a 48x48 Firefight table quarters differently from a 72x48 one.
        /// </summary>
        public static TableQuarter PickQuarter(float width, float height, Func<int> d6)
        {
            int index = Index(4, d6);
            float midX = width / 2f, midZ = height / 2f;
            bool right = index % 2 == 1;
            bool top = index >= 2;
            return new TableQuarter(index, new ZoneRect(
                right ? midX : 0f, right ? width : midX,
                top ? midZ : 0f, top ? height : midZ));
        }

        /// <summary>
        /// Whether every living model of the unit is inside the area - the book's "fully within". A unit
        /// with no living models is not within anything.
        /// </summary>
        public static bool FullyWithin(IUnit unit, ZoneRect area)
        {
            bool any = false;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                any = true;
                if (!area.Contains(model.Position)) return false;
            }
            return any;
        }

        /// <summary>
        /// The players with at least one living unit, for "one random army" / "one random player". A
        /// player who has been wiped out cannot be handed 25 pts by an event.
        /// </summary>
        public static List<PlayerID> PlayersInPlay(ITableState table) =>
            EventableUnits(table).Select(u => u.PlayerID).Distinct().ToList();

        /// <summary>
        /// How much of the effect this firing delivers: the event's dice, summed, times its amount - so
        /// "D3 wounds" is one D3, "2D3 hits" is two D3 <b>summed</b>, and "D3 x 25pts" is one D3 times
        /// 25. A D3 is half a D6 rounded up, which is how every OPR book rolls one.
        /// </summary>
        public static int RollAmount(CampaignEvent campaignEvent, Func<int> d6)
        {
            if (campaignEvent.D3Dice <= 0) return campaignEvent.Amount;

            int total = 0;
            for (int i = 0; i < campaignEvent.D3Dice; i++) total += (Math.Clamp(d6(), 1, 6) + 1) / 2;
            return total * campaignEvent.Amount;
        }
    }
}
