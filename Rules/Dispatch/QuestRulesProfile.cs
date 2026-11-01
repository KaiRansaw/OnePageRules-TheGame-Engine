using FDG.EngineInterface;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #418 - the numbers and choices that differ between the two Quest systems' core books.
    ///
    /// <para>
    /// <b>Why this exists.</b> GF: Star Quest is on <b>GFSQ Core Rules v2.1.0</b> and AoF: Quest on
    /// <b>AoFQ Core Rules v2.0.1</b> - one OPR generation apart - and 2.1.0 is a rules revision rather
    /// than a reprint. The engine was moved onto 2.1.0 for Star Quest across #414 (power costs, the
    /// optional failed-skill payment, the deleted retroactive cast rescue, melee reach, Scout, the
    /// Villain aura), so serving those same numbers to an AoF: Quest game would play a version of the
    /// game its own book does not describe - the same class of fault as serving one system's army book
    /// to another, and the reason AoF: Quest stayed unselectable until this existed.
    /// </para>
    /// <para>
    /// Deliberately separate from <see cref="CoreRulesProfile"/>, which answers "how does this SPECIAL
    /// RULE read in this system" (Scout's procedure, Fear's consumer, Blast's spread). This answers
    /// "what do the HERO mechanics cost and how do they resolve", which is Quest-only and has nothing to
    /// say about Grimdark Future or Age of Fantasy.
    /// </para>
    /// <para>
    /// <b>If OPR publishes AoFQ 2.1.0</b>, almost all of this collapses: point
    /// <see cref="For(EGameSystem)"/> at <see cref="StarQuest"/> for both and delete the AoF profile.
    /// Checked on 2026-10-31 and 2.0.1 was the current AoF: Quest book as far as could be determined.
    /// </para>
    /// </summary>
    public sealed record QuestRulesProfile(
        string Book,
        int ExtraActionPower,
        int RevivePower,
        int PassStatTestPower,
        int IgnoreShakenPower,
        int FailedSkillPower,
        bool FailedSkillPaymentIsOptional,
        float MeleeReachHorizontalInches,
        float MeleeReachVerticalInches,
        float ScoutForwardInches,
        float VillainAuraInches,
        bool VillainAuraIsMandatory)
    {
        /// <summary>
        /// Both Quest systems, on their <b>2.1.0</b> core books (GFSQ v2.1.0 and AoFQ v2.1.0). Quoted:
        /// <list type="bullet">
        /// <item>"Heroes may use any one action plus one Skill action, in any order, and may also choose
        /// to spend <b>3 Power</b> to use any one additional action."</item>
        /// <item>"Heroes within 3" of an Unconscious Hero may use a skill action and spend <b>3 Power</b>
        /// to revive it."</item>
        /// <item>"When using Hero skills, take a test of that skill's stat. If passed the Hero may
        /// resolve its effect, and if failed you <b>may</b> spend 1 Power to count as having passed it
        /// instead." - optional, so a hero skill can fail.</item>
        /// <item>Caster(X) spends tokens/Power <b>before rolling</b> and says nothing about buying a
        /// failed cast back: a failed cast is final.</item>
        /// <item>"All models that are within <b>3"</b> of an enemy model from the target unit
        /// (horizontally or vertically), may attack it."</item>
        /// <item>"Scout: This model may be deployed anywhere fully within <b>12"</b> of its deployment
        /// zone." <b>12", not 9"</b> - owner's ruling, 2026-10-31: "On Army Forge it says Scout is 12".
        /// If there's any stat differences, refer to Army Forge." The 2.1.0 PDF reads 9" and Army Forge
        /// serves 12" for both Quest systems; Army Forge is the one the army data itself comes from, so
        /// it wins. See CLAUDE.md, "When the books and Army Forge disagree".</item>
        /// <item>"Villain: AI units within <b>6"</b> <b>use</b> the Villain's Qua for Morale Tests if
        /// it's better than their own" - mandatory.</item>
        /// </list>
        /// </summary>
        public static readonly QuestRulesProfile Quest = new(
            Book: "GFSQ Core Rules v2.1.0 / AoFQ Core Rules v2.1.0",
            ExtraActionPower: 3,
            RevivePower: 3,
            PassStatTestPower: 2,
            IgnoreShakenPower: 2,
            FailedSkillPower: 1,
            FailedSkillPaymentIsOptional: true,
            MeleeReachHorizontalInches: 3f,
            MeleeReachVerticalInches: 3f,
            ScoutForwardInches: 12f,
            VillainAuraInches: 6f,
            VillainAuraIsMandatory: true);

        /// <summary>The 2.1.0 Quest rules, under its old name. Both systems share them.</summary>
        public static readonly QuestRulesProfile StarQuest = Quest;

        /// <summary>
        /// AoF: Quest's profile - the same object as <see cref="Quest"/>, kept as a name because the two
        /// systems are allowed to diverge again and the seam should be obvious when they do.
        ///
        /// <para>
        /// <b>They did diverge, for about an hour, and it is worth recording why.</b> #418 was built
        /// against <c>AoFQ - Core Rules v2.0.1.pdf</c>, which was the only AoF: Quest book available,
        /// and 2.0.1 disagrees with Star Quest's 2.1.0 on six things: an extra action and a revive cost
        /// <b>2</b> power, a failed hero-skill test <b>must</b> be paid for and the skill resolves
        /// anyway, a failed cast <b>may be bought back for X*2 power</b>, melee reach is <b>2"
        /// horizontal / 4" vertical</b>, Scout reaches <b>12"</b>, and the Villain aura is <b>12" and
        /// optional</b>. All six were implemented, tested and shipped that way.
        /// </para>
        /// <para>
        /// Then Kai added <c>AoFQ - Beginners Guide v2.1.0</c> and <c>AoFQ - Campaign Builder v2.1.0</c>,
        /// and AoF: Quest turns out to be on 2.1.0 as well - matching Star Quest on every one of those
        /// six. So the split was correct about the book it had and wrong about the game, and it
        /// collapsed into this one profile. The lesson for the next reader: <b>a per-system difference is
        /// only real while both systems' CURRENT books say so</b>, and OPR brings its systems up to a
        /// new generation one book at a time. The 2.0.1 numbers are in <c>WorkItems/418</c> if a
        /// narrative campaign ever pins a party to the older book.
        /// </para>
        /// </summary>
        public static readonly QuestRulesProfile AoFQuest = Quest;

        /// <summary>The profile for a game system. A non-Quest system reads as Star Quest's, because
        /// nothing outside a Quest game asks - callers gate on <see cref="GameSystemInfo.IsQuest"/> or
        /// <see cref="GameSystemInfo.IsQuestMode"/>.</summary>
        /// <para>
        /// There is one profile today. The switch stays because the seam is the point: if OPR moves one
        /// Quest system to a new generation and not the other - which is exactly what happened between
        /// 2.0.1 and 2.1.0 - this is where the difference goes, and nothing else has to change.
        /// </para>
        public static QuestRulesProfile For(EGameSystem system) => system switch
        {
            EGameSystem.AoFQuest => AoFQuest,
            _ => StarQuest,
        };

        /// <summary>The profile for the game being played.</summary>
        public static QuestRulesProfile For(IGameContext context) => For(context.Settings.GameSystem);

        // ── Player-facing labels ──────────────────────────────────────────────────────────────────
        // The price appears BOTH in the rule and on the button, and a button that lies about a price is
        // worse than either being wrong alone (#414). So the labels are derived here rather than
        // written out, and the two sites that OFFER an option and the one that MATCHES the answer all
        // read the same string from the same profile.

        public string ExtraActionOption => $"Extra Action ({ExtraActionPower} power)";

        public string ExtraSkillOption => $"Extra Skill ({ExtraActionPower} power)";

        public string ReviveName => $"Revive a hero ({RevivePower} power)";
    }
}
