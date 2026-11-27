using FDG.Data.Containers;
using Newtonsoft.Json;

namespace FDG.Data
{
    /// <summary>
    /// A read-only store that shows one of several stores and can be switched to another (battle replays, homebrew
    /// Kai 2026-11-27): a replay is a list of whole-game save points, and the screen keeps one table view
    /// (<see cref="TableState"/>) whose store is switched from save point to save point instead of being rebuilt.
    /// <para>
    /// Every read goes to the current store. Subscriptions are remembered and moved over on <see cref="Show"/>, so a
    /// view made once (the table state's collections subscribe to "created" / "removed") keeps working.
    /// </para>
    /// </summary>
    public sealed class SwitchingDataStore : IReadableGameDataStore
    {
        private readonly object _gate = new();
        private IReadableGameDataStore _current;
        private readonly List<(Delegate Handler, Action<IReadableGameDataStore> On, Action<IReadableGameDataStore> Off)> _subscriptions = new();

        public SwitchingDataStore(IReadableGameDataStore first) => _current = first;

        /// <summary>The store being shown.</summary>
        public IReadableGameDataStore Current
        {
            get { lock (_gate) return _current; }
        }

        /// <summary>Shows <paramref name="next"/> from now on, moving every subscription over to it.</summary>
        public void Show(IReadableGameDataStore next)
        {
            lock (_gate)
            {
                if (ReferenceEquals(next, _current)) return;
                foreach (var (_, _, off) in _subscriptions) off(_current);
                _current = next;
                foreach (var (_, on, _) in _subscriptions) on(_current);
            }
        }

        private void Add(Delegate handler, Action<IReadableGameDataStore> on, Action<IReadableGameDataStore> off)
        {
            lock (_gate)
            {
                _subscriptions.Add((handler, on, off));
                on(_current);
            }
        }

        private void Forget(Delegate handler)
        {
            lock (_gate)
            {
                int i = _subscriptions.FindIndex(s => s.Handler.Equals(handler));
                if (i < 0) return;
                _subscriptions[i].Off(_current);
                _subscriptions.RemoveAt(i);
            }
        }

        public bool IsTypeAssigned<T>() => Current.IsTypeAssigned<T>();
        public T GetValue<T>(DataReference reference) => Current.GetValue<T>(reference);
        public string GetValueAsJson<T>(DataReference reference) => Current.GetValueAsJson<T>(reference);
        public IEnumerable<T> GetAllValues<T>() => Current.GetAllValues<T>();
        public IEnumerable<DataReference> GetAllDataReferences<T>() => Current.GetAllDataReferences<T>();
        public JsonSerializerSettings GetJsonSettings() => Current.GetJsonSettings();
        public List<ReferenceJsonValuePair> GetAllDataReferencesAsJson() => Current.GetAllDataReferencesAsJson();
        public bool IsValid(DataReference reference, out EInvalidReason failReason) => Current.IsValid(reference, out failReason);

        // A replay is never written to, so nothing is ever added, updated or removed through these.
#pragma warning disable CS0067
        public event Action<DataReference, string>? OnDataAddedAsJson;
        public event Action<DataReference, string>? OnDataUpdatedAsJson;
        public event Action<DataReference>? OnDataRemoved;
#pragma warning restore CS0067

        public void SubscribeToOnCreated<T>(Action<DataReference, T> onCreated) =>
            Add(onCreated, s => s.SubscribeToOnCreated(onCreated), s => s.UnsubscribeFromOnCreated(onCreated));

        public void UnsubscribeFromOnCreated<T>(Action<DataReference, T> onCreated) => Forget(onCreated);

        public void SubscribeToOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated) =>
            Add(onUpdated, s => s.SubscribeToOnAnyUpdatedOfType(onUpdated), s => s.UnsubscribeFromOnAnyUpdatedOfType(onUpdated));

        public void UnsubscribeFromOnAnyUpdatedOfType<T>(Action<DataReference, T> onUpdated) => Forget(onUpdated);

        public void SubscribeToOnRemoved<T>(Action<DataReference, T> onRemoved) =>
            Add(onRemoved, s => s.SubscribeToOnRemoved(onRemoved), s => s.UnsubscribeFromOnRemoved(onRemoved));

        public void UnsubscribeFromOnRemoved<T>(Action<DataReference, T> onRemoved) => Forget(onRemoved);
    }
}
