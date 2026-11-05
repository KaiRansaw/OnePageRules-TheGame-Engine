
namespace FDG.Stages
{
    public class PileInStage : StageBase<ICombatActionContext>
    {
        public StageBinding OnPiledIn;

        public PileInStage(IGameContext gameContext, IStateMachineLayer<ICombatActionContext> parent) : base(gameContext, parent)
        {
            OnPiledIn = new StageBinding(this);
        }

        public override async Task Enter(ICombatActionContext context)
        {
            GameContext.LogDebug("Entered pile in stage.");

            var chargingUnit = context.AttackingUnit.GetValue();
            var defendingUnit = context.DefendingUnit.GetValue();

            // #159: every enemy of the defender OTHER than the charging unit — third parties, or a unit it is
            // already engaged with — is a hard obstacle the defender must not pile through on its way to the
            // charger. Without this a defender plowed straight into a different enemy's base (deeply overlapping).
            var otherEnemies = MovementUtilities.GetEnemyModelFootprints(
                context.DefendingUnit, GameContext, excludeUnit: context.AttackingUnit);

            // #205: the defender's FRIENDLY units are hard obstacles too - a pile-in may not stack the defender
            // on top of its own teammates any more than on a third-party enemy. Pile-in stops the mover short of
            // every obstacle footprint, so folding friendlies into the same list keeps it off them.
            var friendlyObstacles = MovementUtilities.GetFriendlyModelFootprints(context.DefendingUnit, GameContext);
            var obstacles = otherEnemies.Concat(friendlyObstacles).ToList();

            // #458 Fast-Play: Leader-Based Melee (GF / AoF Advanced Rulebook 3.6.0, p17): "When a unit wants to
            // charge, only measure distances from the leader and move it into base contact first. Once the
            // leader has been moved, you must place all other models in the unit in base contact with enemy
            // models, or as close as possible in any way you like, as long as you maintain unit coherency.
            // Then all models from the target that are not in base contact may move in the same way."
            //
            // The charger's leader made the measured move; the game places the rest of the charging unit
            // (no distance limit, coherency kept, the leader held still), then the target's models come in
            // the same way. Placed by the game rather than asked for model by model - the whole point of
            // Fast-Play is that nobody measures these, and the pile-in placement is already the "as close as
            // possible, in coherency" search this needs.
            bool fastPlay = FastPlay.Melee(GameContext.Settings);
            if (fastPlay)
            {
                var attackerObstacles = MovementUtilities.GetEnemyModelFootprints(
                        context.AttackingUnit, GameContext, excludeUnit: context.DefendingUnit)
                    .Concat(MovementUtilities.GetFriendlyModelFootprints(context.AttackingUnit, GameContext))
                    .ToList();
                var placed = PileInUtilities.ComputePileInMoves(
                    defendingUnit.ModelBindings,
                    chargingUnit.ModelBindings,
                    GameContext.TableState.Terrain.Objects,
                    attackerObstacles,
                    maxDistanceInches: FastPlay.UnmeasuredMoveInches,
                    holdStill: chargingUnit.LeaderModelId);
                foreach (var move in placed) move.Model.GetValue().SetPosition(move.NewPosition);
                if (placed.Count > 0)
                    GameContext.Log($"Fast-Play: {placed.Count} of {chargingUnit.Name}'s models placed around the " +
                                    $"enemy after its leader charged in.");
            }

            var moves = PileInUtilities.ComputePileInMoves(
                chargingUnit.ModelBindings,
                defendingUnit.ModelBindings,
                GameContext.TableState.Terrain.Objects,
                obstacles,
                maxDistanceInches: fastPlay ? FastPlay.UnmeasuredMoveInches : PileInUtilities.MAX_PILE_IN_DISTANCE_INCHES);

            foreach (var move in moves)
            {
                move.Model.GetValue().SetPosition(move.NewPosition);
            }

            if (moves.Count > 0)
            {
                // #330: report how many defenders actually reached base contact, so a formation-y
                // result is visible in the log instead of hiding behind a bare move count.
                int inContact = PileInUtilities.CountDefendersInBaseContact(
                    chargingUnit.ModelBindings, defendingUnit.ModelBindings);
                GameContext.Log($"Pile in: {moves.Count} defender model(s) moved toward the charging unit ({inContact} in base contact).");
            }

            await OnPiledIn.Activate(context);
        }
    }
}
