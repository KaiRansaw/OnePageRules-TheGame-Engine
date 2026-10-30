namespace FDG.Campaign
{
    /// <summary>What a campaign random event does, in a shape the game can act on.</summary>
    public enum ECampaignEventKind
    {
        /// <summary>One random unit takes <see cref="CampaignEvent.Amount"/> wounds (D3 when <see cref="CampaignEvent.IsD3"/>).</summary>
        Wounds,
        /// <summary>One random unit takes that many hits.</summary>
        Hits,
        /// <summary>One random unit must take a Dangerous Terrain Test.</summary>
        DangerousTerrainTest,
        /// <summary>An area counts as Dangerous terrain until the end of the round.</summary>
        DangerousArea,
        /// <summary>An area counts as Difficult terrain until the end of the round.</summary>
        DifficultArea,
        /// <summary>One random non-Shaken unit becomes Shaken.</summary>
        BecomeShaken,
        /// <summary>One random Shaken unit stops being Shaken.</summary>
        StopBeingShaken,
        /// <summary>A roll modifier of <see cref="CampaignEvent.Amount"/> on <see cref="CampaignEvent.Roll"/>.</summary>
        RollModifier,
        /// <summary>Movement changes by +/-1" on Advance and +/-2" on Rush/Charge.</summary>
        MovementChange,
        /// <summary>Weapon ranges are halved.</summary>
        HalveRanges,
        /// <summary>A captured objective marker becomes neutral again.</summary>
        ObjectiveNeutral,
        /// <summary>A non-captured objective marker becomes captured by a random player.</summary>
        ObjectiveCapturedRandom,
        /// <summary>A captured objective marker becomes captured by the opponent.</summary>
        ObjectiveCapturedOpponent,
        /// <summary>An army gains points into its treasury.</summary>
        GainPoints,
        /// <summary>A unit gains XP.</summary>
        GainXp,
        /// <summary>A player gains VP.</summary>
        GainVp,
    }

    /// <summary>Which roll a modifier applies to, and which units it reaches.</summary>
    public enum ECampaignEventRoll { Hit, HitMelee, HitShooting, Defense, Morale }

    /// <summary>How wide an event reaches.</summary>
    public enum ECampaignEventScope
    {
        /// <summary>One random unit.</summary>
        RandomUnit,
        /// <summary>Any unit within 3" of one random objective marker.</summary>
        NearRandomObjective,
        /// <summary>One random unit that is within 3" of any objective marker.</summary>
        RandomUnitNearAnyObjective,
        /// <summary>One random table quarter; the effect applies while a unit is fully within it.</summary>
        RandomQuarter,
        /// <summary>One random army.</summary>
        RandomArmy,
        /// <summary>One random player.</summary>
        RandomPlayer,
    }

    /// <summary>
    /// One entry of the campaign random event table. The books roll two dice read as first digit and
    /// second digit, so the codes run 11-16, 21-26, ... 61-66.
    /// </summary>
    /// <param name="Code">The two-digit roll, e.g. 21.</param>
    /// <param name="Name">The event's name as the book prints it.</param>
    /// <param name="Text">The book's wording, for the Battle Report and the Mission window.</param>
    /// <param name="Kind">What it does.</param>
    /// <param name="Scope">Who or what it reaches.</param>
    /// <param name="Amount">The size of the effect; with <see cref="IsD3"/>, the multiplier of a D3.</param>
    /// <param name="IsD3">The amount is rolled: D3, or 2D3 when <paramref name="Amount"/> is 2.</param>
    /// <param name="Roll">For <see cref="ECampaignEventKind.RollModifier"/>, which roll it changes.</param>
    public readonly record struct CampaignEvent(
        int Code,
        string Name,
        string Text,
        ECampaignEventKind Kind,
        ECampaignEventScope Scope,
        int Amount = 0,
        bool IsD3 = false,
        ECampaignEventRoll Roll = ECampaignEventRoll.Hit);

    /// <summary>
    /// The 36-entry campaign random event table, identical in all four Campaign Rules v3.6.0 books
    /// (Grimdark Future, GF: Firefight, Age of Fantasy, AoF: Skirmish - the wording matches word for
    /// word). At the beginning of each round every player rolls one die, and on a 5+ an event happens.
    /// <para>
    /// This is the Grimdark Future / Firefight naming. Age of Fantasy uses the same 36 effects; only the
    /// flavour names differ in places, and they are not worth a second table until AoF campaign has a
    /// screen of its own.
    /// </para>
    /// <para>
    /// The table is data only for now - nothing rolls it or applies it yet. Wiring it into the round
    /// start is the next slice; the structured <see cref="ECampaignEventKind"/> is what makes that
    /// mechanical rather than 36 hand-written cases. See WorkItems/410.
    /// </para>
    /// </summary>
    public static class CampaignEvents
    {
        /// <summary>A player rolls one die at the start of each round; this result or better fires an event.</summary>
        public const int TriggerRoll = 5;

        public static readonly IReadOnlyList<CampaignEvent> All = new CampaignEvent[]
        {
            new(11, "Falling Debris", "One random unit takes D3 wounds.",
                ECampaignEventKind.Wounds, ECampaignEventScope.RandomUnit, Amount: 1, IsD3: true),
            new(12, "Explosive Trap", "One random unit takes 2D3 hits.",
                ECampaignEventKind.Hits, ECampaignEventScope.RandomUnit, Amount: 2, IsD3: true),
            new(13, "Venomous Moss", "The area within 3\" of one random objective marker counts as Dangerous terrain until the end of the round.",
                ECampaignEventKind.DangerousArea, ECampaignEventScope.NearRandomObjective),
            new(14, "Razor Vines", "The area within 3\" of one random objective marker counts as Difficult terrain until the end of the round.",
                ECampaignEventKind.DifficultArea, ECampaignEventScope.NearRandomObjective),
            new(15, "Lightning Strike", "One random unit must take a Dangerous Terrain Test.",
                ECampaignEventKind.DangerousTerrainTest, ECampaignEventScope.RandomUnit),
            new(16, "Stuck in the Mud", "One random unit counts as being in Difficult terrain until the end of the round.",
                ECampaignEventKind.DifficultArea, ECampaignEventScope.RandomUnit),

            new(21, "Swarm Attack", "One random non-Shaken unit becomes Shaken.",
                ECampaignEventKind.BecomeShaken, ECampaignEventScope.RandomUnit),
            new(22, "Debilitating Static", "One random unit gets -1 to Defense rolls until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: -1, Roll: ECampaignEventRoll.Defense),
            new(23, "Dazing Spores", "One random unit gets -1 to hit rolls when shooting until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: -1, Roll: ECampaignEventRoll.HitShooting),
            new(24, "Anomalous Gale", "One random unit gets -1 to hit rolls in melee until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: -1, Roll: ECampaignEventRoll.HitMelee),
            new(25, "Alien Shriek", "One random unit gets -1 to Morale Test rolls until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: -1, Roll: ECampaignEventRoll.Morale),
            new(26, "Sticky Foliage", "One random unit moves -1\" on Advance and -2\" on Rush/Charge actions until the end of the round.",
                ECampaignEventKind.MovementChange, ECampaignEventScope.RandomUnit, Amount: -1),

            new(31, "Sinkhole", "One random unit that is within 3\" of any objective marker takes D3 wounds.",
                ECampaignEventKind.Wounds, ECampaignEventScope.RandomUnitNearAnyObjective, Amount: 1, IsD3: true),
            new(32, "Solar Flare", "One random unit that is within 3\" of any objective marker takes 2D3 hits.",
                ECampaignEventKind.Hits, ECampaignEventScope.RandomUnitNearAnyObjective, Amount: 2, IsD3: true),
            new(33, "Crumbling Earth", "One random captured objective marker becomes neutral again.",
                ECampaignEventKind.ObjectiveNeutral, ECampaignEventScope.NearRandomObjective),
            new(34, "Spotter Drone", "One random non-captured objective marker becomes captured by one random player.",
                ECampaignEventKind.ObjectiveCapturedRandom, ECampaignEventScope.NearRandomObjective),
            new(35, "Robot Guide", "One random captured objective marker becomes captured by the opponent.",
                ECampaignEventKind.ObjectiveCapturedOpponent, ECampaignEventScope.NearRandomObjective),
            new(36, "Psychic Pool", "Until the end of the round, whilst any unit is within 3\" of one random objective marker, it gets +1 to Defense rolls.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.NearRandomObjective, Amount: +1, Roll: ECampaignEventRoll.Defense),

            new(41, "Terrifying Quakes", "One random table quarter counts as Dangerous terrain until the end of the round.",
                ECampaignEventKind.DangerousArea, ECampaignEventScope.RandomQuarter),
            new(42, "Heavy Rain", "One random table quarter counts as Difficult terrain until the end of the round.",
                ECampaignEventKind.DifficultArea, ECampaignEventScope.RandomQuarter),
            new(43, "Thick Clouds", "Until the end of the round, whilst any unit is fully within a random table quarter, it halves its weapon ranges.",
                ECampaignEventKind.HalveRanges, ECampaignEventScope.RandomQuarter),
            new(44, "Psychic Fog", "Until the end of the round, any unit that activates whilst fully within a random table quarter, moves +1\" on Advance and +2\" on Rush/Charge actions.",
                ECampaignEventKind.MovementChange, ECampaignEventScope.RandomQuarter, Amount: +1),
            new(45, "Shifting Illusions", "Until the end of the round, whilst any unit is fully within a random table quarter, it gets +1 to Morale Test rolls.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomQuarter, Amount: +1, Roll: ECampaignEventRoll.Morale),
            new(46, "Support Robots", "Until the end of the round, whilst any unit is fully within a random table quarter, it gets +1 to Defense rolls.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomQuarter, Amount: +1, Roll: ECampaignEventRoll.Defense),

            new(51, "Haste Stims", "One random unit moves +1\" on Advance and +2\" on Rush/Charge actions until the end of the round.",
                ECampaignEventKind.MovementChange, ECampaignEventScope.RandomUnit, Amount: +1),
            new(52, "Prophetic Vision", "One random unit gets +1 to Morale Test rolls until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: +1, Roll: ECampaignEventRoll.Morale),
            new(53, "Invigorating Fumes", "One random unit gets +1 to hit rolls in melee until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: +1, Roll: ECampaignEventRoll.HitMelee),
            new(54, "Enhancing Force-Field", "One random unit gets +1 to hit rolls when shooting until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: +1, Roll: ECampaignEventRoll.HitShooting),
            new(55, "Shield-Projector Drone", "One random unit gets +1 to Defense rolls until the end of the round.",
                ECampaignEventKind.RollModifier, ECampaignEventScope.RandomUnit, Amount: +1, Roll: ECampaignEventRoll.Defense),
            new(56, "Refreshment Kit", "One random Shaken unit stops being Shaken.",
                ECampaignEventKind.StopBeingShaken, ECampaignEventScope.RandomUnit),

            new(61, "Credits Stash", "One random army gets 25pts.",
                ECampaignEventKind.GainPoints, ECampaignEventScope.RandomArmy, Amount: 25),
            new(62, "Injector Pouch", "One random unit gets 1 XP.",
                ECampaignEventKind.GainXp, ECampaignEventScope.RandomUnit, Amount: 1),
            new(63, "Grenade Bag", "One random player gets 1 VP.",
                ECampaignEventKind.GainVp, ECampaignEventScope.RandomPlayer, Amount: 1),
            new(64, "Treasure Holopad", "One random army gets D3 * 25pts.",
                ECampaignEventKind.GainPoints, ECampaignEventScope.RandomArmy, Amount: 25, IsD3: true),
            new(65, "Kit Box", "One random unit gets D3 XP.",
                ECampaignEventKind.GainXp, ECampaignEventScope.RandomUnit, Amount: 1, IsD3: true),
            new(66, "Godly Blessing", "One random player gets D3 VP.",
                ECampaignEventKind.GainVp, ECampaignEventScope.RandomPlayer, Amount: 1, IsD3: true),
        };

        /// <summary>
        /// The event for a two-dice roll, read as first digit then second digit: a 2 then a 1 is event 21.
        /// Each die is clamped to 1-6 so a misbehaving roller can never miss the table.
        /// </summary>
        public static CampaignEvent Roll(Func<int> d6)
        {
            int first = Math.Clamp(d6(), 1, 6);
            int second = Math.Clamp(d6(), 1, 6);
            return ByCode(first * 10 + second);
        }

        /// <summary>The event with this two-digit code.</summary>
        public static CampaignEvent ByCode(int code) => All.First(e => e.Code == code);
    }
}
