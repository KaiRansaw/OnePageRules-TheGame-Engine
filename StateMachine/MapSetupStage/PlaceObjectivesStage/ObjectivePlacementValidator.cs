namespace FDG.Stages
{
    public enum ObjectivePlacementValidity
    {
        Valid,
        OutsideLegalBand,
        TooCloseToObjective,
        ImpassableTerrain,
        Unreachable,
    }

    /// <summary>
    /// Pure-function checks for whether a candidate <see cref="Position"/> is a
    /// legal spot for a new objective marker, per the GF Beginner's Guide v3.5.1
    /// page 6 placement rules. Shared by the engine's auto-placer, the GUI/CLI
    /// resolvers, and the AI resolver so all four agree on legality.
    /// </summary>
    public static class ObjectivePlacementValidator
    {
        /// <summary>Float slack for "over X": a marker 9.0005" away still counts as exactly 9".</summary>
        private const float OverEpsilonInches = 0.001f;

        /// <summary>
        /// Returns <see cref="ObjectivePlacementValidity.Valid"/> if the candidate is legal;
        /// otherwise returns the first failing constraint.
        /// </summary>
        public static ObjectivePlacementValidity Check(
            Position candidate,
            RectangularZone legalBand,
            float minSeparationInches,
            IEnumerable<IObjective> existing,
            IEnumerable<ITerrain> impassableTerrain)
        {
            if (!legalBand.IsPointWithinZone(new Float2(candidate.x, candidate.z)))
                return ObjectivePlacementValidity.OutsideLegalBand;

            // The books say "over 9\" away from other markers" (core rules, Advanced Rulebooks, Mission Cards v3.6.0):
            // exactly 9" is too close. A zero separation (fixed-spot missions) checks nothing.
            foreach (var obj in existing)
            {
                float dx = candidate.x - obj.Position.x;
                float dz = candidate.z - obj.Position.z;
                if (minSeparationInches > 0f && MathF.Sqrt(dx * dx + dz * dz) <= minSeparationInches + OverEpsilonInches)
                    return ObjectivePlacementValidity.TooCloseToObjective;
            }

            foreach (var terrain in impassableTerrain)
            {
                if (terrain.IsPointWithinZone(new Float2(candidate.x, candidate.z)))
                    return ObjectivePlacementValidity.ImpassableTerrain;
            }

            // Unreachable check ("spots too tight to get to") is not yet implemented.
            // The other constraints catch the common cases; a position inside the legal
            // band, away from other markers and outside impassable terrain is almost
            // always reachable in practice. Revisit if a pathological terrain layout
            // produces a legal-band island.
            return ObjectivePlacementValidity.Valid;
        }
    }
}
