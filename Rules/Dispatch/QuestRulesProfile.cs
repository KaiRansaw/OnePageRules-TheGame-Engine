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
        bool FailedCastMayBeBought,
        float MeleeReachHorizontalInches,
        float MeleeReachVerticalInches,
        float ScoutForwardInches,
        float VillainAuraInches,
        bool VillainAuraIsMandatory)
    {
        /// <summary>
        /// GF: Star Quest, GFSQ Core Rules v2.1.0 - what #414 rebased the engine onto.
        /// <list type="bullet">
        /// <item>An extra action, an extra skill and reviving each cost <b>3</b> power (2.0.1 said 2).</item>
        /// <item>A failed hero-skill test <b>may</b> be paid off for 1 power, and the skill <b>fails</b>
        /// if you decline - so a hero skill can fail at all, which it could not under 2.0.1.</item>
        /// <item>A failed cast is <b>final</b>: 2.1.0 deleted 2.0.1's "spend X*2 power to count it as
        /// cast", and the engine never implemented it (pinned by QuestCastPowerTests).</item>
        /// <item>Melee reach is a flat <b>3"</b>, and Scout deploys within <b>9"</b>.</item>
        /// <item>The Villain aura is <b>6"</b> and <b>mandatory</b> (2.0.1: 12" and "may use").</item>
        /// </list>
        /// </summary>
        public static readonly QuestRulesProfile StarQuest = new(
            Book: "GFSQ Core Rules v2.1.0",
            ExtraActionPower: 3,
            RevivePower: 3,
            PassStatTestPower: 2,
            IgnoreShakenPower: 2,
            FailedSkillPower: 1,
            FailedSkillPaymentIsOptional: true,
            FailedCastMayBeBought: false,
            MeleeReachHorizontalInches: 3f,
            MeleeReachVerticalInches: 3f,
            ScoutForwardInches: 9f,
            VillainAuraInches: 6f,
            VillainAuraIsMandatory: true);

        /// <summary>
        /// AoF: Quest, AoFQ Core Rules v2.0.1 - quoted from the book, which is the 2.0.1 generation:
        /// <list type="bullet">
        /// <item>"Heroes ... may spend <b>2 power</b> to take any one additional action"; "Heroes within
        /// 3" may use a skill action and spend <b>2 power</b> to revive the hero".</item>
        /// <item>"When using hero skills, take a test of that skill's stat. If passed the effect is
        /// resolved, and <b>if failed you must first spend 1 power and then the effect is
        /// resolved</b>" - so the payment is forced and the skill always works. This is the behaviour
        /// Q3 removed for Star Quest, and for AoF: Quest it is still the rule.</item>
        /// <item>"Whenever a hero fails to cast a spell, it <b>may spend X*2 power to count as having
        /// cast it</b> instead" - the retroactive rescue 2.1.0 deleted.</item>
        /// <item>"Models within <b>2" horizontally and 4" vertically</b> of enemies must strike".</item>
        /// <item>"Scout: This model may be deployed anywhere fully within <b>12"</b> of its deployment
        /// zone."</item>
        /// <item>"Villain: AI units within <b>12"</b> <b>may</b> take morale tests using the Villain's
        /// Qua".</item>
        /// </list>
        /// <para>
        /// The vertical half of melee reach is currently moot either way - nothing in the engine ever
        /// puts a model at a height (elevation is parked until the Godot port, Kai's call 2026-10-31) -
        /// but it is recorded at the book's value so that it is right the day elevation lands.
        /// </para>
        /// </summary>
        public static readonly QuestRulesProfile AoFQuest = new(
            Book: "AoFQ Core Rules v2.0.1",
            ExtraActionPower: 2,
            RevivePower: 2,
            PassStatTestPower: 2,
            IgnoreShakenPower: 2,
            FailedSkillPower: 1,
            FailedSkillPaymentIsOptional: false,
            FailedCastMayBeBought: true,
            MeleeReachHorizontalInches: 2f,
            MeleeReachVerticalInches: 4f,
            ScoutForwardInches: 12f,
            VillainAuraInches: 12f,
            VillainAuraIsMandatory: false);

        /// <summary>The profile for a game system. A non-Quest system reads as Star Quest's, because
        /// nothing outside a Quest game asks - callers gate on <see cref="GameSystemInfo.IsQuest"/> or
        /// <see cref="GameSystemInfo.IsQuestMode"/>.</summary>
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
