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
            ["Talent: Motivated"] = "engine-marker: GF: Firefight Campaign - MoraleUtilities.TakeMoraleTest (FirefightCampaign.UseMotivated) passes the first morale test.",
            ["Talent: Crazed"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnAttack grants an extra hit on a charge.",
            ["Talent: Toughened"] = "engine-marker: GF: Firefight Campaign - bookkeeping only - StrikeTeamRules writes Tough(+1) into the unit's file.",
            ["Talent: Natural Talent"] = "engine-marker: GF: Firefight Campaign - bookkeeping only - StrikeTeamRules adds the +1 XP.",
            ["Skill: Leader"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated grants the aura to friendly units within 6\".",
            ["Skill: Instigator"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated grants the aura to friendly units within 6\".",
            ["Skill: Tactician"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated grants the aura to friendly units within 6\".",
            ["Skill: Vanguard"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated grants the aura to friendly units within 6\".",
            ["Skill: Scavenger"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated grants the aura to friendly units within 6\".",
            ["Skill: Mastermind"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnAttack gives nearby enemies -1 Defense.",
            ["Skill: Fanatic"] = "engine-marker: GF: Firefight Campaign - bookkeeping only - StrikeTeamRules writes +1 attack into the hero's melee weapon.",
            ["Skill: Berserker"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.WoundRollModifier adds +1 to the wound effect roll.",
            ["Skill: Suppressor"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnAttack gives the target -1 to hit when shooting.",
            ["Skill: Climber"] = "engine-marker: GF: Firefight Campaign - no effect in this game (no jumping) - kept so the campaign skill shows on the unit.",
            ["Skill: Chemist"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.WoundRollModifier gives nearby friends -1 to the wound effect roll.",
            ["Skill: Herbalist"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.HerbalistIgnores lets nearby friends ignore wounds on a 6.",
            ["Skill: Warden"] = "engine-marker: GF: Firefight Campaign - FirefightCampaign.OnActivated stops nearby friends being Shaken.",
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
