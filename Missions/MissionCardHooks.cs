using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;

namespace FDG.Missions
{
    /// <summary>How a unit died, for the cards that care.</summary>
    public enum EKillCause { Other, Shooting, Melee, Spell }

    /// <summary>
    /// Whether this game is played with the Mission Cards, and the small helpers both the round hooks and the
    /// event recorders need.
    /// </summary>
    public static class MissionCardGame
    {
        /// <summary>The Mission Cards apply: picked in the lobby, a standard game, a system whose books have them.</summary>
        public static bool IsOn(GameSettings settings) =>
            settings.MissionCardGame != EMissionCardGame.Off
            && settings.GameMode == EGameMode.Standard
            && MissionCardCatalog.Available(settings.GameSystem);

        public static bool IsOn(IGameContext gameContext) => IsOn(gameContext.Settings);

        /// <summary>Mission Cards: "Place 6 objective markers."</summary>
        public const int ObjectiveCount = 6;

        internal static List<IPlayerSlotInfo> Seats(IGameContext gameContext) =>
            gameContext.TableState.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();

        internal static int SlotOf(IGameContext gameContext, PlayerID player)
        {
            foreach (IPlayerSlotInfo seat in gameContext.TableState.Players.Objects)
                if (seat.IsFilled && seat.PlayerID.Equals(player)) return seat.SlotID;
            return -1;
        }

        internal static int SlotOf(IGameContext gameContext, IUnit? unit) =>
            unit is UnitData data ? SlotOf(gameContext, data.PlayerID) : -1;

        internal static bool HasRule(IReadOnlyList<ResolvedRule> rules, string name) =>
            rules.Any(r => string.Equals(r.Definition.Name, name, StringComparison.Ordinal)
                           || string.Equals(r.RequestedName, name, StringComparison.Ordinal));

        internal static bool IsHeroRules(IReadOnlyList<ResolvedRule> rules) =>
            rules.Any(r => CoreRuleCatalog.IsHeroRuleName(r.Definition.Name) || CoreRuleCatalog.IsHeroRuleName(r.RequestedName));

        /// <summary>
        /// Whether this model has the Hero rule: a joined hero (whose Hero marker stays behind on the merge),
        /// or any model of a unit that is itself a Hero.
        /// </summary>
        internal static bool IsHeroModel(IUnit unit, IModel model) =>
            (unit is UnitData data && data.JoinedHeroModelId is ModelID joined && joined.Equals(model.ID))
            || IsHeroRules(unit.RuleDefinitions) || IsHeroRules(model.RuleDefinitions);

        /// <summary>
        /// Whether this model has the Caster rule: on its own rules (a joined hero), or the unit's when the
        /// rule is the unit's - then its first model, which is the unit's leader, carries it.
        /// </summary>
        internal static bool IsCasterModel(IUnit unit, IModel model) =>
            HasRule(model.RuleDefinitions, "Caster")
            || (HasRule(unit.RuleDefinitions, "Caster") && unit.Models.Count > 0 && ReferenceEquals(unit.Models[0], model));

        internal static int HeroCost(IUnit unit, IModel model) =>
            unit is UnitData data
                ? data.JoinedHeroModelId is ModelID joined && joined.Equals(model.ID) && data.HeroAttachment != null
                    ? data.HeroAttachment.PointCost
                    : data.PointCost
                : 0;
    }

    /// <summary>
    /// The events the cards ask about, recorded from the stages where they happen. Each is a no-op in a game
    /// without the Mission Cards. Every machine runs these stages in lockstep, so every machine records the
    /// same thing.
    /// </summary>
    public static class MissionCardEvents
    {
        private static MissionCardState? StateOf(IGameContext gameContext) =>
            MissionCardGame.IsOn(gameContext) ? GameProgressUtilities.MissionCards(gameContext.GameDataStore) : null;

        public static void UnitDestroyed(IGameContext gameContext, IUnit dead, IUnit? killer, EKillCause cause)
        {
            if (StateOf(gameContext) is not { } state) return;
            state.Kills.Add(new MissionKill
            {
                Round = state.Round,
                VictimSlot = MissionCardGame.SlotOf(gameContext, dead),
                KillerSlot = MissionCardGame.SlotOf(gameContext, killer),
                Cause = cause switch
                {
                    EKillCause.Shooting => "shooting",
                    EKillCause.Melee => "melee",
                    EKillCause.Spell => "spell",
                    _ => "other",
                },
                Flying = MissionCardGame.HasRule(dead.RuleDefinitions, "Flying")
                         || dead.Models.Any(m => MissionCardGame.HasRule(m.RuleDefinitions, "Flying")),
            });
        }

