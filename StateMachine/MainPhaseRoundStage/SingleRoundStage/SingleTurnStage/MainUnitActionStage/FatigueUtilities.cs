using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// #020 Fatigue: a unit that charges or strikes back in melee becomes Fatigued for the rest of the
    /// round, after which it hits only on unmodified 6s in further melee. Shaken units (#089) always
    /// count as fatigued in melee. The token clears at end of round via the <c>Round_OnRoundEnd</c>
    /// hook (<see cref="TokenClearTrigger.RoundEnd"/>), swept by <c>ReconcileObjectivesStage</c>.
    /// </summary>
    public static class FatigueUtilities
    {
        /// <summary>Mark a unit Fatigued for the rest of the round; applied idempotently.</summary>
        public static void ApplyFatigued(IUnit unit)
        {
            if (unit.Tokens.HasToken(TokenType.Fatigued)) return;

            unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Fatigued));
        }

        /// <summary>
        /// Whether a unit hits only on unmodified 6s in melee: it is Fatigued, or it is Shaken (#089),
        /// which always counts as fatigued in melee.
        /// </summary>
        public static bool CountsAsFatiguedInMelee(IUnit unit)
            => unit.Tokens.HasToken(TokenType.Fatigued) || unit.Tokens.HasToken(TokenType.Shaken);

        /// <summary>
        /// #414 - the second half of the 3.6.0 Fatigue rule: *"units hit on unmodified rolls of 6 in
        /// melee until the end of that round, **and those hits don't count as rolls of 6 for special
        /// rules**."* The clause is new in 3.6.0 (GF Core Rules v3.6.0, GFF Core Rules v3.6.0 and GFSQ
        /// Core Rules v2.1.0 all carry it; the 3.5.1 Advanced Rules do not), and it matters a great deal:
        /// without it a fatigued unit's only hits are 6s, so **every 6-triggered rule it owns fires on
        /// every single hit it scores** - Furious and Surge double them, Rending gives all of them
        /// AP(+4), Relentless doubles them at range. Fatigue was quietly the best thing that could
        /// happen to a Rending unit.
        ///
        /// <para>
        /// Returns the dice as the RULES should see them: the 6s are relabelled to 5s rather than
        /// deleted, so the histogram still totals the same number of dice and nothing downstream that
        /// reads the roll count goes wrong. Face 5 is the safe landing spot because on the
        /// hit-roll-complete hook the whole catalog reads only face 6 (the face-1 readers are all Shred,
        /// which fires on the SAVE hook). The real roll is untouched: the dice beat, the log and the hit
        /// count all still show and use the 6s that were actually rolled - a player who rolled a 6 must
        /// see a 6.
        /// </para>
        /// </summary>
        public static IDiceResults HideSixesFromRules(IDiceResults rolls)
        {
            const int Six = 6, Five = 5;
            if (rolls.SideMax < Six || rolls.SideMin > Five) return rolls;
            if (rolls.At(Six) <= 0f) return rolls;

            float[] faces = new float[rolls.SideMax - rolls.SideMin + 1];
            for (int side = rolls.SideMin; side <= rolls.SideMax; side++)
            {
                faces[side - rolls.SideMin] = rolls.At(side);
            }
            faces[Five - rolls.SideMin] += faces[Six - rolls.SideMin];
            faces[Six - rolls.SideMin] = 0f;
            return new DiceResults(faces, rolls.SideMin);
        }
    }
}
