namespace FDG.Campaign
{
    /// <summary>
    /// #435 - how hard the rival's force is sized for the next campaign mission.
    ///
    /// <para>
    /// <b>This is a house rule.</b> Campaign Rules v3.6.0 has no difficulty setting at all: it has one
    /// mission cap and expects a human on the other side of the table to decide what to bring. With a bot
    /// rival somebody has to make that decision, and before this the bot always filled the cap - which is
    /// to say every campaign mission was played on <see cref="Legendary"/> whether the player wanted it or
    /// not. Recorded in <c>claude/house-rules.md</c>.
    /// </para>
    ///
    /// <para>
    /// <b>Not the Quest difficulty.</b> GF: Star Quest and AoF: Quest have a published 1-4 difficulty in
    /// their Campaign Builder, and it scales the enemy as a <i>percentage</i> of the party's points
    /// (<c>StarQuestMissionRules.WaveBudget</c> is 25% per level). That is right for Quest and wrong here,
    /// because this campaign's cap is a <b>flat</b> number of points: bring 2,000 against 1,500 in
    /// Grimdark Future and that 500-point gap is illegal, so a percentage band would quietly propose
    /// illegal forces as armies grow. The four names are shared with Quest deliberately - a player should
    /// not have to learn two vocabularies - but nothing else is, and the two must not be aliased. See
    /// CLAUDE.md on every mode having its own numbers.
    /// </para>
    /// </summary>
    public enum EMissionDifficulty
    {
        /// <summary>
        /// Not chosen - a campaign saved before difficulty existed. Resolved to
        /// <see cref="MissionDifficulty.Default"/> rather than to today's behaviour: such a campaign was
        /// played at <see cref="Legendary"/> by accident, not by choice, and silently keeping that would
        /// hand an existing campaign the hardest setting with nothing on screen to say so.
        /// </summary>
        Unset = 0,
        /// <summary>The rival is a full mission cap below you - the widest legal margin in your favour.</summary>
        Beginner = 1,
        /// <summary>Equal forces: the core rules' own <i>"two armies of equal point cost"</i>.</summary>
        Intermediate = 2,
        /// <summary>The rival outguns you by three fifths of the cap.</summary>
        Expert = 3,
        /// <summary>The rival is a full mission cap above you - the widest legal margin against you.</summary>
        Legendary = 4,
    }

    /// <summary>
    /// #435 - turns a <see cref="EMissionDifficulty"/> into the points the rival should aim to bring,
    /// given what the player is bringing.
    ///
    /// <para>
    /// <b>The window, and why it is the books' own.</b> Campaign Rules v3.6.0: <i>"players may bring any
    /// number of their units to the match, however their total points may only be max. 325pts over the
    /// value of the army with the lowest points."</i> The player picks first (see
    /// <c>StrikeTeamScreen.PrepareEnemyForMission</c> for why that order is the only one that
    /// terminates), so the player's total is the floor and the rival's legal window is
    /// <c>[yours - cap, yours + cap]</c>. Difficulty picks a point inside it. Nothing here is invented
    /// arithmetic: every band is a whole number of <see cref="CampaignNumbers.UnderdogPer"/> steps, and
    /// the two extremes land exactly on <see cref="CampaignNumbers.ArmyCapOverLowest"/> because
    /// <c>cap / rate</c> is exactly <see cref="CampaignNumbers.UnderdogPointsAcrossTheCap"/> in all four
    /// books. That identity is the reason one scale fits every system with no per-system tuning.
    /// </para>
    ///
    /// <para>
    /// <b>The bands are deliberately uneven</b> (-5, 0, +3, +5 underdog points). Even quarters would put
    /// "about equal" at a third of a cap below the player, and equal forces are not an arbitrary point in
    /// the window - they are what the core rules ask for, so <see cref="EMissionDifficulty.Intermediate"/>
    /// sits exactly there. Below equal there is only one interesting setting, "give me a comfortable win";
    /// above it there are two worth separating, because that is where the campaign's rubber band and the
    /// Underdog Bonus do their work.
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty and the Underdog Bonus are the same number read twice.</b> A band expressed in
    /// underdog points IS the points the player will be handed by #434 at that setting: Legendary gives
    /// the rival a full cap more AND gives the player five underdog points to spend on it; Intermediate
    /// gives neither side anything. So the scale is self-balancing rather than a flat handicap - which is
    /// also why the house rule that bots never get underdog points matters here: at
    /// <see cref="EMissionDifficulty.Beginner"/> the rival is five points behind and is given nothing for
    /// it, which is what makes an easy mission easy.
    /// </para>
    ///
    /// <para>
    /// <b>A ceiling, not a guarantee.</b> Early in a campaign the rival will not own enough units to reach
    /// the top of a hard band, so it plays softer until its roster grows. The screen says so; nothing here
    /// pads the force to hit a number it cannot legitimately field.
    /// </para>
    /// </summary>
    public static class MissionDifficulty
    {
        /// <summary>
        /// What <see cref="EMissionDifficulty.Unset"/> means, and what a new campaign starts on: equal
        /// forces, the core rules' own baseline.
        /// </summary>
        public const EMissionDifficulty Default = EMissionDifficulty.Intermediate;

