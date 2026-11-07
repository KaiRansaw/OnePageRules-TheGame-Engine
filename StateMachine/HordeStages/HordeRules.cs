using FDG.Players;
using FDG.Rules.Foundation;

namespace FDG.StateMachine
{
    /// <summary>
    /// The numbers and pure checks behind OPR Horde Mode v3.6.0 (Grimdark Future). Kept free of
    /// stage plumbing so each rule can be unit-tested on its own.
    /// </summary>
    public static class HordeRules
    {
        /// <summary>How many rounds the mission lasts for this mode.</summary>
        public static int RoundLimit(EGameMode mode) => mode switch
        {
            EGameMode.Horde => GameWideConstants.HORDE_NUMBER_OF_ROUNDS,
            // Star Quest Horde has no round limit: it ends when the heroes fall, the objectives are gone, or
            // wave 10 is finished.
            EGameMode.StarQuestHorde or EGameMode.StarQuestMission => StarQuestHordeRules.NoRoundLimit,
            _ => GameWideConstants.NUMBER_OF_ROUNDS,
        };

        /// <summary>The game's length with its settings: a standard game's round count can be set (house rule).</summary>
        public static int RoundLimit(GameSettings settings)
        {
            int limit = RoundLimit(settings.GameMode);
            return limit == GameWideConstants.NUMBER_OF_ROUNDS && settings.NumberOfRounds is int n && n > 0 ? n : limit;
        }

        /// <summary>1-based wave a round belongs to: rounds 1-3 are wave 1, 4-6 wave 2, and so on.</summary>
        public static int WaveOfRound(int round) => ((round - 1) / GameWideConstants.HORDE_ROUNDS_PER_WAVE) + 1;

        /// <summary>
        /// The high-score multiplier: the number of ROUNDS the defenders successfully finished.
        ///
        /// <para>#409 (OPR 3.6.0) changed what this counts. 3.5.0 multiplied by the last WAVE finished,
        /// so a mission could only ever score x1 to x4 and being overrun early was barely distinguishable
        /// from being overrun late. 3.6 multiplies by the last ROUND finished - a x1 to x12 range, and the
        /// rulebook's own worked example changed with it ("destroyed during round 6 and earned 100pts, so
        /// they get a high score of 500pts": overrun DURING round 6 means 5 rounds were finished).</para>
        ///
        /// <para>That example is why being overrun does not count the round it happened in, while holding
        /// out to the end counts every round of the mission.</para>
        /// </summary>
        public static int RoundsFinishedForScore(int roundsFinished, bool defendersSurvived)
        {
            if (defendersSurvived) return GameWideConstants.HORDE_NUMBER_OF_ROUNDS;
            return Math.Max(0, roundsFinished - 1);
        }

        /// <summary>
        /// The five objective-marker anchor points: the table centre, then the centre of each table
        /// quarter. Markers are numbered 1-5 in this order.
        /// </summary>
        public static IReadOnlyList<Position> ObjectiveAnchors(float tableWidth, float tableHeight)
        {
            float cx = tableWidth / 2f, cz = tableHeight / 2f;
            float qx = tableWidth / 4f, qz = tableHeight / 4f;
            return new[]
            {
                new Position(cx, cz),
                new Position(qx, qz),
                new Position(qx * 3f, qz),
                new Position(qx, qz * 3f),
                new Position(qx * 3f, qz * 3f),
            };
        }

        /// <summary>Radius of every Horde deployment area: the table centre and each table corner.</summary>
        /// The full game uses 9"; GF: Firefight's Horde Mode uses 6". Set by <see cref="Configure"/>.
        public static float DeploymentRadiusInches { get; private set; } = 9f;

        /// <summary>Points the defenders earn for each objective they hold at a point-reward round.</summary>
        /// 40 in Grimdark Future / Age of Fantasy, 30 in the two skirmish games (#409: was 10).
        public static int PointsPerSeizedObjective { get; private set; } = 40;

        /// <summary>Extra points when one of the seized objectives is the priority objective.</summary>
        /// 20 in the full games, 15 in the skirmish games (#409: was 5).
        public static int PriorityObjectiveBonus { get; private set; } = 20;

        /// <summary>
        /// Whether killing a Hero together with its unit is worth more than killing the Hero alone.
        /// <para>
        /// #416, and the one place the four Horde books diverge on a RULE rather than a measurement.
        /// Grimdark Future and Age of Fantasy both list three XP tiers - <i>"+3 XP: Killed a unit / +5 XP:
        /// Killed a Hero unit / +8 XP: Killed a Hero and its unit"</i> - and GF: Firefight and AoF:
        /// Skirmish list the first two and stop. There is no "Hero and its unit" line in either skirmish
        /// book. So in those two games a unit that wipes out a joined hero's squad earns 5, the same as
        /// killing the hero alone, and the engine awarded 8 everywhere.
        /// </para>
        /// </summary>
        public static bool AwardsHeroAndUnitXp { get; private set; } = true;

