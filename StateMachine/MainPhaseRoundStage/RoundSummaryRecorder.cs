using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using FDG.Data;
using FDG.Players;

namespace FDG.Stages
{
    /// <summary>
    /// Builds a <see cref="RoundSummary"/>: a snapshot when the round starts, compared with the table when it
    /// ends. The start snapshot lives in memory on the host only - it is a by-product of watching the round,
    /// not game state - so a game resumed mid-round reports that round's losses as unknown rather than
    /// guessing; every later round is complete.
    /// </summary>
    public static class RoundSummaryRecorder
    {
        private sealed class Snapshot
        {
            public int Round;
            public HashSet<DataReference> Alive = new();
            public Dictionary<int, int> Objectives = new();
            public Dictionary<int, int> Vp = new();
        }

        private static readonly ConditionalWeakTable<IReadableGameDataStore, Snapshot> s_start = new();

        /// <summary>Called as a round starts (after any start-of-round arrivals).</summary>
        public static void Begin(IGameContext game, int round)
        {
            var snap = new Snapshot { Round = round };
            IReadableGameDataStore store = game.GameDataStore;
            foreach (DataReference r in store.GetAllDataReferences<UnitData>())
                if (store.GetValue<UnitData>(r).GetIsAlive()) snap.Alive.Add(r);
            foreach (IPlayerSlotInfo slot in Slots(game))
            {
                snap.Objectives[slot.SlotID] = ObjectivesHeld(game, slot);
                snap.Vp[slot.SlotID] = VpOf(game, slot) ?? 0;
            }
            s_start.AddOrUpdate(store, snap);
        }

        /// <summary>Called once the round's objectives and mode scoring are settled; stores the summary.</summary>
        public static RoundSummary Finish(IGameContext game, int round)
        {
            IReadableGameDataStore store = game.GameDataStore;
            s_start.TryGetValue(store, out Snapshot? snap);
            if (snap != null && snap.Round != round) snap = null;

            var summary = new RoundSummary { Round = round };
            foreach (IPlayerSlotInfo slot in Slots(game))
            {
                int held = ObjectivesHeld(game, slot);
                int? vp = VpOf(game, slot);
                var side = new RoundSideSummary
                {
                    SlotId = slot.SlotID,
                    Name = slot.Name,
                    ObjectivesHeld = held,
                    ObjectivesChange = snap == null ? 0 : held - snap.Objectives.GetValueOrDefault(slot.SlotID),
                    Vp = vp,
                    VpChange = snap == null || vp == null ? 0 : vp.Value - snap.Vp.GetValueOrDefault(slot.SlotID),
                    UnitsLost = snap == null ? null : new List<string>(),
                };
                if (snap != null)
                    foreach (DataReference r in snap.Alive)
                    {
                        UnitData unit = store.GetValue<UnitData>(r);
                        if (unit.PlayerID.Equals(slot.PlayerID) && !unit.GetIsAlive()) side.UnitsLost!.Add(unit.Name);
                    }
                summary.Sides.Add(side);
            }

            GameProgressUtilities.AddRoundSummary(game.GameDataStore, summary);
            return summary;
        }

        private static IEnumerable<IPlayerSlotInfo> Slots(IGameContext game) =>
            game.TableState.Players.Objects.Where(s => s.IsFilled).OrderBy(s => s.SlotID);

        private static int ObjectivesHeld(IGameContext game, IPlayerSlotInfo slot) =>
            game.TableState.Objectives.Objects.Count(o => o.OwnerID is { } owner && owner.Equals(slot.PlayerID));

        private static int? VpOf(IGameContext game, IPlayerSlotInfo slot)
        {
            FDG.Missions.MissionCardState? cards = GameProgressUtilities.MissionCards(game.GameDataStore);
            if (cards == null) return null;
            return cards.Vp.TryGetValue(slot.SlotID, out int v) ? v : 0;
        }
    }
}
