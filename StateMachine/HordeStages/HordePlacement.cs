using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;

namespace FDG.Stages
{
    /// <summary>
    /// Shared Horde Mode placement and side bookkeeping, used by the opening deployment and every later
    /// enemy wave: who defends, which units are enemies, the random-corner roll, and putting a unit down
    /// inside a zone through the normal placement request (so a human or a bot places it).
    /// </summary>
    public static class HordePlacement
    {
        /// <summary>Task name for enemy wave placements. Deliberately not the standard deployment name,
        /// so the Tactician's objective-lane logic (built for edge deployment zones) is not applied to a
        /// corner circle; it falls back to its generic in-zone placement.</summary>
        public const string WaveTaskName = "Horde Wave Deploy";

        public static float TableWidth => GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        public static float TableHeight => GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

        public static HashSet<PlayerID> DefenderPlayers(ITableState table)
        {
            int? defending = HordeRules.DefendingTeamNumber(table.Players.Objects);
            return table.Players.Objects
                .Where(p => p.IsFilled && p.TeamNumber == defending)
                .Select(p => p.PlayerID)
                .ToHashSet();
        }

        /// <summary>Every unit in the game with its owning side, in army then list order.</summary>
        public static List<UnitData> AllUnits(IGameContext gameContext) =>
            gameContext.GameDataStore.GetAllValues<ArmyData>()
                .SelectMany(army => army.UnitBindings.Select(binding => binding.GetValue()))
                .ToList();

        /// <summary>One decisive D6 (a real face even under the probabilistic roller).</summary>
        public static int RollD6(IGameContext gameContext)
        {
            IDiceResults result = gameContext.DiceRoller.RollDecisive(6);
            int roll = result.SideMin;
            for (int v = result.SideMin; v <= result.SideMax; v++)
            {
                if (result.At(v) > 0f) { roll = v; break; }
            }
            return roll;
        }

        /// <summary>
        /// How far a deployment area grows when it is too crowded to hold a unit without models standing
        /// inside each other: the rules' 9", then as close to it as there is room for. On the tabletop
        /// players do the same thing by eye when a corner is already packed.
        /// </summary>
        public static float[] CrowdedRadiiInches { get; internal set; } = { 9f, 12f, 15f, 18f };

        /// <summary>
        /// Places a unit within the first radius of <see cref="CrowdedRadiiInches"/> around a point that
        /// holds it cleanly, via its owner (human or bot), and commits the positions.
        /// </summary>
        /// <param name="makeZone">Builds the placement area for a given radius.</param>
        /// <returns>The radius actually used.</returns>
        public static async Task<float> PlaceUnitNear(IGameContext gameContext, UnitData unit,
            Func<float, IBoundedZone> makeZone, string taskName, string displayName,
            IReadOnlyList<DataBinding<ModelData>>? models = null, float[]? radii = null)
        {
            radii ??= CrowdedRadiiInches;
            // #414: Star Quest's Scout is just "may be deployed anywhere fully within 9" of its
            // deployment zone", and a Star Quest unit reaches the table here rather than through normal
            // deployment - so this is the site where that 9" has to land. No effect in any other system.
            float[] baseRadii = radii;
            radii = Rules.Dispatch.ScoutDeploymentRules.WidenForScout(gameContext, unit, radii);
            if (!ReferenceEquals(radii, baseRadii))
            {
                gameContext.Log($"  {unit.Name} has Scout, so it may deploy within {radii[0]:0}\" "
                    + $"instead of {baseRadii[0]:0}\".");
            }

            List<DataBinding<ModelData>> toPlace = (models ?? unit.ModelBindings).ToList();
            List<PlacedObjectEntry<ModelData>> placements = new List<PlacedObjectEntry<ModelData>>();
            float used = radii[0];

            for (int i = 0; i < radii.Length; i++)
            {
                used = radii[i];
                bool last = i == radii.Length - 1;
                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, taskName, makeZone(used),
                    toPlace, displayName: displayName);

                placements = await PlacementRequesting.RequestMandatoryPlacement(gameContext.PlayerRequester, request);
                placements = await PlacementCommitGuard.EnsureClear(gameContext, request, placements, logWarnings: last);

                (_, float depth) = PlacementCommitGuard.WorstOverlap(gameContext.TableState, request, placements);
                if (depth <= PlacementCommitGuard.OverlapToleranceInches || last) break;
            }

            if (used > radii[0])
            {
                gameContext.Log($"  {unit.Name}: the area was crowded, so it deployed within {used:0}\" instead.");
            }

