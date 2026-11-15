using System.Collections.Generic;
using System.Linq;

namespace FDG.Network.Messages
{
    /// <summary>
    /// The unit pictures and model picks one player brings to a game (Kai, 2026-11-14: "does the game on multiplayer matches use the
    /// players' selected unit portraits for the other players? can we make that work?"). Pictures are made and framed
    /// on each player's own computer, so they travel with the army: small PNG copies, keyed by unit and look, plus how
    /// each unit's picture is framed. 3D models are too big to send (tens of MB each), so for those only the choice
    /// travels: which model file stands for each unit and look, as a path inside the UnitModels folder - another player
    /// who has the same file sees it (Kai, 2026-11-14: "could you also do the same for the unit models in game?"). The
    /// engine only carries them - which pictures and how they are drawn is the
    /// front end's business.
    /// </summary>
    public sealed record SeatPictures
    {
        /// <summary>Most a player may send, so a broken or hostile client can't flood the lobby.</summary>
        public const int MaxPictures = 300;
        public const int MaxPictureChars = 400_000;

        /// <summary>"unit|look" (see <see cref="Key"/>) to a base64 PNG.</summary>
        public Dictionary<string, string> Pictures { get; init; } = new();

        /// <summary>The <see cref="Pictures"/> keys that were photographed from a 3D model (drawn over a backdrop).</summary>
        public List<string> ModelMade { get; init; } = new();

        /// <summary>"unit|look" to the model file standing for it, relative to the UnitModels folder ("/" separated).</summary>
        public Dictionary<string, string> Models { get; init; } = new();

        /// <summary>Unit name to its framing: centre across, centre down, zoom.</summary>
        public Dictionary<string, float[]> Framing { get; init; } = new();

        public static string Key(string unit, string? look) => unit + "|" + (look ?? "");

        /// <summary>Drops anything over the limits (too many, too big, malformed framing).</summary>
        public SeatPictures Trimmed()
        {
            var pics = new Dictionary<string, string>();
            foreach (var kv in Pictures)
            {
                if (pics.Count >= MaxPictures) break;
                if (kv.Key.Length > 200 || kv.Value == null || kv.Value.Length > MaxPictureChars) continue;
                pics[kv.Key] = kv.Value;
            }
            var framing = new Dictionary<string, float[]>();
            foreach (var kv in Framing)
                if (framing.Count < MaxPictures && kv.Key.Length <= 200 && kv.Value is { Length: 3 }) framing[kv.Key] = kv.Value;
            var made = ModelMade.Where(pics.ContainsKey).Distinct().ToList();
            var models = new Dictionary<string, string>();
            foreach (var kv in Models)
                if (models.Count < MaxPictures && kv.Key.Length <= 200 && kv.Value is { Length: > 0 and <= 400 }) models[kv.Key] = kv.Value;
            return new SeatPictures { Pictures = pics, Framing = framing, ModelMade = made, Models = models };
        }
    }

    /// <summary>Client to host: the pictures this player brings (replacing any sent before).</summary>
    public record SeatPicturesUpdateMessage(PlayerID PlayerID, SeatPictures Pictures);

    /// <summary>
    /// Host to every player: one seat's pictures, when they change and to a player who joins later. <see cref="Sequence"/>
    /// rises with every one sent, as <see cref="LobbySeatArmyMessage"/>'s does, so a late arrival can't overwrite a newer set.
    /// </summary>
    public record LobbySeatPicturesMessage(PlayerID PlayerID, long Sequence, SeatPictures Pictures);
}
