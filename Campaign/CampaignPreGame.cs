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
    }
}
