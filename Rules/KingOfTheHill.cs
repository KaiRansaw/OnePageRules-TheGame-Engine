using System.Collections.Generic;
using System.Linq;
using FDG.Players;

namespace FDG
{
    /// <summary>
    /// King of the Hill, from the Advanced Rulebooks 3.6.0 (Multiplayer Games, the same text in all four):
    ///
    /// <para>"<b>The King:</b> Throughout the game, players must keep track of the total point cost of all units
    /// in their army (including units that have not been deployed yet), and the player with the highest total
    /// counts as the king. In case of a tie, all tied players count as the king. Note that at the beginning of
    /// the game, all players are tied and all count as the king."</para>
    ///
    /// <para>"<b>Combat Bonus Points:</b> When a player picks one of the king's units as the target for an
    /// attack, they immediately gain one combat bonus point. Once per activation, players may spend one point to
    /// add +1 to the result of all dice in a single roll. Note that combat bonus points may be spent during the
    /// same activation in which they were earned."</para>
    ///
    /// <para><b>Readings</b> (claude/house-rules.md): "total point cost" counts the units still alive - a unit
    /// destroyed is no longer part of the army; "at the beginning of the game all players are tied" is then the
    /// equal-points case the book assumes, and with unequal lists the biggest army is king from the start. An
    /// "attack" is a shot or a charge: one point per target unit per shooting action, one per charge. A point
    /// is spent on one of the spender's OWN rolls (hit, save, morale, casting).</para>
    /// </summary>
    public static class KingOfTheHill
    {
        /// <summary>On, and a free-for-all of three or more sides - the format it is written for.</summary>
        public static bool IsOn(IGameContext game) =>
            game.Settings.KingOfTheHill && game.TableState.Teams.Objects.Count() >= 3;

        /// <summary>The king or kings: the seats whose living units are worth the most.</summary>
        public static HashSet<PlayerID> Kings(ITableState table)
        {
            var worth = new Dictionary<PlayerID, int>();
            foreach (IPlayerSlotInfo slot in table.Players.Objects.Where(s => s.IsFilled))
                worth[slot.PlayerID] = 0;
            foreach (IUnit unit in table.Units.Objects)
                if (unit.GetIsAlive() && unit is UnitData data && worth.ContainsKey(unit.PlayerID))
                    worth[unit.PlayerID] += data.PointCost;
            if (worth.Count == 0) return new HashSet<PlayerID>();
            int top = worth.Values.Max();
            return worth.Where(kv => kv.Value == top).Select(kv => kv.Key).ToHashSet();
        }

        /// <summary>
        /// "When a player picks one of the king's units as the target for an attack, they immediately gain one
        /// combat bonus point." Call once per target picked. A king attacking a fellow king earns too - both
        /// are kings, and the book does not except them - but nobody earns by attacking their own units.
        /// </summary>
        public static void OnTargetPicked(IGameContext game, PlayerID attacker, IUnit target)
        {
            if (!IsOn(game) || target.PlayerID.Equals(attacker)) return;
            if (!Kings(game.TableState).Contains(target.PlayerID)) return;
            int? slot = SlotOf(game, attacker);
            if (slot == null) return;

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(game.GameDataStore);
            if (progress == null) return;
            progress.CombatBonus ??= new Dictionary<int, int>();
            progress.CombatBonus[slot.Value] = progress.CombatBonus.GetValueOrDefault(slot.Value) + 1;
            GameProgressUtilities.WriteProgress(game.GameDataStore, progress);
            game.Log($"King of the Hill: {game.GetPlayerName(attacker)} targets the king's {target.Name} and gains "
                + $"a combat bonus point ({progress.CombatBonus[slot.Value]}).");
        }

        /// <summary>Points this seat holds, and whether it may still spend one this activation.</summary>
        public static (int Points, bool CanSpend) Status(IGameContext game, PlayerID player)
        {
            if (!IsOn(game)) return (0, false);
            int? slot = SlotOf(game, player);
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(game.GameDataStore);
            if (slot == null || progress?.CombatBonus == null) return (0, false);
            int points = progress.CombatBonus.GetValueOrDefault(slot.Value);
            bool spentHere = progress.CombatBonusSpentIn != null
                && progress.CombatBonusSpentIn.TryGetValue(slot.Value, out string? key) && key == ActivationKey(game);
            return (points, points > 0 && !spentHere);
        }

        /// <summary>Spends one point for this activation; false when the seat cannot.</summary>
        public static bool TrySpend(IGameContext game, PlayerID player)
        {
            if (!Status(game, player).CanSpend) return false;
            int slot = SlotOf(game, player)!.Value;
            GameProgressData progress = GameProgressUtilities.TryGetProgress(game.GameDataStore)!;
            progress.CombatBonus![slot] -= 1;
            progress.CombatBonusSpentIn ??= new Dictionary<int, string>();
            progress.CombatBonusSpentIn[slot] = ActivationKey(game);
            GameProgressUtilities.WriteProgress(game.GameDataStore, progress);
            return true;
        }

        private static string ActivationKey(IGameContext game) =>
            $"{game.TableState.Progress.RoundCount}:{(game.TableState.Progress.ActivatingUnit as UnitData)?.ID}";

        private static int? SlotOf(IGameContext game, PlayerID player)
        {
            foreach (IPlayerSlotInfo slot in game.TableState.Players.Objects)
                if (slot.PlayerID.Equals(player)) return slot.SlotID;
            return null;
        }
    }
}
