using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDG.Quest
{
    /// <summary>
    /// One Star Quest hero, saved as a <c>.fdghero</c> file: an army-book hero with a class, its gear, and its
    /// progress (XP, level, credits, items, injuries). Heroes keep this between missions and campaigns.
    ///
    /// <para>Stats are stored the way the rest of the engine stores them: <see cref="Quality"/>,
    /// <see cref="Defense"/>, <see cref="Strength"/>, <see cref="Dexterity"/> and <see cref="Willpower"/> are the
    /// roll needed (5 = "5+"); <see cref="Power"/>, <see cref="Tough"/> and <see cref="Caster"/> are plain
    /// counts.</para>
    /// </summary>
    public sealed class QuestHero
    {
        public const string FileExtension = ".fdghero";
        public const int CurrentFileVersion = 1;

        public int FileVersion { get; set; } = CurrentFileVersion;

        /// <summary>The player's name for the hero ("Sergeant Vex").</summary>
        public string Name { get; set; } = "";

        /// <summary>"GF Star Quest" or "AoF Quest".</summary>
        public string GameType { get; set; } = "GF Star Quest";

        public string Faction { get; set; } = "";
        public string FactionUid { get; set; } = "";
        public string BaseHeroId { get; set; } = "";
        /// <summary>The army-book hero this one is built on ("Grunt Veteran").</summary>
        public string BaseHeroName { get; set; } = "";
        /// <summary>The army-book points of the base hero (before upgrades).</summary>
        public int BaseCost { get; set; }

        /// <summary>Class name ("Berserker"), or "" before one is picked.</summary>
        public string Class { get; set; } = "";
        public string ClassId { get; set; } = "";

        public int Level { get; set; } = 1;
        public int Xp { get; set; }
        public int Credits { get; set; } = 30;

        public int Quality { get; set; } = 5;
        public int Defense { get; set; } = 6;
        public int Tough { get; set; } = 1;
        public int Power { get; set; } = 3;
        public int Strength { get; set; } = 6;
        public int Dexterity { get; set; } = 6;
        public int Willpower { get; set; } = 6;
        /// <summary>Caster(X): spell tokens per round. 0 = not a caster.</summary>
        public int Caster { get; set; }

        /// <summary>How many skills the hero may know (from the class, plus level-ups).</summary>
        public int SkillSlots { get; set; }

        /// <summary>Special rules, e.g. "Strider", "Tough(9)".</summary>
        public List<string> Rules { get; set; } = new();
        /// <summary>Gear, each a bundle of rules: "Combat Shield (Shielded)".</summary>
        public List<string> Gear { get; set; } = new();
        public List<QuestWeapon> Weapons { get; set; } = new();
        /// <summary>The upgrade options taken, by label, in the order they were applied.</summary>
        public List<string> Upgrades { get; set; } = new();
        /// <summary>The points the chosen upgrades cost.</summary>
        public int UpgradeCost { get; set; }

        /// <summary>The class feature (always known) and the skills picked, by name.</summary>
        public string ClassFeat { get; set; } = "";
        public List<string> Skills { get; set; } = new();
        /// <summary>Shop items carried (consumables), by name. Duplicates allowed.</summary>
        public List<string> Items { get; set; } = new();
        public List<string> Injuries { get; set; } = new();
        public List<string> Conditions { get; set; } = new();
        /// <summary>Faction spells known (casters only).</summary>
        public List<string> Spells { get; set; } = new();

        /// <summary>Missions finished, for the hero list.</summary>
        public int Missions { get; set; }

        // ── Campaign (set by the campaign screen before a mission; empty for one-off games) ──
        /// <summary>The campaign this hero is playing a chapter of, or "".</summary>
        public string CampaignName { get; set; } = "";
        /// <summary>The chapter's difficulty (1-4); 0 = not in a campaign mission.</summary>
        public int CampaignDifficulty { get; set; }
        /// <summary>The campaign mission's planned primary objective (1-6), or 0 to roll it at set-up.</summary>
        public int CampaignPrimary { get; set; }
        /// <summary>The campaign mission's planned secondary objective (1-6), or 0 to roll it when it comes up.</summary>
        public int CampaignSecondary { get; set; }
        /// <summary>Wounds carried into the next mission of the chapter.</summary>
        public int WoundsTaken { get; set; }
        /// <summary>Power carried into the next mission (-1 = full).</summary>
        public int PowerLeft { get; set; } = -1;
        /// <summary>The last mission's result: "success" / "failed" ("" = none yet).</summary>
        public string LastOutcome { get; set; } = "";

        /// <summary>The hero file this hero was loaded from, so a mission can write its progress back.</summary>
        public string? SourceFile { get; set; }

        /// <summary>The hero's points, used to size the enemy waves.</summary>
        [JsonIgnore]
        public int Points => BaseCost + UpgradeCost;

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static QuestHero? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<QuestHero>(json); }
            catch (JsonException) { return null; }
        }

        public QuestHero Clone() => FromJson(ToJson())!;
    }

    /// <summary>A weapon as a hero carries it.</summary>
    public sealed class QuestWeapon
    {
        public string Name { get; set; } = "";
        /// <summary>Range in inches; 0 = melee.</summary>
        public int Range { get; set; }
        public int Attacks { get; set; } = 1;
        public int Count { get; set; } = 1;
        /// <summary>Weapon rules, e.g. "AP(1)", "Rending".</summary>
        public List<string> Rules { get; set; } = new();

        public bool IsMelee => Range <= 0;

        /// <summary>'Rifle (24", A1, AP(1))' - ASCII only.</summary>
        public string Label()
        {
            var parts = new List<string>();
            if (Range > 0) parts.Add($"{Range}\"");
            parts.Add($"A{Attacks}");
            parts.AddRange(Rules);
            string prefix = Count > 1 ? $"{Count}x " : "";
            return $"{prefix}{Name} ({string.Join(", ", parts)})";
        }
    }

    /// <summary>
    /// Star Quest progression: the XP needed per level, what each level-up offers, and when skill / shop tiers
    /// unlock (GFSQ Campaign Builder v2.1.0 levelling table; tiers as Army Forge shows them).
    /// </summary>
    public static class QuestProgression
    {
        public const int MaxLevel = 15;

        /// <summary>
        /// Total XP needed to reach each level (index = level), from the Campaign Builder v2.1.0 table.
        /// <para>
        /// Was the v2.0.1 table until 2026-10-30 (`0,0,2,4,8,12,18,24,32,40,50,60,82,94,108,122`), which is
        /// a different curve end to end, not a tweak: level 2 cost 2 XP instead of 3, and level 15 cost 122
        /// instead of 168. Since a mission pays 2 x difficulty XP for the primary objective, heroes were
        /// levelling roughly a third faster than the book intends, and the gap widened at every tier.
        /// </para>
        /// </summary>
        public static readonly int[] XpForLevel = { 0, 0, 3, 6, 12, 18, 27, 36, 48, 60, 75, 90, 108, 126, 147, 168 };

        /// <summary>
        /// The improvements a hero may pick from on reaching each level (index = level), from the same table.
        /// <para>
        /// Levels 4 and 8 were swapped against the book until 2026-10-30: level 4 offered "+1 Qua" (which the
        /// book puts at 8) and level 8 offered "+2 Pow" (which the book puts at 4). Quality is the strongest
        /// improvement on the list, so this handed it out four levels early.
        /// </para>
        /// </summary>
        public static readonly string[][] LevelOptions =
        {
            Array.Empty<string>(),
            Array.Empty<string>(),
            new[] { "+1 Tou", "+1 Skill" },
            new[] { "+1 Str", "+1 Dex", "+1 Wil" },
            new[] { "+2 Tou", "+2 Pow" },
            new[] { "+2 Pow", "+2 Skill", "+1 Caster" },
            new[] { "+1 Tou", "+1 Pow" },
            new[] { "+1 Str", "+1 Dex", "+1 Wil" },
            new[] { "+2 Tou", "+1 Qua" },
            new[] { "+2 Pow", "+2 Skill", "+1 Caster" },
            new[] { "+1 Tou", "+1 Skill" },
            new[] { "+1 Str", "+1 Dex", "+1 Wil" },
            new[] { "+2 Tou", "+1 Qua" },
            new[] { "+2 Pow", "+2 Skill", "+1 Caster" },
            new[] { "+1 Tou", "+1 Pow" },
            new[] { "+1 Str", "+1 Dex", "+1 Wil" },
        };

        /// <summary>The level a hero with this much XP has reached.</summary>
        public static int LevelForXp(int xp)
        {
            int level = 1;
            for (int l = 2; l <= MaxLevel; l++)
                if (xp >= XpForLevel[l]) level = l;
            return level;
        }

        /// <summary>The highest skill / shop tier unlocked at a level: tier 1 at level 2, tier 2 at 5, tier 3 at 9.</summary>
        public static int TierUnlocked(int level) => level >= 9 ? 3 : level >= 5 ? 2 : level >= 2 ? 1 : 0;

        /// <summary>Applies one level-up choice ("+1 Str", "+2 Tou"...) to the hero. Stats needing a roll get
        /// better by going down, never below 2+.</summary>
        public static void Apply(QuestHero hero, string option)
        {
            string[] parts = option.Split(' ', 2);
            if (parts.Length != 2 || !int.TryParse(parts[0].TrimStart('+'), out int n)) return;
            switch (parts[1])
            {
                case "Tou": hero.Tough += n; SetToughRule(hero); break;
                case "Pow": hero.Power += n; break;
                case "Skill": hero.SkillSlots += n; break;
                case "Caster": hero.Caster += n; break;
                case "Qua": hero.Quality = Math.Max(2, hero.Quality - n); break;
                case "Str": hero.Strength = Math.Max(2, hero.Strength - n); break;
                case "Dex": hero.Dexterity = Math.Max(2, hero.Dexterity - n); break;
                case "Wil": hero.Willpower = Math.Max(2, hero.Willpower - n); break;
            }
        }

        /// <summary>Keeps the "Tough(X)" rule in step with <see cref="QuestHero.Tough"/>.</summary>
        public static void SetToughRule(QuestHero hero)
        {
            hero.Rules.RemoveAll(r => r.StartsWith("Tough(", StringComparison.OrdinalIgnoreCase));
            if (hero.Tough > 1) hero.Rules.Add($"Tough({hero.Tough})");
        }
    }
}
