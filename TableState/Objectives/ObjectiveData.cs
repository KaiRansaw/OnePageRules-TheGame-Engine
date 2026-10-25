using FDG.Data;
using Newtonsoft.Json;

namespace FDG
{
    public interface IObjective
    {
        Position Position { get; }
        PlayerID? OwnerID { get; }
        void SetOwner(PlayerID? ownerID);

        /// <summary>Horde Mode: the marker's fixed 1-5 number, or null outside Horde Mode.</summary>
        int? HordeNumber => null;

        /// <summary>Horde Mode: damage markers on this objective (three destroy it). 0 outside Horde Mode.</summary>
        int HordeDamage => 0;

        /// <summary>Horde Mode: whether this is the current priority objective.</summary>
        bool IsHordePriority => false;
    }

    public class ObjectiveData : IObjective
    {
        // Guid.Empty stored in the binding means "no owner".
        private static readonly PlayerID Unowned = new PlayerID(Guid.Empty);

        public Position Position { get; }

        public DataBinding<PlayerID> OwnerIDBinding;

        /// <summary>Horde Mode marker number (1-5). Null for a standard-game objective (and in every
        /// pre-Horde save, where the field is absent).</summary>
        public int? HordeNumber { get; }

        /// <summary>Horde Mode damage-marker count, in the store so it replicates to every player and
        /// rides the save. Null outside Horde Mode.</summary>
        public DataBinding<int>? HordeDamageBinding;

        /// <summary>Horde Mode priority flag (1 = priority), replicated and saved like the damage.</summary>
        public DataBinding<int>? HordePriorityBinding;

        public PlayerID? OwnerID
        {
            get
            {
                var id = OwnerIDBinding.GetValue();
                return id.ID == Guid.Empty ? null : id;
            }
        }

        [JsonIgnore]
        public int HordeDamage => HordeDamageBinding?.GetValue() ?? 0;

        [JsonIgnore]
        public bool IsHordePriority => (HordePriorityBinding?.GetValue() ?? 0) != 0;

        [JsonConstructor]
        public ObjectiveData(Position position, DataBinding<PlayerID> ownerIDBinding, int? hordeNumber = null,
            DataBinding<int>? hordeDamageBinding = null, DataBinding<int>? hordePriorityBinding = null)
        {
            Position = position;
            OwnerIDBinding = ownerIDBinding;
            HordeNumber = hordeNumber;
            HordeDamageBinding = hordeDamageBinding;
            HordePriorityBinding = hordePriorityBinding;
        }

        public ObjectiveData(Position position, IReadWriteableGameDataStore gameDataStore)
        {
            Position = position;
            DataReference ownerRef = gameDataStore.Create(Unowned);
            OwnerIDBinding = gameDataStore.GetDataBinding<PlayerID>(ownerRef);
        }

        /// <summary>
        /// A Horde Mode objective: numbered, with its damage and priority state created in the store
        /// BEFORE the objective itself is, so the references resolve when it replicates or loads.
        /// </summary>
        public ObjectiveData(Position position, IReadWriteableGameDataStore gameDataStore, int hordeNumber)
            : this(position, gameDataStore)
        {
            HordeNumber = hordeNumber;
            HordeDamageBinding = gameDataStore.GetDataBinding<int>(gameDataStore.Create(0));
            HordePriorityBinding = gameDataStore.GetDataBinding<int>(gameDataStore.Create(0));
        }

        public void SetOwner(PlayerID? ownerID)
        {
            OwnerIDBinding.SetValue(ownerID ?? Unowned);
        }

        /// <summary>Horde Mode: sets the damage-marker count. No-op on a standard-game objective.</summary>
        public void SetHordeDamage(int damage) => HordeDamageBinding?.SetValue(damage);

        /// <summary>Horde Mode: marks or clears this as the priority objective. No-op outside Horde Mode.</summary>
        public void SetHordePriority(bool isPriority) => HordePriorityBinding?.SetValue(isPriority ? 1 : 0);
    }
}
