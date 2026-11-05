using System;

namespace FDG.ArmyBuilding
{
    // #378 — the OPR game systems this game's data can belong to, as Army Forge's slugs. A book or army
    // with NO GameSystem field is Grimdark Future: GDF was the only system that existed before the field
    // did, so absent-means-GDF keeps every pre-#378 file's meaning unchanged (owner ruling, 2026-08-23).
    public static class GameSystems
    {
        public const string GrimdarkFuture = "grimdark-future";
        public const string AgeOfFantasy = "age-of-fantasy";

        // #406: the skirmish systems are their OWN books, not a scaled version of their parent's.
        // OPR publishes different points, unit sizes, available units and upgrade options for them -
        // Alien Hives Assault Grunts are 105pts for 10 in Grimdark Future and 30pts for 3 in
        // Firefight, and the Hive Lord is not in the Firefight book at all. An army therefore has to
        // record which book it was built from; collapsing them (as the import did when Firefight
        // lists were first accepted) silently offers a Firefight army in a Grimdark Future lobby
        // with the wrong points.
        public const string GFFirefight = "grimdark-future-firefight";
        public const string AoFSkirmish = "age-of-fantasy-skirmish";

        /// <summary>
        /// #412: GF: Star Quest's ARMY BOOKS - the enemy and bot side of a Star Quest table.
        /// <para>
        /// Star Quest is TWO datasets at Army Forge, and conflating them is the trap. This slug is the
        /// one that holds army books (Army Forge id 10, the "-ai" dataset): the opposition a Star Quest
        /// mission or Horde wave is built from. The player's own side is not an army at all - it is a
        /// party of individually built heroes, which live in <c>FdgRaylib/Assets/StarQuest/</c> and come
        /// from a different Army Forge id (9) with different stats for the same unit. Alien Hives' Grunt
        /// Veteran is 55pts Q6 D6 as a playable hero and 100pts Q5 D5 as an enemy, across rosters of 8
        /// units and 41.
        /// </para>
        /// <para>
        /// And it is NOT Grimdark Future at a different scale either, which is what the game served
        /// before these books existed: that same Grunt Veteran is 40pts in Grimdark Future against
        /// 100pts here, so a Star Quest wave priced off GF's book buys two and a half times the enemy
        /// it should.
        /// </para>
        /// </summary>
        public const string GFStarQuest = "grimdark-future-star-quest";

        /// <summary>
        /// #417: AoF: Quest's ARMY BOOKS - the enemy and bot side of an AoF: Quest table. The fantasy
        /// twin of <see cref="GFStarQuest"/>, and the same trap: Army Forge serves AoF: Quest as TWO
        /// ids, and this slug is the army-book one (id 8, the "-ai" dataset).
        /// <para>
        /// Verified live on 2026-10-31 against Beastmen (uid <c>TciwNI3AOMXAM-dr</c>): <b>id 7</b> is the
        /// player's hero roster - 5 units, Ndoli Beast Lord 45pts Q6 D6 - and <b>id 8</b> is the enemy
        /// book - 20 units, Ndoli Warrior 35pts Q4 D4, Hapari Harpy 40pts, Sambari Hound 50pts. Both ids
        /// answer to the same index slug and hold the same faction names, so nothing about the response
        /// tells you which one you asked for. Pull the hero roster from id 8 and every hero gets the
        /// enemy's profile.
        /// </para>
        /// <para>
        /// And, as with Star Quest, it is NOT Age of Fantasy at a different scale - which is what the
        /// game served before these books existed. The hero roster itself is a separate dataset that is
        /// NOT imported yet (see the AoF: Quest hero work item); only the enemy books are here.
        /// </para>
        /// </summary>
        public const string AoFQuest = "age-of-fantasy-quest";

        /// <summary>The slug a null/empty system field means: Grimdark Future.</summary>
        public static string Normalize(string? slug) =>
            string.IsNullOrWhiteSpace(slug) ? GrimdarkFuture : slug.Trim();

        /// <summary>
        /// #451 - the army-book slug for one of the four systems with a Campaign Rules book, so a screen
        /// that knows the system it is building for can show only that system's books.
        /// <para>
        /// Null for the two Quest systems on purpose: each of those is TWO datasets (player heroes and
        /// enemy army books) with different stats for the same unit (CLAUDE.md), so one slug would be the
        /// wrong answer for one of them. A caller that needs a Quest book has to say which dataset.
        /// </para>
        /// </summary>
        public static string? CampaignBookSlug(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => GrimdarkFuture,
            EGameSystem.GFFirefight => GFFirefight,
            EGameSystem.AgeOfFantasy => AgeOfFantasy,
            EGameSystem.AoFSkirmish => AoFSkirmish,
            _ => null,
        };

