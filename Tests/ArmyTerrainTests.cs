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
        private int _keepQuestions;
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
        private void Build(string? pick = null, IDiceRoller? roller = null, bool gate = false)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.AdvancedRules = EAdvancedRules.ArmyTerrain;
            _context = new TestGameContext(_store, roller ?? new FixedFaceDiceRoller(4), settings: settings,
                playerRequester: new Answer(r => (r is StringSelectionRequest { DisplayName: "Army Terrain - keep" } ? ++_keepQuestions : 0) switch
                {
                    _ => r switch
                {
                    StringSelectionRequest q when pick != null => q.ValidOptions.FirstOrDefault(o => o.StartsWith(pick)) ?? q.ValidOptions[0],
                    StringSelectionRequest q => q.Recommended ?? q.ValidOptions[0],
                    YesNoRequest => gate,
                    PlaceObjectsRequest<ModelData> p => new Selected<List<PlacedObjectEntry<ModelData>>>(p.ModelsToPlace
                        .Select((b, i) => new PlacedObjectEntry<ModelData>(b, new Position(((CircularZone)p.DeploymentZone).Center.X + 2.5f + i * 1.1f, ((CircularZone)p.DeploymentZone).Center.Y))).ToList()),
                    PlaceObjectiveRequest p => new Position((p.LegalBand.Left + p.LegalBand.Right) / 2f, (p.LegalBand.Bottom + p.LegalBand.Top) / 2f),
                    _ => throw new InvalidOperationException(r.GetType().Name),
                }}));
            _keepQuestions = 0;
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
            Assert.That(ArmyTerrain.Relic(EGameSystem.GrimdarkFuture).Select(b => b.Name),
                Is.EqualTo(new[] { "Arcane Pillars", "Enchanted Shrine", "Faction Relic", "Healing Well", "Magic Armory", "Twin Jumpgates" }));
            Assert.That(ArmyTerrain.Built, Does.Contain(ArmyTerrain.DefensiveBarrier));
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
        public async Task Rolling_OneDieEachTable_KeepOne()
        {
            Build(roller: new FaceSequence(3, 2, 6, 6));   // me: Defensive Barrier + Enchanted Shrine; the bot: Watch Tower + the gates
            await ArmyTerrain.Place(_context, _zones);
            Assert.That(_keepQuestions, Is.EqualTo(2), "each player keeps one of its two");
            Assert.That(Mine.Name, Is.EqualTo(ArmyTerrain.EnchantedShrine), "the recommended keep");
            Assert.That(State.Pieces.Single(p => p != Mine).Name, Is.EqualTo(ArmyTerrain.WatchTower), "the bot keeps the one it prefers");
        }

        [Test]
        public void TheSecondTable_IsNamedPerBook()
        {
            Assert.That(ArmyTerrain.SecondTable(EGameSystem.GrimdarkFuture), Is.EqualTo("Relic Terrain"));
            Assert.That(ArmyTerrain.SecondTable(EGameSystem.AoFSkirmish), Is.EqualTo("Magic Terrain"));
            Assert.That(ArmyTerrain.Relic(EGameSystem.GFFirefight).Last().Name, Is.EqualTo("Twin Jumpgates"));
            Assert.That(ArmyTerrain.Relic(EGameSystem.AgeOfFantasy).Last().Name, Is.EqualTo("Twin Voidgates"));
            Assert.That(ArmyTerrain.Relic(EGameSystem.AgeOfFantasy).All(r => r.Text.All(ch => ch < 128)), "ASCII");
        }

        [Test]
        public async Task TwoCopies_WithinTheirDistance()
        {
            Build(ArmyTerrain.ArcanePillars);
            await ArmyTerrain.Place(_context, _zones);
            Assert.That(Mine.X2, Is.Not.Null);
            Assert.That(Position.GetDistance2D(new Position(Mine.X, Mine.Z), new Position(Mine.X2!.Value, Mine.Z2!.Value)) - 3f,
                Is.LessThanOrEqualTo(12.01f), "within 12\" of each other, edge to edge");
            Assert.That(_context.TableState.Terrain.Objects.Count(), Is.EqualTo(4), "two pieces each");
        }

        [Test]
        public async Task ArcanePillars_D3Wounds_ToAnEnemyEndingOnTheLine()
        {
            Build(ArmyTerrain.ArcanePillars, new FixedFaceDiceRoller(6));
            await ArmyTerrain.Place(_context, _zones);
            _theirUnit.Models.OfType<ModelData>().Single().SetMaxWounds(6);
            float mx = (Mine.X + Mine.X2!.Value) / 2f, mz = (Mine.Z + Mine.Z2!.Value) / 2f;
            MoveTo(_theirUnit, mx, mz);
            await ArmyTerrain.EndOfActivation(_context, _theirUnit);
            Assert.That(_theirUnit.Models.Single().WoundsDealt, Is.EqualTo(3f));
            MoveTo(_myUnit, mx, mz);
            await ArmyTerrain.EndOfActivation(_context, _myUnit);
            Assert.That(_myUnit.Models.All(m => m.WoundsDealt == 0f), "not its own pillars");
        }

        [Test]
        public async Task TwinGates_AFriendlyUnitNearOne_MayStepToTheOther()
        {
            Build(ArmyTerrain.TwinJumpgates, gate: true);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 3f, Mine.Z);
            await ArmyTerrain.EndOfActivation(_context, _myUnit);
            var far = new Position(Mine.X2!.Value, Mine.Z2!.Value);
            Assert.That(_myUnit.Models.All(m => Position.GetDistance2D(m.Position, far) <= 6f), "now by the other gate");

            Build(ArmyTerrain.TwinJumpgates, gate: false);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 3f, Mine.Z);
            await ArmyTerrain.EndOfActivation(_context, _myUnit);
            Assert.That(Position.GetDistance2D(_myUnit.Models[0].Position, new Position(Mine.X + 3f, Mine.Z)), Is.LessThan(0.01f), "\"may\" - said no");
        }

        private List<ModelMoveEntry> Straight(UnitData unit, float fromX, float toX, float z) =>
            unit.ModelBindings.Take(1).Select(b => { b.GetValue().SetPosition(new Position(fromX, z));
                return new ModelMoveEntry(b, new List<Position> { new(fromX, z), new(toX, z) }); }).ToList();

        [Test]
        public async Task DefensiveBarrier_IsDifficult_ForTheEnemyOnly()
        {
            Build(ArmyTerrain.DefensiveBarrier);
            await ArmyTerrain.Place(_context, _zones);
            var terrain = _context.TableState.Terrain.Objects.ToList();
            ITerrain area = terrain.Single(t => ArmyTerrain.BarrierTeam(t) == 0);
            Assert.That(area.TerrainType.HasFlag(ETerrainType.Difficult), Is.True);
            Assert.That(area.IsPointWithinZone(new Float2(Mine.X + 7f, Mine.Z)), Is.True, "6\" from the piece's edge");

            // A 10" move through the area (beside the piece itself): the enemy is capped at 6", my own unit isn't.
            float z = Mine.Z + 3f;
            bool theirs = MovementUtilities.ValidatePaths(Straight(_theirUnit, Mine.X - 5f, Mine.X + 5f, z), 12f,
                Array.Empty<EnemyModelFootprint>(), false, false, false, terrain, out var theirErrors);
            Assert.That(theirs, Is.False);
            Assert.That(theirErrors.Any(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit), Is.True);
            bool mine = MovementUtilities.ValidatePaths(Straight(_myUnit, Mine.X - 5f, Mine.X + 5f, z), 12f,
                Array.Empty<EnemyModelFootprint>(), false, false, false, terrain, out var myErrors);
            Assert.That(myErrors.Where(e => e.ErrorReasonType == EErrorReasonType.ExceededDifficultTerrainMoveLimit), Is.Empty,
                "its own side's units ignore it");
        }

        [Test]
        public async Task DefensiveBarrier_AModelTheGameHasntSeen_TreatsItAsDifficult()
        {
            Build(ArmyTerrain.DefensiveBarrier);
            await ArmyTerrain.Place(_context, _zones);
            ITerrain area = _context.TableState.Terrain.Objects.Single(t => ArmyTerrain.BarrierTeam(t) == 0);
            var stranger = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            Assert.That(ArmyTerrain.FriendlyBarrier(area, stranger), Is.False, "unknown = stricter, never looser");
            Assert.That(ArmyTerrain.FriendlyBarrier(area, _myUnit.Models[0]), Is.True);
            Assert.That(ArmyTerrain.FriendlyBarrier(area, _theirUnit.Models[0]), Is.False);
        }

        [Test]
        public async Task HealingWell_Shrine_Relic_Armory()
        {
            Build(ArmyTerrain.HealingWell, new FixedFaceDiceRoller(6));
            await ArmyTerrain.Place(_context, _zones);
            foreach (ModelData m in _myUnit.Models.OfType<ModelData>().Skip(1)) m.DealWounds(m.TotalWounds);
            MoveTo(_myUnit, Mine.X + 4f, Mine.Z);
            await ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(_myUnit.Models.Count(m => m.GetIsAlive()), Is.EqualTo(3));

            Build(ArmyTerrain.EnchantedShrine);
            await ArmyTerrain.Place(_context, _zones);
            _myUnit.AttachRuleDefinition(new FDG.Rules.Dispatch.ResolvedRule("Caster", FDG.Rules.Dispatch.CoreRuleCatalog.Caster));
            MoveTo(_myUnit, Mine.X + 4f, Mine.Z);
            await ArmyTerrain.OnActivation(_context, _myUnit);
            Assert.That(_myUnit.Tokens.GetTokenCount(FDG.Rules.Foundation.TokenType.SpellTokens), Is.EqualTo(1));
            await ArmyTerrain.OnActivation(_context, _theirUnit);
            Assert.That(_theirUnit.Tokens.GetTokenCount(FDG.Rules.Foundation.TokenType.SpellTokens), Is.EqualTo(0), "not a Caster");

            Build(ArmyTerrain.FactionRelic);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 11f, Mine.Z);
            Assert.That(ArmyTerrain.MoraleModifier(_context, _myUnit), Is.EqualTo(1), "within 12\"");
            MoveTo(_myUnit, Mine.X + 15f, Mine.Z);
            Assert.That(ArmyTerrain.MoraleModifier(_context, _myUnit), Is.EqualTo(0));

            Build(ArmyTerrain.MagicArmory);
            await ArmyTerrain.Place(_context, _zones);
            MoveTo(_myUnit, Mine.X + 4f, Mine.Z);
            Assert.That(ArmyTerrain.SaveModifiers(_context, _myUnit).Sum(m => m.Delta), Is.EqualTo(1));
            MoveTo(_theirUnit, Mine.X + 4f, Mine.Z + 2f);
            Assert.That(ArmyTerrain.SaveModifiers(_context, _theirUnit), Is.Empty, "friendly units only");
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
