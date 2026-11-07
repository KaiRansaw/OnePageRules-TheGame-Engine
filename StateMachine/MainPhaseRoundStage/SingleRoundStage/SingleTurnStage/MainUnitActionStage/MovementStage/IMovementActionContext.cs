
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    public interface IMovementActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> MovingUnit { get; }

        public float MaxAdvanceDistance { get; }

        public float MaxRushDistance { get; }

        public float MaxChargeDistance { get; }

        /// <summary>
        /// The largest Advance budget any living model of the unit has - the unit scalar, or a joined
        /// hero's own larger one (#093). This is the allowance the "did it advance?" question has to be
        /// asked against, because the recorded move distance is the MAX across models
        /// (<c>MovementUtilities.GetMaxMoveDistance</c>): comparing a hero's 8" against the unit's 6"
        /// scalar would call a legal advance a rush. Handed to
        /// <c>IUnitActionContext.RegisterMoveFinished</c> so the shoot gate can use the allowance that was
        /// actually in force (#290).
        /// </summary>
        public float MaxModelAdvanceDistance { get; }

        /// <summary>
        /// This model's own Advance/Rush/Charge budget (#093): the unit base + unit movement rules + that
        /// model's OWN movement rules (a joined hero's Fast/Slow). Equals the unit-wide scalars for every
        /// model of a unit with no per-model movement rules. Returns false (and the unit scalars) when the
        /// unit can't move or the model has no computed budget.
        /// </summary>
        public bool TryGetModelMoveBudget(IModel model, out float advance, out float rush, out float charge);

        public List<ITerrain> RelevantTerrain { get; }

        public bool TryGetMovementDistance(out float distance);

        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths);

        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths);

        /// <summary>
        /// The player abandoned the move at the path prompt. No path was submitted and nothing moved, so
        /// <c>MovementStage</c> must leave without registering a move distance.
        /// </summary>
        public bool MoveCancelled { get; }

        /// <summary>Record that the move was abandoned before a path was submitted.</summary>
        public void RegisterMoveCancelled();

        /// <summary>
        /// The dangerous-terrain test <c>ApplyNonMovementTerrainEffectsStage</c> rolled, whose wounds are
        /// still PENDING. Landed and animated by <c>ExecuteMoveStage</c> after the move beat, so a
        /// casualty falls at the destination it walked to rather than blinking out of the start line
        /// while the unit is still standing there. <c>None</c> when nothing tested.
        /// </summary>
        public MovementExecutor.DangerousTerrainResult PendingDangerousTerrain { get; }

        /// <summary>Record the rolled-but-unapplied dangerous-terrain test for <c>ExecuteMoveStage</c>.</summary>
        public void RegisterDangerousTerrainRoll(MovementExecutor.DangerousTerrainResult result);
    }

    public class MovementActionContext : IMovementActionContext
    {
        public IGameContext GameContext { get; }


        public DataBinding<UnitData> MovingUnit { get; private set; }

        public float MaxAdvanceDistance
        {
            get
            {
                return _canMove ? _maxAdvanceDistance : 0f;
            }
        }

        public float MaxRushDistance
        {
            get
            {
                return _canMove ? _maxRushDistance : 0f;
            }
        }

        public float MaxChargeDistance
        {
            get
            {
                return _canMove ? _maxChargeDistance : 0f;
            }
        }

        public float MaxModelAdvanceDistance
        {
            get
            {
                if (!_canMove) return 0f;
                float max = _maxAdvanceDistance;
                foreach (ModelBudget budget in _modelBudgets.Values)
                {
                    if (budget.Advance > max) max = budget.Advance;
                }
                return max;
            }
        }

        public List<ITerrain> RelevantTerrain { get; private set; }

        public MovementExecutor.DangerousTerrainResult PendingDangerousTerrain { get; private set; }
            = MovementExecutor.DangerousTerrainResult.None;

        public void RegisterDangerousTerrainRoll(MovementExecutor.DangerousTerrainResult result)
            => PendingDangerousTerrain = result;

        private bool _canMove;
        private float _maxAdvanceDistance;
        private float _maxRushDistance;
        private float _maxChargeDistance;
        private readonly Dictionary<ModelID, ModelBudget> _modelBudgets = new();

        private readonly record struct ModelBudget(float Advance, float Rush, float Charge);

        private bool _hasMoved = false;
        private float? _movementDistance;
        private List<ModelMoveEntry> _paths;


        public MovementActionContext(IGameContext gameContext, DataBinding<UnitData> movingUnit)
        {
            GameContext = gameContext;

            MovingUnit = movingUnit;

            MovementContextPrecursor precursor = MovementContextPrecursor.GetDefault(gameContext);

            _canMove = precursor.CanMove;
            _maxAdvanceDistance = precursor.MaxAdvanceDistance;
            _maxRushDistance = precursor.MaxRushDistance;
            _maxChargeDistance = precursor.MaxChargeDistance;
            RelevantTerrain = precursor.RelevantTerrain;
            
            //Movement special rules.
            IUnit unit = movingUnit.GetValue();
            MovementModifierSink movementModifiers = new MovementModifierSink();

            AccumulateMovementRules(unit, EActionType.Advance, _maxAdvanceDistance, movementModifiers);
            AccumulateMovementRules(unit, EActionType.Rush, _maxRushDistance, movementModifiers);
            AccumulateMovementRules(unit, EActionType.Charge, _maxChargeDistance, movementModifiers);

            _maxAdvanceDistance += movementModifiers.Net(EActionType.Advance);
            _maxRushDistance += movementModifiers.Net(EActionType.Rush);
            _maxChargeDistance += movementModifiers.Net(EActionType.Charge);

            // "Counts as being in Difficult Terrain" (#153): the whole move is capped at the difficult-terrain
            // limit, after bonuses (the cap is absolute), unless the unit ignores difficult terrain outright
            // (Strider/Flying). Applied to the unit scalars here and to each per-model budget below.
            bool countsAsDifficult =
                MovementRuleQueries.CountsAsInTerrain(unit, GameContext.RuleEvaluator, ECountAsTerrain.Difficult)
                && !MovementRuleQueries.IgnoresDifficultTerrain(unit, GameContext.RuleEvaluator);
            float difficultCap = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES;
            if (countsAsDifficult)
            {
                _maxAdvanceDistance = Math.Min(_maxAdvanceDistance, difficultCap);
                _maxRushDistance = Math.Min(_maxRushDistance, difficultCap);
                _maxChargeDistance = Math.Min(_maxChargeDistance, difficultCap);
            }

            // #093: per-model budgets. Each living model's budget folds the unit's movement rules AND that
            // model's own (a joined hero's Fast/Slow), so a Fast hero can range ahead within coherency while
            // the rest of the unit keeps the unit budget. A native model with no model rules lands exactly on
            // the unit scalars above.
            float baseAdvance = precursor.MaxAdvanceDistance;
            float baseRush = precursor.MaxRushDistance;
            float baseCharge = precursor.MaxChargeDistance;
            foreach (IModel model in unit.Models)
            {
                if (!model.GetIsAlive())
                {
                    continue;
                }

                MovementModifierSink perModel = new MovementModifierSink();
                AccumulateModelMovementRules(unit, model, EActionType.Advance, baseAdvance, perModel);
                AccumulateModelMovementRules(unit, model, EActionType.Rush, baseRush, perModel);
                AccumulateModelMovementRules(unit, model, EActionType.Charge, baseCharge, perModel);
                float modelAdvance = baseAdvance + perModel.Net(EActionType.Advance);
                float modelRush = baseRush + perModel.Net(EActionType.Rush);
                float modelCharge = baseCharge + perModel.Net(EActionType.Charge);
                if (countsAsDifficult)
                {
                    modelAdvance = Math.Min(modelAdvance, difficultCap);
                    modelRush = Math.Min(modelRush, difficultCap);
                    modelCharge = Math.Min(modelCharge, difficultCap);
                }
                _modelBudgets[model.ID] = new ModelBudget(modelAdvance, modelRush, modelCharge);
            }

            // Star Quest: Heavy Rain (random event) makes one table quarter difficult terrain.
            if (QuestActions.IsQuestGame(GameContext) && QuestChallenges.InDifficultQuarter(GameContext, unit)
                && !MovementRuleQueries.IgnoresDifficultTerrain(unit, GameContext.RuleEvaluator))
            {
                _maxAdvanceDistance = Math.Min(_maxAdvanceDistance, difficultCap);
                _maxRushDistance = Math.Min(_maxRushDistance, difficultCap);
                _maxChargeDistance = Math.Min(_maxChargeDistance, difficultCap);
                foreach (ModelID id in _modelBudgets.Keys.ToList())
                {
                    ModelBudget b = _modelBudgets[id];
                    _modelBudgets[id] = new ModelBudget(Math.Min(b.Advance, difficultCap), Math.Min(b.Rush, difficultCap), Math.Min(b.Charge, difficultCap));
                }
            }

            // Extra Actions: Stealth Move "uses an Advance action" - the unit may not move further than its
            // Advance, so the Rush and Charge budgets drop to it. (The token is placed when the action is
            // declared and cleared at the unit's next activation, so it is only ever present while moving
            // during the activation that declared it.)
            if (unit.Tokens.HasToken(TokenType.StealthMove))
            {
                _maxRushDistance = Math.Min(_maxRushDistance, _maxAdvanceDistance);
                _maxChargeDistance = Math.Min(_maxChargeDistance, _maxAdvanceDistance);
                foreach (ModelID id in _modelBudgets.Keys.ToList())
                {
                    ModelBudget b = _modelBudgets[id];
                    _modelBudgets[id] = new ModelBudget(b.Advance, Math.Min(b.Rush, b.Advance), Math.Min(b.Charge, b.Advance));
                }
            }

            // Star Quest: Crippled heroes (and Bad weather) halve movement distance. Extra Actions: so does
            // Last Stand ("halve their movement").
            float factor = QuestActions.MoveFactor(GameContext, unit) * ExtraActions.MoveFactor(unit);
            if (factor < 1f)
            {
                _maxAdvanceDistance *= factor;
                _maxRushDistance *= factor;
                _maxChargeDistance *= factor;
                foreach (ModelID id in _modelBudgets.Keys.ToList())
                {
                    ModelBudget b = _modelBudgets[id];
                    _modelBudgets[id] = new ModelBudget(b.Advance * factor, b.Rush * factor, b.Charge * factor);
                }
            }
        }

        // Read-only projection (#153): a one-shot (NextTrigger) granted movement rule must contribute to
        // ALL THREE action budgets and stay granted for path validation — so nothing here may consume it.
        // The grant is spent once, by ExecuteMoveStage, when the move actually resolves.
        private void AccumulateMovementRules(IUnit unit, EActionType action, float baseDistance,
            MovementModifierSink sink)
        {
            IReadOnlyList<(RuleOperation Op, string RuleName)> operations = GameContext.RuleEvaluator
                .EvaluateAllNamed(new MoveActionDeclaredContext(unit, action, baseDistance, RelevantTerrain),
                    RuleParticipant.Actor(unit));
            sink.ApplyFrom(operations.Select(t => t.Op).ToList());
        }

        // Like AccumulateMovementRules but folds the given model's own rules (AnyOwner union) so a per-model
        // Fast/Slow lands in that model's budget only. Read-only (EvaluateAllNamed, #153): the per-model
        // projection must not consume a one-shot granted movement rule — ExecuteMoveStage spends it on move
        // resolve, so every model's budget must be able to see it.
        private void AccumulateModelMovementRules(IUnit unit, IModel model, EActionType action,
            float baseDistance, MovementModifierSink sink)
        {
            IReadOnlyList<(RuleOperation Op, string RuleName)> operations = GameContext.RuleEvaluator
                .EvaluateAllNamed(new MoveActionDeclaredContext(unit, action, baseDistance, RelevantTerrain),
                    RuleParticipant.Actor(unit, models: new[] { model }));
            sink.ApplyFrom(operations.Select(t => t.Op).ToList());
        }

        public bool TryGetModelMoveBudget(IModel model, out float advance, out float rush, out float charge)
        {
            if (_canMove && _modelBudgets.TryGetValue(model.ID, out ModelBudget budget))
            {
                advance = budget.Advance;
                rush = budget.Rush;
                charge = budget.Charge;
                return true;
            }

            // Fallback to the unit scalars (respecting the can't-move gate), so a caller that hands in a model
            // absent from the map — e.g. one that died after the context was built — still gets a sane cap.
            advance = MaxAdvanceDistance;
            rush = MaxRushDistance;
            charge = MaxChargeDistance;
            return _canMove;
        }

        public void SubmitValidPathTemplate(List<ModelMoveEntry> paths)
        {
            _hasMoved = true;
            // #459 Fast-Play Leader-Based Movement: the unit moved as far as its leader did - that is the
            // distance that decides Advance or Rush. The rest were placed, not measured.
            ModelID? measured = FastPlay.MeasuredMover(MovingUnit.GetValue(), GameContext.Settings);
            List<ModelMoveEntry> counted = measured is { } leader
                ? paths.Where(p => p.Model.GetValue().ID.Equals(leader)).ToList()
                : paths;
            _movementDistance = MovementUtilities.GetMaxMoveDistance(counted.Count > 0 ? counted : paths);
            _paths = paths;
        }

        public bool MoveCancelled { get; private set; }

        public void RegisterMoveCancelled() => MoveCancelled = true;

        public bool TryGetMovementDistance(out float distance)
        {
            if(_hasMoved)
            {
                distance = _movementDistance.Value;
                return true;
            }

            distance = float.NegativeInfinity;
            return false;
        }


        public bool TryGetPaths(out IReadOnlyList<ModelMoveEntry> paths)
        {
            if(_hasMoved)
            {
                paths = _paths;
                return true;
            }

            paths = null;
            return false;
        }
    }

    public struct MovementContextPrecursor
    {
        public bool CanMove;

        public float MaxAdvanceDistance;

        public float MaxRushDistance;

        public float MaxChargeDistance;

        public List<ITerrain> RelevantTerrain;

        public static MovementContextPrecursor GetDefault(IGameContext gameContext)
        {
            MovementContextPrecursor precursor = new MovementContextPrecursor()
            {
                CanMove = true,
                MaxAdvanceDistance = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES,
                MaxRushDistance = GameWideConstants.RUSH_DISTANCE_INCHES,
                MaxChargeDistance = GameWideConstants.CHARGE_DISTANCE_INCHES,
                RelevantTerrain = new List<ITerrain>(gameContext.TableState.Terrain.Objects)
            };

            return precursor;
        }
    }
}
