using FDG.Ai.Resolvers;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FDG.Ai.StarQuest
{
    /// <summary>What an AI unit does this activation, from the Star Quest AI flowchart.</summary>
    public enum EQuestAiMove { Hold, Advance, Rush, Charge, RushToGoal }

    public sealed record QuestAiPlan(EQuestAiMove Kind, IUnit? Target, Position? Goal, float Range, string Why);

    /// <summary>
    /// GF: Star Quest AI activations (Core Rules v2.1.0):
    /// <list type="number">
    /// <item>Are any enemies in line of sight <b>or within 12" but not in line of sight</b>? Yes: step 2.
    /// No: step 3.</item>
    /// <item>If you move, will any enemy be in range to shoot or charge? Yes: Advance to get in range and
    /// shoot the closest, <b>or Hold and shoot if in range</b>, else Charge the closest if that deals more
    /// damage than shooting. No: Rush to the closest.</item>
    /// <item>Within 3" of the AI Goal (the objective marker closest to the unit)? Yes: skip the activation.
    /// No: Rush to the nearest AI Goal.</item>
    /// </list>
    /// Heroes on the AI's side (a bot-run party) keep the normal solo bot.
    /// <para>
    /// #412 brought this to 2.1.0 from 2.0.1, and both additions change how the enemy feels:
    /// </para>
    /// <para>
    /// <b>Step 1's "or within 12" but not in line of sight"</b> means a unit with a wall between it and
    /// the party no longer wanders off to its objective - it hunts. Under 2.0.1 breaking line of sight
    /// made the whole enemy force lose interest, which is the single biggest reason Star Quest played as
    /// hide-and-seek. Note what it implies at step 2: an enemy it cannot SEE cannot be shot, but it can
    /// still be charged, so the usual answer for an unseen enemy within 12" is to come round the corner
    /// after it.
    /// </para>
    /// <para>
    /// <b>Step 2's "or Hold and shoot if in range"</b> is the subtler one. Before, a unit already in range
    /// still Advanced, which moved it closer for no gain and - because Advance is a move - gave the party
    /// a unit standing in the open. Holding keeps it where it is, in whatever cover it was in.
    /// </para>
    /// </summary>
    public static class StarQuestAiBrain
    {
        public const float GoalRadiusInches = 3f;

        /// <summary>
        /// How close an enemy it cannot see still counts as worth engaging - step 1's "or within 12" but
        /// not in line of sight" (GFSQ Core Rules v2.1.0). New in 2.1.0; under v2.0.1 line of sight was
        /// the only trigger.
        /// </summary>
        public const float UnseenEngagementInches = 12f;

        public static IUnit? FindUnit(ITableState table, UnitID id) => table.Units.Objects.FirstOrDefault(u => u.ID.Equals(id));

        public static bool UsesQuestAi(IUnit unit) => unit is not UnitData { IsQuestHero: true };

        public static List<IUnit> Enemies(ITableState table, PlayerID me, IUnit? attacker = null) =>
            table.Units.Objects.Where(u => !ITeamExtensions.AreAllied(table.Teams.Objects, me, u.PlayerID)
                && u.GetIsAlive() && u.GetIsOnBattlefield()
                && !QuestFlags.CannotBeTargeted(table.Progress.Quest, u, attacker)).ToList();

        public static float Distance(IUnit a, IUnit b) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(a, b, out _, out _, includeVertical: false);

        public static bool Sees(IUnit a, IUnit b, List<ITerrain> terrain) =>
            a.Models.Where(m => m.GetIsAlive()).Any(ma => b.Models.Where(m => m.GetIsAlive())
                .Any(mb => LineOfSightUtilities.HasLineOfSight(ma.Position, mb.Position, terrain)));

        public static float MaxRange(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive()).SelectMany(m => m.Weapons).Select(w => w.RangeInches).DefaultIfEmpty(0f).Max();

        /// <summary>Average wounds the unit's weapons of one kind would deal to the target (hit, then fail to block).</summary>
        /// <param name="reachInches">Ranged only: how far away the target will be; weapons that can't reach it are skipped.</param>
        public static float ExpectedWounds(IUnit attacker, IUnit target, bool melee, float reachInches = 0f)
        {
            int quality = attacker is UnitData ua ? ua.Quality : 4;
            int defense = target is UnitData ut ? ut.Defense : 4;
            float pHit = Math.Clamp((7f - quality) / 6f, 1f / 6f, 5f / 6f);
            float total = 0f;
            foreach (IModel model in attacker.Models.Where(m => m.GetIsAlive()))
                foreach (Weapon w in model.Weapons)
                {
                    bool isMelee = w.RangeInches <= 0f;
                    if (isMelee != melee || (!melee && w.RangeInches < reachInches)) continue;
                    int needed = defense + w.ArmorPenetration;
                    float pBlock = needed > 6 ? 1f / 6f : Math.Clamp((7f - needed) / 6f, 1f / 6f, 5f / 6f);
                    total += w.Attacks * pHit * (1f - pBlock);
                }
            return total;
        }

        public static QuestAiPlan Plan(ITableState table, PlayerID me, IUnit unit)
        {
            if (!unit.Models.Any(m => m.GetIsAlive())) return new(EQuestAiMove.Hold, null, null, 0f, "no models");
            var terrain = table.Terrain.Objects.ToList();
            List<IUnit> enemies = Enemies(table, me, unit);
            List<IUnit> visible = enemies.Where(e => Sees(unit, e, terrain)).OrderBy(e => Distance(unit, e)).ToList();

            // #412, step 1 on the 2.1.0 wording: "any enemies in line of sight OR WITHIN 12" BUT NOT IN
            // LINE OF SIGHT". Under 2.0.1 this was line of sight alone, so breaking it made the whole
            // enemy force lose interest and walk off to its objective - which is why Star Quest played as
            // hide-and-seek. An enemy it cannot see still cannot be SHOT (the shoot target below is
            // picked from `visible`, and that is deliberate), but it can be charged or rushed at, so the
            // usual answer for an unseen hero within 12" is to come round the corner after it.
            List<IUnit> engaged = enemies
                .Where(e => Sees(unit, e, terrain) || Distance(unit, e) <= UnseenEngagementInches)
                .OrderBy(e => Distance(unit, e))
                .ToList();

            // Phantasm Grenade: enemies treat the marker as a hero - a visible one closer than any hero draws them in.
            QuestProgress? quest = table.Progress.Quest;
            var phantasms = QuestTreasure.MarkerSpots(quest, "phantasm")
                .Where(p => unit.Models.Where(m => m.GetIsAlive()).Any(m => LineOfSightUtilities.HasLineOfSight(m.Position, p, terrain)))
                .OrderBy(p => DistanceToPoint(unit, p)).ToList();
            if (phantasms.Count > 0 && (engaged.Count == 0 || DistanceToPoint(unit, phantasms[0]) < Distance(unit, engaged[0])))
                return new(EQuestAiMove.RushToGoal, null, phantasms[0], 0f, "drawn to a phantasm");

            if (engaged.Count > 0)
            {
                IUnit closest = engaged[0];
                float d = Distance(unit, closest);
                float range = MaxRange(unit);
                float advance = GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES;
                bool canCharge = d <= GameWideConstants.CHARGE_DISTANCE_INCHES;
                IUnit? shootTarget = range > 0f ? visible.FirstOrDefault(e => Distance(unit, e) <= range + advance) : null;

                if (canCharge)
                {
                    float melee = ExpectedWounds(unit, closest, melee: true);
                    float shooting = shootTarget == null ? 0f : ExpectedWounds(unit, shootTarget, melee: false, reachInches: Math.Max(0f, Distance(unit, shootTarget) - advance));
                    if (melee > shooting || shootTarget == null)
                        return new(EQuestAiMove.Charge, closest, null, 0f, $"charge {closest.Name} ({melee:0.0} vs {shooting:0.0} shooting)");
                }
                if (shootTarget != null)
                    return new(EQuestAiMove.Advance, shootTarget, null, range, $"advance and shoot {shootTarget.Name}");
                return new(EQuestAiMove.Rush, closest, null, 0f, $"rush at {closest.Name}");
            }

            // Noise Grenade: the nearest noise marker becomes the AI Goal.
            var noise = QuestTreasure.MarkerSpots(quest, "noise").OrderBy(p => DistanceToPoint(unit, p)).ToList();
            if (noise.Count > 0)
            {
                if (DistanceToPoint(unit, noise[0]) <= 1f) return new(EQuestAiMove.Hold, null, noise[0], 0f, "at the noise");
                return new(EQuestAiMove.RushToGoal, null, noise[0], 0f, "drawn to a noise marker");
            }
            if (QuestMission.AiGoal(table) is { } missionGoal)
            {
                if (DistanceToPoint(unit, missionGoal) <= GoalRadiusInches)
                    return new(EQuestAiMove.Hold, null, missionGoal, 0f, "holding its AI Goal");
                return new(EQuestAiMove.RushToGoal, null, missionGoal, 0f, "rush to the mission's AI Goal");
            }
            IObjective? goal = AiGoal(table, unit);
            if (goal == null)
            {
                IUnit? any = enemies.OrderBy(e => Distance(unit, e)).FirstOrDefault();
                return any == null ? new(EQuestAiMove.Hold, null, null, 0f, "nothing to do")
                    : new(EQuestAiMove.Rush, any, null, 0f, $"no objective - rush at {any.Name}");
            }
            if (DistanceToPoint(unit, goal.Position) <= GoalRadiusInches)
                return new(EQuestAiMove.Hold, null, goal.Position, 0f, "holding its AI Goal");
            return new(EQuestAiMove.RushToGoal, null, goal.Position, 0f, $"rush to objective {goal.HordeNumber}");
        }

        /// <summary>The objective marker closest to the unit.</summary>
        public static IObjective? AiGoal(ITableState table, IUnit unit) =>
            table.Objectives.Objects.OrderBy(o => DistanceToPoint(unit, o.Position)).FirstOrDefault();

        public static float DistanceToPoint(IUnit unit, Position p) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => MathF.Max(0f, Position.GetDistance2D(m.Position, p) - m.BaseRadiusInches))
                .DefaultIfEmpty(float.MaxValue).Min();

        /// <summary>How close the unit is to the nearest enemy (for activation order).</summary>
        public static float DistanceToNearestEnemy(ITableState table, PlayerID me, IUnit unit) =>
            Enemies(table, me, unit).Select(e => Distance(unit, e)).DefaultIfEmpty(float.MaxValue).Min();
    }

    /// <summary>What the Star Quest action and move resolvers tell each other within an activation.</summary>
    public sealed class StarQuestAiMemory
    {
        /// <summary>Units told to Move although their plan was a Charge (no Charge offered): move, don't seek contact.</summary>
        public readonly HashSet<UnitID> MoveInsteadOfCharge = new();
        /// <summary>Units whose move was declined and that have nothing but Move left: stand still.</summary>
        public readonly HashSet<UnitID> StandStill = new();
        /// <summary>#419 - heroes whose Charge has already been swapped for a Move this activation. Once
        /// only: the swap has to be latched or a hero that cannot complete the move is offered Charge again
        /// on the reopened menu, swapped again, and the activation never ends.</summary>
        public readonly HashSet<UnitID> HeroMoveSwapped = new();
    }

    /// <summary>Star Quest: "the AI activates all units in order from the closest to a hero to the furthest."</summary>
    public sealed class StarQuestActivationResolver : IStageResolver<ChooseUnitToActivateRequest, DataBinding<UnitData>>
    {
        private readonly ITableState _table;
        private readonly PlayerID _me;

        public StarQuestActivationResolver(ITableState table, PlayerID me) { _table = table; _me = me; }

        public Task<DataBinding<UnitData>> Resolve(ChooseUnitToActivateRequest request)
        {
            if (request.ValidOptions.Count == 0)
                throw new InvalidOperationException("AI received an activation request with no valid options.");
            var pick = request.ValidOptions
                .OrderBy(o => StarQuestAiBrain.DistanceToNearestEnemy(_table, _me, o.Option.GetValue()))
                .First();
            return Task.FromResult(pick.Option);
        }
    }

    /// <summary>Star Quest: picks Charge / Move / Shoot / Pass from the flowchart plan.</summary>
    public sealed class StarQuestActionResolver : IStageResolver<ChooseActionRequest, string>
    {
        private readonly ITableState _table;
        private readonly PlayerID _me;
        private readonly SoloMoveDeclineLatch? _latch;
        private readonly AiStringSelectionResolver _solo;
        private readonly HashSet<string> _castThisRound = new();
        private readonly StarQuestAiMemory _memory;

        public StarQuestActionResolver(ITableState table, PlayerID me, SoloMoveDeclineLatch? latch, AiStringSelectionResolver solo,
            StarQuestAiMemory memory)
        {
            _table = table; _me = me; _latch = latch; _solo = solo; _memory = memory;
        }

        public Task<string> Resolve(ChooseActionRequest request)
        {
            IUnit? unit = StarQuestAiBrain.FindUnit(_table, request.ActivatingUnitID);
            if (unit == null) return _solo.Resolve(request);

            // #419 - a bot-run hero. The solo bot's ranking is almost right for one: it takes the free Skill
            // action first (#414), then Charge > Move > Shoot > Pass. The one thing it gets wrong is
            // preferring a CHARGE while the hero has somewhere to be - a charge drags the hero into melee
            // with whatever is nearest instead of onto the objective marker a step away, and the mission
            // stalls. So ask the solo bot, and substitute Move for exactly that case. Everything else -
            // the Skill latch, the shoot-if-in-range rule, the decline latch - is left where it works.
            if (QuestHeroAi.IsHero(unit)) return HeroAction(request, unit);

            if (!StarQuestAiBrain.UsesQuestAi(unit)) return _solo.Resolve(request);

            IReadOnlyList<string> options = request.ValidOptions;
            // AI casters cast first thing when activated (one random spell, all tokens on it).
            string castKey = $"{unit.ID.ID}:{_table.Progress.RoundCount ?? 0}";
            if (options.Contains(ChooseActionStage.CAST_CHOICE_NAME) && _castThisRound.Add(castKey))
                return Task.FromResult(ChooseActionStage.CAST_CHOICE_NAME);
            bool movementDeclined = _latch?.Consume() == true;
            string? Has(string o) => options.Contains(o) ? o : null;

            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _me, unit);
            string? pick = null;
            _memory.MoveInsteadOfCharge.Remove(unit.ID);
            if (movementDeclined && options.Count == 1 && options[0] == ChooseActionStage.MOVEMENT_CHOICE_NAME)
            {
                // Nothing else is offered and the last move was declined: stand still rather than loop.
                _memory.StandStill.Add(unit.ID);
                return Task.FromResult(ChooseActionStage.MOVEMENT_CHOICE_NAME);
            }
            if (!movementDeclined)
            {
                switch (plan.Kind)
                {
                    case EQuestAiMove.Charge:
                        pick = Has(ChooseActionStage.CHARGE_CHOICE_NAME);
                        if (pick == null && (pick = Has(ChooseActionStage.MOVEMENT_CHOICE_NAME)) != null)
                            _memory.MoveInsteadOfCharge.Add(unit.ID);
                        break;
                    case EQuestAiMove.Advance:
                        bool inRange = plan.Target != null && StarQuestAiBrain.Distance(unit, plan.Target) <= plan.Range;
                        pick = inRange ? Has(ChooseActionStage.SHOOT_CHOICE_NAME) ?? Has(ChooseActionStage.MOVEMENT_CHOICE_NAME)
                            : Has(ChooseActionStage.MOVEMENT_CHOICE_NAME);
                        break;
                    case EQuestAiMove.Rush:
                    case EQuestAiMove.RushToGoal:
                        pick = Has(ChooseActionStage.MOVEMENT_CHOICE_NAME);
                        break;
                }
            }
            // Already moved (or holding): shoot anything in range, else end the activation.
            if (pick == null && plan.Kind != EQuestAiMove.Hold && options.Contains(ChooseActionStage.SHOOT_CHOICE_NAME)
                && StarQuestAiBrain.Enemies(_table, _me, unit).Any(e => StarQuestAiBrain.Distance(unit, e) <= StarQuestAiBrain.MaxRange(unit)))
                pick = ChooseActionStage.SHOOT_CHOICE_NAME;
            pick ??= Has(ChooseActionStage.PASS_CHOICE_NAME) ?? options[0];
            return Task.FromResult(pick);
        }

        /// <summary>
        /// #419 - the solo bot's answer, with a Charge swapped for a Move when the hero has somewhere to
        /// walk and is not there yet. Deliberately a substitution rather than a parallel ranking: the Skill
        /// latch that makes a mission finishable at all lives in the solo resolver, and a second ranking
        /// here would have to reproduce it and then keep up with it.
        /// </summary>
        private async Task<string> HeroAction(ChooseActionRequest request, IUnit hero)
        {
            // A fresh activation: Rest is offered only to a hero that has not yet moved, attacked or acted,
            // which is the one "this activation just began" signal the request carries. Same signal the
            // Skill latch uses, for the same reason.
            if (request.ValidOptions.Contains(QuestActions.RestOption))
                _memory.HeroMoveSwapped.Remove(hero.ID);

            string pick = await _solo.Resolve(request);
            if (pick != ChooseActionStage.CHARGE_CHOICE_NAME) return pick;
            if (!request.ValidOptions.Contains(ChooseActionStage.MOVEMENT_CHOICE_NAME)) return pick;

            QuestHeroPlan? plan = QuestHeroAi.Plan(_table, hero);
            if (plan == null || QuestHeroAi.InReach(hero, plan)) return pick;

            // Once per activation. Without this latch the substitution is the loop: a hero whose Move
            // cannot complete (boxed in, or in melee) comes back to a menu still offering Charge, gets
            // swapped to Move again, and the activation spins until the ~1.5M-decision watchdog. One Horde
            // seed went from under a second to not finishing in ten minutes. If the swap has already been
            // spent, the Charge stands - fighting what is in front of it is a perfectly good fallback.
            if (!_memory.HeroMoveSwapped.Add(hero.ID)) return pick;
            
            return ChooseActionStage.MOVEMENT_CHOICE_NAME;
        }
    }

    /// <summary>Star Quest: "Caster(X) [AI Units]: picking one random spell to cast, and spending all tokens to cast it."</summary>
    public sealed class StarQuestSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
    {
        private readonly AiChooseSpellResolver _solo = new();
        private readonly Random _rng;

        public StarQuestSpellResolver(int? seed) { _rng = seed.HasValue ? new Random(seed.Value * 977 + 5) : new Random(); }

        public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
        {
            if (!StarQuestAiBrain.UsesQuestAi(request.CastingUnit.GetValue())) return _solo.Resolve(request);
            int pick = _rng.Next(request.Spells.Count);
            ChooseSpellRequest.SpellOption spell = request.Spells[pick];
            if (!spell.Castable) return Task.FromResult(ChooseSpellReply.Cancel); // "the model doesn't cast"
            int boost = Math.Clamp(request.AvailableTokens - spell.Cost, 0, Math.Max(0, request.MaxUsefulBoost));
            return Task.FromResult(new ChooseSpellReply(pick, boost));
        }
    }

    /// <summary>Star Quest: shoots the closest target it can.</summary>
    public sealed class StarQuestRangedAttackResolver : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>
    {
        private readonly AiChooseRangedAttackResolver _solo = new();

        public Task<CancellableResult<RangedAttackChoice>> Resolve(ChooseRangedAttackRequest request)
        {
            UnitData shooter = request.AttackingUnit.GetValue();
            if (!StarQuestAiBrain.UsesQuestAi(shooter)) return _solo.Resolve(request);
            var options = new List<(Weapon Weapon, WeaponTargetStats Stats)>();
            foreach (WeaponOption weaponOption in request.WeaponOptions)
                foreach (WeaponTargetStats stats in weaponOption.WeaponTargetStats)
                    if (stats.UnselectableReason == null && stats.modelsThatCanShoot.Count > 0)
                        options.Add((weaponOption.Weapon, stats));
            if (options.Count == 0) return _solo.Resolve(request);
            var best = options
                .OrderBy(o => StarQuestAiBrain.Distance(shooter, o.Stats.TargetUnit.GetValue()))
                .ThenByDescending(o => o.Stats.modelsThatCanShoot.Count)
                .First();
            return Task.FromResult<CancellableResult<RangedAttackChoice>>(
                new Selected<RangedAttackChoice>(new RangedAttackChoice(best.Weapon, best.Stats.TargetUnit)));
        }
    }

    /// <summary>
    /// Star Quest moves: Advance to get in range, Rush or Charge at the closest enemy, or Rush to the AI Goal
    /// (pathfinding round terrain). Other moves (forced, optional, heroes on the bot's side) use the solo bot.
    /// </summary>
    public sealed class StarQuestMovementResolver : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
    {
        private readonly ITableState _table;
        private readonly PlayerID _me;
        private readonly SoloMoveDeclineLatch? _latch;
        private readonly AiDefineMovementResolver _solo;
        private readonly StarQuestAiMemory _memory;

        public StarQuestMovementResolver(ITableState table, PlayerID me, SoloMoveDeclineLatch? latch, StarQuestAiMemory memory)
        {
            _table = table; _me = me; _latch = latch; _memory = memory;
            _solo = new AiDefineMovementResolver(table, me, latch);
        }

        public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
        {
            UnitData unit = request.UnitDataBinding.GetValue();
            if (!request.MainActivationMove) return _solo.Resolve(request);

            // #419 - a bot-run hero walks to the thing the mission needs: an objective marker (within 1"),
            // a search token (within 1"), a delivery point, or the exit corner. The solo bot cannot do this
            // because none of those is an IObjective - see QuestHeroAi. With no hero plan (nothing on the
            // table to work) the solo bot takes it and the hero fights, which is the right answer then.
            if (QuestHeroAi.IsHero(unit))
            {
                QuestHeroPlan? heroPlan = QuestHeroAi.Plan(_table, unit);
                if (heroPlan == null || QuestHeroAi.InReach(unit, heroPlan)) return _solo.Resolve(request);
                return MoveToward(request, heroPlan.Goal, heroPlan.StopWithin);
            }

            if (!StarQuestAiBrain.UsesQuestAi(unit)) return _solo.Resolve(request);
            bool standStill = _memory.StandStill.Remove(unit.ID);
            QuestAiPlan plan = StarQuestAiBrain.Plan(_table, _me, unit);
            if (plan.Kind == EQuestAiMove.Charge && _memory.MoveInsteadOfCharge.Remove(unit.ID))
                plan = plan with { Kind = EQuestAiMove.Rush };
            if (plan.Kind == EQuestAiMove.Hold) return _solo.Resolve(request);

            var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return Done(MovementPlanner.StayInPlace(request.UnitDataBinding));

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };
            var terrain = _table.Terrain.Objects.ToList();
            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(_table, _me);
            var friendlyFootprints = MovementPlanner.LiveFriendlyFootprints(_table, _me, unit.ID);
            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);

            if (standStill)
                return Done(MovementPlanner.StayInPlaceValidated(request.UnitDataBinding, living, budgetFor, enemyFootprints,
                    request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain,
                    friendlyFootprints));

            List<ModelMoveEntry> move;
            if (plan.Kind == EQuestAiMove.RushToGoal && plan.Goal is { } goal)
            {
                // Get within 3" of the objective; stop a little short of its centre.
                float dist = Position.GetDistance2D(new Position(cx, cz), goal);
                float budget = Math.Clamp(dist - 1.5f, 0f, request.MaxDistanceInches - 0.001f);
                move = MovementPlanner.PlanMoveToward(request.UnitDataBinding, living, _table, goal, budget,
                    request.MaxDistanceInches, budgetFor, request.CanMoveThroughEnemies,
                    request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain);
            }
            else if (plan.Target != null)
            {
                var targetPrints = MovementPlanner.UnitFootprints(plan.Target);
                if (targetPrints.Count == 0) return _solo.Resolve(request);
                EnemyModelFootprint nearest = targetPrints.OrderBy(f => Dist(f.Center.x, f.Center.z, cx, cz)).First();
                float dx = nearest.Center.x - cx, dz = nearest.Center.z - cz;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                if (dist < 0.01f) return _solo.Resolve(request);
                float ndx = dx / dist, ndz = dz / dist;
                float lead = living.Max(mb => mb.GetValue().BaseRadiusInches);
                float contact = lead + nearest.BaseRadiusInches;
                float standoff = GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + MovementPlanner.StandoffTargetMarginInches;

                float moveDistance, targetGap;
                switch (plan.Kind)
                {
                    case EQuestAiMove.Charge:
                        moveDistance = request.MaxDistanceInches - 0.001f;
                        targetGap = MovementPlanner.ChargeContactTargetGapInches;
                        break;
                    case EQuestAiMove.Advance:
                        // Only as far as needed to be in range (staying a little inside it).
                        moveDistance = request.MaxAdvanceDistance - 0.001f;
                        targetGap = Math.Max(standoff, plan.Range - 1f);
                        break;
                    default:
                        moveDistance = request.MaxDistanceInches - 0.001f;
                        targetGap = standoff;
                        break;
                }

                float initialStep = Math.Clamp(dist - contact - targetGap, 0f, moveDistance);
                float step = MovementPlanner.RefineStepTowardGap(request.UnitDataBinding, living, cx, cz, ndx, ndz,
                    initialStep, moveDistance, targetGap, enemyFootprints, request.MaxDistanceInches);
                if (step <= 0.01f && plan.Kind == EQuestAiMove.Advance) return _solo.Resolve(request);

                if (plan.Kind != EQuestAiMove.Charge && step > 0.01f)
                {
                    // Not charging: route round terrain like the AI Goal move does.
                    var aim = new Position(cx + ndx * step, cz + ndz * step);
                    move = MovementPlanner.PlanMoveToward(request.UnitDataBinding, living, _table, aim, step,
                        request.MaxDistanceInches, budgetFor, request.CanMoveThroughEnemies,
                        request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain);
                }
                else
                {
                    move = MovementPlanner.ValidateWithBackoff(
                        s => MovementPlanner.BuildCandidate(request.UnitDataBinding, living, cx, cz, ndx, ndz, s, request.MaxDistanceInches),
                        step, request.UnitDataBinding, living, budgetFor, enemyFootprints,
                        request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain,
                        terrain, friendlyFootprints,
                        (s, lat) => MovementPlanner.BuildCandidate(request.UnitDataBinding, living, cx, cz, ndx, ndz, s,
                            request.MaxDistanceInches, lateralOffsetInches: lat));
                }
            }
            else return _solo.Resolve(request);

            if (!MovementUtilities.ValidatePaths(move, budgetFor, enemyFootprints, request.CanMoveThroughEnemies,
                    request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlyFootprints))
                return _solo.Resolve(request);
            return Done(move);
        }

        /// <summary>
        /// #419 - walk toward a point and stop <paramref name="stopWithin"/> short of it, routing round
        /// terrain. The same planner call the enemy's RushToGoal uses; what differs is the stopping
        /// distance, because a hero that stops 3" from an objective marker cannot work it and a hero that
        /// walks onto the marker's exact centre is wasting the rest of its move.
        /// <para>
        /// Aims for half the reach rather than the reach itself: the reach is measured from the model's
        /// edge (<see cref="QuestMission.DistanceToPoint"/> subtracts the base radius) while the move is
        /// planned from the unit's centre of mass, and with a multi-model unit those are not the same
        /// point. Half the slack costs nothing - the move is capped by the budget anyway - and arriving at
        /// 0.9" when 1" was needed is the one failure that cannot be recovered next activation, because the
        /// hero is already standing where it meant to be.
        /// </para>
        /// </summary>
        private Task<CancellableResult<List<ModelMoveEntry>>> MoveToward(DefineMovementPathRequest request,
            Position goal, float stopWithin)
        {
            var living = request.UnitDataBinding.GetValue().ModelBindings
                .Where(mb => mb.GetValue().GetIsAlive()).ToList();
            if (living.Count == 0) return Done(MovementPlanner.StayInPlace(request.UnitDataBinding));

            Func<ModelMoveEntry, ModelMoveBudget> budgetFor = entry =>
            {
                var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                return new ModelMoveBudget(rush, maxDist);
            };

            float cx = living.Average(mb => mb.GetValue().Position.x);
            float cz = living.Average(mb => mb.GetValue().Position.z);
            float dist = Position.GetDistance2D(new Position(cx, cz), goal);
            float budget = Math.Clamp(dist - stopWithin * 0.5f, 0f, request.MaxDistanceInches - 0.001f);

            List<ModelMoveEntry> move = MovementPlanner.PlanMoveToward(request.UnitDataBinding, living, _table,
                goal, budget, request.MaxDistanceInches, budgetFor, request.CanMoveThroughEnemies,
                request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain);

            var terrain = _table.Terrain.Objects.ToList();
            var enemyFootprints = MovementPlanner.LiveEnemyFootprints(_table, _me);
            var friendlyFootprints = MovementPlanner.LiveFriendlyFootprints(_table, _me,
                request.UnitDataBinding.GetValue().ID);
            if (MovementUtilities.ValidatePaths(move, budgetFor, enemyFootprints, request.CanMoveThroughEnemies,
                    request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlyFootprints))
                return Done(move);

            // #419 - the walk cannot be made legal (boxed in, or the goal is behind something impassable).
            // Stand still, explicitly and validly.
            //
            // The obvious thing here is to hand back to the solo bot, and that is what the first version
            // did. It hangs the game: the solo path resolver declines a move it has no legal answer for,
            // the engine's Back affordance reopens the action menu, the hero is offered Move again, and
            // round and round until the ~1.5M-decision watchdog. One Horde seed went from finishing in
            // under a second to not finishing in ten minutes. The enemy's own branches carry
            // SoloMoveDeclineLatch and StarQuestAiMemory.StandStill for exactly this reason, and those
            // guards were written after the same bug - hero moves have to respect them too.
            return Done(MovementPlanner.StayInPlaceValidated(request.UnitDataBinding, living, budgetFor,
                enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, terrain, friendlyFootprints));
        }

        private static Task<CancellableResult<List<ModelMoveEntry>>> Done(List<ModelMoveEntry> move) =>
            Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Selected<List<ModelMoveEntry>>(move));

        private static float Dist(float ax, float az, float bx, float bz)
        {
            float dx = ax - bx, dz = az - bz;
            return MathF.Sqrt(dx * dx + dz * dz);
        }
    }
}
