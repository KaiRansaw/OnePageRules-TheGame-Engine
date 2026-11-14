using System;
using System.Collections.Generic;
using System.Linq;

namespace FDG
{
    /// <summary>
    /// What the books say about terrain, as data: the common terrain kinds with their rules, each rule's own wording,
    /// the placement guidance (how many pieces, how much cover, how wide the gaps), and the Advanced Rulebooks'
    /// terrain generator. Drives the Map Maker (Map Maker mockup, approved 2026-11-13: every item kept).
    ///
    /// <para><b>Sources, per game</b> (Kai's ruling 2026-11-01: only the current books):</para>
    /// <list type="bullet">
    /// <item>GF, GF: Firefight, AoF, AoF: Skirmish - each game's own Advanced Rulebook v3.6.0, Terrain (p.11-12) and
    /// Advanced Terrain - Terrain Generator. The four books share the piece list and rule wording but NOT the numbers:
    /// GF and AoF advise 15-20 pieces covering 25% and print a 9 + 4D3 generator; Firefight and Skirmish advise 20-25
    /// covering 50% and print 10 + 6D3. Skirmish's gaps are 6" / 3", the others 12" / 6".</item>
    /// <item>AoF: Quest - Beginners Guide v2.1.0, Terrain (same guidance as Skirmish). No generator.</item>
    /// <item>GF: Star Quest - Core Rules v2.1.0 has no placement guidance and no generator, so neither is offered.</item>
    /// </list>
    ///
    /// <para>Text is ASCII (CLAUDE.md): the books' curly quotes and inch marks are folded to plain ones.</para>
    /// </summary>
    public static class TerrainBook
    {
        // ================================================================== the common kinds

        /// <summary>
        /// One kind of terrain piece. <paramref name="Rules"/> are the book's; <paramref name="Width"/> /
        /// <paramref name="Depth"/> are the default size (a judgement call, kept inside the book's advice: large features
        /// "between 4"x4" and 8"x8"", scatter "between 1"x1" and 3"x3""); <paramref name="Round"/> means a circle of
        /// diameter <paramref name="Width"/>. <paramref name="Height"/> is how tall the block is drawn (no rules effect
        /// while the table is flat).
        /// </summary>
        public sealed record PieceKind(string Id, string Name, ETerrainType Rules, float Width, float Depth, bool Round, float Height, string BookLine);

        private const ETerrainType Solid = ETerrainType.Impassible | ETerrainType.Blocking;

        /// <summary>
        /// The Advanced Rulebooks' "guidelines for common terrain types", in the book's order. Lake and River have two
        /// entries each because the book gives each two rule sets (shallow / deep).
        /// </summary>
        public static readonly IReadOnlyList<PieceKind> Kinds = new PieceKind[]
        {
            new("Barricade",     "Barricade",     ETerrainType.Cover, 3, 1, false, 1f, "Barricades - Cover"),
            new("Building",      "Building",      Solid, 6, 6, false, 5f, "Buildings - Impassable + Sight Blocking"),
            new("Field",         "Field",         ETerrainType.Difficult | ETerrainType.Cover, 6, 6, false, 0.6f, "Fields - Difficult + Cover"),
            new("Forest",        "Forest",        ETerrainType.Difficult | ETerrainType.Cover | ETerrainType.Dense, 6, 6, true, 3f, "Forests - Difficult + Cover + Dense"),
            new("Hill",          "Hill",          ETerrainType.Cover | ETerrainType.Steep, 8, 6, true, 1.5f, "Hills - Cover + Steep"),
            new("LakeShallow",   "Lake (shallow)", ETerrainType.Difficult, 6, 6, true, 0.15f, "Lakes - Difficult (if shallow) / Impassable (if deep)"),
            new("LakeDeep",      "Lake (deep)",   ETerrainType.Impassible, 6, 6, true, 0.15f, "Lakes - Difficult (if shallow) / Impassable (if deep)"),
            new("Lava",          "Lava",          ETerrainType.Dangerous, 5, 5, true, 0.15f, "Lava - Dangerous"),
            new("Mountain",      "Mountain",      Solid, 8, 8, true, 6f, "Mountains - Impassable + Sight Blocking"),
            new("RiverShallow",  "River (shallow)", ETerrainType.Difficult, 12, 3, false, 0.15f, "Rivers - Difficult (if shallow) / Unstable (if deep)"),
            new("RiverDeep",     "River (deep)",  ETerrainType.Unstable, 12, 3, false, 0.15f, "Rivers - Difficult (if shallow) / Unstable (if deep)"),
            new("Rubble",        "Rubble",        ETerrainType.Difficult, 4, 4, false, 0.5f, "Rubble - Difficult"),
            new("Ruins",         "Ruins",         ETerrainType.Cover | ETerrainType.Unstable, 6, 6, false, 2.5f, "Ruins - Cover + Unstable"),
            new("Swamp",         "Swamp",         ETerrainType.Difficult, 6, 6, true, 0.15f, "Swamps - Difficult"),
            new("Wall",          "Wall",          ETerrainType.Climbable, 8, 1, false, 2f, "Walls - Climbable"),
        };