            Commit(unit, placements);
            return used;
        }

        /// <summary>
        /// The smallest radius of <see cref="CrowdedRadiiInches"/> around a point that the unit can still
        /// fit into without overlapping anything already on the table, judged by a dry run of the bot's
        /// placer (nothing is moved). Lets a human be offered an area they can actually fill, instead of a
        /// 9" circle that is already packed - which used to leave them unable to finish deploying.
        /// </summary>
        public static async Task<float> RoomyRadius(IGameContext gameContext, UnitData unit,
            Func<float, IBoundedZone> makeZone, string taskName, float[]? radii = null)
        {
            radii ??= CrowdedRadiiInches;
            foreach (float radius in radii)
            {
                var probe = new PlaceObjectsRequest<ModelData>(unit.PlayerID, taskName, makeZone(radius),
                    unit.ModelBindings);
                CancellableResult<List<PlacedObjectEntry<ModelData>>> trial =
                    await new FDG.Ai.Resolvers.AiPlaceObjectsResolver<ModelData>(gameContext.TableState).Resolve(probe);
                if (trial is Selected<List<PlacedObjectEntry<ModelData>>> selected)
                {
                    (_, float depth) = PlacementCommitGuard.WorstOverlap(gameContext.TableState, probe, selected.Value);
                    if (depth <= PlacementCommitGuard.OverlapToleranceInches) return radius;
                }
            }
            return radii[^1];
        }

        /// <summary>Takes a placed unit back off the table (Horde deployment undo): every model returns to
        /// the unplaced (0,0) sentinel the deployment starts from.</summary>
        public static void Unplace(UnitData unit)
        {
            foreach (DataBinding<ModelData> binding in unit.ModelBindings)
            {
                binding.GetValue().SetPosition(new Position(0f, 0f));
                binding.GetValue().SetFacing(ModelData.DefaultFacing);
            }
        }

        /// <summary>
        /// Deploys a party one hero at a time, then keeps offering to pick a hero back up and put it down
        /// again until the player says they are done.
        ///
        /// <para>Deployment used to be a one-way loop: a hero dropped in an awkward spot could leave the
        /// next one with nowhere legal to stand, and there was no way back - the phase simply could not be
        /// finished. Confirming a placement is now reversible right up to the moment the mission starts.</para>
        /// </summary>
        public static async Task PlacePartyNear(IGameContext gameContext, IReadOnlyList<UnitData> heroes,
            Func<float, IBoundedZone> makeZone, string taskName, float[]? radii = null)
        {
            if (heroes.Count == 0) return;

            const string done = "Start the mission";
            var placed = new HashSet<UnitID>();

            // Re-placements a party may make before deployment ends itself. Deliberately far more than
            // anyone would use by hand - it exists so the loop below cannot run forever, not to ration
            // a player's fiddling.
            int repositionsLeft = Math.Max(200, heroes.Count * 50);

            while (true)
            {
                UnitData? next = heroes.FirstOrDefault(h => !placed.Contains(h.ID));
                if (next != null)
                {
                    await PlaceUnitNear(gameContext, next, makeZone, taskName, $"Deploying {next.Name}", radii: radii);
                    placed.Add(next.ID);
                    continue;
                }

                // "Start the mission" goes FIRST. Every automated answerer takes the first valid option
                // (AiStringSelectionResolver's fallback, and the CLI resolvers' EOF default), so with
                // "Move <hero>" first a bot-run party unplaced a hero, placed it again, and was asked
                // again - forever. That is the deployment hang: ~500k placements and ~500k menu answers
                // in 150s, at 100% CPU, which is what made the Star Quest horde tests report
                // RoundsDone == 0 and never finish.
                var labels = new List<string> { done };
                labels.AddRange(heroes.Select(h => $"Move {h.Name}"));
                var request = new StringSelectionRequest(heroes[0].PlayerID,
                    "Everyone is deployed. Start the mission, or pick a hero up to place it again.",
                    labels, Array.Empty<StringSelectionRequest.InvalidOption>(),
                    displayName: "Deployment");
                string picked = await gameContext.PlayerRequester
                    .RequestDecision<StringSelectionRequest, string>(request);
                if (picked == done) return;

                UnitData? again = heroes.FirstOrDefault(h => $"Move {h.Name}" == picked);
                if (again == null) return;   // an unexpected reply (a bot, EOF): take it as "done"

                // Backstop for any answerer that neither picks "done" nor replies unexpectedly - it
                // would otherwise loop here with no way out.
                if (--repositionsLeft <= 0)
                {
                    gameContext.Log("Deployment: too many re-placements in a row - starting the mission.");
                    return;
                }

                gameContext.Log($"{again.Name} is picked back up to be placed again.");
                Unplace(again);
                placed.Remove(again.ID);
            }
        }

