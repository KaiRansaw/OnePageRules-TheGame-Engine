using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Political Intrigue - the directives (GF p49, AoF p49). Played-game check: the bot sweep.</summary>
    [TestFixture]
    public class PoliticalIntrigueTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private UnitData _myUnit = null!, _theirUnit = null!;
        private readonly List<object> _asked = new();

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
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

        /// <summary>Answers a directive offer with <paramref name="directive"/> (null = the recommended answer).</summary>
        private void Build(string? directive = null, int face = 4, EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = EAdvancedRules.PoliticalDirectives;
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: new Answer(r =>
                {
                    _asked.Add(r);
                    if (r is StringSelectionRequest q)
                    {
                        if (directive != null && q.DisplayName == "Political Intrigue - directive")
                            return q.ValidOptions.FirstOrDefault(o => o.StartsWith(directive)) ?? q.ValidOptions[0];
                        return q.Recommended ?? q.ValidOptions[0];
                    }
                    return r is YesNoRequest y ? y.DefaultAnswer : throw new InvalidOperationException(r.GetType().Name);
                }));
            _asked.Clear();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
            GameProgressUtilities.WriteProgress(_store, new GameProgressData(
                stage: EResumeStage.MainPhase, roundCount: 1,
                teamActivateOrder: new List<int> { 0, 1 },
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 }, { 1, 0 } },
                unactivatedUnits: new List<DataBinding<UnitData>>(),
                settings: settings));
            _myUnit = Unit(_me, "Mine", new Position(20f, 20f), models: 3);
            _theirUnit = Unit(_them, "Theirs", new Position(20f, 30f), models: 1);
            _store.Create(new ObjectiveData(new Position(40f, 24f), _store));
        }

        private UnitData Unit(PlayerID owner, string name, Position at, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon> { new("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0) },
                    new Position(at.x + i * 1.5f, at.z), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings) { PointCost = 100 };
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(unit)) }));
            return unit;
        }

        private Task<PoliticalIntrigue.EOutcome> Offer(int round = 1, bool units = true) => PoliticalIntrigue.Offer(_context, _me, round, units);
        private PoliticalIntrigueState State => PoliticalIntrigue.State(_store)!;
        private IObjective Marker => _context.TableState.Objectives.Objects.Single();

        [Test]
        public void OnlyGrimdarkFutureAndAgeOfFantasyHaveIt()
        {
            Assert.That(PoliticalIntrigue.BooksHaveIt(EGameSystem.GrimdarkFuture) && PoliticalIntrigue.BooksHaveIt(EGameSystem.AgeOfFantasy));
            Assert.That(PoliticalIntrigue.BooksHaveIt(EGameSystem.GFFirefight) || PoliticalIntrigue.BooksHaveIt(EGameSystem.AoFSkirmish), Is.False);
            GameSettings s = GameSettings.GetDefault();
            s.AdvancedRules = EAdvancedRules.PoliticalDirectives;
            s.GameSystem = EGameSystem.GFFirefight;
            Assert.That(AdvancedRules.IsOn(s, EAdvancedRules.PoliticalDirectives), Is.False);
            Assert.That(PoliticalIntrigue.DirectiveText.Values.All(t => t.All(ch => ch < 128)), "ASCII");
            Assert.That(AdvancedRules.Chapters(EGameSystem.AgeOfFantasy), Does.Contain(AdvancedRules.PoliticalIntrigueChapter));
        }

        [Test]
        public async Task Influence_MakesAHeldObjectiveNeutral()
        {
            Build(PoliticalIntrigue.Influence);
            Marker.SetOwner(_them);
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Used));
            Assert.That(Marker.OwnerID, Is.Null);
        }

        [Test]
        public async Task Propaganda_AFailedTest_IsShaken()
        {
            Build(PoliticalIntrigue.Propaganda, face: 1);
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Used));
            Assert.That(_theirUnit.Tokens.HasToken(TokenType.Shaken), Is.True);

            Build(PoliticalIntrigue.Propaganda, face: 6);
            await Offer();
            Assert.That(_theirUnit.Tokens.HasToken(TokenType.Shaken), Is.False, "passed");
        }

        [Test]
        public async Task Recruitment_RestoresD3()
        {
            Build(PoliticalIntrigue.Recruitment, face: 6);
            foreach (ModelData m in _myUnit.Models.OfType<ModelData>()) m.DealWounds(m.TotalWounds);
            ((ModelData)_myUnit.Models[0]).DealWounds(-1f);   // one left standing
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Used));
            Assert.That(_myUnit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(3), "a 6 on the D3 roll = 3 restored");
        }

        [Test]
        public async Task Industry_SetsUpOneShot_AtAnEnemyWithin12()
        {
            Build(PoliticalIntrigue.Industry);
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Shoot));
            var shot = PoliticalIntrigue.TakePendingShot(_context);
            Assert.That(shot, Is.Not.Null);
            Assert.That(shot!.Value.Shooter.GetValue(), Is.SameAs(_myUnit));
            Assert.That(shot.Value.Target, Is.EqualTo(_theirUnit.ID));
            Assert.That(PoliticalIntrigue.TakePendingShot(_context), Is.Null, "taken once");

            Build(PoliticalIntrigue.Industry);
            _theirUnit.Models.OfType<ModelData>().Single().SetPosition(new Position(20f, 40f));   // 19.5" away
            Assert.That(PoliticalIntrigue.TargetCount(_context, _me, PoliticalIntrigue.Industry), Is.EqualTo(0), "over 12\"");
        }

        [Test]
        public async Task TwicePerRound_EachDirectiveOnce_ThenANewRound()
        {
            Build(PoliticalIntrigue.Propaganda, face: 6);
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Used));
            _asked.Clear();
            Assert.That(await Offer(), Is.Not.EqualTo(PoliticalIntrigue.EOutcome.Used), "Propaganda is used this round");
            var q = _asked.OfType<StringSelectionRequest>().Single();
            Assert.That(q.ValidOptions.Any(o => o.StartsWith(PoliticalIntrigue.Propaganda)), Is.False);

            Build(PoliticalIntrigue.ActivateUnit);
            var used = new PoliticalIntrigueState { Round = 1 };
            used.Used[_me.ToString()] = new List<string> { PoliticalIntrigue.Influence, PoliticalIntrigue.Propaganda };
            PoliticalIntrigue.Save(_context, used);
            _asked.Clear();
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.None));
            Assert.That(_asked, Is.Empty, "two used: not asked again this round");
            await Offer(round: 2);
            Assert.That(_asked, Is.Not.Empty, "a new round, a new offer");
        }

        [Test]
        public async Task NoMoreThisRound_StopsTheQuestions_AndNoUnitsLeft_MeansNoDirectives()
        {
            Build(PoliticalIntrigue.NoMoreThisRound);
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.None));
            _asked.Clear();
            await Offer();
            Assert.That(_asked, Is.Empty);

            Build(PoliticalIntrigue.Influence);
            Marker.SetOwner(_them);
            Assert.That(await Offer(units: false), Is.EqualTo(PoliticalIntrigue.EOutcome.None));
            Assert.That(Marker.OwnerID, Is.Not.Null, "\"players may not use directives if they've run out of units to activate\"");
        }

        [Test]
        public void TheBot_PrefersAFreeShot_ThenTheEnemysObjective()
        {
            Build();
            var all = PoliticalIntrigue.Order.ToList();
            Assert.That(PoliticalIntrigue.BotPick(_context, _me, all), Is.EqualTo(PoliticalIntrigue.Industry));
            var noShot = all.Where(d => d != PoliticalIntrigue.Industry).ToList();
            Assert.That(PoliticalIntrigue.BotPick(_context, _me, noShot), Is.EqualTo(PoliticalIntrigue.Recruitment).Or.EqualTo(PoliticalIntrigue.Propaganda),
                "no enemy objective: Influence isn't worth it");
            Marker.SetOwner(_them);
            Assert.That(PoliticalIntrigue.BotPick(_context, _me, noShot), Is.EqualTo(PoliticalIntrigue.Influence));
            Marker.SetOwner(_me);
            Assert.That(PoliticalIntrigue.BotPick(_context, _me, new[] { PoliticalIntrigue.Influence, PoliticalIntrigue.Mobilization }), Is.Null,
                "never its own objective, never a planless move");
        }
    }
}