        public static PieceKind? Kind(string? id) => id == null ? null : Kinds.FirstOrDefault(k => string.Equals(k.Id, id, StringComparison.OrdinalIgnoreCase));

        // ================================================================== the rules, in the book's words

        /// <summary>The flags a piece can carry, in the order the Advanced Rulebook prints them.</summary>
        public static readonly IReadOnlyList<ETerrainType> RuleFlags = new[]
        {
            ETerrainType.Impassible, ETerrainType.Blocking, ETerrainType.Cover, ETerrainType.Difficult,
            ETerrainType.Dangerous, ETerrainType.Dense, ETerrainType.Unstable, ETerrainType.Steep, ETerrainType.Climbable,
        };

        /// <summary>The name the book gives a flag ("Sight Blocking", "Impassable").</summary>
        public static string RuleName(ETerrainType flag) => flag switch
        {
            ETerrainType.Impassible => "Impassable",
            ETerrainType.Blocking => "Sight Blocking",
            _ => flag.ToString(),
        };

        /// <summary>Flags with no effect until the table has heights (Godot port step 6).</summary>
        public static bool NeedsHeights(ETerrainType flag) => flag is ETerrainType.Steep or ETerrainType.Climbable;

        /// <summary>
        /// The rule's text, word for word from the Advanced Rulebook v3.6.0, Terrain p.12 (identical in the GF, GFF,
        /// AoF and AoFS books). Kai, 2026-11-02: rule text shown to the player is the book's own, never a summary.
        /// </summary>
        public static string RuleText(ETerrainType flag) => flag switch
        {
            ETerrainType.Impassible =>
                "Any surface that would stop models from moving through it, as well as gaps over 1\" wide, count as Impassable terrain.\n"
                + "Units may not move through Impassable terrain, unless they have any rules that allow them to ignore it. Note that units may move "
                + "across gaps up to 1\" wide as if they were flat ground, but may not end their move partially overhanging gaps or terrain.",
            ETerrainType.Blocking =>
                "Any piece of terrain that models can't see or shoot through counts as Sight Blocking terrain.\n"
                + "Units may not draw line of sight through Sight Blocking terrain, unless they have any rules that allow them to ignore it.",
            ETerrainType.Cover =>
                "Terrain features that models can hide in or behind, or that could stop projectiles, count as Cover terrain.\n"
                + "If the majority of models in a unit are fully inside a piece of Cover terrain or behind a sight blocker (for multi-model units), "
                + "or that are mostly inside Cover terrain or behind sight blockers (for single-model units), they get +1 to Defense rolls when "
                + "blocking hits from shooting attacks.",
            ETerrainType.Difficult =>
                "Terrain features that hinder a model's movement, or force them to slow down, count as Difficult terrain.\n"
                + "If any model in a unit moves in or through Difficult terrain at any point of its move, then all models in the unit may not "
                + "move more than 6\" for that movement.",
            ETerrainType.Dangerous =>
                "Terrain features that could harm models, or outright kill them, count as Dangerous terrain.\n"
                + "If any model in a unit moves into or through Dangerous terrain, or activates in it, then the entire unit must take a Dangerous "
                + "terrain test.\n"
                + "To take a Dangerous terrain test, roll as many dice as the number of wounds it would take to fully destroy the unit, and for "
                + "each result is 1 the unit takes one wound.",
            ETerrainType.Dense =>
                "Terrain features that are densely pack and that would be Difficult to see through count as dense terrain.\n"
                + "Units may draw line of sight into and out of dense terrain, but not through it to targets on the other side.",
            ETerrainType.Unstable =>
                "Terrain features that could be harmful if not moved through carefully count as unstable terrain.\n"
                + "If any model in a unit moves over 6\" when moving into or through unstable terrain, then it counts as Dangerous terrain.",
            ETerrainType.Steep =>
                "Terrain features that require extra effort to get on top of, but that are easy to get down from, count as steep terrain.\n"
                + "Units moving onto steep terrain count it as being in Difficult terrain, but units moving off it count it as open terrain.",
            ETerrainType.Climbable =>
                "Terrain features that are over 3\" tall count as Impassable terrain, but any terrain piece that is up to 3\" tall may be climbed "
                + "as part of a unit's move (units may not end their move mid-climb). If not all models in a unit can fit at the top, then a "
                + "coherency of 3\" is allowed to models at different elevations.\n"
                + "Note that short terrain up to 1\" tall may be ignored for the purposes of movement (instead of having to climb up 1\" and "
                + "down 1\" to cross it).",
            _ => "",
        };

