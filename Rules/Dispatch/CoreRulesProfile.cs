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
        bool ScoutIsSimpleRedeploy,
        bool EveryHeroLendsMoraleQuality,
        int TransportHeroToughCap,
        int TransportNonHeroToughCap,
        int TransportStandardModelTough,
        int MaxTargetsPerShootAction,
        bool StrikeBackIsMandatory,
        int CommandGroupExtraPicks,
        bool CommandGroupCapIsPerArmy)
    {
        /// <summary>
        /// The Command Group upgrades that confer on the picked units: <b>Banner</b> and <b>Musician</b>.
        /// <b>Sergeant is deliberately absent</b> - it is <i>"This model gets AP(+1) when attacking"</i>
        /// in every system, self-only, with nothing to confer (#439).
        /// </summary>
        public static readonly string[] ConferringCommandGroupRules = { "Banner", "Musician" };

        /// <summary>
        /// Grimdark Future and Age of Fantasy: the full-scale games. Blast caps on the target unit's
        /// models and spills nowhere; Scout sets aside and deploys last; melee has its Melee Results step.
        /// </summary>
        public static readonly CoreRulesProfile FullScale = new(
            "Grimdark Future / Age of Fantasy",
            MeleeResultsDecideMorale: true,
            BlastSplitsBetweenNearbyUnits: false,
            ScoutIsSimpleRedeploy: false,
            // "The Hero's unit, and units within 6" of friendly Heroes WITH TOUGH(9) OR HIGHER, may use
            // the Hero's Quality for Morale Tests as long as the Hero isn't Shaken." The threshold is
            // what makes this a big-monster aura rather than something every Hero projects.
            EveryHeroLendsMoraleQuality: false,
            // "up to X friendly models or Heroes with up to Tough(6), and non-Heroes with up to
            // Tough(3) which occupy 3 spaces each." An ordinary model is Tough(1) here, so anything
            // above that is the 3-space "large" case.
            TransportHeroToughCap: 6,
            TransportNonHeroToughCap: 3,
            TransportStandardModelTough: 1,
            // "may fire at up to two different targets with different weapon types."
            MaxTargetsPerShootAction: 2,
            // "Then defenders MAY CHOOSE to strike back, but don't have to."
            StrikeBackIsMandatory: false,
            // GF and AoF print the narrow Command Group: "This model AND ITS UNIT get +1 to Morale Test
            // rolls triggered by shooting or melee" - nobody else. AoF v3.6.0's section is the one that
            // prints it; GF's core book has no Command Groups section at all, and Army Forge serves the
            // same narrow wording for it (#439).
            CommandGroupExtraPicks: 0,
            // AoF v3.6.0: "Each UNIT may only have one of each of the following upgrades."
            CommandGroupCapIsPerArmy: false);

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
            ScoutIsSimpleRedeploy: false,
            // #420: these books print the aura with NO Tough threshold and no join clauses at all -
            // "Units within 6" of friendly Heroes may use the Hero's Quality for Morale Tests as long as
            // the Hero isn't Shaken." Every Hero projects it. Running Grimdark Future's Tough(9) gate
            // here withheld the aura from essentially every Hero in a skirmish game, where Tough(9) is a
            // vehicle-scale number, so a unit beside its own Hero tested on its own Quality all game.
            EveryHeroLendsMoraleQuality: true,
            // #420: "up to X friendly models with Tough(3) or Heroes with up to Tough(18), and
            // non-Heroes with up to Tough(9) which occupy 3 spaces each." The skirmish games put every
            // ordinary model at Tough(3), so the whole scale is x3 - and the engine was running
            // Grimdark Future's 6/3/1 here, which refused passengers the rule allows.
            // #437 - WHERE THAT WORDING COMES FROM: Army Forge, not these books. Neither the GFF nor the
            // AoFS Core Rules v3.6.0 has a Transport entry at all; Army Forge serves one per system with
            // the scaled caps. Correct numbers, wrong citation until now.
            TransportHeroToughCap: 18,
            TransportNonHeroToughCap: 9,
            TransportStandardModelTough: 3,
            // Both skirmish books print the full-scale clauses here, word for word.
            MaxTargetsPerShootAction: 2,
            StrikeBackIsMandatory: false,
            // #439: the WIDE Command Group. AoFS v3.6.0 prints it ("This model's unit and up to 2
            // friendly units picked before the start of the game get ... If this model is killed, picked
            // models that are deployed lose the rule"), GFF's core book has no Command Groups section,
            // and Army Forge serves that same wide wording for Firefight - the Transport situation
            // exactly, so Army Forge governs.
            CommandGroupExtraPicks: 2,
            // Deliberately FALSE on the shared skirmish profile, and overridden for AoF: Skirmish alone
            // in <see cref="For"/>. Only the AoFS book caps Command Groups per army ("Each army may only
            // have one model with one of the following upgrades"); GFF's core book has no Command Groups
            // section, and Army Forge's one-line summaries carry no cap either way - so there is NO
            // source for an army-wide cap in Firefight. Reading AoFS's clause across to it because both
            // are skirmish games is the mistake CLAUDE.md names as the easiest one to make here.
            CommandGroupCapIsPerArmy: false);

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
            ScoutIsSimpleRedeploy: true,
            // #420: both Quest books print the same threshold-free aura as the skirmish games.
            EveryHeroLendsMoraleQuality: true,
            // "up to X friendly models with Tough(9) or Heroes with up to Tough(54), and non-Heroes
            // with up to Tough(27)." The Quest scale is x9 on Grimdark Future's.
            // #437: again Army Forge's wording - GFSQ Core Rules v2.1.0 has no Transport entry either.
            TransportHeroToughCap: 54,
            TransportNonHeroToughCap: 27,
            TransportStandardModelTough: 9,
            // #437: ONE target, not two. AoFQ Core v2.1.0 is explicit - "When shooting, a unit must pick
            // ONE valid target and all models in the unit may shoot at it" - and GFSQ v2.1.0 never grants
            // the split at all: "Models in range and line of sight may fire all ranged weapons." Both
            // books, and it fits the mode: Quest is characters against the world, so a hero picks a
            // target and commits. The engine ran the full-scale cap of 2 in every system until #437.
            MaxTargetsPerShootAction: 1,
            // #437: MUST strike back. GFSQ v2.1.0 "Then defenders must strike back"; AoFQ v2.1.0 "the
            // defending unit must strike back". The full-scale books say "may choose to ... but don't
            // have to", and the engine offered that choice everywhere.
            StrikeBackIsMandatory: true,
            // #439: both Quest systems take the WIDE Command Group from Army Forge - "This model's unit
            // and up to 2 friendly units picked before the start of the game". Neither Quest core book
            // has a Command Groups section, so Army Forge is the only source, as with Transport above.
            // Checked: all four wide systems' Banner and Musician say plain "up to 2 friendly units",
            // with no "nearest" qualifier - that word appears on the Quest systems' "...Aura" rules
            // (Enhanced Lustbound Aura and its family), which are a different mechanism, so the player
            // picks freely here.
            CommandGroupExtraPicks: 2,
            // No Quest book prints a Command Groups cap, so the book data's per-unit maxPicks stands.
            CommandGroupCapIsPerArmy: false);

        /// <summary>
        /// #418 - AoF: Quest reads the same ways as Star Quest. Checked line by line, originally against
        /// AoFQ Core Rules v2.0.1 and <b>re-verified against v2.1.0</b> when that edition arrived
        /// (2026-11-01); v2.0.1 is now an archived edition and is cited here only as history:
        /// <list type="bullet">
        /// <item><i>"Blast(X): ... Hits must be split evenly between all enemy units within 3" of any
        /// model from the target"</i> - the same spread clause #414 built, and worded more explicitly
        /// than Star Quest's.</item>
        /// <item><i>"Scout: This model may be deployed anywhere fully within 9" of its deployment
        /// zone"</i> - a plain redeploy, no set-aside and no standoff. <b>v2.1.0 prints 9", the same as
        /// Star Quest</b> (v2.0.1 printed 12"), while Army Forge serves 12" for both; the distance lives
        /// in <see cref="QuestRulesProfile"/> under the Army-Forge-wins ruling, and only the PROCEDURE
        /// lives here.</item>
        /// <item>Its Morale section has only the half-strength trigger, and its Fear(X) reads <i>"when
        /// checking if the target must take morale test in melee"</i> - so there is no melee-results
        /// comparison for Fear to attach to, exactly as in Star Quest.</item>
        /// </list>
        /// It therefore shares Star Quest's profile; the two systems' numbers differ, their special-rule
        /// READINGS do not.
        /// </summary>
        /// <summary>
        /// AoF: Skirmish is the skirmish profile with one clause of its own: <i>"Each army may only have
        /// one model with one of the following upgrades."</i> It is the ONLY book of the six that caps
        /// Command Groups per army, and Firefight's core book has no Command Groups section at all - so
        /// this override exists to stop that clause being read across to Firefight on the strength of
        /// both being skirmish games (#439).
        /// </summary>
        private static readonly CoreRulesProfile AoFSkirmishProfile =
            Skirmish with { SystemName = "AoF: Skirmish", CommandGroupCapIsPerArmy = true };

        public static CoreRulesProfile For(EGameSystem system) => system switch
        {
            EGameSystem.GFStarQuest or EGameSystem.AoFQuest => StarQuest,
            EGameSystem.AoFSkirmish => AoFSkirmishProfile,
            EGameSystem.GFFirefight => Skirmish,
            _ => FullScale,
        };

        /// <summary>The profile for the game in progress, from its settings.</summary>
        public static CoreRulesProfile For(IGameContext context) => For(context.Settings.GameSystem);
    }
}