        /// <summary>The four settings in order, for a dropdown. Game text, so ASCII only (CLAUDE.md).</summary>
        public static readonly EMissionDifficulty[] All =
        {
            EMissionDifficulty.Beginner, EMissionDifficulty.Intermediate,
            EMissionDifficulty.Expert, EMissionDifficulty.Legendary,
        };

        /// <summary><see cref="EMissionDifficulty.Unset"/> resolved to the setting it means.</summary>
        public static EMissionDifficulty Resolve(EMissionDifficulty difficulty) =>
            difficulty == EMissionDifficulty.Unset ? Default : difficulty;

        /// <summary>The setting's name. Game text, so ASCII only.</summary>
        public static string Name(EMissionDifficulty difficulty) => Resolve(difficulty) switch
        {
            EMissionDifficulty.Beginner => "Beginner",
            EMissionDifficulty.Expert => "Expert",
            EMissionDifficulty.Legendary => "Legendary",
            _ => "Intermediate",
        };

        /// <summary>
        /// How far ahead of the player the rival should be, in underdog points - negative when the rival
        /// should be the smaller force. The band table, and the only place these four numbers live.
        /// </summary>
        public static int UnderdogPointsOfGap(EMissionDifficulty difficulty) => Resolve(difficulty) switch
        {
            // -5 and +5 are exactly the mission cap, by the cap/rate identity above.
            EMissionDifficulty.Beginner => -CampaignNumbers.UnderdogPointsAcrossTheCap,
            EMissionDifficulty.Expert => 3,
            EMissionDifficulty.Legendary => CampaignNumbers.UnderdogPointsAcrossTheCap,
            _ => 0,
        };

        /// <summary>
        /// The points the rival should aim for, given what the player is bringing. Never below 1: at
        /// <see cref="EMissionDifficulty.Beginner"/> against a small force the band can land at or under
        /// zero, and a target of zero would read as "bring nothing" rather than "bring as little as you
        /// can" - the picker's own rule that at least one unit always comes handles the rest.
        /// </summary>
        public static int EnemyPointsTarget(EMissionDifficulty difficulty, int playerPoints,
                                            CampaignNumbers numbers) =>
            Math.Max(1, playerPoints + UnderdogPointsOfGap(difficulty) * numbers.UnderdogPer);

        /// <summary>
        /// The most the rival may legally bring, whatever the band says: the books' cap over the smallest
        /// force present, which is the player's. A target is an aim and this is the law - they coincide at
        /// <see cref="EMissionDifficulty.Legendary"/> and nowhere else.
        /// </summary>
        public static int HardCap(int playerPoints, CampaignNumbers numbers) =>
            playerPoints + numbers.ArmyCapOverLowest;

        /// <summary>
        /// One line for the screen: what this setting will do to the rival's force, in points. Game text,
        /// so ASCII only.
        /// </summary>
        public static string Describe(EMissionDifficulty difficulty, int playerPoints,
                                      CampaignNumbers numbers)
        {
            int gap = UnderdogPointsOfGap(difficulty);
            int target = EnemyPointsTarget(difficulty, playerPoints, numbers);
            if (gap == 0) return $"Even match: about {target} pts, the same as yours.";

            int points = Math.Abs(gap) * numbers.UnderdogPer;
            return gap < 0
                ? $"You outgun them: about {target} pts, {points} below yours."
                : $"They outgun you: about {target} pts, {points} above yours - "
                  + $"worth {gap} underdog point(s) to you.";
        }
    }
}
