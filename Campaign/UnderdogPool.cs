using FDG.Players;

namespace FDG.Campaign
{
    /// <summary>
    /// #434 - this mission's Underdog Bonus points, per army.
    ///
    /// <para>
    /// Campaign Rules v3.6.0, worded identically in all four books: <i>"Any army that has a point value
    /// lower than at least one other gets an underdog bonus. You get 1 underdog point for every 65pts
    /// difference between your army and the one with the highest value. Each underdog point can be spent
    /// to modify all dice in a friendly or enemy roll by +1 or -1, but only one point can be spent per
    /// roll."</i> The rate is 65 in Grimdark Future, 25 in Firefight, 50 in Age of Fantasy and 20 in
    /// Skirmish - each exactly a fifth of that book's mission cap, so the widest legal gap between two
    /// armies is always worth exactly five underdog points.
    /// </para>
    ///
    /// <para>
    /// <b>Campaign only.</b> The core rules ask for <i>"two armies of equal point cost"</i> and have no
    /// underdog rule at all, so a standard game has an empty pool and nothing ever offers a spend. That
    /// is why the seeding call lives on the campaign's mission-setup path rather than in game start-up.
    /// </para>
    ///
    /// <para>
    /// This type owns the arithmetic's *application*; the arithmetic itself is
    /// <see cref="CampaignPreGame.UnderdogPoints"/>, which was built and tested when the rest of mission
    /// setup was. Spending is <c>UnderdogSpend</c>, which the roll stages call.
    /// </para>
    /// </summary>
    public static class UnderdogPool
    {
        /// <summary>
        /// Works out every army's underdog points for this mission and stores them.
        ///
        /// <para>
        /// <paramref name="entrants"/> must be the forces as brought to THIS mission, paired with the
        /// seat that brings each. House rule, recorded 2026-11-01: the books' <i>"the army with the
        /// lowest points"</i> is read as the points brought to the match rather than an army sheet's
        /// total value. The deciding evidence is that cap / rate is exactly 5 in all four books, so the
        /// widest legal gap is always exactly five underdog points - which only holds if the cap and the
        /// bonus measure the same thing.
        /// </para>
        /// </summary>
        /// <param name="entrants">
        /// Every force in this mission, each with the seat that brings it and whether that seat is the
        /// bot's.
        /// </param>
        public static void Seed(IGameContext gameContext,
            IReadOnlyList<(int SlotId, CampaignEntrant Entrant, bool IsBot)> entrants,
            CampaignNumbers numbers)
        {
            // Every force counts towards who the underdog IS - the comparison is against the largest
            // force on the table, bot or not.
            var armies = new List<CampaignEntrant>(entrants.Count);
            foreach ((int _, CampaignEntrant entrant, bool _) in entrants) armies.Add(entrant);

            var pool = new Dictionary<int, int>(entrants.Count);
            foreach ((int slotId, CampaignEntrant entrant, bool isBot) in entrants)
            {
                // House rule (Kai, 2026-10-30): underdog points go to the PLAYER only, never a bot. The
                // books give them to any army worth less than another, so this is a deliberate deviation
                // - a bot cannot judge which roll is worth a point, and silently handing it five free
                // +1s would make a mission harder in a way the player could not see or plan around.
                //
                // Enforced HERE, at seeding, rather than at the prompt: a bot with no pool has nothing
                // to spend, so no roll stage needs to know what a bot is and no future caller can
                // reintroduce the behaviour by forgetting a check.
                if (isBot) continue;

                int points = CampaignPreGame.UnderdogPoints(entrant.Points, armies, numbers);
                if (points > 0) pool[slotId] = points;
            }

            GameProgressUtilities.SetUnderdogPool(gameContext.GameDataStore, pool);

            foreach ((int slotId, CampaignEntrant entrant, bool _) in entrants)
            {
                if (!pool.TryGetValue(slotId, out int points)) continue;
                gameContext.Log($"{entrant.Name} is the underdog this mission: {points} underdog "
                    + $"point(s) (1 per {numbers.UnderdogPer} pts behind the largest force). Each may "
                    + "shift every die in one roll by 1, yours or the enemy's.");
            }
        }

