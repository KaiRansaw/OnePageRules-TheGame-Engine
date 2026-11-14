using System.Linq;

using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Utilities;
using System;
using System.Collections.Generic;

namespace FDG.Stages
{

    public class DetermineSaveRollsNeededStage<TMetadata> 
        : CombatStage<DetermineSaveRollNeededResults, DetermineSaveRollsNeededStage<TMetadata>, TMetadata>
        where TMetadata : ICombatMetadata
    {
        public DetermineSaveRollsNeededStage(IGameContext gameContext, IStateMachineLayer<TMetadata> parent) 
            : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<DetermineSaveRollNeededResults, Task> onFinished)
        {
            List<PendingSaveRolls> pendingSaveRollsList = new List<PendingSaveRolls>();

            // #006: a joined hero saves at the unit's Defense until it is the sole survivor, then its own.
            int baseDefense = HeroStatRules.GetSaveDefense(metaData.DefendingUnit.GetValue());
            CoverCheckResults coverResults = QueryForResultOrThrowException<CoverCheckResults>(metaData);

            RollToHitResults rollToHitResults = QueryForResultOrThrowException<RollToHitResults>(metaData);

            // Fortified (defender) reduces the WEAPON's AP, floored at 0 — so it does nothing against an
            // AP(0) hit, unlike a flat +1 save. Rule-granted AP (Rending/Thrust) rides SaveModifier below
            // and is not reduced here.
            int ap = Math.Max(0, metaData.WeaponType.ArmorPenetration - rollToHitResults.ArmorPenetrationReduction);

            // #042 save-modifier rules (Rending) folded their net modifier at hit-roll-complete and
            // carried it here; a negative delta to the save roll raises the threshold. Subtract it,
            // mirroring the hit stage's `HitRollNeeded -= Net(Hit)` convention.
            // #033 granted "+X to defense" buffs on the defender (one-shot / duration) lower the threshold,
            // same sign as cover; one-shot ("next time") grants are consumed by this attack's saves.
            // #407: ConsumeDetailed so each buff can name itself ("Protector +1") instead of all of them
            // collapsing into one "buff" chip. Read before the one-shot grants drain, as on the hit stage.
            // Campaign events 36 / 46: +1 Defense "whilst" inside an area - checked now, where it stands.
            CampaignEventAreas.RefreshRollBonus(GameContext, metaData.DefendingUnit.GetValue(), ERollKind.Save);
            (int grantedDefense, IReadOnlyList<GrantedModifier> grantedDetail) =
                GrantedRollModifiers.ConsumeDetailed(metaData.DefendingUnit.GetValue(), ERollKind.Save);

            // #100 #14b: AP-side attacker-bonus markers on the defender — persistent (Piercing Target)
            // counted, spendable (Piercing Tag/Spotter) claimed via a prompt to the attacking player
            // "before rolling to block". A positive net RAISES the defender's threshold.
            int markerNet = await TargetMarkerSpend.ConsumeNet(GameContext,
                metaData.AttackingUnit.PlayerID(), metaData.DefendingUnit.GetValue(), ERollKind.Save);

            // #434: the campaign Underdog Bonus on the save. The DEFENDER is the one rolling, so its
            // owner gets +1 and the attacker's owner gets -1 - UnderdogSpend works that out from who
            // owns the rolling unit. Subtracted like the granted defense buffs, since a positive delta
            // to the roller lowers its threshold. No-op outside a campaign.
            int underdogNet = await UnderdogSpend.ConsumeNet(
                GameContext, ERollKind.Save, metaData.DefendingUnit.GetValue());

            // Extra Actions: Last Stand -1, Hunker Down +1 against shooting, Defensive Stance +1 in melee.
            IReadOnlyList<GrantedModifier> extraActions = ExtraActions.SaveModifiers(
                metaData.DefendingUnit.GetValue(), metaData.IsMelee, metaData.IsSpell);
            if (extraActions.Count > 0)
            {
                grantedDefense += ExtraActions.Net(extraActions);
                grantedDetail = grantedDetail.Concat(extraActions).ToList();
            }

            // Challenge Bonuses (Solo & Co-Op 3.6.0): a losing AI's units get +1 to Defense rolls.
            IReadOnlyList<GrantedModifier> challenge = ChallengeBonuses.SaveModifiers(GameContext,
                metaData.DefendingUnit.GetValue());
            if (challenge.Count > 0)
            {
                grantedDefense += ExtraActions.Net(challenge);
                grantedDetail = grantedDetail.Concat(challenge).ToList();
                GameContext.Log($"{metaData.DefendingUnit.GetValue().Name}: {challenge[0].Source} +1 to Defense.");
            }

            // Pierce: "Ignores all positive Defense modifiers on the target." The rule-based ones went at the hit roll;
            // cover and every granted bonus (spells, items, Hunker Down, Defensive Stance, the Challenge Bonus, the
            // defender's Underdog +1) are added here, so they go here. Penalties stay (Last Stand -1).
            int coverBonus = coverResults.DefenseRollBonus;
            if (rollToHitResults.PiercesDefenseBonuses)
            {
                int keptGranted = grantedDetail.Count > 0 ? grantedDetail.Where(g => g.Delta <= 0).Sum(g => g.Delta) : Math.Min(0, grantedDefense);
                int dropped = Math.Max(0, coverBonus) + (grantedDefense - keptGranted) + Math.Max(0, underdogNet);
                coverBonus = Math.Min(0, coverBonus);
                grantedDetail = grantedDetail.Where(g => g.Delta <= 0).ToList();
                grantedDefense = keptGranted;
                underdogNet = Math.Min(0, underdogNet);
                if (dropped > 0)
                    GameContext.Log($"Pierce: {metaData.DefendingUnit.GetValue().Name} loses +{dropped} Defense (cover and bonuses).");
            }

            int baseDefenseWithAP = baseDefense + ap - coverBonus - rollToHitResults.SaveModifier
                - grantedDefense + markerNet - underdogNet;

            GameContext.Log($"Base roll to save is {baseDefense}, AP {ap}, cover -{coverBonus}, " +
                $"Rending/save mods {rollToHitResults.SaveModifier}, markers +{markerNet}, " +
                $"underdog {underdogNet:+0;-0;0}, " +
                $"effective threshold {baseDefenseWithAP} (not yet clamped).");

            foreach (SuccessfulHitInfo hits in rollToHitResults.SuccessfulHitList)
            {
                // #016 Per-hit-group effects: a save modifier tagged on this specific hit group shifts
                // its threshold, stacking on top of the unit-wide modifiers already folded into
                // baseDefenseWithAP (same sign convention — a negative raises the threshold).
                int saveNeeded = baseDefenseWithAP - hits.SaveModifier;
                PendingSaveRolls pendingSaveRolls = new PendingSaveRolls(hits.Rolls, saveNeeded, hits.Source);

                pendingSaveRollsList.Add(pendingSaveRolls);
            }

            DetermineSaveRollNeededResults results = new DetermineSaveRollNeededResults(pendingSaveRollsList);
            results.ThresholdTags = ComposeThresholdTags(baseDefense, ap, coverBonus,
                rollToHitResults.SaveModifierTags, grantedDefense, markerNet, grantedDetail);
            await onFinished(results);
        }

