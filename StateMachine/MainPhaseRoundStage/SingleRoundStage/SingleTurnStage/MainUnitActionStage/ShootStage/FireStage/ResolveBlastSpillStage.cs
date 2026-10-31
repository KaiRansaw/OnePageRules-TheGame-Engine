using System.Collections.Generic;
using FDG.Data;

namespace FDG.Stages
{
    /// <summary>
    /// Resolves one unit's share of a blast that spread off the target: its saves, its wounds, its morale.
    ///
    /// <para>
    /// #414. Blast in GF: Firefight, AoF: Skirmish and GF: Star Quest splits its hits between the target
    /// and every enemy unit within 3". The target's share goes through the ordinary firing chain;
    /// <c>RollToHitStage</c> leaves the rest on a <see cref="BlastSpillResults"/>, and this stage drains it
    /// one unit per pass, driven round the loop by <see cref="DetermineMoreBlastSpillStage"/>.
    /// </para>
    ///
    /// <para>
    /// Built on <c>ResolveSpellDamageStage</c>'s shape, which already solved "resolve the same attack
    /// against several units, each with its own saves": a parent stage whose child context is a fresh
    /// combat metadata per receiver, with the save/wound chain as its children. Three things are different,
    /// and each of them is a way a hand-rolled version would be wrong:
    /// </para>
    /// <list type="bullet">
    /// <item>The hits are <b>already final</b>. They have been multiplied and split, so they go through
    /// <see cref="SyntheticHitResolution.ResolveAlreadyFinalHits"/>, which runs only the receiving unit's
    /// defensive fold. The ordinary fold would multiply Blast a second time and - because a synthetic hit
    /// count is carried as dice sitting at the top face - would promote Rending/Crack AP on every spilled
    /// hit.</item>
    /// <item><b>Cover is seeded at zero</b>, which is Blast's own text ("Ignores Cover") rather than a
    /// shortcut. The save stage needs the result present either way.</item>
    /// <item>It is <b>not a spell</b>, so rules that gate themselves out of spell damage (Shielded) apply
    /// here as they would to any shot.</item>
    /// </list>
    ///
    /// <para>
    /// Morale needs no special case: a bystander taken to half strength tests when its wounds are applied,
    /// through the same path a directly targeted unit uses.
    /// </para>
    /// </summary>
    public class ResolveBlastSpillStage : ParentStage<ICombatMetadata, ICombatMetadata>
    {
        public StageBinding OnFinished;

        public ResolveBlastSpillStage(IGameContext gameContext, IStateMachineLayer<ICombatMetadata> parent)
            : base(gameContext, parent)
        {
        }

        protected override ICombatMetadata GetNewChildContext(ICombatMetadata contextSelf)
        {
            contextSelf.QueryForResult(out BlastSpillResults spill);
            (DataBinding<UnitData> unit, float hits) = spill.ConsumeNext();

            GameContext.Log($"Blast spreads {hits} hit(s) onto {unit.GetValue().Name}.");

            SyntheticHitResolution.Result resolved = SyntheticHitResolution.ResolveAlreadyFinalHits(
                GameContext, spill.Attacker.GetValue(), unit.GetValue(), hits, (Weapon)spill.Weapon);

            CombatMetadata metadata = new CombatMetadata(GameContext, spill.Attacker, unit,
                spill.Weapon, weaponCount: 1, isMelee: false, isSpell: false);

            RollToHitResults hitResults = new RollToHitResults(resolved.HitGroups, new List<FailedHitInfo>());
            hitResults.SaveModifier = resolved.SaveModifier;
            hitResults.ArmorPenetrationReduction = resolved.ArmorPenetrationReduction;
            metadata.AddResult(hitResults);
            // "Blast(X): Ignores Cover" - no cover bonus for a spilled hit either, and the save stage
            // needs the result to exist.
            metadata.AddResult(new CoverCheckResults(0));

            return metadata;
        }

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<ICombatMetadata> startingChild)
        {
            OnFinished = new StageBinding(this);

            Dictionary<string, Transition> dictionary = new TransitionSetBuilder(this)
                .AddChild(new DetermineSaveRollsNeededStage<ICombatMetadata>(GameContext, this), out var determineSaveRollsNeeded)
                .AddChild(new RollToSaveStage<ICombatMetadata>(GameContext, this), out var rollToSave)
                .AddChild(new AssignWoundsStage<ICombatMetadata>(GameContext, this), out var assignWounds)
                .AddChild(new ApplyWoundsStage<ICombatMetadata>(GameContext, this), out var applyWounds)
                .AddSibling(nameof(OnFinished), OnFinished, out string finishedEvent)
                .Build();

            startingChild = determineSaveRollsNeeded;

            determineSaveRollsNeeded.BindNextStage(rollToSave)
                .BindNextStage(assignWounds)
                .BindNextStage(applyWounds)
                .BindToEvent(finishedEvent);

            return dictionary;
        }
    }
}