        public static void Commit(UnitData unit, List<PlacedObjectEntry<ModelData>> placements)
        {
            // Same order as the standard deployment: clear the off-table state, then land the models.
            ReserveRules.ClearReserve(unit);
            foreach (PlacedObjectEntry<ModelData> entry in placements)
            {
                entry.Binding.GetValue().SetPosition(entry.Position);
                if (entry.Facing.HasValue) entry.Binding.GetValue().SetFacing(entry.Facing.Value);
            }
        }

        /// <summary>
        /// Rolls the unit's corner (1-4, or the defenders' pick on a 5-6) and places it within 9" of it.
        /// Returns the corner used.
        /// </summary>
        public static async Task<int> PlaceAtRandomCorner(IGameContext gameContext, UnitData unit, string reason,
            float[]? radii = null)
        {
            int roll = RollD6(gameContext);
            int corner = roll >= 1 && roll <= 4 ? roll : await AskDefendersForCorner(gameContext, unit, roll);
            string how = roll <= 4 ? $"rolled {roll}" : $"rolled {roll}, defenders chose";
            gameContext.Log($"  {unit.Name} ({reason}): {how} corner {corner}.");

            Position c = HordeRules.CornerPosition(corner, TableWidth, TableHeight);
            await PlaceUnitNear(gameContext, unit,
                radius => new TableCornerZone(new Float2(c.x, c.z), radius, TableWidth, TableHeight),
                WaveTaskName, $"Deploying {unit.Name}", radii: radii);
            return corner;
        }

        /// <summary>
        /// A 5-6 on the arrival roll: the defenders choose the corner. Asked of the defending player, with
        /// <see cref="DefendersPickCorner"/>'s pick on top as the suggestion (a bot defender takes it).
        /// </summary>
        public static async Task<int> AskDefendersForCorner(IGameContext gameContext, UnitData unit, int roll)
        {
            int suggested = DefendersPickCorner(gameContext.TableState);
            PlayerID? chooser = DefenderChooser(gameContext);
            if (chooser == null) return suggested;

            List<Position> defenders = LivingOnTableModels(gameContext.TableState, DefenderPlayers(gameContext.TableState));
            var corners = new[] { 1, 2, 3, 4 }.OrderBy(c => c != suggested).ThenBy(c => c).ToList();
            var labels = new List<string>();
            var descriptions = new Dictionary<string, string>();
            foreach (int c in corners)
            {
                string label = $"Corner {c} ({HordeRules.CornerName(c)})" + (c == suggested ? " - suggested" : "");
                labels.Add(label);
                Position at = HordeRules.CornerPosition(c, TableWidth, TableHeight);
                descriptions[label] = defenders.Count == 0
                    ? ""
                    : $"Your nearest model is {defenders.Min(p => Position.GetDistance2D(p, at)):0}\" from this corner.";
            }

            var request = new StringSelectionRequest(chooser.Value,
                $"{unit.Name} rolled a {roll} for its arrival corner - you choose where it comes in.",
                labels, Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions,
                displayName: $"Choosing where {unit.Name} arrives");
            string picked = await gameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            int index = labels.IndexOf(picked);
            return corners[index >= 0 ? index : 0];
        }

