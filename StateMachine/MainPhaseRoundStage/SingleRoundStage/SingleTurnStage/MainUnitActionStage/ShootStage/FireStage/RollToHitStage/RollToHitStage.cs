using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Stages
{

    public class RollToHitStage<TMetadata> : CombatStage<RollToHitResults, RollToHitStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public RollToHitStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<RollToHitResults, Task> onFinished)
        {
            // Attack count and hit threshold are both determined in DetermineHitRollStage (#015).
            // Read them here and roll the determined AttackCount — not a local product — so any
            // attack-count modifier that stage folds in is honoured. Rolled BEFORE the attack beat
            // (#239) so the beat can carry the true hit share; the roll's position in the RNG
            // sequence is unchanged.
            DetermineHitRollResults hitRollResults = QueryForResultOrThrowException<DetermineHitRollResults>(metaData);

            float attacks = hitRollResults.AttackCount;
            IDiceResults rollToHitResults = GameContext.DiceRoller.Roll(attacks);

            //We do this here because modifiers shouldn't do it, or else they can't add up in opposite
            //directions. For example, if your Quality is 6, and something gives you +1 to hit, and something
            //else gives you -1 to hit. They should cancel each other out and you'd need a 6. But if you processed
            //the +1 first, and clamped it, it would still be 6, and the -1 would move it to 5.

            int hitRollNeeded = DiceUtilities.ClampSuccessRollNeeded(hitRollResults.HitRollNeeded);

            IDiceResults successfulResults = rollToHitResults.SubsetAtOrAbove(hitRollNeeded);
            IDiceResults failedResults = rollToHitResults.SubsetBelow(hitRollNeeded);

            // Show the attack — tracers (ranged) or a clash (melee) — playing WHILE the dice that
            // resolve it tumble (#238). #276: endpoints are truthful — shots fire only from carriers
            // that can actually strike (LoS + range / melee range), capped at the rolled weapon count
            // (a #157 split shot fires ONE beam, from a different sniper each shot), and a Takedown
            // shot's tracer aims at its picked model.
            (List<Position> attackerPositions, List<Position> targetPositions) =
                AttackBeatPositions.Endpoints(GameContext.TableState, metaData, GameContext.RuleEvaluator,
                    GameContext.Settings.SeeThroughFriendlyUnits);
            // The attackers turn to face what they shoot or fight, and stay so (round bases only - FacingRules).
            FacingRules.FaceTarget(metaData.AttackingUnit.GetValue(), metaData.DefendingUnit.GetValue());

            if (attackerPositions.Count > 0 && targetPositions.Count > 0)
            {
                // Each volley fires every weapon at once; the weapon's Attacks is the volley count.
                // #239: the weapon's effect key + the natural hit share ride along so the front-end
                // picks the right visual/sounds and lands only the shots that hit.
                await GameContext.Presenter.Present(new AttackBeat(metaData.IsMelee,
                    attackerPositions, targetPositions,
                    volleyCount: metaData.WeaponType.Attacks,
                    armorPenetration: metaData.WeaponType.ArmorPenetration,
                    weaponEffect: metaData.WeaponType.EffectKey,
                    hitCount: successfulResults.TotalRolls,
                    attackCount: attacks,
                    attackerUnit: metaData.AttackingUnit.GetValue()?.ID,
                    targetUnit: metaData.DefendingUnit.GetValue()?.ID,
                    weaponName: metaData.WeaponType.Name,
                    weaponCount: metaData.WeaponCount));
            }

            GameContext.Log($"Rolled {successfulResults.TotalRolls} successful hits out of {attacks} total attacks.");

            // #042 hit-roll-complete rules fire here, evaluated against the UNMODIFIED rolls (the synthetic
            // hits added below sit at face 6 and would pollute a natural-6 read). One evaluation feeds them
            // all: extra-hit (Surge/Furious/Relentless), hit-multiplier (Blast), per-hit-AP (Rending/Crack)
            // and whole-attack save (Thrust) rules on the attacker, plus the defender's defensive save mods.
            // #245: evaluated BEFORE the dice beat is presented (it's a pure computation - no awaits, no
            // rolls - so the RNG sequence and grant spends are unchanged) so the beat can carry the
            // face-triggered proc chips ("Furious +2 on 6s") that this evaluation produces.
            IUnit attacker = metaData.AttackingUnit.GetValue();
            IUnit defender = metaData.DefendingUnit.GetValue();
            float distance = UnitCompareUtilities.MinDistanceBetweenUnits(attacker, defender, out _, out _, includeVertical: true);

            // #204: the LIVE named evaluation - spends grants / narrates exactly like EvaluateAll, but also
            // pairs each op with the alias-aware rule name so the save presentation can say "+1 (Furious)",
            // "x3 (Blast)", "Rending AP+1". `operations` is the plain-op view the existing sinks consume.
            // #414 (OPR 3.6.0 Fatigue): "...and those hits don't count as rolls of 6 for special rules".
            // A fatigued unit hits ONLY on 6s, so without this every 6-triggered rule it owns would fire
            // on every hit it scores - Rending promoting all of them to AP(+4), Furious and Surge
            // doubling them. The masked view goes to the RULES only; the beat below and the hit count
            // still carry the dice that were really rolled.
            IDiceResults rulesSeeRolls = metaData.IsMelee && FatigueUtilities.CountsAsFatiguedInMelee(attacker)
                ? FatigueUtilities.HideSixesFromRules(rollToHitResults)
                : rollToHitResults;

            IReadOnlyList<(RuleOperation Op, string RuleName)> named = GameContext.RuleEvaluator.EvaluateAllNamedLive(
                new HitRollCompleteContext(attacker, defender, rulesSeeRolls, distance, metaData.IsMelee,
                    metaData.IsCharging, IsSpell: false,
                    ChargeOriginDistanceInches: metaData.ChargeOriginDistanceInches,
                    UnpredictableBranch: metaData.UnpredictableBranch,
                    // #197 misc (Grounded family): the terrain-proximity conditions read the bearer's
                    // models against the live terrain layout.
                    TerrainPieces: GameContext.TableState.Terrain.Objects.ToList(),
                    HitsScored: successfulResults.TotalRolls),
                // #006 slice F / #093: the attacker batch's living owners contribute their per-model rules
                // under AllOwners semantics (fires only when every owner shares it), so a joined hero's
                // Furious/Relentless fire for a hero-only batch and a homogeneous squad's shared per-model
                // rule fires once — without leaking onto a mixed batch's pooled roll.
                RuleParticipant.Actor(attacker, metaData.WeaponType,
                    HeroStatRules.LivingWeaponBatchOwners(metaData.AttackingUnit.GetValue(), metaData.WeaponType),
                    EModelRuleScope.AllOwners),
                // The defender contributes its DEFENSIVE save modifiers here (Shielded's +1 to defense,
                // Fortified's AP reduction) — the mirror of how DetermineHitRollStage evaluates the defender
                // as Subject for hit modifiers. Its Net(Save) folds into RollToHitResults.SaveModifier below
                // alongside the attacker's whole-attack AP, so a defensive +1 and an attacker's -N net
                // correctly. #183: passing the defender's living models surfaces a joined hero's relocated
                // Shielded/Fortified (gated by AllModelsHaveThisRule) instead of silently dropping them.
                RuleParticipant.Subject(defender, models: HeroStatRules.LivingModels(defender)));

            IReadOnlyList<RuleOperation> operations = named.Select(n => n.Op).ToList();

            // Show the natural to-hit roll (the synthetic extra-hits below aren't dice), enriched with
            // the #245 glance metadata: who is rolling at whom, how the threshold came to be (chips
            // composed in DetermineHitRollStage), and which face-triggered rules fired on it.
            List<string> procTags = ComposeProcTags(named);
            await GameContext.Presenter.Present(
                DiceRolledBeat.From(rollToHitResults, hitRollNeeded, GameContext.Settings.RandomnessType,
                    HitBeatLabel(metaData.WeaponType),
                    RollTags.Count(successfulResults.TotalRolls, "hit"),
                    category: ERollBeatCategory.Offense,
                    context: HitBeatContext(attacker.Name, defender.Name, attacks),
                    modifierTags: hitRollResults.ThresholdTags,
                    procTags: procTags.Count > 0 ? procTags : null));

            // Extra Actions - Covering Fire: "Then, roll one die per hit, and if you roll at least one 4+, then
            // the target must take a Morale Test (regardless of casualties). All hits are then discarded as
            // misses." The test itself is taken once per target, after the shooting (ResolveRangedMoraleStage),
            // so a unit firing several weapons at one target does not test it once per weapon.
            if (!metaData.IsMelee && attacker.Tokens.HasToken(TokenType.CoveringFire))
            {
                int suppressDice = (int)Math.Round(successfulResults.TotalRolls);
                var faces = new List<int>();
                for (int i = 0; i < suppressDice; i++) faces.Add(GameContext.DiceRoller.RollDecisiveFace());
                bool suppressed = faces.Any(face => face >= 4);
                string rolled = faces.Count == 0 ? "no hits" : string.Join(", ", faces);
                GameContext.Log($"Covering Fire: {suppressDice} suppression dice ({rolled}) - "
                    + (suppressed ? $"{defender.Name} must take a Morale Test." : "no effect."));
                if (suppressed)
                {
                    ExtraActions.Mark(defender, TokenType.Suppressed);
                    await GameContext.Announce($"{defender.Name} is pinned by Covering Fire!", tier: EBannerTier.Toast);
                }

                RollToHitResults suppression = new RollToHitResults(new List<SuccessfulHitInfo>(),
                    new List<FailedHitInfo>() { new FailedHitInfo(rollToHitResults) });
                suppression.SelfWounds = operations.OfType<RuleOperation.InflictSelfWounds>().Sum(op => op.Wounds);
                await onFinished(suppression);
                return;
            }

            // #032 per-hit AP (Rending/Crack on an unmodified 6): split the rolled successes so only the
            // matching-face hits carry the raised save threshold; the rest stay at base AP. With no such
            // rule the splitter returns a single group — identical to the old one-group behaviour. The named
            // form (#204) tags each peeled group with its rule so the save stage can say "Rending AP+1".
            RollToHitResults results = new RollToHitResults(
                PerHitApSplitter.Split(successfulResults, named),
                new List<FailedHitInfo>() { new FailedHitInfo(failedResults) });

            // #042 extra-hit rules (Surge / Furious / Relentless) fire at hit-roll-complete: an unmodified 6
            // spawns extra hits. Fold InsertExtraHits ops through the sink and append the total as ONE
            // synthetic group (base AP — a generated hit is not itself a natural 6). #204 keeps this ONE-group
            // structure byte-for-byte (so the save-roll RNG is untouched and outcomes don't move) and only
            // TAGS the group with the responsible rule name(s) for the save presentation ("+3 (Furious)").
            HitInjectionSink hitInjection = new HitInjectionSink();
            hitInjection.ApplyFrom(operations);
            if (hitInjection.TotalExtraHits > 0f)
            {
                results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                    SyntheticHits(hitInjection.TotalExtraHits, rollToHitResults), 0,
                    new HitGroupSource(EHitSourceKind.ExtraHits, ExtraHitRuleNames(named), hitInjection.TotalExtraHits)));
            }

            // #412 extra-ATTACK rules (Psychotic / Crazed): an unmodified 6 buys another attack DIE,
            // not a hit. Thrown here, at the volley's own already-clamped hit threshold, and only the
            // successes are appended - which is why this cannot share the injection sink above (see
            // IExtraAttackRollSink). Thrown ONCE, outside the rule pass, so the book's "does not apply
            // to newly generated attacks" holds by construction: a 6 on an extra die buys nothing.
            //
            // Placed BEFORE the Blast multiplier so a Blast weapon fans out the extra attacks' hits
            // too, matching how it already fans out Furious's injected hits.
            ExtraAttackRollSink extraAttacks = new ExtraAttackRollSink();
            extraAttacks.ApplyFrom(operations);
            if (extraAttacks.TotalAttackDice > 0f)
            {
                int diceToRoll = (int)Math.Floor(extraAttacks.TotalAttackDice);
                if (diceToRoll > 0)
                {
                    IDiceResults extraRolls = GameContext.DiceRoller.Roll(diceToRoll);
                    float extraLanded = extraRolls.AtOrAbove(hitRollNeeded);

                    await GameContext.Presenter.Present(
                        DiceRolledBeat.From(extraRolls, hitRollNeeded,
                            GameContext.Settings.RandomnessType, ExtraAttackRuleNames(named),
                            RollTags.Count(extraLanded, "extra hit"),
                            category: ERollBeatCategory.Offense,
                            context: $"{attacker.Name} presses the attack on {defender.Name}"));

                    if (extraLanded > 0f)
                    {
                        results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                            extraRolls.SubsetAtOrAbove(hitRollNeeded), 0,
                            new HitGroupSource(EHitSourceKind.ExtraHits,
                                ExtraAttackRuleNames(named), extraLanded)));
                    }
                    GameContext.Log($"{attacker.Name} rolled {diceToRoll} extra attack(s) " +
                        $"at {hitRollNeeded}+ -> {extraLanded} extra hit(s).");
                }
            }

            // #042 hit-multiplier rules (Blast) fire at the same hook but resolve "after other rules":
            // multiply the POST-injection hit total. Folded after the injection above so Blast multiplies
            // whatever hits landed (including Surge's).
            //
            // The model-count cap is PER HIT, and the multiplied hits STACK across hits (owner-ruled
            // 2026-07-31): "hits X times for each hit, but no more times than there are models in the
            // target unit" bounds what ONE hit can fan out to, not the volley's total. So an A3 Blast(3)
            // that lands 3 hits on a 3-model unit deals 3 x 3 = 9, and on a 2-model unit 3 x 2 = 6 - not
            // 3 either way, which is what capping the TOTAL used to produce (and which silently deleted
            // save dice the defender owed).
            HitMultiplierSink hitMultiplier = new HitMultiplierSink();
            hitMultiplier.ApplyFrom(operations);

            // #414: in GF: Firefight, AoF: Skirmish and GF: Star Quest, Blast does not stop at the target.
            // "Hits must be split evenly between the target and all enemy units within 3" of any model
            // from the target." BlastSpreadRules decides the whole distribution (including which cap
            // applies - see its remarks, that reading was a real fork); this branch keeps the target's
            // share and queues the rest for ResolveBlastSpillStage.
            //
            // The target's share can be LOWER than the hits that landed - a Blast(1) weapon multiplies
            // nothing and still shares its hits out - so this both adds and removes, where the full-scale
            // branch below only ever adds.
            bool blastSpreads = hitMultiplier.NetMultiplier >= 1
                && operations.OfType<RuleOperation.MultiplyHits>().Any()
                && Rules.Dispatch.CoreRulesProfile.For(GameContext).BlastSplitsBetweenNearbyUnits;

            if (blastSpreads)
            {
                float hitsBefore = TotalHits(results);
                List<IUnit> nearby = Rules.Dispatch.BlastSpreadRules.UnitsInSpreadRange(
                    GameContext.TableState, attacker, defender);
                Rules.Dispatch.BlastSpreadRules.Spread spread = Rules.Dispatch.BlastSpreadRules.Distribute(
                    defender, nearby, hitsBefore, hitMultiplier.NetMultiplier);

                string spreadName = named.FirstOrDefault(n => n.Op is RuleOperation.MultiplyHits).RuleName;
                if (string.IsNullOrEmpty(spreadName)) spreadName = "Blast";

                float targetShare = spread.Target.Hits;
                if (targetShare > hitsBefore)
                {
                    results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                        SyntheticHits(targetShare - hitsBefore, rollToHitResults), 0,
                        new HitGroupSource(EHitSourceKind.BlastMultiplier, spreadName,
                            spread.EffectiveMultiplier)));
                }
                else if (targetShare < hitsBefore)
                {
                    // The hits that leave carry the heaviest AP with them, because that is the one
                    // removal path this stage has (Parry's) and a second one would be a second thing to
                    // keep correct. The books leave the choice open ("players pick how"), so an
                    // arbitrary-but-consistent rule is within them.
                    RemoveHitsWorstFirst(results, hitsBefore - targetShare, rollToHitResults);
                }

                if (spread.Spills)
                {
                    var shares = new List<(DataBinding<UnitData> Unit, float Hits)>();
                    foreach (Rules.Dispatch.BlastSpreadRules.Share share in spread.Others)
                    {
                        if (TryGetBinding(share.Unit, out DataBinding<UnitData> binding))
                        {
                            shares.Add((binding, share.Hits));
                        }
                    }

                    if (shares.Count > 0)
                    {
                        metaData.AddResult(new BlastSpillResults(metaData.AttackingUnit, metaData.WeaponType, shares));
                    }
                }

                GameContext.Log($"{spreadName} x{spread.EffectiveMultiplier}: {hitsBefore} hit(s) -> "
                    + $"{spread.TotalHits} across {spread.Others.Count + 1} unit(s); {defender.Name} takes "
                    + $"{targetShare}.");
            }
            else if (hitMultiplier.NetMultiplier > 1)
            {
                float currentHits = TotalHits(results);
                int targetModelCount = CountLivingModels(defender);
                // Floored at 1 so a target with no living models (unreachable in play - such a unit is not
                // a legal target) leaves the hits untouched rather than erasing them.
                int effectiveMultiplier = Math.Max(1, Math.Min(hitMultiplier.NetMultiplier, targetModelCount));
                float cappedHits = currentHits * effectiveMultiplier;
                float extraHits = cappedHits - currentHits;
                if (extraHits > 0f)
                {
                    // #204: tag the overflow group so the save stage shows "xN (Blast)" (name from the op,
                    // falling back to "Blast" if a book aliased it away). The EFFECTIVE multiplier rides
                    // along, not the authored one, so the beat's arithmetic ("3 hits x2 (Blast) = 6") adds
                    // up when the target's model count trimmed it.
                    string blastName = named.FirstOrDefault(n => n.Op is RuleOperation.MultiplyHits).RuleName;
                    if (string.IsNullOrEmpty(blastName)) blastName = "Blast";
                    results.SuccessfulHitList.Add(new SuccessfulHitInfo(
                        SyntheticHits(extraHits, rollToHitResults), 0,
                        new HitGroupSource(EHitSourceKind.BlastMultiplier, blastName, effectiveMultiplier)));
                    GameContext.Log($"Blast multiplied {currentHits} hits x{effectiveMultiplier} " +
                        $"(authored x{hitMultiplier.NetMultiplier}, capped per hit at {targetModelCount} " +
                        $"target model(s)) -> {cappedHits} total.");
                }
            }

            // #412 Parry: the defender cancels hits that have already landed. Resolved HERE, after
            // injection and the Blast multiplier, because the book caps the pool at "as many dice as
            // hits taken" and that total is only final once every hit-adding rule has had its say.
            //
            // Which hits go: the book says the defender picks, and the defender always picks the ones
            // that would hurt most, so the groups are cancelled worst-first (most negative SaveModifier
            // = highest AP). Automatic for the same reason Counter's "may" is (#409) - declining, or
            // picking worse, can only hurt the player doing the picking, so a prompt would buy nothing.
            HitIgnoreSink hitIgnore = new HitIgnoreSink();
            hitIgnore.ApplyFrom(operations);
            if (hitIgnore.TotalDice > 0f)
            {
                float hitsTaken = TotalHits(results);
                int diceToRoll = (int)Math.Floor(Math.Min(hitIgnore.TotalDice, hitsTaken));
                if (diceToRoll > 0)
                {
                    IDiceResults parryRolls = GameContext.DiceRoller.Roll(diceToRoll);
                    float cancelled = Math.Min(parryRolls.AtOrAbove(hitIgnore.SuccessThreshold), hitsTaken);

                    await GameContext.Presenter.Present(
                        DiceRolledBeat.From(parryRolls, hitIgnore.SuccessThreshold,
                            GameContext.Settings.RandomnessType, "Parry",
                            RollTags.Count(cancelled, "hit ignored"),
                            category: ERollBeatCategory.Defense,
                            context: $"{defender.Name} parries {attacker.Name}"));

                    if (cancelled > 0f)
                    {
                        RemoveHitsWorstFirst(results, cancelled, rollToHitResults);
                        GameContext.Log($"{defender.Name} parried {cancelled} of {hitsTaken} hit(s) " +
                            $"({diceToRoll} dice at {hitIgnore.SuccessThreshold}+).");
                    }
                }
            }

            // Whole-attack save-modifier rules fold their net modifier here and carry it to the save stage
            // via RollToHitResults.SaveModifier — Thrust's charge AP, the defender's Shielded +1. Rending
            // and Crack no longer ride this scalar: their AP is per-hit (split into a face group above), so
            // this now holds only genuinely attack-wide modifiers.
            RollModifierSink saveModifiers = new RollModifierSink();
            saveModifiers.ApplyFrom(operations);
            // #412 Pierce discards the FAVOURABLE half of that total ("ignores all positive Defense
            // modifiers on the target") and leaves the attacker's own AP alone. Clamping the net at
            // zero would have been wrong in the common case: +1 cover against AP(1) nets to 0, and a
            // clamp would then have thrown away the AP as well as the cover.
            bool piercesDefenseBonuses =
                operations.OfType<RuleOperation.IgnorePositiveDefenseModifiers>().Any();
            results.PiercesDefenseBonuses = piercesDefenseBonuses;
            results.SaveModifier = piercesDefenseBonuses
                ? saveModifiers.NetIgnoringPositive(ERollKind.Save)
                : saveModifiers.Net(ERollKind.Save);
            if (piercesDefenseBonuses)
            {
                GameContext.Log($"{attacker.Name} pierces {defender.Name}'s Defense bonuses "
                    + $"(net {saveModifiers.Net(ERollKind.Save)} -> {results.SaveModifier}).");
            }

            // Fortified (defender) reduces the incoming WEAPON AP, floored at the save stage. Sum any
            // reduction ops here and carry the total alongside the save modifier.
            results.ArmorPenetrationReduction = operations
                .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

            // #245: name the rules behind the two scalars above so the save beat's chips can say
            // "Shielded +1" / "Thrust -1" / "Fortified AP-1" instead of an anonymous net number.
            results.SaveModifierTags = ComposeSaveModifierTags(named);

            // #197 Hazardous: the attacker's own unmodified 1s wound it. Counted here, where the UNMODIFIED
            // histogram is in hand, and CARRIED to ApplyWoundsStage - the last stage of every chain that
            // has a hit roll (shooting, melee swings, Strafing) - which applies it once the target's wounds
            // have landed. Owner-ruled 2026-07-29: the shot the models paid for resolves first, and the
            // attacking unit is never torn down while later stages of its own attack are still running.
            // The player learns of it at the moment of the roll, via the proc chip on the to-hit beat above.
            results.SelfWounds = operations.OfType<RuleOperation.InflictSelfWounds>().Sum(op => op.Wounds);

            await onFinished(results);
        }

        // The to-hit beat's header names the weapon being rolled with, mirroring how the save beat is
        // captioned "{weapon}: {breakdown}" (#204) — with several weapon batches firing in one activation
        // the bare "Roll to Hit" said nothing about which one this was. Falls back to the bare label for a
        // nameless weapon. Internal for tests.
        internal static string HitBeatLabel(IWeapon weapon) =>
            string.IsNullOrWhiteSpace(weapon.Name) ? "Roll to Hit" : $"Roll to Hit - {weapon.Name}";

        // The context line: who is rolling at whom, plus the size of the volley. The count is the TOTAL
        // attacks rolled (carriers x the weapon's Attacks, plus any attack-count modifier), so it matches
        // the dice on screen — and it is the only readable count under the probabilistic roller, which
        // draws a success bar instead of dice. Internal for tests.
        internal static string HitBeatContext(string attackerName, string defenderName, float attacks) =>
            $"{attackerName} -> {defenderName}  |  {attacks:0.##} attack{(attacks == 1f ? "" : "s")}";

        // #245: the to-hit beat's gold proc chips - face-triggered rule effects that fired on this roll.
        // Extra hits (Furious/Surge/Relentless) trigger on the top face; per-hit AP (Rending/Crack)
        // names its own face. The front-end highlights top-face successes when any chip is present.
        // Internal for tests.
        internal static List<string> ComposeProcTags(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            List<string> tags = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                switch (op)
                {
                    case RuleOperation.InsertExtraHits extra when extra.Count > 0f:
                        tags.Add($"{RollTags.NameOr(ruleName, "extra hits")} +{extra.Count:0.##} on 6s");
                        break;
                    case RuleOperation.ApplyPerHitSaveModifier perHit when perHit.Delta != 0:
                        // A negative save delta raises the threshold - display as the AP it plays as.
                        tags.Add($"{RollTags.NameOr(ruleName, "AP")} AP+{-perHit.Delta:0.##} on {perHit.OnRollValue}s");
                        break;
                    // #197 Hazardous: the self-wound is applied after the attack resolves, but it is the
                    // 1s on THIS roll that caused it, so the chip belongs on this beat.
                    case RuleOperation.InflictSelfWounds self when self.Wounds > 0f:
                        tags.Add($"{RollTags.NameOr(ruleName, "self-wound")} {self.Wounds:0.##} " +
                            $"self-wound{(self.Wounds == 1f ? "" : "s")}");
                        break;
                }
            }
            return tags;
        }

        // #245: chips naming the whole-attack save modifiers this evaluation produced, for the save
        // beat. Positive is the defender's favor (Shielded +1), negative the attacker's (Thrust -1);
        // Fortified's AP reduction reads as the AP it removes. Internal for tests.
        internal static List<string>? ComposeSaveModifierTags(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            List<string> tags = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                switch (op)
                {
                    case RuleOperation.ApplyRollModifier { Roll: ERollKind.Save } mod when mod.Delta != 0:
                        tags.Add($"{RollTags.NameOr(ruleName, "modifier")} {RollTags.Delta(mod.Delta)}");
                        break;
                    case RuleOperation.ReduceArmorPenetration reduce when reduce.Amount != 0:
                        tags.Add($"{RollTags.NameOr(ruleName, "AP reduction")} AP-{reduce.Amount}");
                        break;
                }
            }
            return tags.Count > 0 ? tags : null;
        }

        // #204: the distinct rule name(s) that added on-6 extra hits, joined for the save presentation
        // (usually one, e.g. "Furious"; "Furious, Surge" if two stack). Presentation only - does not affect
        // the pooled hit total. Empty rule names fall back to "extra hits".
        private static string ExtraHitRuleNames(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            var names = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.InsertExtraHits extra || extra.Count <= 0f) continue;
                string name = string.IsNullOrEmpty(ruleName) ? "extra hits" : ruleName;
                if (!names.Contains(name)) names.Add(name);
            }
            return names.Count > 0 ? string.Join(", ", names) : "extra hits";
        }

        /// <summary>
        /// #412: the same naming courtesy <see cref="ExtraHitRuleNames"/> does, for the rolled
        /// extra-attack group, so the save presentation says "+2 (Psychotic)" rather than a bare total.
        /// </summary>
        private static string ExtraAttackRuleNames(IReadOnlyList<(RuleOperation Op, string RuleName)> named)
        {
            var names = new List<string>();
            foreach ((RuleOperation op, string ruleName) in named)
            {
                if (op is not RuleOperation.InsertExtraAttackRolls extra || extra.Dice <= 0f) continue;
                string name = string.IsNullOrEmpty(ruleName) ? "extra attacks" : ruleName;
                if (!names.Contains(name)) names.Add(name);
            }
            return names.Count > 0 ? string.Join(", ", names) : "extra attacks";
        }

        private static float TotalHits(RollToHitResults results)
        {
            float total = 0f;
            foreach (SuccessfulHitInfo hit in results.SuccessfulHitList)
            {
                total += hit.HitCount;
            }
            return total;
        }

        private static int CountLivingModels(IUnit unit)
        {
            int count = 0;
            foreach (IModel model in unit.Models)
            {
                if (model.GetIsAlive()) count++;
            }
            return count;
        }

        // Bridges a scalar extra-hit count into the IDiceResults the save flow consumes. Injected
        // hits have no real face — only the count (TotalRolls) matters downstream, plus the weapon's
        // AP — so they sit at the top face as automatic successes. The #032 per-hit AP split
        // (Rending/Crack) runs only over the ROLLED successes, never these synthetic groups, so injected
        // hits stay base-AP even though they sit at face 6 — exactly as they must.
        /// <summary>
        /// #412: removes <paramref name="toRemove"/> hits from <paramref name="results"/>, taking them
        /// from the groups that hurt the defender most first - the most negative SaveModifier is the
        /// highest AP, so that is what a defender choosing which hits to ignore would cancel.
        /// A partially drained group is rebuilt as a synthetic histogram of its remaining count, the
        /// same shape the injection and Blast paths already add; an emptied one is dropped.
        /// </summary>
        private static void RemoveHitsWorstFirst(RollToHitResults results, float toRemove,
            IDiceResults template)
        {
            List<int> order = Enumerable.Range(0, results.SuccessfulHitList.Count)
                .OrderBy(i => results.SuccessfulHitList[i].SaveModifier)
                .ToList();

            foreach (int index in order)
            {
                if (toRemove <= 0f) break;

                SuccessfulHitInfo group = results.SuccessfulHitList[index];
                float take = Math.Min(group.HitCount, toRemove);
                if (take <= 0f) continue;

                toRemove -= take;
                float left = group.HitCount - take;
                results.SuccessfulHitList[index] = new SuccessfulHitInfo(
                    SyntheticHits(left, template), group.SaveModifier, group.Source);
            }

            results.SuccessfulHitList.RemoveAll(group => group.HitCount <= 0f);
        }

        /// <summary>
        /// The <see cref="DataBinding{T}"/> for a unit found on the table, by walking the armies.
        /// <para>
        /// #414. The stages work in bindings and <see cref="BlastSpreadRules"/> works in
        /// <see cref="IUnit"/>, because it is pure geometry and has no business knowing about the store.
        /// Matched on the unit ID rather than by reference, the same way HordeShop resolves a unit, and a
        /// unit in no army is skipped rather than guessed at - that is a data fault, not a blast target.
        /// </para>
        /// </summary>
        private bool TryGetBinding(IUnit unit, out DataBinding<UnitData> binding)
        {
            foreach (ArmyData army in GameContext.GameDataStore.GetAllValues<ArmyData>())
            {
                foreach (DataBinding<UnitData> candidate in army.UnitBindings)
                {
                    if (candidate.GetValue().ID.ID == unit.ID.ID)
                    {
                        binding = candidate;
                        return true;
                    }
                }
            }

            binding = default!;
            return false;
        }

        private static IDiceResults SyntheticHits(float count, IDiceResults template)
        {
            int faceCount = template.SideMax - template.SideMin + 1;
            float[] perSide = new float[faceCount];
            perSide[faceCount - 1] = count;
            return new DiceResults(perSide, template.SideMin);
        }
    }

    
}