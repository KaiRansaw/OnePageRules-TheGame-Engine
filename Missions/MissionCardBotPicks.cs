using FDG.Players;
using FDG.Rules.Dispatch;

namespace FDG.Missions
{
    /// <summary>
    /// #462 - the engine's advice on the card choices: what a bot picks (Kai, 2026-11-15: bots play towards
    /// their cards), and what the screen's "Use suggested" offers a person.
    ///
    /// <para>
    /// The whole idea is "don't hold a card you can't score": Purity against an army with no Caster, Bring Them
    /// Down with no Flying enemy, Giant Slayer with nothing that tough, Assassinate / Decapitate with no enemy
    /// Hero, Attunement with no Caster of your own, Fire at Will with no guns, Keep at Bay with under three
    /// units. Those are thrown away when the book allows a discard, left out of a built deck, and placed last.
    /// Everything else is kept. House rule C, "Bots and their cards".
    /// </para>
    /// </summary>
    public sealed partial class MissionCardRoundHooks
    {
        /// <summary>Below this a card is not worth holding.</summary>
        private const float HopelessBelow = 0.25f;

        private List<IUnit> LivingUnitsOf(Func<int, bool> slotMatches) =>
            _gameContext.TableState.Units.Objects
                .Where(u => u.GetIsAlive() && slotMatches(MissionCardGame.SlotOf(_gameContext, u)))
                .ToList();

        private int TeamOfSlot(int slot) =>
            MissionCardGame.Seats(_gameContext).FirstOrDefault(s => s.SlotID == slot)?.TeamNumber ?? int.MinValue;

        /// <summary>How likely <paramref name="slot"/> is to score <paramref name="card"/> at all: 0 = it can't.</summary>
        internal float Feasibility(int card, int slot)
        {
            int team = TeamOfSlot(slot);
            List<IUnit> enemies = LivingUnitsOf(s => s >= 0 && TeamOfSlot(s) != team);
            List<IUnit> ours = LivingUnitsOf(s => s >= 0 && TeamOfSlot(s) == team);
            IEnumerable<(IUnit Unit, IModel Model)> Models(List<IUnit> units) =>
                units.SelectMany(u => u.Models.Where(m => m.GetIsAlive()).Select(m => (u, m)));

            switch (card)
            {
                case 62: return Models(enemies).Any(x => MissionCardGame.IsCasterModel(x.Unit, x.Model)) ? 1f : 0f;
                case 63:
                    return enemies.Any(u => MissionCardGame.IsBringThemDownTarget(u, System)) ? 1f : 0f;
                case 64: return Models(enemies).Any(x => MissionCardGame.IsHeroModel(x.Unit, x.Model)) ? 1f : 0f;
                case 65:
                    int tough = MissionCardCatalog.GiantSlayerTough(System);
                    return Models(enemies).Any(x => MathF.Round(x.Model.TotalWounds) >= tough) ? 1f : 0f;
                case 61:
                {
                    MissionCardState? state = GameProgressUtilities.MissionCards(Store);
                    if (state == null) return 1f;
                    var targets = state.TopHeroes.Where(h => h.Key != slot && TeamOfSlot(h.Key) != team)
                        .SelectMany(h => h.Value).ToHashSet();
                    return targets.Count > 0 ? 1f : 0f;
                }
                case 56: return Models(ours).Any(x => MissionCardGame.IsCasterModel(x.Unit, x.Model)) ? 1f : 0f;
                case 51: return Models(ours).Any(x => x.Model.Weapons.Any(w => w.IsRanged())) ? 1f : 0f;
                case 43: return ours.Count(u => !ReserveRules.IsInReserve(u)) >= 3 ? 1f : 0f;
                case 46: return 0.2f;   // all six objectives at once: rarely, but 5 VP - kept unless something better is there
                case 45: return 0.7f;
                default: return 1f;
            }
        }

        private bool Hopeless(int card, int slot) => Feasibility(card, slot) < HopelessBelow;

        /// <summary>The worst card to throw away, or null when every card is worth keeping.</summary>
        private int? WorstCard(IEnumerable<int> cards, int slot)
        {
            var worst = cards.Select(c => (Card: c, F: Feasibility(c, slot))).OrderBy(x => x.F).FirstOrDefault();
            return worst.Card != 0 && worst.F < HopelessBelow ? worst.Card : null;
        }

        /// <summary>
        /// The suggested 18: the old suggestion (both Seize sets and the cards an army scores by just playing) with
        /// any card this army can't score swapped for the next best - Hold cards first, then the rest in order.
        /// </summary>
        private List<int> SuggestedDeckFor(int slot)
        {
            var preferred = MissionCardCatalog.SuggestedDeck.ToList();
            var fallbacks = new List<int> { 31, 32, 33, 34, 35, 36, 44, 54, 55, 64, 61, 66, 65, 63, 62, 56, 43, 46 };
            var order = preferred.Concat(fallbacks)
                .Concat(MissionCardCatalog.All(System).Select(c => c.Number)).Distinct().ToList();
            return order.Select((c, i) => (Card: c, F: Feasibility(c, slot), I: i))
                .OrderByDescending(x => x.F >= HopelessBelow ? 1 : 0)
                .ThenBy(x => x.I)
                .Take(MissionCardCatalog.CustomDeckSize)
                .Select(x => x.Card).ToList();
        }

        /// <summary>
        /// Which cards to place from hand: ones already being met on the table now first (the most VP), then by
        /// how scoreable they are, then in the order drawn.
        /// </summary>
        private List<int> PlaceOrder(List<int> hand, int slot, int need)
        {
            MissionCardState? state = GameProgressUtilities.MissionCards(Store);
            MissionBoard? board = state == null ? null : Board(Math.Max(1, state.Round));
            return hand.Select((c, i) => (Card: c, I: i,
                    Now: state != null && board != null
                        ? MissionCardRules.Evaluate(MissionCardCatalog.Card(System, c), slot, slot, board, state, System).Vp
                        : 0,
                    F: Feasibility(c, slot)))
                .OrderByDescending(x => x.F >= HopelessBelow ? 1 : 0)
                .ThenByDescending(x => x.Now)
                .ThenByDescending(x => x.F)
                .ThenBy(x => x.I)
                .Take(need).Select(x => x.Card).ToList();
        }

        /// <summary>
        /// Planned Gambit / Direct Challenge: how good an opponent's visible card is for them right now - VP they
        /// would score on the table as it stands, plus how scoreable it is. A hidden card reads as average.
        /// </summary>
        private float ValueToOpponent(MissionCardState state, MissionCardSide oppSide, int card, MissionBoard board)
        {
            if (oppSide.Hidden.Contains(card)) return 0.5f;
            int now = MissionCardRules.Evaluate(MissionCardCatalog.Card(System, card), oppSide.Slot, oppSide.Slot,
                board, state, System).Vp;
            return now * 2f + Feasibility(card, oppSide.Slot);
        }
    }
}