        public static void MoraleFailed(IGameContext gameContext, IUnit unit)
        {
            if (StateOf(gameContext) is not { } state) return;
            state.MoraleFails.Add(new[] { state.Round, MissionCardGame.SlotOf(gameContext, unit) });
        }

        public static void SpellCast(IGameContext gameContext, IUnit caster)
        {
            if (StateOf(gameContext) is not { } state) return;
            state.SpellsCast.Add(new[] { state.Round, MissionCardGame.SlotOf(gameContext, caster) });
        }

        public static void MeleeWon(IGameContext gameContext, IUnit winner, float winnerWounds, float loserWounds)
        {
            if (StateOf(gameContext) is not { } state) return;
            int margin = (int)MathF.Floor(winnerWounds - loserWounds + 0.001f);
            state.MeleeWins.Add(new[] { state.Round, MissionCardGame.SlotOf(gameContext, winner), margin });
        }
    }

    /// <summary>
    /// The Mission Cards round by round (Mission Cards v3.6.0, Battle of Wits and Total Domination): deal at
    /// the start of each round (and the one free redraw in round 1), score and discard at the end.
    /// Bots score their cards like anyone, but do not play towards them (Kai, 2026-11-05) - their only
    /// decisions here are to keep what they drew.
    /// </summary>
    public sealed class MissionCardRoundHooks : IRoundHooks
    {
        private static readonly TextColor CardColor = new TextColor(140, 210, 255, 255);
        private const string KeepAll = "Keep these cards";
        private const string KeepCards = "Keep all cards";

        private readonly IGameContext _gameContext;

        public MissionCardRoundHooks(IGameContext gameContext) => _gameContext = gameContext;

        private EGameSystem System => _gameContext.Settings.GameSystem;
        private IReadWriteableGameDataStore Store => _gameContext.GameDataStore;

        private string CardName(int number) => $"{number} {MissionCardCatalog.Card(System, number).Title}";

        private string SeatName(int slot) =>
            MissionCardGame.Seats(_gameContext).FirstOrDefault(s => s.SlotID == slot)?.Name ?? $"Player {slot + 1}";

        // ── Start of round ───────────────────────────────────────────────────────────────────────

        public async Task OnRoundStart(IMainPhaseContext context)
        {
            int round = context.RoundCount;
            MissionCardState state = GameProgressUtilities.MissionCards(Store) ?? Deal();
            state.Round = round;

            // What the end-of-round check compares against: who held what, and who was alive.
            state.OwnersAtRoundStart = OwnersNow();
            state.AliveAtRoundStart = AliveModels();

            if (state.Game == EMissionCardGame.BattleOfWits)
            {
                foreach (IPlayerSlotInfo seat in MissionCardGame.Seats(_gameContext))
                {
                    MissionCardSide side = state.Sides[seat.SlotID];
                    side.Table = DrawUpTo(state, side.Deck, side.Discard, side.Table, seat.SlotID);
                    if (round == 1) await FirstRoundRedrawAll(state, seat, side);
                    LogTable(seat.Name, side.Table);
                }
            }
            else
            {
                state.SharedTable = DrawUpTo(state, state.SharedDeck, state.SharedDiscard, state.SharedTable, -1);
                if (round == 1)
                {
                    foreach (IPlayerSlotInfo seat in MissionCardGame.Seats(_gameContext))
                        await FirstRoundRedrawOne(state, seat);
                }
                LogTable("Shared", state.SharedTable);
            }

            await _gameContext.Announce($"Round {round} mission cards dealt", CardColor);
        }

