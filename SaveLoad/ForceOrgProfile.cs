using FDG.ArmyBuilding;

namespace FDG.SaveLoad
{
    /// <summary>
    /// The optional Force Organisation caps, which differ per OPR game system (#409). Until 3.6 the
    /// validator hard-coded Grimdark Future's numbers and implemented only two of the five caps; the
    /// books have always had four different sets, and 3.6 added a model/Tough cap to every one of them.
    ///
    /// <para>Each profile is read straight off its own Core Rules v3.6.0 (or v3.6.0 skirmish) book:</para>
    /// <list type="table">
    /// <listheader><term>System</term><description>Hero / copies-X / unit / density</description></listheader>
    /// <item><term>Grimdark Future</term><description>500 / 1000 / 200 / 1 model-or-Tough per 20</description></item>
    /// <item><term>Age of Fantasy</term><description>375 / 750 / 150 / 1 model-or-Tough per 15</description></item>
    /// <item><term>GF: Firefight</term><description>400 / 400 / 80 / 3 Tough per 40</description></item>
    /// <item><term>AoF: Skirmish</term><description>300 / 300 / 60 / 3 Tough per 30</description></item>
    /// </list>
    ///
    /// <para>The two scales word the density cap differently - the big games say "max. 1 model or Tough
    /// per Npts", the skirmish games "max. 3 total Tough per Npts" - but both are counting the same
    /// quantity, so one pair of numbers covers both: <see cref="DensityAllowancePerBlock"/> models-or-Tough
    /// per <see cref="DensityPointsPerBlock"/> points. A model's contribution is its Tough value, or 1 if
    /// it has none, which is what makes a Tough(6) walker cost six bodies' worth of the cap.</para>
    /// </summary>
    public sealed record ForceOrgProfile(
        string SystemName,
        int PointsPerHero,
        int PointsPerExtraCopy,
        int PointsPerUnit,
        int DensityAllowancePerBlock,
        int DensityPointsPerBlock,
        int MaxSingleUnitPercent = 35)
    {
        public static readonly ForceOrgProfile GrimdarkFuture = new(
            "Grimdark Future",
            PointsPerHero: 500, PointsPerExtraCopy: 1000, PointsPerUnit: 200,
            DensityAllowancePerBlock: 1, DensityPointsPerBlock: 20);

        public static readonly ForceOrgProfile AgeOfFantasy = new(
            "Age of Fantasy",
            PointsPerHero: 375, PointsPerExtraCopy: 750, PointsPerUnit: 150,
            DensityAllowancePerBlock: 1, DensityPointsPerBlock: 15);

        public static readonly ForceOrgProfile GrimdarkFutureFirefight = new(
            "GF: Firefight",
            PointsPerHero: 400, PointsPerExtraCopy: 400, PointsPerUnit: 80,
            DensityAllowancePerBlock: 3, DensityPointsPerBlock: 40);

        public static readonly ForceOrgProfile AgeOfFantasySkirmish = new(
            "AoF: Skirmish",
            PointsPerHero: 300, PointsPerExtraCopy: 300, PointsPerUnit: 60,
            DensityAllowancePerBlock: 3, DensityPointsPerBlock: 30);

        /// <summary>
        /// The profile for an army's game-system slug. An army with no slug is Grimdark Future - the only
        /// system that existed before the field did (see <c>ArmyListFile.GameSystem</c>) - and so is any
        /// system without a profile of its own, which keeps an unrecognised slug advisory rather than
        /// silently capless.
        /// </summary>
        public static ForceOrgProfile For(string? gameSystemSlug)
        {
            // Matched through GameSystems.SameSystem rather than a switch on the constants: Normalize
            // only trims, so slug comparison has to stay case-insensitive the way every other consumer
            // does it.
            if (GameSystems.SameSystem(gameSystemSlug, GameSystems.AgeOfFantasy)) return AgeOfFantasy;
            if (GameSystems.SameSystem(gameSystemSlug, GameSystems.GFFirefight)) return GrimdarkFutureFirefight;
            if (GameSystems.SameSystem(gameSystemSlug, GameSystems.AoFSkirmish)) return AgeOfFantasySkirmish;
            return GrimdarkFuture;
        }
    }
}
