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
        private void Build(string? directive = null, int face = 4, EGameSystem system = EGameSystem.GrimdarkFuture,
            EAdvancedRules flags = EAdvancedRules.PoliticalDirectives, string? politician = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = flags;
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: new Answer(r =>
                {
                    _asked.Add(r);
                    if (r is StringSelectionRequest q)
                    {
                        if (politician != null && q.DisplayName == "Political Intrigue - politician")
                            return q.ValidOptions.FirstOrDefault(o => o.StartsWith(politician)) ?? q.ValidOptions[0];
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
        // ================================================================= politicians

        private void Politicians(params string[] names)
        {
            var st = PoliticalIntrigue.State(_store) ?? new PoliticalIntrigueState { Round = 1 };
            st.Playstyle = 1;
            st.Politicians[_me.ToString()] = names.ToList();
            PoliticalIntrigue.Save(_context, st);
        }

        private void BuildP(string directive, string politician, int face = 4) =>
            Build(directive, face, flags: EAdvancedRules.Politicians, politician: politician);

        [Test]
        public void ThePoliticians_AreTheBooks()
        {
            Assert.That(PoliticalIntrigue.PoliticianList, Has.Count.EqualTo(10));
            Assert.That(PoliticalIntrigue.PoliticianList.Where(p => p.OncePerGame).Select(p => p.Name),
                Is.EquivalentTo(new[] { "Warmonger", "Negotiator", "Manipulator", "Escalator" }));
            Assert.That(PoliticalIntrigue.PoliticianNamed("Trader"), Has.Property("TypeA").EqualTo("Propaganda").And.Property("TypeB").EqualTo("Industry"));
            Assert.That(PoliticalIntrigue.PoliticianList.All(p => p.Text.All(ch => ch < 128)), "ASCII");
            var pairs = PoliticalIntrigue.PoliticianList.Select(p => p.TypeA + "/" + p.TypeB).ToList();
            Assert.That(pairs.Distinct().Count(), Is.EqualTo(10), "every pair of the five directives once");
            var resolver = FDG.Rules.Dispatch.CoreRuleCatalog.CreateResolver();
            foreach (string rule in new[] { FDG.Rules.Dispatch.CampaignEventRuleCatalog.TraderName,
                         FDG.Rules.Dispatch.CampaignEventRuleCatalog.WardenName, FDG.Rules.Dispatch.CampaignEventRuleCatalog.DeceiverName })
                Assert.That(resolver.TryResolve(rule, out _), Is.True, rule + " must resolve, or its grant does nothing");
        }

        [Test]
        public async Task BeforeTheGame_ThePlaystyleIsRolled_AndBotsPickTwo()
        {
            Build(face: 5, flags: EAdvancedRules.Politicians);
            await PoliticalIntrigue.Settle(_context);
            Assert.That(State.Playstyle, Is.EqualTo(3), "a 5 on the D3 roll = Hidden");
            Assert.That(State.Politicians[_me.ToString()], Is.EqualTo(new[] { "Mastermind", "Warmonger" }));
            Assert.That(State.Hidden[_them.ToString()], Has.Count.EqualTo(2));
            Assert.That(PoliticalIntrigue.VisiblePoliticians(State, _them, new[] { _me }), Is.All.EqualTo("a secret politician"));
            Assert.That(PoliticalIntrigue.VisiblePoliticians(State, _them, new[] { _them }), Has.Some.StartsWith("Mastermind"));
            Assert.That(PoliticalIntrigue.On(_context), Is.True, "politicians bring the directives with them");
        }

        [Test]
        public async Task Mastermind_OnInfluence_PlusOneNearTheObjective_ForMySideOnly()
        {
            BuildP(PoliticalIntrigue.Influence, "Mastermind");
            Politicians("Mastermind");
            Marker.SetOwner(_them);
            await Offer();
            _theirUnit.Models.OfType<ModelData>().Single().SetPosition(new Position(41f, 24f));
            Assert.That(PoliticalIntrigue.HitModifiers(_context, _myUnit, _theirUnit, spell: false).Sum(m => m.Delta), Is.EqualTo(1));
            foreach (ModelData m in _myUnit.Models.OfType<ModelData>()) m.SetPosition(new Position(39f + m.Position.x / 100f, 25f));
            Assert.That(PoliticalIntrigue.HitModifiers(_context, _theirUnit, _myUnit, spell: false), Is.Empty, "the enemy's attacks don't get it");
            _theirUnit.Models.OfType<ModelData>().Single().SetPosition(new Position(20f, 30f));
            Assert.That(PoliticalIntrigue.HitModifiers(_context, _myUnit, _theirUnit, spell: false), Is.Empty, "not within 3\"");
            Assert.That(State.PoliticiansUsed[_me.ToString()], Does.Contain("Mastermind"));
        }

        [Test]
        public async Task Warmonger_D3Wounds_OncePerGame()
        {
            BuildP(PoliticalIntrigue.Propaganda, "Warmonger", face: 6);
            _theirUnit.Models.OfType<ModelData>().Single().SetMaxWounds(6);
            Politicians("Warmonger");
            await Offer();
            Assert.That(_theirUnit.Models.Single().WoundsDealt, Is.EqualTo(3f), "a 6 = 3 wounds");
            State.PoliticiansUsed.Clear();   // a later round
            Assert.That(PoliticalIntrigue.Eligible(State, _me, PoliticalIntrigue.Influence), Is.Empty, "once per game");
        }

        [Test]
        public async Task Trader_Warden_Visionary_Escalator()
        {
            BuildP(PoliticalIntrigue.Industry, "Trader");
            Politicians("Trader");
            Assert.That(await Offer(), Is.EqualTo(PoliticalIntrigue.EOutcome.Shoot));
            Assert.That(_myUnit.Tokens.GetAllTokens(TokenType.RuleGrant).Any(t => t.Payload is FDG.Rules.Tokens.TokenPayload.RuleGrant g
                && g.RuleName == FDG.Rules.Dispatch.CampaignEventRuleCatalog.TraderName), Is.True);

            BuildP(PoliticalIntrigue.Propaganda, "Trader", face: 6);
            Politicians("Trader");
            await Offer();
            Assert.That(_theirUnit.Tokens.HasToken(TokenType.Mark), Is.True, "the next attack on it gets AP(+1)");

            BuildP(PoliticalIntrigue.Recruitment, "Warden");
            ((ModelData)_myUnit.Models[0]).DealWounds(1f);
            Politicians("Warden");
            await Offer();
            Assert.That(_myUnit.Tokens.GetAllTokens(TokenType.RuleGrant).Any(t => t.Payload is FDG.Rules.Tokens.TokenPayload.RuleGrant g
                && g.RuleName == FDG.Rules.Dispatch.CampaignEventRuleCatalog.WardenName), Is.True);

            BuildP(PoliticalIntrigue.Recruitment, "Visionary");
            ((ModelData)_myUnit.Models[0]).DealWounds(1f);
            _myUnit.Tokens.AddToken(FDG.Rules.Tokens.TokenDefinitionCatalog.Create(TokenType.Shaken));
            Politicians("Visionary");
            await Offer();
            Assert.That(_myUnit.Tokens.HasToken(TokenType.Shaken), Is.False);

            BuildP(PoliticalIntrigue.Industry, "Escalator");
            Politicians("Escalator");
            await Offer();
            Assert.That(State.SecondShot, Is.EqualTo(_myUnit.ID.ID.ToString()));
        }

        [Test]
        public async Task Deceiver_OnInfluence_DangerousArea_UntilTheRoundEnds()
        {
            BuildP(PoliticalIntrigue.Influence, "Deceiver");
            Politicians("Deceiver");
            Marker.SetOwner(_them);
            await Offer();
            Assert.That(_context.TableState.Terrain.Objects.Count(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)
                && t.TerrainType.HasFlag(ETerrainType.Difficult)), Is.EqualTo(1));
            PoliticalIntrigue.RoundEnd(_context);
            Assert.That(_context.TableState.Terrain.Objects, Is.Empty);
        }

        [Test]
        public async Task NoPolitician_KeepsItSecret_AndUnused()
        {
            BuildP(PoliticalIntrigue.Influence, PoliticalIntrigue.NoPolitician);
            Politicians("Mastermind");
            State.Hidden[_me.ToString()] = new List<string> { "Mastermind" };
            Marker.SetOwner(_them);
            await Offer();
            Assert.That(State.Hidden[_me.ToString()], Does.Contain("Mastermind"));
            Assert.That(State.MastermindAreas, Is.Empty);

            BuildP(PoliticalIntrigue.Influence, "Mastermind");
            Politicians("Mastermind");
            State.Hidden[_me.ToString()] = new List<string> { "Mastermind" };
            Marker.SetOwner(_them);
            await Offer();
            Assert.That(State.Hidden[_me.ToString()], Is.Empty, "revealed when used for the first time");
        }
    }
}
