using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Quest
{
    /// <summary>
    /// #418 - the 36 random events, named in the vocabulary of the Quest system being played.
    ///
    /// <para>
    /// The two Campaign Builders' event tables are the same table: same two-dice codes, same effects,
    /// in the same order. Only the flavour differs - Star Quest's <i>Psychic Fog</i> is AoF: Quest's
    /// <i>Mystic Fog</i>, its <i>Robot Guide</i> an <i>Animal Guide</i>, its <i>Haste Stims</i>
    /// <i>Haste Berries</i>. 21 of the 36 are renamed and 15 are word for word identical.
    /// </para>
    /// <para>
    /// The engine carried Star Quest's names only, so an AoF: Quest party was being told a Solar Flare
    /// had dimmed their armour in a world with no sun worth speaking of. Nothing was broken - the effect
    /// was right every time - which is why this sat unnoticed: the same shape as the treasure tables
    /// (<see cref="QuestItemSkin"/>), where the names mattered rather more.
    /// </para>
    /// <para>
    /// The four events that hand over an item or money read their name from <see cref="QuestItemSkin"/>
    /// rather than repeating it here, so those strings have exactly one owner.
    /// </para>
    /// </summary>
    public static class QuestEventNames
    {
        /// <summary>
        /// Star Quest's name -> AoF: Quest's, for 17 of the 21 events whose flavour differs (the other
        /// four are treasure entries, below). Typed from <c>AoFQ - Campaign Builder v2.1.0</c> /
        /// <c>AoFQ - Beginners Guide v2.1.0</c> against <c>GFSQ - Campaign Builder v2.1.0</c>. The
        /// remaining 15 (Lightning Strike, Stuck in the Mud, Falling Debris, Razor Vines, Shifting
        /// Illusion, Swarm Attack, Sticky Foliage, Venomous Moss, Crumbling Earth, Dazing Spores, Heavy
        /// Rain, Thick Clouds, Terrifying Quakes, Sinkhole and Prophetic Vision) are identical in both
        /// books and need no entry.
        /// </summary>
        private static readonly Dictionary<int, string> AoFEventNames = new()
        {
            [13] = "Debilitating Curse",   // Debilitating Static
            [21] = "Magic Trap",           // Explosive Trap
            [22] = "Eldritch Shriek",      // Alien Shriek
            [23] = "Hexed Gale",           // Anomalous Gale
            [32] = "Melting Curse",        // Solar Flare
            [41] = "Magical Pool",         // Psychic Pool
            [42] = "Animal Guide",         // Robot Guide
            [43] = "Treasure Map",         // Treasure Holopad
            [44] = "Spotter Bird",         // Spotter Drone
            [45] = "Refreshing Grubs",     // Refreshment Kit
            [51] = "Mystic Fog",           // Psychic Fog
            [52] = "Haste Berries",        // Haste Stims
            [53] = "Weapon Enchantment",   // Invigorating Fumes
            [54] = "Supporting Spirit",    // Support Robot
            [55] = "Enchanted Winds",      // Enhancing Force-Field
            [56] = "Guardian Spirit",      // Shield-Projector Drone
            [66] = "Godly Sunrays",        // Godly Blessing
            // 61, 63, 64 and 65 are the Elixir Pouch, Bomb Bag, Gold Stash and Potion Box - they are
            // treasure-table entries, and QuestItemSkin owns those names. Deliberately absent here.
        };

        /// <summary>
        /// The event's name for this system. Falls through to the canonical (Star Quest) name, then to
        /// the item skin - so the four treasure-shaped events get their name from the one place that
        /// knows it - and finally to "Event NN" for a code no table defines.
        /// </summary>
        public static string Name(int number, EGameSystem system, string canonical)
        {
            if (system == EGameSystem.AoFQuest && AoFEventNames.TryGetValue(number, out string? skinned))
                return skinned;
            return QuestItemSkin.Name(system, canonical);
        }

        /// <summary>
        /// Fills an event's effect text for this system. The four events that grant something write
        /// <c>{item:Name}</c> or <c>{money:N}</c> rather than naming one system's gear, so the sentence
        /// a player reads matches what lands in the hero's pack.
        /// </summary>
        public static string Fill(string text, EGameSystem system)
        {
            if (string.IsNullOrEmpty(text) || !text.Contains('{')) return text;

            var built = new System.Text.StringBuilder(text.Length + 8);
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0) { built.Append(text, i, text.Length - i); break; }
                int close = text.IndexOf('}', open);
                if (close < 0) { built.Append(text, i, text.Length - i); break; }

                built.Append(text, i, open - i);
                string token = text[(open + 1)..close];
                built.Append(Resolve(token, system));
                i = close + 1;
            }
            return built.ToString();
        }

        private static string Resolve(string token, EGameSystem system)
        {
            if (token.StartsWith("item:", StringComparison.Ordinal))
                return QuestItemSkin.Name(system, token["item:".Length..]);
            if (token.StartsWith("money:", StringComparison.Ordinal)
                && int.TryParse(token["money:".Length..], out int amount))
                return QuestItemSkin.Money(system, amount);
            return token;   // an unknown placeholder reads as itself rather than vanishing
        }

        /// <summary>Every event code this skin renames, for a fixture that walks them.</summary>
        public static IReadOnlyCollection<int> RenamedCodes => AoFEventNames.Keys;

        /// <summary>AoF: Quest's name for a renamed code, for a fixture that checks it against the book.</summary>
        public static string AoFName(int number) => AoFEventNames[number];
    }
}
