using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
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
        private readonly ITableState? _table;

        /// <param name="table">
        /// #423: needed only for Grimdark Future's Transport clause, which is about the first round and
        /// about who is riding in what. Optional so the older construction (and its tests) still compiles;
        /// without it the clause is skipped and the order is exactly what #419 shipped.
        /// </param>
        public SoloRulesActivationResolver(Random rng, ITableState? table = null)
        {
            _rng = rng;
            _table = table;
        }

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            var options = request.ValidOptions.Select(o => o.Option).ToList();
            DataBinding<UnitData> pick = SoloAiActivationOrder.Pick(
                options,
                binding => binding.GetValue(),
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES,
                sides => _rng.Next(sides) + 1,
                holdBack: CargoWaitingForItsRide);
            return Task.FromResult(pick);
        }

        /// <summary>
        /// #423 Transport (Grimdark Future only): "AI units with Transport <b>always activate before their
        /// cargo on the first round</b>."
        ///
        /// <para>
        /// Read as: on round 1, a unit riding in a transport that has not activated yet waits. Not "all
        /// transports first" - the book ties each transport to <i>its</i> cargo, so a second transport's
        /// passengers do not hold up a transport that has already unloaded.
        /// </para>
        /// <para>
        /// Round 1 only, as written. From round 2 the cargo is normally out anyway (the clause two lines
        /// further on makes it disembark on its first activation), and a unit that re-boarded later has no
        /// clause about it.
        /// </para>
        /// </summary>
        private bool CargoWaitingForItsRide(DataBinding<UnitData> candidate)
        {
            if (_table == null) return false;

            // The clause is in the Grimdark Future book ONLY. Age of Fantasy has the Transport RULE (its
            // own Tough caps and all) but its Solo & Co-Op book prints no Transport clause, and Firefight
            // and Skirmish have neither. An activation order is a thing the book either tells the AI to
            // change or does not, so where it is silent the order stays as the rest of the book sets it.
            if (!SoloCoOpProfile.For(_table.Progress.GameSystem).HasTransportClause) return false;

            if ((_table.Progress.RoundCount ?? 0) > 1) return false;

            IUnit unit = candidate.GetValue();
            if (TransportUtilities.GetTransportId(unit) is not { } transportId) return false;

            foreach (IUnit other in _table.Units.Objects)
            {
                if (!other.ID.Equals(transportId)) continue;
                // The ride is still to go: wait for it. Once it has activated (or died, in which case it
                // is not in the list any more and its cargo has spilled out) the cargo is free to act.
                return !other.Tokens.HasToken(TokenType.ActivatedThisRound);
            }

            return false;
        }
    }
}
