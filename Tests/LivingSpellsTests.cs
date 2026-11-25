using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Advanced Casting - Living Spells (GF p35, GFF p32, AoF p35, AoFS p32). Casting / unbinding through the real
    /// cast stage is in CasterRuleIntegrationTests.</summary>
    [TestFixture]
    public class LivingSpellsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private readonly List<StringSelectionRequest> _asked = new();
        private Func<StringSelectionRequest, string>? _answer;
        private Position? _place;
        private bool _gate;
        private Position? _gateTo;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private sealed class Faces : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public Faces(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_i < _faces.Length ? _faces[_i++] : 1);
        }

        private void Build(EGameSystem system, EAdvancedRules rules, params int[] faces)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _asked.Clear();
            _context = new TestGameContext(_store, new Faces(faces), settings: settings, playerRequester: new Answer(r => r switch
            {
                StringSelectionRequest q => Record(q),
                PlaceObjectiveRequest pl => _place ?? throw new InvalidOperationException("no spot"),
                YesNoRequest => _gate,
                PlaceObjectsRequest<ModelData> po => _gateTo == null
                    ? new Cancelled<List<PlacedObjectEntry<ModelData>>>()
                    : (object)new Selected<List<PlacedObjectEntry<ModelData>>>(po.ModelsToPlace.Select((b, i) =>
                        new PlacedObjectEntry<ModelData>(b, new Position(_gateTo.Value.x + i * 1.2f, _gateTo.Value.z), null)).ToList()),
                AssignWoundsRequest w => AutoFill(w),
                _ => throw new InvalidOperationException(r.GetType().Name),
            }));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        private object Record(StringSelectionRequest q)
        {
            _asked.Add(q);
            return _answer?.Invoke(q) ?? q.Recommended ?? q.ValidOptions[0];
        }

        private static object AutoFill(AssignWoundsRequest w)
        {
            var results = new AssignWoundsResults(w.UnitReceivingWounds, w.Packets);
            results.AutoFill();
            return results;
        }

        private static RuntimeSpell Spell(string name, int cost) => new RuntimeSpell(
            new SpellDefinition(name, cost, new TargetSelector(18f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger)), Array.Empty<ResolvedRule>());

        private UnitData Unit(PlayerID owner, Position at, int models, int casterLevel = 0, int tough = 1, IReadOnlyList<RuntimeSpell>? spells = null)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(new CircleBase(0.5f), tough,
                    _store.GetDataBinding<float>(_store.Create((float)tough)),
                    _store.GetDataBinding<Position>(_store.Create(new Position(at.x + i * 1.5f, at.z))),
                    _store.GetDataBinding<Float2>(_store.Create(new Float2(0f, 1f))), new List<Weapon>());
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, casterLevel > 0 ? "Wizard" : "Squad", quality: 4, defense: 4, modelBindings: bindings);
            if (casterLevel > 0)
                unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster, new RuleArgument[] { new RuleArgument.Int(casterLevel) }));
            var army = new ArmyData(owner, new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(unit)) });
            if (spells != null) army.SetSpells(spells);
            _store.Create(army);
            return unit;
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

        private LivingSpellsState State => LivingSpells.State(_store)!;

        private LivingSpellPiece Put(PlayerID owner, string name, float x, float z, float fx = 0f, float fz = 1f)
        {
            LivingSpellsState s = LivingSpells.State(_store) ?? new LivingSpellsState();
            var p = new LivingSpellPiece { Owner = owner.ID.ToString(), OwnerName = "P", Name = name, X = x, Z = z, Fx = fx, Fz = fz };
            s.OnTable.Add(p);
            LivingSpells.Save(_context, s);
            return p;
        }

        [Test]
        public void Catalog_EighteenSpells_InAllFourBooks()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
            {
                Assert.That(LivingSpells.Table(s), Has.Count.EqualTo(18));
                Assert.That(LivingSpells.Table(s).GroupBy(d => d.Category).Select(g => g.Count()), Is.All.EqualTo(6));
                Assert.That(AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, s).Select(o => o.Name), Does.Contain(LivingSpells.Option));
                Assert.That((LivingSpells.BookText(s) + LivingSpells.GameText).All(ch => ch < 128), "ASCII");
            }
            Assert.That(LivingSpells.Gates(EGameSystem.GrimdarkFuture), Is.EqualTo("Linked Jumpgates"));
            Assert.That(LivingSpells.Gates(EGameSystem.AoFSkirmish), Is.EqualTo("Linked Voidgates"));
            Assert.That(LivingSpells.Table(EGameSystem.GrimdarkFuture).Where(d => d.Category == LivingSpells.Forbidden).Select(d => d.Value), Is.All.EqualTo(3));
        }

        [Test]
        public async Task Settle_EachArmyGetsAPrimitiveSpell_PickedOrRolled()
        {
            // The bot rolls: a 3 -> Cogs of Time.
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 3);
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
            _answer = q => q.TargetPlayerID == _me ? q.ValidOptions.First(o => o.StartsWith("Shard of Mist")) : q.Recommended!;
            await LivingSpells.Settle(_context);
            Assert.That(_asked, Has.Count.EqualTo(2), "one question per army - only the Primitive Spells are in so far");
            Assert.That(_asked[0].ValidOptions, Has.Count.EqualTo(7), "Roll + the six");
            Assert.That(State.Picks[_me.ID.ToString()], Is.EqualTo(new[] { LivingSpells.ShardOfMist }));
            Assert.That(State.Picks[_them.ID.ToString()], Is.EqualTo(new[] { LivingSpells.CogsOfTime }));
            await LivingSpells.Settle(_context);
            Assert.That(_asked, Has.Count.EqualTo(2), "settled once");
        }

        [Test]
        public void OfferSpells_OwnPicks_AndUnbindWithin12()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells);
            UnitData wizard = Unit(_me, new Position(20f, 20f), 1, casterLevel: 1);
            var s = new LivingSpellsState();
            s.Picks[_me.ID.ToString()] = new List<string> { LivingSpells.BurningPyre };
            LivingSpells.Save(_context, s);
            Put(_them, LivingSpells.ShardOfMist, 31f, 20f);
            Put(_them, LivingSpells.CogsOfTime, 40f, 20f);
            var names = LivingSpells.OfferSpells(_context, wizard, _me).Select(r => r.Name).ToList();
            Assert.That(names, Does.Contain("Living Spell: Burning Pyre"));
            Assert.That(names.Count(n => n.StartsWith("Unbind: Shard of Mist")), Is.EqualTo(1), "11\" away - in reach");
            Assert.That(names.Any(n => n.StartsWith("Unbind: Cogs")), Is.False, "20\" away");
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.None);
            wizard = Unit(_me, new Position(20f, 20f), 1, casterLevel: 1);
            Assert.That(LivingSpells.OfferSpells(_context, wizard, _me), Is.Empty, "off: nothing");
        }

        [Test]
        public async Task Cast_PlacesWithin12_AndReplacesTheOldOne()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells);
            UnitData wizard = Unit(_me, new Position(20f, 20f), 1, casterLevel: 1);
            _place = new Position(25f, 20f);
            await LivingSpells.Resolve(_context, wizard, "Living Spell: Burning Pyre");
            Assert.That(State.OnTable.Single().X, Is.EqualTo(25f).Within(0.01f));
            _place = new Position(60f, 20f);
            await LivingSpells.Resolve(_context, wizard, "Living Spell: Shard of Mist");
            LivingSpellPiece only = State.OnTable.Single();
            Assert.That(only.Name, Is.EqualTo(LivingSpells.ShardOfMist), "one per army: the Pyre is gone");
            Assert.That(only.X, Is.LessThan(20f + 12f + 1.5f), "a spot past 12\" is pulled back");
            Assert.That(_store.GetAllValues<TerrainData>().Count(LivingSpells.IsMarker), Is.EqualTo(1), "the old marker is erased");
        }

        [Test]
        public async Task Unbind_RemovesTheEnemySpell()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells);
            UnitData wizard = Unit(_me, new Position(20f, 20f), 1, casterLevel: 1);
            Put(_them, LivingSpells.ShardOfMist, 25f, 20f);
            await LivingSpells.Resolve(_context, wizard, "Unbind: Shard of Mist (P)");
            Assert.That(State.OnTable, Is.Empty);
        }

        [Test]
        public async Task BurningPyre_D3WoundsToUnitsWithin6()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 6, 6);
            UnitData near = Unit(_them, new Position(25f, 20f), 5);
            UnitData far = Unit(_them, new Position(40f, 20f), 5);
            Put(_me, LivingSpells.BurningPyre, 20f, 20f);
            await LivingSpells.RoundEnd(_context);
            Assert.That(near.Models.Count(m => !m.GetIsAlive()), Is.EqualTo(3), "a D3 of 3");
            Assert.That(far.Models.All(m => m.GetIsAlive()), Is.True);
        }

        [Test]
        public async Task CornerMover_Moves12TowardsTheRolledCorner_AndLeavesAtTheEdge()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 1);
            LivingSpellPiece p = Put(_me, LivingSpells.CogsOfTime, 40f, 30f);
            await LivingSpells.RoundEnd(_context);
            Assert.That(MathF.Sqrt((p.X - 40f) * (p.X - 40f) + (p.Z - 30f) * (p.Z - 30f)), Is.EqualTo(12f).Within(0.01f));
            Assert.That(p.X, Is.LessThan(40f), "corner 1 = (0, 0)");
            Assert.That(p.Z, Is.LessThan(30f));
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 1);
            Put(_me, LivingSpells.CogsOfTime, 6f, 6f);
            await LivingSpells.RoundEnd(_context);
            Assert.That(State.OnTable, Is.Empty, "reached the table edge");
        }

        [Test]
        public async Task SuffocatingCloud_Moves12Ahead_TestingUnitsItPasses()
        {
            // Dangerous tests: a 1 wounds. Every die a 1.
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            UnitData inPath = Unit(_them, new Position(30f, 30f), 1, tough: 3);
            UnitData aside = Unit(_them, new Position(30f, 40f), 1, tough: 3);
            LivingSpellPiece p = Put(_me, LivingSpells.SuffocatingCloud, 20f, 30f, fx: 1f, fz: 0f);
            await LivingSpells.RoundEnd(_context);
            Assert.That(p.X, Is.EqualTo(32f).Within(0.01f));
            Assert.That(inPath.Models[0].WoundsDealt, Is.GreaterThan(0), "it passed through");
            Assert.That(aside.Models[0].WoundsDealt, Is.EqualTo(0), "10\" to the side: the cloud is ~7\" long");
        }

        [Test]
        public async Task DeathlyPendulum_HitsWhatIsNearWhereItStops()
        {
            // Corner 3 = top right, then 2D3 = 3+3 hits, Defense rolls of 1 fail.
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells, 3, 6, 6, 1, 1, 1, 1, 1, 1);
            float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
            var start = new Position(w / 2f, h / 2f);
            float dx = w - start.x, dz = h - start.z, d = MathF.Sqrt(dx * dx + dz * dz);
            var stop = new Position(start.x + dx / d * 12f, start.z + dz / d * 12f);
            UnitData there = Unit(_them, new Position(stop.x + 2f, stop.z), 10);
            Put(_me, LivingSpells.DeathlyPendulum, start.x, start.z);
            await LivingSpells.RoundEnd(_context);
            Assert.That(there.Models.Count(m => !m.GetIsAlive()), Is.EqualTo(6));
        }

        [Test]
        public void CogsAndMist_HitModifiers_ForShootersWithin6()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells);
            UnitData shooter = Unit(_me, new Position(20f, 20f), 1);
            Put(_them, LivingSpells.CogsOfTime, 24f, 20f);
            Assert.That(LivingSpells.HitModifiers(_context, shooter, melee: false, spell: false).Sum(m => m.Delta), Is.EqualTo(1));
            Assert.That(LivingSpells.HitModifiers(_context, shooter, melee: true, spell: false), Is.Empty, "shooting only");
            Put(_them, LivingSpells.ShardOfMist, 20f, 25f);
            Assert.That(LivingSpells.HitModifiers(_context, shooter, melee: false, spell: false).Sum(m => m.Delta), Is.EqualTo(0));
            UnitData far = Unit(_me, new Position(50f, 20f), 1);
            Assert.That(LivingSpells.HitModifiers(_context, far, melee: false, spell: false), Is.Empty);
        }

        [Test]
        public async Task Gates_AUnitBesideOne_MayStepToTheOther()
        {
            Build(EGameSystem.AgeOfFantasy, EAdvancedRules.LivingSpells);
            UnitData squad = Unit(_them, new Position(21.5f, 20f), 2);
            var s = new LivingSpellsState();
            s.OnTable.Add(new LivingSpellPiece { Owner = _me.ID.ToString(), Name = "Linked Voidgates", X = 20f, Z = 20f, X2 = 40f, Z2 = 20f });
            LivingSpells.Save(_context, s);
            _gate = true;
            _gateTo = new Position(41.5f, 20f);
            await LivingSpells.EndOfActivation(_context, squad);
            Assert.That(squad.Models[0].Position.x, Is.EqualTo(41.5f).Within(0.01f), "any unit may use it (the enemy's here)");
            _gate = false;
            squad.Models[0].SetPosition(new Position(30f, 20f));
            await LivingSpells.EndOfActivation(_context, squad);
            Assert.That(squad.Models[0].Position.x, Is.EqualTo(30f).Within(0.01f), "not near a copy");
        }

        [Test]
        public void LongSpell_LiesAcrossItsFacing()
        {
            var p = new LivingSpellPiece { Name = LivingSpells.SuffocatingCloud, X = 0f, Z = 0f, Fx = 1f, Fz = 0f };
            var d = LivingSpells.Find(EGameSystem.GrimdarkFuture, LivingSpells.SuffocatingCloud)!;
            Assert.That(LivingSpells.DistanceTo(p, d, 0f, 3f), Is.EqualTo(0f), "along its length (perpendicular to the front)");
            Assert.That(LivingSpells.DistanceTo(p, d, 3f, 0f), Is.EqualTo(3f - LivingSpells.LongWidth / 2f).Within(0.01f), "in front");
            Assert.That(LivingSpells.DistanceTo(p, d, 0f, 5f), Is.EqualTo(5f - LivingSpells.LongLength / 2f).Within(0.01f), "past its end");
        }

        [Test]
        public void DistanceToEdge_StopsAtTheFootprint()
        {
            Assert.That(LivingSpells.DistanceToEdge(10f, 10f, -1f, 0f, 1f), Is.EqualTo(9f).Within(0.01f));
        }

        [Test]
        public void PanelLines_ListPicksAndSpellsOnTheTable()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.LivingSpells);
            var s = new LivingSpellsState();
            s.Picks["a"] = new List<string> { LivingSpells.BurningPyre };
            s.Names["a"] = "Kai";
            LivingSpells.Save(_context, s);
            Put(_me, LivingSpells.ShardOfMist, 10f, 10f);
            var lines = LivingSpells.PanelLines(_store, _context.Settings);
            Assert.That(lines.Any(l => l.Text == "Kai: Burning Pyre"));
            Assert.That(lines.Any(l => l.Text.StartsWith("On the table: P's Shard of Mist")));
            Assert.That(lines.All(l => (l.Text + l.Hover).All(ch => ch < 128)));
        }

        [Test]
        public void TerrainEffects_SkipLivingSpellMarkers()
        {
            Assert.That(LivingSpells.IsMarker(new TerrainData(ETerrainType.None, new CircularZone(new Float2(0f, 0f), 1f), 0f, name: "Living spell: X (P)")), Is.True);
            Assert.That(LivingSpells.IsMarker(new TerrainData(ETerrainType.None, new CircularZone(new Float2(0f, 0f), 1f), 0f, name: "Forest")), Is.False);
        }
    }
}
