using FDG.SaveLoad;

namespace FDG.Network.Messages
{
    /// <summary>
    /// Host to client: the full army list one lobby seat is bringing, so a joined player's lobby can show
    /// every seat's list (portrait cards, hovers, the unit detail) the same way the host's does. The
    /// roster broadcast (<see cref="LobbyPlayerListUpdate"/>) still carries only the per-seat summary;
    /// this rides beside it, one army per message, and only when a seat's list actually changes - plus a
    /// full set, sent to just that connection, when a player joins after the lists were picked.
    ///
    /// <para>The army is the same System.Text.Json string <see cref="ArmyListUpdateMessage"/> carries (and
    /// the on-disk <c>.fdgarmy</c> holds) - one format, not two. Null means the seat has no list.</para>
    ///
    /// <para><see cref="Sequence"/> rises with every army the host sends. The host's per-connection writes
    /// are not guaranteed to leave in the order they were queued, so a client keeps the highest sequence
    /// it has seen for each seat and drops anything older - a quick re-pick can never be overwritten by
    /// the list it replaced.</para>
    /// </summary>
    public record LobbySeatArmyMessage(PlayerID PlayerID, long Sequence, string? ArmyListJson)
    {
        public static LobbySeatArmyMessage FromArmy(PlayerID playerID, long sequence, ArmyListFile? armyListFile) =>
            new LobbySeatArmyMessage(playerID, sequence,
                armyListFile == null ? null : ArmyListUpdateMessage.EncodeArmy(armyListFile));

        /// <summary>The carried list, or null when the seat has none. Throws on a payload that can't be
        /// read - the receiver records that rather than showing a half-read army.</summary>
        public ArmyListFile? DecodeArmy() =>
            ArmyListJson == null ? null : ArmyListUpdateMessage.DecodeArmy(PlayerID, ArmyListJson);
    }
}
