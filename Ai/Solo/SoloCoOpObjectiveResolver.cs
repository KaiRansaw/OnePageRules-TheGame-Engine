using FDG.GameModel;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - the official AI placing an objective marker by the book's squares rule rather than by the
    /// mirrored-random spread the baseline uses. See <see cref="SoloCoOpObjectivePlacer"/> for the wording
    /// and <see cref="SoloCoOpProfile"/> for the count, which is six squares everywhere except AoF:
    /// Skirmish, where it is four.
    ///
    /// <para>
    /// Legality is delegated to the same <see cref="ObjectiveAutoPlacer"/> the engine's own auto-placement
    /// uses, so "a valid position, whilst following the mission's objective set up rules" means exactly
    /// what it means everywhere else in the engine: inside the legal band, clear of impassable terrain, and
    /// far enough from the markers already down.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpObjectiveResolver : IStageResolver<PlaceObjectiveRequest, Position>
    {
        private readonly ITableState _tableState;
        private readonly Random _rng;

        public SoloCoOpObjectiveResolver(ITableState tableState, Random? rng)
        {
            _tableState = tableState;
            _rng = rng ?? new Random();
        }

        /// <summary>
        /// Read per request rather than captured in the constructor: the registry is built before the
        /// progress record exists, so a system captured then would always be the Grimdark Future default.
        /// </summary>
        private SoloCoOpProfile Profile => SoloCoOpProfile.For(_tableState.Progress.GameSystem);

        public Task<Position> Resolve(PlaceObjectiveRequest request)
        {
            var existing = _tableState.Objectives.Objects.ToList();
            var impassable = _tableState.Terrain.Objects
                .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
                .ToList();

            // The same validator the engine's own auto-placement and interactive placement both use, so
            // "a valid position, whilst following the mission's objective set up rules" means here exactly
            // what it means everywhere else.
            bool IsLegal(Position candidate) =>
                ObjectivePlacementValidator.Check(candidate, request.LegalBand, request.MinSeparationInches,
                    existing, impassable) == ObjectivePlacementValidity.Valid;

            Position chosen = SoloCoOpObjectivePlacer.Choose(request.LegalBand, Profile.ObjectiveSquares,
                IsLegal, sides => _rng.Next(sides) + 1);

            // The squares rule can genuinely run out of room - a mission with a tight separation and a band
            // full of terrain. Falling back to the engine's own placer is better than faulting the game
            // over a marker, and it is the same helper the non-AI auto-placement path uses.
            if (IsLegal(chosen)) return Task.FromResult(chosen);

            if (ObjectiveAutoPlacer.TryChoosePlacement(
                    request.LegalBand, request.MinSeparationInches,
                    request.MarkerIndex, request.TotalMarkers,
                    existing, impassable, _rng, out Position fallback))
                return Task.FromResult(fallback);

            throw new InvalidOperationException(
                "AI could not find a legal objective placement. Terrain/marker layout has no valid spots in the legal band.");
        }
    }
}
