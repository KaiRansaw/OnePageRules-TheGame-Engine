using FDG.GameModel;

namespace FDG.Ai.Solo
{
    /// <summary>Which units a Blast weapon's AI goes for first - the one clause GF and Firefight word differently.</summary>
    public enum EBlastPriority
    {
        /// <summary>GF and AoF: "multi-model units <b>without Tough</b> first, and multi-model units with Tough second".</summary>
        MultiModelWithoutToughFirst,

        /// <summary>Firefight and AoF: Skirmish: "multi-model units first, and single-model units second".</summary>
        MultiModelFirst,
    }

    /// <summary>Which units a Deadly weapon's AI goes for first.</summary>
    public enum EDeadlyPriority
    {
        /// <summary>GF and AoF: "single-model units <b>with Tough</b> first, and multi-model units with Tough second".</summary>
        SingleModelWithToughFirst,

        /// <summary>Firefight and AoF: Skirmish: "single-model units first, and multi-model units second".</summary>
        SingleModelFirst,
    }

    /// <summary>
    /// #419 - the seven places where the four <b>Solo &amp; Co-Op Rules v3.6.0</b> books disagree.
    ///
    /// <para>
    /// Everything else in those books - the five-step decision tree, shooting, melee, terrain, the
    /// activation order, Ambush, Caster, Indirect, Relentless, Scout and the Challenge Bonuses - is word
    /// for word identical across Grimdark Future, Firefight, Age of Fantasy and AoF: Skirmish. So this is a
    /// small table of exceptions rather than four implementations, and the behaviour lives in one place.
    /// </para>
    /// <para>
    /// <b>AoF: Skirmish is the one that catches you out.</b> Both skirmish-scale games, so the obvious
    /// assumption is that it matches Firefight - and it does on Blast and Deadly, but <b>not</b> on
    /// deployment. It splits the table into TWO sections off a d6 (1-3 and 4-6) and places objectives in
    /// FOUR squares, where the other three use three sections off a D3 and six squares. Assuming otherwise
    /// is precisely the mistake that shipped six wrong rules for AoF: Quest two patches earlier.
    /// </para>
    /// <para>
    /// <b>Recorded as OPR's own inconsistency, not tidied:</b> the Skirmish book says AI units are "randomly
    /// divided into <b>3 groups</b> of equal size" and then, in the next sentence, that the table is divided
    /// into <b>2 sections</b> - so three groups compete for two sections, and its own "re-rolling if all
    /// groups would deploy in the same section" is what resolves it. Followed literally.
    /// </para>
    /// </summary>
    public sealed record SoloCoOpProfile(
        string Book,
        int ObjectiveSquares,
        int DeploymentSections,
        bool SectionRollIsD3,
        bool HasArtilleryClause,
        bool HasTransportClause,
        EBlastPriority Blast,
        EDeadlyPriority Deadly)
    {
        /// <summary>
        /// Groups the AI's units are divided into before deployment. Three in every book, including the
        /// Skirmish one that then offers only two sections to put them in.
        /// </summary>
        public const int DeploymentGroups = 3;

        public static readonly SoloCoOpProfile GrimdarkFuture = new(
            Book: "GF - Solo & Co-Op Rules v3.6.0",
            ObjectiveSquares: 6,
            DeploymentSections: 3,
            SectionRollIsD3: true,
            HasArtilleryClause: true,
            HasTransportClause: true,
            Blast: EBlastPriority.MultiModelWithoutToughFirst,
            Deadly: EDeadlyPriority.SingleModelWithToughFirst);

        public static readonly SoloCoOpProfile Firefight = new(
            Book: "GFF - Solo & Co-Op Rules v3.6.0",
            ObjectiveSquares: 6,
            DeploymentSections: 3,
            SectionRollIsD3: true,
            HasArtilleryClause: false,   // Firefight has no Artillery rule
            HasTransportClause: false,   // nor Transport
            Blast: EBlastPriority.MultiModelFirst,
            Deadly: EDeadlyPriority.SingleModelFirst);

        public static readonly SoloCoOpProfile AgeOfFantasy = new(
            Book: "AoF - Solo & Co-Op Rules v3.6.0",
            ObjectiveSquares: 6,
            DeploymentSections: 3,
            SectionRollIsD3: true,
            HasArtilleryClause: true,
            HasTransportClause: false,   // the only clause AoF drops relative to GF
            Blast: EBlastPriority.MultiModelWithoutToughFirst,
            Deadly: EDeadlyPriority.SingleModelWithToughFirst);

        public static readonly SoloCoOpProfile AoFSkirmish = new(
            Book: "AoFS - Solo & Co-Op Rules v3.6.0",
            ObjectiveSquares: 4,         // four, not six
            DeploymentSections: 2,       // two, not three
            SectionRollIsD3: false,      // a d6: 1-3 = section 1, 4-6 = section 2
            HasArtilleryClause: false,
            HasTransportClause: false,
            Blast: EBlastPriority.MultiModelFirst,
            Deadly: EDeadlyPriority.SingleModelFirst);

        /// <summary>
        /// The book for this system. The Quest systems have no Solo &amp; Co-Op book at all - their core
        /// rules carry their own AI flowchart, which <c>StarQuestAiBrain</c> implements - so asking for one
        /// is a wiring mistake and gets Grimdark Future's rather than a crash, with the mistake recorded
        /// here rather than hidden.
        /// </summary>
        public static SoloCoOpProfile For(EGameSystem system) => system switch
        {
            EGameSystem.GrimdarkFuture => GrimdarkFuture,
            EGameSystem.GFFirefight => Firefight,
            EGameSystem.AgeOfFantasy => AgeOfFantasy,
            EGameSystem.AoFSkirmish => AoFSkirmish,
            _ => GrimdarkFuture,
        };

        /// <summary>
        /// Maps a die roll to a deployment section, per the book's own wording: a D3 straight across in
        /// three of them, and a d6 halved in the Skirmish one.
        /// </summary>
        /// <param name="roll">A d6 result, 1-6. The D3 books use only its lower half.</param>
        public int SectionForRoll(int roll)
        {
            if (SectionRollIsD3) return Math.Clamp((roll - 1) % DeploymentSections + 1, 1, DeploymentSections);
            return roll <= 3 ? 1 : 2;
        }
    }
}
