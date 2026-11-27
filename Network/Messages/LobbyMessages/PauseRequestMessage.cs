namespace FDG.Network.Messages
{
    /// <summary>
    /// Client -> host: a player asks to pause the running multiplayer game (homebrew pause &amp; resume, Kai
    /// 2026-11-27). The host saves the game and ends it for everyone; Load Game resumes it later and each friend
    /// joins back into their own seat.
    /// </summary>
    public class PauseRequestMessage
    {
        public string PlayerName { get; set; } = "";

        public PauseRequestMessage() { }

        public PauseRequestMessage(string playerName) => PlayerName = playerName;
    }
}
