using System;
using FDG.Ai.Resolvers;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Ai.Solo
{
    /// <summary>
    /// #423 - the official AI's Ambush clause, from <b>Solo &amp; Co-Op Rules v3.6.0</b> (identical in all
    /// four books):
    ///
    /// <para>
    /// "<b>Ambush:</b> AI units with Ambush must always be <b>kept in reserve</b>, and must always deploy at
    /// the <b>start of the second round</b>."
    /// </para>
    /// <para>
    /// Only the first half needed building. The arrival is already right: Ambush defers with
    /// <c>MinArrivalRound = 2</c>, the start-of-round stage offers the arrival from round 2 onward, and
    /// <see cref="AiYesNoResolver"/> answers each question with the request's own declared default - which
    /// for "Deploy X from Ambush this round?" is yes. So the unit comes on at the start of round two
    /// without any code here.
    /// </para>
    /// <para>
    /// <b>What this changes, and why it was deliberately the other way.</b>
    /// <see cref="AiStringSelectionResolver"/> always answers the hold-or-deploy prompt with "Deploy
    /// Normally", under a comment explaining that the AI's reserve placement "drops the unit at the first
    /// legal row from a table edge, ignoring objectives and the enemy, so holding only strands the unit".
    /// That was true of the baseline resolver and is no longer true of the official one: since #419 the
    /// book AI places through <see cref="SoloCoOpPlaceObjectsResolver{T}"/>, which aims at the closest
    /// objective and keeps off Difficult and Dangerous ground. So the clause can be followed without
    /// throwing the unit away - and the baseline keeps its old answer, which plan D1 freezes anyway.
    /// </para>
    /// <para>
    /// Everything that is not that one prompt is delegated unchanged, so the companion-action handling and
    /// the first-option fallback stay in the one place that owns them.
    /// </para>
    /// </summary>
    public sealed class SoloCoOpStringSelectionResolver : IStageResolver<StringSelectionRequest, string>
    {
        /// <summary>
        /// The label the deployment prompt uses for holding an Ambush unit. Built from the stage's own
        /// helper rather than typed out, so a change to the wording cannot silently stop matching - the
        /// mistake <see cref="ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE"/>'s own remarks record.
        /// </summary>
        private static readonly string HoldInAmbush = ChooseUnitToDeployStage.HoldChoiceFor("Ambush");

        private readonly AiStringSelectionResolver _inner;

        public SoloCoOpStringSelectionResolver(AiStringSelectionResolver inner) => _inner = inner;

        public Task<string> Resolve(StringSelectionRequest request)
        {
            foreach (string option in request.ValidOptions)
            {
                // Matched on the exact rule name rather than on "any option starting with Hold in":
                // the same prompt serves every rule that defers a deployment, and the book's clause is
                // about Ambush. An army that renames Ambush falls back to the delegate's "deploy
                // normally", which is the pre-#423 behaviour rather than a guess about what the alias
                // meant.
                if (string.Equals(option, HoldInAmbush, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(option);
                }
            }

            return _inner.Resolve(request);
        }
    }
}
