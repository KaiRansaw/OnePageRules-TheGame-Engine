using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>City Fighting part 1 (GFF / AoFS Advanced Rules 3.6.0 p47): Cornered Units, Destructible Cover, Thrown
    /// Objects, Buildings on Fire. Played-game check: the bot sweep with the chapter on.</summary>
    [TestFixture]
    public class CityFightingTests
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

        private static object Reply(object r) => r switch
        {
            StringSelectionRequest q => q.Recommended ?? q.ValidOptions[0],
            AssignWoundsRequest w => w.GetType().GetMethod("AutoAssign")?.Invoke(w, null) ?? true,
            _ => true,
        };

        private const EAdvancedRules AllFour = EAdvancedRules.CorneredUnits | EAdvancedRules.DestructibleCover
            | EAdvancedRules.ThrownObjects | EAdvancedRules.BuildingsOnFire;

        private void Build(EAdvancedRules rules = AllFour, int die = 4, EGameSystem system = EGameSystem.GFFirefight)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedDiceRoller(die), settings: settings,
                playerRequester: new Answer(r => { _asked.Add(r); return Reply(r); }));
            _asked.Clear();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _me }));
            _store.Create(new TeamData(1, new List<PlayerID> { _them }));
            new PlayerSlot(0, teamNumber: 0, _me, new ArmyListFile(), _store).AssignPlayerController(new Seat("Kai", _me));
            new PlayerSlot(1, teamNumber: 1, _them, new ArmyListFile(), _store).AssignPlayerController(new Seat("Bot", _them));
        }

        private UnitData Unit(PlayerID owner, string name, params (float x, float z)[] at)
        {
            var bindings = at
                .Select(p => _store.GetDataBinding<ModelData>(_store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(p.x, p.z), _store))))
                .ToList();
            var unit = new UnitData(owner, name, quality: 4, defense: 5, modelBindings: bindings) { PointCost = 50 };
            _store.Create(unit);
            return _context.TableState.Units.Objects.OfType<UnitData>().Last(u => u.Name == name);
        }

        private DataBinding<UnitData> Binding(UnitData unit) =>
            _store.GetDataBinding<UnitData>(_store.GetAllDataReferences<UnitData>().First(r => ReferenceEquals(_store.GetValue<UnitData>(r), unit)));

        private void Piece(string kind, ETerrainType type, float left, float right, float bottom, float top, float height = 4f, string? name = null) =>
            _store.Create(new TerrainData(type, new RectangularZone(left, right, bottom, top), height, name: name ?? kind, kind: kind));

        private static int Alive(UnitData u) => u.Models.Count(m => m.GetIsAlive());

        private List<ITerrain> Terrain => _context.TableState.Terrain.Objects.ToList();

        // ---------------------------------------------------------------- the lobby

        [Test]
        public void OnlyFirefightAndSkirmish_HaveTheChapter_WithTheBooksWords()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GFFirefight, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.In(CityFighting.Chapter, s).Select(o => o.Name),
                    Is.EqualTo(new[] { CityFighting.CorneredUnitsName, CityFighting.DestructibleCoverName, CityFighting.ThrownObjectsName, CityFighting.BuildingsOnFireName }));
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFStarQuest })
                Assert.That(AdvancedRules.In(CityFighting.Chapter, s), Is.Empty);
            Assert.That(CityFighting.BookText(CityFighting.ThrownObjectsName), Does.Contain("On a 4+ the target takes one hit."));
            Assert.That(CityFighting.BookText(CityFighting.BuildingsOnFireName), Does.Contain("on a result of 6 the fire stops"));
            Assert.That(AdvancedRules.ChapterIntro[CityFighting.Chapter], Does.StartWith("City Fighting rules are optional rules"));
            foreach (var o in CityFighting.All)
                Assert.That(CityFighting.GameText(o.Name).All(ch => ch < 128), Is.True, o.Name + " is ASCII");
        }

        [Test]
        public void TheFlagsAreTheirOwn_NoOtherChapterUsesThem()
        {
            EAdvancedRules city = AdvancedRules.ChapterFlags(CityFighting.Chapter);
            foreach (var o in AdvancedRules.Catalog.Where(o => o.Chapter != CityFighting.Chapter))
                Assert.That(o.Flag & city, Is.EqualTo(EAdvancedRules.None), o.Name);
        }

        // ---------------------------------------------------------------- pieces

        [Test]
        public void Buildings_AreBuildingRuinsOrSolid_SmallCoverIsUpTo3AndAHalfInches()
        {
            Build();
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 0, 6, 0, 6);
            Piece("Ruins", ETerrainType.Cover | ETerrainType.Unstable, 10, 16, 0, 6);
            Piece("Forest", ETerrainType.Cover | ETerrainType.Difficult, 20, 26, 0, 6);
            Piece("Barricade", ETerrainType.Cover, 30, 33, 0, 1, 1f);
            Piece("Crates", ETerrainType.Cover, 40, 42, 0, 2, 1f);
            Piece("BigCover", ETerrainType.Cover, 50, 54, 0, 4, 1f);
            var t = Terrain;
            Assert.That(t.Where(CityFighting.IsBuilding).Select(p => p.Kind), Is.EquivalentTo(new[] { "Building", "Ruins" }));
            Assert.That(t.Where(CityFighting.IsSmallCover).Select(p => p.Kind), Is.EquivalentTo(new[] { "Barricade", "Crates" }));
        }

        // ---------------------------------------------------------------- Cornered Units

        private (UnitData Victim, UnitData Foes) Corner(bool leaveAGap)
        {
            // A building on the left; the victim against its right wall; enemies round the other three sides.
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 10, 20, 10, 30);
            UnitData victim = Unit(_me, "Victims", (21f, 20f));
            var ring = new List<(float, float)>();
            for (int i = 0; i <= 12; i++)
            {
                double a = -Math.PI / 2 + i * Math.PI / 12;
                if (leaveAGap && i >= 4 && i <= 8) continue;   // a 3" gap: room to walk out, 1" clear of both sides
                ring.Add((21f + 2.2f * (float)Math.Cos(a), 20f + 2.2f * (float)Math.Sin(a)));
            }
            UnitData foes = Unit(_them, "Mob", ring.ToArray());
            return (victim, foes);
        }

        [Test]
        public void Cornered_AgainstABuilding_CircledByEnemies_GivesPlusOneToHitAndAp_InMeleeOnly()
        {
            Build();
            (UnitData victim, UnitData foes) = Corner(leaveAGap: false);
            Assert.That(CityFighting.Cornered(_context, victim), Is.True);
            Assert.That(CityFighting.HitModifiers(_context, victim, melee: true, spell: false).Single().Delta, Is.EqualTo(1));
            Assert.That(CityFighting.ArmorPenetrationBonus(_context, victim, melee: true, spell: false), Is.EqualTo(1));
            Assert.That(CityFighting.HitModifiers(_context, victim, melee: false, spell: false), Is.Empty, "shooting");
            Assert.That(CityFighting.ArmorPenetrationBonus(_context, victim, melee: false, spell: false), Is.Zero);
            Assert.That(CityFighting.Cornered(_context, foes), Is.False, "the circle isn't cornered");
        }

        [Test]
        public void NotCornered_WithAGapInTheRing_AwayFromBuildings_OrWithTheRuleOff()
        {
            Build();
            (UnitData victim, _) = Corner(leaveAGap: true);
            Assert.That(CityFighting.Cornered(_context, victim), Is.False, "a gap to move past them");

            Build();
            UnitData open = Unit(_me, "Open", (40f, 20f));
            Unit(_them, "Near", (42f, 20f));
            Assert.That(CityFighting.Cornered(_context, open), Is.False, "no building");

            Build(EAdvancedRules.DestructibleCover);
            (victim, _) = Corner(leaveAGap: false);
            Assert.That(CityFighting.HitModifiers(_context, victim, true, false), Is.Empty);
            Assert.That(CityFighting.ArmorPenetrationBonus(_context, victim, true, false), Is.Zero);
        }

        // ---------------------------------------------------------------- Destructible Cover

        [Test]
        public void SmallCoverBetween_IsRolledAfterTheShooting_AOneRemovesIt()
        {
            Build(die: 1);
            Piece("Crates", ETerrainType.Cover, 9, 11, 18, 20, 1f, name: "Crates");
            UnitData shooter = Unit(_me, "Shooters", (10f, 5f));
            UnitData target = Unit(_them, "Targets", (10f, 20.8f));
            Assert.That(CityFighting.CoverBehind(_context, shooter, target).Select(p => p.Name), Is.EqualTo(new[] { "Crates" }));
            CityFighting.NoteShot(_context, shooter, target);
            CityFighting.NoteShot(_context, shooter, target);   // a second weapon: still one die for the piece
            Assert.That(Terrain.Any(t => t.Name == "Crates"), Is.True, "the cover still counts for the shots themselves");
            CityFighting.AfterShooting(_context);
            Assert.That(Terrain.Any(t => t.Name == "Crates"), Is.False);
            Assert.That(CityFighting.State(_store)!.Destroyed, Has.Count.EqualTo(1));
        }

        [Test]
        public void Cover_Stands_OnAnyOtherRoll_AndOnlySmallPiecesInTheWay_WithinAnInch_AreRolled()
        {
            Build(die: 2);
            Piece("Crates", ETerrainType.Cover, 9, 11, 18, 20, 1f, name: "Crates");
            UnitData shooter = Unit(_me, "Shooters", (10f, 5f));
            UnitData target = Unit(_them, "Targets", (10f, 20.8f));
            CityFighting.NoteShot(_context, shooter, target);
            CityFighting.AfterShooting(_context);
            Assert.That(Terrain.Any(t => t.Name == "Crates"), Is.True);

            Build(die: 1);
            Piece("Wall", ETerrainType.Cover, 6, 14, 18, 20, 1f, name: "LongWall");           // 8" long: not small
            Piece("Crates", ETerrainType.Cover, 30, 32, 18, 20, 1f, name: "Aside");          // not in the way
            Piece("Crates", ETerrainType.Cover, 9, 11, 12, 14, 1f, name: "FarCrates");     // in the way, but 6" from the target
            shooter = Unit(_me, "Shooters", (10f, 5f));
            target = Unit(_them, "Targets", (10f, 20.8f));
            Assert.That(CityFighting.CoverBehind(_context, shooter, target), Is.Empty);
        }

        [Test]
        public void DestructibleCover_Off_NothingIsRolled()
        {
            Build(EAdvancedRules.CorneredUnits, die: 1);
            Piece("Crates", ETerrainType.Cover, 9, 11, 18, 20, 1f, name: "Crates");
            UnitData shooter = Unit(_me, "Shooters", (10f, 5f));
            UnitData target = Unit(_them, "Targets", (10f, 20.8f));
            CityFighting.NoteShot(_context, shooter, target);
            CityFighting.AfterShooting(_context);
            Assert.That(Terrain.Any(t => t.Name == "Crates"), Is.True);
        }

        // ---------------------------------------------------------------- Thrown Objects

        private UnitData OnTheRoof()
        {
            // A climbable 4" building at x 10-20; the thrower stands on top near its right edge.
            Piece("Building", ETerrainType.Climbable | ETerrainType.Cover, 10, 20, 10, 20, 4f, name: "Tower");
            return Unit(_me, "Roofers", (19f, 15f));
        }

        [Test]
        public void Thrown_FromTheRoof_AtAnEnemyAtTheFoot_FourUpIsOneHit()
        {
            Build(die: 4);
            UnitData thrower = OnTheRoof();
            UnitData foot = Unit(_them, "AtTheFoot", (22f, 15f));
            Unit(_them, "TooFar", (30f, 15f));               // 11" across
            Assert.That(CityFighting.RoofOf(_context, thrower)!.Name, Is.EqualTo("Tower"));
            Assert.That(CityFighting.ThrowTargets(_context, thrower, CityFighting.RoofOf(_context, thrower)!).Select(u => u.Name),
                Is.EqualTo(new[] { "AtTheFoot" }));
            var ctx = new FDG.Stages.UnitActionContext(_context, Binding(thrower));
            Assert.That(CityFighting.ActionOptions(_context, ctx).Select(o => o.Option), Does.Contain(CityFighting.ThrowOption));
            Assert.That(CityFighting.Throw(_context, thrower).Result, Is.True);
            Assert.That(Alive(foot), Is.Zero, "one hit, Defense 5+ failed on a 4");
        }

        [Test]
        public void Thrown_OnAThree_Misses_AndNotFromTheGround()
        {
            Build(die: 3);
            UnitData thrower = OnTheRoof();
            UnitData foot = Unit(_them, "AtTheFoot", (22f, 15f));
            Assert.That(CityFighting.Throw(_context, thrower).Result, Is.True);
            Assert.That(Alive(foot), Is.EqualTo(1));

            Build(die: 4);
            Piece("Building", ETerrainType.Climbable | ETerrainType.Cover, 10, 20, 10, 20, 4f, name: "Tower");
            UnitData ground = Unit(_me, "Ground", (21f, 25f));
            Unit(_them, "AtTheFoot", (22f, 15f));
            Assert.That(CityFighting.RoofOf(_context, ground), Is.Null);
            Assert.That(CityFighting.ActionOptions(_context, new FDG.Stages.UnitActionContext(_context, Binding(ground))).Select(o => o.Option),
                Does.Not.Contain(CityFighting.ThrowOption));
        }

        [Test]
        public void InsteadOfShooting_NotAfterAttacking_NotAfterARush_NotWhenShaken()
        {
            Build(die: 4);
            UnitData thrower = OnTheRoof();
            Unit(_them, "AtTheFoot", (22f, 15f));
            var attacked = new FDG.Stages.UnitActionContext(_context, Binding(thrower));
            attacked.RegisterAttackedFinished();
            Assert.That(CityFighting.ActionOptions(_context, attacked), Is.Empty);
            var rushed = new FDG.Stages.UnitActionContext(_context, Binding(thrower));
            rushed.RegisterMoveFinished(12f, 6f);
            Assert.That(CityFighting.ActionOptions(_context, rushed), Is.Empty);
            var advanced = new FDG.Stages.UnitActionContext(_context, Binding(thrower));
            advanced.RegisterMoveFinished(5f, 6f);
            Assert.That(CityFighting.ActionOptions(_context, advanced), Is.Not.Empty);
            thrower.Tokens.AddToken(new Rules.Tokens.Token(Rules.Foundation.TokenType.Shaken, 1, new Rules.Foundation.TokenClearTrigger.ManualOnly()));
            Assert.That(CityFighting.ActionOptions(_context, new FDG.Stages.UnitActionContext(_context, Binding(thrower))), Is.Empty);
        }

        // ---------------------------------------------------------------- Buildings on Fire

        [Test]
        public void OnASix_TheBuildingBurns_UnitsInsideTestEachRoundEnd_ThenASixPutsItOut()
        {
            Build(die: 6);
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 10, 20, 10, 20, 4f, name: "Inn");
            UnitData arsonists = Unit(_me, "Arsonists", (21.5f, 15f));
            UnitData inside = Unit(_them, "Inside", (15f, 15f));
            Assert.That(CityFighting.ActionOptions(_context, new FDG.Stages.UnitActionContext(_context, Binding(arsonists))).Select(o => o.Option),
                Does.Contain(CityFighting.FireOption));
            Assert.That(CityFighting.SetOnFire(_context, arsonists).Result, Is.True);
            Assert.That(CityFighting.BurningBuildings(_context).Single().Name, Is.EqualTo("Inn"));
            Assert.That(CityFighting.FireTargets(_context, arsonists), Is.Empty, "already burning");

            CityFighting.RoundEnd(_context).Wait();
            Assert.That(Alive(inside), Is.EqualTo(1), "a 6 passes the Dangerous test");
            Assert.That(CityFighting.BurningBuildings(_context), Is.Empty, "and a 6 puts the fire out");
        }

        [Test]
        public void OnAOne_UnitsInsideAreHurt_AndTheFireSpreadsToABuildingWithin6()
        {
            Build(die: 6);
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 10, 20, 10, 20, 4f, name: "Inn");
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 24, 30, 10, 20, 4f, name: "Barn");    // 4" away
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 40, 46, 10, 20, 4f, name: "Church");  // 20" away
            UnitData arsonists = Unit(_me, "Arsonists", (15f, 21.5f));
            UnitData inside = Unit(_them, "Inside", (15f, 15f));
            CityFighting.SetOnFire(_context, arsonists).Wait();

            // Same game, dice now land on 1.
            var ctx = new TestGameContext(_store, new FixedDiceRoller(1), settings: _context.Settings,
                playerRequester: new Answer(r => { _asked.Add(r); return Reply(r); }));
            CityFighting.RoundEnd(ctx).Wait();
            Assert.That(Alive(inside), Is.Zero, "a 1 fails the Dangerous test");
            Assert.That(CityFighting.BurningBuildings(ctx).Select(b => b.Name), Is.EquivalentTo(new[] { "Inn", "Barn" }));
        }

        [Test]
        public void BuildingsOnFire_Off_NoMenuEntry_AndNothingBurns()
        {
            Build(EAdvancedRules.ThrownObjects, die: 6);
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 10, 20, 10, 20, 4f, name: "Inn");
            UnitData arsonists = Unit(_me, "Arsonists", (21.5f, 15f));
            Assert.That(CityFighting.ActionOptions(_context, new FDG.Stages.UnitActionContext(_context, Binding(arsonists))), Is.Empty);
        }

        [Test]
        public void PanelLines_NameTheRules_AndWhatBurns()
        {
            Build(die: 6);
            Piece("Building", ETerrainType.Impassible | ETerrainType.Blocking, 10, 20, 10, 20, 4f, name: "Inn");
            UnitData arsonists = Unit(_me, "Arsonists", (21.5f, 15f));
            CityFighting.SetOnFire(_context, arsonists).Wait();
            List<string> lines = CityFighting.PanelLines(_context.Settings, _store);
            Assert.That(lines[0], Does.StartWith("City Fighting: Cornered Units"));
            Assert.That(lines, Has.Some.EqualTo("On fire: Inn at (15\", 15\")"));
        }
    }
}
