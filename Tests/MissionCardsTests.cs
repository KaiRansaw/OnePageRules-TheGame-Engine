using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Missions;
using FDG.Players;
using FDG.Presentation;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// The Mission Cards (Mission Cards v3.6.0; Kai's card screenshots and the two per-book differences he
    /// gave, 2026-11-05): the catalog, what each card asks, and both fixed-deck games played to the end.
    /// </summary>
    [TestFixture]
    public class MissionCardsTests
    {
        // ── The catalog ──────────────────────────────────────────────────────────────────────────

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void ThirtySixCards_NumberedLikeTwoDice_AsciiOnly(EGameSystem system)
        {
            var cards = MissionCardCatalog.All(system);
            Assert.That(cards.Select(c => c.Number).Distinct().Count(), Is.EqualTo(36));
            foreach (var card in cards)
            {
                Assert.That(card.Number / 10, Is.InRange(1, 6), "first die");
                Assert.That(card.Number % 10, Is.InRange(1, 6), "second die");
                foreach (string text in card.Conditions.Select(c => c.Text).Append(card.Title).Append(card.Note ?? ""))
                    Assert.That(text.All(ch => ch < 128), Is.True, $"card {card.Number}: game text must be ASCII: {text}");
            }
        }

        [Test]
        public void TheTwoCardsThatDifferByBook()
        {
            // Kai: "65: AoF/GF - ... tough 12 or higher, while AoFS/GFF is on tough 9 or higher."
            Assert.That(MissionCardCatalog.Card(EGameSystem.GrimdarkFuture, 65).Conditions[0].Text, Does.Contain("Tough(12)"));
            Assert.That(MissionCardCatalog.Card(EGameSystem.AgeOfFantasy, 65).Conditions[0].Text, Does.Contain("Tough(12)"));
            Assert.That(MissionCardCatalog.Card(EGameSystem.GFFirefight, 65).Conditions[0].Text, Does.Contain("Tough(9)"));
            Assert.That(MissionCardCatalog.Card(EGameSystem.AoFSkirmish, 65).Conditions[0].Text, Does.Contain("Tough(9)"));
            // "64: AoF/GF - ... three or more enemy models with the hero rule, while AoFS/GFF is only on killing 2 or more."
            Assert.That(MissionCardCatalog.Card(EGameSystem.GrimdarkFuture, 64).Conditions[1].Text, Does.Contain("three or more"));
            Assert.That(MissionCardCatalog.Card(EGameSystem.AoFSkirmish, 64).Conditions[1].Text, Does.Contain("two or more"));
        }

        [Test]
        public void OnlyTheFourBattleGamesHaveThem()
        {
            Assert.That(MissionCardCatalog.Available(EGameSystem.GFStarQuest), Is.False);
            Assert.That(MissionCardCatalog.Available(EGameSystem.AoFQuest), Is.False);
            var settings = GameSettings.GetDefault();
            settings.MissionCardGame = EMissionCardGame.BattleOfWits;
            settings.GameSystem = EGameSystem.GFFirefight;
            Assert.That(MissionCardGame.IsOn(settings), Is.True);
            settings.GameMode = EGameMode.Horde;
            Assert.That(MissionCardGame.IsOn(settings), Is.False, "a standard game only");
        }

        // ── What the cards ask ───────────────────────────────────────────────────────────────────

        private const int Me = 0, Them = 1;

        private static MissionBoard Board(int round = 2, Dictionary<int, int>? owners = null, params BoardUnit[] units) => new()
        {
            Round = round,
            TeamOf = new Dictionary<int, int> { [Me] = 0, [Them] = 1 },
            OwnersNow = owners ?? Enumerable.Range(1, 6).ToDictionary(i => i, _ => -1),
            Units = units.ToList(),
        };

        private static int Vp(int card, MissionBoard board, MissionCardState? state = null,
            EGameSystem system = EGameSystem.GrimdarkFuture, int slot = Me) =>
            MissionCardRules.Evaluate(MissionCardCatalog.Card(system, card), slot, slot, board,
                state ?? new MissionCardState(), system).Vp;

        private static Dictionary<int, int> Owned(params (int obj, int slot)[] held)
        {
            var owners = Enumerable.Range(1, 6).ToDictionary(i => i, _ => -1);
            foreach (var (obj, slot) in held) owners[obj] = slot;
            return owners;
        }

        [Test]
        public void Seize_AndHold()
        {
            var board = Board(owners: Owned((3, Me)));
            Assert.That(Vp(13, board), Is.EqualTo(1));
            Assert.That(Vp(23, board), Is.EqualTo(1), "the deck has two of each Seize");
            Assert.That(Vp(14, board), Is.EqualTo(0));
            Assert.That(Vp(13, board, slot: Them), Is.EqualTo(0));

            var state = new MissionCardState { OwnersAtRoundStart = Owned((3, Them)) };
            Assert.That(Vp(33, board, state), Is.EqualTo(0), "Hold needs it at the beginning too");
            state.OwnersAtRoundStart = Owned((3, Me));
            Assert.That(Vp(33, board, state), Is.EqualTo(2));
        }

        [Test]
        public void TacticalPrecision_AndPerfection()
        {
            Assert.That(Vp(45, Board(owners: Owned((1, Me), (2, Me)))), Is.EqualTo(0));
            Assert.That(Vp(45, Board(owners: Owned((1, Me), (2, Me), (6, Me)))), Is.EqualTo(2));
            var all = Owned(Enumerable.Range(1, 6).Select(i => (i, Me)).ToArray());
            Assert.That(Vp(46, Board(owners: all)), Is.EqualTo(5));
            all[4] = -1;
            Assert.That(Vp(46, Board(owners: all)), Is.EqualTo(0));
        }

        [Test]
        public void DeploymentZoneCards()
        {
            var mineHome = new BoardUnit(Me, new[] { 0 }, 20f);
            var mineAway = new BoardUnit(Me, new[] { 1 }, 20f);
            var theirsInMine = new BoardUnit(Them, new[] { 0 }, 20f);

            Assert.That(Vp(41, Board(units: mineAway)), Is.EqualTo(1), "Move Out");
            Assert.That(Vp(41, Board(units: new[] { mineAway, mineHome })), Is.EqualTo(0));

            Assert.That(Vp(42, Board(units: mineAway)), Is.EqualTo(1), "Infiltrate, one");
            Assert.That(Vp(42, Board(units: new[] { mineAway, mineAway, mineAway })), Is.EqualTo(2), "Infiltrate, three");

            var three = new[] { mineHome, mineHome, mineHome };
            Assert.That(Vp(43, Board(round: 2, units: three)), Is.EqualTo(1), "Keep at Bay");
            Assert.That(Vp(43, Board(round: 1, units: three)), Is.EqualTo(0), "can't be scored at the end of round 1");
            Assert.That(Vp(43, Board(round: 2, units: three.Append(theirsInMine).ToArray())), Is.EqualTo(0));
        }

        [Test]
        public void Outmaneuver()
        {
            Assert.That(Vp(54, Board(units: new BoardUnit(Them, Array.Empty<int>(), 13f))), Is.EqualTo(2));
            Assert.That(Vp(54, Board(units: new BoardUnit(Them, Array.Empty<int>(), 8f))), Is.EqualTo(1));
            Assert.That(Vp(54, Board(units: new BoardUnit(Them, Array.Empty<int>(), 3f))), Is.EqualTo(0));
            Assert.That(Vp(54, Board(units: new BoardUnit(Me, Array.Empty<int>(), 0f))), Is.EqualTo(2), "my own units don't matter");
        }

        [Test]
        public void ChangingPriority_UsesTheRolledObjective()
        {
            var state = new MissionCardState { OwnersAtRoundStart = Owned((5, Them)) };
            state.Priority[MissionCardState.PriorityKey(Me, 44)] = 5;
            Assert.That(Vp(44, Board(owners: Owned((5, Me))), state), Is.EqualTo(2), "taken from the enemy");
            state.OwnersAtRoundStart = Owned((5, Me));
            Assert.That(Vp(44, Board(owners: Owned((5, Me))), state), Is.EqualTo(1), "held all along");
            Assert.That(Vp(44, Board(owners: Owned((4, Me))), state), Is.EqualTo(0), "wrong objective");
        }

        private static MissionKill Kill(int round, int victim, int killer, string cause, bool flying = false) =>
            new() { Round = round, VictimSlot = victim, KillerSlot = killer, Cause = cause, Flying = flying };

        [Test]
        public void KillCards_CountThisRoundAndHowItDied()
        {
            var state = new MissionCardState();
            state.Kills.Add(Kill(2, Them, Me, "shooting"));
            state.Kills.Add(Kill(2, Them, Me, "melee"));
            state.Kills.Add(Kill(2, Them, -1, "melee"));    // routed after losing a melee
            state.Kills.Add(Kill(1, Them, Me, "shooting"));  // last round - doesn't count
            state.Kills.Add(Kill(2, Me, Them, "shooting"));  // one of mine

            var board = Board(round: 2);
            Assert.That(Vp(51, board, state), Is.EqualTo(1), "Fire at Will: one shot this round");
            Assert.That(Vp(52, board, state), Is.EqualTo(1), "In the Trenches: two in melee, rout included");
            Assert.That(Vp(53, board, state), Is.EqualTo(2), "Lay Them Low: three this round");
            state.Kills.Add(Kill(2, Them, Me, "melee"));
            Assert.That(Vp(52, board, state), Is.EqualTo(2));
            state.Kills.Add(Kill(2, Them, Me, "spell", flying: true));
            Assert.That(Vp(51, board, state), Is.EqualTo(1), "a spell is not shooting");
            Assert.That(Vp(63, board, state), Is.EqualTo(1), "Bring Them Down");
            Assert.That(Vp(63, board, state, slot: Them), Is.EqualTo(0));
        }

        private static MissionModelDeath Death(int round, int victim, bool hero = false, bool caster = false, int tough = 1,
            string id = "m") => new() { Round = round, VictimSlot = victim, Hero = hero, Caster = caster, Tough = tough, ModelId = id };

        [Test]
        public void ModelCards_FollowTheBook()
        {
            var state = new MissionCardState();
            state.ModelDeaths.Add(Death(2, Them, hero: true));
            state.ModelDeaths.Add(Death(2, Them, hero: true, caster: true));
            var board = Board(round: 2);

            Assert.That(Vp(62, board, state), Is.EqualTo(1), "Purity");
            Assert.That(Vp(64, board, state, EGameSystem.GrimdarkFuture), Is.EqualTo(1), "GF: two heroes is not three");
            Assert.That(Vp(64, board, state, EGameSystem.GFFirefight), Is.EqualTo(2), "GFF: two heroes is enough");

            state.ModelDeaths.Add(Death(2, Them, tough: 9));
            Assert.That(Vp(65, board, state, EGameSystem.AoFSkirmish), Is.EqualTo(1), "Skirmish: Tough(9)");
            Assert.That(Vp(65, board, state, EGameSystem.AgeOfFantasy), Is.EqualTo(0), "AoF: needs Tough(12)");
            state.ModelDeaths.Add(Death(2, Them, tough: 18));
            Assert.That(Vp(65, board, state, EGameSystem.AgeOfFantasy), Is.EqualTo(2));
        }

        [Test]
        public void Decapitate_AnyRound_AnyTiedTopHero()
        {
            var state = new MissionCardState();
            state.TopHeroes[Them] = new List<string> { "boss-a", "boss-b" };
            state.ModelDeaths.Add(Death(1, Them, hero: true, id: "boss-b"));
            Assert.That(Vp(61, Board(round: 3), state), Is.EqualTo(2), "this or any previous round; ties all count");
            Assert.That(Vp(61, Board(round: 3), state, slot: Them), Is.EqualTo(0));
        }

        [Test]
        public void IntimidateAttunementShowNoMercy()
        {
            var state = new MissionCardState();
            state.MoraleFails.Add(new[] { 2, Them });
            state.SpellsCast.AddRange(new[] { new[] { 2, Me }, new[] { 2, Me }, new[] { 2, Me } });
            state.MeleeWins.Add(new[] { 2, Me, 4 });
            var board = Board(round: 2);
            Assert.That(Vp(55, board, state), Is.EqualTo(1));
            Assert.That(Vp(56, board, state), Is.EqualTo(2));
            Assert.That(Vp(66, board, state), Is.EqualTo(1));
            state.MeleeWins.Add(new[] { 2, Me, 6 });
            Assert.That(Vp(66, board, state), Is.EqualTo(2));
        }

        // ── Custom decks ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Paragon_OnlyWhatHeroUnitsDid()
        {
            var state = new MissionCardState();
            state.Kills.Add(Kill(2, Them, Me, "shooting"));
            var board = Board(round: 2, owners: Owned((3, Me)));
            MissionCardDef fire = MissionCardCatalog.Card(EGameSystem.GrimdarkFuture, 51);
            Assert.That(MissionCardRules.Evaluate(fire, Me, Me, board, state, EGameSystem.GrimdarkFuture, heroOnly: true).Vp,
                Is.EqualTo(0), "the shooter had no Hero");
            state.Kills.Add(new MissionKill { Round = 2, VictimSlot = Them, KillerSlot = Me, Cause = "shooting", KillerHero = true });
            Assert.That(MissionCardRules.Evaluate(fire, Me, Me, board, state, EGameSystem.GrimdarkFuture, heroOnly: true).Vp,
                Is.EqualTo(1));

            MissionCardDef seize = MissionCardCatalog.Card(EGameSystem.GrimdarkFuture, 13);
            Assert.That(MissionCardRules.Evaluate(seize, Me, Me, board, state, EGameSystem.GrimdarkFuture, heroOnly: true).Vp,
                Is.EqualTo(0), "held, but no Hero unit there");
            var withHero = new MissionBoard
            {
                Round = 2, TeamOf = board.TeamOf, OwnersNow = board.OwnersNow,
                HeroUnitsNear = new Dictionary<int, List<int>> { [3] = new List<int> { Me } },
            };
            Assert.That(MissionCardRules.Evaluate(seize, Me, Me, withHero, state, EGameSystem.GrimdarkFuture, heroOnly: true).Vp,
                Is.EqualTo(1));
        }

        [Test]
        public void PickStrings_OnlyLegalAnswersGetThrough()
        {
            var request = new FDG.StageResolution.Requests.PickStringsRequest(new PlayerID(Guid.NewGuid()), "pick",
                new[] { "a", "b", "c" }, 2, 2, new[] { "a", "c" });
            Assert.That(request.Sanitise(new List<string> { "b", "c" }), Is.EqualTo(new[] { "b", "c" }));
            Assert.That(request.Sanitise(new List<string> { "b" }), Is.EqualTo(new[] { "a", "c" }), "too few - the suggestion");
            Assert.That(request.Sanitise(new List<string> { "b", "b" }), Is.EqualTo(new[] { "a", "c" }), "repeats");
            Assert.That(request.Sanitise(new List<string> { "b", "z" }), Is.EqualTo(new[] { "a", "c" }), "unknown");
        }

        [Test]
        public void TheSuggestedDeck_IsEighteenUniqueCards()
        {
            Assert.That(MissionCardCatalog.SuggestedDeck.Distinct().Count(), Is.EqualTo(MissionCardCatalog.CustomDeckSize));
            var numbers = MissionCardCatalog.All(EGameSystem.GrimdarkFuture).Select(c => c.Number).ToHashSet();
            Assert.That(MissionCardCatalog.SuggestedDeck.All(numbers.Contains), Is.True);
        }

        // ── Played to the end ────────────────────────────────────────────────────────────────────

        private sealed class LogCapturingController : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string> _lines;
            internal LogCapturingController(IPlayerController inner, List<string> lines) { _inner = inner; _lines = lines; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer
            { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { lock (_lines) _lines.Add(logMessage); _inner.SendLogMessage(logMessage, color); }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) =>
                _inner.SendPlayerMessage(sendingPlayerName, messageType, message);
        }

        private static async Task<(List<string> Log, MissionCardState? State, GameResult Result)> PlayAGame(int seed,
            EGameSystem system, EMissionCardGame game)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();
            var slots = new PlayerSlot[2];
            for (int i = 0; i < slots.Length; i++)
            {
                ArmyListFile army = TestArmies.MakeRichArmy(i == 0 ? "Ours" : "Theirs");
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()), army, store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = AiResolverRegistryFactory.CreateSoloRulesController(
                    $"AI {i}", slots[i].PlayerID, aiGame, seed, slots[i].SlotID);
                slots[i].AssignPlayerController(new LogCapturingController(ai, log));
            }

            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.GameSystem = system;
            settings.MissionCardGame = game;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += result => completed.TrySetResult(result);
            Task finished = await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            Assert.That(finished, Is.SameAs(completed.Task), "the game must play to completion.");
            lock (log) return (log.ToList(), GameProgressUtilities.MissionCards(store), completed.Task.Result);
        }

        [TestCase(EGameSystem.GrimdarkFuture, EMissionCardGame.BattleOfWits, 4620)]
        [TestCase(EGameSystem.GFFirefight, EMissionCardGame.TotalDomination, 4621)]
        [TestCase(EGameSystem.AgeOfFantasy, EMissionCardGame.TotalDomination, 4622)]
        [TestCase(EGameSystem.AoFSkirmish, EMissionCardGame.BattleOfWits, 4623)]
        [TestCase(EGameSystem.GrimdarkFuture, EMissionCardGame.DirectChallenge, 4624)]
        [TestCase(EGameSystem.GFFirefight, EMissionCardGame.StrategicRuse, 4625)]
        [TestCase(EGameSystem.AgeOfFantasy, EMissionCardGame.PlannedGambit, 4626)]
        [TestCase(EGameSystem.AoFSkirmish, EMissionCardGame.CoordinatedRecovery, 4627)]
        [TestCase(EGameSystem.GrimdarkFuture, EMissionCardGame.LogisticalSupremacy, 4628)]
        [TestCase(EGameSystem.GFFirefight, EMissionCardGame.TacticalParagons, 4629)]
        public async Task APlayedGame_DealsSixObjectives_ScoresCards_AndIsWonOnVictoryPoints(EGameSystem system,
            EMissionCardGame game, int seed)
        {
            var (log, state, result) = await PlayAGame(seed, system, game);

            Assert.That(log.Any(l => l.Contains("Mission Cards: 6 objectives, no objective roll.")), Is.True, "six objectives, no roll");
            Assert.That(state, Is.Not.Null, "the mission cards were dealt");
            Assert.That(state!.Game, Is.EqualTo(game));
            Assert.That(state.FinalObjectivesScored, Is.True, "the final objectives were added");
            Assert.That(log.Count(l => l.StartsWith("Mission cards (", StringComparison.Ordinal)),
                Is.GreaterThanOrEqualTo(game == EMissionCardGame.TotalDomination ? 4 : 8), "dealt every round");
            Assert.That(log.Any(l => l.StartsWith("Victory points:", StringComparison.Ordinal)), Is.True);

            foreach (string line in log.Where(l => l.Contains("mission card", StringComparison.OrdinalIgnoreCase)
                         || l.StartsWith("Victory points", StringComparison.Ordinal) || l.Contains(" VP")))
                TestContext.WriteLine(line);

            // The card arithmetic adds up: VP = scored cards + final objectives.
            int cardVp = state.Scored.Sum(s => s.Vp);
            Assert.That(state.Vp.Values.Sum(), Is.GreaterThanOrEqualTo(cardVp));
            if (game == EMissionCardGame.TotalDomination)
                Assert.That(state.SharedTable.Count, Is.LessThanOrEqualTo(3));
            else
                foreach (MissionCardSide side in state.Sides.Values)
                    Assert.That(side.Table.Count, Is.LessThanOrEqualTo(3));

            if (MissionCardCatalog.IsCustom(game))
            {
                Assert.That(log.Count(l => l.Contains("builds a 18-card mission deck")), Is.GreaterThanOrEqualTo(2),
                    "each player built an 18-card deck after deployment");
                foreach (MissionCardSide side in state.Sides.Values)
                {
                    var all = side.Deck.Concat(side.Hand).Concat(side.Table).Concat(side.Discard).ToList();
                    Assert.That(all.Distinct().Count(), Is.EqualTo(18), "the 18 cards are all still somewhere, once each");
                    Assert.That(side.Hand.Count, Is.LessThanOrEqualTo(MissionCardCatalog.SupremacyHandSize));
                }
            }
            if (game == EMissionCardGame.DirectChallenge)
                Assert.That(log.Any(l => l.StartsWith("Direct Challenge:", StringComparison.Ordinal)), Is.True);

            int top = state.Vp.Values.Max();
            bool tie = state.Vp.Values.Count(v => v == top) > 1;
            Assert.That(result.Outcome == EGameOutcome.Tie, Is.EqualTo(tie), $"VP {string.Join("/", state.Vp.Values)} - the winner is the one with most VP");
        }
    }
}