        /// <summary>
        /// Whether this game plays with a flag at all. Dense / Unstable: <see cref="Stages.QuestTerrainTypes.AppliesTo"/>
        /// (every game but Star Quest, Kai 2026-11-13). Steep / Climbable: none yet (they need heights).
        /// </summary>
        public static bool Plays(ETerrainType flag, EGameSystem system) => flag switch
        {
            ETerrainType.Dense or ETerrainType.Unstable => Stages.QuestTerrainTypes.AppliesTo(system),
            ETerrainType.Steep or ETerrainType.Climbable => false,
            _ => true,
        };

        // ================================================================== placement guidance (book check)

        /// <summary>
        /// One book's terrain placement advice. <paramref name="CoverTablePercent"/>: "take as many pieces of terrain as
        /// you need to fully cover at least X% of the table". <paramref name="MaxGap"/>: "no gaps bigger than X"";
        /// <paramref name="LargeUnitGap"/>: "gaps of at least X" between terrain" for large units.
        /// </summary>
        public sealed record Guidance(int MinPieces, int MaxPieces, int CoverTablePercent, float MaxGap, float LargeUnitGap, string Book);

        /// <summary>Each game's own advice, or null where its book gives none (GF: Star Quest).</summary>
        public static Guidance? GuidanceFor(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => new(15, 20, 25, 12, 6, "GF Advanced Rulebook v3.6.0"),
            EGameSystem.AgeOfFantasy => new(15, 20, 25, 12, 6, "AoF Advanced Rulebook v3.6.0"),
            EGameSystem.GFFirefight => new(20, 25, 50, 12, 6, "GFF Advanced Rulebook v3.6.0"),
            EGameSystem.AoFSkirmish => new(20, 25, 50, 6, 3, "AoFS Advanced Rules v3.6.0"),
            EGameSystem.AoFQuest => new(20, 25, 50, 6, 3, "AoFQ Beginners Guide v2.1.0"),
            _ => null,
        };

        /// <summary>The type-mix advice every one of those books prints (identical wording in all five).</summary>
        public const int BlockPercent = 50, CoverPercent = 33, DifficultPercent = 33;

        // ================================================================== the terrain generator

        public enum ETheme { Generic, Hospitable, Wasteland, Death }

