using FDG.Rules.Definitions;
using FDG.Rules.Foundation;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// The special rules whose DEFINITION differs by game system - registered on top of
    /// <see cref="CoreRuleCatalog"/> (Grimdark Future's wording) by <see cref="CoreRuleCatalog.CreateResolver"/>
    /// for the game being played, so an army's "Slayer" in a Firefight game resolves to Firefight's Slayer.
    ///
    /// <para>
    /// Where a rule differs only in how a stage CONSUMES it (Blast's spread, Fear's reading, Scout's
    /// procedure) the split lives in <see cref="CoreRulesProfile"/>. This file is for the other kind: the
    /// rule's own numbers or hooks are different, so the system needs a different definition under the same
    /// name. Each entry is Army Forge's text for that system (<c>BuiltInAssets/OfficialRuleText.json</c>,
    /// harvested 2026-10-31) - Army Forge wins on numbers (Kai, 2026-10-31), and every one of these is
    /// printed the same way in that system's core book where the book has the rule at all.
    /// </para>
    ///
    /// <para>
    /// Found by diffing every catalog rule's official text across the six systems (2026-11-05). The full
    /// table and what was deliberately left alone is in <c>WorkItems/463</c>.
    /// </para>
    /// </summary>
    public static class SystemRuleVariants
    {
        /// <summary>
        /// Helper rules the variants grant by name. Registered for every system (they are inert unless a
        /// variant names them) so the catalog's aura-integrity check holds.
        /// </summary>
        public static IReadOnlyList<SpecialRuleDefinition> Helpers => new[] { FanaticCharge };

        public static IReadOnlyList<SpecialRuleDefinition> For(EGameSystem system) => system switch
        {
            EGameSystem.GFFirefight => new[] { SlayerAt(9) },
            EGameSystem.AoFSkirmish => new[] { SlayerAt(9), FuriousBuffUpTo3, FanaticHeroOnly },
            EGameSystem.AgeOfFantasy => new[] { FanaticHeroAndUnit },
            EGameSystem.GFStarQuest => new[] { SlayerAt(27), QuestIndirect, FuriousBuffOncePerGameUpTo3 },
            EGameSystem.AoFQuest => new[] { SlayerAt(27), QuestIndirect, FuriousBuffUpTo3Nearest },
            _ => Array.Empty<SpecialRuleDefinition>(),
        };

        // ── Slayer ───────────────────────────────────────────────────────────────────────────────
        // "...this model's weapons get AP(+2) if most models in the target have Tough(3) or higher." GF and
        // AoF print 3; GFF and AoFS 9; both Quest systems 27 - the same x3 / x9 scale as Transport's caps,
        // since an ordinary model is Tough(3) in the skirmish games and Tough(9) in Quest. At Tough(3) in a
        // skirmish game Slayer was AP(+2) against everything on the table.

        private static SpecialRuleDefinition SlayerAt(int tough) => new("Slayer",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(
                        new Condition.And(
                            new Condition.Not(new Condition.TokenPresent(TokenType.MovedThisRound)),
                            new Condition.Not(new Condition.IsMelee())),
                        new Condition.TargetMajorityHasTough(tough)),
                    new Effect.RollModifier(ERollKind.Save, Delta: -2),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(new Condition.IsCharging(), new Condition.TargetMajorityHasTough(tough)),
                    new Effect.RollModifier(ERollKind.Save, Delta: -2),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: $"On a Hold action and shooting, or when charging, this model's weapons get AP(+2) if most models in the target have Tough({tough}) or higher.");

        // ── Indirect ─────────────────────────────────────────────────────────────────────────────
        // Both Quest systems: "May target enemies that are not in line of sight as if in line of sight, and
        // ignores cover from sight obstructions, but gets -1 to hit rolls when shooting after moving." - the
        // pre-3.6.0 shape: no Hold requirement, a -1 to hit instead. The four standard games say "When using
        // Hold actions and shooting, ..." with no penalty (the catalog's definition).

        private static SpecialRuleDefinition QuestIndirect { get; } = new("Indirect",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                    new Condition.Always(),
                    new Effect.IgnoreLineOfSight(),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnSaveRollModifier,
                    new Condition.Always(),
                    new Effect.IgnoreCover(),
                    ELifetime.ThisAttack),
                new HookEntry(EHookID.Shooting_OnHitRollModifier,
                    new Condition.TokenPresent(TokenType.MovedThisRound),
                    new Effect.RollModifier(ERollKind.Hit, Delta: -1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            ERuleScope.Weapon,
            Valence: EValence.Positive,
            Description: "Fires at targets out of line of sight as if in sight and ignores cover from sight "
                + "obstructions, but gets -1 to hit when shooting after moving.");

        // ── Furious Buff ─────────────────────────────────────────────────────────────────────────
        // GF / AoF: "pick one friendly unit within 12"". AoF: Skirmish: "pick up to 3 friendly units".
        // AoF: Quest: "up to 3 NEAREST friendly units". GF: Star Quest: "Only one use per game", up to 3.
        // (GF: Firefight has no Furious Buff of its own in Army Forge; its books keep the catalog's.)

        private static SpecialRuleDefinition FuriousBuffWith(Cost cost, int max, bool nearest, string description) =>
            new("Furious Buff",
                Array.Empty<HookEntry>(),
                new[]
                {
                    new ActivatedAbility(EHookID.Activation_OnBeforeAttackAction, cost,
                        new TargetSelector(12f, 1, max, ETargetAffinity.Friend, false),
                        new Effect.AddRule("Furious", ELifetime.NextTrigger),
                        new Condition.Always()),
                },
                Valence: EValence.Positive,
                Description: description);

        private static SpecialRuleDefinition FuriousBuffUpTo3 { get; } = FuriousBuffWith(new Cost.OncePerActivation(), 3, false,
            "Once per activation, before attacking, give up to 3 friendly units within 12\" Furious for their next attack.");

        /// <summary>
        /// "Up to 3 NEAREST friendly units": the player still picks up to three, but only from the three
        /// nearest - the selector cannot express "nearest", so it is offered from all within 12" and the
        /// player is trusted with it. Recorded in house rules.
        /// </summary>
        private static SpecialRuleDefinition FuriousBuffUpTo3Nearest { get; } = FuriousBuffWith(new Cost.OncePerActivation(), 3, true,
            "Once per activation, before attacking, give up to 3 of the nearest friendly units within 12\" Furious for their next attack.");

        private static SpecialRuleDefinition FuriousBuffOncePerGameUpTo3 { get; } = FuriousBuffWith(new Cost.OncePerGame(), 3, false,
            "Once per game, before attacking, give up to 3 friendly units within 12\" Furious for their next attack.");

        // ── Fanatic ──────────────────────────────────────────────────────────────────────────────
        // A different rule entirely in the fantasy games. GF / GFF / GFSQ: the catalog's after-deployment
        // reposition. AoF: "The Hero and its unit get AP(+1) in melee when charging." AoF: Skirmish: "The
        // Hero gets AP(+1) in melee when charging." (AoF: Quest has no Fanatic text in Army Forge, so its
        // books keep the catalog's - flagged in WorkItems/463, not guessed.)

        /// <summary>The bare charge bonus, granted unit-wide by AoF's Fanatic and carried by AoFS's itself.</summary>
        public static SpecialRuleDefinition FanaticCharge { get; } = new("Fanatic (charging)",
            new[]
            {
                new HookEntry(EHookID.Shooting_OnHitRollComplete,
                    new Condition.And(new Condition.IsCharging(), new Condition.IsMelee()),
                    new Effect.RollModifier(ERollKind.Save, Delta: -1),
                    ELifetime.ThisAttack),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: "Gets AP(+1) in melee when charging.");

        private static SpecialRuleDefinition FanaticHeroOnly { get; } = FanaticCharge with
        {
            Name = "Fanatic",
            Description = "The Hero gets AP(+1) in melee when charging.",
        };

        private static SpecialRuleDefinition FanaticHeroAndUnit { get; } = new("Fanatic",
            new[]
            {
                new HookEntry(EHookID.Lifecycle_OnUnitCreated,
                    new Condition.Always(),
                    new Effect.Aura(FanaticCharge.Name),
                    ELifetime.UntilEndOfGame),
            },
            Array.Empty<ActivatedAbility>(),
            Valence: EValence.Positive,
            Description: "The Hero and its unit get AP(+1) in melee when charging.");
    }
}
