namespace FDG.Quest
{
    /// <summary>One shop item (a consumable): its shop tier (0 = always for sale; 1/2/3 unlock at levels 2/5/9),
    /// its cost in credits, and its rules text. From Army Forge's GF: Star Quest rules.</summary>
    public sealed record QuestItemInfo(string Name, int Tier, int Cost, string Description)
    {
        /// <summary>The shop buys items back for their full cost.</summary>
        public int SellValue => Cost;
    }

    public static class QuestItems
    {
        /// <summary>
        /// Items this engine can't play, and why - the counterpart of
        /// <c>QuestAdvancedSkills.NotPossible</c>, shown in place of the generic "isn't in the game yet"
        /// line when a hero has one in its pack.
        /// <para>
        /// #414. These two are buyable, carriable and listed in the shop, and using them does nothing.
        /// Skills have had a ledger like this since the skill work; items had none, so a player could
        /// spend 25 credits on a Pheromone Grenade and never be told it was inert. Both depend on
        /// mechanics that genuinely are not in the engine, so the honest answer is to say so at the point
        /// of use rather than to fake an effect.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyDictionary<string, string> NotPossible = new Dictionary<string, string>
        {
            // Same reason as the Leap skill: nothing in this engine ever rolls to jump.
            ["Agility Injector"] = "There are no jumping rolls in this game, so this has no effect.",
            // The AI's hero-targeting reads the hero list, not a per-unit "treat as hero" flag. The
            // Phantasm Grenade works because it places a MARKER the AI already understands; marking a
            // living enemy unit as a hero has no equivalent.
            ["Pheromone Grenade"] = "Enemies can't be made to look like heroes to each other in this game.",
        };

        public static readonly IReadOnlyList<QuestItemInfo> All = new List<QuestItemInfo>
        {
            new("Courage Injector", 0, 5, "Use with skill action, then remove. The hero automatically passes all morale tests until the end of the round."),
            new("Curing Kit", 0, 5, "Use with skill action, then remove. The hero loses all status conditions. The effect may be applied to one other hero within 3\" instead."),
            new("Frag Grenade", 0, 10, "Use with skill action, then remove. Pick one enemy unit within 9\" and roll one die. On a 4+ it takes 1 hit with Blast(3) and AP(1)."),
            new("Med Kit", 0, 15, "Use with skill action, then remove. The hero restores 3 wounds. The effect may be applied to one other hero within 3\" instead."),
            new("Power Injector", 0, 15, "Use with skill action, then remove. The hero gets +2 to its next stat roll."),
            new("Concussive Grenade", 1, 5, "Use with skill action, then remove. Pick one enemy unit within 6\", which is immediately pushed 6\" directly away."),
            new("Flash Grenade", 1, 10, "Use with skill action, then remove. Pick one enemy unit within 9\", which gets -1 to hit until the end of the round."),
            new("Grease Grenade", 1, 10, "Use with skill action, then remove. Place a grease marker within 12\". The area within 3\" of it counts as Difficult Terrain. Remove the marker at the end of 3 rounds."),
            new("Nanoplate Injector", 1, 10, "Use with skill action, then remove. The hero gets +1 to Def rolls until the end of the round."),
            new("Noise Grenade", 1, 5, "Use with skill action, then remove. Place one noise marker within 12\". Enemies treat the nearest marker as the AI goal. Remove the marker at the end of 3 rounds, or when an enemy moves within 1\"."),
            new("Psychic Kit", 1, 15, "Use with skill action, then remove. The hero gets one spell token."),
            new("Resting Kit", 1, 15, "Use with skill action, then remove. The hero restores 3 power. The effect may be applied to one other hero within 3\" instead."),
            new("Speed Injector", 1, 10, "Use with skill action, then remove. The hero gets +2\" on Advance, and +4\" on Rush/Charge until the end of the round."),
            new("Adrenaline Injector", 2, 15, "Use with skill action, then remove. The hero doesn't get fatigued until the end of the round."),
            new("Phantasm Grenade", 2, 15, "Use with skill action, then remove. Place one phantasm marker within 6\". Enemies treat the marker as a hero. Remove the marker at the end of 3 rounds, or when an enemy attacks or moves within 1\" of it."),
            new("Poison Grenade", 2, 10, "Use with skill action, then remove. Place a poison marker on an enemy unit within 9\". When it activates, roll one die. On a 4+ it takes 1 wound, else the marker is removed."),
            new("Recon Kit", 2, 5, "Use with skill action, then remove. The hero may reveal one search token in line of sight and within 12\", and may modify the roll to see what it contains by +1/-1."),
            new("Reviving Kit", 2, 20, "Use with skill action, then remove. Revive one unconscious hero within 3\", and it restores 6 wounds."),
            new("Smoke Grenade", 2, 10, "Use with skill action, then remove. Place a smoke marker within 12\". The area within 3\" of it counts as Cover Terrain. Remove the marker at the end of 3 rounds."),
            new("Web Grenade", 2, 15, "Use with skill action, then remove. Pick one enemy unit within 9\", which gets -2\" on Advance, and -4\" on Rush/Charge until the end of the round."),
            new("Advanced Med Kit", 3, 30, "Use with skill action, then remove. The hero restores 6 wounds. The effect may be applied to one other hero within 3\" instead."),
            new("Advanced Nanoplate Injector", 3, 20, "Use with skill action, then remove. The hero gets +2 to Def rolls until the end of the round."),
            new("Advanced Psychic Kit", 3, 30, "Use with skill action, then remove. The hero gets two spell tokens."),
            new("Advanced Resting Kit", 3, 30, "Use with skill action, then remove. The hero restores 6 power. The effect may be applied to one other hero within 3\" instead."),
            new("Advanced Speed Injector", 3, 20, "Use with skill action, then remove. The hero gets +4\" on Advance, and +8\" on Rush/Charge until the end of the round."),
            new("Agility Injector", 3, 5, "Use with skill action, then remove. The hero automatically passes all jumping rolls until the end of the round."),
            new("Fire Grenade", 3, 15, "Use with skill action, then remove. Place a fire marker within 12\". The area within 3\" of it counts as Dangerous Terrain. Remove the marker at the end of 3 rounds."),
            new("Frenzy Injector", 3, 10, "Use with skill action, then remove. The next time the hero destroys an enemy unit until the end of the round, it may move by up to 6\"."),
            new("Optical Injector", 3, 10, "Use with skill action, then remove. The hero gets +6\" range until the end of the round."),
            new("Pheromone Grenade", 3, 25, "Use with skill action, then remove. Pick one enemy unit within 9\", which other enemies treat as a hero until the end of the round."),
            new("Silence Grenade", 3, 20, "Use with skill action, then remove. Place a silence marker on an enemy unit within 9\". That unit gets -2 to spell casting rolls until the end of the round. Remove the marker at the end of 3 rounds."),
            new("Stun Grenade", 3, 15, "Use with skill action, then remove. Pick one enemy unit within 6\", which must take a morale test. If failed, it is Shaken."),
            new("Teleport Kit", 3, 10, "Use with skill action, then remove. The hero may be placed anywhere within 6\"."),
        };

        public static QuestItemInfo? Find(string name) => All.FirstOrDefault(i => i.Name == name);

        /// <summary>"10c" -> 10; null when the text isn't a credits amount.</summary>
        public static int? Credits(string contents) =>
            contents.EndsWith("c") && int.TryParse(contents[..^1], out int c) ? c : null;
    }
}
