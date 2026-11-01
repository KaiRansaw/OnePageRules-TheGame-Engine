
using FDG.Utilities;
using System;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{

    public class DetermineHitRollStage<TMetadata>
        : CombatStage<DetermineHitRollResults, DetermineHitRollStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineHitRollStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<DetermineHitRollResults, Task> onFinished)
        {
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();

            // #100 #14: this is the first rule-evaluation point of any attack (shooting or melee), so claim
            // the defender's marks here — transfer each marked rule onto the attacker for this attack and
            // remove the mark. Done before the evaluation below so a hit-side marked rule (+1 to hit) applies
            // immediately; later hooks pick it up as a normal grant; the grant is retired at the attacker's
            // next claim.
            ClaimTargetMarks(attacker, defender);

            // #377 — spend one-shot range grants ("+6\" range when shooting once", Battle Rune): the
            // range check itself only ever PEEKS (RangeRuleQueries.EffectiveRange is a non-logging
            // per-frame query that must not consume), so "the next time the effect would apply" is the
            // shot that used the range — here, at the attack's first live evaluation. Shooting only:
            // a melee swing never read the range. The ops themselves are discarded; the live evaluation
            // exists to log the rule and retire its NextTrigger grant.
            if (!metaData.IsMelee)
            {
                GameContext.RuleEvaluator.EvaluateAllNamedLive(
                    new Rules.Dispatch.Contexts.RangeModifierContext(attacker),
                    RuleParticipant.Actor(attacker, metaData.WeaponType));
            }

            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical:true);

            // #245: the NAMED live evaluation — identical to EvaluateAll (logs, spends grants), but each
            // op carries its alias-aware rule name so the to-hit beat's chips can say "Stealth -1".
            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new HitRollModifierContext(attacker, defender, distance, AttackerMoved: metaData.AttackerMoved,
                    IsMelee: metaData.IsMelee, IsCharging: metaData.IsCharging,
                    ChargeOriginDistanceInches: metaData.ChargeOriginDistanceInches,
                    UnpredictableBranch: metaData.UnpredictableBranch,
                    // #197 misc (Grounded family): the terrain-proximity conditions read the bearer's
                    // models against the live terrain layout.
                    TerrainPieces: GameContext.TableState.Terrain.Objects.ToList()),
                // #006 slice F / #093: the attacker batch's living owners contribute their per-model rules
                // under AllOwners semantics — a rule fires only when every owner shares it, so a joined
                // hero's Furious/Relentless/Thrust fire for a hero-only batch (and a homogeneous elite
                // squad's shared per-model rule fires once), without leaking onto a mixed batch's pooled roll.
                RuleParticipant.Actor(attacker, metaData.WeaponType,
                    HeroStatRules.LivingWeaponBatchOwners(metaData.AttackingUnit.GetValue(), metaData.WeaponType),
                    EModelRuleScope.AllOwners),
                // #183: the defender's living models contribute their per-model defensive rules (a joined
                // hero's relocated Evasive/Melee-Evasion/Artillery) under AnyOwner, so the merge no longer
                // hides them; the rule's AllModelsHaveThisRule gate still decides whether it applies.
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));

            IReadOnlyList<RuleOperation> operations = named.Select(n => n.Op).ToList();

            // #042 quality-floor rules (Reliable) set the BASE quality before per-roll modifiers:
            // "treated as 2+, still modifiable". Fold the floor sink and improve the base, then let
            // the roll-modifier sink (Stealth/Artillery/Indirect) stack on top. Stage interprets no op.
            // #006: a weapon batch owned only by a joined hero fires at the hero's Quality (per-model).
            int baseQuality = HeroStatRules.GetAttackQuality(metaData.AttackingUnit.GetValue(), metaData.WeaponType);
            QualityFloorSink qualityFloor = new QualityFloorSink();
            qualityFloor.ApplyFrom(operations);
            if (qualityFloor.HasFloor)
            {
                baseQuality = Math.Min(baseQuality, qualityFloor.Quality);
            }

            // #015 Attack count: how many attack dice this volley rolls (weapon Attacks × weapons
            // firing). Computed here, beside the hit threshold, so attack-count modifiers fold at this
            // point with the same accumulate-before-use discipline as the hit-roll modifiers above.
            // RollToHitStage reads the result. #376: a bonus batch (Bloodthirsty Fighter) overrides the
            // count with its earned (possibly fractional) total instead — the real weapon, a fixed pool.
            float attackCount = metaData.AttackCountOverride
                ?? metaData.WeaponType.Attacks * metaData.WeaponCount;

            // #197 P12 Regenerative Strength: the one attack-count modifier in the corpus. Stage code
            // rather than a rule at EHookID.Shooting_OnPreHitRollCount because it prompts and is gated to
            // one weapon per melee — see RegenerativeStrengthAttacks for why the hook stays dormant. The
            // float is the point: the bonus is a marker total that the probabilistic roller makes
            // fractional, and attackCount has always been a float. Not offered to a bonus batch — its
            // pool is exactly what the blocks earned.
            if (metaData.AttackCountOverride == null)
            {
                attackCount += await RegenerativeStrengthAttacks.Offer(GameContext, attacker,
                    metaData.WeaponType, metaData.IsMelee);
            }

            DetermineHitRollResults results = new DetermineHitRollResults(baseQuality, attackCount);

            RollModifierSink rollModifiers = new RollModifierSink();
            rollModifiers.ApplyFrom(operations);

            // #422 Unstoppable: "ignores all negative modifiers which are not from this model's special
            // rules". Same shape as Pierce at the save roll - a marker operation, and a sink read that
            // drops one part of the stack - except the part dropped is chosen by SOURCE rather than by
            // sign, so the model keeps its own penalties (the HitPenaltyWhenShooting family) and loses
            // everything aimed at it from outside: the target's Stealth, the -1 for shooting an Aircraft.
            RuleOperation.IgnoreForeignNegativeModifiers? ignoresForeignPenalties = operations
                .OfType<RuleOperation.IgnoreForeignNegativeModifiers>().FirstOrDefault();
            int hitModifier = ignoresForeignPenalties is { } immunity
                ? rollModifiers.NetIgnoringForeignNegatives(ERollKind.Hit, immunity.Bearer)
                : rollModifiers.Net(ERollKind.Hit);
            results.HitRollNeeded -= hitModifier;

            if (ignoresForeignPenalties != null && hitModifier != rollModifiers.Net(ERollKind.Hit))
            {
                GameContext.Log($"{attacker.Name} is Unstoppable - penalties not from its own rules are "
                    + $"ignored (net {rollModifiers.Net(ERollKind.Hit)} -> {hitModifier}).");
            }

            // #020 Fatigue: a unit that has already charged or struck back this round — or that is Shaken
            // (#089) — hits only on unmodified 6s in melee, ignoring all modifiers. Computed up front so the
            // one-shot grant consumption below can skip itself rather than spending a buff for no effect.
            bool fatiguedInMelee = metaData.IsMelee && FatigueUtilities.CountsAsFatiguedInMelee(attacker);

            // #033 granted "+X to hit" buffs on the attacker (one-shot / duration) fold in with the same
            // sign convention; one-shot ("next time") grants are consumed by this roll - skipped while
            // fatigued so the buff carries over to the attacker's next (non-fatigued) attack instead.
            // #407: ConsumeDetailed, not ConsumeNet - the itemised buffs are read BEFORE the one-shot
            // grants are drained, so a "next roll" buff can still name itself on the roll it is spent on.
            int grantedNet = 0;
            IReadOnlyList<GrantedModifier> grantedDetail = Array.Empty<GrantedModifier>();
            if (!fatiguedInMelee)
            {
                (grantedNet, grantedDetail) = GrantedRollModifiers.ConsumeDetailed(
                    metaData.AttackingUnit.GetValue(), ERollKind.Hit);
                results.HitRollNeeded -= grantedNet;
            }

            // #100 #14b: attacker-bonus markers on the defender — persistent (Target family) counted,
            // spendable (Tag/Spotter) claimed via a prompt to the attacking player. Skipped while
            // fatigued for the same reason as the granted buffs above: only unmodified 6s hit, so
            // spending markers would waste them.
            int markerNet = 0;
            if (!fatiguedInMelee)
            {
                markerNet = await TargetMarkerSpend.ConsumeNet(GameContext,
                    metaData.AttackingUnit.PlayerID(), defender, ERollKind.Hit);
                results.HitRollNeeded -= markerNet;
            }

            // Star Quest (Vengeful Strike +1 to hit a Shaken enemy; Dread Attack / Martial Strike note the
            // target). Shown with the granted modifiers on the dice beat.
            // Campaign hero skills and talents used to be applied here (the v3.5.1 Firefight Mastermind,
            // Suppressor and Crazed). In Campaign Rules v3.6.0 all three are ordinary rule definitions or
            // once-per-game markers, so the rules engine handles them. See WorkItems/410.

            if (QuestActions.IsQuestGame(GameContext))
            {
                float questDistance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);
                FDG.StateMachine.QuestAttackModifiers quest = QuestFlags.OnAttack(GameContext, attacker, defender,
                    metaData.IsMelee, questDistance, metaData.IsCharging);
                if (!fatiguedInMelee && quest.Net != 0)
                {
                    results.HitRollNeeded -= quest.Net;
                    // #407: carried as named entries rather than summed into grantedNet, so the chips can
                    // say "Vengeful Strike +1" instead of lumping it into a generic "buff".
                    grantedDetail = grantedDetail.Concat(quest.HitModifiers).ToList();
                }
            }

            GameContext.Log($"Base hit roll required is {results.HitRollNeeded} based on attacker's quality.");

            // Override the computed threshold rather than stacking a modifier; the d6 comparison in
            // RollToHitStage then admits only natural 6s.
            if (fatiguedInMelee)
            {
                results.HitRollNeeded = 6;
                GameContext.Log($"{attacker.Name} is fatigued - hits only on unmodified 6s in melee.");
            }

            results.ThresholdTags = ComposeThresholdTags(baseQuality, named, grantedNet, fatiguedInMelee,
                markerNet, grantedDetail);

            await onFinished(results);
        }

        // #245: the to-hit beat's modifier chips - how the threshold came to be. Null (no chips, no
        // extended beat) when the threshold is just the unmodified quality; otherwise the base plus
        // one chip per named contribution, so "Quality 4+ | Stealth -1" reads as the arithmetic behind
        // the badge's "5+". Internal for tests.
        /// <param name="grantedDetail">#407: the granted buffs itemised by source (skills, items, spells,
        /// random events). When supplied, each is named on its own chip - "Vengeful Strike +1" - instead
        /// of collapsing into one "buff" chip, which is what made it impossible to tell from play whether
        /// a particular skill had fired. Falls back to the summed chip when empty, so a caller without
        /// the detail (the pre-roll forecast) still reads correctly.</param>
        internal static List<string>? ComposeThresholdTags(int baseQuality,
            IReadOnlyList<(RuleOperation Op, string RuleName)> named, int grantedNet, bool fatiguedInMelee,
            int markerNet = 0, IReadOnlyList<GrantedModifier>? grantedDetail = null)
        {
            List<string> tags = new List<string> { $"Quality {baseQuality}+" };
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.ApplyRollModifier mod || mod.Roll != ERollKind.Hit || mod.Delta == 0)
                    continue;
                tags.Add($"{RollTags.NameOr(ruleName, "modifier")} {RollTags.Delta(mod.Delta)}");
            }
            if (grantedDetail is { Count: > 0 })
            {
                foreach (GrantedModifier granted in grantedDetail)
                {
                    if (granted.Delta != 0) tags.Add(granted.Describe());
                }
            }
            else if (grantedNet != 0)
            {
                tags.Add($"buff {RollTags.Delta(grantedNet)}");
            }
            if (markerNet != 0) tags.Add($"markers {RollTags.Delta(markerNet)}");
            if (fatiguedInMelee) tags.Add("Fatigued: 6s only");

            return tags.Count > 1 ? tags : null;
        }

        // #100 #14 mark claim: if the defender carries any Mark tokens (placed by a "mark an enemy →
        // friendlies get X against it once" spell), the FIRST attack into it claims them — transfer each
        // marked rule onto the attacker as a one-attack (AttackEnd) grant the read-back then applies across
        // every hook of this attack, and remove the mark so no later attack benefits. The spend is the
        // attack itself, not the dice.
        //
        // The one-attack (AttackEnd) grant is retired here, at the START of the attacker's NEXT attack,
        // rather than at the previous attack's terminus: an attack has several exit paths and AttackEnd has
        // no fired hook, so lazy retirement at the next claim is simpler and robust. Between attacks the
        // spent grant lies inert (its rule only fires at combat hooks, all of which come after this point),
        // so retiring it just-in-time is behaviourally identical to an attack-end sweep.
        private static void ClaimTargetMarks(IUnit attacker, IUnit defender)
        {
            new TokenClearService().ClearAttackEndTokens(new[] { attacker.Tokens });

            List<Token> marks = defender.Tokens.GetAllTokens(TokenType.Mark).ToList(); // snapshot before mutating
            foreach (Token mark in marks)
            {
                if (mark.Payload is TokenPayload.RuleGrant grant)
                {
                    attacker.Tokens.AddToken(new Token(TokenType.RuleGrant, 1,
                        new TokenClearTrigger.AttackEnd(),
                        Payload: new TokenPayload.RuleGrant(grant.RuleName, ELifetime.ThisAttack)));
                }

                defender.Tokens.RemoveTokensWithPayload(mark.Type, mark.OwnerUnitID, mark.Payload, mark.Count);
            }
        }
    }
}