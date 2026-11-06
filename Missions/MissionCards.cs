using FDG.Players;

namespace FDG.Missions
{
    /// <summary>How the Mission Cards are played, or not at all.</summary>
    public enum EMissionCardGame
    {
        /// <summary>No mission cards: the core rules' objectives decide the game.</summary>
        Off = 0,
        /// <summary>"Players compete in completing as many of their own objective cards as they can" - a
        /// personal 36-card deck each, three cards face up.</summary>
        BattleOfWits,
        /// <summary>"Players compete in completing the same set of objective cards" - one shared deck, three
        /// cards on the table for everyone.</summary>
        TotalDomination,
    }

    /// <summary>One condition on a card and the victory points it is worth.</summary>
    public sealed record MissionCondition(string Text, int Vp);

    /// <summary>One of the 36 mission cards.</summary>
    public sealed record MissionCardDef(int Number, string Title, IReadOnlyList<MissionCondition> Conditions,
        string? Note = null);

    /// <summary>
    /// The 36 Mission Cards (Mission Cards v3.6.0, the same deck in GF, GF: Firefight, AoF and AoF: Skirmish -
    /// Kai's screenshots, 2026-11-05, of the Firefight/Skirmish printing). The cards are images in the PDF, so
    /// this is typed from those screenshots.
    ///
    /// <para>
    /// Two cards differ by book, per Kai: <b>65 Giant Slayer</b>'s 1 VP is Tough(12)+ in GF/AoF against
    /// Tough(9)+ in GFF/AoFS, and <b>64 Assassinate</b>'s 2 VP needs three Hero models in GF/AoF against two in
    /// GFF/AoFS. Some cards carry different names in some books; Kai: use the names given.
    /// </para>
    /// <para>
    /// Card text is game text: ASCII only. Card 43's "Heave" and "round1" are the print's typos, corrected.
    /// </para>
    /// </summary>
    public static class MissionCardCatalog
    {
        public static bool LargeGame(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy;

        /// <summary>Whether this game's books have the Mission Cards at all: the four non-Quest games.</summary>
        public static bool Available(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy
                or EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static int GiantSlayerTough(EGameSystem system) => LargeGame(system) ? 12 : 9;
        public const int GiantSlayerBigTough = 18;
        public static int AssassinateMany(EGameSystem system) => LargeGame(system) ? 3 : 2;

        public static IReadOnlyList<MissionCardDef> All(EGameSystem system)
        {
            static MissionCondition C(string text, int vp) => new(text, vp);
            var cards = new List<MissionCardDef>();
            for (int i = 1; i <= 6; i++)
            {
                cards.Add(new MissionCardDef(10 + i, $"Seize {i}", new[] { C($"Control objective {i}.", 1) }));
            }
            for (int i = 1; i <= 6; i++)
            {
                cards.Add(new MissionCardDef(20 + i, $"Seize {i}", new[] { C($"Control objective {i}.", 1) }));
            }
            for (int i = 1; i <= 6; i++)
            {
                cards.Add(new MissionCardDef(30 + i, $"Hold {i}",
                    new[] { C($"Control objective {i} at the beginning and end of the same round.", 2) }));
            }
            int tough = GiantSlayerTough(system), many = AssassinateMany(system);
            cards.AddRange(new[]
            {
                new MissionCardDef(41, "Move Out", new[] { C("Have no friendly units within your deployment zone.", 1) }),
                new MissionCardDef(42, "Infiltrate", new[]
                {
                    C("Have at least one friendly unit within the enemy's deployment zone.", 1),
                    C("Have at least three friendly units within the enemy's deployment zone.", 2),
                }),
                new MissionCardDef(43, "Keep at Bay", new[]
                {
                    C("Have at least three friendly units within your deployment zone whilst no enemy units are within your deployment zone.", 1),
                }, "This card can't be scored at the end of round 1."),
                new MissionCardDef(44, "Changing Priority", new[]
                {
                    C("Roll one D6 when you draw this card. Control the objective with that number.", 1),
                    C("Control the objective with that number after it was controlled by the enemy at the beginning of the round.", 2),
                }),
                new MissionCardDef(45, "Tactical Precision", new[] { C("Control any three objectives.", 2) }),
                new MissionCardDef(46, "Tactical Perfection", new[] { C("Control all objectives.", 5) }),
                new MissionCardDef(51, "Fire at Will", new[]
                {
                    C("Destroy one enemy unit by shooting.", 1), C("Destroy three or more enemy units by shooting.", 2),
                }),
                new MissionCardDef(52, "In the Trenches", new[]
                {
                    C("Destroy one enemy unit in melee.", 1), C("Destroy three or more enemy units in melee.", 2),
                }),
                new MissionCardDef(53, "Lay Them Low", new[]
                {
                    C("Destroy one enemy unit.", 1), C("Destroy three or more enemy units.", 2),
                    C("Destroy six or more enemy units.", 5),
                }),
                new MissionCardDef(54, "Outmaneuver", new[]
                {
                    C("No enemy units within 6\" of the table centre.", 1),
                    C("No enemy units within 12\" of the table centre.", 2),
                }),
                new MissionCardDef(55, "Intimidate", new[]
                {
                    C("One enemy unit fails a morale test.", 1), C("Three or more enemy units fail a morale test.", 2),
                }),
                new MissionCardDef(56, "Attunement", new[]
                {
                    C("Successfully cast one spell.", 1), C("Successfully cast three or more spells.", 2),
                }),
                new MissionCardDef(61, "Decapitate", new[]
                {
                    C("In this or any previous round, destroy the enemy Hero with the highest cost. In case of a tie in cost any of them counts.", 2),
                }),
                new MissionCardDef(62, "Purity", new[] { C("Destroy one enemy model with the Caster rule.", 1) }),
                new MissionCardDef(63, "Bring Them Down", new[] { C("Destroy one enemy unit with the Flying rule.", 1) }),
                new MissionCardDef(64, "Assassinate", new[]
                {
                    C("Destroy one enemy model with the Hero rule.", 1),
                    C($"Destroy {(many == 3 ? "three" : "two")} or more enemy models with the Hero rule.", 2),
                }),
                new MissionCardDef(65, "Giant Slayer", new[]
                {
                    C($"Destroy one enemy model with Tough({tough}) or higher.", 1),
                    C($"Destroy one enemy model with Tough({GiantSlayerBigTough}) or higher.", 2),
                }),
                new MissionCardDef(66, "Show No Mercy", new[]
                {
                    C("Win a melee by three or more wounds.", 1), C("Win a melee by six or more wounds.", 2),
                }),
            });
            return cards;
        }

        public static MissionCardDef Card(EGameSystem system, int number) =>
            All(system).First(c => c.Number == number);

        public static string Name(EMissionCardGame game) => game switch
        {
            EMissionCardGame.Off => "Off (core rules)",
            EMissionCardGame.BattleOfWits => "Battle of Wits",
            EMissionCardGame.TotalDomination => "Total Domination",
            _ => game.ToString(),
        };
    }

    // ── State: what the game has drawn, scored and seen. Plain data, so it saves and replicates. ─────────

    /// <summary>One player's own deck and cards (Battle of Wits), keyed by slot id in the state.</summary>
    public sealed class MissionCardSide
    {
        public int Slot { get; set; }
        public List<int> Deck { get; set; } = new();
        public List<int> Table { get; set; } = new();
        public List<int> Discard { get; set; } = new();
        /// <summary>Victory points so far - cards, and at the end of the game the final objectives.</summary>
        public int Vp { get; set; }
    }

    /// <summary>A unit destroyed this game: who it belonged to, who did it and how.</summary>
    public sealed class MissionKill
    {
        public int Round { get; set; }
        public int VictimSlot { get; set; }
        /// <summary>The killer's slot, or -1 when nothing killed it (a rout, dangerous terrain...).</summary>
        public int KillerSlot { get; set; } = -1;
        /// <summary>"shooting", "melee", "spell" or "other".</summary>
        public string Cause { get; set; } = "other";
        public bool Flying { get; set; }
    }

    /// <summary>A model that died in a round, found by comparing who was alive at its start.</summary>
    public sealed class MissionModelDeath
    {
        public int Round { get; set; }
        public int VictimSlot { get; set; }
        public string ModelId { get; set; } = "";
        public bool Hero { get; set; }
        public bool Caster { get; set; }
        public int Tough { get; set; }
    }

    /// <summary>A scored card, for the report and the end of the game.</summary>
    public sealed class MissionScore
    {
        public int Round { get; set; }
        public int Slot { get; set; }
        public int Card { get; set; }
        public int Vp { get; set; }
        public string Text { get; set; } = "";
    }

    /// <summary>A living model at the start of a round - enough to know what it was once it is gone.</summary>
    public sealed class MissionModelSnapshot
    {
        public string ModelId { get; set; } = "";
        public int Slot { get; set; }
        public bool Hero { get; set; }
        public bool Caster { get; set; }
        public int Tough { get; set; }
    }

    /// <summary>
    /// Everything the mission cards need, riding the progress record (saved, replicated). Mutated in place by
    /// stages every machine runs in lockstep, like the underdog pool.
    /// </summary>
    public sealed class MissionCardState
    {
        public EMissionCardGame Game { get; set; }
        public Dictionary<int, MissionCardSide> Sides { get; set; } = new();

        // Total Domination's shared deck and table.
        public List<int> SharedDeck { get; set; } = new();
        public List<int> SharedTable { get; set; } = new();
        public List<int> SharedDiscard { get; set; } = new();

        /// <summary>Changing Priority's rolled objective, keyed "slot:card" (-1 for the shared table).</summary>
        public Dictionary<string, int> Priority { get; set; } = new();

        public List<MissionKill> Kills { get; set; } = new();
        public List<MissionModelDeath> ModelDeaths { get; set; } = new();
        /// <summary>[round, slot of the unit that failed]</summary>
        public List<int[]> MoraleFails { get; set; } = new();
        /// <summary>[round, slot of the caster]</summary>
        public List<int[]> SpellsCast { get; set; } = new();
        /// <summary>[round, winner slot, margin in wounds]</summary>
        public List<int[]> MeleeWins { get; set; } = new();

        /// <summary>Objective number (1-based) -> owner slot at the start of this round, -1 for nobody.</summary>
        public Dictionary<int, int> OwnersAtRoundStart { get; set; } = new();
        public List<MissionModelSnapshot> AliveAtRoundStart { get; set; } = new();
        public int Round { get; set; }

        /// <summary>Slot -> that army's highest-cost Hero model(s), fixed at the start of the game (Decapitate).</summary>
        public Dictionary<int, List<string>> TopHeroes { get; set; } = new();
        public List<MissionScore> Scored { get; set; } = new();
        /// <summary>Slot -> victory points (Total Domination; Battle of Wits keeps them on the side too).</summary>
        public Dictionary<int, int> Vp { get; set; } = new();
        public bool FinalObjectivesScored { get; set; }

        public static string PriorityKey(int slot, int card) => $"{slot}:{card}";
    }
}