        /// <summary>Only the four Advanced Rulebooks print a generator.</summary>
        public static bool HasGenerator(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy or EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        /// <summary>"Generic Planets" in GF / GFF, "Generic Planes" in AoF / AoFS - each book's own word.</summary>
        public static string ThemeName(ETheme theme, EGameSystem system)
        {
            bool fantasy = system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
            return $"{theme} {(fantasy ? "Planes" : "Planets")}";
        }

        /// <summary>The book's one-line description of a theme (Generic has none of its own).</summary>
        public static string ThemeText(ETheme theme, EGameSystem system)
        {
            bool fantasy = system is EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
            string w = fantasy ? "planes" : "planets";
            return theme switch
            {
                ETheme.Generic => $"The Generic {(fantasy ? "Plane" : "Planet")} setup allows you to generate terrain without having to worry about using thematic pieces.",
                ETheme.Hospitable => $"Many {w} are pleasant and liveable, sustaining wildlife and civilization alike, with relatively intact natural landscapes.",
                ETheme.Wasteland => $"Either naturally barren or devastated by years of industry or war, some {w} are little more than wastes of dirt and ash.",
                _ => fantasy
                    ? "Some of the most dangerous planes in the planarverse, varying from deadly jungles, to burning volcanoes, to icy mountains."
                    : "Some of the most dangerous planets in the universe, varying from deadly jungles, to burning volcanoes, to icy mountains.",
            };
        }

        /// <summary>A generated piece: the book's name for it, its rules, its size and look, and which kind it draws as.</summary>
        public sealed record GeneratedPiece(string Name, ETerrainType Rules, string KindId, float Width, float Depth, bool Round, bool Large = false);

        // Each entry: the book's name, its rules (as printed in the theme's 2D6 table, or the common-kind list for
        // Generic, whose table prints names only), and the kind it is drawn as. "Large" pieces get the book's
        // large-feature size; scatter its scatter size.
        private sealed record Entry(string Name, ETerrainType Rules, string Kind, bool Large);

        private static Entry E(string name, ETerrainType rules, string kind, bool large = true) => new(name, rules, kind, large);

        private const ETerrainType DC = ETerrainType.Difficult | ETerrainType.Cover;

        // Generic Planets / Planes - the table names pieces only; the rules come from the common-kind list on p.11.
        // "Sight Blocker" is drawn and played like the book's Buildings / Mountains (Impassable + Sight Blocking); a
        // "Lake" with no depth given is the shallow one. Both are judgement calls: claude/house-rules.md.
        private static readonly Entry GHill = E("Large Hill", ETerrainType.Cover | ETerrainType.Steep, "Hill");
        private static readonly Entry GRuins = E("Large Ruins", ETerrainType.Cover | ETerrainType.Unstable, "Ruins");
        private static readonly Entry GField = E("Large Field", DC, "Field");
        private static readonly Entry GBarricade = E("Barricade", ETerrainType.Cover, "Barricade", large: false);
        private static readonly Entry GForest = E("Large Forest", DC | ETerrainType.Dense, "Forest");
        private static readonly Entry GBlocker = E("Large Sight Blocker", Solid, "Building");
        private static readonly Entry GLake = E("Large Lake", ETerrainType.Difficult, "LakeShallow");

        // Hospitable
        private static readonly Entry HDeep = E("Deep River or Lake", ETerrainType.Impassible, "LakeDeep");
        private static readonly Entry HStream = E("Shallow River or Stream", ETerrainType.Difficult, "RiverShallow");
        private static readonly Entry HCrops = E("Tall Crops Field", DC, "Field");
        private static readonly Entry HRuins = E("Ruins or Debris", ETerrainType.Cover | ETerrainType.Unstable, "Ruins");
        private static readonly Entry HForest = E("Forest or Plantation", DC | ETerrainType.Dense, "Forest");
        private static readonly Entry HHill = E("Hill", ETerrainType.Cover | ETerrainType.Steep, "Hill");
        private static readonly Entry HWall = E("Wall or Fence", ETerrainType.Cover | ETerrainType.Difficult, "Wall", large: false);
        private static readonly Entry HBuilding = E("One Large Building", Solid, "Building");
        private static readonly Entry HSmall = E("Group of Small Buildings", Solid, "Building");
        private static readonly Entry HSteep = E("Steep Hill", ETerrainType.Cover | ETerrainType.Steep | ETerrainType.Unstable, "Hill");

        // Wasteland
        private static readonly Entry WToxicRiver = E("Toxic River", ETerrainType.Difficult | ETerrainType.Dangerous, "RiverShallow");
        private static readonly Entry WSludge = E("Sludge Stream or Canal", ETerrainType.Dangerous, "RiverShallow");
        private static readonly Entry WSludgeField = E("Sludge Field", ETerrainType.Difficult, "Swamp");
        private static readonly Entry WRuins = E("Ruins or Debris", ETerrainType.Cover | ETerrainType.Unstable, "Ruins");
        private static readonly Entry WRock = E("Large Rock or Two to Three Small Rocks", Solid, "Mountain");
        private static readonly Entry WDunes = E("Group of Small Dunes", ETerrainType.Cover | ETerrainType.Steep, "Hill");
        private static readonly Entry WCrater = E("Crater", ETerrainType.Cover, "Rubble");
        private static readonly Entry WSmog = E("Toxic Smog", ETerrainType.Cover | ETerrainType.Dangerous | ETerrainType.Dense, "Forest");
        private static readonly Entry WBuilding = E("Large Building", Solid, "Building");
        private static readonly Entry WMines = E("Mine Field", ETerrainType.Dangerous, "Lava");
        private static readonly Entry WSmall = E("Group of Small Buildings", Solid, "Building");

        // Death
        private static readonly Entry DMarsh = E("Death Marsh", ETerrainType.Difficult | ETerrainType.Dangerous, "Swamp");
        private static readonly Entry DRock = E("Crumbly Rock", ETerrainType.Difficult, "Rubble");
        private static readonly Entry DOvergrown = E("Overgrown Ruins", DC, "Ruins");
        private static readonly Entry DDeadly = E("Deadly Vegetation", DC | ETerrainType.Dangerous, "Forest");
        private static readonly Entry DDense = E("Dense Vegetation", DC | ETerrainType.Dense, "Forest");
        private static readonly Entry DHill = E("Hill", ETerrainType.Cover | ETerrainType.Steep, "Hill");
        private static readonly Entry DSmall = E("Group of Small Buildings", Solid, "Building");
        private static readonly Entry DSteep = E("Steep Hill", ETerrainType.Cover | ETerrainType.Steep | ETerrainType.Unstable, "Hill");
        private static readonly Entry DLair = E("Beast Lair", ETerrainType.Dangerous, "Ruins");
        private static readonly Entry DQuicksand = E("Quicksand", ETerrainType.Unstable, "Swamp");

        /// <summary>
        /// The pieces a theme starts with: three groups, each "A" or "A / B" (one of the two at random). GF and AoF take
        /// 2 / 2 / 5 (nine pieces), Firefight and Skirmish 3 / 3 / 4 (ten).
        /// </summary>
        private static (Entry[] Options, int Count)[] Fixed(ETheme theme, bool skirmish)
        {
            int a = skirmish ? 3 : 2, b = skirmish ? 3 : 2, c = skirmish ? 4 : 5;
            return theme switch
            {
                ETheme.Generic => new[] { (new[] { GBarricade }, a), (new[] { GForest, GField }, b), (new[] { GBlocker }, c) },
                ETheme.Hospitable => new[] { (new[] { HWall }, a), (new[] { HHill, HForest with { Name = "Plantation" } }, b), (new[] { HForest with { Name = "Forest" }, HBuilding with { Name = "Large Building" } }, c) },
                ETheme.Wasteland => new[] { (new[] { WDunes }, a), (new[] { WRock with { Name = "Large Rock" }, WCrater }, b), (new[] { WBuilding, WSmog }, c) },
                _ => new[] { (new[] { DOvergrown }, a), (new[] { DDeadly, DHill }, b), (new[] { DDense, DSteep }, c) },
            };
        }

        /// <summary>The 2D6 table, indexed 2..12. An entry with several options ("Hill, Forest, or Field") picks one at random.</summary>
        private static Entry[][] Table(ETheme theme) => theme switch
        {
            ETheme.Generic => new[]
            {
                new[] { GHill }, new[] { GHill }, new[] { GRuins }, new[] { GField }, new[] { GBarricade }, new[] { GBarricade },
                new[] { GBarricade }, new[] { GForest }, new[] { GBlocker }, new[] { GLake }, new[] { GLake },
            },
            ETheme.Hospitable => new[]
            {
                new[] { HDeep }, new[] { HStream }, new[] { HCrops }, new[] { HRuins }, new[] { HForest }, new[] { HHill, HForest with { Name = "Forest" }, HCrops with { Name = "Field" } },
                new[] { HHill }, new[] { HWall }, new[] { HBuilding }, new[] { HSmall }, new[] { HSteep },
            },
            ETheme.Wasteland => new[]
            {
                new[] { WToxicRiver }, new[] { WSludge }, new[] { WSludgeField }, new[] { WRuins }, new[] { WRock }, new[] { WDunes },
                new[] { WCrater }, new[] { WSmog }, new[] { WBuilding }, new[] { WMines }, new[] { WSmall },
            },
            _ => new[]
            {
                new[] { DMarsh }, new[] { DRock }, new[] { DOvergrown }, new[] { DDeadly }, new[] { DDense }, new[] { DDense, DHill },
                new[] { DHill }, new[] { DSmall }, new[] { DSteep }, new[] { DLair }, new[] { DQuicksand },
            },
        };

        /// <summary>
        /// How many extra pieces: "take 4D3 more" in GF and AoF, "6D3" in Firefight and Skirmish - except Firefight's
        /// Death Planets, which the GFF Advanced Rulebook v3.6.0 prints as "4D3" (its other three themes say 6D3, and so
        /// does Skirmish's Death Plane). Implemented as printed; flagged in claude/house-rules.md as a book inconsistency.
        /// </summary>
        public static int ExtraDice(EGameSystem system, ETheme theme) =>
            system == EGameSystem.GFFirefight && theme == ETheme.Death ? 4
            : system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish ? 6
            : 4;

        public static int FixedCount(EGameSystem system) => system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish ? 10 : 9;

        /// <summary>
        /// Rolls a theme's terrain exactly as the book says. <paramref name="d6"/> returns 1..6. Sizes are the book's
        /// size advice (large features 4"-8", scatter 1"-3"), rolled to whole inches; long pieces (rivers, walls) are
        /// sized to read as rivers and walls. The book's last step - "players alternate in picking D3 pieces of terrain
        /// each, which count as Dangerous Terrain" (Generic) - is a per-game choice, so it is not part of the map.
        /// </summary>
        public static List<GeneratedPiece> Generate(EGameSystem system, ETheme theme, Func<int> d6)
        {
            if (!HasGenerator(system)) return new List<GeneratedPiece>();
            bool skirmish = system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;
            var picks = new List<Entry>();
            foreach (var (options, count) in Fixed(theme, skirmish))
                for (int i = 0; i < count; i++) picks.Add(options.Length == 1 ? options[0] : options[(d6() - 1) % options.Length]);

            int extra = 0;
            for (int i = 0; i < ExtraDice(system, theme); i++) extra += (d6() + 1) / 2; // D3
            Entry[][] table = Table(theme);
            for (int i = 0; i < extra; i++)
            {
                int roll = d6() + d6();
                Entry[] options = table[roll - 2];
                picks.Add(options.Length == 1 ? options[0] : options[(d6() - 1) % options.Length]);
            }

            return picks.Select(p => Sized(p, d6)).ToList();
        }

        private static GeneratedPiece Sized(Entry e, Func<int> d6)
        {
            PieceKind kind = Kind(e.Kind)!;
            if (e.Kind is "RiverShallow" or "RiverDeep") return new(e.Name, e.Rules, e.Kind, 10 + d6(), 3, false, true);
            if (e.Kind == "Wall") return new(e.Name, e.Rules, e.Kind, 5 + d6() / 2 * 1f, 1, false);
            if (!e.Large) return new(e.Name, e.Rules, e.Kind, Math.Min(3, 1 + (d6() + 1) / 2), 1, false); // scatter: 2"-3" x 1"
            float w = 3 + (d6() + 1) / 2 + (d6() + 1) / 2 - 1;                                    // 4"-8"
            float d = kind.Round ? w : 3 + (d6() + 1) / 2 + (d6() + 1) / 2 - 1;
            return new(e.Name, e.Rules, e.Kind, w, d, kind.Round, true);
        }

        /// <summary>
        /// Grows the large pieces (never scatter) evenly until they cover the game's "at least X% of the table", as far
        /// as the book's largest size allows ("can be as large as 12"x12""). The 4"-8" sizes alone fall well short of
        /// 25% on a 6'x4' table, so without this every generated table would fail the book's own check.
        /// </summary>
        public static List<GeneratedPiece> GrowToCover(IReadOnlyList<GeneratedPiece> pieces, float tableW, float tableH, EGameSystem system)
        {
            if (GuidanceFor(system) is not { } g) return pieces.ToList();
            float Area(GeneratedPiece p) => p.Round ? MathF.PI * p.Width * p.Width / 4f : p.Width * p.Depth;
            float target = tableW * tableH * g.CoverTablePercent / 100f;
            float scatter = pieces.Where(p => !p.Large).Sum(Area), large = pieces.Where(p => p.Large).Sum(Area);
            if (large <= 0 || scatter + large >= target) return pieces.ToList();
            float k = MathF.Sqrt((target - scatter) / large);
            return pieces.Select(p =>
            {
                if (!p.Large) return p;
                bool river = p.KindId is "RiverShallow" or "RiverDeep";
                float w = MathF.Min(river ? 24 : 12, MathF.Round(p.Width * k)), d = river ? p.Depth : MathF.Min(12, MathF.Round(p.Depth * k));
                return p with { Width = w, Depth = p.Round ? w : d };
            }).ToList();
        }

        // ================================================================== spreading pieces out

        /// <summary>A generated piece put on the table: its centre (table inches) and whether it is turned 90 degrees.</summary>
        public sealed record PlacedPiece(GeneratedPiece Piece, float X, float Z, bool Turned)
        {
            public float W => Turned ? Piece.Depth : Piece.Width;
            public float D => Turned ? Piece.Width : Piece.Depth;
        }

        /// <summary>
        /// Spreads pieces over a table. The books leave this open ("There are no specific rules on how you should place
        /// terrain"), so this is a judgement call recorded in claude/house-rules.md: biggest pieces first, each at a random
        /// spot that keeps it 6" from the others and the table edges (the spacing the Advanced Rulebooks' own placement
        /// styles use: "over 6" away from each other and the table edge"); when the table is too full for that, the
        /// spacing drops to 3" and then 1" (the book's widest gap a model can step across) before a piece is left out,
        /// as the book does with a piece that cannot be placed ("If it's impossible to place the terrain piece, simply
        /// remove it"). <paramref name="rand01"/> returns 0..1.
        /// </summary>
        public static List<PlacedPiece> Spread(IEnumerable<GeneratedPiece> pieces, float tableW, float tableH, Func<double> rand01, out int left)
        {
            var placed = new List<PlacedPiece>();
            left = 0;
            foreach (GeneratedPiece p in pieces.OrderByDescending(p => p.Width * p.Depth))
            {
                PlacedPiece? spot = null;
                foreach (float gap in new[] { 6f, 3f, 1f })
                {
                    for (int attempt = 0; attempt < 400 && spot == null; attempt++)
                    {
                        bool turned = !p.Round && rand01() < 0.5;
                        float w = turned ? p.Depth : p.Width, d = turned ? p.Width : p.Depth;
                        float minX = gap + w / 2, maxX = tableW - gap - w / 2, minZ = gap + d / 2, maxZ = tableH - gap - d / 2;
                        if (maxX < minX || maxZ < minZ) break;
                        float x = MathF.Round((float)(minX + rand01() * (maxX - minX))), z = MathF.Round((float)(minZ + rand01() * (maxZ - minZ)));
                        var candidate = new PlacedPiece(p, x, z, turned);
                        if (placed.All(o => Gap(o, candidate) >= gap)) spot = candidate;
                    }
                    if (spot != null) break;
                }
                if (spot != null) placed.Add(spot); else left++;
            }
            return placed;
        }

        /// <summary>The edge-to-edge gap between two placed pieces' footprints (box to box; 0 when they overlap).</summary>
        public static float Gap(PlacedPiece a, PlacedPiece b)
        {
            float dx = MathF.Abs(a.X - b.X) - (a.W + b.W) / 2, dz = MathF.Abs(a.Z - b.Z) - (a.D + b.D) / 2;
            if (dx <= 0 && dz <= 0) return 0;
            return MathF.Sqrt(MathF.Max(0, dx) * MathF.Max(0, dx) + MathF.Max(0, dz) * MathF.Max(0, dz));
        }
    }
}
