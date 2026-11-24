using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Utilities;

namespace FDG.Stages
{
    public class CoverCheckStage : CombatStage<CoverCheckResults, CoverCheckStage, ICombatMetadata>
    {
        public CoverCheckStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent) : base(gameContext, parent)
        {
        }

        protected override async Task RunStage(ICombatMetadata metaData, Func<CoverCheckResults, Task> onFinished)
        {
            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(
                GameContext.TableState, metaData.AttackingUnit, metaData.DefendingUnit,
                GameContext.Settings.SeeThroughFriendlyUnits);
            IReadOnlyList<ITerrain> terrain = GameContext.TableState.Terrain.Objects
                .Concat(modelBlockers).ToList();

            // #385: the shared majority computation - the same function that feeds the targeting
            // panel's cover flag, so the preview and this roll cannot disagree. Dead models are
            // excluded on both sides there (#158); this stage used to count them. The #201
            // proximity exceptions ride the same call.
            // #456 Leader-Based Shooting: every shot counts as fired from the leader's position, so cover
            // is judged from there - a judgement call, recorded in claude/house-rules.md.
            CoverMajority.Result majority = CoverMajority.Evaluate(
                metaData.AttackingUnit, metaData.DefendingUnit, terrain,
                GameContext.Settings.CoverProximityExceptionsEnabled,
                FastPlay.ShootingOrigin(metaData.AttackingUnit.GetValue(), GameContext.Settings),
                FastPlay.SimplifiedTerrain(GameContext.Settings));

            // Majority rule: more than half of the living defending models in cover → +1 defense bonus.
            int bonus = majority.HasCover ? 1 : 0;
            // Battlefield Effects - Heavy Storm: the whole table counts as Cover; cover pieces give +2.
            // Random Events - Shimmering Mist: a unit in a misted quarter counts as in Cover.
            int stormed = BattlefieldEffects.CoverBonus(GameContext, metaData.DefendingUnit.GetValue(), majority.HasCover, bonus);
            if (stormed != bonus) GameContext.Log(BattlefieldEffects.Is(GameContext, BattlefieldEffects.HeavyStorm)
                ? $"Heavy Storm: Defense +{stormed} from cover{(majority.HasCover ? " (in a cover piece)" : " (the whole table counts as Cover)")}."
                : "Shimmering Mist: the target counts as in Cover (+1 Defense).");
            bonus = stormed;

            // Army Terrain - Protective Dome: "the area within 6" of this faction terrain counts as Cover terrain for
            // friendly units".
            if (bonus == 0 && ArmyTerrain.InFriendlyDome(GameContext, metaData.DefendingUnit.GetValue()))
            {
                bonus = 1;
                GameContext.Log("Protective Dome: the target counts as in Cover.");
            }

            // #042 Blast: the attacking weapon ignores cover — drop the bonus. Derived from the attacker's
            // rules via the shared query so the cover stage, targeting options, and movement options agree.
            if (bonus > 0 && SightRuleQueries.IgnoresCover(
                    metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator))
            {
                GameContext.Log($"Cover ignored by {metaData.AttackingUnit.GetValue().Name}'s weapon (Blast).");
                bonus = 0;
            }

            // Command Points - Lightning Reflexes (target counts as in Cover) / Heightened Senses (ignore Cover), for
            // this shooting. Shooting only - spells and melee never get cover.
            if (!metaData.IsMelee && !metaData.IsSpell)
            {
                bool ignores = SightRuleQueries.IgnoresCover(metaData.AttackingUnit.GetValue(), metaData.WeaponType, GameContext.RuleEvaluator);
                int before = bonus;
                bonus = await CommandPoints.CoverStratagems(GameContext, metaData.AttackingUnit.GetValue(),
                    metaData.DefendingUnit.GetValue(), bonus, ignores);
                if (bonus != before) GameContext.Log($"Command Points: cover {(bonus > before ? "given" : "ignored")} for this shooting.");
            }

            if (bonus > 0)
                GameContext.Log($"Cover: {majority.ModelsInCover}/{majority.LivingDefenders} defending models in cover. Defense +{bonus}.");
            else
                GameContext.Log($"Cover: {majority.ModelsInCover}/{majority.LivingDefenders} defending models in cover. No bonus.");

            await onFinished(new CoverCheckResults(bonus));
        }
    }
}