        // #245: the save beats' modifier chips - the attack-wide arithmetic behind the threshold
        // ("Defense 4+ | AP 2 | Cover +1"). Null (no chips, no extended beat) when the threshold is
        // just the unmodified defense. Per-bucket differences (Rending) stay in the beat label.
        // Internal for tests.
        /// <param name="grantedDetail">#407: the granted Defense buffs itemised by source, so each names
        /// itself rather than merging into one "buff" chip. Empty falls back to the summed chip, which is
        /// what the pre-roll forecast passes.</param>
        internal static List<string>? ComposeThresholdTags(int baseDefense, int ap, int coverBonus,
            List<string>? saveModifierTags, int grantedDefense, int markerNet = 0,
            IReadOnlyList<GrantedModifier>? grantedDetail = null)
        {
            List<string> tags = new List<string> { $"Defense {baseDefense}+" };
            if (ap > 0) tags.Add($"AP {ap}");
            if (coverBonus > 0) tags.Add($"Cover +{coverBonus}");
            if (saveModifierTags != null) tags.AddRange(saveModifierTags);
            if (grantedDetail is { Count: > 0 })
            {
                foreach (GrantedModifier granted in grantedDetail)
                {
                    if (granted.Delta != 0) tags.Add(granted.Describe());
                }
            }
            else if (grantedDefense != 0)
            {
                tags.Add($"buff {RollTags.Delta(grantedDefense)}");
            }
            if (markerNet != 0) tags.Add($"markers +{markerNet}");

            return tags.Count > 1 ? tags : null;
        }
    }
}