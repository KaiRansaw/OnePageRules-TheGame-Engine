using FDG.SaveLoad;

namespace FDG.Network.Connection.Lobby
{
    /// <summary>What this machine knows about the full list a lobby seat is bringing.</summary>
    public enum ESeatArmyStatus
    {
        /// <summary>The seat has not picked a list.</summary>
        NoArmy,

        /// <summary>The roster says the seat has a list, but the full list has not reached this machine
        /// yet (a joined player, moments after a pick or a join).</summary>
        Loading,

        /// <summary><see cref="SeatArmyView.Army"/> holds the list.</summary>
        Ready,

        /// <summary>The list arrived but could not be read (a build mismatch the join check missed).</summary>
        Unreadable,
    }

    /// <summary>A seat's full army list as this machine holds it - see
    /// <see cref="ILobbyViewModel.GetSeatArmy"/>. <see cref="Army"/> is set only when
    /// <see cref="Status"/> is <see cref="ESeatArmyStatus.Ready"/>.</summary>
    public readonly record struct SeatArmyView(ESeatArmyStatus Status, ArmyListFile? Army)
    {
        public static SeatArmyView None => new(ESeatArmyStatus.NoArmy, null);
    }
}
