using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FDG.Campaign.Map
{
    /// <summary>
    /// #467 step 2 - what happens to the armies' books between map turns (Kai, 2026-11-14: "rivals run themselves").
    ///
    /// <para><b>Applying a turn.</b> A finished turn's off-table changes (claims paid, rest and Hive income, the book's
    /// rewards for rival-vs-rival rolls) go into each army's own StrikeTeamInfo - the treasury and mission VP live there,
    /// never on the map.</para>
    ///
    /// <para><b>A rival's own between-turn steps.</b> It takes the choices waiting on its unit sheets and spends its
    /// treasury the way <see cref="CampaignBot"/> always has (a Hero if it has none, then units cheapest first, never past
    /// what it could bring). The one map twist is the <b>land reserve</b>: while there is neutral land it can reach, it
    /// keeps back what claiming the next region costs, so its treasury buys land as well as units. Without that it would
    /// spend every point on bodies and only ever take land by fighting garrisons - the "free land" Kai ruled out.</para>
    ///
    /// <para>House rule B17 ("the enemy never spends its own treasury - you do") does not apply to map campaigns:
    /// with up to three rivals, doing every rival's books by hand was Kai's own reason for "rivals run themselves".</para>
    /// </summary>
    public static class CampaignMapUpkeep
    {
        /// <summary>Puts a finished turn's off-table change into an army's books. The treasury never goes below 0.</summary>
        public static void Apply(StrikeTeamInfo info, MapArmyChange change)
        {
            info.BankedPoints = Math.Max(0, info.BankedPoints + change.Treasury);
            info.Vp += change.MissionVp;
        }

        /// <summary>
        /// What a rival keeps back for land: the price of its next claim while it can reach neutral land, else nothing.
        /// Never more than it has.
        /// </summary>
        public static int LandReserve(CampaignMap map, string faction, int treasury, CampaignNumbers n)
        {
            bool landToBuy = CampaignMapRules.Reachable(map, faction).Any(r => r.Owner == null);
            return landToBuy ? Math.Min(treasury, CampaignMapRules.ClaimCost(map, faction, n)) : 0;
        }

        /// <summary>
        /// A rival's steps between map turns: resolve its sheets' choices, then spend all but its land reserve.
        /// </summary>
        /// <param name="biggestOtherArmy">The biggest other army's value on the map - <see cref="CampaignBot"/>'s roster ceiling.</param>
        /// <returns>What it did, one line each (for its history, and the turn report once the player has met it).</returns>
        public static List<string> RivalBetweenTurns(CampaignMap map, string faction, ArmyListFile army, BookFile? book,
                                                     CampaignNumbers n, int biggestOtherArmy, Func<int> d6)
        {
            var log = new List<string>();
            if (army.StrikeTeam is not { } info || map.Faction(faction) is not { Alive: true, IsPlayer: false }) return log;

            log.AddRange(CampaignBot.ResolveChoices(army, d6));
            if (book == null) return log;

            int reserve = LandReserve(map, faction, info.BankedPoints, n);
            info.BankedPoints -= reserve;
            try { log.AddRange(CampaignBot.Spend(army, book, n, biggestOtherArmy, d6)); }
            finally { info.BankedPoints += reserve; }
            if (reserve > 0) log.Add($"{info.Name} keeps {reserve} pts back to buy land.");
            info.History.AddRange(log);
            return log;
        }
    }
}
