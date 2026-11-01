using FDG.Rules.Foundation;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - which unit the AI activates next, from <b>Solo &amp; Co-Op Rules v3.6.0</b>:
    ///
    /// <para>
    /// "Before starting, divide the table into <b>3 different sections</b> along the AI's deployment zone
    /// edge, and number them 1, 2 and 3. When it's the AI's turn to activate a unit, first <b>roll a D3</b>
    /// to see which section it will activate a unit in. If there are no eligible units in that section,
    /// <b>move onto the next section with an eligible unit, going clockwise</b>. Then <b>roll a die to
    /// randomly determine which unit</b> from that section is activated next. <b>Shaken AI units only
    /// activate after all non-shaken AI units have activated.</b>"
    /// </para>
    /// <para>
    /// The old bot took the first unit the engine offered, in whatever order the army happened to be built.
    /// That is not a cosmetic difference: activating a predictable sequence lets a player work out exactly
    /// what moves next and stand just outside its reach, which is most of what makes a bot feel beatable in
    /// a bad way. The randomised section roll is the book's answer to that.
    /// </para>
    /// <para>
    /// <b>Sections, and what "clockwise" means here.</b> The book draws the three bands along the AI's own
    /// deployment edge, which the engine does not record once deployment is over. The bands are therefore
    /// taken across the table's longer axis, which is the same division for the edge-to-edge deployments
    /// every mode in the engine uses, and "the next section going clockwise" becomes "the next band along,
    /// wrapping" - 1 to 2 to 3 to 1. Recorded as an interpretation rather than a reading, because a mode
    /// with corner deployment (Horde) has no single AI edge at all and this still has to give an answer.
    /// </para>
    /// </summary>
    public static class SoloAiActivationOrder
    {
        public const int Sections = 3;

        /// <summary>
        /// Which band a position falls in, 1 to 3, across the table's longer axis.
        /// </summary>
        public static int SectionOf(Position position, float tableWidthInches, float tableHeightInches)
        {
            bool widthIsLonger = tableWidthInches >= tableHeightInches;
            float along = widthIsLonger ? position.x : position.z;
            float span = widthIsLonger ? tableWidthInches : tableHeightInches;
            if (span <= 0f) return 1;

            int band = (int)(along / (span / Sections)) + 1;
            return Math.Clamp(band, 1, Sections);
        }

        /// <summary>
        /// The unit to activate: a D3 for the section, the next section clockwise if that one is empty, then
        /// a random unit from it - and Shaken units only once nothing else is left.
        /// </summary>
        /// <param name="roll">A D3 (1-3) for the section, and a die for the unit within it.</param>
        /// <param name="holdBack">
        /// #423: candidates to leave until last, whatever the section roll says - Grimdark Future's
        /// Transport clause, "AI units with Transport <b>always activate before their cargo on the first
        /// round</b>". Passed in as a predicate rather than read here because the test needs the table
        /// state and the round, and this helper deliberately knows about neither; null for every other
        /// system and every round after the first. Applied with the same shape as the Shaken clause above
        /// it, including the same safety valve: if holding back would leave nothing to activate, nothing
        /// is held back.
        /// </param>
        public static T Pick<T>(IReadOnlyList<T> candidates, Func<T, IUnit> unitOf,
            float tableWidthInches, float tableHeightInches, Func<int, int> roll,
            Func<T, bool>? holdBack = null)
        {
            if (candidates.Count == 0)
                throw new InvalidOperationException("AI received an activation request with no valid options.");
            if (candidates.Count == 1) return candidates[0];

            // "Shaken AI units only activate after all non-shaken AI units have activated."
            var eligible = candidates
                .Where(c => !unitOf(c).Tokens.HasToken(TokenType.Shaken))
                .ToList();
            if (eligible.Count == 0) eligible = candidates.ToList();

            if (holdBack != null)
            {
                var notHeld = eligible.Where(c => !holdBack(c)).ToList();
                if (notHeld.Count > 0) eligible = notHeld;
            }

            var bySection = new Dictionary<int, List<T>>();
            foreach (T candidate in eligible)
            {
                IUnit unit = unitOf(candidate);
                IModel? model = unit.Models.FirstOrDefault(m => m.GetIsAlive());
                int section = model == null ? 1
                    : SectionOf(model.Position, tableWidthInches, tableHeightInches);
                if (!bySection.TryGetValue(section, out List<T>? list))
                    bySection[section] = list = new List<T>();
                list.Add(candidate);
            }

            int start = Math.Clamp(roll(Sections), 1, Sections);
            for (int step = 0; step < Sections; step++)
            {
                int section = (start - 1 + step) % Sections + 1;   // "going clockwise", wrapping
                if (!bySection.TryGetValue(section, out List<T>? inSection) || inSection.Count == 0) continue;
                int index = Math.Clamp(roll(inSection.Count), 1, inSection.Count) - 1;
                return inSection[index];
            }

            return eligible[0];   // unreachable: eligible is non-empty, so some section has a unit
        }
    }
}
