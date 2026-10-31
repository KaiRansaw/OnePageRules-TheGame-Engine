using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Quest
{
    /// <summary>
    /// #418 - the Quest systems' item and treasure names, which are the same objects under two skins.
    ///
    /// <para>
    /// OPR publishes one Quest ruleset and dresses it twice. Put the two Campaign Builders' Hidden
    /// Treasure tables side by side and they are the same table, roll for roll, sub-roll for sub-roll:
    /// </para>
    /// <list type="table">
    /// <item><term>3</term><description>Injector Pouch (Courage / Frenzy / Power Injector) is Elixir
    /// Pouch (Heroic / Conquest / Power Elixir)</description></item>
    /// <item><term>4</term><description>Grenade Bag (Noise / Grease / Stun Grenade) is Bomb Bag (Noise /
    /// Grease / Stunning Bomb)</description></item>
    /// <item><term>5</term><description>Kit Box (Curing Kit / Teleport Kit / Med Kit) is Potion Box
    /// (Curing / Teleport / Health Potion)</description></item>
    /// <item><term>6</term><description>Credits Stash (5c / 10c / 15c) is Gold Stash (5g / 10g /
    /// 15g)</description></item>
    /// </list>
    /// <para>
    /// So an AoF: Quest hero was finding <b>Med Kits and credits</b> in a fantasy dungeon, because the
    /// engine's treasure table is Star Quest's and was the only one. This maps the names per system.
    /// </para>
    /// <para>
    /// <b>What is read from a book and what is inferred.</b> The AoF: Quest NAMES and their positions in
    /// the table are read from <c>AoFQ - Campaign Builder v2.1.0</c> word for word. Their EFFECTS are
    /// not printed there (neither book prints item effects; Star Quest's came from Army Forge's own
    /// quest data), so each AoF name is wired to the effect of the Star Quest item in the same table
    /// slot. That is an inference, and it is recorded as one - but it is a far better one than handing a
    /// fantasy party a Med Kit, and the alternative was inventing nine effects.
    /// </para>
    /// <para>
    /// Items are stored on a hero sheet BY NAME, so a hero keeps the name it found - "Health Potion" on
    /// an AoF: Quest sheet, "Med Kit" on a Star Quest one - and <see cref="QuestItems.Find"/> resolves
    /// either to the one definition that carries the rules. Saved heroes from before #418 hold the
    /// Star Quest names and keep working unchanged.
    /// </para>
    /// </summary>
    public static class QuestItemSkin
    {
        /// <summary>Canonical (Star Quest) name -> AoF: Quest name, from the 2.1.0 Campaign Builder.</summary>
        private static readonly Dictionary<string, string> AoFNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                // Hidden Treasure roll 3 - the pouch.
                ["Courage Injector"] = "Heroic Elixir",
                ["Frenzy Injector"] = "Conquest Elixir",
                ["Power Injector"] = "Power Elixir",
                // Roll 4 - the bag.
                ["Noise Grenade"] = "Noise Bomb",
                ["Grease Grenade"] = "Grease Bomb",
                ["Stun Grenade"] = "Stunning Bomb",
                // Roll 5 - the box.
                ["Curing Kit"] = "Curing Potion",
                ["Teleport Kit"] = "Teleport Potion",
                ["Med Kit"] = "Health Potion",
            };

        /// <summary>The reverse map, over BOTH the items and the table entries - a treasure-table entry
        /// name has to map back too, or "Elixir Pouch" reads as a name nothing recognises.</summary>
        private static Dictionary<string, string>? _canonicalNames;

        private static Dictionary<string, string> CanonicalNames => _canonicalNames ??= BuildReverse();

        private static Dictionary<string, string> BuildReverse()
        {
            var reverse = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> pair in AoFNames) reverse[pair.Value] = pair.Key;
            foreach (KeyValuePair<string, string> pair in AoFTreasureEntries) reverse[pair.Value] = pair.Key;
            return reverse;
        }

        /// <summary>Canonical treasure-table entry name -> AoF: Quest's name for the same entry.</summary>
        private static readonly Dictionary<string, string> AoFTreasureEntries =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["Injector Pouch"] = "Elixir Pouch",
                ["Grenade Bag"] = "Bomb Bag",
                ["Kit Box"] = "Potion Box",
                ["Credits Stash"] = "Gold Stash",
                // "Hazard" and "Treasure Trove" are the same words in both books.
            };

        /// <summary>True for the system whose names differ from the engine's canonical set.</summary>
        private static bool IsAoF(EGameSystem system) => system == EGameSystem.AoFQuest;

        /// <summary>
        /// What this system calls an item or a treasure-table entry. Anything with no skin - every item
        /// outside the treasure table, and the entries both books spell the same - comes back unchanged.
        /// </summary>
        public static string Name(EGameSystem system, string canonical)
        {
            if (!IsAoF(system) || string.IsNullOrEmpty(canonical)) return canonical;
            if (AoFNames.TryGetValue(canonical, out string? skinned)) return skinned;
            if (AoFTreasureEntries.TryGetValue(canonical, out string? entry)) return entry;
            return canonical;
        }

        /// <summary>
        /// The canonical name behind whatever a hero is holding, so one rules definition serves both
        /// skins. An unknown name is handed back as itself - a custom item, or a name from a future book.
        /// </summary>
        public static string Canonical(string name) =>
            !string.IsNullOrEmpty(name) && CanonicalNames.TryGetValue(name, out string? canonical)
                ? canonical
                : name;

        /// <summary>
        /// This system's currency: Star Quest counts <b>credits</b> ("15c") and AoF: Quest <b>gold</b>
        /// ("15g"). The letter rides on the value because that is how both books write it, and because
        /// a treasure's contents are a plain string the engine parses back out
        /// (<see cref="QuestItems.Credits"/> accepts either).
        /// </summary>
        public static string CurrencySuffix(EGameSystem system) => IsAoF(system) ? "g" : "c";

        /// <summary>The word for the currency, for a sentence rather than a value.</summary>
        public static string CurrencyWord(EGameSystem system) => IsAoF(system) ? "gold" : "credits";

        /// <summary>A money amount written the way this system writes it: 15 -> "15c" or "15g".</summary>
        public static string Money(EGameSystem system, int amount) =>
            $"{amount}{CurrencySuffix(system)}";

        /// <summary>Every name this skin can produce, for a fixture that wants to prove a system never
        /// shows the other's words.</summary>
        public static IEnumerable<string> AllAoFNames => AoFNames.Values.Concat(AoFTreasureEntries.Values);

        /// <summary>Every canonical name that has a skin.</summary>
        public static IEnumerable<string> AllCanonicalNames =>
            AoFNames.Keys.Concat(AoFTreasureEntries.Keys);
    }
}
