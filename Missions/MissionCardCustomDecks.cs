using FDG.Players;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Missions
{
    /// <summary>
    /// The six custom-deck games (Mission Cards v3.6.0, "Custom Deck Rules" and the six game types; the same
    /// text in GF, GFF, AoF and AoFS):
    /// <list type="bullet">
    ///   <item><i>"After deployment, each player must put together a deck of 18 unique cards, chosen from the
    ///   standard 36 cards."</i> - asked at the start of round 1, which is after deployment.</item>
    ///   <item><i>"At the beginning of each round, players draw cards from their own deck until they have 5
    ///   cards in their hand, and then they must place cards face up on the table until they have 3 cards in
    ///   front of them. At the beginning of the first round, players may discard any of their cards and draw
    ///   again once."</i></item>
    ///   <item><i>"When players are placing cards on the table, they may place one of them face down, if they
    ///   don't already have a card that is face down on the table."</i></item>
    ///   <item>Scoring and discarding as Battle of Wits, from the cards on the table.</item>
    /// </list>
    /// Bots build the suggested deck, keep their cards face up and take no optional picks (Kai, 2026-11-05:
    /// bots score but don't chase cards).
    /// </summary>
    public sealed partial class MissionCardRoundHooks
    {
        private const string AllFaceUp = "Place them all face up";
        private const string NoParagon = "No paragon this round";
        private const string KeepOpponent = "Leave their cards alone";

        private string Label(int card) => CardName(card);

        private int FromLabel(string label) => int.Parse(label.Split(' ')[0]);

        private Dictionary<string, string> Descriptions(IEnumerable<int> cards, int prioritySlot) =>
            cards.ToDictionary(Label, c => Describe(c, prioritySlot));

        /// <summary>How another player sees a card on this side's table: its name, or "Hidden card N" when face down.</summary>
        private string SeenBy(MissionCardSide side, int card)
        {
            if (!side.Hidden.Contains(card)) return Label(card);
            return $"Hidden card {side.Hidden.IndexOf(card) + 1}";
        }

        private async Task<List<string>> AskMany(IPlayerSlotInfo seat, string instructions, IReadOnlyList<string> options,
            int min, int max, IReadOnlyList<string> suggested, Dictionary<string, string>? descriptions, string displayName)
        {
            var request = new PickStringsRequest(seat.PlayerID, instructions, options, min, max, suggested,
                descriptions, displayName);
            List<string> picked = await _gameContext.PlayerRequester.RequestDecision<PickStringsRequest, List<string>>(request);
            return request.Sanitise(picked);
        }

        // ── Start of round ───────────────────────────────────────────────────────────────────────

        private async Task CustomRoundStart(MissionCardState state, int round)
        {
            List<IPlayerSlotInfo> seats = MissionCardGame.Seats(_gameContext);
            foreach (MissionCardSide side in state.Sides.Values)
            {
                side.Challenge = 0;
                side.Paragon = 0;
            }

            foreach (IPlayerSlotInfo seat in seats)
            {
                MissionCardSide side = state.Sides[seat.SlotID];
                if (!side.DeckBuilt) await BuildDeck(state, seat, side);

                int limit = HandLimit(state, seat, round);
                Draw(state, side, limit, seat.SlotID);

                // "At the beginning of the first round, players may discard any of their cards and draw again once."
                if (round == 1 && side.Hand.Count > 0)
                {
                    List<string> drop = await AskMany(seat,
                        "Round 1: you may discard any of the cards in your hand and draw again once.",
                        side.Hand.Select(Label).ToList(), 0, side.Hand.Count, Array.Empty<string>(),
                        Descriptions(side.Hand, seat.SlotID), "Discarding mission cards to redraw");
                    if (drop.Count > 0)
                    {
                        var dropped = drop.Select(FromLabel).ToList();
                        side.Hand = side.Hand.Where(c => !dropped.Contains(c)).ToList();
                        side.Discard.AddRange(dropped);
                        Draw(state, side, limit, seat.SlotID);
                        _gameContext.Log($"{seat.Name} discards {dropped.Count} card(s) and draws again.");
                    }
                }

                await PlaceCards(state, seat, side);
            }

            if (state.Game == EMissionCardGame.PlannedGambit) await PlannedGambit(state, seats);
            if (state.Game == EMissionCardGame.DirectChallenge) await DirectChallenge(state, seats);
            if (state.Game == EMissionCardGame.StrategicRuse && round > 1) await StrategicRuse(state, seats);
            if (state.Game == EMissionCardGame.TacticalParagons) await TacticalParagons(state, seats);

            foreach (IPlayerSlotInfo seat in seats)
            {
                MissionCardSide side = state.Sides[seat.SlotID];
                _gameContext.Log($"Mission cards ({seat.Name}): " +
                    string.Join(", ", side.Table.Select(c => SeenBy(side, c))) + $". {side.Hand.Count} in hand.");
            }
        }

        private async Task BuildDeck(MissionCardState state, IPlayerSlotInfo seat, MissionCardSide side)
        {
            List<int> all = MissionCardCatalog.All(System).Select(c => c.Number).ToList();
            List<string> picked = await AskMany(seat,
                $"Build your mission deck: pick {MissionCardCatalog.CustomDeckSize} of the 36 cards. " +
                "Hover a card to read it.",
                all.Select(Label).ToList(), MissionCardCatalog.CustomDeckSize, MissionCardCatalog.CustomDeckSize,
                MissionCardCatalog.SuggestedDeck.Select(Label).ToList(), Descriptions(all, seat.SlotID),
                "Building a mission deck");
            side.Deck = Shuffle(picked.Select(FromLabel).ToList());
            side.DeckBuilt = true;
            _gameContext.Log($"{seat.Name} builds a {side.Deck.Count}-card mission deck.");
        }

        /// <summary>
        /// 5 cards, or 6 in Logistical Supremacy from round 2 for the side that controls more objective
        /// markers than its opponent(s): "if one player controls more objective marker than their opponent,
        /// they may draw cards until they have 6 in their hand instead of 5."
        /// </summary>
        private int HandLimit(MissionCardState state, IPlayerSlotInfo seat, int round)
        {
            if (state.Game != EMissionCardGame.LogisticalSupremacy || round <= 1) return MissionCardCatalog.HandSize;
            var teamOf = MissionCardGame.Seats(_gameContext).ToDictionary(s => s.SlotID, s => s.TeamNumber);
            int Held(int team) => state.OwnersAtRoundStart.Values.Count(o => o >= 0 && teamOf.TryGetValue(o, out int t) && t == team);
            int mine = Held(seat.TeamNumber);
            bool more = teamOf.Values.Distinct().Where(t => t != seat.TeamNumber).All(t => mine > Held(t));
            if (!more) return MissionCardCatalog.HandSize;
            _gameContext.Log($"Logistical Supremacy: {seat.Name} controls more objectives and holds 6 cards.");
            return MissionCardCatalog.SupremacyHandSize;
        }

        private void Draw(MissionCardState state, MissionCardSide side, int handLimit, int prioritySlot)
        {
            var hand = new List<int>(side.Hand);
            while (hand.Count < handLimit)
            {
                if (side.Deck.Count == 0)
                {
                    if (side.Discard.Count == 0) break;
                    side.Deck.AddRange(Shuffle(side.Discard));
                    side.Discard.Clear();
                }
                int card = side.Deck[0];
                side.Deck.RemoveAt(0);
                hand.Add(card);
                OnDrawn(state, card, prioritySlot);
            }
            side.Hand = hand;
        }

        /// <summary>From hand to table until three are in front of the player; one may go face down.</summary>
        private async Task PlaceCards(MissionCardState state, IPlayerSlotInfo seat, MissionCardSide side)
        {
            int need = Math.Min(3 - side.Table.Count, side.Hand.Count);
            if (need <= 0) return;
            List<string> picks = await AskMany(seat,
                $"Place {need} mission card{(need == 1 ? "" : "s")} from your hand on the table.",
                side.Hand.Select(Label).ToList(), need, need, side.Hand.Take(need).Select(Label).ToList(),
                Descriptions(side.Hand, seat.SlotID), "Placing mission cards");
            var placed = picks.Select(FromLabel).ToList();
            side.Hand = side.Hand.Where(c => !placed.Contains(c)).ToList();
            side.Table = side.Table.Concat(placed).ToList();

            // "they may place one of them face down, if they don't already have a card that is face down"
            if (side.Hidden.Count > 0) return;
            var options = new List<string> { AllFaceUp };
            var descriptions = new Dictionary<string, string> { [AllFaceUp] = "Everyone can see all your cards." };
            foreach (int card in placed)
            {
                string label = $"Place {Label(card)} face down";
                options.Add(label);
                descriptions[label] = "Your opponent sees only 'Hidden card' until it is scored or revealed.";
            }
            string picked = await Ask(seat, "You may place one of these cards face down.", options, descriptions,
                "Hiding a mission card");
            int index = options.IndexOf(picked);
            if (index > 0) side.Hidden = new List<int> { placed[index - 1] };
        }

        /// <summary>
        /// Planned Gambit: "each player may select one of their opponent's cards, and return it to their hand.
        /// The opponent must then place another one of their cards on the table to replace it."
        /// </summary>
        private async Task PlannedGambit(MissionCardState state, List<IPlayerSlotInfo> seats)
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                var targets = new List<(IPlayerSlotInfo Opp, int Card)>();
                var options = new List<string> { KeepOpponent };
                foreach (IPlayerSlotInfo opp in seats.Where(o => o.TeamNumber != seat.TeamNumber))
                {
                    MissionCardSide oppSide = state.Sides[opp.SlotID];
                    foreach (int onTable in oppSide.Table)
                    {
                        targets.Add((opp, onTable));
                        options.Add($"Send back {opp.Name}'s {SeenBy(oppSide, onTable)}");
                    }
                }
                if (targets.Count == 0) continue;
                string picked = await Ask(seat, "Planned Gambit: you may send one of your opponent's cards back to their hand.",
                    options, new Dictionary<string, string>(), "Planned Gambit");
                int index = options.IndexOf(picked);
                if (index <= 0) continue;

                (IPlayerSlotInfo target, int sent) = targets[index - 1];
                MissionCardSide side = state.Sides[target.SlotID];
                side.Table = side.Table.Where(c => c != sent).ToList();
                side.Hidden = side.Hidden.Where(c => c != sent).ToList();
                side.Hand = side.Hand.Append(sent).ToList();
                _gameContext.Log($"Planned Gambit: {seat.Name} sends {target.Name}'s {Label(sent)} back to their hand.");

                var replacements = side.Hand.Where(c => c != sent).ToList();
                if (replacements.Count == 0) continue;
                List<string> chosen = await AskMany(target,
                    $"{seat.Name} sent {Label(sent)} back to your hand. Place another card in its place.",
                    replacements.Select(Label).ToList(), 1, 1, new[] { Label(replacements[0]) },
                    Descriptions(replacements, target.SlotID), "Replacing a mission card");
                int card = FromLabel(chosen[0]);
                side.Hand = side.Hand.Where(c => c != card).ToList();
                side.Table = side.Table.Append(card).ToList();
            }
        }

        /// <summary>
        /// Direct Challenge: "each player must select one of their opponent's cards to be their challenge card
        /// for that round. When a challenge card is scored, the player gets +1 VP for it."
        /// </summary>
        private async Task DirectChallenge(MissionCardState state, List<IPlayerSlotInfo> seats)
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                foreach (IPlayerSlotInfo opp in seats.Where(o => o.TeamNumber != seat.TeamNumber))
                {
                    MissionCardSide oppSide = state.Sides[opp.SlotID];
                    if (oppSide.Table.Count == 0) continue;
                    var options = oppSide.Table.Select(c => $"Challenge {opp.Name}: {SeenBy(oppSide, c)}").ToList();
                    string picked = await Ask(seat,
                        $"Direct Challenge: pick one of {opp.Name}'s cards as their challenge card this round " +
                        "(+1 VP to them if they score it).", options, new Dictionary<string, string>(), "Direct Challenge");
                    int index = Math.Max(0, options.IndexOf(picked));
                    oppSide.Challenge = oppSide.Table[index];
                    _gameContext.Log($"Direct Challenge: {seat.Name} challenges {opp.Name} with {SeenBy(oppSide, oppSide.Challenge)}.");
                }
            }
        }

        /// <summary>
        /// Strategic Ruse: "the player with the highest total score must turn all of their hidden cards face up,
        /// and the player with the lowest total score may turn any of their cards face down. In case of a tie,
        /// each player must turn their cards face up/down until they only have up to one card that is face down
        /// again."
        /// </summary>
        private async Task StrategicRuse(MissionCardState state, List<IPlayerSlotInfo> seats)
        {
            int Vp(IPlayerSlotInfo s) => state.Vp.TryGetValue(s.SlotID, out int v) ? v : 0;
            int high = seats.Max(Vp), low = seats.Min(Vp);
            foreach (IPlayerSlotInfo seat in seats)
            {
                MissionCardSide side = state.Sides[seat.SlotID];
                if (high == low)
                {
                    if (side.Hidden.Count <= 1) continue;
                    var options = side.Hidden.Select(c => $"Keep {Label(c)} face down").Append("Turn them all face up").ToList();
                    string picked = await Ask(seat, "Strategic Ruse (tied): you may keep only one card face down.",
                        options, new Dictionary<string, string>(), "Strategic Ruse");
                    int index = options.IndexOf(picked);
                    side.Hidden = index >= 0 && index < side.Hidden.Count ? new List<int> { side.Hidden[index] } : new List<int>();
                }
                else if (Vp(seat) == high)
                {
                    if (side.Hidden.Count == 0) continue;
                    _gameContext.Log($"Strategic Ruse: {seat.Name} leads and turns {string.Join(", ", side.Hidden.Select(Label))} face up.");
                    side.Hidden = new List<int>();
                }
                else if (Vp(seat) == low)
                {
                    var faceUp = side.Table.Where(c => !side.Hidden.Contains(c)).ToList();
                    if (faceUp.Count == 0) continue;
                    List<string> hide = await AskMany(seat,
                        "Strategic Ruse: you have the lowest score and may turn any of your cards face down.",
                        faceUp.Select(Label).ToList(), 0, faceUp.Count, Array.Empty<string>(),
                        Descriptions(faceUp, seat.SlotID), "Strategic Ruse");
                    if (hide.Count == 0) continue;
                    side.Hidden = side.Hidden.Concat(hide.Select(FromLabel)).ToList();
                    _gameContext.Log($"Strategic Ruse: {seat.Name} turns {hide.Count} card(s) face down.");
                }
            }
        }

        /// <summary>
        /// Tactical Paragons: "each player may select one of their own cards to be their paragon card for that
        /// round. Paragon cards may only be scored by units containing a Hero, and the player gets +1 VP for it."
        /// </summary>
        private async Task TacticalParagons(MissionCardState state, List<IPlayerSlotInfo> seats)
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                MissionCardSide side = state.Sides[seat.SlotID];
                if (side.Table.Count == 0) continue;
                var options = new List<string> { NoParagon };
                var descriptions = new Dictionary<string, string> { [NoParagon] = "Score all your cards normally." };
                foreach (int card in side.Table)
                {
                    string label = $"Paragon: {Label(card)}";
                    options.Add(label);
                    descriptions[label] = "Only units with a Hero can complete it this round; +1 VP if they do. " + Describe(card, seat.SlotID);
                }
                string picked = await Ask(seat, "Tactical Paragons: you may pick one of your cards as this round's paragon.",
                    options, descriptions, "Tactical Paragons");
                int index = options.IndexOf(picked);
                if (index <= 0) continue;
                side.Paragon = side.Table[index - 1];
                _gameContext.Log($"Tactical Paragons: {seat.Name} picks {SeenBy(side, side.Paragon)} as their paragon.");
            }
        }

        // ── End of round ─────────────────────────────────────────────────────────────────────────

        private async Task CustomRoundEnd(MissionCardState state, MissionBoard board, List<IPlayerSlotInfo> seats, int round)
        {
            foreach (IPlayerSlotInfo seat in seats)
            {
                MissionCardSide side = state.Sides[seat.SlotID];
                var best = await BestCard(seat, side.Table, seat.SlotID, board, state, side);
                if (best != null)
                {
                    await Score(state, seat, best.Value.Card, best.Value.Vp, best.Value.Why, round);
                    side.Discard.Add(best.Value.Card);
                    side.Table = side.Table.Where(c => c != best.Value.Card).ToList();
                    side.Hidden = side.Hidden.Where(c => c != best.Value.Card).ToList();
                }
                side.Vp = state.Vp[seat.SlotID];
                if (!MoreRoundsAfter(round)) continue;

                // "After scoring cards, each player may discard one of their remaining cards."
                int? dropped = await AskDiscardOne(seat, side.Table, seat.SlotID,
                    "You may discard one of your remaining mission cards.");
                if (dropped is int d)
                {
                    side.Discard.Add(d);
                    side.Table = side.Table.Where(c => c != d).ToList();
                    side.Hidden = side.Hidden.Where(c => c != d).ToList();
                    _gameContext.Log($"{seat.Name} discards {SeenBy(side, d)}.");
                }

                // Coordinated Recovery: "each player may select up to two cards from their discard pile and
                // shuffle them back into their deck."
                if (state.Game == EMissionCardGame.CoordinatedRecovery && side.Discard.Count > 0)
                {
                    List<string> back = await AskMany(seat,
                        "Coordinated Recovery: you may shuffle up to two of your discarded cards back into your deck.",
                        side.Discard.Distinct().Select(Label).ToList(), 0, 2, Array.Empty<string>(),
                        Descriptions(side.Discard.Distinct(), seat.SlotID), "Coordinated Recovery");
                    if (back.Count > 0)
                    {
                        var cards = back.Select(FromLabel).ToList();
                        side.Discard = side.Discard.Where(c => !cards.Contains(c)).ToList();
                        side.Deck = Shuffle(side.Deck.Concat(cards).ToList());
                        _gameContext.Log($"Coordinated Recovery: {seat.Name} shuffles {back.Count} card(s) back into their deck.");
                    }
                }
            }
        }
    }
}
