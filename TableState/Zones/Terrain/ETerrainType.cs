namespace FDG
{
    [Flags]
    public enum ETerrainType
    {
        None       = 0,
        Cover      = 1 << 0,
        Impassible = 1 << 1,
        Difficult  = 1 << 2,
        Dangerous  = 1 << 3,
        Blocking   = 1 << 4,
        Elevated   = 1 << 5,

        /// <summary>
        /// #439 / audit finding 8 - <b>AoF: Quest only.</b> <i>"Units may draw line of sight into and out
        /// of dense terrain, but not through it to targets on the other side."</i> (AoFQ Beginners Guide
        /// v2.1.0.)
        /// <para>
        /// Not the same as <see cref="Blocking"/>, which stops a sight line whatever touches it: a unit
        /// standing in a dense wood can shoot out of it and be shot at, and only a line that crosses the
        /// piece with BOTH ends outside is stopped. The guide's own example pieces are Forests
        /// (Difficult + Cover + Dense) and Thick Vegetation.
        /// </para>
        /// </summary>
        Dense      = 1 << 6,

        /// <summary>
        /// #439 / audit finding 8 - <b>AoF: Quest only.</b> <i>"If any model in a unit moves over 6" when
        /// moving into or through unstable terrain, then it counts as Dangerous terrain."</i> (AoFQ
        /// Beginners Guide v2.1.0.)
        /// <para>
        /// So it is <see cref="Dangerous"/> conditionally, on the DISTANCE of the move rather than on the
        /// unit: cross it on an Advance and nothing happens, cross it on a Rush or Charge and the unit
        /// takes a Dangerous Terrain Test. The guide's example pieces are Ruins (Cover + Unstable),
        /// Crumbling Buildings, Rubble, Traps and deep Rivers.
        /// </para>
        /// </summary>
        Unstable   = 1 << 7,
    }
}