        /// <summary>Rounds at whose end point rewards are paid out (3, 6, 9, 12 - the end of each wave).</summary>
        /// <summary>How close to a seized objective a unit must be to buy upgrades (and recruits deploy).</summary>
        /// 9" in the full game, 6" in Firefight.
        public static float ShopRangeInches { get; private set; } = 9f;

        /// <summary>
        /// How close a unit must be to a seized objective to be REINFORCED - which is not the same band as
        /// buying upgrades or recruiting.
        /// <para>
        /// <b>#437: all four Horde Mode v3.6.0 books print 9" for reinforcing</b>, including GFF and AoFS,
        /// where they scaled buying upgrades and recruiting down to 6" and left this one at 9":
        /// <i>"Defenders may spend points to reinforce units that are down to half or less of their total
        /// size or Tough value ... and that are within 9" of a seized objective."</i> Word for word in all
        /// four. The engine reused <see cref="ShopRangeInches"/> here, so a skirmish Horde refused a
        /// reinforcement the book allows, between 6" and 9" out.
        /// </para>
        /// <para>
        /// It is probably an OPR slip - they scaled the other two bands - but the rules go in as written
        /// unless Kai says otherwise, all four books agree, and it errs toward letting the defenders do
        /// something rather than refusing them, so a literal reading cannot break a mission.
        /// </para>
        /// </summary>
        public static float ReinforceRangeInches => 9f;

        /// <summary>
        /// Sets the Horde Mode sizes for the game type. The skirmish books (GF: Firefight and AoF:
        /// Skirmish Horde Mode v3.6.0) use a 4'x4' table and 6" deployment and shop areas; the full games
        /// (Grimdark Future and Age of Fantasy) use 6'x4' and 9".
        /// <para>
        /// #409: the skirmish point rewards are 30 per seized objective and +15 for the priority one.
        /// They were 10 and +5 here, which came from the 3.5.0 Firefight book; 3.6 tripled them, so a
        /// skirmish mission's high score is now in the same ballpark as a full game's.
        /// </para>
        /// </summary>
        public static void Configure(EGameSystem system)
        {
            bool skirmish = GameSystemInfo.IsSkirmish(system);
            DeploymentRadiusInches = skirmish ? 6f : 9f;
            ShopRangeInches = skirmish ? 6f : 9f;
            PointsPerSeizedObjective = skirmish ? 30 : 40;
            PriorityObjectiveBonus = skirmish ? 15 : 20;
            // #416: the skirmish Horde books have only TWO XP tiers. GF and AoF list
            //   +3 XP: Killed a unit / +5 XP: Killed a Hero unit / +8 XP: Killed a Hero and its unit
            // and GFF and AoFS list the first two and stop - there is no "Hero and its unit" line in
            // either of them. Read word for word out of all four Horde Mode v3.6.0 books. So a Firefight
            // or Skirmish unit that wipes out a hero's squad scores 5, not 8.
            AwardsHeroAndUnitXp = !skirmish;
            FDG.Stages.HordePlacement.CrowdedRadiiInches = skirmish
                ? new[] { 6f, 9f, 12f, 15f } : new[] { 9f, 12f, 15f, 18f };
        }

        public static bool IsRewardRound(int round) =>
            round >= 1 && round <= GameWideConstants.HORDE_NUMBER_OF_ROUNDS
            && round % GameWideConstants.HORDE_ROUNDS_PER_WAVE == 0;

        /// <summary>The point reward at the end of rounds 3, 6, 9 and 12: 40 per seized objective, +20 if
        /// the priority one is among them (30 and +15 in the skirmish games).</summary>
        public static int WaveReward(int seizedObjectives, bool prioritySeized) =>
            seizedObjectives * PointsPerSeizedObjective + (prioritySeized && seizedObjectives > 0 ? PriorityObjectiveBonus : 0);

        /// <summary>Damage markers that destroy an objective.</summary>
        public const int ObjectiveDamageToDestroy = 3;

        /// <summary>Rounds at whose start a new priority objective is set (1, 4, 7, 10).</summary>
        public static bool IsPriorityRound(int round) =>
            round >= 1 && round <= GameWideConstants.HORDE_NUMBER_OF_ROUNDS
            && (round - 1) % GameWideConstants.HORDE_ROUNDS_PER_WAVE == 0;

        /// <summary>Rounds at whose start a new enemy wave deploys (4, 7, 10 - wave 1 is the opening deployment).</summary>
        public static bool IsWaveRound(int round) => round > 1 && IsPriorityRound(round);

