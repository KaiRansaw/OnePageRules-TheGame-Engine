using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Stages;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // "Each round, players alternate in activating one unit each, starting with the player that won the
    // deployment roll-off. Each new round, the player that finished activating first on the last round
    // gets to go first."
    //
    // The handoff that carries "who leads" between rounds was already right: MainPhaseContext seeds round 1
    // from the deployment roll-off order and thereafter takes the finish order the round reports, so index 0
    // of TeamActivateOrder is always the team that should open the round (MainPhaseContextTests pins that
    // half). What was missing is the other half, pinned here: that the round actually OPENS with the team at
    // index 0.
    //
    // The alternation cursor advances before every activation, and TryAdvance means "move PAST the current
    // position" - so a cursor sitting at index 0 hands the round's opening activation to index 1, the team
    // that should have gone second. Deployment and objective placement dodge this with a first-turn guard
    // (DetermineNextDeployPlayerStage, DetermineNextObjectivePlacerStage); the round loop had no equivalent,
    // so in a two-player game the wrong player opened EVERY round.
    [TestFixture]
    public class RoundActivationOrderTests
    {
        private GameDataStore _store = null!;
        private PlayerID _playerA;
        private PlayerID _playerB;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _playerA = new PlayerID(System.Guid.NewGuid());
            _playerB = new PlayerID(System.Guid.NewGuid());
        }

        // ── The round opens with the head of the order ───────────────────────────────────────────────────

        [Test]
        public void FirstActivationOfARound_GoesToTheTeamAtTheHeadOfTheOrder()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            // Team A won the deployment roll-off, so team A leads round 1.
            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            bool advanced = round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(advanced, Is.True);
            Assert.That(team, Is.SameAs(teamA),
                "the roll-off winner activates first, not second");
            Assert.That(player, Is.EqualTo(_playerA));
        }

        [Test]
        public void FirstActivationOfALaterRound_GoesToWhoeverFinishedActivatingFirst()
        {
            // The reported bug, end to end: team A led round 1, team B ran out of units first, so B leads
            // round 2. MainPhaseContext already hands the round the order [B, A]; what is checked here is
            // that the round opens with B rather than handing the opening activation straight back to A.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var mainPhase = new MainPhaseContext(ctx, new List<ITeam> { teamA, teamB });
            mainPhase.OnEndOfRound(new List<ITeam> { teamB, teamA });

            var round = new SingleRoundContext(ctx, mainPhase.TeamActivateOrder!, mainPhase.RoundCount);

            round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(mainPhase.RoundCount, Is.EqualTo(2));
            Assert.That(team, Is.SameAs(teamB),
                "the player that finished activating first last round gets to go first");
            Assert.That(player, Is.EqualTo(_playerB));
        }

        [Test]
        public async Task DeterminePlayerTurnStage_OpensTheRoundWithTheHeadOfTheOrder()
        {
            // Same statement one layer up, at the stage that actually drives the round loop - the cursor is
            // only ever advanced from here, so this is the seam where a wrong opening player reaches players.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            var stage = new DeterminePlayerTurnStage(ctx, new NoOpLayer<ISingleRoundContext>());
            stage.OnDeterminedPlayerTurn.Bind("determined");
            stage.OnNoPlayersLeft.Bind("none");
            await stage.Enter(round);

            Assert.That(round.GetCurrentPlayerID(), Is.EqualTo(_playerA));
        }

        [Test]
        public void FirstActivationOfARound_GoesToTheHeadTeamsFirstListedPlayer()
        {
            // Within a team the round-robin has the same off-by-one, so a two-player team would open with
            // its SECOND player.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            var teammate = new PlayerID(System.Guid.NewGuid());
            MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(teammate, "A2", new Position(9f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA, teammate);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(team, Is.SameAs(teamA));
            Assert.That(player, Is.EqualTo(_playerA),
                "the head team opens with its first-listed player, not its second");
        }

        // ── ...without breaking what already worked ──────────────────────────────────────────────────────

        [Test]
        public void AfterTheOpeningActivation_TheRoundAlternates()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> a1 = MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerA, "A2", new Position(9f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            round.TryAdvanceToNextPlayer(out ITeam? first, out _);
            round.MarkUnitAsActivated(a1);
            round.TryAdvanceToNextPlayer(out ITeam? second, out _);

            Assert.That(first, Is.SameAs(teamA));
            Assert.That(second, Is.SameAs(teamB),
                "one unit each, alternating - the opening fix must not turn into two activations in a row");
        }

        [Test]
        public void AHeadTeamWithNothingToActivate_IsSkipped()
        {
            // The cursor's skip-empty scan has to apply to the opening activation too: a team whose whole
            // force is off-table (all in Ambush reserve) leads the order but cannot open the round.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            bool advanced = round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(advanced, Is.True);
            Assert.That(team, Is.SameAs(teamB));
            Assert.That(player, Is.EqualTo(_playerB));
        }

        [Test]
        public void ARoundWithNoActivationsAtAll_ReportsNoOneLeft()
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 1);

            Assert.That(round.TryAdvanceToNextPlayer(out _, out _), Is.False,
                "an opening position that is merely 'not yet taken' must still end the round when nobody " +
                "has anything to activate");
        }

        // ── ...and survives a save taken before the round's first activation ─────────────────────────────

        [Test]
        public void SaveTakenBeforeTheOpeningActivation_ResumesWithTheHeadOfTheOrder()
        {
            // The rolling snapshot is written at the TOP of DeterminePlayerTurnStage, i.e. before the round's
            // first advance - so "nobody has activated yet" is a cursor position that has to round-trip
            // through GameProgressData, or a game saved at the start of a round resumes on the wrong player.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var live = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 2);

            GameProgressData snapshot = GameProgressUtilities.Capture(live, ctx.Settings, EResumeStage.MainPhase);
            SingleRoundContext restored = GameProgressUtilities.RestoreSingleRoundContext(ctx, snapshot);

            restored.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(team, Is.SameAs(teamA));
            Assert.That(player, Is.EqualTo(_playerA));
        }

        [Test]
        public void SaveTakenMidRound_ResumesOnTheAlternation()
        {
            // The other side of that round-trip: once the round HAS opened, a restored cursor must still
            // advance rather than replay the team that just went.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> a1 = MakeUnit(_playerA, "A1", new Position(5f, 5f));
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var live = new SingleRoundContext(ctx, new List<ITeam> { teamA, teamB }, roundCount: 2);
            live.TryAdvanceToNextPlayer(out _, out _);
            live.MarkUnitAsActivated(a1);

            GameProgressData snapshot = GameProgressUtilities.Capture(live, ctx.Settings, EResumeStage.MainPhase);
            SingleRoundContext restored = GameProgressUtilities.RestoreSingleRoundContext(ctx, snapshot);

            restored.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(team, Is.SameAs(teamB));
            Assert.That(player, Is.EqualTo(_playerB));
        }

        // ── The cursor's own contract ────────────────────────────────────────────────────────────────────

        [Test]
        public void APlainCursor_IsPositionedOnTheFirstTurn()
        {
            // Terrain and terrain-points placement read the current position and THEN advance, so a plain
            // cursor must keep pointing AT the first (team, player). Pinned so the round's opening fix
            // cannot be mistaken for a change to this idiom.
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);
            var cursor = new TeamPlayerAlternationCursor(new List<ITeam> { teamA, teamB });

            Assert.That(cursor.GetCurrentTeam(), Is.SameAs(teamA));
            Assert.That(cursor.GetCurrentPlayerID(), Is.EqualTo(_playerA));

            cursor.TryAdvance(_ => true, _ => true, out ITeam? next, out _);
            Assert.That(next, Is.SameAs(teamB), "and then advancing moves PAST it");
        }

        [Test]
        public void AParkedCursor_ArrivesOnTheFirstTurnWhenItAdvances()
        {
            // The round's idiom: advance before every turn, including the first.
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);
            var cursor = new TeamPlayerAlternationCursor(new List<ITeam> { teamA, teamB });
            cursor.ParkBeforeFirstTurn();

            cursor.TryAdvance(_ => true, _ => true, out ITeam? first, out PlayerID? player);
            Assert.That(first, Is.SameAs(teamA));
            Assert.That(player, Is.EqualTo(_playerA));

            cursor.TryAdvance(_ => true, _ => true, out ITeam? second, out _);
            Assert.That(second, Is.SameAs(teamB), "and it alternates normally from there");
        }

        // ── Fixture ──────────────────────────────────────────────────────────────────────────────────────

        // ── Quest modes: the heroes always lead ──────────────────────────────────────────────────────────

        // GF: Star Quest core rules v2.1.0, "Playing the Game": "Each round, players activate all Heroes first, and
        // then the AI activates all units in order from the closest to a Hero to the furthest." A round where the
        // AI had no unit on the table (the sentries dead, reinforcements not in yet) recorded the AI as finishing
        // first, so the next round's order came in as [AI, heroes] - and the enemies opened every round from then
        // on (bug hunt 2026-11-27 run 3: 24 of 26 rounds in one played mission).
        [TestCase(EGameMode.StarQuestMission)]
        [TestCase(EGameMode.StarQuestHorde)]
        public void QuestRound_HeroesActivateFirst_EvenWhenTheAiFinishedFirstLastRound(EGameMode mode)
        {
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = mode;
            ctx.Settings = settings;
            DataBinding<UnitData> hero = MakeUnit(_playerA, "Hero", new Position(5f, 5f));
            hero.GetValue().QuestHeroJson = new QuestHero { Name = "Hero", Power = 3 }.ToJson();
            MakeUnit(_playerB, "Enemy", new Position(25f, 5f));
            ITeam heroes = MakeTeam(1, _playerA);
            ITeam ai = MakeTeam(2, _playerB);

            var mainPhase = new MainPhaseContext(ctx, new List<ITeam> { heroes, ai });
            mainPhase.OnEndOfRound(new List<ITeam> { ai, heroes });   // the AI "finished first" last round
            var round = new SingleRoundContext(ctx, mainPhase.TeamActivateOrder!, mainPhase.RoundCount);

            round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? player);

            Assert.That(team, Is.SameAs(heroes), "the heroes open every Quest round");
            Assert.That(player, Is.EqualTo(_playerA));
        }

        [Test]
        public void StandardRound_StillFollowsTheFinishOrder()
        {
            // The Quest rule must not leak into army games: there the team that finished first leads.
            var ctx = new TriggeredMoveTestContext(_store, new NullPlayerRequester());
            DataBinding<UnitData> hero = MakeUnit(_playerA, "A1", new Position(5f, 5f));
            hero.GetValue().QuestHeroJson = new QuestHero { Name = "Hero", Power = 3 }.ToJson();
            MakeUnit(_playerB, "B1", new Position(25f, 5f));
            ITeam teamA = MakeTeam(1, _playerA);
            ITeam teamB = MakeTeam(2, _playerB);

            var round = new SingleRoundContext(ctx, new List<ITeam> { teamB, teamA }, 2);
            round.TryAdvanceToNextPlayer(out ITeam? team, out PlayerID? _);

            Assert.That(team, Is.SameAs(teamB));
        }

        private ITeam MakeTeam(int number, params PlayerID[] players)
        {
            var team = new TeamData(number, players.ToList());
            _store.Create(team); // surfaces in TableState.Teams, which SingleRoundContext reads
            return team;
        }

        // Positions are off the origin on purpose: a unit whose living models all sit at exactly (0,0) reads
        // as off-battlefield and never enters the round's activation pool.
        private DataBinding<UnitData> MakeUnit(PlayerID player, string name, Position position)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
            var modelBindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };

            var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(player, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
