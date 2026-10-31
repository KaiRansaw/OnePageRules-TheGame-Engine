using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #103 — the AI never spends its own spell tokens to boost or hinder another Caster's cast; it doesn't
    /// yet reason about whether the swing is worth the tokens, so it always declines (spends 0) rather than
    /// burning tokens on an arbitrary cast. Same conservative stance as the pre-attack ability skip. A real
    /// policy (protect a high-value cast / spoil a dangerous enemy one) is a future refinement.
    /// <para>
    /// #414 — the same answer covers the Power half (GFSQ v2.1.0), and for AI units it is not even a
    /// policy gap: the book's <c>Caster(X) [AI Units]</c> entry spends tokens only, and an AI unit has no
    /// Power to spend. This resolver also drives a bot playing the HERO side (<c>--all-ai</c>), which does
    /// have Power - declining there is the same conservative stance as above, not a rules claim.
    /// </para>
    /// </summary>
    public class AiCastAssistResolver : IStageResolver<CastAssistRequest, CastAssistReply>
    {
        public Task<CastAssistReply> Resolve(CastAssistRequest request) =>
            Task.FromResult(CastAssistReply.None);
    }
}
