
namespace FDG.Campaign
{
    /// <summary>
    /// Which end condition a campaign uses (Campaign Rules v3.6.0, "Campaign Types").
    /// Only <see cref="Endless"/> is wired up; the rest are recognised so a saved campaign that was
    /// created later still loads, and so <c>CampaignRules.IsOver</c> has somewhere to grow.
    /// </summary>
    public enum ECampaignType
    {
        /// <summary>Never ends; VP accumulate on a leaderboard. Kai's call, 2026-10-30: build this one.</summary>
        Endless = 0,
        /// <summary>Play D3+4 missions; most VP after the last one wins. Not wired up yet.</summary>
        GameLimited = 1,
        /// <summary>First army to D3+4 VP after a match wins. Not wired up yet.</summary>
        PointLimited = 2,
        /// <summary>Ends after D3+4 weeks; most VP wins. Not wired up yet.</summary>
        TimeLimited = 3,
    }

    /// <summary>
    /// One game system's campaign price list (Campaign Rules v3.6.0). The four books - Grimdark Future,
    /// GF: Firefight, Age of Fantasy and AoF: Skirmish - are the same document section for section; only
    /// these numbers differ, plus the wording split recorded on <see cref="HeroSkillsCoverUnit"/>.
    /// See the project doc claude/campaign-rules-delta.md.
    /// <para>
    /// GF and AoF carry a fourth XP line the skirmish books lack - "+3 XP: Killed a Hero and its unit".
    /// It needs no field: +1 for the unit plus +2 for the Hero already comes to 3, so the line is a
    /// clarification that you don't get to claim both separately on top, not a different number.
    /// </para>
    /// </summary>
    /// <param name="Treasury">Points each army starts with before the first mission.</param>
    /// <param name="ArmyCapOverLowest">A mission army may be at most this many points over the lowest army's value.</param>
    /// <param name="UnderdogPer">One underdog point per this many points of difference. Deferred (needs 2+ real armies).</param>
    /// <param name="SpecialObjectiveBonus">Treasury bonus for completing the rolled special objective.</param>
    /// <param name="WinPoints">Treasury the winning army earns.</param>
    /// <param name="LosePoints">Treasury each losing army earns.</param>
    /// <param name="HeroInjuryTalentCost">How much cheaper an injured hero counts, and how much dearer a talented one.</param>
    /// <param name="RegularLevelCost">Points a regular unit costs more per level.</param>
    /// <param name="HeroLevelCost">Points a hero costs more per level.</param>
    /// <param name="VeteranRecruitCost">Surcharge for one new recruit per mission to start with 5 XP.</param>
    /// <param name="HeroSkillsCoverUnit">GF and AoF hero skill traits read "the Hero and its unit"; the skirmish
    /// books read just "the Hero", because at that scale a hero is its own unit.</param>
    /// <param name="EventCredits">
    /// What the two treasury random events pay: Credits Stash / Gold Stash is this, and Treasure Holopad /
    /// Treasure Map is D3 times it. 25 in three books and <b>15 in AoF: Skirmish</b> (#433) - the one
    /// number in the 36-entry event table that is not shared, and the reason that table needed a
    /// per-system seam at all.
    /// </param>
    public readonly record struct CampaignNumbers(
        int Treasury,
        int ArmyCapOverLowest,
        int UnderdogPer,
        int SpecialObjectiveBonus,
        int WinPoints,
        int LosePoints,
        int HeroInjuryTalentCost,
        int RegularLevelCost,
        int HeroLevelCost,
        int VeteranRecruitCost,
        bool HeroSkillsCoverUnit,
        int EventCredits)
    {
        /// <summary>VP the winning army earns. The same in all four books.</summary>
        public const int WinVp = 2;

        /// <summary>XP needed per level, and the cap (so six levels at most). The same in all four books.</summary>
        public const int XpPerLevel = 5, MaxXp = 30;

        /// <summary>A hero army may hold at most this many Heroes, and may recruit one per mission.</summary>
        public const int MaxHeroes = 3;

        /// <summary>
        /// The least an army must be worth before the first mission: 80% of its treasury.
        /// GFF says 320 of 400, AoF 600 of 750 and AoFS 240 of 300 - all exactly 80%. GF's book says
        /// "spend at least 800pts" against a 750pt treasury, which is impossible; Kai's call on
        /// 2026-10-30 was to treat that as an OPR typo and derive every system's minimum from the
        /// pattern instead of storing a fourth number that only one book disagrees with.
        /// </summary>
        public int MinimumSpend => Treasury * 4 / 5;

        /// <summary>Grimdark Future (Campaign Rules v3.6.0).</summary>
        public static CampaignNumbers GrimdarkFuture { get; } = new(
            Treasury: 750, ArmyCapOverLowest: 325, UnderdogPer: 65, SpecialObjectiveBonus: 75,
            WinPoints: 150, LosePoints: 300, HeroInjuryTalentCost: 25,
            RegularLevelCost: 25, HeroLevelCost: 55, VeteranRecruitCost: 50,
            HeroSkillsCoverUnit: true, EventCredits: 25);

        /// <summary>GF: Firefight (Campaign Rules v3.6.0).</summary>
        public static CampaignNumbers Firefight { get; } = new(
            Treasury: 400, ArmyCapOverLowest: 125, UnderdogPer: 25, SpecialObjectiveBonus: 40,
            WinPoints: 80, LosePoints: 160, HeroInjuryTalentCost: 15,
            RegularLevelCost: 15, HeroLevelCost: 25, VeteranRecruitCost: 30,
            HeroSkillsCoverUnit: false, EventCredits: 25);

        /// <summary>
        /// Age of Fantasy (Campaign Rules v3.6.0).
        ///
        /// <para>
        /// <b>Not identical to Grimdark Future</b>, though it was written that way here until #433. Every
        /// number matches except the two that matter most to a mission: the book prints a
        /// <b>250pt</b> army cap and <b>50pts</b> per underdog point, against Grimdark Future's 325 and
        /// 65. Aliasing the two gave every Age of Fantasy campaign a 30% wider mission cap than its own
        /// book allows, and one fewer underdog point than it should hand out at a given gap - invisible
        /// in play, because a too-generous cap never refuses anything.
        /// </para>
        /// <para>
        /// The cross-check that confirms the pair: <c>ArmyCapOverLowest / UnderdogPer</c> is exactly 5 in
        /// all four books (325/65, 125/25, 250/50, 100/20), so the two numbers move together and 250/50
        /// is self-consistent while 250/65 or 325/50 would not be.
        /// </para>
        /// </summary>
        public static CampaignNumbers AgeOfFantasy { get; } = new(
            Treasury: 750, ArmyCapOverLowest: 250, UnderdogPer: 50, SpecialObjectiveBonus: 75,
            WinPoints: 150, LosePoints: 300, HeroInjuryTalentCost: 25,
            RegularLevelCost: 25, HeroLevelCost: 55, VeteranRecruitCost: 50,
            HeroSkillsCoverUnit: true, EventCredits: 25);

        /// <summary>AoF: Skirmish (Campaign Rules v3.6.0).</summary>
        public static CampaignNumbers Skirmish { get; } = new(
            Treasury: 300, ArmyCapOverLowest: 100, UnderdogPer: 20, SpecialObjectiveBonus: 30,
            WinPoints: 60, LosePoints: 120, HeroInjuryTalentCost: 15,
            RegularLevelCost: 15, HeroLevelCost: 25, VeteranRecruitCost: 30,
            HeroSkillsCoverUnit: false, EventCredits: 15);

        /// <summary>
        /// The books' own ratio of mission cap to underdog rate, identical in all four: the full legal
        /// window a mission army may occupy is exactly this many underdog points wide. Used by the
        /// difficulty bands (#435) and worth a name, because it is the one piece of arithmetic that makes
        /// a difficulty scale behave the same in every system without per-system tuning.
        /// </summary>
        public const int UnderdogPointsAcrossTheCap = 5;

        /// <summary>
        /// The price list for a game system. Star Quest and Quest have their own campaign (the GFSQ
        /// Campaign Builder), not this one, so they fall back to their parent system's numbers rather
        /// than throwing - nothing should ever ask, but a stale save must not crash the screen.
        /// </summary>
        public static CampaignNumbers For(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => GrimdarkFuture,
            EGameSystem.GFFirefight => Firefight,
            EGameSystem.AgeOfFantasy => AgeOfFantasy,
            EGameSystem.AoFSkirmish => Skirmish,
            EGameSystem.GFStarQuest => GrimdarkFuture,
            EGameSystem.AoFQuest => AgeOfFantasy,
            _ => GrimdarkFuture,
        };

        /// <summary>Whether this game system has a Campaign Rules v3.6.0 book of its own.</summary>
        public static bool HasCampaignBook(EGameSystem system) =>
            system is EGameSystem.GrimdarkFuture or EGameSystem.GFFirefight
                   or EGameSystem.AgeOfFantasy or EGameSystem.AoFSkirmish;
    }
}
