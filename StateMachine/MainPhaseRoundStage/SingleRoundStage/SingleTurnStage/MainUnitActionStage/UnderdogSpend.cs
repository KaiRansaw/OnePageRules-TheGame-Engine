using System.Collections.Generic;
using System.Threading.Tasks;
using FDG.Campaign;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #434 - offers the underdog player a point to spend on the roll about to happen, and returns the
    /// modifier the roll stage folds in.
    ///
    /// <para>
    /// Campaign Rules v3.6.0: <i>"Each underdog point can be spent to modify all dice in a friendly or
    /// enemy roll by +1 or -1, but only one point can be spent per roll."</i> So a point is worth one
    /// step on one roll, and which direction follows from whose roll it is: <b>+1</b> on the underdog's
    /// own roll, <b>-1</b> on an enemy's. Both come back as a delta to the <i>rolling</i> unit's roll,
    /// which is the sign convention every roll stage already uses for granted modifiers.
    /// </para>
    ///
    /// <para>
    /// Modelled on <see cref="TargetMarkerSpend"/>, deliberately and closely: the same shape of call from
    /// inside a roll stage just before the threshold is computed, the same skip-when-there-is-nothing-to
    /// -spend, the same ordering of options so the CLI's EOF default and the AI's first-option fallback
    /// both land somewhere sensible, and the same return-a-net-the-stage-folds-in. A second mechanism for
    /// "ask the owner whether to spend a resource on this roll" would be a second thing to keep right.
    /// </para>
    ///
    /// <para>
    /// <b>Only one point per roll, and that cap lives here</b> rather than in
    /// <see cref="UnderdogPool"/>: the pool knows how many a seat has, the roll knows how many it may
    /// take, and this method is the only thing that sees both. It returns at most one step in either
    /// direction, whatever the pool holds.
    /// </para>
    ///
    /// <para>
    /// <b>Bots never get asked</b>, because a bot never has a pool - the house rule is enforced in
    /// <see cref="UnderdogPool.Seed"/>. Nothing here needs to know what a bot is.
    /// </para>
    ///
    /// <para>
    /// <b>No underdog points means no prompt and no cost.</b> A standard game has an empty pool, because
    /// the core rules have no underdog rule at all. Two things enforce that: the
    /// <see cref="UnderdogPool.AnyRemaining"/> call at the top, which is a <b>fast path</b> so the common
    /// case costs one dictionary scan rather than a walk over every seat, and the per-seat check inside
    /// the loop, which is the actual correctness. Deleting the fast path changes nothing observable -
    /// verified by sabotage - so do not "fix" a test by leaning on it.
    /// </para>
    /// </summary>
    public static class UnderdogSpend
    {
        /// <summary>
        /// Asks the underdog player whether to spend a point on this roll, and returns the delta to apply
        /// to <paramref name="rollingUnit"/>'s roll: +1 if its own owner spent, -1 if an opponent did, 0
        /// if nobody did.
        /// </summary>
        /// <param name="roll">Which roll is about to be made, for the prompt's wording.</param>
        /// <param name="rollingUnit">The unit making the roll - its owner decides whether this is a
        /// friendly or an enemy roll for each seat holding points.</param>
        public static async Task<int> ConsumeNet(IGameContext gameContext, ERollKind roll, IUnit rollingUnit)
        {
            // The cheap guard first: in a standard game, and in a campaign mission where the player is
            // not behind or has spent everything, this is the whole cost of calling us.
            if (rollingUnit == null || !UnderdogPool.AnyRemaining(gameContext)) return 0;

            PlayerID roller = rollingUnit.PlayerID;

            foreach (IPlayerSlotInfo slot in gameContext.TableState.Players.Objects)
            {
                if (!slot.IsFilled) continue;

                int left = UnderdogPool.Remaining(gameContext, slot.PlayerID);
                if (left <= 0) continue;

                bool ownRoll = slot.PlayerID.Equals(roller);
                int delta = ownRoll ? +1 : -1;

                if (!await Ask(gameContext, slot.PlayerID, roll, rollingUnit, left, ownRoll)) continue;
                if (!UnderdogPool.TrySpend(gameContext, slot.PlayerID)) continue;

                gameContext.Log($"Underdog point spent: {Describe(roll)} "
                    + $"{(delta > 0 ? "+1" : "-1")} for {rollingUnit.Name} "
                    + $"({left - 1} point(s) left).");

                // One point per roll: stop at the first spend, whoever made it and however many are left.
                return delta;
            }

            return 0;
        }

        private static async Task<bool> Ask(IGameContext gameContext, PlayerID spender, ERollKind roll,
            IUnit rollingUnit, int left, bool ownRoll)
        {
            string effect = ownRoll
                ? $"+1 to {rollingUnit.Name}'s {Describe(roll)}"
                : $"-1 to {rollingUnit.Name}'s {Describe(roll)}";

            // "Don't spend" first: an underdog point is scarce and the automated paths (CLI EOF, AI
            // fallback, an unattended run) must not burn the player's five points on the first five rolls
            // of the game. The opposite of TargetMarkerSpend's aggressive default, and for that reason -
            // a marker is use-it-or-lose-it, an underdog point is a decision worth keeping.
            var options = new List<string> { "Don't spend", $"Spend 1 underdog point: {effect}" };

            StringSelectionRequest request = new StringSelectionRequest(spender,
                $"{rollingUnit.Name} is about to make a {Describe(roll)}. "
                + $"Spend an underdog point? ({left} left)",
                options, new List<StringSelectionRequest.InvalidOption>(),
                displayName: "Underdog Bonus");

            string choice = await gameContext.PlayerRequester
                .RequestDecision<StringSelectionRequest, string>(request);

            return choice == options[1];
        }

        /// <summary>The roll in the player's words. Game text, so ASCII only (CLAUDE.md).</summary>
        private static string Describe(ERollKind roll) => roll switch
        {
            ERollKind.Hit => "hit roll",
            ERollKind.Save => "save roll",
            ERollKind.Morale => "morale test",
            ERollKind.Cast => "casting roll",
            _ => "roll",
        };
    }
}
