using FDG.Rules.Foundation;
using FDG.Utilities;

namespace FDG.Ai.Solo
{
    /// <summary>What an AI unit does this activation, from the Solo &amp; Co-Op decision tree.</summary>
    public enum ESoloAiMove
    {
        /// <summary>Step 2's "Hold position": it is on a contested objective with nothing to shoot.</summary>
        Hold,
        /// <summary>Advance and shoot the closest target, staying on the objective it holds (step 2).</summary>
        AdvanceAndShoot,
        /// <summary>Charge the closest target (steps 2, 4 and 5).</summary>
        Charge,
        /// <summary>Advance toward an objective and shoot the closest target on the way (step 4).</summary>
        AdvanceToObjective,
        /// <summary>Rush for an objective with nothing in range (steps 4 and 5).</summary>
        RushToObjective,
    }

    /// <summary>One unit's plan: what to do, who to do it to, and where it is heading.</summary>
    public sealed record SoloAiPlan(ESoloAiMove Kind, IUnit? Target, Position? Goal, float Range, string Why);

    /// <summary>
    /// #419 - the AI decision tree from <b>Solo &amp; Co-Op Rules v3.6.0</b>, which is what the Horde books
    /// mean when they say the enemy side is "controlled by the AI (using the Solo &amp; Co-Op rules)".
    ///
    /// <para>
    /// Word for word, the book's five steps:
    /// </para>
    /// <list type="number">
    /// <item>Is this unit within 3" of an objective that is <b>not controlled by the AI</b>? Yes: step 2.
    /// No: step 3.</item>
    /// <item>Is any enemy in range to shoot/charge <b>without moving this unit over 3" away from the
    /// objective</b>? Yes: Advance and shoot closest, or charge closest. No: <b>Hold position</b>.</item>
    /// <item>Is there any objective not controlled by the AI that has <b>no non-Shaken AI units within
    /// 3"</b> of it? Yes: step 4. No: step 5.</item>
    /// <item>If you move towards the objective, will any enemy be in range to shoot/charge? Yes: Advance
    /// towards objective and shoot closest, or Charge closest towards objective if you can deal more damage
    /// than with shooting. No: Rush toward objective.</item>
    /// <item>Is any enemy in range to shoot/charge? Yes: Advance and shoot closest, or charge closest. No:
    /// Rush toward closest objective.</item>
    /// </list>
    /// <para>
    /// <b>What this replaces, and why it matters.</b> The old solo bot ranked Charge &gt; Move &gt; Shoot
    /// &gt; Pass with <i>no notion of objectives at all</i>, so it played every game as a brawl: it walked
    /// at the nearest enemy and never at a marker. Objectives decide the winner in every mode the engine
    /// has, which made the one bot that is supposed to follow the book the one least able to win with it.
    /// </para>
    /// <para>
    /// The tree is deliberately a pure function of table state, like <c>StarQuestAiBrain.Plan</c>: it is
    /// tested on its own, and the resolvers do the dice and the geometry. Identical in GF and Firefight -
    /// the two books' trees are word for word the same, and the four clauses that do differ between them
    /// are per-rule (Blast, Deadly, Artillery, Transport), not per-step.
    /// </para>
    /// </summary>
    public static class SoloAiBrain
    {
        /// <summary>"within 3" of an objective", used by steps 1 and 3.</summary>
        public const float ObjectiveReachInches = 3f;

        /// <summary>
        /// Whether an objective counts as the AI's. The book says "not controlled by the AI", and the engine
        /// already tracks control as the seizing player on the objective, so an unclaimed marker and an
        /// enemy-held one are both worth going for - which is what makes the tree move at all.
        /// </summary>
        public static bool ControlledByAi(ITableState table, PlayerID me, IObjective objective) =>
            objective.OwnerID is PlayerID owner
            && ITeamExtensions.AreAllied(table.Teams.Objects, me, owner);

        /// <summary>Objectives this AI does not already hold.</summary>
        public static List<IObjective> Contested(ITableState table, PlayerID me) =>
            table.Objectives.Objects.Where(o => !ControlledByAi(table, me, o)).ToList();

