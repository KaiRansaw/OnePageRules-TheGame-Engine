namespace FDG.Campaign
{
    /// <summary>
    /// The roll tables shared by all four Campaign Rules v3.6.0 books (Grimdark Future, GF: Firefight,
    /// Age of Fantasy, AoF: Skirmish). The books differ only in their price list (see
    /// <see cref="CampaignNumbers"/>) and in whether a hero's skill traits also cover its unit - the
    /// tables themselves are identical, so there is one copy here.
    /// <para>
    /// These REPLACE the v3.5.1 Firefight tables that <c>StrikeTeamRules</c> was written against. Several
    /// names survived the version change with a different effect (Prowler, Runner, Duellist, Fanatic,
    /// Berserker, Leader, Tactician, Mastermind, Chemist, Herbalist, Suppressor, Destroyer) and several
    /// were dropped outright (Instigator, Vanguard, Scavenger, Climber, Hunter, Warden, Bitter Rivalry,
    /// Horrible Scars, Arm Injury, Traumatized). A strike team file written before this change keeps its
    /// old rule names, which the engine will report as unimplemented and leave inert rather than
    /// misapply - see claude/campaign-rules-delta.md.
    /// </para>
    /// </summary>
    public static class CampaignTables
    {
        /// <summary>Written in front of a trait name so it can never collide with an army book's own rule.</summary>
        public const string InjuryPrefix = "Injury: ";
        public const string TalentPrefix = "Talent: ";
        public const string SkillPrefix = "Skill: ";

        /// <summary>A pending choice the player still has to make, kept on the unit's sheet.</summary>
        public const string Specialist = "Specialist";
        public const string FastLearner = "FastLearner";
        public const string LuckyBreak = "LuckyBreak";
        public const string LearnedLesson = "LearnedLesson";
        public const string SkillSetPick = "SkillSet:";

        // -- Casualties (step 3) ---------------------------------------------------------------------

        /// <summary>Hero injury traits, rolled 1-6. 1 is death; 6 lets the player pick any other entry.</summary>
        public static readonly string[] Injuries =
            { "Dead", "Weakened", "Maimed", "Blinded", "Crippled", "Lucky Break" };

        /// <summary>Hero talent traits, rolled 1-6. 6 lets the player pick any other entry.</summary>
        public static readonly string[] Talents =
            { "Natural Talent", "Motivated", "Crazed", "Vigilant", "Toughened", "Learned Lesson" };

        /// <summary>Injury traits a Lucky Break may be swapped for: everything but Dead and Lucky Break.</summary>
        public static readonly string[] PickableInjuries = { "Weakened", "Maimed", "Blinded", "Crippled" };

        /// <summary>Talent traits a Learned Lesson may be swapped for: everything but Learned Lesson.</summary>
        public static readonly string[] PickableTalents =
            { "Natural Talent", "Motivated", "Crazed", "Vigilant", "Toughened" };

        // -- Levelling (step 4) ----------------------------------------------------------------------

        /// <summary>
        /// The regular level-up trait table, rolled 1-6. These are the same six traits Horde Mode uses,
        /// so they share <c>HordeExperience</c>'s rule names and need no new definitions.
        /// </summary>
        public static readonly string[] Traits =
        {
            StateMachine.HordeExperience.Agile,
            StateMachine.HordeExperience.Headstrong,
            Specialist,                                   // the player picks melee or shooting
            StateMachine.HordeExperience.Resilient,
            StateMachine.HordeExperience.Elite,
            FastLearner,                                  // the player picks any other trait
        };

        /// <summary>
        /// The hero skill-set roll: 1-2 Pathfinder or Captain, 3-4 Warrior or Hunter, 5-6 Warden or
        /// Overseer. The player picks one of the pair, then rolls on it.
        /// </summary>
        public static readonly (string A, string B)[] SkillSetPairs =
            { ("Pathfinder", "Captain"), ("Warrior", "Hunter"), ("Warden", "Overseer") };

        /// <summary>Each skill set's three skill traits, rolled 1-2, 3-4, 5-6.</summary>
        public static readonly IReadOnlyDictionary<string, string[]> SkillSets =
            new Dictionary<string, string[]>
            {
                ["Pathfinder"] = new[] { "Prowler", "Runner", "Explorer" },
                ["Captain"] = new[] { "Motivator", "Leader", "Idol" },
                ["Warrior"] = new[] { "Duellist", "Fanatic", "Berserker" },
                ["Hunter"] = new[] { "Suppressor", "Destroyer", "Sharpshooter" },
                ["Warden"] = new[] { "Herbalist", "Chemist", "Healer" },
                ["Overseer"] = new[] { "Tactician", "Strategist", "Mastermind" },
            };

        // -- Special objectives (step 1) -------------------------------------------------------------

        /// <summary>
        /// The special objective each army rolls for before a mission, worth
        /// <see cref="CampaignNumbers.SpecialObjectiveBonus"/> into its treasury. Rolled 1-6; an
        /// objective that cannot be completed is re-rolled.
        /// </summary>
        public static readonly (string Name, string Text)[] SpecialObjectives =
        {
            ("Hexer", "Successfully cast four or more spells by the end of the game."),
            ("Annihilator", "Be the first player to destroy an enemy unit."),
            ("Headhunter", "Kill one or more enemy Heroes."),
            ("Dominator", "Have one unit fully within each table quarter at the end of the game."),
            ("Intimidator", "Have most of your units inside the enemy deployment zone at the end of the game."),
            ("Strategist", "Control most objectives at the end of the game."),
        };

        /// <summary>The 1-6 roll for a table, clamped so a misbehaving die can never index out of range.</summary>
        public static string Entry(string[] table, int roll) => table[Math.Clamp(roll, 1, 6) - 1];
    }
}