        /// <summary>Whether two system fields name the same game system, absent meaning GDF on both sides.</summary>
        public static bool SameSystem(string? a, string? b) =>
            string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

        /// <summary>The human-readable name of a game-system slug, for UI labels and gate messages.
        /// An unrecognized slug (an OPR custom-tool army, say) reads as itself rather than as a
        /// guess.</summary>
        public static string DisplayName(string? slug) => Normalize(slug) switch
        {
            GrimdarkFuture => "Grimdark Future",
            AgeOfFantasy => "Age of Fantasy",
            GFFirefight => "GF: Firefight",
            AoFSkirmish => "AoF: Skirmish",
            GFStarQuest => "GF: Star Quest",
            AoFQuest => "AoF: Quest",
            var other => other,
        };

        /// <summary>Whether an army whose system field is <paramref name="slug"/> may be brought to a
        /// lobby set to <paramref name="allowed"/>. <see cref="EAllowedGameSystems.All"/> takes
        /// everything, INCLUDING a slug neither constant here names - that is the whole point of the
        /// value (#402).</summary>
        public static bool IsAllowed(EAllowedGameSystems allowed, string? slug) => allowed switch
        {
            EAllowedGameSystems.GrimdarkFuture => SameSystem(slug, GrimdarkFuture),
            EAllowedGameSystems.AgeOfFantasy => SameSystem(slug, AgeOfFantasy),
            EAllowedGameSystems.GFFirefight => SameSystem(slug, GFFirefight),
            EAllowedGameSystems.AoFSkirmish => SameSystem(slug, AoFSkirmish),
            EAllowedGameSystems.GFStarQuest => SameSystem(slug, GFStarQuest),
            EAllowedGameSystems.AoFQuest => SameSystem(slug, AoFQuest),
            _ => true,
        };

        /// <summary>The lobby label for an <see cref="EAllowedGameSystems"/> setting.</summary>
        public static string DisplayName(EAllowedGameSystems allowed) => allowed switch
        {
            EAllowedGameSystems.GrimdarkFuture => "Grimdark Future",
            EAllowedGameSystems.AgeOfFantasy => "Age of Fantasy",
            EAllowedGameSystems.GFFirefight => "GF: Firefight",
            EAllowedGameSystems.AoFSkirmish => "AoF: Skirmish",
            EAllowedGameSystems.GFStarQuest => "GF: Star Quest",
            EAllowedGameSystems.AoFQuest => "AoF: Quest",
            _ => "All",
        };
    }

    /// <summary>
    /// #402 - which game systems' armies a lobby accepts, as a host-owned <see cref="GameSettings"/>
    /// setting. <see cref="All"/> is 0 so a save or config written before the field existed
    /// deserializes to it, i.e. to the pre-#402 behavior of accepting everything.
    ///
    /// <para>Deliberately NOT a flags enum over the two known slugs: OPR's custom-book tool produces
    /// armies that belong to neither collection, and <see cref="All"/> has to keep taking those. A
    /// third system, if one is ever imported, is a new member here.</para>
    /// </summary>
    public enum EAllowedGameSystems
    {
        /// <summary>Any army, whatever its system field says - including one this build doesn't
        /// recognize. The default, and what every pre-#402 file means.</summary>
        All = 0,

        /// <summary>Grimdark Future only. An army with NO system field counts as GDF
        /// (<see cref="GameSystems.Normalize"/>), so every pre-#378 army list passes.</summary>
        GrimdarkFuture = 1,

        /// <summary>Age of Fantasy only.</summary>
        AgeOfFantasy = 2,

        /// <summary>GF: Firefight only (#406). Its own book - NOT Grimdark Future at a smaller
        /// scale.</summary>
        GFFirefight = 3,

        /// <summary>AoF: Skirmish only (#406). Its own book, as above.</summary>
        AoFSkirmish = 4,

        /// <summary>GF: Star Quest only (#412) - its enemy/bot army books. See
        /// <see cref="GameSystems.GFStarQuest"/> for why these are neither the hero roster nor
        /// Grimdark Future.</summary>
        GFStarQuest = 5,

        /// <summary>AoF: Quest only (#417) - its enemy/bot army books. See
        /// <see cref="GameSystems.AoFQuest"/> for why these are neither the hero roster nor Age of
        /// Fantasy.</summary>
        AoFQuest = 6,
    }
}