        /// <summary>The start of the game: decks shuffled, the heroes Decapitate will ask about fixed.</summary>
        private MissionCardState Deal()
        {
            var state = new MissionCardState { Game = _gameContext.Settings.MissionCardGame };
            List<int> all = MissionCardCatalog.All(System).Select(c => c.Number).ToList();
            foreach (IPlayerSlotInfo seat in MissionCardGame.Seats(_gameContext))
            {
                state.Sides[seat.SlotID] = new MissionCardSide
                {
                    Slot = seat.SlotID,
                    Deck = state.Game == EMissionCardGame.BattleOfWits ? Shuffle(all) : new List<int>(),
                };
                state.Vp[seat.SlotID] = 0;
            }
            if (state.Game == EMissionCardGame.TotalDomination) state.SharedDeck = Shuffle(all);

            // Decapitate: "the enemy Hero with the highest cost. In case of a tie in cost any of them counts."
            var heroes = new Dictionary<int, List<(string Id, int Cost)>>();
            foreach (IUnit unit in _gameContext.TableState.Units.Objects)
            {
                int slot = MissionCardGame.SlotOf(_gameContext, unit);
                if (slot < 0) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive() || !MissionCardGame.IsHeroModel(unit, model)) continue;
                    if (!heroes.TryGetValue(slot, out var list)) heroes[slot] = list = new();
                    list.Add((model.ID.ID.ToString(), MissionCardGame.HeroCost(unit, model)));
                }
            }
            foreach ((int slot, var list) in heroes)
            {
                int top = list.Max(h => h.Cost);
                state.TopHeroes[slot] = list.Where(h => h.Cost == top).Select(h => h.Id).ToList();
            }