        /// <summary>
        /// #435 - the seeding call a real game makes: works out the pool from the seats themselves, at
        /// launch, with nothing for a caller to pass wrongly or forget.
        ///
        /// <para>
        /// <b>Why here and not on the campaign screen.</b> The screen knows the two forces, but not the
        /// slot ids they end up in - those are assigned in the lobby. Seeding from the slots closes that
        /// gap and, more usefully, makes the pool a property of what is actually on the table rather than
        /// of what a screen intended. #434 shipped without this call and the Bonus did nothing in play as
        /// a result; a seeding step that lives on the launch path cannot be left unwired again.
        /// </para>
        ///
        /// <para>
        /// <b>Which seats are bots is read from the controller, not from the army file.</b>
        /// <c>StrikeTeamInfo.IsBot</c> says what the campaign screen thinks; the controller says who is
        /// actually playing the seat. Those can differ - a campaign army opened hot-seat by a second
        /// human, or a player's army handed to a bot - and the house rule is about whether a human can
        /// judge when to spend a point, which only the controller answers.
        /// </para>
        ///
        /// <para>
        /// <b>Campaign games only</b>, and only the four systems with a Campaign Rules v3.6.0 book of
        /// their own. A standard game has no campaign sheets on its armies, so this leaves the pool empty
        /// and every roll stage's guard costs one dictionary scan. The two Quest campaigns are a different
        /// book with no underdog rule and are excluded by
        /// <see cref="CampaignNumbers.HasCampaignBook"/> rather than by falling through to a parent
        /// system's numbers.
        /// </para>
        ///
        /// <para>
        /// <b>Fresh launches only.</b> On a resume the slots' army files are vestigial (the armies live in
        /// the loaded store) and the pool - including whatever has already been spent - comes back with
        /// the progress record, so re-seeding would refund the mission's spending.
        /// </para>
        /// </summary>
        public static void SeedFromSlots(IGameContext gameContext, IReadOnlyList<PlayerSlot> slots,
            EGameSystem system)
        {
            if (!CampaignNumbers.HasCampaignBook(system)) return;

            var entrants = new List<(int SlotId, CampaignEntrant Entrant, bool IsBot)>();
            foreach (PlayerSlot slot in slots)
            {
                if (!slot.IsFilled) continue;
                if (slot.ArmyListFile is not { StrikeTeam: { } team } army) continue;

                // The points BROUGHT: the mission file the campaign screen writes holds only the units
                // coming to this mission, so its total is the force on the table (the house rule recorded
                // on Seed below).
                string name = team.Name.Length > 0 ? team.Name : army.Name;
                bool isBot = slot.Controller is ComputerPlayerController;
                entrants.Add((slot.SlotID, new CampaignEntrant(name, army.TotalPoints, false), isBot));
            }

            // One campaign army is not a campaign mission: the Bonus compares armies, and with nothing to
            // compare against the arithmetic gives zero anyway. Returning early keeps the log quiet.
            if (entrants.Count < 2) return;

            Seed(gameContext, entrants, CampaignNumbers.For(system));
        }

        /// <summary>Underdog points <paramref name="player"/> has left this mission; 0 when it has none.</summary>
        public static int Remaining(IGameContext gameContext, PlayerID player)
        {
            int? slot = SlotOf(gameContext, player);
            if (slot == null) return 0;
            return GameProgressUtilities.UnderdogPool(gameContext.GameDataStore)
                .TryGetValue(slot.Value, out int left) ? left : 0;
        }

        /// <summary>
        /// Spends one of <paramref name="player"/>'s points, or returns false and changes nothing when it
        /// has none. The caller is responsible for the book's "only one point per roll" - this will
        /// happily spend a second if asked, because the cap belongs to the roll, not to the pool.
        /// </summary>
        public static bool TrySpend(IGameContext gameContext, PlayerID player)
        {
            int? slot = SlotOf(gameContext, player);
            return slot != null
                && GameProgressUtilities.TrySpendUnderdogPoint(gameContext.GameDataStore, slot.Value);
        }

        /// <summary>Whether anybody at this table has a point left - the cheap guard before any prompt.</summary>
        public static bool AnyRemaining(IGameContext gameContext)
        {
            foreach ((int _, int left) in GameProgressUtilities.UnderdogPool(gameContext.GameDataStore))
            {
                if (left > 0) return true;
            }
            return false;
        }

        private static int? SlotOf(IGameContext gameContext, PlayerID player)
        {
            foreach (IPlayerSlotInfo slot in gameContext.TableState.Players.Objects)
            {
                if (slot.PlayerID.Equals(player)) return slot.SlotID;
            }
            return null;
        }
    }
}
