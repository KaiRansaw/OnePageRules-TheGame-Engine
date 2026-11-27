using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FDG.Ai
{
    /// <summary>
    /// Advanced Casting - Magic Pools, for the bots (lane 2, 2026-11-27). The book: "players may take any number of spell
    /// dice from their magic pool, and roll them at the same time as the caster ... If the total blocking result is equal
    /// or higher than the casting result, then the spell is blocked ... if any of the casting dice has an unmodified result
    /// of 6, then the spell can't be blocked." The Solo &amp; Co-Op books predate the chapter, so this is the bots' own
    /// policy: block with the fewest dice that give an even chance against a blockable cast, keeping two dice for each of the bot's own
    /// casters still to act this round. Before, bots never blocked.
    /// </summary>
    public static class PoolBlockPolicy
    {
        /// <summary>Chance the block works: no casting 6, and the blocking dice total at least the casting dice total.</summary>
        public static double BlockChance(int castingDice, int blockingDice)
        {
            if (castingDice <= 0 || blockingDice <= 0) return 0;
            // Casting dice that show no 6: each 1..5. Distribution of their sum, weighted by (1/6)^n.
            double[] cast = SumDistribution(castingDice, 5);
            double[] block = SumDistribution(blockingDice, 6);
            // P(block total >= s) for every s.
            var atLeast = new double[block.Length + 1];
            for (int s = block.Length - 1; s >= 0; s--) atLeast[s] = atLeast[s + 1] + block[s];
            double p = 0;
            for (int s = 0; s < cast.Length; s++)
                if (cast[s] > 0) p += cast[s] * (s < atLeast.Length ? atLeast[s] : 0);
            return p;
        }

        /// <summary>
        /// Dice to block with: the fewest that give an even chance against a cast that CAN be blocked (no casting 6 - a
        /// big cast is usually unblockable whatever is thrown at it, so the bar is half of that chance), from what is
        /// left after keeping two per own caster still to act; none when that many can't reach it.
        /// </summary>
        public static int DiceFor(int castingDice, int pool, int keep)
        {
            int usable = Math.Max(0, pool - Math.Max(0, keep));
            double bar = 0.5 * Math.Pow(5 / 6.0, Math.Max(1, castingDice));
            for (int k = 1; k <= usable; k++)
                if (BlockChance(castingDice, k) >= bar) return k;
            return 0;
        }

        /// <summary>The bot's answer to a pool block request.</summary>
        public static CastAssistReply Reply(ITableState table, CastAssistRequest request)
        {
            if (!request.PoolBlock || request.IsFriendly || request.AvailableTokens <= 0) return CastAssistReply.None;
            PlayerID me = request.TargetPlayerID;
            int ownCastersToAct = table.Units.Objects.Count(u => u.PlayerID.Equals(me) && u.GetIsAlive() && u.GetIsOnBattlefield()
                && !u.Tokens.HasToken(Rules.Foundation.TokenType.ActivatedThisRound) && IsCaster(table, u));
            int dice = DiceFor(Math.Max(1, request.CastingDice), request.AvailableTokens, 2 * ownCastersToAct);
            return dice > 0 ? new CastAssistReply(dice) : CastAssistReply.None;
        }

        /// <summary>Magic Pools casting: chance that <paramref name="dice"/> dice roll at least <paramref name="needed"/>
        /// successes, each die succeeding on <paramref name="perDie"/>+ (a 6 always, a 1 never).</summary>
        public static double CastChance(int dice, int needed, int perDie = 4)
        {
            if (needed <= 0) return 1;
            if (dice < needed) return 0;
            double p = Math.Clamp((7 - perDie) / 6.0, 1 / 6.0, 5 / 6.0);
            double total = 0;
            for (int k = needed; k <= dice; k++) total += Binomial(dice, k) * Math.Pow(p, k) * Math.Pow(1 - p, dice - k);
            return total;
        }

        /// <summary>Chance of a backlash: any die shows a 1.</summary>
        public static double BacklashChance(int dice) => dice <= 0 ? 0 : 1 - Math.Pow(5 / 6.0, dice);

        /// <summary>
        /// Dice for a bot's own pool cast: the spell's value at least, and extra dice while each one buys more chance to
        /// cast than half its added backlash risk (a backlash ends the caster's casting for the round and wounds it).
        /// Never more than the pool, and never more than three over the value.
        /// </summary>
        public static int CastDiceFor(int value, int pool, int perDie = 4)
        {
            if (pool < value) return value;
            int best = value;
            double bestScore = CastChance(value, value, perDie) - 0.5 * BacklashChance(value);
            for (int d = value + 1; d <= Math.Min(pool, value + 3); d++)
            {
                double score = CastChance(d, value, perDie) - 0.5 * BacklashChance(d);
                if (score > bestScore + 1e-9) { best = d; bestScore = score; }
            }
            return best;
        }

        /// <summary>2D6 Casting: chance that two dice reach <paramref name="needed"/> (12 always succeeds, 2 always fails).</summary>
        public static double TwoD6Chance(int needed)
        {
            int ways = 0;
            for (int a = 1; a <= 6; a++)
                for (int b = 1; b <= 6; b++)
                    if (a + b == 12 || (a + b != 2 && a + b >= needed)) ways++;
            return ways / 36.0;
        }

        private static double Binomial(int n, int k)
        {
            double r = 1;
            for (int i = 1; i <= k; i++) r = r * (n - k + i) / i;
            return r;
        }

        private static bool IsCaster(ITableState table, IUnit unit)
        {
            try { return CapabilityRuleQueries.CanCast(unit, ReadOnlyRuleEvaluators.For(table)); }
            catch (InvalidOperationException) { return false; }
        }

        // Probability of each total for n dice with faces 1..faces, each face weighted 1/6 (so the totals of a "no 6"
        // roll carry the (5/6)^n chance of having no 6 at all).
        private static double[] SumDistribution(int n, int faces)
        {
            var dist = new double[] { 1.0 };
            for (int i = 0; i < n; i++)
            {
                var next = new double[dist.Length + faces];
                for (int s = 0; s < dist.Length; s++)
                    for (int f = 1; f <= faces; f++) next[s + f] += dist[s] / 6.0;
                dist = next;
            }
            return dist;
        }
    }
}
