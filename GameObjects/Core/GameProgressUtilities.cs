using FDG.Data;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// Reads and writes the single <see cref="GameProgressData"/> instance that mirrors the
    /// game-flow position (round, turn cursor, unactivated units) into the store, and captures that
    /// position from the live <see cref="IMainPhaseContext"/> / <see cref="ISingleRoundContext"/>.
    /// <para>
    /// The capture call belongs at an activation boundary (the quiescent point between unit
    /// activations), where no transient combat/movement context exists. That wiring lands with the
    /// rolling-snapshot mechanism; this class is the data plumbing it builds on.
    /// </para>
    /// </summary>
    public static class GameProgressUtilities
    {
        /// <summary>The single progress record, or null if none has been written (or the type isn't registered).</summary>
        public static GameProgressData? TryGetProgress(IReadableGameDataStore store)
        {
            if (!store.IsTypeAssigned<GameProgressData>())
                return null;

            foreach (GameProgressData progress in store.GetAllValues<GameProgressData>())
                return progress;

            return null;
        }

        /// <summary>
        /// Writes <paramref name="progress"/> as the store's single progress record — updating the
        /// existing one in place (firing update events) if present, otherwise creating it.
        /// </summary>
        public static void WriteProgress(IReadWriteableGameDataStore store, GameProgressData progress)
        {
            // Horde Mode's score is not part of the per-turn snapshot (Capture knows nothing of it), so a
            // snapshot that arrives without one keeps the score already on the record.
            if (progress.Horde == null)
                progress.Horde = TryGetProgress(store)?.Horde;
            if (progress.Quest == null)
                progress.Quest = TryGetProgress(store)?.Quest;
            // Star Quest state saved during set-up (before the first progress record exists) moves in now.
            if (progress.Quest == null && PendingQuest.TryGetValue(store, out QuestProgress? pending))
                progress.Quest = pending.Clone();
            PendingQuest.Remove(store);

            // #429: the same carry-forward for the deployment zones. They are chosen during deployment,
            // before the main phase creates the first progress record, so they need the pending hop as
            // well as the carry-forward - otherwise the first snapshot of the game would erase them, and
            // every test that set them directly would still pass.
            if (progress.DeploymentZones == null)
                progress.DeploymentZones = TryGetProgress(store)?.DeploymentZones;
            if (progress.DeploymentZones == null
                && PendingZones.TryGetValue(store, out Dictionary<int, ZoneRect>? zones))
                progress.DeploymentZones = CloneZones(zones);
            PendingZones.Remove(store);

            // #434: the same two hops for the underdog pool. It is computed at mission setup - before the
            // main phase writes the first progress record - so the pending hop is what gets it into the
            // game at all, and the carry-forward is what stops the next snapshot erasing it mid-mission.
            if (progress.Underdog == null)
                progress.Underdog = TryGetProgress(store)?.Underdog;
            if (progress.Underdog == null
                && PendingUnderdog.TryGetValue(store, out Dictionary<int, int>? pool))
                progress.Underdog = new Dictionary<int, int>(pool);
            PendingUnderdog.Remove(store);

            // Mission Cards: the same two hops. The decks are dealt at the start of round 1, which can run
            // before the first progress record of the game is written.
            if (progress.MissionCards == null)
                progress.MissionCards = TryGetProgress(store)?.MissionCards;
            if (progress.MissionCards == null
                && PendingMissionCards.TryGetValue(store, out FDG.Missions.MissionCardState? cards))
                progress.MissionCards = cards;
            PendingMissionCards.Remove(store);

            foreach (DataReference existing in store.GetAllDataReferences<GameProgressData>())
            {
                store.SetValue(existing, progress);
                return;
            }

            store.Create(progress);
        }

        /// <summary>
        /// Horde Mode: adds <paramref name="points"/> to the defenders' earned and available points and
        /// returns the new score. No-op (null) if no progress record exists yet.
        /// </summary>
        /// <summary>Horde Mode: spends shop points (the earned total, which the high score uses, is unchanged).</summary>
        /// <summary>Star Quest: the mission's wave state (a copy - change it and pass it to
        /// <see cref="SetQuestProgress"/>). A fresh state if none is stored yet.</summary>
        public static QuestProgress GetQuestProgress(IReadableGameDataStore store) =>
            TryGetProgress(store)?.Quest?.Clone()
            ?? (PendingQuest.TryGetValue(store, out QuestProgress? pending) ? pending.Clone() : new QuestProgress());

        /// <summary>Star Quest state still waiting for the first progress record (set-up / deployment), or null.</summary>
        public static QuestProgress? PendingQuestFor(IReadableGameDataStore store) =>
            PendingQuest.TryGetValue(store, out QuestProgress? pending) ? pending : null;

        /// <summary>Star Quest state written during map set-up / deployment, before the main phase creates the
        /// progress record; it moves into the first record written.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, QuestProgress> PendingQuest = new();

        /// <summary>#429 - deployment zones written during deployment, waiting for the first progress record.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<int, ZoneRect>> PendingZones = new();

        /// <summary>#434 - the underdog pool written at mission setup, waiting for the first progress record.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, Dictionary<int, int>> PendingUnderdog = new();

        /// <summary>The Mission Cards' state written before the first progress record exists.</summary>
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, FDG.Missions.MissionCardState> PendingMissionCards = new();

        /// <summary>
        /// The game's Mission Cards state - the live object, which the mission stages change in place on every
        /// machine in lockstep - or null when the game has none (yet).
        /// </summary>
        public static FDG.Missions.MissionCardState? MissionCards(IReadableGameDataStore store) =>
            TryGetProgress(store)?.MissionCards
            ?? (PendingMissionCards.TryGetValue(store, out FDG.Missions.MissionCardState? pending) ? pending : null);

        /// <summary>Stores the Mission Cards state, on the progress record or waiting for it.</summary>
        public static void SetMissionCards(IReadWriteableGameDataStore store, FDG.Missions.MissionCardState state)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress != null)
            {
                progress.MissionCards = state;
                // Written through, so a change made after the last per-turn snapshot - the final
                // objective VP at game end - reaches a joined player too (network play-through,
                // 2026-11-06: the client's Game Over card showed the winner on 0 VP).
                WriteProgress(store, progress);
                return;
            }
            PendingMissionCards.Remove(store);
            PendingMissionCards.Add(store, state);
        }

        /// <summary>
        /// #434 - writes this mission's underdog pool (slot id -> points). Call once at mission setup,
        /// from <c>UnderdogPool.Seed</c>; before the first progress record exists it waits in the pending
        /// table and moves into that record, exactly as the Star Quest and deployment-zone state does.
        /// </summary>
        public static void SetUnderdogPool(IReadWriteableGameDataStore store, Dictionary<int, int> pool)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress != null)
            {
                progress.Underdog = new Dictionary<int, int>(pool);
                return;
            }

            PendingUnderdog.Remove(store);
            PendingUnderdog.Add(store, new Dictionary<int, int>(pool));
        }

        /// <summary>
        /// #434 - this mission's underdog pool, from the progress record or from the pending table when
        /// the record does not exist yet. Empty when there is none, so callers need no null handling: a
        /// standard (non-campaign) game simply has no underdog points.
        /// </summary>
        public static IReadOnlyDictionary<int, int> UnderdogPool(IReadableGameDataStore store)
        {
            Dictionary<int, int>? stored = TryGetProgress(store)?.Underdog;
            if (stored != null) return stored;
            return PendingUnderdog.TryGetValue(store, out Dictionary<int, int>? pending)
                ? pending
                : new Dictionary<int, int>();
        }

        /// <summary>
        /// #434 - takes one underdog point off <paramref name="slotId"/>, returning false (and changing
        /// nothing) when that seat has none left. The write goes wherever the pool currently lives, so
        /// spending works before and after the first progress record.
        /// </summary>
        public static bool TrySpendUnderdogPoint(IReadWriteableGameDataStore store, int slotId)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress?.Underdog != null)
            {
                if (!progress.Underdog.TryGetValue(slotId, out int left) || left <= 0) return false;
                progress.Underdog[slotId] = left - 1;
                return true;
            }

            if (!PendingUnderdog.TryGetValue(store, out Dictionary<int, int>? pending)) return false;
            if (!pending.TryGetValue(slotId, out int pendingLeft) || pendingLeft <= 0) return false;
            pending[slotId] = pendingLeft - 1;
            return true;
        }

        private static Dictionary<int, ZoneRect> CloneZones(Dictionary<int, ZoneRect> zones)
        {
            var copy = new Dictionary<int, ZoneRect>(zones.Count);
            foreach ((int team, ZoneRect rect) in zones) copy[team] = rect.Clone();
            return copy;
        }

        /// <summary>
        /// #429 - records each team's deployment zone, by <see cref="ITeam.TeamNumber"/>. Called once, at
        /// the end of the stage that chooses them, which is before the main phase has written any progress
        /// record - so it parks them until <see cref="WriteProgress"/> can fold them in.
        /// </summary>
        public static void SetDeploymentZones(IReadWriteableGameDataStore store, Dictionary<int, ZoneRect> zones)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress == null)
            {
                PendingZones.Remove(store);
                PendingZones.Add(store, CloneZones(zones));
                return;
            }

            progress.DeploymentZones = CloneZones(zones);
            WriteProgress(store, progress);
        }

        /// <summary>
        /// Every team's deployment zone, or null when this game never recorded them (a pre-#429 save, or a
        /// game whose deployment chose none). Reads the pending set too, so a stage running inside
        /// deployment can see what an earlier one chose.
        /// </summary>
        public static IReadOnlyDictionary<int, ZoneRect>? DeploymentZones(IReadableGameDataStore store)
        {
            if (TryGetProgress(store)?.DeploymentZones is { Count: > 0 } stored) return stored;
            return PendingZones.TryGetValue(store, out Dictionary<int, ZoneRect>? pending) ? pending : null;
        }

        /// <summary>One team's deployment zone, or null if it has none recorded.</summary>
        public static ZoneRect? DeploymentZoneOf(IReadableGameDataStore store, int teamNumber) =>
            DeploymentZones(store) is { } zones && zones.TryGetValue(teamNumber, out ZoneRect? zone) ? zone : null;

        /// <summary>Star Quest: stores the mission's wave state. No-op if no progress record exists yet.</summary>
        public static void SetQuestProgress(IReadWriteableGameDataStore store, QuestProgress quest)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress == null)
            {
                PendingQuest.AddOrUpdate(store, quest.Clone());
                return;
            }
            progress.Quest = quest.Clone();
            WriteProgress(store, progress);
        }

        public static HordeScore? SpendHordePoints(IReadWriteableGameDataStore store, int points)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress == null) return null;
            progress.Horde = (progress.Horde ?? new HordeScore(0, 0)).WithSpent(points);
            WriteProgress(store, progress);
            return progress.Horde;
        }

        public static HordeScore? AddHordePoints(IReadWriteableGameDataStore store, int points)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress == null) return null;
            progress.Horde = (progress.Horde ?? new HordeScore(0, 0)).WithEarned(points);
            WriteProgress(store, progress);
            return progress.Horde;
        }

        /// <summary>
        /// Records the unit currently taking its activation (or clears it with null) on the store's
        /// progress record, so <see cref="IGameProgress.ActivatingUnit"/> can surface it. No-op if no
        /// progress record exists yet (pre-main-phase, or minimal test stores). Writes through
        /// <see cref="WriteProgress"/> so the change replicates to clients like the rest of the record.
        /// </summary>
        public static void SetActivatingUnit(IReadWriteableGameDataStore store, DataBinding<UnitData>? unit)
        {
            GameProgressData? progress = TryGetProgress(store);
            if (progress == null) return;
            progress.ActivatingUnit = unit;
            WriteProgress(store, progress);
        }

        /// <summary>
        /// Snapshots the live round/turn position into a serializable <see cref="GameProgressData"/>.
        /// Teams are recorded by <see cref="ITeam.TeamNumber"/>; unactivated units are flattened
        /// across players (each unit carries its own <see cref="UnitData.PlayerID"/>). The round
        /// number is read from <see cref="ISingleRoundContext.RoundCount"/>, which carries the value
        /// down from the <see cref="IMainPhaseContext"/>.
        /// </summary>
        public static GameProgressData Capture(
            ISingleRoundContext round,
            GameSettings settings,
            EResumeStage stage)
        {
            List<DataBinding<UnitData>> unactivated = new List<DataBinding<UnitData>>();
            foreach (KeyValuePair<PlayerID, List<DataBinding<UnitData>>> kvp in round.UnactivatedUnits)
                unactivated.AddRange(kvp.Value);

            List<int> teamOrder = new List<int>();
            foreach (ITeam team in round.TeamActivateOrder)
                teamOrder.Add(team.TeamNumber);

            List<int> finishOrder = new List<int>();
            foreach (ITeam team in round.CurrentRoundTeamFinishOrder)
                finishOrder.Add(team.TeamNumber);

            Dictionary<int, int> playerIndexByTeam = new Dictionary<int, int>();
            foreach (KeyValuePair<ITeam, int> kvp in round.CurrentActivePlayerIndexPerTeam)
                playerIndexByTeam[kvp.Key.TeamNumber] = kvp.Value;

            return new GameProgressData(
                stage: stage,
                roundCount: round.RoundCount,
                teamActivateOrder: teamOrder,
                currentRoundTeamFinishOrder: finishOrder,
                currentTeamIndex: round.CurrentActivatingTeamIndex,
                currentPlayerIndexPerTeam: playerIndexByTeam,
                unactivatedUnits: unactivated,
                settings: settings);
        }

        /// <summary>Rebuilds the main-phase context (round number + activation order) from a snapshot.</summary>
        public static MainPhaseContext RestoreMainPhaseContext(IGameContext gameContext, GameProgressData progress)
        {
            List<ITeam> allTeams = gameContext.TableState.Teams.Objects.ToList();
            return new MainPhaseContext(
                gameContext, WithMissingTeamsAppended(allTeams, progress.TeamActivateOrder), progress.RoundCount);
        }

        /// <summary>
        /// A saved activation order, with any team the snapshot omits appended. The activation order is the
        /// team list the round alternates over, so a truncated one silently benches a player for the rest of
        /// the game. Saves written before that bug was fixed carry exactly such an order, and loading one
        /// would otherwise resume straight back into the skipped-activations state - so the order is repaired
        /// on the way in rather than trusted.
        /// </summary>
        private static List<ITeam> WithMissingTeamsAppended(List<ITeam> allTeams, List<int> savedTeamNumbers)
        {
            List<ITeam> order = MapTeams(allTeams, savedTeamNumbers);
            foreach (ITeam team in allTeams)
            {
                if (order.Contains(team) == false)
                    order.Add(team);
            }

            return order;
        }

        /// <summary>
        /// Rebuilds the in-progress round context from a snapshot: maps team numbers back to the
        /// store's canonical <see cref="ITeam"/> instances, restores the cursor + finish order, and
        /// regroups the unactivated units by owner (with an entry for every player so
        /// <see cref="ISingleRoundContext.MarkUnitAsActivated"/> never misses a key).
        /// </summary>
        public static SingleRoundContext RestoreSingleRoundContext(IGameContext gameContext, GameProgressData progress)
        {
            List<ITeam> allTeams = gameContext.TableState.Teams.Objects.ToList();
            List<ITeam> teamOrder = WithMissingTeamsAppended(allTeams, progress.TeamActivateOrder);
            List<ITeam> finishOrder = MapTeams(allTeams, progress.CurrentRoundTeamFinishOrder);

            Dictionary<ITeam, int> playerIndexPerTeam = new Dictionary<ITeam, int>();
            foreach (KeyValuePair<int, int> kvp in progress.CurrentPlayerIndexPerTeam)
            {
                playerIndexPerTeam[FindTeam(allTeams, kvp.Key)] = kvp.Value;
            }

            Dictionary<PlayerID, List<DataBinding<UnitData>>> unactivated = new Dictionary<PlayerID, List<DataBinding<UnitData>>>();
            foreach (ITeam team in allTeams)
            {
                foreach (PlayerID playerID in team.Players)
                {
                    if (!unactivated.ContainsKey(playerID))
                    {
                        unactivated[playerID] = new List<DataBinding<UnitData>>();
                    }
                }
            }

            foreach (DataBinding<UnitData> unit in progress.UnactivatedUnits)
            {
                PlayerID owner = unit.GetValue().PlayerID;
                if (!unactivated.ContainsKey(owner))
                {
                    unactivated[owner] = new List<DataBinding<UnitData>>();
                }

                unactivated[owner].Add(unit);
            }

            return new SingleRoundContext(gameContext, teamOrder, progress.RoundCount,
                progress.CurrentTeamIndex, playerIndexPerTeam, finishOrder, unactivated);
        }

        private static List<ITeam> MapTeams(IReadOnlyList<ITeam> allTeams, List<int> teamNumbers)
        {
            List<ITeam> result = new List<ITeam>(teamNumbers.Count);
            foreach (int teamNumber in teamNumbers)
            {
                result.Add(FindTeam(allTeams, teamNumber));
            }

            return result;
        }

        private static ITeam FindTeam(IReadOnlyList<ITeam> allTeams, int teamNumber)
        {
            foreach (ITeam team in allTeams)
            {
                if (team.TeamNumber == teamNumber)
                {
                    return team;
                }
            }

            throw new InvalidOperationException(
                $"Saved game references team {teamNumber}, which isn't present in the restored store.");
        }
    }
}
