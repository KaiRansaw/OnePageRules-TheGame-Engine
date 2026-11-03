using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>One army as it goes into a campaign mission: its name, what it is worth, and whether it casts.</summary>
    /// <param name="Name">The army's campaign name.</param>
    /// <param name="Points">The value of the units it is bringing to this mission.</param>
    /// <param name="HasCaster">Whether any unit it is bringing can cast, which decides if Hexer is rollable.</param>
    public readonly record struct CampaignEntrant(string Name, int Points, bool HasCaster);

    /// <summary>
    /// Step 1 of the campaign loop (Mission Setup) from Campaign Rules v3.6.0: which units an army may
    /// bring, underdog points, and the special objective each army rolls for. Plus the campaign-type end
    /// check, which belongs to the campaign rather than to any one mission.
    /// <para>
    /// Rules only - nothing here reads or writes the table. The in-game half (scoring a special
    /// objective, spending an underdog point on a roll) is the next slice. See WorkItems/410.
    /// </para>
    /// </summary>
    public static class CampaignPreGame
    {
        // -- The armies -------------------------------------------------------------------------------

        /// <summary>
        /// The most an army may bring to this mission: the lowest army's value plus the book's cap.
        /// There is no points limit in a campaign game beyond this.
        /// </summary>
        public static int MaxPointsForMission(IReadOnlyList<CampaignEntrant> armies, CampaignNumbers n)
        {
            if (armies.Count == 0) return n.ArmyCapOverLowest;
            return armies.Min(a => a.Points) + n.ArmyCapOverLowest;
        }

        /// <summary>
        /// Why this selection of units can't be taken to the mission, or null if it can. The book asks
        /// for at least one unit and no more than the cap over the lowest army.
        /// </summary>
        public static string? CannotBring(int points, IReadOnlyList<CampaignEntrant> otherArmies,
                                          CampaignNumbers n, int unitCount)
        {
            if (unitCount < 1) return "Bring at least one unit to the mission.";
            if (otherArmies.Count == 0) return null;
            int cap = otherArmies.Min(a => a.Points) + n.ArmyCapOverLowest;
            return points > cap
                ? $"That is {points} pts; the limit this mission is {cap} pts ({n.ArmyCapOverLowest} over the smallest army)."
                : null;
        }

        // -- Underdog points --------------------------------------------------------------------------

        /// <summary>
        /// Underdog points for one army: one for every <see cref="CampaignNumbers.UnderdogPer"/> points
        /// it trails the highest-valued army by. Each point may shift every die in one friendly or enemy
        /// roll by +1 or -1, and only one point may be spent per roll.
        /// <para>
        /// Deferred by Kai's call on 2026-10-30: the first campaign build is one human army against a
        /// bot, and underdog points only mean anything with two or more real armies. The arithmetic is
        /// here and tested so the rule is recorded correctly; nothing offers the spend yet.
        /// </para>
        /// </summary>
        public static int UnderdogPoints(int myPoints, IReadOnlyList<CampaignEntrant> allArmies, CampaignNumbers n)
        {
            if (allArmies.Count == 0 || n.UnderdogPer <= 0) return 0;
            int highest = allArmies.Max(a => a.Points);
            int behind = highest - myPoints;
            return behind <= 0 ? 0 : behind / n.UnderdogPer;
        }

        // -- Special objectives -----------------------------------------------------------------------

        /// <summary>
        /// Whether a special objective can be completed at all in this mission. The book says to re-roll
        /// one that can't be. Only Hexer has a real precondition - an army with no caster can never cast
        /// four spells.
        /// </summary>
        public static bool CanComplete(string objective, CampaignEntrant army) =>
            objective != "Hexer" || army.HasCaster;

        /// <summary>
        /// Rolls this army's special objective, re-rolling anything it could not complete. Worth
        /// <see cref="CampaignNumbers.SpecialObjectiveBonus"/> into its treasury if it is completed.
        /// </summary>
        public static string RollSpecialObjective(CampaignEntrant army, Func<int> d6)
        {
            for (int guard = 0; guard < 60; guard++)
            {
                (string name, _) = CampaignTables.SpecialObjectives[Math.Clamp(d6(), 1, 6) - 1];
                if (CanComplete(name, army)) return name;
            }
            // Every face unrollable is impossible (only Hexer has a precondition), but never spin.
            return CampaignTables.SpecialObjectives.First(o => CanComplete(o.Name, army)).Name;
        }

        /// <summary>The book's wording for a special objective, for the Mission window.</summary>
        public static string ObjectiveText(string objective) =>
            CampaignTables.SpecialObjectives.FirstOrDefault(o => o.Name == objective).Text ?? "";

        /// <summary>
        /// Pays the special objective bonus into an army's treasury, and says so. Call it once, after the
        /// mission, for an army that completed the objective it rolled.
        /// </summary>
        public static string CompleteSpecialObjective(ArmyListFile army, string objective, CampaignNumbers n)
        {
            StrikeTeamInfo info = army.StrikeTeam ??= new StrikeTeamInfo { Name = army.Name };
            info.BankedPoints += n.SpecialObjectiveBonus;
            return $"{info.Name} completed its special objective ({objective}): +{n.SpecialObjectiveBonus} pts.";
        }

        // -- Campaign length --------------------------------------------------------------------------

        /// <summary>
        /// Whether the campaign has ended. Endless never does - VP just accumulate on a leaderboard,
        /// which is the type Kai chose on 2026-10-30. The other three are recognised and reported as not
        /// finished, so a campaign saved as one of them still opens instead of ending immediately; their
        /// real checks land when they are offered in the UI.
        /// </summary>
        public static bool IsOver(ECampaignType type, int missionsPlayed, int highestVp, int missionLimit, int vpLimit) =>
            type switch
            {
                ECampaignType.Endless => false,
                ECampaignType.GameLimited => missionLimit > 0 && missionsPlayed >= missionLimit,
                ECampaignType.PointLimited => vpLimit > 0 && highestVp >= vpLimit,
                ECampaignType.TimeLimited => false,  // measured in real weeks; the screen owns the date
                _ => false,
            };

        /// <summary>
        /// The D3+4 a campaign rolls once to set its length, for the three limited types. Not used by
        /// Endless.
        /// </summary>
        public static int RollCampaignLength(Func<int> d6) => (Math.Clamp(d6(), 1, 6) + 1) / 2 + 4;

        /// <summary>
        /// #445 - where a campaign stands: both armies' VP, the target if it has one, and whether it is
        /// decided. <see cref="Standing"/> builds it; nothing else should work the question out itself.
        /// </summary>
        /// <param name="Type">The campaign's end condition.</param>
        /// <param name="VpTarget">The VP needed to win, or 0 when the campaign has no target.</param>
        /// <param name="YourVp">The player's campaign VP.</param>
        /// <param name="RivalVp">The standing enemy's campaign VP, or 0 when there is no enemy yet.</param>
        /// <param name="Outcome">Who has won, or <see cref="ECampaignOutcome.InProgress"/>.</param>
        public readonly record struct CampaignStanding(
            ECampaignType Type, int VpTarget, int YourVp, int RivalVp, ECampaignOutcome Outcome)
        {
            /// <summary>True once the campaign is decided and no further mission should be played.</summary>
            public bool IsOver => Outcome != ECampaignOutcome.InProgress;

            /// <summary>
            /// True when this campaign is being raced to a VP target. False for Endless, and false for a
            /// PointLimited campaign with no target stored - which is how campaigns saved before #445
            /// read, and they must behave as Endless rather than as "already won".
            /// </summary>
            public bool HasTarget => Type == ECampaignType.PointLimited && VpTarget > 0;

            /// <summary>
            /// The line the campaign screen shows: "First to 7 VP" or "Endless". Game text, so ASCII
            /// only (CLAUDE.md).
            /// </summary>
            public string Describe() => HasTarget ? $"First to {VpTarget} VP" : "Endless";
        }

        /// <summary>
        /// Works out <see cref="CampaignStanding"/> for a campaign from its two armies' blocks.
        ///
        /// <para>
        /// The target is read from the PLAYER's block. Both blocks carry the same value (see
        /// <see cref="StrikeTeamInfo.VpTarget"/>), and reading one of them rather than reconciling the
        /// two is deliberate: a <c>Max</c> of the pair would paper over a bug that wrote them out of
        /// step instead of letting a test catch it.
        /// </para>
        ///
        /// <para>
        /// <paramref name="rival"/> may be null - a campaign started in a folder with only one army has
        /// no standing enemy (#428) - and then the rival sits on 0 VP, so only the player can reach the
        /// target. That is correct rather than a special case: there is nobody else scoring.
        /// </para>
        /// </summary>
        public static CampaignStanding Standing(StrikeTeamInfo you, StrikeTeamInfo? rival)
        {
            int yourVp = you.Vp;
            int rivalVp = rival?.Vp ?? 0;

            // Decided at all? Asked through IsOver so the end condition lives in exactly one place, with
            // the leader's VP as the figure it tests. missionLimit is 0 because the mission- and
            // week-limited types are not offered (campaign-flow-spec section 3a), so a campaign saved as
            // one of those keeps opening rather than ending on a number nothing ever set.
            bool over = IsOver(you.CampaignType, you.Matches, Math.Max(yourVp, rivalVp), 0, you.VpTarget);

            ECampaignOutcome outcome = !over
                ? ECampaignOutcome.InProgress
                : yourVp > rivalVp ? ECampaignOutcome.YouWon
                : rivalVp > yourVp ? ECampaignOutcome.RivalWon
                // Both crossed the line in the same mission on equal VP. The books have no tiebreak for
                // this, so it is a draw rather than a coin-flip dressed up as a rule.
                : ECampaignOutcome.Draw;

            return new CampaignStanding(you.CampaignType, you.VpTarget, yourVp, rivalVp, outcome);
        }
    }
}
