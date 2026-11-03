using System.Text.Json;

namespace FDG.Campaign
{
    /// <summary>
    /// GF: Firefight Campaign: the team-level record of a strike team. A strike team is an ordinary army file
    /// (.fdgarmy) with this block on it and a <see cref="StrikeUnit"/> sheet on every unit, so it can be picked in
    /// the lobby like any army and keeps its progress between games.
    /// </summary>
    public sealed class StrikeTeamInfo
    {
        public string Name { get; set; } = "";
        /// <summary>Victory points: 2 per won mission.</summary>
        public int Vp { get; set; }
        /// <summary>The treasury: points available to spend on upgrades and recruits.</summary>
        public int BankedPoints { get; set; }
        public int Matches { get; set; }
        /// <summary>
        /// Which Campaign Rules v3.6.0 price list this army plays under. Defaults to
        /// <see cref="EGameSystem.GrimdarkFuture"/> (0), so a strike team file written before the 3.6
        /// move loads without a game system and must be read as Firefight by whoever opened it - the
        /// Firefight campaign screen passes its own numbers explicitly for exactly that reason.
        /// </summary>
        public EGameSystem GameSystem { get; set; } = EGameSystem.GrimdarkFuture;
        /// <summary>
        /// The campaign's end condition. <see cref="ECampaignType.Endless"/> and
        /// <see cref="ECampaignType.PointLimited"/> are wired up (#445); the other two are recognised
        /// so a campaign saved as one still loads, and are not offered in the UI.
        /// </summary>
        public ECampaignType CampaignType { get; set; } = ECampaignType.Endless;
        /// <summary>
        /// #445 - the VP an army must reach to win a <see cref="ECampaignType.PointLimited"/> campaign.
        /// Zero means no target, which is what every campaign saved before this field had, and is why
        /// <see cref="CampaignPreGame.IsOver"/> treats a zero limit as "not finished" rather than
        /// "finished immediately" - an old Endless campaign must keep opening.
        /// <para>
        /// Written to BOTH armies' blocks even though only the player's is read
        /// (<see cref="CampaignPreGame.Standing"/> takes it from the player). The target is a property of
        /// the campaign rather than of either army, so letting the two files disagree would make whichever
        /// one you happened to read the wrong answer. <see cref="Difficulty"/> and
        /// <see cref="EnemyArmyFile"/> are genuinely the player's own and stay player-only; this is not.
        /// </para>
        /// </summary>
        public int VpTarget { get; set; }
        /// <summary>
        /// The books' <b>No Permadeath</b> option, picked when the campaign is created (Kai's call,
        /// 2026-11-01: "have it be a toggle when creating a campaign"). A unit that would be killed
        /// instead loses the XP it earned that match and sits out D3 missions - see
        /// <see cref="StrikeUnit.MissionsOut"/>. Off by default, which is the books' default.
        /// </summary>
        public bool NoPermadeath { get; set; }
        /// <summary>True for an army the bot runs, so the campaign screen can spend its treasury for it.</summary>
        public bool IsBot { get; set; }
        /// <summary>
        /// #435 - how hard the rival's force is sized for the next mission. Set on the PLAYER's army,
        /// beside <see cref="EnemyArmyFile"/>, because it is the player's choice about their own campaign
        /// and the rival's block is the thing being sized. Chosen on Mission Setup and remembered, so a
        /// campaign stays at the difficulty it was being played at.
        /// <para>
        /// <see cref="EMissionDifficulty.Unset"/> on a campaign saved before this existed - see that
        /// member for what it resolves to and why it is not Legendary.
        /// </para>
        /// </summary>
        /// <remarks>
        /// The converter is declared HERE, on the property, not only on the enum. System.Text.Json
        /// resolves a property-level <c>[JsonConverter]</c> ahead of anything in
        /// <c>JsonSerializerOptions.Converters</c>, and <c>RuleJson.Options</c> carries a
        /// <c>JsonStringEnumConverter</c> that otherwise wins over the enum's own attribute and throws on
        /// the names this field used to be saved under. Found by a test, not by reasoning.
        /// </remarks>
        [System.Text.Json.Serialization.JsonConverter(typeof(MissionDifficultyJsonConverter))]
        public EMissionDifficulty Difficulty { get; set; } = EMissionDifficulty.Unset;
        /// <summary>
        /// #428 - the campaign army file of this campaign's standing enemy (a bot army created with it),
        /// or null when it has none. Set on the PLAYER's army only; the enemy's own block leaves it null
        /// and carries <see cref="IsBot"/> instead. Every mission is fought against this same army, which
        /// is what gives the campaign an arc: it levels, recruits and remembers losing.
        /// </summary>
        public string? EnemyArmyFile { get; set; }
        /// <summary>
        /// The special objective rolled for the next mission, worth
        /// <see cref="CampaignNumbers.SpecialObjectiveBonus"/> if it is completed. Empty until one is
        /// rolled; cleared again once the mission is over.
        /// </summary>
        public string SpecialObjective { get; set; } = "";
        /// <summary>
        /// The <see cref="StrikeUnit.Id"/> of every unit coming to the next mission. Empty means the
        /// whole army comes, which is what a brand new campaign army wants.
        /// </summary>
        public List<string> Bringing { get; set; } = new();
        /// <summary>One line per finished match, newest last.</summary>
        public List<string> History { get; set; } = new();
    }

