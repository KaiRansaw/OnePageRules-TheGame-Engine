using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Army Terrain, part 1 - the framework and the Battle Terrain (GF p32, AoF p32, GFF p30, AoFS p30).</summary>
    [TestFixture]
    public class ArmyTerrainTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private UnitData _myUnit = null!, _theirUnit = null!;
        private readonly Dictionary<int, ZoneRect> _zones = new() { [0] = new ZoneRect(0f, 72f, 0f, 12f), [1] = new ZoneRect(0f, 72f, 36f, 48f) };

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

        private sealed class FaceSequence : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public FaceSequence(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_faces[Math.Min(_i++, _faces.Length - 1)]);
        }

        /// <summary><paramref name="pick"/> = the piece each player picks (null = roll); placements go to the band's middle.</summary>
        private void Build(string? pick = null, IDiceRoller? roller = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = EAdvancedRules.ArmyTerrain;
            _context = new TestGameContext(_store, roller ?? new FixedFaceDiceRoller(4), settings: settings,
                playerRequester: new Answer(r => r switch
                {
                    StringSelectionRequest q when pick != null => q.ValidOptions.FirstOrDefault(o => o.StartsWith(pick)) ?? q.ValidOptions[0],
                    StringSelectionRequest q => q.Recommended ?? q.ValidOptions[0],
                    PlaceObjectiveRequest p => new Position((p.LegalBand.Left + p.LegalBand.Right) / 2f, (p.LegalBand.Bottom + p.LegalBand.Top) / 2f),
                    _ => throw new InvalidOperationException(r.GetType().Name),
                }));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
            _store.Create(new ObjectiveData(new Position(36f, 24f), _store));
            _myUnit = Unit(_me, new Position(30f, 6f), 3);
            _theirUnit = Unit(_them, new Position(30f, 42f), 1);
        }

        private UnitData Unit(PlayerID owner, Position at, int models)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i * 1.5f, at.z), _store))));
            var unit = new UnitData(owner, "Unit", quality: 4, defense: 4, modelBindings: bindings);
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(unit)) }));
            return unit;
        }

        private ArmyTerrainState State => ArmyTerrain.State(_store)!;
        private ArmyPiece Mine => State.Pieces.Single(p => p.Owner == _me.ID.ToString());
        private void MoveTo(UnitData u, float x, float z) { int i = 0; foreach (ModelData m in u.Models.OfType<ModelData>()) m.SetPosition(new Position(x + 1.5f * i++, z)); }

        [Test]
        public void TheBattleTerrain_IsTheBooks()
        {
            Assert.That(ArmyTerrain.Battle.Select(b => b.Name), Is.EqualTo(new[] { "Army Banner", "Battle Forge", "Defensive Barrier", "Icon of War", "Protective Dome", "Watch Tower" }));
            Assert.That(ArmyTerrain.Built, Does.Not.Contain(ArmyTerrain.DefensiveBarrier));
            Assert.That(ArmyTerrain.Battle.All(b => b.Text.All(ch => ch < 128)), "ASCII");
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.Chapters(s), Does.Contain(AdvancedRules.ArmyTerrainChapter), s.ToString());
        }

        [Test]
        public async Task EachArmy_PlacesItsPiece_InItsZone_AwayFromObjectivesAndEdges()
        {
            Build(ArmyTerrain.BattleForge);
            await ArmyTerrain.Place(_context, _zones);
            Assert.That(State.Pieces, Has.Count.EqualTo(2));
            foreach (ArmyPiece p in State.Pieces)
            {
                Assert.That(p.X, Is.InRange(7.5f, 72f - 7.5f));
                Assert.That(p.Z, Is.InRange(7.5f, 48f - 7.5f), "over 6\" from the table edges");
                Assert.That(Position.GetDistance2D(new Position(p.X, p.Z), new Position(36f, 24f)), Is.GreaterThanOrEqualTo(7.5f));
            }
            Assert.That(Mine.Z, Is.LessThanOrEqualTo(12f), "in its own zone");
            var pieces = _context.TableState.Terrain.Objects.ToList();
            Assert.That(pieces, Has.Count.EqualTo(2));
            Assert.That(pieces.All(t => t.TerrainType.HasFlag(ETerrainType.Impassible) && t.TerrainType.HasFlag(ETerrainType.Blocking)), "Impassable and Sight Blocking");
        }

        [Test]
        public async Task NotWithin6OfAnObjective_EvenIfAskedThere()
        {
            Build(ArmyTerrain.BattleForge);
            _store.Create(new ObjectiveData(new Position(36f, 9f), _store));   // right where the band's middle is
            await ArmyTerrain.Place(_context, _zones);
            Assert.That(Position.GetDistance2D(new Position(Mine.X, Mine.Z), new Position(36f, 9f)), Is.GreaterThanOrEqualTo(7.5f));
        }

        [Test]
        public async Task Rolling_SkipsWhatIsNotBuiltYet()
        {
            Build(roller: new FaceSequence(3, 2, 6));   // Defensive Barrier -> again: Battle Forge; the bot: Watch Tower
            await ArmyTerrain.Place(_context, _zones);
            Assert.That(Mine.Name, Is.EqualTo(ArmyTerrain.BattleForge));
            Assert.That(State.Pieces.Single(p => p != Mine).Name, Is.EqualTo(ArmyTerrain.WatchTower));
        }

        [Test]
        public async Task BattleForge_PlusOneShooting_ForAUnitThatActivatedNearIt()
        {
            Build(ArmyTerrain.BattleForge);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 4f, Mine.Z);
            ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: false, spell: false, charging: false).Sum(m => m.Delta), Is.EqualTo(1));
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: true, spell: false, charging: true), Is.Empty, "shooting only");
            ArmyTerrain.OnActivation(_context, _theirUnit);
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: false, spell: false, charging: false), Is.Empty,
                "only the unit activating now");
            MoveTo(_myUnit, Mine.X + 12f, Mine.Z);
            ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: false, spell: false, charging: false), Is.Empty, "over 6\"");
        }

        [Test]
        public async Task IconOfWar_12Inches_MeleeWhenCharging_AndArmyBanner_Moves()
        {
            Build(ArmyTerrain.IconOfWar);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 11f, Mine.Z);
            ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: true, spell: false, charging: true).Sum(m => m.Delta), Is.EqualTo(1));
            Assert.That(ArmyTerrain.HitModifiers(_context, _myUnit, melee: true, spell: false, charging: false), Is.Empty);

            Build(ArmyTerrain.ArmyBanner);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 4f, Mine.Z);
            ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(ArmyTerrain.MoveChange(_context, _myUnit), Is.EqualTo((2f, 4f)));
            Assert.That(ArmyTerrain.MoveChange(_context, _theirUnit), Is.EqualTo((0f, 0f)));
        }

        [Test]
        public async Task ProtectiveDome_FriendsOnly_MostModels()
        {
            Build(ArmyTerrain.ProtectiveDome);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 3f, Mine.Z);
            Assert.That(ArmyTerrain.InFriendlyDome(_context, _myUnit), Is.True);
            MoveTo(_theirUnit, Mine.X + 3f, Mine.Z + 2f);
            Assert.That(ArmyTerrain.InFriendlyDome(_context, _theirUnit), Is.False, "the enemy's dome isn't this one");
            MoveTo(_myUnit, Mine.X + 7f, Mine.Z);   // gaps 5", 6.5", 8": only 1 of 3 inside
            Assert.That(ArmyTerrain.InFriendlyDome(_context, _myUnit), Is.False, "most models must be inside");
        }

        [Test]
        public async Task WatchTower_IsDangerous_ForEnemiesOnly()
        {
            Build(ArmyTerrain.WatchTower);
            await ArmyTerrain.Place(_context, _zones);
            var forThem = ArmyTerrain.EnemyDangerous(_context, _theirUnit);
            Assert.That(forThem, Has.Count.EqualTo(1), "my tower is dangerous for them");
            Assert.That(forThem[0].IsPointWithinZone(new Float2(Mine.X + 7f, Mine.Z)), Is.True, "6\" from the piece's edge");
            Assert.That(forThem[0].IsPointWithinZone(new Float2(Mine.X + 8f, Mine.Z)), Is.False);
            Assert.That(ArmyTerrain.EnemyDangerous(_context, _myUnit).Count, Is.EqualTo(1), "and theirs is for me");
            Assert.That(ArmyTerrain.EnemyDangerous(_context, _myUnit)[0].IsPointWithinZone(new Float2(Mine.X, Mine.Z)), Is.False);
        }
    }
}
