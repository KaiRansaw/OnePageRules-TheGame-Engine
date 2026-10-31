using System.Collections.Generic;
using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #244 — the cast action's spell picker: choose which spell to cast AND how many of the caster's own
    /// extra spell tokens to spend boosting the roll (+1 each, on top of the spell's cost). The reply is a
    /// <see cref="ChooseSpellReply"/>; a negative <see cref="ChooseSpellReply.SpellIndex"/> cancels back to
    /// Choose Action with nothing spent.
    ///
    /// <para><b>Why its own request type rather than a <see cref="StringSelectionRequest"/>.</b> Same seam
    /// argument as <see cref="ChooseAbilityEffectRequest"/>: AI resolvers are swapped in one request type at
    /// a time (<c>docs/ai-agent-plan.md</c> A4), and the Tactician previously had to recognize the spell
    /// picker by sniffing the prompt prefix. It also lets the reply carry the boost count, which a plain
    /// string choice cannot.</para>
    ///
    /// <para><b>Boost usefulness context.</b> The success threshold clamps at 2 — a natural 1 always
    /// fails (GDF core principle) — so boosting past <c>BaseThreshold - 2</c> only matters as a hedge
    /// against enemy Casters hindering (-1 per token, prompted AFTER the caster commits).
    /// <see cref="HinderTokensInRange"/> carries how many hinder tokens enemy Casters within assist range
    /// currently hold, so a UI can gray out boost past <see cref="MaxUsefulBoost"/> and say why.</para>
    /// </summary>
    public class ChooseSpellRequest : IStageTaskRequest<ChooseSpellReply>
    {
        /// <summary>One offered spell. <paramref name="Label"/> is the picker label ("Name (cost)");
        /// <paramref name="Description"/> summarizes the effect, shown as subtext. Non-castable entries
        /// (unaffordable, or no legal target) are display-only, with <paramref name="UnavailableReason"/>
        /// saying why — replying with one is treated as a cancel.</summary>
        public record SpellOption(string Label, string Description, int Cost, bool Castable,
            string? UnavailableReason);

        /// <summary>One Spell Conduit in range: whose it is and what it adds to the roll when a cast is
        /// made from its position.</summary>
        public record RelayOption(string UnitName, int RollBonus);

        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>The unit casting — display (name) and any future canvas highlighting.</summary>
        public DataBinding<UnitData> CastingUnit { get; }

        /// <summary>The caster's current spell-token pool. Boost is additionally capped by
        /// <c>AvailableTokens - Cost</c> of the chosen spell.</summary>
        public int AvailableTokens { get; }

        /// <summary>
        /// #414 - the Power a Star Quest Hero may ALSO put into this roll (+1 per Power, GFSQ v2.1.0), zero
        /// for every other caster. Kept separate from <see cref="AvailableTokens"/> rather than summed into
        /// one spendable number on purpose: the two are not interchangeable. Tokens refresh every round and
        /// are what a second spell this activation would be paid with, while Power is the hero's whole
        /// mission budget that revive, extra actions and failed tests all compete for. A picker that spent
        /// one on the player's behalf would quietly cost them the other.
        /// </summary>
        public int AvailablePower { get; }

        /// <summary>The unmodified cast success threshold (roll this or higher succeeds; base 4).</summary>
        public int BaseThreshold { get; }

        /// <summary>Total spell tokens held by enemy Casters within assist range of the caster right now —
        /// the most the roll can be hindered by (see class remarks).</summary>
        public int HinderTokensInRange { get; }

        /// <summary>All of the army's spells in stable order. The reply's index points into this list.</summary>
        public IReadOnlyList<SpellOption> Spells { get; }

        /// <summary>
        /// #197 P23 — the <c>Spell Conduit</c>s currently relaying for this caster, usually empty. There is
        /// nothing to reply to: the origin is derived from the targets chosen (a relay origin is never worse
        /// than the caster's own, so there is no decision), and which targets get the bonus is stated on
        /// each row of the target list. This is here so the picker can say the bonus is available at all,
        /// which is where a player is deciding whether a spell is worth casting.
        /// </summary>
        public IReadOnlyList<RelayOption> RelaysInRange { get; }

        /// <summary>The most boost tokens that can possibly matter: enough to reach the 2+ floor (a
        /// natural 1 always fails, so the threshold never drops below 2) plus one per in-range enemy
        /// hinder token. UIs cap the boost picker at min(this, affordable).</summary>
        [JsonIgnore]
        public int MaxUsefulBoost => (BaseThreshold - DiceUtilities.MINIMUM_SUCCESS_ROLL) + HinderTokensInRange;

        [JsonConstructor]
        public ChooseSpellRequest(PlayerID targetPlayerID, TaskID taskID, DataBinding<UnitData> castingUnit,
            int availableTokens, int baseThreshold, int hinderTokensInRange, IReadOnlyList<SpellOption> spells,
            IReadOnlyList<RelayOption>? relaysInRange = null, int availablePower = 0)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            CastingUnit = castingUnit;
            AvailableTokens = availableTokens;
            BaseThreshold = baseThreshold;
            HinderTokensInRange = hinderTokensInRange;
            Spells = spells;
            RelaysInRange = relaysInRange ?? Array.Empty<RelayOption>();
            AvailablePower = availablePower;
            TaskName = "Choosing a Spell";
        }

        public ChooseSpellRequest(PlayerID targetPlayerID, DataBinding<UnitData> castingUnit,
            int availableTokens, int baseThreshold, int hinderTokensInRange, IReadOnlyList<SpellOption> spells,
            IReadOnlyList<RelayOption>? relaysInRange = null, int availablePower = 0)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), castingUnit, availableTokens, baseThreshold,
                   hinderTokensInRange, spells, relaysInRange, availablePower)
        {
        }

        public Task<ChooseSpellReply> Resolve(ChooseSpellReply resolution) => Task.FromResult(resolution);
    }

    /// <summary>
    /// Reply to <see cref="ChooseSpellRequest"/>: which spell (index into the request's
    /// <see cref="ChooseSpellRequest.Spells"/>; negative = cancel the cast, nothing spent), how many
    /// extra tokens the caster spends boosting the roll (+1 each; the stage clamps to what remains after
    /// the spell's cost), and - #414, Star Quest Heroes only - how much Power it spends on top (+1 each,
    /// clamped to the hero's Power; see <see cref="FDG.Stages.CastPower"/>).
    /// <para>
    /// <paramref name="BoostPower"/> defaults to zero so every existing construction site, resolver and
    /// saved reply keeps its old meaning: tokens only.
    /// </para>
    /// </summary>
    public record ChooseSpellReply(int SpellIndex, int BoostTokens, int BoostPower = 0)
    {
        public static ChooseSpellReply Cancel => new ChooseSpellReply(-1, 0, 0);

        /// <summary>Total the roll is shifted by - what the player bought, however they paid for it.</summary>
        [JsonIgnore]
        public int TotalBoost => BoostTokens + BoostPower;
    }
}
