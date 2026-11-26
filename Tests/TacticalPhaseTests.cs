using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Tactical Phase (GFF / AoFS Advanced Rules 3.6.0 p46). Played-game check: the bot sweep.</summary>
    [TestFixture]
    public class TacticalPhaseTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private readonly List<object> _asked = new();
        private Func<object, object?>? _override;

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

        private object Reply(object r)
        {
            if (_override?.Invoke(r) is { } o) return o;
            return r switch
            {
                StringSelectionRequest q => q.Recommended ?? q.ValidOptions[0],
                PlaceObjectiveRequest p => new Position(12f + 8f * (p.MarkerIndex - 1), (p.LegalBand.Bottom + p.LegalBand.Top) / 2f),
                AssignWoundsRequest w => AutoFill(w),
                _ => true,
            };
        }

        private static object AutoFill(AssignWoundsRequest w) => w.GetType().GetMethod("AutoAssign")?.Invoke(w, null) ?? true;

        private void Build(EAdvancedRules rules, EGameSystem system = EGameSystem.GFFirefight, IDiceRoller? dice = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, dice ?? new FixedDiceRoller(4), settings: settings,
                playerRequester: new Answer(r => { _asked.Add(r); return Reply(r); }));
            _asked.Clear();
            _override = null;
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
        }

        private UnitData Unit(PlayerID owner, string name, Position at, int models = 1)
        {
            var bindings = Enumerable.Range(0, models)
                .Select(i => _store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(at.x + i, at.z), _store))))
                .ToList();
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings) { PointCost = 50 };
            _store.GetDataBinding<UnitData>(_store.Create(unit));
            return _context.TableState.Units.Objects.OfType<UnitData>().Last(u => u.Name == name);
        }

        private static readonly Dictionary<int, ZoneRect> Zones = new()
        {
            [0] = new ZoneRect(0f, 48f, 0f, 12f),
            [1] = new ZoneRect(0f, 48f, 36f, 48f),
        };

        private TacticalPhaseState S => TacticalPhase.State(_store)!;

        // ---------------------------------------------------------------- the lobby

        [Test]
        public void OnlyFirefightAndSkirmish_HaveIt_InTheirOwnChapter()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.In(AdvancedRules.TacticalPhaseChapter, s).Select(o => o.Name),
                    Is.EqualTo(new[] { TacticalPhase.ForwardPositionsName, TacticalPhase.BattlePreparationsName }));
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy })
                Assert.That(AdvancedRules.In(AdvancedRules.TacticalPhaseChapter, s), Is.Empty);
            Assert.That(TacticalPhase.BookText(TacticalPhase.BattlePreparationsName), Does.Contain("note them").Or.Contain("in secret"));
        }

        // ---------------------------------------------------------------- forward positions

        [Test]
        public async Task ForwardPositions_ThreeMarkersEach_OnOppositeEdges_AllLockedAtTheEnd()
        {
            Build(EAdvancedRules.ForwardPositions);
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(S.MarkersDone, Is.True);
            Assert.That(S.Markers.Count(m => m.Slot == 0), Is.EqualTo(3));
            Assert.That(S.Markers.Count(m => m.Slot == 1), Is.EqualTo(3));
            Assert.That(S.Markers.All(m => m.Locked), Is.True);
            Assert.That(S.Edges.Values, Is.EquivalentTo(new[] { TacticalPhase.EdgeBottom, TacticalPhase.EdgeTop }));
            // Every marker stays within 12" of another friendly one.
            foreach (ForwardMarker m in S.Markers)
                Assert.That(S.Markers.Any(o => o != m && o.Slot == m.Slot && Dist(o, m) <= 12.05f), Is.True, $"marker {m.Number}");
            // They are on the table as pieces with no rules.
            Assert.That(_context.TableState.Terrain.Objects.Count(TacticalPhase.IsMarker), Is.EqualTo(6));
            Assert.That(_context.TableState.Terrain.Objects.Where(TacticalPhase.IsMarker).All(t => t.TerrainType == ETerrainType.None));
        }

        [Test]
        public async Task ForwardPositions_PlacingOffTheEdge_IsRefused_AndAskedAgain()
        {
            Build(EAdvancedRules.ForwardPositions);
            int wrong = 1;
            _override = r => r is PlaceObjectiveRequest && wrong-- > 0 ? new Position(24f, 24f) : null;
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(_asked.OfType<PlaceObjectiveRequest>().Count(), Is.EqualTo(7));
            Assert.That(S.Markers.Where(m => m.Slot >= 0).All(m => m.Z < 1.01f || m.Z > 46.99f || m.Locked));
        }

        [Test]
        public void AMarker_StopsAsSoonAsItComesWithin12OfAnEnemyMarker()
        {
            var s = new TacticalPhaseState();
            var mine = new ForwardMarker { Slot = 0, Number = 1, X = 24f, Z = 1f };
            s.Markers.Add(mine);
            s.Markers.Add(new ForwardMarker { Slot = 0, Number = 2, X = 30f, Z = 1f });
            s.Markers.Add(new ForwardMarker { Slot = 1, Number = 1, X = 24f, Z = 20f });
            (float x, float z) = TacticalPhase.StopNearEnemy(s, mine, (24f, 13f));
            Assert.That(z, Is.EqualTo(8f).Within(0.15f));
            Assert.That(x, Is.EqualTo(24f));
        }

        [Test]
        public void TwelveForward_StaysWithin12OfAFriendlyMarker()
        {
            var s = new TacticalPhaseState();
            var lead = new ForwardMarker { Slot = 0, Number = 1, X = 24f, Z = 1f };
            s.Markers.Add(lead);
            s.Markers.Add(new ForwardMarker { Slot = 0, Number = 2, X = 24f, Z = 1f + 0f });
            s.Markers[1].X = 30f;
            (float _, float z) = TacticalPhase.ForwardSpot(s, lead, TacticalPhase.EdgeBottom);
            // From (24,1) with a friend at (30,1): the furthest forward point within 12" of the friend is about z = 11.4.
            Assert.That(z, Is.LessThanOrEqualTo(11.5f).And.GreaterThan(9f));
        }

        [Test]
        public async Task Deployment_FullyWithin3OfOwnMarkers_OrWithin12OfTheFarCorner()
        {
            Build(EAdvancedRules.ForwardPositions);
            await TacticalPhase.ForwardPositions(_context, Zones);
            UnitData unit = Unit(_me, "Squad", new Position(0f, 0f));
            IBoundedZone zone = TacticalPhase.DeployZone(_context, unit)!;
            Assert.That(zone, Is.Not.Null);
            ForwardMarker m = S.Markers.First(x => x.Slot == 0);
            Assert.That(zone.IsPointWithinZone(new Float2(m.X, m.Z)), Is.True, "on the marker");
            Assert.That(zone.IsPointWithinZone(new Float2(m.X + 2.4f, m.Z + 0f)), Is.True, "2.4\" off: the base still fits within 3\"");
            foreach (ForwardMarker e in S.Markers.Where(x => x.Slot == 1))
                Assert.That(zone.IsPointWithinZone(new Float2(e.X, e.Z)), Is.False, "not on the enemy's markers");
            Assert.That(zone.IsPointWithinZone(new Float2(m.X, m.Z + 3.5f)), Is.False, "3.5\" off is outside");
            (float cx, float cz) = TacticalPhase.Corner(S, 0);
            Assert.That(S.Markers.Where(e => e.Slot == 1).Min(e => MathF.Sqrt((e.X - cx) * (e.X - cx) + (e.Z - cz) * (e.Z - cz))),
                Is.GreaterThanOrEqualTo(new[] { (0f, 0f), (48f, 0f), (0f, 48f), (48f, 48f) }.Max(k => S.Markers.Where(e => e.Slot == 1)
                    .Min(e => MathF.Sqrt((e.X - k.Item1) * (e.X - k.Item1) + (e.Z - k.Item2) * (e.Z - k.Item2)))) - 0.01f));
            float ox = cx < 1f ? 5f : cx - 5f, oz = cz < 1f ? 5f : cz - 5f;
            Assert.That(zone.IsPointWithinZone(new Float2(ox, oz)), Is.True, "in the far corner");
        }

        [Test]
        public async Task ChoosingWhereAMarkerGoes_MoreThan12Away_IsRefused()
        {
            Build(EAdvancedRules.ForwardPositions);
            bool chose = false;
            _override = r =>
            {
                if (r is StringSelectionRequest q && q.TargetPlayerID.Equals(_me) && !chose && q.Instructions.StartsWith("Forward Positions: move", StringComparison.Ordinal))
                {
                    chose = true;
                    return q.ValidOptions.First(o => o.StartsWith(TacticalPhase.ChoosePrefix, StringComparison.Ordinal));
                }
                // Within 12" of marker 2 or 3, but 22" from marker 1 itself.
                if (r is PlaceObjectiveRequest p && p.Reach != null) return new Position(34f, 5f);
                return null;
            };
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(chose, Is.True);
            Assert.That(_asked.OfType<PlaceObjectiveRequest>().Count(p => p.Reach != null), Is.EqualTo(3), "asked again each time, then the game moves it");
            Assert.That(S.Markers.Single(x => x.Slot == 0 && x.Number == 1).X, Is.EqualTo(12f).Within(0.01f),
                "marker 1 went straight forward instead");
        }

        [Test]
        public async Task EachPart_IsItsOwnOption()
        {
            Build(EAdvancedRules.ForwardPositions);
            Piece("Ruin", 24f, 24f);
            await TacticalPhase.ForwardPositions(_context, Zones);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Log, Is.Empty, "no preparations without Battle Preparations");
            Build(EAdvancedRules.BattlePreparations);
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(TacticalPhase.State(_store), Is.Null, "no markers without Forward Positions");
        }

        [Test]
        public async Task Off_NoMarkers_AndTheNormalZone()
        {
            Build(EAdvancedRules.None);
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(TacticalPhase.State(_store), Is.Null);
            Assert.That(TacticalPhase.DeployZone(_context, Unit(_me, "Squad", new Position(0f, 0f))), Is.Null);
            Build(EAdvancedRules.ForwardPositions, EGameSystem.GrimdarkFuture);
            await TacticalPhase.ForwardPositions(_context, Zones);
            Assert.That(TacticalPhase.State(_store), Is.Null, "not in Grimdark Future");
        }

        [Test]
        public async Task RoundOne_TakesTheMarkersOff()
        {
            Build(EAdvancedRules.ForwardPositions);
            await TacticalPhase.ForwardPositions(_context, Zones);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(_context.TableState.Terrain.Objects.Count(TacticalPhase.IsMarker), Is.Zero);
            Assert.That(TacticalPhase.DeployZone(_context, Unit(_me, "Late", new Position(0f, 0f))), Is.Not.Null,
                "reserves arriving later still use the same rule's markers? no - but the state remains for the panel");
        }

        // ---------------------------------------------------------------- battle preparations

        private ITerrain Piece(string name, float x, float z, ETerrainType type = ETerrainType.Difficult)
        {
            _store.Create(new TerrainData(type, new RectangularZone(x - 2f, x + 2f, z - 2f, z + 2f), 2f, name: name));
            return _context.TableState.Terrain.Objects.Last(t => t.Name == name);
        }

        private Func<object, object?> Tactics(string mine, string theirs, Func<object, object?>? then = null) => r =>
        {
            if (r is StringSelectionRequest q && q.Instructions.StartsWith("Battle Preparations", StringComparison.Ordinal))
                return q.TargetPlayerID.Equals(_me) ? mine : theirs;
            return then?.Invoke(r);
        };

        [Test]
        public async Task PlantTraps_AnEnemyMovingWithin1_TakesD3Wounds_AndTheTrapGoes()
        {
            Build(EAdvancedRules.BattlePreparations, dice: new FixedDiceRoller(6));
            Piece("Ruin", 24f, 24f);
            UnitData enemy = Unit(_them, "Raiders", new Position(24f, 30f), models: 5);
            _override = Tactics(TacticalPhase.PlantTraps, TacticalPhase.FortifyTerrain);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Traps.Count(t => t.Slot == 0), Is.GreaterThan(0));
            int before = enemy.Models.Count(m => m.GetIsAlive());
            Assert.That(S.Traps, Has.Count.EqualTo(1), "a piece already trapped can't be trapped again by the same side");
            // Passing 1.5" from the piece's edge (base 0.5" radius): within 1".
            await TacticalPhase.AfterMove(_context, enemy, new[] { (new Position(27.0f, 30f), (IReadOnlyList<Position>)new[] { new Position(27.0f, 18f) }) });
            Assert.That(enemy.Models.Count(m => m.GetIsAlive()), Is.EqualTo(before - 3), "D3 on a 6 = 3 wounds");
            Assert.That(S.Traps.Count(t => t.Slot == 0), Is.Zero);
        }

        [Test]
        public async Task PlantTraps_OwnUnits_AndFarMoves_DontSetThemOff()
        {
            Build(EAdvancedRules.BattlePreparations, dice: new FixedDiceRoller(6));
            Piece("Ruin", 24f, 24f);
            UnitData mine = Unit(_me, "Mine", new Position(24f, 30f));
            UnitData enemy = Unit(_them, "Raiders", new Position(24f, 30f), models: 5);
            _override = Tactics(TacticalPhase.PlantTraps, TacticalPhase.FortifyTerrain);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            int traps = S.Traps.Count;
            await TacticalPhase.AfterMove(_context, mine, new[] { (new Position(24f, 30f), (IReadOnlyList<Position>)new[] { new Position(24f, 20f) }) });
            await TacticalPhase.AfterMove(_context, enemy, new[] { (new Position(30f, 30f), (IReadOnlyList<Position>)new[] { new Position(30f, 18f) }) });
            Assert.That(S.Traps.Count, Is.EqualTo(traps));
            Assert.That(enemy.Models.All(m => m.GetIsAlive()));
        }

        [Test]
        public async Task DisarmTraps_SameRound_CancelsThePlant()
        {
            Build(EAdvancedRules.BattlePreparations);
            Piece("Ruin", 24f, 24f);
            _override = Tactics(TacticalPhase.PlantTraps, TacticalPhase.DisarmTraps);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Traps, Is.Empty);
        }

        [Test]
        public async Task DisarmTraps_Later_RemovesOneEarlierRoundsTraps()
        {
            Build(EAdvancedRules.BattlePreparations, dice: new FixedDiceRoller(2)); // D3 = 1 trap a round
            Piece("Ruin", 24f, 24f);
            Piece("Wood", 10f, 24f);
            int round = 0;
            _override = r =>
            {
                if (r is StringSelectionRequest q && q.Instructions.StartsWith("Battle Preparations", StringComparison.Ordinal))
                {
                    if (q.TargetPlayerID.Equals(_me)) round++;
                    return q.TargetPlayerID.Equals(_me) ? (round <= 2 ? TacticalPhase.PlantTraps : TacticalPhase.MovePathfinders)
                        : (round == 3 ? TacticalPhase.DisarmTraps : TacticalPhase.FortifyTerrain);
                }
                return null;
            };
            Unit(_me, "Scouts", new Position(24f, 6f));
            _override = Combine(_override, r => r is PlaceObjectsRequest<ModelData> p
                ? new Selected<List<PlacedObjectEntry<ModelData>>>(p.ModelsToPlace.Select(m => new PlacedObjectEntry<ModelData>(m, new Position(24f, 9f))).ToList()) : null);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Traps.Select(t => t.Round).Distinct().Count(), Is.EqualTo(1), "one of the two plant rounds was removed: " + string.Join(";", S.Log) + " traps " + string.Join(",", S.Traps.Select(t => t.Round)));
        }

        private static Func<object, object?> Combine(Func<object, object?> a, Func<object, object?> b) => r => a(r) ?? b(r);

        [Test]
        public async Task FortifyTerrain_DefenseInside_ForThatSideOnly_AndOnlyDifficult()
        {
            Build(EAdvancedRules.BattlePreparations, dice: new FixedDiceRoller(6));
            Piece("Ruin", 24f, 10f);
            Piece("Wall", 10f, 10f, ETerrainType.Blocking);
            _override = Tactics(TacticalPhase.FortifyTerrain, TacticalPhase.PlantTraps);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Fortified.Where(f => f.Slot == 0).Select(f => f.Terrain.Split('|')[0]).Distinct(), Is.EqualTo(new[] { "Ruin" }));
            UnitData inside = Unit(_me, "Holders", new Position(24f, 10f));
            UnitData outside = Unit(_me, "Elsewhere", new Position(40f, 40f));
            UnitData enemyInside = Unit(_them, "Intruders", new Position(23f, 11f));
            Assert.That(TacticalPhase.SaveModifiers(_context, inside).Sum(m => m.Delta), Is.EqualTo(1));
            Assert.That(TacticalPhase.SaveModifiers(_context, outside), Is.Empty);
            Assert.That(TacticalPhase.SaveModifiers(_context, enemyInside), Is.Empty);
        }

        [Test]
        public async Task FortifyTerrain_IgnorePenalty_OnlyForThatSidesModels()
        {
            Build(EAdvancedRules.BattlePreparations);
            ITerrain ruin = Piece("Ruin", 24f, 10f);
            _override = Tactics(TacticalPhase.FortifyTerrain, TacticalPhase.PlantTraps,
                r => r is StringSelectionRequest q && q.Instructions.StartsWith("Fortify Terrain: what", StringComparison.Ordinal)
                    ? q.ValidOptions.First(o => o.Contains("ignore")) : null);
            UnitData mine = Unit(_me, "Mine", new Position(24f, 4f));
            UnitData theirs = Unit(_them, "Theirs", new Position(24f, 40f));
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(TacticalPhase.IgnoresPenalty(ruin, mine.Models[0]), Is.True);
            Assert.That(TacticalPhase.IgnoresPenalty(ruin, theirs.Models[0]), Is.False);
        }

        [Test]
        public async Task SabotageTerrain_FirstEnemyIntoTheFortifiedPiece_TakesD3_Once()
        {
            Build(EAdvancedRules.BattlePreparations, dice: new FixedDiceRoller(4));
            Piece("Ruin", 24f, 24f);
            _override = Tactics(TacticalPhase.FortifyTerrain, TacticalPhase.SabotageTerrain);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Sabotaged.Count(s => s.Slot == 1), Is.EqualTo(1));
            UnitData mine = Unit(_me, "Mine", new Position(24f, 30f), models: 5);
            await TacticalPhase.AfterMove(_context, mine, new[] { (new Position(24f, 30f), (IReadOnlyList<Position>)new[] { new Position(24f, 24f) }) });
            Assert.That(mine.Models.Count(m => m.GetIsAlive()), Is.EqualTo(3), "D3 on a 4 = 2 wounds");
            await TacticalPhase.AfterMove(_context, mine, new[] { (new Position(24f, 24f), (IReadOnlyList<Position>)new[] { new Position(24f, 30f), new Position(24f, 24f) }) });
            Assert.That(mine.Models.Count(m => m.GetIsAlive()), Is.EqualTo(3), "only the first time");
        }

        [Test]
        public async Task SabotageTerrain_WithNothingFortified_DoesNothing()
        {
            Build(EAdvancedRules.BattlePreparations);
            Piece("Ruin", 24f, 24f);
            _override = Tactics(TacticalPhase.PlantTraps, TacticalPhase.SabotageTerrain);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            Assert.That(S.Sabotaged, Is.Empty);
        }

        [Test]
        public async Task MovePathfinders_Within6_ThenShootPathfinders_QueuesAShot()
        {
            Build(EAdvancedRules.BattlePreparations);
            UnitData scouts = Unit(_me, "Scouts", new Position(24f, 6f));
            Unit(_them, "Gunners", new Position(24f, 40f));
            int round = 0;
            _override = Combine(r =>
            {
                if (r is StringSelectionRequest q && q.Instructions.StartsWith("Battle Preparations", StringComparison.Ordinal))
                {
                    if (q.TargetPlayerID.Equals(_me)) round++;
                    return q.TargetPlayerID.Equals(_me) ? TacticalPhase.MovePathfinders : TacticalPhase.ShootPathfinders;
                }
                return null;
            }, r => r is PlaceObjectsRequest<ModelData> p
                ? new Selected<List<PlacedObjectEntry<ModelData>>>(p.ModelsToPlace.Select(m => new PlacedObjectEntry<ModelData>(m, new Position(24f, 11f))).ToList()) : null);
            await TacticalPhase.BeforeRoundOne(_context, 1);
            var place = _asked.OfType<PlaceObjectsRequest<ModelData>>().First();
            Assert.That(place.MaxDistanceFromStartInches, Is.EqualTo(6f));
            Assert.That(scouts.Models[0].Position.z, Is.EqualTo(11f));
            Assert.That(S.Pathfinders.Values, Has.All.EqualTo(0));
            Assert.That(S.Log, Has.Count.EqualTo(3));
        }

        [Test]
        public void TheFreeShot_IsTakenOnce_ByItsOwnSide()
        {
            Build(EAdvancedRules.BattlePreparations);
            var s = new TacticalPhaseState();
            s.FreeShots.Add(new FreeShot { Slot = 1, Shooter = "a", Target = "b" });
            TacticalPhase.Save(_context, s);
            Assert.That(TacticalPhase.StartFreeShot(_context, _me), Is.False);
            Assert.That(TacticalPhase.StartFreeShot(_context, _them), Is.True);
            Assert.That(TacticalPhase.StartFreeShot(_context, _them), Is.False);
            Assert.That(TacticalPhase.TakeActiveShot(_context), Is.Null, "units that no longer exist: no shot");
        }

        private static float Dist(ForwardMarker a, ForwardMarker b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
    }
}
