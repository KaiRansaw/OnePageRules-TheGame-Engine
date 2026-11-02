using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #439 - the Command Group picks, in the four games whose books call for them.
    ///
    /// <para>
    /// GF: Firefight, AoF: Skirmish, GF: Star Quest and AoF: Quest all print Banner and Musician as
    /// <i>"This model's unit <b>and up to 2 friendly units picked before the start of the game</b>
    /// get ..."</i>. Grimdark Future and Age of Fantasy give them to the bearer's own unit and nobody
    /// else, so in those two this stage asks nothing and falls straight through -
    /// <see cref="CoreRulesProfile.CommandGroupExtraPicks"/> is the per-system switch, the same seam
    /// #437 used for the shooting-target cap and mandatory strike-back.
    /// </para>
    ///
    /// <para>
    /// <b>Why here.</b> "Before the start of the game" puts it in deployment, and Kai's call
    /// (2026-11-01) was to ask each game rather than store the picks in the army list - in a campaign
    /// the units you bring change from mission to mission, so a pick saved in the list could name a unit
    /// that is not in this game. It runs BEFORE units are placed, which also keeps the book's
    /// <i>"picked models that are deployed lose the rule"</i> meaningful: a picked unit may still be in
    /// Ambush or Scout reserve when the bearer dies.
    /// </para>
    ///
    /// <para>
    /// <b>The grant.</b> A picked unit takes a <see cref="TokenType.RuleGrant"/> token naming the rule,
    /// which <c>RuleEvaluator</c> resolves back into a live rule through the same path as a static
    /// attachment - the mechanism auras already use, so Banner on a picked unit behaves exactly as it
    /// does on the bearer's own. <see cref="TokenClearTrigger.ManualOnly"/> and
    /// <see cref="ELifetime.Aura"/>, because it lasts until the bearer dies rather than until the end of
    /// anything.
    /// </para>
    /// </summary>
    public class PickCommandGroupUnitsStage : StageBase<IDeploymentContext>
    {
        public StageBinding ToDeployAllUnits;

        public PickCommandGroupUnitsStage(IGameContext gameContext,
            IStateMachineLayer<IDeploymentContext> parent)
            : base(gameContext, parent)
        {
            ToDeployAllUnits = new StageBinding(this);
        }

        public override async Task Enter(IDeploymentContext context)
        {
            int picksAllowed = CoreRulesProfile.For(context.GameContext).CommandGroupExtraPicks;

            if (picksAllowed <= 0)
            {
                context.LogDebug(
                    "Command Group picks: this game's books give Banner and Musician to the bearer's own "
                    + "unit only - nothing to pick.");
                await ToDeployAllUnits.Activate(context);
                return;
            }

            // Army order, then each army's own unit order - not a sort. It must be identical on host and
            // client so the same requests are issued in the same sequence over the wire.
            List<DataBinding<UnitData>> allUnits = context.TableState()
                .Armies.Objects
                .OfType<ArmyData>()
                .SelectMany(army => army.UnitBindings)
                .ToList();

            foreach (DataBinding<UnitData> bearer in allUnits)
            {
                foreach (string ruleName in CoreRulesProfile.ConferringCommandGroupRules)
                {
                    if (!CarriesRule(bearer.GetValue(), ruleName)) continue;

                    await PickFor(context, bearer, ruleName, picksAllowed, allUnits);
                }
            }

            await ToDeployAllUnits.Activate(context);
        }

        private async Task PickFor(IDeploymentContext context, DataBinding<UnitData> bearer, string ruleName,
            int picksAllowed, List<DataBinding<UnitData>> allUnits)
        {
            UnitData bearerUnit = bearer.GetValue();

            for (int pickNumber = 1; pickNumber <= picksAllowed; pickNumber++)
            {
                List<DataBinding<UnitData>> candidates = allUnits
                    // By ID, not by binding reference. Both come out of the same ArmyData.UnitBindings
                    // list today, so reference equality happens to hold - but if a binding is ever
                    // re-wrapped on the way here, reference equality would stop matching and a bearer
                    // would be offered itself as a pick.
                    .Where(candidate => candidate.GetValue().ID != bearerUnit.ID
                        // "friendly" is read as the bearer's OWN army. In a team game an ally's units are
                        // not fed by your banner - the books assume two players and say only "friendly",
                        // and the narrower reading is the one that cannot hand a buff to someone else's
                        // army by accident. Recorded as a judgement call in claude/house-rules.md.
                        && candidate.GetValue().PlayerID == bearerUnit.PlayerID
                        && candidate.GetValue().GetIsAlive()
                        // Already carries it natively, or was already picked for it - either way there is
                        // nothing a second grant would add.
                        && !CarriesRule(candidate.GetValue(), ruleName)
                        && !HoldsGrantFor(candidate.GetValue(), ruleName))
                    .ToList();

                if (candidates.Count == 0)
                {
                    if (pickNumber == 1)
                    {
                        context.Log($"{ruleName} on {bearerUnit.Name}: no other friendly unit to confer it on.");
                    }

                    return;
                }

                var options = candidates
                    .Select(candidate => new PickCommandGroupUnitRequest.ValidOption(
                        candidate, candidate.GetValue().Name))
                    .ToList();

                var request = new PickCommandGroupUnitRequest(bearerUnit.PlayerID, bearerUnit.Name, ruleName,
                    pickNumber, picksAllowed, options,
                    Array.Empty<PickCommandGroupUnitRequest.InvalidOption>());

                CancellableResult<DataBinding<UnitData>> reply = await context.GameContext.PlayerRequester
                    .RequestDecision<PickCommandGroupUnitRequest, CancellableResult<DataBinding<UnitData>>>(
                        request);

                if (reply is not Selected<DataBinding<UnitData>> selected)
                {
                    // Declining is a legal answer to "up to 2", and it ends this bearer's picking rather
                    // than skipping one slot - there is no reason a player would decline the first and
                    // want the second.
                    context.Log($"{ruleName} on {bearerUnit.Name}: no further units picked.");
                    return;
                }

                Grant(selected.Value, ruleName, bearerUnit);
                context.Log($"{ruleName} on {bearerUnit.Name} also covers {selected.Value.GetValue().Name}.");
            }
        }

        /// <summary>
        /// Hands the rule to a picked unit the same way <see cref="Effect.Aura"/> does, through a
        /// <see cref="RuleOperation.GrantTokenToUnit"/> so token provenance and valence are handled by the
        /// one code path rather than by a second, nearly-identical one here.
        /// </summary>
        /// <remarks>
        /// <c>internal</c> so the tests grant through THIS code rather than rebuilding the token
        /// themselves - a test that constructs its own token proves the mechanism works and says nothing
        /// about whether the stage uses it, which is how a correct-looking grant with the wrong clear
        /// trigger would have shipped.
        /// </remarks>
        internal static void Grant(DataBinding<UnitData> unit, string ruleName, IUnit bearer)
        {
            var operations = new List<RuleOperation>
            {
                new RuleOperation.GrantTokenToUnit(unit.GetValue(),
                    // OwnerDestroyed + OwnerUnitID is the whole of "If this model is killed, picked
                    // models that are deployed lose the rule": UnitDestructionNotifier already calls
                    // TokenClearService.ClearForDestroyedOwner on every alive-to-dead transition, so the
                    // grants a bearer placed are revoked when that bearer dies, per bearer, with no new
                    // code. It is the mechanism Unstoppable Mark uses for exactly this reason.
                    new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.OwnerDestroyed(),
                        Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.Aura),
                        OwnerUnitID: bearer.ID)),
            };

            OperationApplier.ApplyTokenOperations(operations);
        }

        /// <summary>
        /// Whether the unit carries the rule statically. Matching as
        /// <see cref="Condition.UnitHasRule"/> does it - case-insensitive, and against the requested name
        /// as well as the definition's, so an aliased Banner still counts.
        /// <para>
        /// <b>Model-level rules are checked too</b>, which <see cref="Condition.UnitHasRule"/> does not
        /// do. A Command Group upgrade normally folds onto the unit, because its effect is unit-wide -
        /// but the hero merge RELOCATES a joined hero's own unit-scoped rules onto the hero model
        /// (#183/#197), so a hero carrying a Banner into a squad would be invisible to a unit-level
        /// check and its banner would silently confer on nobody. The same reasoning
        /// <see cref="Condition.MostModelsHaveThisRule"/> gives for reading both places.
        /// </para>
        /// </summary>
        internal static bool CarriesRule(IUnit unit, string ruleName) =>
            unit.RuleDefinitions.Any(rule => Names(rule, ruleName))
            || unit.Models.Any(model => model.RuleDefinitions.Any(rule => Names(rule, ruleName)));

        private static bool Names(ResolvedRule rule, string ruleName) =>
            string.Equals(rule.Definition.Name, ruleName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rule.RequestedName, ruleName, StringComparison.OrdinalIgnoreCase);

        /// <summary>Whether the unit already holds a granted copy of the rule.</summary>
        internal static bool HoldsGrantFor(IUnit unit, string ruleName)
        {
            foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.Payload is TokenPayload.RuleGrant grant
                    && string.Equals(grant.RuleName, ruleName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