        /// <summary>
        /// A 6 on the priority roll: the defenders choose the priority objective from
        /// <paramref name="eligible"/>, with <see cref="DefendersPickPriority"/>'s pick on top as the suggestion.
        /// </summary>
        public static async Task<int> AskDefendersForPriority(IGameContext gameContext, IReadOnlyList<int> eligible)
        {
            int suggested = DefendersPickPriority(gameContext.TableState, eligible);
            PlayerID? chooser = DefenderChooser(gameContext);
            if (chooser == null || eligible.Count == 1) return suggested;

            List<Position> defenders = LivingOnTableModels(gameContext.TableState, DefenderPlayers(gameContext.TableState));
            var positions = gameContext.GameDataStore.GetAllValues<ObjectiveData>()
                .Where(o => o.HordeNumber != null)
                .ToDictionary(o => o.HordeNumber!.Value, o => o.Position);
            var numbers = eligible.OrderBy(n => n != suggested).ThenBy(n => n).ToList();
            var labels = new List<string>();
            var descriptions = new Dictionary<string, string>();
            foreach (int n in numbers)
            {
                string label = $"Objective {n}" + (n == suggested ? " - suggested" : "");
                labels.Add(label);
                descriptions[label] = defenders.Count == 0 || !positions.TryGetValue(n, out Position at)
                    ? "Holding the priority objective at the end of a wave is worth 20 extra points."
                    : $"Your nearest model is {defenders.Min(p => Position.GetDistance2D(p, at)):0}\" away. " +
                      "Holding it at the end of a wave is worth 20 extra points.";
            }

            var request = new StringSelectionRequest(chooser.Value,
                "The priority roll was a 6 - you choose the new priority objective.",
                labels, Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions,
                displayName: "Choosing the priority objective");
            string picked = await gameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            int index = labels.IndexOf(picked);
            return numbers[index >= 0 ? index : 0];
        }

        /// <summary>The defending player who makes the side's choices: the lowest-numbered defending slot.</summary>
        private static PlayerID? DefenderChooser(IGameContext gameContext)
        {
            HashSet<PlayerID> defenders = DefenderPlayers(gameContext.TableState);
            if (!gameContext.GameDataStore.IsTypeAssigned<PlayerSlotInfo>()) return defenders.Cast<PlayerID?>().FirstOrDefault();
            return gameContext.GameDataStore.GetAllValues<PlayerSlotInfo>()
                .Where(s => defenders.Contains(s.PlayerID))
                .OrderBy(s => s.SlotID)
                .Select(s => (PlayerID?)s.PlayerID)
                .FirstOrDefault();
        }

        /// <summary>
        /// The suggested corner on a 5-6 (and what a bot defender picks): the corner farthest from the
        /// defenders' nearest model (lowest number on a tie).
        /// </summary>
        public static int DefendersPickCorner(ITableState table)
        {
            List<Position> defenderModels = LivingOnTableModels(table, DefenderPlayers(table));
            int bestCorner = 1;
            float bestDist = float.MinValue;
            for (int corner = 1; corner <= 4; corner++)
            {
                Position c = HordeRules.CornerPosition(corner, TableWidth, TableHeight);
                float nearest = defenderModels.Count == 0
                    ? 0f
                    : defenderModels.Min(p => Position.GetDistance2D(p, c));
                if (nearest > bestDist + 0.001f) { bestDist = nearest; bestCorner = corner; }
            }
            return bestCorner;
        }

        /// <summary>
        /// The suggested priority objective on a 6 (and what a bot defender picks): the eligible objective
        /// nearest the middle of the defenders' army (lowest number on a tie).
        /// </summary>
        public static int DefendersPickPriority(ITableState table, IReadOnlyList<int> eligible)
        {
            List<Position> defenderModels = LivingOnTableModels(table, DefenderPlayers(table));
            if (defenderModels.Count == 0) return eligible[0];

            var middle = new Position(defenderModels.Average(p => p.x), defenderModels.Average(p => p.z));
            var anchors = HordeRules.ObjectiveAnchors(TableWidth, TableHeight);
            return eligible
                .OrderBy(n => Position.GetDistance2D(anchors[n - 1], middle))
                .ThenBy(n => n)
                .First();
        }

        private static List<Position> LivingOnTableModels(ITableState table, HashSet<PlayerID> owners)
        {
            var result = new List<Position>();
            foreach (IUnit unit in table.Units.Objects)
            {
                if (!owners.Contains(unit.PlayerID)) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    if (model.Position.x == 0f && model.Position.z == 0f) continue; // unplaced
                    result.Add(model.Position);
                }
            }
            return result;
        }

        /// <summary>Brings a destroyed or Shaken unit back at full strength, ready to be placed again.</summary>
        public static void Revive(UnitData unit)
        {
            foreach (DataBinding<ModelData> binding in unit.ModelBindings)
            {
                ModelData model = binding.GetValue();
                model.SetMaxWounds((int)MathF.Round(model.TotalWounds));
            }

            // #414: the Firefight wound marker used to be swept here too; 3.6.0 deleted that system.
            foreach (TokenType type in new[] { TokenType.Shaken, TokenType.Fatigued })
            {
                int count = unit.Tokens.GetTokenCount(type);
                if (count > 0) unit.Tokens.RemoveTokens(type, count);
            }
        }
    }
}