        /// <summary>
        /// Step 3's test: a contested objective with no non-Shaken AI unit within 3" of it. "Non-Shaken"
        /// matters - a Shaken unit sitting on a marker is not holding it for the purposes of this step, so
        /// the tree correctly sends someone else.
        /// </summary>
        public static bool Unattended(ITableState table, PlayerID me, IObjective objective)
        {
            foreach (IUnit unit in table.Units.Objects)
            {
                if (!unit.GetIsAlive() || !unit.GetIsOnBattlefield()) continue;
                if (!ITeamExtensions.AreAllied(table.Teams.Objects, me, unit.PlayerID)) continue;
                if (unit.Tokens.HasToken(TokenType.Shaken)) continue;
                if (AiCombatMath.DistanceToPoint(unit, objective.Position) <= ObjectiveReachInches) return false;
            }

            return true;
        }

        /// <summary>
        /// The closest target worth shooting, by the book's shooting priority (see
        /// <see cref="SoloAiTargeting"/>): closest, preferring one in the open over one in Cover, and
        /// preferring a unit that has not activated yet.
        /// </summary>
        public static IUnit? ShootTarget(ITableState table, PlayerID me, IUnit unit, float reachInches)
        {
            var terrain = table.Terrain.Objects.ToList();
            float range = AiCombatMath.MaxRange(unit);
            if (range <= 0f) return null;

            var candidates = AiCombatMath.Enemies(table, me)
                .Where(e => AiCombatMath.Distance(unit, e) <= range + reachInches)
                .Where(e => AiCombatMath.Sees(unit, e, terrain))
                .ToList();
            return SoloAiTargeting.PickShootTarget(table, unit, candidates);
        }

        /// <summary>The closest target worth charging, by the book's melee priority.</summary>
        /// <param name="stayNear">
        /// Step 2: "without moving this unit over 3" away from the objective". A charge ends in contact with
        /// its target, so the target itself has to stand within 3" of the objective the unit is holding -
        /// otherwise the charge drags the unit off the marker the step exists to keep. The tree used to
        /// charge anything within 12" from step 2 (found in the bot audit, 2026-11-07).
        /// </param>
        public static IUnit? ChargeTarget(ITableState table, PlayerID me, IUnit unit, Position? stayNear = null)
        {
            float reach = ChargeReach(unit);
            var candidates = AiCombatMath.Enemies(table, me)
                .Where(e => AiCombatMath.Distance(unit, e) <= reach)
                .Where(e => stayNear is not { } at
                            || AiCombatMath.DistanceToPoint(e, at) <= ObjectiveReachInches)
                .ToList();
            return SoloAiTargeting.PickChargeTarget(unit, candidates);
        }

        // Read-only and non-logging: the movement queries ask the rule graph what Fast, Slow, Rapid Advance
        // and the like do to this unit's moves. No movement rule rolls dice.
        private static readonly Rules.Dispatch.RuleEvaluator MoveEvaluator =
            new(new ProbabilisticDiceRoller(), ruleResolver: Rules.Dispatch.CoreRuleCatalog.CreateResolver());

        /// <summary>
        /// The unit's own Advance, with its rules. The tree asks "if you move towards the objective, will any
        /// enemy be in range" - a Fast unit reaches further than 6" and a Slow one less, and planning on a
        /// flat 6" sent Slow units to advance-and-shoot at targets they could not reach (bot audit, 2026-11-07).
        /// </summary>
        public static float AdvanceReach(IUnit unit)
        {
            try { return Rules.Dispatch.MovementRuleQueries.EffectiveMoveShootDistance(unit, MoveEvaluator); }
            catch (InvalidOperationException) { return GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES; }
        }

        /// <summary>The unit's own Charge distance, with its rules (see <see cref="AdvanceReach"/>).</summary>
        public static float ChargeReach(IUnit unit)
        {
            try { return Rules.Dispatch.MovementRuleQueries.EffectiveChargeDistance(unit, MoveEvaluator); }
            catch (InvalidOperationException) { return GameWideConstants.CHARGE_DISTANCE_INCHES; }
        }

        /// <summary>
        /// Every plan the tree makes, as "unit: why". For tests and the bot audit (who planned what, so a
        /// move that broke its own plan can be told from one that followed it). Null in play.
        /// </summary>
        public static Action<IUnit, SoloAiPlan>? DecisionTrace;

        /// <summary>The whole tree for one unit.</summary>
        public static SoloAiPlan Plan(ITableState table, PlayerID me, IUnit unit)
        {
            SoloAiPlan plan = PlanCore(table, me, unit);
            DecisionTrace?.Invoke(unit, plan);
            return plan;
        }

