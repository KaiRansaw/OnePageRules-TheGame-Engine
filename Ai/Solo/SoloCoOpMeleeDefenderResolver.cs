using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// Which enemy the official bot fights once its charge has made contact. <b>Solo &amp; Co-Op Rules
    /// v3.6.0</b>: "AI units always charge the closest valid target, prioritizing units that haven't
    /// activated yet." The decision tree picks that unit before moving (<see cref="SoloAiBrain"/>); this
    /// makes sure the melee is then fought against it, rather than whichever unit the move also brushed.
    ///
    /// <para>
    /// The book bot used the baseline's resolver here, which fights the unit in contact with the
    /// <i>fewest models</i> - no rule says that. Found in the bot audit (2026-11-07): four games in eight
    /// had a bot plan a charge on one unit and fight another.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpMeleeDefenderResolver
        : IStageResolver<CancellableSelectionRequest<UnitData>, CancellableResult<DataBinding<UnitData>>>
    {
        private readonly ITableState _table;
        private readonly SoloChargeMemory _memory;
        private readonly AiChooseMeleeDefenderResolver _fallback = new();

        public SoloCoOpMeleeDefenderResolver(ITableState table, SoloChargeMemory memory)
        {
            _table = table;
            _memory = memory;
        }

        public Task<CancellableResult<DataBinding<UnitData>>> Resolve(CancellableSelectionRequest<UnitData> request)
        {
            if (request.ValidOptions.Count == 0 || _table.Progress.ActivatingUnit is not { } charger)
                return _fallback.Resolve(request);

            var options = request.ValidOptions.Select(o => o.Option).ToList();

            // The unit the tree charged, if it is one of the units in contact.
            if (_memory.TargetOf(charger.ID) is { } planned
                && options.FirstOrDefault(o => o.GetValue().ID.Equals(planned)) is { } chosen)
                return Task.FromResult<CancellableResult<DataBinding<UnitData>>>(new Selected<DataBinding<UnitData>>(chosen));

            // Otherwise the book's own melee priority over the units it can fight.
            IUnit? pick = SoloAiTargeting.PickChargeTarget(charger, options.Select(o => (IUnit)o.GetValue()).ToList());
            DataBinding<UnitData> binding = pick == null ? options[0] : options.First(o => ReferenceEquals(o.GetValue(), pick));
            return Task.FromResult<CancellableResult<DataBinding<UnitData>>>(new Selected<DataBinding<UnitData>>(binding));
        }
    }

    /// <summary>The charge target the decision tree chose for each AI unit, for <see cref="SoloCoOpMeleeDefenderResolver"/>.</summary>
    public sealed class SoloChargeMemory
    {
        private readonly Dictionary<UnitID, UnitID> _targets = new();
        private readonly object _gate = new();

        public void Remember(UnitID charger, UnitID target) { lock (_gate) _targets[charger] = target; }

        public UnitID? TargetOf(UnitID charger) { lock (_gate) return _targets.TryGetValue(charger, out UnitID t) ? t : null; }
    }
}
