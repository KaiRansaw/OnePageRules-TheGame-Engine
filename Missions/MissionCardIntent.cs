using FDG.Players;
using FDG.Utilities;

namespace FDG.Missions
{
    /// <summary>
    /// #462 - what one side's face-up mission cards ask it to do, in the terms an AI plays in: which objectives
    /// are worth more, which enemies to kill, whether to shoot or fight, where to stand.
    ///
    /// <para>
    /// Kai, 2026-11-15: bots now play towards their cards (they used to only score them). The official AI -
    /// Solo &amp; Co-Op Rules v3.6.0 - says nothing about mission cards; the card games are a separate book. So
    /// the AI keeps its own decision tree and the cards only break the choices the tree leaves open (which
    /// objective, which target, shoot or charge). House rule C (mission cards), "Bots and their cards".
    /// </para>
    /// <para>
    /// Read from the game's own progress record, so it is the same on every machine. Only the side's OWN cards
    /// (plus Total Domination's shared ones) - never an opponent's hidden card.
    /// </para>
    /// </summary>
    public sealed class MissionCardIntent
    {
        /// <summary>The side's slot.</summary>
        public int Slot { get; private init; }

        /// <summary>Objective number (1-based, table order) -> the most VP a card pays for controlling it.</summary>
        public Dictionary<int, int> ObjectiveVp { get; } = new();

        /// <summary>Fire at Will: kills by shooting score.</summary>
        public bool WantsShootingKills { get; private set; }
        /// <summary>In the Trenches / Show No Mercy: melee scores.</summary>
        public bool WantsMelee { get; private set; }
        /// <summary>Infiltrate: units inside the enemy's deployment zone.</summary>
        public bool WantsInfiltrate { get; private set; }

        public bool WantsHero { get; private set; }
        public bool WantsCaster { get; private set; }
        public bool WantsFlying { get; private set; }
        /// <summary>Giant Slayer: the lowest Tough value that pays (0 = not on a card).</summary>
        public int WantsToughAtLeast { get; private set; }
        /// <summary>Decapitate: the enemy's highest-cost Hero model ids still worth killing.</summary>
        public HashSet<string> DecapitateModels { get; } = new();

        /// <summary>The cards this reads, for the log and tests.</summary>
        public IReadOnlyList<int> Cards { get; private init; } = Array.Empty<int>();

        public bool WantsKills => WantsHero || WantsCaster || WantsFlying || WantsToughAtLeast > 0 || DecapitateModels.Count > 0;

        public bool Any => ObjectiveVp.Count > 0 || WantsKills || WantsShootingKills || WantsMelee || WantsInfiltrate;

        /// <summary>
        /// How much further a unit will walk for a card's objective than for the closest one: 9" per VP. A
        /// 1-VP Seize beats a plain objective up to 9" nearer, a 2-VP Hold up to 18" - far enough to choose the
        /// card's marker on its own side of the table, not so far the whole army marches across it.
        /// </summary>
        public const float PullInchesPerVp = 9f;

        /// <summary>Tests only: this slot plays as if its cards weren't there, to measure what playing to them is worth.</summary>
        internal static int? IgnoreSlot;

        /// <summary>
        /// The cards in play for <paramref name="me"/>, or null when the game has no mission cards, the player
        /// has no seat, or nothing on its cards changes how it plays.
        /// </summary>
        public static MissionCardIntent? For(ITableState table, PlayerID me)
        {
            MissionCardState? state;
            EGameSystem system;
            try
            {
                state = GameProgressUtilities.MissionCards(table.DataStore);
                system = table.Progress.GameSystem;
            }
            catch (Exception) { return null; }
            if (state == null || state.Game == EMissionCardGame.Off) return null;

            int slot = SlotOf(table, me);
            if (slot < 0 || slot == IgnoreSlot) return null;

            List<int> cards = state.Game == EMissionCardGame.TotalDomination
                ? state.SharedTable.ToList()
                : state.Sides.TryGetValue(slot, out MissionCardSide? side) ? side.Table.ToList() : new List<int>();
            if (cards.Count == 0) return null;

            int prioritySlot = state.Game == EMissionCardGame.TotalDomination ? -1 : slot;
            var intent = new MissionCardIntent { Slot = slot, Cards = cards };
            int objectives = table.Objectives.Objects.Count();
            foreach (int card in cards) intent.Read(card, state, prioritySlot, objectives, system, table, me);
            return intent.Any ? intent : null;
        }

        private void Want(int objective, int vp)
        {
            if (objective < 1) return;
            ObjectiveVp[objective] = Math.Max(vp, ObjectiveVp.TryGetValue(objective, out int had) ? had : 0);
        }