        private static SoloAiPlan PlanCore(ITableState table, PlayerID me, IUnit unit)
        {
            if (!unit.Models.Any(m => m.GetIsAlive()))
                return new SoloAiPlan(ESoloAiMove.Hold, null, null, 0f, "no living models");

            float advance = AdvanceReach(unit);
            float range = AiCombatMath.MaxRange(unit);
            List<IObjective> contested = Contested(table, me);

            // ── Step 1: standing on a contested objective? ──────────────────────────────────────────────
            IObjective? here = contested
                .Where(o => AiCombatMath.DistanceToPoint(unit, o.Position) <= ObjectiveReachInches)
                .OrderBy(o => AiCombatMath.DistanceToPoint(unit, o.Position))
                .FirstOrDefault();

            if (here != null)
            {
                // Step 2: anything to hit WITHOUT leaving the objective. The book's "without moving this
                // unit over 3" away from the objective" is the whole point of the step - the unit is
                // holding ground, so the reach it may use is only what keeps it on the marker, not a full
                // Advance. Measured from where it stands: it may step up to 3" from the objective's centre.
                float slack = Math.Max(0f, ObjectiveReachInches
                    - AiCombatMath.DistanceToPoint(unit, here.Position));
                IUnit? shoot = ShootTarget(table, me, unit, slack);
                IUnit? charge = ChargeTarget(table, me, unit, stayNear: here.Position);

                if (charge != null && (shoot == null
                        || AiCombatMath.ExpectedWounds(unit, charge, melee: true)
                           > AiCombatMath.ExpectedWounds(unit, shoot, melee: false)))
                    return new SoloAiPlan(ESoloAiMove.Charge, charge, here.Position, 0f,
                        $"holding objective - charge {charge.Name}");
                if (shoot != null)
                    return new SoloAiPlan(ESoloAiMove.AdvanceAndShoot, shoot, here.Position, range,
                        $"holding objective - shoot {shoot.Name}");
                return new SoloAiPlan(ESoloAiMove.Hold, null, here.Position, 0f, "holding its objective");
            }

            // ── Step 3: an unattended contested objective to go and take? ───────────────────────────────
            IObjective? target = contested
                .Where(o => Unattended(table, me, o))
                .OrderBy(o => AiCombatMath.DistanceToPoint(unit, o.Position))
                .FirstOrDefault();

            if (target != null)
            {
                // Step 4: will anything be in reach once it has moved that way?
                IUnit? shoot = ShootTarget(table, me, unit, advance);
                IUnit? charge = ChargeTarget(table, me, unit);

                if (charge != null && (shoot == null
                        || AiCombatMath.ExpectedWounds(unit, charge, melee: true)
                           > AiCombatMath.ExpectedWounds(unit, shoot, melee: false)))
                    return new SoloAiPlan(ESoloAiMove.Charge, charge, target.Position, 0f,
                        $"charge {charge.Name} on the way to an objective");
                if (shoot != null)
                    return new SoloAiPlan(ESoloAiMove.AdvanceToObjective, shoot, target.Position, range,
                        $"advance on the objective, shooting {shoot.Name}");
                return new SoloAiPlan(ESoloAiMove.RushToObjective, null, target.Position, 0f,
                    "rush for an unheld objective");
            }

            // ── Step 5: nothing to take, so fight, else head for the closest objective ──────────────────
            IUnit? anyShoot = ShootTarget(table, me, unit, advance);
            IUnit? anyCharge = ChargeTarget(table, me, unit);

            if (anyCharge != null && (anyShoot == null
                    || AiCombatMath.ExpectedWounds(unit, anyCharge, melee: true)
                       > AiCombatMath.ExpectedWounds(unit, anyShoot, melee: false)))
                return new SoloAiPlan(ESoloAiMove.Charge, anyCharge, null, 0f, $"charge {anyCharge.Name}");
            if (anyShoot != null)
                return new SoloAiPlan(ESoloAiMove.AdvanceAndShoot, anyShoot, null, range,
                    $"advance and shoot {anyShoot.Name}");

            IObjective? closest = table.Objectives.Objects
                .OrderBy(o => AiCombatMath.DistanceToPoint(unit, o.Position)).FirstOrDefault();
            if (closest != null)
                return new SoloAiPlan(ESoloAiMove.RushToObjective, null, closest.Position, 0f,
                    "nothing in range - rush the closest objective");

            return new SoloAiPlan(ESoloAiMove.Hold, null, null, 0f, "no objectives and nothing in range");
        }
    }
}