    /// <summary>
    /// GF: Firefight Campaign: one unit's strike team sheet - XP, traits, injuries, talents, hero skills and any
    /// choices still to make. Rides on the unit in play (<see cref="UnitData.StrikeUnitJson"/>) so kills can be
    /// counted during the game, then is written back to the team file.
    /// </summary>
    public sealed class StrikeUnit
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public bool IsHero { get; set; }
        public int Xp { get; set; }
        public List<string> Traits { get; set; } = new();
        public List<string> Injuries { get; set; } = new();
        public List<string> Talents { get; set; } = new();
        public List<string> SkillSets { get; set; } = new();
        public List<string> Skills { get; set; } = new();
        /// <summary>Choices the player still has to make on the strike team screen: "Specialist", "FastLearner",
        /// or "SkillSet:A|B" (pick one of two skill sets).</summary>
        public List<string> Pending { get; set; } = new();
        /// <summary>Points added to (or taken off) the unit's cost by level ups, injuries and talents.</summary>
        public int CostChange { get; set; }
        public int Games { get; set; }
        /// <summary>
        /// Missions this unit must still sit out, under the <see cref="StrikeTeamInfo.NoPermadeath"/>
        /// option: it was killed, survived anyway, and is recovering. It may not be taken to a mission
        /// while this is above zero, and it counts down by one each mission the army plays without it.
        /// Always zero in a campaign where permadeath is on, because there the unit is simply gone.
        /// </summary>
        public int MissionsOut { get; set; }
        /// <summary>Counted during a game; cleared when the result is written back.</summary>
        public int Kills { get; set; }
        public int HeroKills { get; set; }

        /// <summary>
        /// #430 - spells this unit cast successfully during the game in progress. Summed across an army
        /// for the campaign's <b>Hexer</b> objective ("successfully cast four or more spells by the end of
        /// the game"), which cannot be answered from a finished table: a resolved cast leaves no trace.
        /// Cleared with <see cref="Kills"/> when the result is written back.
        /// </summary>
        public int SpellsCast { get; set; }

        /// <summary>
        /// #430 - this unit destroyed the FIRST enemy unit of the game, which is the campaign's
        /// <b>Annihilator</b> objective ("be the first player to destroy an enemy unit"). At most one unit
        /// on the table carries it, and a finished table cannot tell you who it was any other way - it
        /// records that units died, never in what order. Cleared with <see cref="Kills"/>.
        /// <para>
        /// It lives on the sheet rather than on the game's progress record on purpose. The progress record
        /// replicates to every client when it is written, and this is written from inside the attack
        /// pipeline - paying for a mid-kill replication of the whole record, for a campaign-only
        /// objective, is a cost for nothing. The sheet is already written at this exact point (kills are
        /// counted here), so this rides machinery that is known to be safe here.
        /// </para>
        /// </summary>
        public bool DrewFirstBlood { get; set; }

        /// <summary>
        /// #432 - rewards a campaign random event handed this unit or its army during the game in
        /// progress, waiting for the after-game write-back to fold them into the campaign file.
        /// <para>
        /// Treasury and VP belong to the ARMY, which is not on the table at all, so the reward rides one
        /// of its units' sheets to get out of the game - the same route the Hexer count and the
        /// first-blood stamp take, and for the same reason: the sheet is the only piece of campaign state
        /// an in-game effect can reach. Cleared with <see cref="Kills"/> when the result is written back.
        /// </para>
        /// </summary>
        public int EventXp { get; set; }
        public int EventPoints { get; set; }
        public int EventVp { get; set; }
        /// <summary>The strike team file this unit belongs to (set when the team is saved).</summary>
        public string? TeamFile { get; set; }
        /// <summary>
        /// The unit's upgrade selections as a serialized <see cref="ArmyBuilding.BuilderUnit"/>, so it can
        /// be re-opened between missions to buy and sell upgrades.
        /// <para>
        /// It lives on the sheet rather than in <c>BuiltArmyFile.Selections</c> on purpose. A campaign
        /// army's units carry changes the book knows nothing about - levels, injuries, talents, skills and
        /// the points those cost - and re-compiling the whole army from a builder list would wipe them,
        /// which is why the strike team screen nulls <c>Selections</c>. Keeping a per-unit loadout here
        /// means a campaign upgrade re-compiles exactly one unit and then re-applies that unit's campaign
        /// changes on top (<c>CampaignEconomy.ReapplyCampaignChanges</c>). Horde Mode keeps its
        /// mid-game loadout the same way, on the unit's <c>HordeLoadoutJson</c>.
        /// </para>
        /// <para>Null means the unit was never built from a book (a hand-written or imported army), and
        /// it can be recruited alongside but not upgraded.</para>
        /// </summary>
        public string? LoadoutJson { get; set; }

        public int Level => Math.Min(StrikeTeamRules.MaxXp, Xp) / StrikeTeamRules.XpPerLevel;

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
        public string ToJson() => JsonSerializer.Serialize(this, Options);

        public static StrikeUnit? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<StrikeUnit>(json); }
            catch (JsonException) { return null; }
        }
    }
}
