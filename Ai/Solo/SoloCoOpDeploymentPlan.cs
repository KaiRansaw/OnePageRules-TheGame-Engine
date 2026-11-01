namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - the one piece of deployment state the book needs kept between requests: which of the AI's
    /// three groups goes in which table section.
    ///
    /// <para>
    /// "AI units are randomly divided into <b>3 groups</b> of equal size (as far as possible) ... For each
    /// group <b>roll a D3</b> to determine which section its units deploy in, <b>re-rolling if all 3 groups
    /// would deploy in the same section</b>."
    /// </para>
    /// <para>
    /// This cannot live in the placement resolver's own call, because the engine asks about one unit at a
    /// time and the groups have to agree with each other across all of those calls. Rolled once, lazily, on
    /// the first placement of a game, and then read.
    /// </para>
    /// <para>
    /// <b>Groups are assigned by deployment order, which is the honest reading.</b> The book says units are
    /// "randomly divided" into groups, and the engine already hands them over in a random order - so taking
    /// them in threes as they arrive produces the same distribution as shuffling them first, without
    /// needing the whole roster up front (which a resolver answering one request does not have).
    /// </para>
    /// <para>
    /// <b>AoF: Skirmish gets three groups and two sections</b>, because that is what its book says in two
    /// consecutive sentences. Its own re-roll clause is what resolves the squeeze, and the "not all in one
    /// section" guarantee still holds with two.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpDeploymentPlan
    {
        private readonly Func<int, int> _roll;
        private int[]? _sectionForGroup;

        /// <param name="roll">Rolls 1..n inclusive - the AI's own seeded stream, not the game's dice.</param>
        public SoloCoOpDeploymentPlan(Func<int, int> roll) => _roll = roll;

        /// <summary>The section this deployment belongs in, by its order in the deployment sequence.</summary>
        public int SectionFor(int deployIndex, SoloCoOpProfile profile)
        {
            int[] sections = Sections(profile);
            int group = Math.Abs(deployIndex) % SoloCoOpProfile.DeploymentGroups;
            return sections[group];
        }

        /// <summary>The rolled section per group, rolled once per game.</summary>
        public int[] Sections(SoloCoOpProfile profile)
        {
            if (_sectionForGroup != null) return _sectionForGroup;

            var rolled = new int[SoloCoOpProfile.DeploymentGroups];

            // "re-rolling if all 3 groups would deploy in the same section". Bounded rather than a while
            // loop: with one section the condition can never be satisfied, and an AI that spins on a die
            // roll is worse than one that accepts a legal-but-dull layout.
            const int Attempts = 20;
            for (int attempt = 0; attempt < Attempts; attempt++)
            {
                for (int group = 0; group < rolled.Length; group++)
                    rolled[group] = profile.SectionForRoll(_roll(6));

                if (profile.DeploymentSections <= 1) break;
                if (rolled.Distinct().Count() > 1) break;
            }

            _sectionForGroup = rolled;
            return _sectionForGroup;
        }
    }
}
