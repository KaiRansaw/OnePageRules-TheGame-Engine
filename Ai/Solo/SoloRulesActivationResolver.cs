using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #419 - answers "which unit activates next?" with the <b>Solo &amp; Co-Op Rules v3.6.0</b> order:
    /// a D3 for one of three table sections, the next section along if that one has nobody eligible, then a
    /// random unit from it, with Shaken units held back until everything else has gone.
    /// <para>
    /// Replaces a <c>DerivedRequestAdapter</c> onto <c>AiSelectionResolver</c>, which took the first option
    /// the engine offered - army build order, in practice. See <see cref="SoloAiActivationOrder"/> for the
    /// book's wording and for what "clockwise" had to become in an engine that does not keep the AI's
    /// deployment edge after deployment.
    /// </para>
    /// <para>
    /// The dice come from the AI's own seeded stream, not the game's dice roller: this is the bot deciding
    /// how to play, not a roll the rules ask a player to make, so it must not consume the shared dice
    /// sequence that every pinned scenario and benchmark depends on.
    /// </para>
    /// </summary>
    public sealed class SoloRulesActivationResolver
        : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly Random _rng;

        public SoloRulesActivationResolver(Random rng) => _rng = rng;

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            var options = request.ValidOptions.Select(o => o.Option).ToList();
            DataBinding<UnitData> pick = SoloAiActivationOrder.Pick(
                options,
                binding => binding.GetValue(),
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES,
                sides => _rng.Next(sides) + 1);
            return Task.FromResult(pick);
        }
    }
}
