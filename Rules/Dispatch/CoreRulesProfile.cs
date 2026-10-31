namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The core-rule behaviours that genuinely differ between OPR's game systems.
    ///
    /// <para>
    /// #414. <see cref="CoreRuleCatalog"/> is one shared set of definitions written to Grimdark Future's
    /// wording, and for almost every rule that is correct - the five books say the same thing. A handful
    /// do not, and until this existed the engine played Grimdark Future's version of them in every game.
    /// Each flag below was read word for word out of the book it belongs to, and the table is recorded in
    /// <c>WorkItems/414</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately not a per-system rule catalog.</b> A parallel <see cref="CoreRuleCatalog"/> per
    /// system would be a great deal of architecture for a handful of flags, and it would invite copies of
    /// the ~200 rules that do NOT differ - which is how the data drifts. This follows
    /// <see cref="SaveLoad.ForceOrgProfile"/>, which solved the same shape for force-org caps: one profile
    /// keyed on the system, read at the sites that already own the behaviour. If the flag count ever grows
    /// past a handful, that is the moment to reconsider.
    /// </para>
    ///
    /// <para>
    /// Keyed on <see cref="EGameSystem"/> - the game being played - rather than on the army's book slug.
    /// Which rules apply is a property of the game, not of a list: two armies in one game play by one set
    /// of rules even if one of them was written for another system.
    /// </para>
    /// </summary>
    public sealed record CoreRulesProfile(
        string SystemName,
        bool MeleeResultsDecideMorale,
        bool BlastSplitsBetweenNearbyUnits,
        bool ScoutIsSimpleRedeploy)
    {
        /// <summary>
        /// Grimdark Future and Age of Fantasy: the full-scale games. Blast caps on the target unit's
        /// models and spills nowhere; Scout sets aside and deploys last; melee has its Melee Results step.
        /// </summary>
        public static readonly CoreRulesProfile FullScale = new(
            "Grimdark Future / Age of Fantasy",
            MeleeResultsDecideMorale: true,
            BlastSplitsBetweenNearbyUnits: false,
            ScoutIsSimpleRedeploy: false);

        /// <summary>
        /// GF: Firefight and AoF: Skirmish. Identical to the full-scale games except that <b>Blast splits</b>:
        /// <i>"Hits must be split evenly between the target and all enemy units within 3" of any model from
        /// the target (defender picks how), but each unit may only take up to as many hits as models in
        /// it."</i> (GFF/AoFS Core Rules v3.6.0, word for word identical in both.)
        /// <para>
        /// AoF: Skirmish's Scout wording has not been read yet - the copy in the project cuts off before
        /// the S entries - so it takes Firefight's, which matches it everywhere else that has been checked.
        /// Recorded as unverified in WorkItems/414 rather than assumed silently.
        /// </para>
        /// </summary>
        public static readonly CoreRulesProfile Skirmish = new(
            "GF: Firefight / AoF: Skirmish",
            MeleeResultsDecideMorale: true,
            BlastSplitsBetweenNearbyUnits: true,
            ScoutIsSimpleRedeploy: false);

        /// <summary>
        /// GF: Star Quest (GFSQ Core Rules v2.1.0). Three differences, all read from the book:
        /// <list type="bullet">
        /// <item><b>Blast splits</b>, as in the skirmish games.</item>
        /// <item><b>Scout</b> is <i>"may be deployed anywhere fully within 9" of its deployment zone"</i> and
        /// nothing else: no set-aside, no alternating placement, no distance from enemies.</item>
        /// <item><b>There is no Melee Results step at all.</b> Star Quest's Morale section has only the
        /// half-strength trigger - <i>"at the end of an activation where wounds leave a unit with half or
        /// less of its total size or Tou value"</i> - and no "compare wounds, the loser tests". This is also
        /// why its Fear(X) reads <i>"when checking if they must take a Morale Test (must deal at least one
        /// wound)"</i> where every other book reads "when checking who won": in Star Quest there is no
        /// who-won for it to attach to.</item>
        /// </list>
        /// </summary>
        public static readonly CoreRulesProfile StarQuest = new(
            "GF: Star Quest",
            MeleeResultsDecideMorale: false,
            BlastSplitsBetweenNearbyUnits: true,
            ScoutIsSimpleRedeploy: true);

        /// <summary>
        /// #418 - AoF: Quest reads the same three ways, checked line by line against <b>AoFQ Core Rules
        /// v2.0.1</b> rather than assumed from the shape of the game:
        /// <list type="bullet">
        /// <item><i>"Blast(X): ... Hits must be split evenly between all enemy units within 3" of any
        /// model from the target"</i> - the same spread clause #414 built, and worded more explicitly
        /// than Star Quest's.</item>
        /// <item><i>"Scout: This model may be deployed anywhere fully within 12" of its deployment
        /// zone"</i> - a plain redeploy, no set-aside and no standoff. <b>12" where Star Quest says
        /// 9"</b>, which is why the distance lives in <see cref="QuestRulesProfile"/> and only the
        /// PROCEDURE lives here.</item>
        /// <item>Its Morale section has only the half-strength trigger, and its Fear(X) reads <i>"when
        /// checking if the target must take morale test in melee"</i> - so there is no melee-results
        /// comparison for Fear to attach to, exactly as in Star Quest.</item>
        /// </list>
        /// It therefore shares Star Quest's profile; the two systems' numbers differ, their special-rule
        /// READINGS do not.
        /// </summary>
        public static CoreRulesProfile For(EGameSystem system) => system switch
        {
            EGameSystem.GFStarQuest or EGameSystem.AoFQuest => StarQuest,
            EGameSystem.GFFirefight or EGameSystem.AoFSkirmish => Skirmish,
            _ => FullScale,
        };

        /// <summary>The profile for the game in progress, from its settings.</summary>
        public static CoreRulesProfile For(IGameContext context) => For(context.Settings.GameSystem);
    }
}