        /// <summary>
        /// The objective's number (1-5): the anchor it sits closest to. Markers can slide off their anchor
        /// to dodge terrain, but never as far as another anchor, so the nearest anchor is its number.
        /// </summary>
        public static int ObjectiveNumber(Position objectivePosition, float tableWidth, float tableHeight)
        {
            var anchors = ObjectiveAnchors(tableWidth, tableHeight);
            int best = 0;
            float bestDist = float.MaxValue;
            for (int i = 0; i < anchors.Count; i++)
            {
                float dx = anchors[i].x - objectivePosition.x, dz = anchors[i].z - objectivePosition.z;
                float d = dx * dx + dz * dz;
                if (d < bestDist) { bestDist = d; best = i; }
            }
            return best + 1;
        }

        /// <summary>
        /// Table corner 1-4 as a point: 1 = (0,0), 2 = (width,0), 3 = (0,height), 4 = (width,height).
        /// </summary>
        public static Position CornerPosition(int corner, float tableWidth, float tableHeight) => corner switch
        {
            1 => new Position(0f, 0f),
            2 => new Position(tableWidth, 0f),
            3 => new Position(0f, tableHeight),
            4 => new Position(tableWidth, tableHeight),
            _ => throw new ArgumentOutOfRangeException(nameof(corner), $"Corners are numbered 1-4 (got {corner})."),
        };

        /// <summary>
        /// Rolls for the next priority objective. 1-5 picks that numbered objective (re-rolling numbers
        /// that no longer exist or that were the last priority); a 6 lets the defenders pick. With one
        /// objective or fewer left there is no priority (null).
        /// </summary>
        /// <param name="remaining">Numbers of the objectives still on the table.</param>
        /// <param name="lastPriority">The previous priority objective, which may not be picked again.</param>
        /// <param name="rollD6">Rolls one die.</param>
        /// <param name="defenderPick">Chooses from the eligible numbers on a 6.</param>
        public static int? ChoosePriority(IReadOnlyCollection<int> remaining, int? lastPriority,
            Func<int> rollD6, Func<IReadOnlyList<int>, int> defenderPick)
        {
            (int? rolled, IReadOnlyList<int>? pickFrom) = RollPriority(remaining, lastPriority, rollD6);
            return pickFrom != null ? defenderPick(pickFrom) : rolled;
        }

        /// <summary>
        /// The dice half of <see cref="ChoosePriority"/>: either the number the dice landed on, or - on a 6
        /// - the eligible objectives the defenders choose from (asked separately, since that is a prompt).
        /// Both null when there is no priority.
        /// </summary>
        public static (int? Rolled, IReadOnlyList<int>? DefendersPickFrom) RollPriority(
            IReadOnlyCollection<int> remaining, int? lastPriority, Func<int> rollD6)
        {
            if (remaining.Count <= 1) return (null, null);

            List<int> eligible = remaining.Where(n => n != lastPriority).OrderBy(n => n).ToList();
            if (eligible.Count == 0) return (null, null);

            // Re-rolls are guaranteed to land eventually; the cap only guards a broken roller.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                int roll = rollD6();
                if (roll >= 6) return (null, eligible);
                if (eligible.Contains(roll)) return (roll, null);
            }
            return (null, eligible);
        }

        /// <summary>Where corner 1-4 sits on screen (z = 0 is the bottom edge).</summary>
        public static string CornerName(int corner) => corner switch
        {
            1 => "bottom-left",
            2 => "bottom-right",
            3 => "top-left",
            4 => "top-right",
            _ => "?",
        };

        /// <summary>
        /// Which corner a wave unit arrives at: 1-4 is that corner; on a 5-6 the defenders pick, passed in
        /// as <paramref name="defenderPick"/>.
        /// </summary>
        public static int CornerForRoll(int roll, Func<int> defenderPick) =>
            roll >= 1 && roll <= 4 ? roll : defenderPick();

        /// <summary>
        /// The defending side. Version 1 rule: the team of the lowest-numbered filled player slot
        /// (the host) defends; every other team is the enemy. Returns null if no slot is filled.
        /// </summary>
        public static int? DefendingTeamNumber(IEnumerable<IPlayerSlotInfo> players)
        {
            IPlayerSlotInfo? first = players.Where(p => p.IsFilled).OrderBy(p => p.SlotID).FirstOrDefault();
            return first?.TeamNumber;
        }

        /// <summary>True when every defending unit is destroyed or Shaken (a mission-ending condition).</summary>
        public static bool AllDefendersDownOrShaken(ITableState table)
        {
            int? defending = DefendingTeamNumber(table.Players.Objects);
            if (defending == null) return false;

            HashSet<PlayerID> defenderIds = table.Players.Objects
                .Where(p => p.IsFilled && p.TeamNumber == defending.Value)
                .Select(p => p.PlayerID)
                .ToHashSet();

            foreach (IUnit unit in table.Units.Objects)
            {
                if (!defenderIds.Contains(unit.PlayerID)) continue;
                if (!unit.GetIsAlive()) continue;
                if (unit.Tokens.HasToken(TokenType.Shaken)) continue;
                return false;
            }

            return true;
        }
    }
}
