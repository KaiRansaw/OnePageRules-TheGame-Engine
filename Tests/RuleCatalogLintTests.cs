using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    // #166a — catalog-wide "every rule must fire" lint. One case per core rule: RuleFireLint proves
    // each passive entry can produce an operation in a synthesizable situation and each activated
    // ability is offered at a hook a stage polls and resolves to stage-executable operations. This is
    // the automated Breath Attack lesson (SpecialRulesAudit BUG-1): validation/registration/
    // serialization tests all pass for a rule that is a complete no-op in play; this suite cannot.
    //
    // Rules that legitimately fail live on the allowlist below WITH a reason — the allowlist is the
    // documented not-covered ledger. A stale entry (rule starts firing) fails the test too, so the
    // ledger can only shrink honestly.
    [TestFixture]
    public class RuleCatalogLintTests
    {
        private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
        {
            ["Hero"] = "engine-marker: no dispatch entries; consumed by HeroJoinResolver/HeroStatRules " +
                "(join flow, stat substitution).",
            ["Limited"] = "engine-marker: once-per-game weapon gating is enforced by LimitedRules " +
                "reading the LimitedSpent token, not by dispatch entries.",
            ["Disembark"] = "stage-enacted: Effect.Disembark is a deliberate no-op marker; " +
                "ChooseActionStage routes the offer to DisembarkStage, which performs the move.",
            ["Embark"] = "stage-enacted: Effect.Embark is a deliberate no-op marker; ChooseActionStage " +
                "routes the offer to EmbarkStage after the spatial transport-in-range gate.",
            ["Teleport"] = "stage-enacted: Effect.Teleport is a deliberate no-op marker; ChooseActionStage " +
                "routes the offer to TeleportStage, which runs the 6\" placement (#197).",
            ["Delayed Action"] = "engine-marker: no dispatch entries or abilities; ChooseUnitToActivateStage " +
                "detects it by name and offers the hold-back (pass-the-turn) option (#197).",
            // Transport and Re-Deployment were here until the capability seam gave each a real entry at
            // Lifecycle_OnCapabilityQuery: they are no longer detected by name, so they no longer need an
            // exemption. Their absence is the assertion.
            ["Retaliate"] = "engine-marker: no dispatch entries or abilities; ResolveMeleeReflectStage detects " +
                "it by name and deals X hits per wound taken back at the attacker (#197 P11).",
            ["Deathstrike"] = "engine-marker: no dispatch entries or abilities; ResolveMeleeReflectStage detects " +
                "it by name and deals X hits per killed model back at the attacker (#197 P11).",
            ["Trait: Elite"] = "engine-marker: Horde Mode trait with no dispatch entries; MoraleUtilities." +
                "TakeMoraleTest reads its grant (HordeExperience.TryUseElite) for the once-per-round re-roll.",
            ["Trait: Fast Learner"] = "engine-marker: Horde Mode bookkeeping only - records that the unit " +
                "picked a trait (HordeExperience.LevelUp); it has no effect of its own by design.",
            // Campaign Rules v3.6.0 hero traits (#410). Eleven of the eighteen hero skills are
            // "once per game" effects, which the engine has no general per-unit offer for (the same
            // gap already recorded for Trait: Elite), or need a hook context it does not expose. They
            // are markers on purpose: named and described so they show on the unit card and in the
            // Battle Report, with no mechanical effect, rather than silently doing the wrong thing.
            ["Talent: Toughened"] = "engine-marker (#410): bookkeeping only - CampaignRules writes Tough(+3) into the unit's file.",
            ["Talent: Natural Talent"] = "engine-marker (#410): bookkeeping only - CampaignRules adds the +1 XP.",
            ["Skill: Explorer"] = "not-wired (#410): once per game, ignores Difficult and Dangerous terrain - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Motivator"] = "not-wired (#410): +1 to Morale Test rolls triggered by shooting - a morale test does not record what triggered it.",
            ["Skill: Leader"] = "not-wired (#410): +1 to Morale Test rolls in melee - a morale test does not record what triggered it.",
            ["Skill: Idol"] = "not-wired (#410): once per game, may ignore becoming Shaken - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Duellist"] = "not-wired (#410): AP(+1) in melee when charged - a melee strike-back does not record that this unit was the one charged.",
            ["Skill: Berserker"] = "not-wired (#410): once per game, may ignore becoming Fatigued - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Suppressor"] = "not-wired (#410): AP(+1) when using Hold and shooting - the Defense roll's context does not carry whether the attacker moved.",
            ["Skill: Destroyer"] = "not-wired (#410): AP(+1) when using move actions and shooting - the Defense roll's context does not carry whether the attacker moved.",
            ["Skill: Sharpshooter"] = "not-wired (#410): once per game, +12\" range when shooting - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Healer"] = "not-wired (#410): once per game, restore D3 models/wounds on activation - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Tactician"] = "not-wired (#410): once per game, re-roll one roll of any kind in melee - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Strategist"] = "not-wired (#410): once per game, re-roll one roll of any kind when shooting - needs a per-unit once-per-game offer the engine does not have.",
            ["Skill: Mastermind"] = "not-wired (#410): once per game, force an enemy re-roll when targeted - needs a per-unit once-per-game offer the engine does not have.",
            ["Self-Destruct"] = "engine-marker: no dispatch entries or abilities; ResolveMeleeReflectStage detects " +
                "it by name, deals X hits per participating model and self-kills any survivor (#197 P11).",
            ["Assault"] = "engine-marker (#409, OPR 3.6.0): no dispatch entries - which attack pool a weapon " +
                "belongs to is settled from the raw weapon list before any rule evaluation, so " +
                "IUnitExtensions.GetMeleeWeapons consults AssaultRules.IsAssault by name to put an Assault " +
                "weapon in the melee pool as well as the ranged one.",
        };

        // All plus the two standalone definitions FDGServer attaches to every unit at army setup —
        // they never appear in CoreRuleCatalog.All, but they are live in every game.
        private static IEnumerable<SpecialRuleDefinition> LintedRules() =>
            CoreRuleCatalog.All.Concat(new[] { CoreRuleCatalog.Disembark, CoreRuleCatalog.Embark });

        private static IEnumerable<TestCaseData> AllCatalogRules() =>
            LintedRules().Select(rule => new TestCaseData(rule).SetArgDisplayNames(rule.Name));

        [TestCaseSource(nameof(AllCatalogRules))]
        public void EveryCatalogRuleFires(SpecialRuleDefinition rule)
        {
            IReadOnlyList<string> problems = RuleFireLint.Check(rule);

            if (Allowlist.TryGetValue(rule.Name, out string? reason))
            {
                Assert.That(problems, Is.Not.Empty,
                    $"'{rule.Name}' is allowlisted ({reason}) but now passes the fire-lint - " +
                    "remove its stale allowlist entry.");
                return;
            }

            Assert.That(problems, Is.Empty,
                $"'{rule.Name}' fails the fire-lint:{Environment.NewLine}  " +
                string.Join($"{Environment.NewLine}  ", problems));
        }

        // The allowlist must never carry names the catalog doesn't — a renamed or deleted rule would
        // otherwise keep a phantom ledger entry forever.
        [Test]
        public void AllowlistNamesExistInCatalog()
        {
            var catalogNames = LintedRules().Select(r => r.Name).ToHashSet();
            var unknown = Allowlist.Keys.Where(name => !catalogNames.Contains(name)).ToList();
            Assert.That(unknown, Is.Empty,
                "Allowlist entries with no matching catalog rule: " + string.Join(", ", unknown));
        }
    }
}
