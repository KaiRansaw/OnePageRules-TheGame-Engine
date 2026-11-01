namespace FDG.Ai
{
    /// <summary>
    /// Which AI drives a player slot (#191, docs/ai-agent-plan.md). Every rung of the Tactician
    /// ladder stays selectable forever so any rung can be benchmarked against any other (plan G4);
    /// the solo-rules bot is the permanent baseline and is never modified (plan D1).
    ///
    /// <para>
    /// #419: the LOBBY now offers only the official OPR AI - <see cref="SoloCoOp"/> for GF, Firefight, AoF
    /// and AoF: Skirmish, and <see cref="StarQuest"/> for the Quest systems. The rest stay selectable with
    /// <c>--ai-profile</c>, which is exactly what plan G4 asks for ("selectable behind flags forever") -
    /// so hiding them from the menu is allowed, and deleting them is not.
    /// </summary>
    public enum EAiProfile
    {
        SoloRules,
        Tactician,
        /// <summary>
        /// Scripted human stand-in (#191 tooling): holds a defensive line and shoots, moving only
        /// to claim enemy-free objectives. Measurement apparatus for behaviors the advancing solo
        /// bot never elicits (melee armies crossing a held firing line), not a ladder rung.
        /// </summary>
        Gunline,
        /// <summary>
        /// B rung (#191 campaign step 9): the Tactician's policy with a UCT search over macro-actions
        /// deciding each activation. Everything below the activation - movement geometry, targets,
        /// wound assignment - is still the A policy playing out the search's prescription, and any
        /// search failure degrades to plain Tactician (plan G3). Lobby name: "Strategist Bot".
        /// </summary>
        Strategist,

        /// <summary>GF: Star Quest enemies: the Star Quest AI flowchart (solo bot underneath).</summary>
        StarQuest,

        /// <summary>
        /// #419 - <b>the official OPR AI</b> for GF, Firefight, AoF and AoF: Skirmish: Solo &amp; Co-Op
        /// Rules v3.6.0, which is what the Horde books mean by "controlled by the AI (using the Solo &amp;
        /// Co-Op rules)". This is the only non-Quest bot the lobby offers, by Kai's call on 2026-10-31:
        /// the game runs the official rules, and the house bots below stay behind <c>--ai-profile</c>.
        /// <para>
        /// Separate from <see cref="SoloRules"/> on purpose. That one is the frozen benchmark baseline
        /// (plan D1) and, despite what its resolvers used to claim, was never the book's AI.
        /// </para>
        /// </summary>
        SoloCoOp,
    }
}
