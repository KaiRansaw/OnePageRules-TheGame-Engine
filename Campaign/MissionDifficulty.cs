using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDG.Campaign
{
    /// <summary>
    /// #435 - how hard the rival's force is sized for the next campaign mission.
    ///
    /// <para>
    /// <b>This is a house rule.</b> Campaign Rules v3.6.0 has no difficulty setting at all: it has one
    /// mission cap and expects a human on the other side of the table to decide what to bring. With a bot
    /// rival somebody has to make that decision, and before this the bot always filled the cap - which is
    /// to say every campaign mission was played on <see cref="Brutal"/> whether the player wanted it or
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
    [JsonConverter(typeof(MissionDifficultyJsonConverter))]
    public enum EMissionDifficulty
    {
        /// <summary>
        /// Not chosen - a campaign saved before difficulty existed. Resolved to
        /// <see cref="MissionDifficulty.Default"/> rather than to today's behaviour: such a campaign was
        /// played at <see cref="Brutal"/> by accident, not by choice, and silently keeping that would
        /// hand an existing campaign the hardest setting with nothing on screen to say so.
        /// </summary>
        Unset = 0,
        /// <summary>The rival is a full mission cap below you - the widest legal margin in your favour.</summary>
        Easy = 1,
        /// <summary>
        /// Equal forces: the core rules' own <i>"two armies of equal point cost"</i>, and the setting that
        /// pays exactly what the campaign book prints. Every other setting is measured from this one.
        /// </summary>
        Normal = 2,
        /// <summary>The rival outguns you by three fifths of the cap.</summary>
        Hard = 3,
        /// <summary>The rival is a full mission cap above you - the widest legal margin against you.</summary>
        Brutal = 4,
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
    /// the window - they are what the core rules ask for, so <see cref="EMissionDifficulty.Normal"/>
    /// sits exactly there. Below equal there is only one interesting setting, "give me a comfortable win";
    /// above it there are two worth separating, because that is where the campaign's rubber band and the
    /// Underdog Bonus do their work.
    /// </para>
    ///
    /// <para>
    /// <b>Difficulty and the Underdog Bonus are the same number read twice.</b> A band expressed in
    /// underdog points IS the points the player will be handed by #434 at that setting: Brutal gives
    /// the rival a full cap more AND gives the player five underdog points to spend on it; Normal
    /// gives neither side anything. So the scale is self-balancing rather than a flat handicap - which is
    /// also why the house rule that bots never get underdog points matters here: at
    /// <see cref="EMissionDifficulty.Easy"/> the rival is five points behind and is given nothing for
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
        /// forces, the core rules' own baseline, paying exactly what the book prints.
        /// </summary>
        public const EMissionDifficulty Default = EMissionDifficulty.Normal;

        /// <summary>The four settings in order, for a dropdown. Game text, so ASCII only (CLAUDE.md).</summary>
        public static readonly EMissionDifficulty[] All =
        {
            EMissionDifficulty.Easy, EMissionDifficulty.Normal,
            EMissionDifficulty.Hard, EMissionDifficulty.Brutal,
        };

        /// <summary><see cref="EMissionDifficulty.Unset"/> resolved to the setting it means.</summary>
        public static EMissionDifficulty Resolve(EMissionDifficulty difficulty) =>
            difficulty == EMissionDifficulty.Unset ? Default : difficulty;

        /// <summary>The setting's name. Game text, so ASCII only.</summary>
        public static string Name(EMissionDifficulty difficulty) => Resolve(difficulty) switch
        {
            EMissionDifficulty.Easy => "Easy",
            EMissionDifficulty.Hard => "Hard",
            EMissionDifficulty.Brutal => "Brutal",
            _ => "Normal",
        };

        /// <summary>
        /// How far ahead of the player the rival should be, in underdog points - negative when the rival
        /// should be the smaller force. The band table, and the only place these four numbers live.
        /// </summary>
        public static int UnderdogPointsOfGap(EMissionDifficulty difficulty) => Resolve(difficulty) switch
        {
            // -5 and +5 are exactly the mission cap, by the cap/rate identity above.
            EMissionDifficulty.Easy => -CampaignNumbers.UnderdogPointsAcrossTheCap,
            EMissionDifficulty.Hard => 3,
            EMissionDifficulty.Brutal => CampaignNumbers.UnderdogPointsAcrossTheCap,
            _ => 0,
        };

        /// <summary>
        /// The points the rival should aim for, given what the player is bringing. Never below 1: at
        /// <see cref="EMissionDifficulty.Easy"/> against a small force the band can land at or under
        /// zero, and a target of zero would read as "bring nothing" rather than "bring as little as you
        /// can" - the picker's own rule that at least one unit always comes handles the rest.
        /// </summary>
        public static int EnemyPointsTarget(EMissionDifficulty difficulty, int playerPoints,
                                            CampaignNumbers numbers) =>
            Math.Max(1, playerPoints + UnderdogPointsOfGap(difficulty) * numbers.UnderdogPer);

        /// <summary>
        /// The most the rival may legally bring, whatever the band says: the books' cap over the smallest
        /// force present, which is the player's. A target is an aim and this is the law - they coincide at
        /// <see cref="EMissionDifficulty.Brutal"/> and nowhere else.
        /// </summary>
        public static int HardCap(int playerPoints, CampaignNumbers numbers) =>
            playerPoints + numbers.ArmyCapOverLowest;

        /// <summary>
        /// The setting's level, 1-4, which is what the rewards multiply by. Named rather than cast from
        /// the enum so the stored numbering and the reward scale cannot drift apart silently.
        /// </summary>
        public static int Level(EMissionDifficulty difficulty) => (int)Resolve(difficulty);

        /// <summary>
        /// The treasury a WIN pays at this setting.
        ///
        /// <para>
        /// Kai's call, 2026-11-02, from two answers taken together: scale rewards <b>by the setting's
        /// level</b> the way the Quest Campaign Builder does (<i>"Get 5c times the chosen Difficulty
        /// level"</i>), and make <b>Normal the normal payout</b>. Those pin each other: if Normal is
        /// level 2 and pays the book's own figure, then one level is worth half of it, and the four
        /// settings come out at half / full / one-and-a-half / double. In Grimdark Future that is
        /// <b>75 / 150 / 225 / 300</b>, and the middle one is exactly what the book prints.
        /// </para>
        /// <para>
        /// So <see cref="EMissionDifficulty.Easy"/> deliberately pays <i>less</i> than the book - Kai's
        /// words, <i>"Easy is scaled down, so rewards are scaled down as well"</i>. That is the one place
        /// this scale is not simply a bonus on top of the books.
        /// </para>
        /// <para>
        /// <b>Multiplied before it is halved</b>, not after: a book printing an odd win payout would lose
        /// a point per level to integer division the other way round, and Normal must land on the book's
        /// figure exactly. All four books print an even number today (150, 80, 150, 60), so this costs
        /// nothing now and cannot go wrong if one changes.
        /// </para>
        /// </summary>
        public static int WinPoints(EMissionDifficulty difficulty, CampaignNumbers numbers) =>
            numbers.WinPoints * Level(difficulty) / 2;

        /// <summary>
        /// The VP a win is worth at this setting: 1 / 2 / 3 / 4, by the same halving as
        /// <see cref="WinPoints"/> against the books' flat <see cref="CampaignNumbers.WinVp"/> of 2. So
        /// a campaign played on Normal reaches a VP target at exactly the rate the book intends.
        /// </summary>
        public static int WinVp(EMissionDifficulty difficulty) =>
            CampaignNumbers.WinVp * Level(difficulty) / 2;

        /// <summary>
        /// The treasury a LOSS pays - the book's own figure, <b>unscaled at every setting</b>.
        ///
        /// <para>
        /// Kai's call, 2026-11-02: <i>"loss payout scale does not change"</i>. It exists as a method
        /// anyway so that the two halves of the payout read the same way at the call site, and so the
        /// reason is written down where someone would otherwise "finish the job" by scaling it.
        /// </para>
        /// <para>
        /// The reason it must not scale: the books pay the loser roughly double the winner (300 against
        /// 150 in Grimdark Future) precisely so whoever is behind catches up. Multiply that by four on
        /// the hardest setting and losing becomes the profitable move - you would bank 1,200 points for
        /// being beaten, field a far bigger army next mission, and the rubber band would be pulling the
        /// wrong way.
        /// </para>
        /// </summary>
        public static int LosePoints(EMissionDifficulty difficulty, CampaignNumbers numbers) =>
            numbers.LosePoints;

        /// <summary>
        /// One line for the screen: what a win pays at this setting. Game text, so ASCII only.
        /// </summary>
        public static string DescribeReward(EMissionDifficulty difficulty, CampaignNumbers numbers)
        {
            int points = WinPoints(difficulty, numbers);
            int vp = WinVp(difficulty);
            string note = Resolve(difficulty) == Default
                ? " - what the book pays"
                : points > numbers.WinPoints ? " - more than the book pays" : " - less than the book pays";
            return $"A win pays {vp} VP and {points} pts{note}. A loss pays "
                   + $"{LosePoints(difficulty, numbers)} pts at every setting.";
        }

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

    /// <summary>
    /// Reads <see cref="EMissionDifficulty"/> from a campaign file, accepting <b>the names it used to
    /// have</b> as well as the ones it has now.
    ///
    /// <para>
    /// The enum is stored by NAME (<c>RuleJson.Options</c> carries a <c>JsonStringEnumConverter</c>), and
    /// the four settings were renamed on 2026-11-02 - Beginner/Intermediate/Expert/Legendary became
    /// Easy/Normal/Hard/Brutal, because sharing Quest's published vocabulary made two different mechanics
    /// read as one. Without this, a campaign army saved by the build that shipped the old names would not
    /// just lose its difficulty: the whole file would fail to deserialize and the campaign would look
    /// gone. One renamed field must never cost somebody their campaign.
    /// </para>
    /// <para>
    /// <b>Declared on the property as well as on the enum</b>, and the property is the one that counts:
    /// System.Text.Json resolves a property-level attribute ahead of
    /// <c>JsonSerializerOptions.Converters</c>, and <c>RuleJson.Options</c> carries a
    /// <c>JsonStringEnumConverter</c> which otherwise wins over a type-level attribute and throws on the
    /// old names. The attribute on the enum is the statement of intent for anything serialized with
    /// plain options; <c>StrikeTeamInfo.Difficulty</c> carries the one that actually applies.
    /// </para>
    /// <para>
    /// Numbers are accepted too, and anything unrecognised reads as
    /// <see cref="EMissionDifficulty.Unset"/> rather than throwing - which resolves to
    /// <see cref="MissionDifficulty.Default"/>, the safe answer. Writing always uses the current name.
    /// </para>
    /// </summary>
    public sealed class MissionDifficultyJsonConverter : JsonConverter<EMissionDifficulty>
    {
        public override EMissionDifficulty Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                int value = reader.GetInt32();
                return value is >= 0 and <= 4 ? (EMissionDifficulty)value : EMissionDifficulty.Unset;
            }
            if (reader.TokenType != JsonTokenType.String) return EMissionDifficulty.Unset;

            return reader.GetString() switch
            {
                "Easy" or "Beginner" => EMissionDifficulty.Easy,
                "Normal" or "Intermediate" => EMissionDifficulty.Normal,
                "Hard" or "Expert" => EMissionDifficulty.Hard,
                "Brutal" or "Legendary" => EMissionDifficulty.Brutal,
                _ => EMissionDifficulty.Unset,
            };
        }

        public override void Write(Utf8JsonWriter writer, EMissionDifficulty value,
            JsonSerializerOptions options) =>
            writer.WriteStringValue(value == EMissionDifficulty.Unset
                ? "Unset"
                : MissionDifficulty.Name(value));
    }
}
