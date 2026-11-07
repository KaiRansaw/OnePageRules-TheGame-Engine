using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// Horde Mode's per-round steps (OPR v3.6.0):
    /// <list type="bullet">
    ///   <item>Start of rounds 1, 4, 7, 10: set a new priority objective.</item>
    ///   <item>Start of rounds 4, 7, 10: Shaken enemies are removed as destroyed, then every destroyed
    ///   enemy unit returns at full strength at a random table corner.</item>
    ///   <item>End of every round: an objective with enemies within 3" and no defenders takes a damage
    ///   marker; three markers destroy it.</item>
    ///   <item>The mission ends when every defending unit is Shaken or destroyed, or no objective is left
    ///   (round 12 is handled by the round limit).</item>
    /// </list>
    /// </summary>
    public sealed class HordeRoundHooks : IRoundHooks
    {
        private readonly IGameContext _gameContext;

        // Damage markers and the priority flag live on each objective's own record in the store (see
        // ObjectiveData), so every player's screen shows them and they survive a save and resume.
        public HordeRoundHooks(IGameContext gameContext)
        {
            _gameContext = gameContext;
        }

        public async Task OnRoundStart(IMainPhaseContext context)
        {
            int round = context.RoundCount;

            if (HordeRules.IsPriorityRound(round))
                await SetPriority();

            if (HordeRules.IsWaveRound(round))
                await DeployWave(HordeRules.WaveOfRound(round));

            if (round == HordeAmbush.ArrivalRound)
                await HordeAmbush.Arrive(_gameContext);
        }

        public async Task<bool> OnRoundEnd(IMainPhaseContext context, int roundJustFinished)
        {
            await DamageObjectives();

            bool rewardRound = HordeRules.IsRewardRound(roundJustFinished);
            List<ObjectiveData> seized = rewardRound
                ? await AwardWavePoints(HordeRules.WaveOfRound(roundJustFinished))
                : new List<ObjectiveData>();

            ITableState table = _gameContext.TableState;
            if (HordeRules.AllDefendersDownOrShaken(table))
            {
                _gameContext.Log($"Horde Mode: every defending unit is Shaken or destroyed after round {roundJustFinished}.");
                return true;
            }

            if (!table.Objectives.Objects.Any())
            {
                _gameContext.Log($"Horde Mode: every objective marker is destroyed after round {roundJustFinished}.");
                return true;
            }

            // The shop opens after the wave's points, unless the mission is over (after round 12 there is
            // nothing left to spend them on).
            if (rewardRound && roundJustFinished < HordeRules.RoundLimit(_gameContext.Settings))
                await HordeShop.Run(_gameContext, seized, HordeRules.WaveOfRound(roundJustFinished));

            return false;
        }

        internal async Task SetPriority()
        {
            List<ObjectiveData> objectives = _gameContext.GameDataStore.GetAllValues<ObjectiveData>().ToList();
            List<int> remaining = objectives.Select(o => NumberOf(o)).Distinct().ToList();

            int? previous = objectives.Where(o => o.IsHordePriority).Select(o => (int?)NumberOf(o)).FirstOrDefault();
            (int? chosen, IReadOnlyList<int>? pickFrom) = HordeRules.RollPriority(remaining, previous,
                () =>
                {
                    int roll = HordePlacement.RollD6(_gameContext);
                    _gameContext.Log($"  Priority roll: {roll}.");
                    return roll;
                });
            if (pickFrom != null)
            {
                chosen = await HordePlacement.AskDefendersForPriority(_gameContext, pickFrom);
                _gameContext.Log($"  The defenders choose objective {chosen}.");
            }

            foreach (ObjectiveData objective in objectives)
            {
                bool isPriority = chosen != null && NumberOf(objective) == chosen.Value;
                if (objective.IsHordePriority != isPriority) objective.SetHordePriority(isPriority);
            }

            if (chosen == null)
            {
                _gameContext.Log("Horde Mode: one objective or fewer left - there is no priority objective.");
                await _gameContext.Announce("No Priority Objective", new TextColor(200, 200, 200, 255));
                return;
            }

            _gameContext.Log($"Horde Mode: objective {chosen} is the priority objective.");
            await _gameContext.Announce($"Priority Objective: {chosen}", new TextColor(255, 210, 80, 255));
        }

        internal async Task DeployWave(int wave)
        {
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(_gameContext.TableState);

            List<UnitData> returning = HordePlacement.AllUnits(_gameContext)
                .Where(u => !defenders.Contains(u.PlayerID))
                .Where(u => !ReserveRules.IsInReserve(u))
                .Where(u => !u.GetIsAlive() || u.Tokens.HasToken(TokenType.Shaken))
                .ToList();

            if (returning.Count == 0)
            {
                // Said plainly, rather than a "Wave N incoming!" splash that nothing follows (QA 5.2).
                await _gameContext.Announce($"Wave {wave}: no enemies to send back.", new TextColor(255, 120, 80, 255),
                    EBannerTier.Headline);
                _gameContext.Log($"Horde Mode: wave {wave} - no destroyed or Shaken enemies to return.");
                return;
            }

            await _gameContext.Announce($"Wave {wave} incoming!", new TextColor(255, 120, 80, 255),
                EBannerTier.Headline);

            _gameContext.Log($"Horde Mode: wave {wave} - {returning.Count} enemy unit(s) return at random corners.");
            foreach (UnitData unit in returning)
            {
                HordePlacement.Revive(unit);
                await HordePlacement.PlaceAtRandomCorner(_gameContext, unit, $"wave {wave}");
            }
        }

        internal async Task DamageObjectives()
        {
            ITableState table = _gameContext.TableState;
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(table);

            var modelToUnit = new Dictionary<IModel, IUnit>();
            foreach (IUnit unit in table.Units.Objects)
                foreach (IModel model in unit.Models)
                    modelToUnit[model] = unit;

            foreach (DataBinding<ObjectiveData> binding in _gameContext.GameDataStore
                         .GetAllDataBindings<ObjectiveData>().ToList())
            {
                ObjectiveData objective = binding.GetValue();
                HashSet<PlayerID> near = ReconcileObjectivesStage.PlayersNearObjective(
                    objective, table.Models.Objects, modelToUnit);

                bool enemyNear = near.Any(p => !defenders.Contains(p));
                bool defenderNear = near.Any(defenders.Contains);
                if (!enemyNear || defenderNear) continue;

                int number = NumberOf(objective);
                int damage = objective.HordeDamage + 1;
                objective.SetHordeDamage(damage);

                if (damage < HordeRules.ObjectiveDamageToDestroy)
                {
                    _gameContext.Log($"Horde Mode: enemies seize objective {number} - damage {damage}/{HordeRules.ObjectiveDamageToDestroy}.");
                    await _gameContext.Announce($"Objective {number} damaged ({damage}/{HordeRules.ObjectiveDamageToDestroy})",
                        new TextColor(255, 140, 60, 255));
                    continue;
                }

                // A destroyed priority objective simply leaves no priority until the next one is set.
                _gameContext.GameDataStore.Destroy(binding.Reference);

                _gameContext.Log($"Horde Mode: objective {number} has taken {damage} damage and is destroyed.");
                await _gameContext.Announce($"Objective {number} destroyed!", new TextColor(255, 80, 60, 255),
                    EBannerTier.Headline);
            }
        }

        /// <summary>
        /// A point-reward round (the end of rounds 3, 6, 9 and 12): every objective with a defending unit
        /// within 3" and no enemy within 3" counts as seized - 40 points each, +20 if the priority
        /// objective is one of them (30 and +15 in the skirmish games). Uses the same "within 3"" test as
        /// objective control, so Shaken units and units that arrived this round don't count.
        /// </summary>
        internal async Task<List<ObjectiveData>> AwardWavePoints(int wave)
        {
            ITableState table = _gameContext.TableState;
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(table);

            var modelToUnit = new Dictionary<IModel, IUnit>();
            foreach (IUnit unit in table.Units.Objects)
                foreach (IModel model in unit.Models)
                    modelToUnit[model] = unit;

            var seizedObjectives = new List<ObjectiveData>();
            int seized = 0;
            bool prioritySeized = false;
            foreach (ObjectiveData objective in _gameContext.GameDataStore.GetAllValues<ObjectiveData>()
                         .OrderBy(o => NumberOf(o)).ToList())
            {
                HashSet<PlayerID> near = ReconcileObjectivesStage.PlayersNearObjective(
                    objective, table.Models.Objects, modelToUnit);
                if (!near.Any(defenders.Contains) || near.Any(p => !defenders.Contains(p))) continue;

                seized++;
                seizedObjectives.Add(objective);
                if (objective.IsHordePriority) prioritySeized = true;
                _gameContext.Log($"  Defenders hold objective {NumberOf(objective)}" +
                    (objective.IsHordePriority ? " (priority)." : "."));
            }

            int reward = HordeRules.WaveReward(seized, prioritySeized);
            HordeScore? score = GameProgressUtilities.AddHordePoints(_gameContext.GameDataStore, reward);
            int total = score?.PointsEarned ?? reward;

            string detail = seized == 0
                ? "no objectives held"
                : $"{seized} objective{(seized == 1 ? "" : "s")} held" + (prioritySeized ? " incl. priority" : "");
            // #409: the reward is a ROUND event in 3.6 (end of rounds 3, 6, 9, 12) and the high score
            // multiplies by rounds, so the player is told which round paid out rather than which wave.
            _gameContext.Log($"Horde Mode: round {_gameContext.TableState.Progress.RoundCount} point reward"
                + $" - {detail}: +{reward} points (total {total}).");
            await _gameContext.Announce($"+{reward} points: {detail}", new TextColor(255, 215, 0, 255),
                EBannerTier.Headline);
            return seizedObjectives;
        }

        private static int NumberOf(IObjective objective) =>
            objective.HordeNumber
            ?? HordeRules.ObjectiveNumber(objective.Position, HordePlacement.TableWidth, HordePlacement.TableHeight);
    }
}
