namespace FDG.Ai
{
    /// <summary>
    /// #419 - which set of decisions a solo-rules-family registry carries.
    ///
    /// <para>
    /// The three share almost all of their resolvers (wound assignment, deployment zones, melee defenders,
    /// terrain placement), and differ only where a rulebook says they must. Keeping them one registry with
    /// a flavour, rather than three registries, is what stops a fix to the shared 90% from landing in one
    /// and not the others.
    /// </para>
    /// </summary>
    public enum ESoloFlavour
    {
        /// <summary>
        /// The frozen benchmark baseline. <b>Never change its behaviour</b> - plan D1
        /// (docs/ai-agent-plan.md, signed off 2026-07-09) makes it the permanent yardstick every Tactician
        /// and Strategist gate was measured against, so a change here silently invalidates those numbers.
        /// It is NOT the official OPR AI and never was, despite what its resolvers used to claim.
        /// </summary>
        Baseline,

        /// <summary>
        /// The Quest systems' official AI: the flowchart printed in GFSQ / AoFQ Core Rules v2.1.0, which is
        /// a different document from the Solo &amp; Co-Op rules and a different behaviour.
        /// </summary>
        StarQuest,

        /// <summary>
        /// The official AI for GF, Firefight, AoF and AoF: Skirmish - <b>Solo &amp; Co-Op Rules v3.6.0</b>,
        /// which is what the Horde books mean by "controlled by the AI (using the Solo &amp; Co-Op rules)".
        /// All four books are word for word identical apart from seven per-system clauses.
        /// </summary>
        SoloCoOp,
    }
}
