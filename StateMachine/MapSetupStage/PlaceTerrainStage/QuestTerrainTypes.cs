namespace FDG.Stages
{
    /// <summary>
    /// #440 / audit finding 8 - the two extra terrain types <b>AoF: Quest alone</b> defines, applied to
    /// the shared terrain pool when an AoF: Quest game sets up its table.
    ///
    /// <para>
    /// <b>Why only AoF: Quest.</b> Its Beginners Guide v2.1.0 is the one current book that defines Dense
    /// and Unstable (alongside Steep and Climbable, both of which need elevation and are parked by Kai's
    /// call). <b>GF: Star Quest's Core Rules v2.1.0 was read end to end and its terrain section lists
    /// only Cover, Difficult and Dangerous</b> - so "the Quest systems" is the wrong grouping here, and
    /// this is one system's rule, not two. The four standard books list the same three.
    /// </para>
    ///
    /// <para>
    /// <b>Which pieces get them, and why by NAME.</b> The guide gives its own mapping - <i>"Forests -
    /// Difficult + Cover + Dense"</i>, <i>"Ruins - Cover + Unstable"</i>, <i>"Rivers - Difficult (if
    /// shallow) / Unstable (if deep)"</i> - so the book keys this on what the piece IS, and the engine's
    /// flags cannot tell a forest from a ruin (both are Cover + Difficult). Matching on the pool's own
    /// piece names is therefore the faithful reading rather than a shortcut.
    /// </para>
    ///
    /// <para>
    /// Name matching is only safe with a guard, so <c>QuestTerrainTypesTests</c> asserts every name below
    /// still exists in the pool. Rename a piece without updating this list and that test fails, rather
    /// than the type quietly stopping being applied - which is how this kind of mapping normally rots.
    /// </para>
    /// </summary>
    public static class QuestTerrainTypes
    {
        /// <summary>
        /// Pool pieces the guide would call a forest or thick vegetation: line of sight goes into and out
        /// of them but not across.
        /// </summary>
        public static readonly string[] DensePieceNames =
        {
            "Forest", "Ancient wood", "Copse",
        };

        /// <summary>
        /// Pool pieces the guide would call ruins or rubble: <i>"Ruins - Cover + Unstable"</i>.
        /// </summary>
        public static readonly string[] UnstablePieceNames =
        {
            "Ruined building", "Cathedral shell", "Collapsed wall", "Rubble",
        };

        // Deliberately NOT mapped, so the next reader does not think they were missed:
        //
        //  * The guide's third mapping is "Rivers - Difficult (if shallow) / Unstable (if deep)", and the
        //    pool has no deep river. "Stream" and "Marsh" are shallow (Difficult), "Water pool" is
        //    impassible, and "Sunken mire" is already Difficult + Dangerous - which is STRICTER than
        //    Unstable, so adding it would weaken the piece rather than enrich it.
        //  * "Crater", "Crater rim" and "Wreckage" are not in the guide's mapping at all. They would be
        //    reasonable guesses and that is exactly the reason to leave them: the audit that produced
        //    this finding exists because things were guessed.

        /// <summary>
        /// Whether this game plays with Dense and Unstable terrain at all. AoF: Quest only - see the type
        /// remarks for why Star Quest is excluded despite also being a Quest system.
        /// </summary>
        public static bool AppliesTo(EGameSystem system) => system == EGameSystem.AoFQuest;

        /// <summary>
        /// The extra flags a pool piece of this name should carry, or <see cref="ETerrainType.None"/> for
        /// a piece the guide's mapping does not mention. Case-insensitive, because the names are authored
        /// data rather than identifiers.
        /// </summary>
        public static ETerrainType ExtraFlagsFor(string? pieceName)
        {
            if (string.IsNullOrWhiteSpace(pieceName)) return ETerrainType.None;

            ETerrainType extra = ETerrainType.None;

            foreach (string name in DensePieceNames)
            {
                if (string.Equals(pieceName, name, StringComparison.OrdinalIgnoreCase))
                {
                    extra |= ETerrainType.Dense;
                    break;
                }
            }

            foreach (string name in UnstablePieceNames)
            {
                if (string.Equals(pieceName, name, StringComparison.OrdinalIgnoreCase))
                {
                    extra |= ETerrainType.Unstable;
                    break;
                }
            }

            return extra;
        }
    }
}
