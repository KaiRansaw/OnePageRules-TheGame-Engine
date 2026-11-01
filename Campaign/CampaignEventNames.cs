namespace FDG.Campaign
{
    /// <summary>
    /// #433 - the 36 campaign random events, named in the vocabulary of the game being played.
    ///
    /// <para>
    /// The four Campaign Rules v3.6.0 books carry the same 36 events: same two-dice codes, same effects,
    /// in the same order. Grimdark Future and Firefight share one set of names; <b>Age of Fantasy and
    /// AoF: Skirmish share a different set</b>, renaming 21 of the 36 and leaving 15 word for word -
    /// Explosive Trap is a <i>Magic Trap</i>, Solar Flare a <i>Melting Curse</i>, a Robot Guide an
    /// <i>Animal Guide</i>, Credits Stash a <i>Gold Stash</i>.
    /// </para>
    ///
    /// <para>
    /// <b>Why this was worth a slice.</b> <see cref="CampaignEvents"/> held Grimdark Future's names only,
    /// with a note saying the fantasy names "are not worth a second table until AoF campaign has a screen
    /// of its own". It has had one since #418, and #432 then started announcing those names in play - so
    /// an Age of Fantasy campaign was being told a Solar Flare had dimmed its knights' armour. Nothing
    /// was broken, which is exactly why it would have sat there: the effect was right every time. Same
    /// shape, and the same fix, as the Quest systems' event names (<see cref="FDG.Quest.QuestEventNames"/>,
    /// #418) and their treasure tables (<c>QuestItemSkin</c>).
    /// </para>
    ///
    /// <para>
    /// The amounts are not here. One event pays differently per system - Skirmish's Gold Stash is 15pts
    /// where the others are 25 - and that number lives in
    /// <see cref="CampaignNumbers.EventCredits"/> beside the rest of the per-system price list, so there
    /// is one owner for it. This file is names only.
    /// </para>
    /// </summary>
    public static class CampaignEventNames
    {
        /// <summary>
        /// Grimdark Future's name -> the fantasy books' name, for the 21 events whose flavour differs.
        /// Typed from <c>AoF - Campaign Rules v3.6.0</c> and cross-checked against
        /// <c>AoFS - Campaign Rules v3.6.0</c>, which agree on every one of them.
        /// <para>
        /// The other 15 are identical in all four books and need no entry: Falling Debris (11), Venomous
        /// Moss (13), Razor Vines (14), Lightning Strike (15), Stuck in the Mud (16), Swarm Attack (21),
        /// Dazing Spores (23), Sticky Foliage (26), Sinkhole (31), Crumbling Earth (33), Terrifying
        /// Quakes (41), Heavy Rain (42), Thick Clouds (43), Shifting Illusions (45) and Prophetic Vision
        /// (52).
        /// </para>
        /// </summary>
        private static readonly Dictionary<int, string> FantasyNames = new()
        {
            [12] = "Magic Trap",            // Explosive Trap
            [22] = "Debilitating Curse",    // Debilitating Static
            [24] = "Hexed Gale",            // Anomalous Gale
            [25] = "Eldritch Shriek",       // Alien Shriek
            [32] = "Melting Curse",         // Solar Flare
            [34] = "Spotter Bird",          // Spotter Drone
            [35] = "Animal Guide",          // Robot Guide
            [36] = "Magical Pool",          // Psychic Pool
            [44] = "Mystic Fog",            // Psychic Fog
            [46] = "Supporting Spirits",    // Support Robots
            [51] = "Haste Berries",         // Haste Stims
            [53] = "Weapon Enchantment",    // Invigorating Fumes
            [54] = "Enchanted Winds",       // Enhancing Force-Field
            [55] = "Guardian Spirit",       // Shield-Projector Drone
            [56] = "Refreshing Grubs",      // Refreshment Kit
            [61] = "Gold Stash",            // Credits Stash
            [62] = "Elixir Pouch",          // Injector Pouch
            [63] = "Bomb Bag",              // Grenade Bag
            [64] = "Treasure Map",          // Treasure Holopad
            [65] = "Potion Box",            // Kit Box
            [66] = "Godly Sunrays",         // Godly Blessing
        };

        /// <summary>
        /// Whether this system reads the fantasy names. Age of Fantasy and AoF: Skirmish are the two with
        /// a campaign book; AoF: Quest is listed because it shares the vocabulary and a stale save must
        /// not read as science fiction, even though its own campaign is the Campaign Builder's and never
        /// reaches this table.
        /// </summary>
        public static bool IsFantasy(EGameSystem system) =>
            system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish or EGameSystem.AoFQuest;

        /// <summary>
        /// The event's name in this system's words, falling through to the canonical Grimdark Future name.
        /// </summary>
        public static string Name(CampaignEvent fired, EGameSystem system) =>
            IsFantasy(system) && FantasyNames.TryGetValue(fired.Code, out string? fantasy)
                ? fantasy
                : fired.Name;

        /// <summary>
        /// The event's wording with this system's numbers in it. Only the two treasury events carry a
        /// number in their text, written as <c>{pts}</c> in the canonical table so that one system's
        /// figure is never baked into a shared string.
        /// </summary>
        public static string Text(CampaignEvent fired, EGameSystem system) =>
            fired.Text.Contains(PointsToken, StringComparison.Ordinal)
                ? fired.Text.Replace(PointsToken, Credits(fired, system).ToString(), StringComparison.Ordinal)
                : fired.Text;

        /// <summary>
        /// What one unit of this event's effect is worth here. Everything in the table is shared except
        /// the two treasury events, whose figure comes from the system's own price list.
        /// </summary>
        public static int Credits(CampaignEvent fired, EGameSystem system) =>
            fired.Kind == ECampaignEventKind.GainPoints
                ? CampaignNumbers.For(system).EventCredits
                : fired.Amount;

        /// <summary>The placeholder the canonical table writes in place of a points figure.</summary>
        public const string PointsToken = "{pts}";

        /// <summary>Every code the fantasy books rename, for a fixture that walks them.</summary>
        public static IReadOnlyCollection<int> RenamedCodes => FantasyNames.Keys;

        /// <summary>The fantasy name for a renamed code, for a fixture that checks it against the book.</summary>
        public static string FantasyName(int code) => FantasyNames[code];
    }
}
