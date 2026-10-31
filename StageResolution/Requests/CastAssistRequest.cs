using FDG.Data;
using FDG.StageResolution;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #103 — asks one Caster's controller how much to spend on another Caster's cast, before the cast roll
    /// resolves. A friendly Caster adds +1 to the roll per point spent, an enemy Caster subtracts 1 per
    /// point; the reply is a <see cref="CastAssistReply"/> saying how many spell tokens and — #414, Star
    /// Quest Heroes only — how much Power to put in.
    ///
    /// Carries both units (not just names) so a GUI resolver can highlight the assisting unit on the table
    /// and draw a line to the casting unit — blue for a friendly boost, orange for an enemy disruption — and
    /// surface the assister's token count. CLI/AI resolvers just read the counts.
    /// </summary>
    public class CastAssistRequest : IStageTaskRequest<CastAssistReply>
    {
        public PlayerID TargetPlayerID { get; }
        public TaskID TaskID { get; }
        public string TaskName { get; }

        /// <summary>The Caster being asked to spend (its controller is <see cref="TargetPlayerID"/>).</summary>
        public DataBinding<UnitData> AssistingUnit { get; }

        /// <summary>The Caster whose cast is being swayed.</summary>
        public DataBinding<UnitData> CastingUnit { get; }

        /// <summary>True when the assister is friendly to the caster (+1 each); false for an enemy (-1 each).</summary>
        public bool IsFriendly { get; }

        /// <summary>The most tokens this assister may spend (its current spell-token pool).</summary>
        public int AvailableTokens { get; }

        /// <summary>
        /// #414 — the Power a Star Quest Hero may spend here instead of, or as well as, tokens (GFSQ v2.1.0
        /// Caster(X): "+1 to the roll per token/Power, or to give an enemy Caster within 18" -1 to their roll
        /// per token/Power"). Zero for AI casters and ordinary army casters, which have no Power.
        /// <para>
        /// This is why a Hero holding **no tokens at all** must still be offered this prompt: before 2.1.0
        /// an empty token pool meant there was nothing to decide, and the stage skipped such a caster
        /// outright. A hero with 4 Power can now counter-spell on an empty pool.
        /// </para>
        /// </summary>
        public int AvailablePower { get; }

        public string SpellName { get; }

        /// <summary>What the spell being swayed actually does (SpellText.Describe), shown as subtext so the
        /// assister's controller can judge whether it is worth spending - the same text the caster's own
        /// spell picker shows. Empty when the caster's spell list has no description for it.</summary>
        public string SpellDescription { get; }

        [JsonConstructor]
        public CastAssistRequest(PlayerID targetPlayerID, TaskID taskID, DataBinding<UnitData> assistingUnit,
            DataBinding<UnitData> castingUnit, bool isFriendly, int availableTokens, string spellName,
            string spellDescription = "", int availablePower = 0)
        {
            TargetPlayerID = targetPlayerID;
            TaskID = taskID;
            AssistingUnit = assistingUnit;
            CastingUnit = castingUnit;
            IsFriendly = isFriendly;
            AvailableTokens = availableTokens;
            AvailablePower = availablePower;
            SpellName = spellName;
            SpellDescription = spellDescription ?? "";
            TaskName = "Assisting a Spell Cast";
        }

        public CastAssistRequest(PlayerID targetPlayerID, DataBinding<UnitData> assistingUnit,
            DataBinding<UnitData> castingUnit, bool isFriendly, int availableTokens, string spellName,
            string spellDescription = "", int availablePower = 0)
            : this(targetPlayerID, new TaskID(Guid.NewGuid()), assistingUnit, castingUnit, isFriendly,
                   availableTokens, spellName, spellDescription, availablePower)
        {
        }

        public Task<CastAssistReply> Resolve(CastAssistReply resolution) => Task.FromResult(resolution);
    }

    /// <summary>
    /// Reply to <see cref="CastAssistRequest"/>: how many spell tokens and how much Power this Caster puts
    /// into the cast being swayed. Each point moves the roll by one, in the direction
    /// <see cref="CastAssistRequest.IsFriendly"/> gives. The stage clamps both to what the assister actually
    /// holds, so a resolver cannot wound a hero by overspending.
    /// <para>
    /// A record rather than the bare <c>int</c> this request used to reply with: the two currencies are not
    /// interchangeable (see <see cref="CastAssistRequest.AvailablePower"/>), so which one a counter-spell
    /// was paid from has to be the replying player's choice rather than a rule the stage applies for them.
    /// <see cref="None"/> is the decline, and what every automated resolver answers.
    /// </para>
    /// </summary>
    public record CastAssistReply(int Tokens, int Power = 0)
    {
        public static CastAssistReply None => new CastAssistReply(0, 0);

        /// <summary>How far this reply moves the roll, before the friendly/enemy sign is applied.</summary>
        [JsonIgnore]
        public int Total => Tokens + Power;
    }
}
