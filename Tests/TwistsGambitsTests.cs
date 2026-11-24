using FDG.Data;
using FDG.Missions;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Twists &amp; Gambits (Advanced Rulebooks 3.6.0: GF p44-45, GFF p41-42, AoF p44-45, AoFS p41-42) - the
    /// mission, its scoring, the underdog and the twists. Played-game check: the bot sweep.</summary>
    [TestFixture]
    public class TwistsGambitsTests
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

        private void Build(IDiceRoller roller, EGameSystem system = EGameSystem.GrimdarkFuture)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.ExtraMission = EExtraMission.TwistsAndGambits;
            _context = new TestGameContext(_store, roller, settings: settings,
                playerRequester: new Answer(r => { _asked.Add(r); return r is StringSelectionRequest q ? q.Recommended ?? q.ValidOptions[0] : true; }));
            _asked.Clear();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
            _myUnit = Unit(_me, "Mine", new Position(10f, 6f));
            _theirUnit = Unit(_them, "Theirs", new Position(10f, 40f));
            // Objectives 1-3 outside the zones, and one in each zone (Kai's at the bottom, the bot's at the top).
            foreach (float x in new[] { 12f, 36f, 60f }) _store.Create(new ObjectiveData(new Position(x, 24f), _store));
            _store.Create(new ObjectiveData(new Position(36f, 4f), _store));
            _store.Create(new ObjectiveData(new Position(36f, 44f), _store));
            ExtraMissionState s = ExtraMissions.StateFor(_context);
            s.Twists = new TwistsGambitsState
            {
                Numbered = new() { "12.00,24.00", "36.00,24.00", "60.00,24.00" },
                ZoneMarker = new() { [0] = "36.00,4.00", [1] = "36.00,44.00" },
            };
            ExtraMissions.SaveState(_context, s);
        }

        private UnitData Unit(PlayerID owner, string name, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            var unit = new UnitData(owner, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) }) { PointCost = 100 };
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(unit)) }));
            return unit;
        }

        private static void Kill(UnitData unit)
        {
            foreach (IModel model in unit.Models) ((ModelData)model).DealWounds(((ModelData)model).TotalWounds);
        }

        private IObjective Objective(string key) => _context.TableState.Objectives.Objects.Single(o => TwistsGambits.Key(o.Position) == key);
        private ExtraMissionState State => ExtraMissions.StateFor(_context);
        private TwistsGambitsState Twists => State.Twists!;
        private void Vp(int mine, int theirs) { ExtraMissionState s = State; s.Vp[0] = mine; s.Vp[1] = theirs; ExtraMissions.SaveState(_context, s); }
        private Task End() => TwistsGambits.RoundEnd(_context, State, 1);
        private int VpOf(int slot) => State.Vp.GetValueOrDefault(slot);

        [Test]
        public void TheTwists_AreTheBooks()
        {
            Assert.That(TwistsGambits.Twists.Keys, Is.EquivalentTo(new[] { "A1", "A2", "A3", "A4", "A5", "A6", "B1", "B2", "B3", "B4", "B5", "B6" }));
            Assert.That(TwistsGambits.Twists.Values.All(t => (t.Name + t.Text).All(ch => ch < 128)), "game text is ASCII");
            Assert.That(TwistsGambits.ObjectiveVp(EGameSystem.AoFSkirmish), Is.EqualTo(2), "AoFS p41: 2 VPs per objective");
            Assert.That(new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight }.Select(TwistsGambits.ObjectiveVp),
                Is.All.EqualTo(1));
            Assert.That(TwistsGambits.StrategicDivideWhenControlled(EGameSystem.AgeOfFantasy), Is.True, "AoF p45 prints 'when controlled'");
            Assert.That(TwistsGambits.StrategicDivideWhenControlled(EGameSystem.GrimdarkFuture), Is.False);
            Assert.That(ExtraMissionCatalog.LobbyOrder, Does.Contain(EExtraMission.TwistsAndGambits));
            Assert.That(ExtraMissionCatalog.TwistsAndGambits.Progressive, Is.True);
        }

        [Test]
        public async Task EqualVp_NoUnderdog_TheTwistIsIgnored()
        {
            Build(new FaceSequence(1, 3));
            Vp(2, 2);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Underdog, Is.EqualTo(-1));
            Assert.That(Twists.Twist, Is.EqualTo(""));
            Assert.That(_asked, Is.Empty, "nobody picks anything");
        }

        [Test]
        public async Task TheUnderdog_PicksWhatTheTwistHits_RustingHex()
        {
            Build(new FaceSequence(2, 3));   // 1-3: table A; 3: Rusting Hex
            Vp(0, 3);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Underdog, Is.EqualTo(0), "fewer VP as the round begins");
            Assert.That(Twists.Twist, Is.EqualTo("A3"));
            var q = _asked.OfType<StringSelectionRequest>().Single();
            Assert.That(q.TargetPlayerID, Is.EqualTo(_me), "the underdog picks");
            Assert.That(q.ValidOptions, Is.EqualTo(new[] { "Theirs" }), "a non-underdog unit");
            Assert.That(_theirUnit.Tokens.HasToken(RollModifierTokens.TypeFor(ERollKind.Save)), Is.True);
        }

        [Test]
        public async Task CurseOfSands_TakesTwoOffAdvance_AndFourOffRushAndCharge()
        {
            Build(new FaceSequence(1, 2));
            Vp(4, 1);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Underdog, Is.EqualTo(1));
            Assert.That(TwistsGambits.MoveChange(_context, _myUnit), Is.EqualTo((-2f, -4f)), "the bot cursed Kai's unit");
            Assert.That(TwistsGambits.MoveChange(_context, _theirUnit), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public async Task RoundScoring_KillsObjectivesAndMore()
        {
            Build(new FaceSequence(1, 1));
            Vp(0, 0);
            await TwistsGambits.RoundStart(_context, 1);   // equal: no twist
            Kill(_theirUnit);
            Objective("12.00,24.00").SetOwner(_me);
            Objective("36.00,24.00").SetOwner(_me);
            Objective("60.00,24.00").SetOwner(_them);
            await End();
            Assert.That(VpOf(0), Is.EqualTo(1 + 2 + 1), "1 destroyed, 2 objectives, more objectives");
            Assert.That(VpOf(1), Is.EqualTo(1), "1 objective");
            Assert.That(Twists.LastScore, Has.Count.EqualTo(2));
        }

        [Test]
        public async Task Skirmish_ScoresTwoPerObjective()
        {
            Build(new FaceSequence(1, 1), EGameSystem.AoFSkirmish);
            Vp(0, 0);
            await TwistsGambits.RoundStart(_context, 1);
            Objective("12.00,24.00").SetOwner(_them);
            await End();
            Assert.That(VpOf(1), Is.EqualTo(2 + 1));
        }

        [Test]
        public async Task TargetPriority_PlusTwo_ForHoldingIt()
        {
            Build(new FaceSequence(5, 2));   // 4-6: table B; 2: Target Priority
            Vp(0, 1);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Twist, Is.EqualTo("B2"));
            string picked = Twists.PickedObjectives.Single();
            Vp(0, 0);
            Objective(picked).SetOwner(_them);
            await End();
            Assert.That(VpOf(1), Is.EqualTo(1 + 1 + 2), "the objective, more objectives, +2 for the priority target");
        }

        [Test]
        public async Task AllOutAssault_PlusThree_ForSeizingTheEnemyZoneMarker()
        {
            Build(new FaceSequence(6, 3));
            Vp(0, 1);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Twist, Is.EqualTo("B3"));
            Vp(0, 0);
            Objective("36.00,44.00").SetOwner(_me);   // seized this round: neutral as the round began
            await End();
            Assert.That(VpOf(0), Is.EqualTo(1 + 1 + 3));

            // Held from the start of the round is not "seizing".
            Build(new FaceSequence(6, 3));
            Objective("36.00,44.00").SetOwner(_me);
            Vp(0, 1);
            await TwistsGambits.RoundStart(_context, 2);
            Vp(0, 0);
            await End();
            Assert.That(VpOf(0), Is.EqualTo(1 + 1));
        }

        [Test]
        public async Task ObjectiveDenied_GoesNeutral_CantBeSeized_DangerousUntilTheRoundEnds()
        {
            Build(new FaceSequence(4, 6));
            Objective("12.00,24.00").SetOwner(_them);
            Vp(0, 1);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Twist, Is.EqualTo("B6"));
            IObjective denied = Objective(Twists.PickedObjectives.Single());
            Assert.That(TwistsGambits.Key(denied.Position), Is.EqualTo("12.00,24.00"), "the bot denies one the enemy holds");
            Assert.That(denied.OwnerID, Is.Null);
            Assert.That(TwistsGambits.Denied(_context, denied), Is.True);
            Assert.That(_context.TableState.Terrain.Objects.Count(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)), Is.EqualTo(1));
            await End();
            Assert.That(_context.TableState.Terrain.Objects.Count(t => t.TerrainType.HasFlag(ETerrainType.Dangerous)), Is.EqualTo(0),
                "the Dangerous area lasts until the end of the round");
        }

        [Test]
        public async Task DuelOfFate_PlusOne_ToWhoeverDestroysAPickedUnit()
        {
            Build(new FaceSequence(3, 6));
            Vp(0, 2);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Twist, Is.EqualTo("A6"));
            Assert.That(Twists.PickedUnits, Has.Count.EqualTo(2));
            Vp(0, 0);
            Kill(_theirUnit);
            TwistsGambits.UnitDestroyed(_context, _theirUnit, _myUnit);
            await End();
            Assert.That(VpOf(0), Is.EqualTo(1 + 1), "1 destroyed + Duel of Fate");
        }

        [Test]
        public async Task FightingFrenzy_PlusOneToHit_AndPlusOnePerKill()
        {
            Build(new FaceSequence(1, 5));
            Vp(0, 2);
            await TwistsGambits.RoundStart(_context, 2);
            Assert.That(Twists.Twist, Is.EqualTo("A5"));
            Assert.That(_myUnit.Tokens.HasToken(RollModifierTokens.TypeFor(ERollKind.Hit)), Is.True);
            Vp(0, 0);
            Kill(_theirUnit);
            TwistsGambits.UnitDestroyed(_context, _theirUnit, _myUnit);
            await End();
            Assert.That(VpOf(0), Is.EqualTo(2));
        }
    }
}
