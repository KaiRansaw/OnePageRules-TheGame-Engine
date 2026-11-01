using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// #432 - the campaign random event table, happening on the table.
    ///
    /// <para>
    /// The books: <i>at the beginning of each round, every player rolls one die, and on a 5+ a random
    /// event happens</i>, rolled on a 36-entry table read as two digits. The table has been data in
    /// <see cref="CampaignEvents"/> since the campaign engine landed, and nothing rolled it. This is the
    /// half that makes it happen.
    /// </para>
    ///
    /// <para>
    /// <b>Only in a campaign game</b>, and the test for that is a unit with a campaign sheet - the same
    /// guard the Hexer counter uses. An ordinary game pays one scan of the table per round.
    /// </para>
    ///
    /// <para>
    /// <b>What is applied and what is not.</b> 29 of the 36 entries resolve fully here. Seven do not yet,
    /// and each says so in the log rather than firing silently - an event that announces itself and then
    /// does nothing is worse than one that admits it:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Explosive Trap (12) and Solar Flare (32)</b> deal hits, which have to run the
    ///   save-and-wound child pipeline. That pipeline only exists inside an attack stage
    ///   (<c>BeforeAttackActionStage</c> seeds it); a round-start stage has no way to drive it.</item>
    ///   <item><b>Thick Clouds (43)</b> halves weapon ranges. <c>Effect.RangeModifier</c> adds and
    ///   subtracts inches; there is no multiplier, and inventing "-12 inches" instead would be a
    ///   different rule.</item>
    ///   <item><b>Psychic Pool (36), Shifting Illusions (45), Support Robots (46) and Psychic Fog
    ///   (44)</b> are conditional on POSITION - "whilst any unit is within 3 inches of the marker" /
    ///   "fully within the quarter". A grant handed out at round start would be wrong both ways: a unit
    ///   that walks in later would not get it, and one that leaves would keep it. They need re-evaluating
    ///   as each unit activates, which is its own slice.</item>
    /// </list>
    /// </summary>
    public static class CampaignEventRunner
    {
        /// <summary>How far "within 3 inches of one random objective marker" reaches.</summary>
        public const float NearObjectiveInches = 3f;

        /// <summary>
        /// Whether this is a campaign game: any unit on the table carrying a campaign sheet. Cheap, and
        /// the same question <c>CastSpellStage</c> asks before counting a spell for Hexer.
        /// </summary>
        public static bool IsCampaignGame(ITableState table) =>
            table.Units.Objects.OfType<UnitData>()
                .Any(u => StrikeUnit.FromJson(u.StrikeUnitJson) != null);

        /// <summary>
        /// The whole round-start step: every player rolls, and each 5+ fires one event. Returns the log
        /// lines, which the caller announces - so a test can read what happened without a presentation
        /// layer.
        /// </summary>
        public static async Task<List<string>> RollForRound(IGameContext gameContext)
        {
            var log = new List<string>();
            ITableState table = gameContext.TableState;
            if (!IsCampaignGame(table)) return log;

            int Die() => gameContext.DiceRoller.RollDecisiveFace();

            // Every player rolls, in a stable order - the roster's, not the dictionary's - so a seeded
            // game replays identically.
            foreach (PlayerID player in CampaignEventTargeting.PlayersInPlay(table)
                         .OrderBy(p => p.ID.ToString(), StringComparer.Ordinal))
            {
                if (!CampaignEventTargeting.ShouldFire(Die)) continue;

                CampaignEvent fired = CampaignEvents.Roll(Die);
                log.Add($"Campaign event {fired.Code}: {fired.Name}. {fired.Text}");
                log.Add(await Apply(gameContext, fired, Die));
            }

            return log;
        }

        /// <summary>
        /// Resolves one event. Every branch returns a line saying what it did to whom, including when it
        /// did nothing because there was no legal target - which is a real outcome, not a failure.
        /// </summary>
        public static async Task<string> Apply(IGameContext gameContext, CampaignEvent fired, Func<int> d6)
        {
            ITableState table = gameContext.TableState;

            switch (fired.Kind)
            {
                case ECampaignEventKind.Wounds:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6);
                    if (target == null) return "No unit could be hit, so nothing happened.";
                    int wounds = CampaignEventTargeting.RollAmount(fired, d6);
                    await CasualtyPresentation.ApplyUnitWounds(gameContext, target, wounds);
                    return $"{target.Name} takes {wounds} wound(s).";
                }

                case ECampaignEventKind.DangerousTerrainTest:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6);
                    if (target == null) return "No unit could be tested, so nothing happened.";
                    if (MovementRuleQueries.IgnoresAllTerrain(target, gameContext.RuleEvaluator))
                        return $"{target.Name} ignores all terrain, so it takes no test.";
                    MovementExecutor.DangerousTerrainResult rolled =
                        MovementExecutor.RollForcedDangerousTerrain(gameContext, target);
                    await MovementExecutor.ResolveDangerousTerrain(gameContext, rolled);
                    return $"{target.Name} took a Dangerous Terrain Test.";
                }

                case ECampaignEventKind.BecomeShaken:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6,
                        u => !u.Tokens.HasToken(TokenType.Shaken));
                    if (target == null) return "Every unit was already Shaken, so nothing happened.";
                    target.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
                    return $"{target.Name} is Shaken.";
                }

                case ECampaignEventKind.StopBeingShaken:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6,
                        u => u.Tokens.HasToken(TokenType.Shaken));
                    if (target == null) return "No unit was Shaken, so nothing happened.";
                    target.Tokens.RemoveTokens(TokenType.Shaken);
                    return $"{target.Name} is no longer Shaken.";
                }

                case ECampaignEventKind.RollModifier when fired.Scope == ECampaignEventScope.RandomUnit:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6);
                    if (target == null) return "No unit could be affected, so nothing happened.";
                    if (!GrantRollModifier(target, fired))
                        return $"A {RollName(fired.Roll)} modifier of {Signed(fired.Amount)} has no "
                               + "matching rule, so nothing happened.";
                    return $"{target.Name} gets {Signed(fired.Amount)} to {RollName(fired.Roll)} rolls "
                           + "until the end of the round.";
                }

                case ECampaignEventKind.MovementChange when fired.Scope == ECampaignEventScope.RandomUnit:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6);
                    if (target == null) return "No unit could be affected, so nothing happened.";
                    string rule = fired.Amount >= 0
                        ? CampaignEventRuleCatalog.HastenedName
                        : CampaignEventRuleCatalog.SlowedName;
                    GrantRuleForTheRound(target, rule);
                    return $"{target.Name} moves {Signed(fired.Amount)}\" on Advance and "
                           + $"{Signed(fired.Amount * 2)}\" on Rush/Charge until the end of the round.";
                }

                case ECampaignEventKind.DangerousArea:
                case ECampaignEventKind.DifficultArea:
                    return PlaceTemporaryTerrain(gameContext, fired, d6);

                case ECampaignEventKind.ObjectiveNeutral:
                {
                    IObjective? marker = CampaignEventTargeting.PickOne(
                        CampaignEventTargeting.Objectives(table, CampaignEventTargeting.IsCaptured), d6);
                    if (marker == null) return "No objective marker was held, so nothing happened.";
                    marker.SetOwner(null);
                    return "A held objective marker is neutral again.";
                }

                case ECampaignEventKind.ObjectiveCapturedRandom:
                {
                    IObjective? marker = CampaignEventTargeting.PickOne(
                        CampaignEventTargeting.Objectives(table, o => !CampaignEventTargeting.IsCaptured(o)), d6);
                    if (marker == null) return "Every objective marker was already held, so nothing happened.";
                    List<PlayerID> players = CampaignEventTargeting.PlayersInPlay(table);
                    if (players.Count == 0) return "Nobody was left to take the marker.";
                    PlayerID taker = players[CampaignEventTargeting.Index(players.Count, d6)];
                    marker.SetOwner(taker);
                    return $"{NameOf(table, taker)} captures a loose objective marker.";
                }

                case ECampaignEventKind.ObjectiveCapturedOpponent:
                {
                    IObjective? marker = CampaignEventTargeting.PickOne(
                        CampaignEventTargeting.Objectives(table, CampaignEventTargeting.IsCaptured), d6);
                    if (marker == null) return "No objective marker was held, so nothing happened.";
                    PlayerID holder = marker.OwnerID!.Value;
                    List<PlayerID> others = CampaignEventTargeting.PlayersInPlay(table)
                        .Where(p => p != holder).ToList();
                    if (others.Count == 0) return "There was no opponent to hand the marker to.";
                    PlayerID taker = others[CampaignEventTargeting.Index(others.Count, d6)];
                    marker.SetOwner(taker);
                    return $"A held objective marker changes hands to {NameOf(table, taker)}.";
                }

                case ECampaignEventKind.GainXp:
                {
                    IUnit? target = PickScopedUnit(table, fired.Scope, d6, HasSheet);
                    if (target == null) return "No campaign unit could gain XP, so nothing happened.";
                    int xp = CampaignEventTargeting.RollAmount(fired, d6);
                    return EditSheet(target, sheet => sheet.EventXp += xp)
                        ? $"{target.Name} gains {xp} XP."
                        : "No campaign unit could gain XP, so nothing happened.";
                }

                case ECampaignEventKind.GainPoints:
                case ECampaignEventKind.GainVp:
                {
                    // Treasury and VP belong to the ARMY, which is not on the table - so the reward rides
                    // one of its units' sheets and the after-game write-back folds it into the campaign
                    // file, the same way a spell count or a first-blood stamp does.
                    IUnit? bearer = PickScopedUnit(table, fired.Scope, d6, HasSheet);
                    if (bearer == null) return "No campaign army could be rewarded, so nothing happened.";
                    int amount = CampaignEventTargeting.RollAmount(fired, d6);
                    bool points = fired.Kind == ECampaignEventKind.GainPoints;
                    bool ok = EditSheet(bearer, sheet =>
                    {
                        if (points) sheet.EventPoints += amount;
                        else sheet.EventVp += amount;
                    });
                    if (!ok) return "No campaign army could be rewarded, so nothing happened.";
                    return points
                        ? $"{bearer.Name}'s army gains {amount} pts into its treasury."
                        : $"{bearer.Name}'s army gains {amount} VP.";
                }

                case ECampaignEventKind.Hits:
                    return "Hits from a random event are not applied yet: they need the save-and-wound "
                           + "pipeline, which only exists inside an attack. Nothing happened.";

                case ECampaignEventKind.HalveRanges:
                    return "Halved weapon ranges are not applied yet: the engine can add and subtract "
                           + "inches but cannot halve a range. Nothing happened.";

                case ECampaignEventKind.RollModifier:
                case ECampaignEventKind.MovementChange:
                    return "This event only applies while a unit is inside an area, which needs checking "
                           + "as each unit activates. Not applied yet, so nothing happened.";

                default:
                    return "That event is not implemented, so nothing happened.";
            }
        }

        // -- Targets ---------------------------------------------------------------------------------

        /// <summary>
        /// The unit an event's scope points at. The two unit scopes differ only in their filter: plain
        /// "one random unit", or "one random unit that is within 3 inches of any objective marker".
        /// </summary>
        private static IUnit? PickScopedUnit(ITableState table, ECampaignEventScope scope, Func<int> d6,
            Func<IUnit, bool>? extra = null)
        {
            Func<IUnit, bool>? filter = extra;
            if (scope == ECampaignEventScope.RandomUnitNearAnyObjective)
            {
                var markers = CampaignEventTargeting.Objectives(table);
                filter = u => (extra == null || extra(u))
                              && markers.Any(m => u.Models.Any(mo => mo.GetIsAlive()
                                  && Position.GetDistance2D(mo.Position, m.Position) <= NearObjectiveInches));
            }

            return CampaignEventTargeting.PickOne(CampaignEventTargeting.EventableUnits(table, filter), d6);
        }

        // -- Effects ---------------------------------------------------------------------------------

        /// <summary>
        /// "-1 to Defense rolls until the end of the round" and its siblings, for the round.
        ///
        /// <para>
        /// Two different mechanisms, because the engine's hit roll and its other rolls are not the same
        /// shape. <b>Defense and Morale</b> are a roll-modifier token: the save and morale stages already
        /// fold granted modifier tokens into their own math (<see cref="GrantedRollModifiers"/>), and
        /// <c>TokenClearService</c> sweeps a round token at round end.
        /// </para>
        ///
        /// <para>
        /// <b>Hit rolls are not</b>, and this is the part worth reading. A hit-roll modifier token is a
        /// single <see cref="ERollKind.Hit"/> value with nothing in it that says melee or shooting - so
        /// "-1 to hit rolls in melee" granted that way would also apply to the unit's shooting, which is
        /// a different (and strictly worse) rule than the one the event printed. The catalog already has
        /// the four melee- and shooting-gated definitions, each a hook with an <c>IsMelee</c> condition
        /// the hit stage evaluates per attack, so the event grants one of those by name instead. All four
        /// of the table's hit events are exactly +1 or -1, which is exactly what those rules are, so
        /// nothing is lost in the translation; an amount the catalog cannot express returns false and the
        /// caller says so rather than quietly applying the wrong one.
        /// </para>
        /// </summary>
        private static bool GrantRollModifier(IUnit unit, CampaignEvent fired)
        {
            if (fired.Roll is ECampaignEventRoll.HitMelee or ECampaignEventRoll.HitShooting)
            {
                bool melee = fired.Roll == ECampaignEventRoll.HitMelee;
                SpecialRuleDefinition? rule = (melee, fired.Amount) switch
                {
                    (true, +1) => CoreRuleCatalog.HitBonusInMelee,
                    (true, -1) => CoreRuleCatalog.HitPenaltyInMelee,
                    (false, +1) => CoreRuleCatalog.HitBonusWhenShooting,
                    (false, -1) => CoreRuleCatalog.HitPenaltyWhenShooting,
                    _ => null,
                };
                if (rule == null) return false;
                GrantRuleForTheRound(unit, rule.Name);
                return true;
            }

            ERollKind roll = fired.Roll switch
            {
                ECampaignEventRoll.Defense => ERollKind.Save,
                ECampaignEventRoll.Morale => ERollKind.Morale,
                _ => ERollKind.Hit,
            };

            unit.Tokens.AddToken(new Token(RollModifierTokens.TypeFor(roll), 1,
                new TokenClearTrigger.RoundEnd(),
                Payload: new TokenPayload.StatModifier(fired.Amount, $"Campaign event: {fired.Name}")));
            return true;
        }

        /// <summary>Grants a catalog rule for the rest of the round, as a RuleGrant token.</summary>
        private static void GrantRuleForTheRound(IUnit unit, string ruleName) =>
            unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.RoundEnd(),
                Payload: new TokenPayload.RuleGrant(ruleName, ELifetime.ThisRound)));

        /// <summary>
        /// The area events, as real terrain placed for the round: a 3-inch circle around a marker, or a
        /// whole table quarter, typed Dangerous or Difficult.
        /// <para>
        /// Terrain rather than a per-unit grant because that is what the book describes - "the area
        /// counts as Dangerous terrain" - and because the engine's movement already asks the terrain
        /// where it is. A unit that walks in later is affected, and one that leaves is not, with nothing
        /// re-evaluating anything. The piece is cleared at the end of the round by
        /// <see cref="ClearTemporaryTerrain"/>.
        /// </para>
        /// </summary>
        private static string PlaceTemporaryTerrain(IGameContext gameContext, CampaignEvent fired, Func<int> d6)
        {
            ITableState table = gameContext.TableState;
            ETerrainType type = fired.Kind == ECampaignEventKind.DangerousArea
                ? ETerrainType.Dangerous
                : ETerrainType.Difficult;
            string typeName = fired.Kind == ECampaignEventKind.DangerousArea ? "Dangerous" : "Difficult";

            IZone shape;
            string where;
            switch (fired.Scope)
            {
                case ECampaignEventScope.NearRandomObjective:
                {
                    IObjective? marker = CampaignEventTargeting.PickOne(
                        CampaignEventTargeting.Objectives(table), d6);
                    if (marker == null) return "There were no objective markers, so nothing happened.";
                    shape = new CircularZone(new Float2(marker.Position.x, marker.Position.z),
                        NearObjectiveInches);
                    where = "around an objective marker";
                    break;
                }

                case ECampaignEventScope.RandomQuarter:
                {
                    (float width, float height) = TableSize(table);
                    TableQuarter quarter = CampaignEventTargeting.PickQuarter(width, height, d6);
                    shape = new RectangularZone(quarter.Area.Left, quarter.Area.Right,
                        quarter.Area.Bottom, quarter.Area.Top);
                    where = $"over table quarter {quarter.Index + 1}";
                    break;
                }

                default:
                {
                    // "One random unit counts as being in Difficult terrain" (16): a piece just big
                    // enough to cover where it stands.
                    IUnit? target = PickScopedUnit(table, ECampaignEventScope.RandomUnit, d6);
                    if (target == null) return "No unit could be affected, so nothing happened.";
                    IModel? any = target.Models.FirstOrDefault(m => m.GetIsAlive());
                    if (any == null) return "No unit could be affected, so nothing happened.";
                    shape = new CircularZone(new Float2(any.Position.x, any.Position.z),
                        UnitRadius(target) + 1f);
                    where = $"where {target.Name} stands";
                    break;
                }
            }

            gameContext.GameDataStore.Create(new TerrainData(type, shape, heightInches: 0f,
                name: TemporaryTerrainName(fired)));
            return $"The ground {where} counts as {typeName} terrain until the end of the round.";
        }

        /// <summary>
        /// Removes the terrain an event placed. Called at the end of the round, because the pieces are
        /// real terrain and nothing else would ever take them away - an event that said "until the end of
        /// the round" and then left a minefield on the table for the rest of the game would be worse than
        /// one that did nothing.
        /// </summary>
        public static int ClearTemporaryTerrain(IGameContext gameContext)
        {
            var doomed = gameContext.GameDataStore.GetAllDataReferences<TerrainData>()
                .Where(reference => gameContext.GameDataStore.IsValid(reference, out _)
                                    && IsTemporary(gameContext.GameDataStore.GetValue<TerrainData>(reference)))
                .ToList();

            foreach (DataReference reference in doomed) gameContext.GameDataStore.Destroy(reference);
            return doomed.Count;
        }

        /// <summary>The marker that says a terrain piece belongs to a campaign event and is this round's.</summary>
        private const string TemporaryPrefix = "Campaign event: ";

        private static string TemporaryTerrainName(CampaignEvent fired) => TemporaryPrefix + fired.Name;

        private static bool IsTemporary(TerrainData terrain) =>
            terrain.Name != null && terrain.Name.StartsWith(TemporaryPrefix, StringComparison.Ordinal);

        // -- Sheets ----------------------------------------------------------------------------------

        private static bool HasSheet(IUnit unit) =>
            unit is UnitData data && StrikeUnit.FromJson(data.StrikeUnitJson) != null;

        /// <summary>Reads, edits and writes back a unit's campaign sheet. False if it has none.</summary>
        private static bool EditSheet(IUnit unit, Action<StrikeUnit> edit)
        {
            if (unit is not UnitData data) return false;
            if (StrikeUnit.FromJson(data.StrikeUnitJson) is not { } sheet) return false;
            edit(sheet);
            data.StrikeUnitJson = sheet.ToJson();
            return true;
        }

        // -- Shared ----------------------------------------------------------------------------------

        private static (float Width, float Height) TableSize(ITableState table) =>
            GameProgressUtilities.TryGetProgress(table.DataStore)?.Settings is { } settings
                ? GameWideConstants.TableSizeFor(settings.GameSystem)
                : (GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

        private static float UnitRadius(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => m.BaseShape.BoundingRadiusInches)
                .DefaultIfEmpty(0.5f)
                .Max();

        private static string NameOf(ITableState table, PlayerID player) =>
            table.Players.Objects.FirstOrDefault(p => p.PlayerID == player)?.Name ?? "A player";

        private static string Signed(int value) => value >= 0 ? $"+{value}" : value.ToString();

        private static string RollName(ECampaignEventRoll roll) => roll switch
        {
            ECampaignEventRoll.Defense => "Defense",
            ECampaignEventRoll.Morale => "Morale Test",
            ECampaignEventRoll.HitMelee => "melee hit",
            ECampaignEventRoll.HitShooting => "shooting hit",
            _ => "hit",
        };
    }
}