            GameProgressUtilities.SetMissionCards(Store, state);
            _gameContext.Log($"Mission Cards: {MissionCardCatalog.Name(state.Game)}. Six objectives, four rounds, most victory points wins.");
            return state;
        }

        /// <summary>A fair shuffle off the game's dice, so every machine deals the same cards.</summary>
        private List<int> Shuffle(List<int> cards)
        {
            var deck = new List<int>(cards);
            for (int i = deck.Count - 1; i > 0; i--)
            {
                int j = _gameContext.DiceRoller.RollDecisiveFace(i + 1) - 1;
                (deck[i], deck[j]) = (deck[j], deck[i]);
            }
            return deck;
        }

        /// <summary>
        /// Draws from the top of <paramref name="deck"/> until the table has three, reshuffling the discards in
        /// if the deck runs out (four rounds rarely get there). Returns the new table - a new list, so the
        /// screen never reads one that is being changed.
        /// </summary>
        private List<int> DrawUpTo(MissionCardState state, List<int> deck, List<int> discard, List<int> table, int prioritySlot)
        {
            var next = new List<int>(table);
            while (next.Count < 3)
            {
                if (deck.Count == 0)
                {
                    if (discard.Count == 0) break;
                    deck.AddRange(Shuffle(discard));
                    discard.Clear();
                }
                int card = deck[0];
                deck.RemoveAt(0);
                next.Add(card);
                OnDrawn(state, card, prioritySlot);
            }
            return next;
        }

        /// <summary>Changing Priority: "Roll one D6 when you draw this card."</summary>
        private void OnDrawn(MissionCardState state, int card, int prioritySlot)
        {
            if (card != 44) return;
            int roll = _gameContext.DiceRoller.RollDecisiveFace(6);
            state.Priority[MissionCardState.PriorityKey(prioritySlot, 44)] = roll;
            _gameContext.Log($"  Changing Priority: rolled {roll} - objective {roll}.");
        }

        private void LogTable(string who, List<int> table) =>
            _gameContext.Log($"Mission cards ({who}): {string.Join(", ", table.Select(CardName))}.");

        private string Describe(int card, int prioritySlot)
        {
            MissionCardDef def = MissionCardCatalog.Card(System, card);
            string text = string.Join(" / ", def.Conditions.Select(c => $"{c.Vp} VP: {c.Text}"));
            MissionCardState? state = GameProgressUtilities.MissionCards(Store);
            if (card == 44 && state != null && state.Priority.TryGetValue(MissionCardState.PriorityKey(prioritySlot, 44), out int obj))
                text += $" (rolled: objective {obj})";
            return def.Note == null ? text : $"{text} {def.Note}";
        }

        /// <summary>Battle of Wits, round 1: "players may discard any of their cards and draw again once."</summary>
        private async Task FirstRoundRedrawAll(MissionCardState state, IPlayerSlotInfo seat, MissionCardSide side)
        {
            var toDiscard = new List<int>();
            while (toDiscard.Count < side.Table.Count)
            {
                var keep = side.Table.Where(c => !toDiscard.Contains(c)).ToList();
                var options = new List<string> { toDiscard.Count == 0 ? KeepAll : "Done - draw replacements" };
                var descriptions = new Dictionary<string, string>
                {
                    [options[0]] = toDiscard.Count == 0
                        ? "Play these three."
                        : $"Discard {string.Join(", ", toDiscard.Select(CardName))} and draw again.",
                };
                foreach (int card in keep)
                {
                    string label = $"Discard {CardName(card)}";
                    options.Add(label);
                    descriptions[label] = Describe(card, seat.SlotID);
                }
                string picked = await Ask(seat, "Round 1: you may discard any of your mission cards and draw again once.",
                    options, descriptions, "Choosing mission cards to redraw");
                int index = options.IndexOf(picked);
                if (index <= 0) break;
                toDiscard.Add(keep[index - 1]);
            }
            if (toDiscard.Count == 0) return;

            side.Discard.AddRange(toDiscard);
            side.Table = DrawUpTo(state, side.Deck, side.Discard, side.Table.Where(c => !toDiscard.Contains(c)).ToList(), seat.SlotID);
            _gameContext.Log($"{seat.Name} discards {string.Join(", ", toDiscard.Select(CardName))} and draws again.");
        }

        /// <summary>Total Domination, round 1: "each player may discard one of the cards and draw again once."</summary>
        private async Task FirstRoundRedrawOne(MissionCardState state, IPlayerSlotInfo seat)
        {
            int? picked = await AskDiscardOne(seat, state.SharedTable, -1,
                "Round 1: you may discard one of the shared mission cards and draw again.");
            if (picked == null) return;
            int card = picked.Value;
            state.SharedDiscard.Add(card);
            state.SharedTable = DrawUpTo(state, state.SharedDeck, state.SharedDiscard,
                state.SharedTable.Where(c => c != card).ToList(), -1);
            _gameContext.Log($"{seat.Name} discards {CardName(card)} and draws {CardName(state.SharedTable.Last())}.");
        }

        /// <summary>Offers "keep" (first, so a bot keeps) or discarding one card. Null when kept.</summary>
        private async Task<int?> AskDiscardOne(IPlayerSlotInfo seat, List<int> table, int prioritySlot, string instructions)
        {
            if (table.Count == 0) return null;
            var options = new List<string> { KeepCards };
            var descriptions = new Dictionary<string, string> { [KeepCards] = "Discard nothing." };
            foreach (int card in table)
            {
                string label = $"Discard {CardName(card)}";
                options.Add(label);
                descriptions[label] = Describe(card, prioritySlot);
            }
            string picked = await Ask(seat, instructions, options, descriptions, "Choosing a mission card to discard");
            int index = options.IndexOf(picked);
            return index <= 0 ? null : table[index - 1];
        }

        private async Task<string> Ask(IPlayerSlotInfo seat, string instructions, List<string> options,
            Dictionary<string, string> descriptions, string displayName)
        {
            var request = new StringSelectionRequest(seat.PlayerID, instructions, options,
                Array.Empty<StringSelectionRequest.InvalidOption>(), descriptions, displayName: displayName);
            return await _gameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
        }

        // ── End of round ─────────────────────────────────────────────────────────────────────────

        public async Task<bool> OnRoundEnd(IMainPhaseContext context, int roundJustFinished)
        {
            if (GameProgressUtilities.MissionCards(Store) is not { } state) return false;
            state.Round = roundJustFinished;
            RecordModelDeaths(state, roundJustFinished);
            MissionBoard board = Board(roundJustFinished);
            List<IPlayerSlotInfo> seats = MissionCardGame.Seats(_gameContext);

            if (state.Game == EMissionCardGame.BattleOfWits)
            {
                foreach (IPlayerSlotInfo seat in seats)
                {
                    MissionCardSide side = state.Sides[seat.SlotID];
                    var best = await BestCard(seat, side.Table, seat.SlotID, board, state);
                    if (best != null)
                    {
                        await Score(state, seat, best.Value.Card, best.Value.Vp, best.Value.Why, roundJustFinished);
                        side.Discard.Add(best.Value.Card);
                        side.Table = side.Table.Where(c => c != best.Value.Card).ToList();
                    }
                    side.Vp = state.Vp[seat.SlotID];

                    // "After scoring cards, each player may choose to discard one of their remaining cards."
                    if (roundJustFinished < HordeRulesLimit())
                    {
                        int? dropped = await AskDiscardOne(seat, side.Table, seat.SlotID,
                            "You may discard one of your remaining mission cards.");
                        if (dropped is int d)
                        {
                            side.Discard.Add(d);
                            side.Table = side.Table.Where(c => c != d).ToList();
                            _gameContext.Log($"{seat.Name} discards {CardName(d)}.");
                        }
                    }
                }
            }
            else
            {
                var picks = new Dictionary<int, (int Card, int Vp, string Why)>();
                foreach (IPlayerSlotInfo seat in seats)
                {
                    var best = await BestCard(seat, state.SharedTable, -1, board, state);
                    if (best != null) picks[seat.SlotID] = best.Value;
                }

                // "In case of a tie neither player scores it." - two or more players would score the same card.
                var contested = picks.GroupBy(p => p.Value.Card).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
                foreach (int card in contested)
                {
                    _gameContext.Log($"Mission card {CardName(card)}: completed by more than one player - nobody scores it.");
                    await _gameContext.Announce($"{CardName(card)}: tied - nobody scores", CardColor);
                }

                var scoredCards = new HashSet<int>();
                foreach (IPlayerSlotInfo seat in seats)
                {
                    if (!picks.TryGetValue(seat.SlotID, out var pick) || contested.Contains(pick.Card)) continue;
                    await Score(state, seat, pick.Card, pick.Vp, pick.Why, roundJustFinished);
                    scoredCards.Add(pick.Card);
                }
                state.SharedDiscard.AddRange(scoredCards);
                state.SharedTable = state.SharedTable.Where(c => !scoredCards.Contains(c)).ToList();
                foreach (MissionCardSide side in state.Sides.Values) side.Vp = state.Vp[side.Slot];

                // "At the end of each round, the player with the lowest total score may discard one of the
                // remaining cards." When two or more share the lowest score there is no such player.
                if (roundJustFinished < HordeRulesLimit() && seats.Count > 0)
                {
                    int low = seats.Min(s => state.Vp[s.SlotID]);
                    var lowest = seats.Where(s => state.Vp[s.SlotID] == low).ToList();
                    if (lowest.Count == 1)
                    {
                        int? dropped = await AskDiscardOne(lowest[0], state.SharedTable, -1,
                            "You have the lowest score: you may discard one of the shared mission cards.");
                        if (dropped is int d)
                        {
                            state.SharedDiscard.Add(d);
                            state.SharedTable = state.SharedTable.Where(c => c != d).ToList();
                            _gameContext.Log($"{lowest[0].Name} (lowest score) discards {CardName(d)}.");
                        }
                    }
                }
            }

            _gameContext.Log("Victory points: " + string.Join(", ", seats.Select(s => $"{s.Name} {state.Vp[s.SlotID]}")) + ".");
            return false;
        }

        private int HordeRulesLimit() => FDG.StateMachine.HordeRules.RoundLimit(_gameContext.Settings.GameMode);

        /// <summary>
        /// The completed card worth most VP - "they must score the one worth most victory points". When two or
        /// more are worth the same, the player picks which one scores (Kai, 2026-11-05: holding one back can
        /// be an easy point next round); a bot takes the first.
        /// </summary>
        private async Task<(int Card, int Vp, string Why)?> BestCard(IPlayerSlotInfo seat, List<int> table,
            int prioritySlot, MissionBoard board, MissionCardState state)
        {
            var done = new List<(int Card, int Vp, string Why)>();
            foreach (int card in table)
            {
                (int vp, string? why) = MissionCardRules.Evaluate(MissionCardCatalog.Card(System, card), seat.SlotID,
                    prioritySlot, board, state, System);
                if (vp > 0) done.Add((card, vp, why!));
            }
            if (done.Count == 0) return null;
            int top = done.Max(d => d.Vp);
            var tied = done.Where(d => d.Vp == top).ToList();
            if (tied.Count == 1) return tied[0];

            var options = new List<string>();
            var descriptions = new Dictionary<string, string>();
            foreach (var d in tied)
            {
                string label = $"Score {CardName(d.Card)} ({d.Vp} VP)";
                options.Add(label);
                descriptions[label] = $"Completed: {d.Why} The other card stays on the table.";
            }
            string picked = await Ask(seat,
                $"You completed {tied.Count} mission cards worth {top} VP. Only one scores this round - pick which.",
                options, descriptions, "Choosing which mission card to score");
            int index = options.IndexOf(picked);
            return tied[index >= 0 ? index : 0];
        }

        private async Task Score(MissionCardState state, IPlayerSlotInfo seat, int card, int vp, string why, int round)
        {
            state.Vp[seat.SlotID] = state.Vp.TryGetValue(seat.SlotID, out int had) ? had + vp : vp;
            state.Scored = new List<MissionScore>(state.Scored)
            {
                new MissionScore { Round = round, Slot = seat.SlotID, Card = card, Vp = vp, Text = why },
            };
            _gameContext.Log($"{seat.Name} scores mission card {CardName(card)} for {vp} VP: {why}");
            await _gameContext.Announce($"{seat.Name}: {MissionCardCatalog.Card(System, card).Title} +{vp} VP", CardColor);
        }

        // ── Reading the table ────────────────────────────────────────────────────────────────────

        private Dictionary<int, int> OwnersNow()
        {
            var owners = new Dictionary<int, int>();
            var objectives = _gameContext.TableState.Objectives.Objects.ToList();
            for (int i = 0; i < objectives.Count; i++)
                owners[i + 1] = objectives[i].OwnerID is PlayerID owner ? MissionCardGame.SlotOf(_gameContext, owner) : -1;
            return owners;
        }

        private List<MissionModelSnapshot> AliveModels()
        {
            var alive = new List<MissionModelSnapshot>();
            foreach (IUnit unit in _gameContext.TableState.Units.Objects)
            {
                int slot = MissionCardGame.SlotOf(_gameContext, unit);
                if (slot < 0) continue;
                foreach (IModel model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    alive.Add(new MissionModelSnapshot
                    {
                        ModelId = model.ID.ID.ToString(),
                        Slot = slot,
                        Hero = MissionCardGame.IsHeroModel(unit, model),
                        Caster = MissionCardGame.IsCasterModel(unit, model),
                        Tough = (int)MathF.Round(model.TotalWounds),
                    });
                }
            }
            return alive;
        }

        private void RecordModelDeaths(MissionCardState state, int round)
        {
            var aliveNow = new HashSet<string>();
            foreach (IUnit unit in _gameContext.TableState.Units.Objects)
                foreach (IModel model in unit.Models)
                    if (model.GetIsAlive()) aliveNow.Add(model.ID.ID.ToString());
            foreach (MissionModelSnapshot was in state.AliveAtRoundStart)
            {
                if (aliveNow.Contains(was.ModelId)) continue;
                state.ModelDeaths.Add(new MissionModelDeath
                {
                    Round = round, VictimSlot = was.Slot, ModelId = was.ModelId,
                    Hero = was.Hero, Caster = was.Caster, Tough = was.Tough,
                });
            }
        }

        private MissionBoard Board(int round)
        {
            var teamOf = MissionCardGame.Seats(_gameContext).ToDictionary(s => s.SlotID, s => s.TeamNumber);
            var zones = GameProgressUtilities.DeploymentZones(Store) ?? new Dictionary<int, ZoneRect>();
            var centre = new Position(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES / 2f,
                GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES / 2f);

            var units = new List<BoardUnit>();
            foreach (IUnit unit in _gameContext.TableState.Units.Objects)
            {
                if (!unit.GetIsAlive() || ReserveRules.IsInReserve(unit)) continue;
                int slot = MissionCardGame.SlotOf(_gameContext, unit);
                if (slot < 0) continue;
                var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
                var inZones = zones.Where(z => living.Any(m => z.Value.Contains(m.Position))).Select(z => z.Key).ToList();
                float centreDistance = living.Count == 0
                    ? float.MaxValue
                    : living.Min(m => BaseShapeGeometry.SurfaceDistanceToPoint2D(m.BaseShape, m.Position, m.Facing, centre));
                units.Add(new BoardUnit(slot, inZones, centreDistance));
            }

            return new MissionBoard { Round = round, TeamOf = teamOf, OwnersNow = OwnersNow(), Units = units };
        }

        // ── End of the game ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// "Final Objectives: At the end of the game, players get 1 VP for each objective that they control."
        /// Idempotent. Returns the final victory points by slot, or null when the game has no mission cards.
        /// </summary>
        public static Dictionary<int, int>? ScoreFinalObjectives(IGameContext gameContext)
        {
            if (!MissionCardGame.IsOn(gameContext)) return null;
            if (GameProgressUtilities.MissionCards(gameContext.GameDataStore) is not { } state) return null;
            if (!state.FinalObjectivesScored)
            {
                state.FinalObjectivesScored = true;
                foreach (IObjective objective in gameContext.TableState.Objectives.Objects)
                {
                    if (objective.OwnerID is not PlayerID owner) continue;
                    int slot = MissionCardGame.SlotOf(gameContext, owner);
                    if (slot < 0) continue;
                    state.Vp[slot] = state.Vp.TryGetValue(slot, out int had) ? had + 1 : 1;
                }
                foreach (MissionCardSide side in state.Sides.Values)
                    side.Vp = state.Vp.TryGetValue(side.Slot, out int vp) ? vp : 0;
                gameContext.Log("Final objectives: +1 VP for each objective controlled.");
            }
            return new Dictionary<int, int>(state.Vp);
        }
    }
}
