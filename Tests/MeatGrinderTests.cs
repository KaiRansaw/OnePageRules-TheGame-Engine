using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Missions;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Meat Grinder Battles (Advanced Rulebooks 3.6.0: GF p51, GFF p49, AoF p51, AoFS p49). The numbers are
    /// checked against every book's worked examples; the rest against a two-seat table. Played-game check: the bot sweep.</summary>
    [TestFixture]
    public class MeatGrinderTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private readonly List<object> _asked = new();

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private sealed class FaceSequence : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public FaceSequence(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_faces[Math.Min(_i++, _faces.Length - 1)]);
        }

        private sealed class Seat : IPlayerController
        {
            public Seat(string name, PlayerID id) { Name = name; ID = id; }
            public string Name { get; }
            public PlayerID ID { get; }
            public bool IsReady => true;
            public Presentation.IPresentationSink? PresentationSink => null;
#pragma warning disable CS0067
            public event Action<bool>? OnReadyStateChanged;
            public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
#pragma warning restore CS0067
            public Task WaitUntilReadyAsync() => Task.CompletedTask;
            public void SendLogMessage(string logMessage, TextColor color) { }
            public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
        }

        private object Reply(object r) => r switch
        {
            PlaceObjectsRequest<ModelData> place => new Selected<List<PlacedObjectEntry<ModelData>>>(
                place.ModelsToPlace.Select((m, i) => new PlacedObjectEntry<ModelData>(m, new Position(20f + 2f * i, 6f))).ToList()),
            HordeShopRequest => HordeShopReply.Nothing,
            StringSelectionRequest q => q.Recommended ?? q.ValidOptions[0],
            _ => true,
        };

        /// <summary>Two seats, each with an army of one 100-pt unit and one 50-pt unit (150 pts).</summary>
        private void Build(EExtraMission mission, params int[] faces)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.ExtraMission = mission;
            settings.ArmyPoints = 600;
            _context = new TestGameContext(_store, new FaceSequence(faces.Length == 0 ? new[] { 3 } : faces), settings: settings,
                playerRequester: new Answer(r => { _asked.Add(r); return Reply(r); }));
            _asked.Clear();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
            Army(_me, 6f);
            Army(_them, 40f);
            ExtraMissionState s = ExtraMissions.StateFor(_context);
            s.Mission = mission;
            MeatGrinder.Begin(_context, s);
            ExtraMissions.SaveState(_context, s);
        }

        private void Army(PlayerID owner, float z)
        {
            var big = Unit(owner, "Big", 100, 2, new Position(10f, z));
            var small = Unit(owner, "Small", 50, 1, new Position(30f, z));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>>
            {
                _store.GetDataBinding<UnitData>(_store.Create(big)), _store.GetDataBinding<UnitData>(_store.Create(small)),
            }));
        }

        private UnitData Unit(PlayerID owner, string name, int cost, int models, Position at)
        {
            var bindings = Enumerable.Range(0, models)
                .Select(i => _store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i, at.z), _store))))
                .ToList();
            return new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings) { PointCost = cost };
        }

        private UnitData UnitOf(PlayerID owner, string name) =>
            _context.TableState.Units.Objects.OfType<UnitData>().Single(u => u.PlayerID.Equals(owner) && u.Name == name);

        private static void Kill(UnitData unit, int models = int.MaxValue)
        {
            foreach (IModel model in unit.Models.Take(models)) ((ModelData)model).DealWounds(((ModelData)model).TotalWounds);
        }

        private ExtraMissionState State => ExtraMissions.StateFor(_context);
        private MeatGrinderState Grinder => State.Grinder!;

        // ---------------------------------------------------------------- the book's numbers

        [Test]
        public void Tokens_FollowEveryBooksExample()
        {
            // AoFS 600: "1 token per full 30pts ... a 25pts unit would get 0 tokens, whilst a 70pts unit would get 2".
            Assert.That(MeatGrinder.TokensFor(25, 600), Is.EqualTo(0));
            Assert.That(MeatGrinder.TokensFor(70, 600), Is.EqualTo(2));
            // GFF 800: 30 -> 0, 90 -> 2.
            Assert.That(MeatGrinder.TokensFor(30, 800), Is.EqualTo(0));
            Assert.That(MeatGrinder.TokensFor(90, 800), Is.EqualTo(2));
            // AoF 1500: 70 -> 0, 200 -> 2.
            Assert.That(MeatGrinder.TokensFor(70, 1500), Is.EqualTo(0));
            Assert.That(MeatGrinder.TokensFor(200, 1500), Is.EqualTo(2));
            // GF 2000: 90 -> 0, 250 -> 2. And an exact step: 100 -> 1.
            Assert.That(MeatGrinder.TokensFor(90, 2000), Is.EqualTo(0));
            Assert.That(MeatGrinder.TokensFor(250, 2000), Is.EqualTo(2));
            Assert.That(MeatGrinder.TokensFor(100, 2000), Is.EqualTo(1));
        }

        [Test]
        public void Recruitment_Purse_FollowsEveryBooksExample()
        {
            // "For a 600pts game, you start with 300pts, get 90pts to spend each round, score X*6pts per objective, and
            // can spend a max. of 180pts." GFF 800: 400 / 120 / X*8 / 240. AoF 1500: 750 / 225 / X*15 / 450. GF 2000:
            // 1000 / 300 / X*20 / 600.
            foreach ((int size, int start, int each, int perObj, int max) in new[]
                     { (600, 300, 90, 6, 180), (800, 400, 120, 8, 240), (1500, 750, 225, 15, 450), (2000, 1000, 300, 20, 600) })
            {
                Assert.That(MeatGrinder.StartingPoints(size), Is.EqualTo(start), $"{size}: start");
                Assert.That(MeatGrinder.MaxSpend(size), Is.EqualTo(max), $"{size}: max per round");
                Assert.That(MeatGrinder.Income(size, round: 1, objectives: 0), Is.EqualTo(each), $"{size}: each round");
                Assert.That(MeatGrinder.Income(size, round: 3, objectives: 2), Is.EqualTo(each + 3 * perObj * 2), $"{size}: X*{perObj} per objective");
                Assert.That(MeatGrinder.Income(size, round: 3, objectives: 2, baseShare: false), Is.EqualTo(3 * perObj * 2), $"{size}: underdog rule off");
            }
            // Rounding up: 1% of 650 is 6.5.
            Assert.That(MeatGrinder.Income(650, round: 1, objectives: 1, baseShare: false), Is.EqualTo(7));
        }

        [Test]
        public void TheMissions_InTheLobby_FiveMarkers_SixRounds_HalfPointsForRecruitment()
        {
            Assert.That(ExtraMissionCatalog.LobbyOrder, Does.Contain(EExtraMission.MeatGrinderRespawning)
                .And.Contain(EExtraMission.MeatGrinderRecruitment).And.Contain(EExtraMission.MeatGrinderRecruitmentUnderdog));
            foreach (EExtraMission m in new[] { EExtraMission.MeatGrinderRespawning, EExtraMission.MeatGrinderRecruitment, EExtraMission.MeatGrinderRecruitmentUnderdog })
            {
                ExtraMissionInfo info = ExtraMissionCatalog.Of(m);
                Assert.That(info.Mission, Is.EqualTo(m));
                Assert.That(info.Markers, Is.EqualTo(EMissionMarkers.Five));
                Assert.That((info.Name + info.BookText).All(ch => ch < 128), "game text is ASCII");
                GameSettings s = GameSettings.GetDefault();
                s.ExtraMission = m;
                Assert.That(HordeRules.RoundLimit(s), Is.EqualTo(6), $"{m}: 6 rounds");
            }
            Assert.That(ExtraMissionCatalog.Of(EExtraMission.MeatGrinderRespawning).Progressive, Is.False, "VP from kills, not each round");
            Assert.That(ExtraMissionCatalog.Of(EExtraMission.MeatGrinderRecruitment).Progressive, Is.True, "X VP per objective each round");
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.MeatGrinderRecruitment, 0, 1), Is.EqualTo(1000));
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.MeatGrinderRespawning, 0, 1), Is.EqualTo(2000));
            Build(EExtraMission.MeatGrinderRespawning);
            Assert.That(ExtraMissions.MarkerCount(_context), Is.EqualTo(5));
            Assert.That(ExtraMissions.MapMarkersAllowed(_context.Settings, 5), Is.True);
            Assert.That(ExtraMissions.MapMarkersAllowed(_context.Settings, 4), Is.False);
        }

        // ---------------------------------------------------------------- respawning

        [Test]
        public void ADestroyedUnit_ScoresTheEnemyItsPoints_AndGoesIntoReservesWithTokens()
        {
            Build(EExtraMission.MeatGrinderRespawning);
            UnitData big = UnitOf(_them, "Big");
            Kill(big);
            MeatGrinder.AfterActivation(_context);
            Assert.That(State.Vp.GetValueOrDefault(0), Is.EqualTo(100), "Kai scores the bot's 100-pt unit");
            Assert.That(big.GetIsAlive(), Is.True, "back at full strength, off the table");
            Assert.That(ReserveRules.IsInReserve(big), Is.True);
            Assert.That(big.Tokens.HasToken(TokenType.Respawning), Is.True, "Ambush can't bring it on");
            // 5% of a 150-pt army is 7.5: 100 pts = 13 full steps.
            Assert.That(Grinder.Tokens[big.ID.ID.ToString()], Is.EqualTo(13));
            MeatGrinder.AfterActivation(_context);
            Assert.That(State.Vp.GetValueOrDefault(0), Is.EqualTo(100), "scored once per death");
        }

        [Test]
        public void AUnitWithNoPoints_StaysGone_AndScoresNothing()
        {
            Build(EExtraMission.MeatGrinderRespawning);
            UnitData summoned = UnitOf(_them, "Small");
            summoned.PointCost = 0;
            Kill(summoned);
            MeatGrinder.AfterActivation(_context);
            MeatGrinder.AfterActivation(_context);
            Assert.That(summoned.GetIsAlive(), Is.False, "never part of the army: it does not come back");
            Assert.That(State.Vp.GetValueOrDefault(0), Is.EqualTo(0));
            Assert.That(Grinder.Gone, Has.Count.EqualTo(1), "noted once");
        }

        [Test]
        public async Task TheRoundStartRoll_MustBeatTheTokens_ElseOneComesOff()
        {
            Build(EExtraMission.MeatGrinderRespawning, 2, 3);
            UnitData small = UnitOf(_me, "Small");
            Kill(small);
            MeatGrinder.AfterActivation(_context);
            string id = small.ID.ID.ToString();
            Assert.That(Grinder.Tokens[id], Is.EqualTo(6), "50 pts / 7.5 = 6 full steps");
            Grinder.Tokens[id] = 2;
            ExtraMissions.SaveState(_context, State);
            MeatGrinder.AfterActivation(_context);

            await MeatGrinder.RoundStart(_context, 2, null);   // rolls 2: not higher than 2
            Assert.That(ReserveRules.IsInReserve(small), Is.True);
            Assert.That(Grinder.Tokens[id], Is.EqualTo(1), "a failed roll takes one token off");

            await MeatGrinder.RoundStart(_context, 3, null);   // rolls 3: higher than 1
            Assert.That(ReserveRules.IsInReserve(small), Is.False, "deployed again");
            Assert.That(small.Tokens.HasToken(TokenType.Respawning), Is.False);
            Assert.That(Grinder.Tokens.ContainsKey(id), Is.False);
            Assert.That(small.Models.Single().Position.z, Is.LessThan(12f), "in its deployment zone");
        }

        [Test]
        public async Task Withdraw_OnlyAtHalfStrengthOrLess_AndItCountsAsDestroyed()
        {
            Build(EExtraMission.MeatGrinderRespawning);
            UnitData big = UnitOf(_me, "Big");
            Assert.That(MeatGrinder.CanWithdraw(_context, big), Is.False, "both models standing");
            Kill(big, 1);
            Assert.That(MeatGrinder.CanWithdraw(_context, big), Is.True, "one of two left = half");
            await MeatGrinder.Withdraw(_context, big);
            Assert.That(State.Vp.GetValueOrDefault(1), Is.EqualTo(100), "the enemy scores it, as a destroyed unit");
            Assert.That(ReserveRules.IsInReserve(big), Is.True);

            Build(EExtraMission.MeatGrinderRecruitment);
            UnitData other = UnitOf(_me, "Big");
            Kill(other, 1);
            Assert.That(MeatGrinder.CanWithdraw(_context, other), Is.False, "only in Respawning");
        }

        [Test]
        public void RespawningFinalScore_Kills_Objectives_AndHalfStrengthEnemies()
        {
            Build(EExtraMission.MeatGrinderRespawning);
            ExtraMissionState s = State;
            s.Vp[0] = 50;
            ExtraMissions.SaveState(_context, s);
            var objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            objective.SetOwner(_me);
            Kill(UnitOf(_them, "Big"), 1);   // half strength, on the table: 100/2 = 50

            (Dictionary<int, int> score, string unit) = ExtraMissions.FinalScore(_context)!.Value;
            Assert.That(unit, Is.EqualTo("VP"));
            // 50 kills + 1 objective at 20% of 150 (30) + 50 for the bot's Big at half strength.
            Assert.That(score[0], Is.EqualTo(50 + 30 + 50));
            Assert.That(score.GetValueOrDefault(1), Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- recruitment

        [Test]
        public void Recruitment_StartsWithHalfTheGameLessTheList_AndEarnsEachRound()
        {
            Build(EExtraMission.MeatGrinderRecruitment);
            Assert.That(Grinder.Bank[0], Is.EqualTo(300 - 150), "600 game: 300 to recruit with, the list cost 150");
            var objective = new ObjectiveData(new Position(36f, 24f), _store);
            _store.Create(objective);
            objective.SetOwner(_me);

            MeatGrinder.RoundEnd(_context, State, 2);
            Assert.That(Grinder.LastIncome[0], Is.EqualTo(90 + 2 * 6), "15% + X*1% per objective");
            Assert.That(Grinder.LastIncome[1], Is.EqualTo(90));
            Assert.That(Grinder.Bank[0], Is.EqualTo(150 + 102));
        }

        [Test]
        public void Underdog_FromRoundThree_OnlyThePlayerBehindGetsTheFifteenPercent()
        {
            Build(EExtraMission.MeatGrinderRecruitmentUnderdog);
            ExtraMissionState s = State;
            s.Vp[0] = 3; s.Vp[1] = 8;
            MeatGrinder.RoundEnd(_context, s, 2);
            Assert.That(s.Grinder!.LastIncome[1], Is.EqualTo(90), "not yet: before the end of round 3");
            MeatGrinder.RoundEnd(_context, s, 3);
            Assert.That(s.Grinder.Underdog, Is.EqualTo(0));
            Assert.That(s.Grinder.LastIncome[0], Is.EqualTo(90));
            Assert.That(s.Grinder.LastIncome[1], Is.EqualTo(0), "the leader gets no 15%");
            s.Vp[0] = 8;
            MeatGrinder.RoundEnd(_context, s, 4);
            Assert.That(s.Grinder.Underdog, Is.EqualTo(-1), "level: no underdog");
            Assert.That(s.Grinder.LastIncome.Values, Is.All.EqualTo(0), "so nobody gets the 15%");
        }

        [Test]
        public async Task Recruitment_TheShop_IsCappedAtThirtyPercent_AndBuyingNothingKeepsThePoints()
        {
            Build(EExtraMission.MeatGrinderRecruitment);
            ExtraMissionState s = State;
            s.Grinder!.Bank[0] = 400;
            ExtraMissions.SaveState(_context, s);
            await MeatGrinder.RoundStart(_context, 2, null);
            HordeShopRequest mine = _asked.OfType<HordeShopRequest>().Single(r => r.TargetPlayerID.Equals(_me));
            Assert.That(mine.RecruitOnly && mine.BotsRecruit, Is.True);
            Assert.That(mine.PointsAvailable, Is.EqualTo(180), "max. 30% of 600");
            Assert.That(mine.PointsBanked, Is.EqualTo(400));
            Assert.That(Grinder.Bank[0], Is.EqualTo(400), "nothing bought, nothing spent");
        }

        [Test]
        public void ABot_BuysFromItsBook_WithinThePoints_OnlyWhenAsked()
        {
            BookFile book = DemoBook.Build();
            var request = new HordeShopRequest(new PlayerID(Guid.NewGuid()), 2, 200, book.Name, book.Name,
                new List<HordeShopObjective> { new(0, 10f, 6f, false) });
            Func<string, string, string?, BookFile?>? before = BookSource.Find;
            try
            {
                BookSource.Find = (_, _, _) => book;
                Assert.That(RecruitPicker.BotReply(request).RecruitsJson, Is.Null, "Horde Mode: bots keep their points");
                request.BotsRecruit = true;
                HordeShopReply reply = RecruitPicker.BotReply(request);
                Assert.That(reply.RecruitsJson, Is.Not.Null);
                ArmyListFile? list = RecruitPicker.Pick(book, 200, new Random(1));
                Assert.That(list, Is.Not.Null);
                Assert.That(list!.Units.Sum(u => u.PointCost), Is.InRange(1, 200));
            }
            finally { BookSource.Find = before; }
        }
    }
}