        private void Read(int card, MissionCardState state, int prioritySlot, int objectives, EGameSystem system,
                          ITableState table, PlayerID me)
        {
            switch (card)
            {
                case >= 11 and <= 16: Want(card - 10, 1); return;
                case >= 21 and <= 26: Want(card - 20, 1); return;
                case >= 31 and <= 36: Want(card - 30, 2); return;
                case 44:
                    if (state.Priority.TryGetValue(MissionCardState.PriorityKey(prioritySlot, 44), out int rolled)) Want(rolled, 1);
                    return;
                // Tactical Precision / Perfection: any marker helps a little - a small pull on all of them.
                case 45: case 46:
                    for (int i = 1; i <= objectives; i++) Want(i, 1);
                    return;
                case 42: WantsInfiltrate = true; return;
                case 51: WantsShootingKills = true; return;
                case 52: case 66: WantsMelee = true; return;
                case 62: WantsCaster = true; return;
                case 63: WantsFlying = true; return;
                case 64: WantsHero = true; return;
                case 65: WantsToughAtLeast = MissionCardCatalog.GiantSlayerTough(system); return;
                case 61:
                    foreach ((int enemySlot, List<string> heroes) in state.TopHeroes)
                    {
                        if (enemySlot == Slot || SameTeam(table, Slot, enemySlot)) continue;
                        foreach (string id in heroes) DecapitateModels.Add(id);
                    }
                    return;
            }
        }

        /// <summary>The VP a card pays for controlling this objective (0 for none).</summary>
        public int VpFor(ITableState table, IObjective objective)
        {
            int index = table.Objectives.Objects.ToList().IndexOf(objective);
            return index >= 0 && ObjectiveVp.TryGetValue(index + 1, out int vp) ? vp : 0;
        }

        /// <summary>A distance as the card-aware tree ranks objectives: card markers count as nearer.</summary>
        public float Pulled(ITableState table, IObjective objective, float distance) =>
            distance - VpFor(table, objective) * PullInchesPerVp;

        /// <summary>Whether killing this enemy unit (or a model in it) pays on one of the side's cards.</summary>
        public bool IsCardTarget(IUnit enemy)
        {
            if (!WantsKills) return false;
            if (WantsFlying && (MissionCardGame.HasRule(enemy.RuleDefinitions, "Flying")
                                || enemy.Models.Any(m => m.GetIsAlive() && MissionCardGame.HasRule(m.RuleDefinitions, "Flying"))))
                return true;
            foreach (IModel model in enemy.Models)
            {
                if (!model.GetIsAlive()) continue;
                if (WantsHero && MissionCardGame.IsHeroModel(enemy, model)) return true;
                if (WantsCaster && MissionCardGame.IsCasterModel(enemy, model)) return true;
                if (WantsToughAtLeast > 0 && MathF.Round(model.TotalWounds) >= WantsToughAtLeast) return true;
                if (DecapitateModels.Contains(model.ID.ID.ToString())) return true;
            }
            return false;
        }

        /// <summary>
        /// Infiltrate: the point a unit should head for inside an enemy deployment zone - the nearest point of
        /// the nearest enemy zone, 3" in from its edge. Null when there is no such zone or the unit is in one.
        /// </summary>
        public Position? InfiltrateGoal(ITableState table, PlayerID me, IUnit unit)
        {
            if (!WantsInfiltrate) return null;
            IReadOnlyDictionary<int, ZoneRect>? zones;
            try { zones = GameProgressUtilities.DeploymentZones(table.DataStore); }
            catch (Exception) { return null; }
            if (zones == null) return null;
            int myTeam = TeamOf(table, Slot);
            var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return null;
            Position at = living[0].Position;

            Position? best = null;
            float bestDistance = float.MaxValue;
            foreach ((int team, ZoneRect zone) in zones)
            {
                if (team == myTeam) continue;
                if (living.Any(m => zone.Contains(m.Position))) return null;
                float inset = 3f;
                float x = Math.Clamp(at.x, Math.Min(zone.Left + inset, zone.Right), Math.Max(zone.Right - inset, zone.Left));
                float z = Math.Clamp(at.z, Math.Min(zone.Bottom + inset, zone.Top), Math.Max(zone.Top - inset, zone.Bottom));
                var point = new Position(x, z);
                float d = Position.GetDistance2D(at, point);
                if (d < bestDistance) { bestDistance = d; best = point; }
            }
            return best;
        }

        internal static int SlotOf(ITableState table, PlayerID player)
        {
            foreach (IPlayerSlotInfo seat in table.Players.Objects)
                if (seat.IsFilled && seat.PlayerID.Equals(player)) return seat.SlotID;
            return -1;
        }

        private static int TeamOf(ITableState table, int slot) =>
            table.Players.Objects.FirstOrDefault(p => p.IsFilled && p.SlotID == slot)?.TeamNumber ?? int.MinValue;

        private static bool SameTeam(ITableState table, int a, int b) => TeamOf(table, a) == TeamOf(table, b);
    }
}
