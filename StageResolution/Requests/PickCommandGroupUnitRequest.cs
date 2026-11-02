using FDG.Data;
using Newtonsoft.Json;

namespace FDG.StageResolution.Requests
{
    /// <summary>
    /// #439 - one of the friendly units a Banner or Musician confers on, picked before the game starts.
    ///
    /// <para>
    /// In GF: Firefight, AoF: Skirmish, GF: Star Quest and AoF: Quest these upgrades read
    /// <i>"This model's unit <b>and up to 2 friendly units picked before the start of the game</b>
    /// get ..."</i>, where Grimdark Future and Age of Fantasy give them to the bearer's own unit and
    /// nobody else. The stage asks once per pick, so "up to 2" is two requests and cancelling the first
    /// takes none.
    /// </para>
    ///
    /// <para>
    /// <b>Its own type, not a bare <see cref="CancellableSelectionRequest{T}"/> of UnitData</b>, for the
    /// reason <see cref="ChooseUnitToActivateRequest"/> gives: resolvers dispatch on TYPE. The official
    /// AI already binds plain unit selection to its deployment-order resolver, so a bare selection here
    /// would be answered by "which unit should I deploy next" logic. Each front end adapts this type onto
    /// the unit picker it already has (<c>DerivedRequestAdapter</c>), so no new resolver, no new network
    /// message and no new GUI surface.
    /// </para>
    ///
    /// <para>
    /// <b>Cancellable by design</b> - the book says "up to 2", so declining is a legal answer and not a
    /// Back button. <see cref="CancellableSelectionRequest{T}"/> rather than
    /// <see cref="SelectionRequest{T}"/> because that models the decline as a real reply instead of a
    /// null binding, which the latter's own remarks warn does not survive the networked reply path.
    /// </para>
    /// </summary>
    public class PickCommandGroupUnitRequest : CancellableSelectionRequest<UnitData>
    {
        /// <summary>The unit carrying the Banner or Musician. Shown in the prompt, not used to resolve.</summary>
        public string BearerUnitName { get; }

        /// <summary>The rule being conferred - "Banner" or "Musician".</summary>
        public string RuleName { get; }

        /// <summary>Which pick this is (1-based) and how many are allowed, for the prompt.</summary>
        public int PickNumber { get; }

        public int PicksAllowed { get; }

        public PickCommandGroupUnitRequest(PlayerID targetPlayerID, string bearerUnitName, string ruleName,
            int pickNumber, int picksAllowed,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions)
            : base(targetPlayerID,
                // Game-facing, so ASCII only (CLAUDE.md). Phrased "Banner on X" rather than "X's Banner"
                // because unit names are often plural and "Warriors's Banner" is what that produced.
                $"{ruleName} on {bearerUnitName}: pick friendly unit {pickNumber} of {picksAllowed} "
                + "to gain it (or decline)",
                validOptions, invalidOptions,
                displayName: $"Picking units for {ruleName}")
        {
            BearerUnitName = bearerUnitName;
            RuleName = ruleName;
            PickNumber = pickNumber;
            PicksAllowed = picksAllowed;
        }

        [JsonConstructor]
        public PickCommandGroupUnitRequest(PlayerID targetPlayerID, TaskID taskID, string instructions,
            IReadOnlyList<ValidOption> validOptions, IReadOnlyList<InvalidOption> invalidOptions,
            string? displayName, string bearerUnitName, string ruleName, int pickNumber, int picksAllowed)
            : base(targetPlayerID, taskID, instructions, validOptions, invalidOptions, displayName)
        {
            BearerUnitName = bearerUnitName;
            RuleName = ruleName;
            PickNumber = pickNumber;
            PicksAllowed = picksAllowed;
        }
    }
}
