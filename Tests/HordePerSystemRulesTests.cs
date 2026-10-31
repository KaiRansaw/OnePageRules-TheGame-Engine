using FDG.Data;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    // #416 - the four Horde Mode v3.6.0 books, read word for word, and the two places the engine played
    // one system's version everywhere.
    //
    // Nearly all of Horde Mode is identical across Grimdark Future, Age of Fantasy, GF: Firefight and
    // AoF: Skirmish: 12 rounds, three rounds per wave, five objectives, priority at rounds 1/4/7/10,
    // waves at 4/7/10, rewards at 3/6/9/12, three damage to destroy a marker, the same six-trait table,
    // the same 30 XP cap. What differs:
    //
    //   | | GF | AoF | GFF | AoFS |
    //   | points         | 2000 | 1500 | 800 | 600 |
    //   | deployment     | 9"   | 9"   | 6"  | 6"  |
    //   | reward/priority| 40/20| 40/20| 30/15| 30/15 |
    //   | XP tiers       | 3/5/8| 3/5/8| 3/5 | 3/5 |
    //
    // The sizes were already handled by HordeRules.Configure. The XP table was not: both skirmish books
    // list "+3 XP: Killed a unit" and "+5 XP: Killed a Hero unit" and then stop - there is no "+8 XP:
    // Killed a Hero and its unit" line in either of them - and the engine awarded 8 in all four games.
    //
    // The second half of this fixture pins something that is NOT a bug, because it reads exactly like one
    // and was reported as one: the point reward re-tests occupancy at the moment it pays out, rather than
    // reading the objective owner the engine recorded earlier in the same stage. That is the book.
    [TestFixture]
    public class HordePerSystemRulesTests
    {
        [TearDown]
        public void RestoreDefaults() => HordeRules.Configure(EGameSystem.GrimdarkFuture);

        // ── The XP table ────────────────────────────────────────────────────────────────────────────

        [TestCase(EGameSystem.GrimdarkFuture, 8)]
        [TestCase(EGameSystem.AgeOfFantasy, 8)]
        [TestCase(EGameSystem.GFFirefight, 5)]
        [TestCase(EGameSystem.AoFSkirmish, 5)]
        public void KillingAHeroAndItsUnit_IsWorthWhatTheSystemsBookSays(EGameSystem system, int expected)
        {
            // GF/AoF: "+8 XP: Killed a Hero and its unit". GFF/AoFS: no such line, so it is worth the
            // same as the hero alone. A unit wiping out a joined hero's squad is the whole point of the
            // tier, so this is not a rounding detail - it is a third of a level.
            HordeRules.Configure(system);

            Assert.That(HordeExperience.XpForKill(hadJoinedHero: true, wasSoloHero: false),
                Is.EqualTo(expected));
        }

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void TheOtherTwoXpTiersAreTheSameInEveryBook(EGameSystem system)
        {
            // All four books agree on these, so a per-system flag must not disturb them.
            HordeRules.Configure(system);

            Assert.That(HordeExperience.XpForKill(hadJoinedHero: false, wasSoloHero: true), Is.EqualTo(5),
                "+5 XP: Killed a Hero unit");
            Assert.That(HordeExperience.XpForKill(hadJoinedHero: false, wasSoloHero: false), Is.EqualTo(3),
                "+3 XP: Killed a unit");
        }

        [TestCase(EGameSystem.GrimdarkFuture, 40, 20, 9f)]
        [TestCase(EGameSystem.AgeOfFantasy, 40, 20, 9f)]
        [TestCase(EGameSystem.GFFirefight, 30, 15, 6f)]
        [TestCase(EGameSystem.AoFSkirmish, 30, 15, 6f)]
        public void TheSizesMatchEachBook(EGameSystem system, int perObjective, int priority, float radius)
        {
            // Already correct; pinned here so the whole per-system table lives in one place and a future
            // Configure change cannot move one of them by accident.
            HordeRules.Configure(system);

            Assert.Multiple(() =>
            {
                Assert.That(HordeRules.PointsPerSeizedObjective, Is.EqualTo(perObjective));
                Assert.That(HordeRules.PriorityObjectiveBonus, Is.EqualTo(priority));
                Assert.That(HordeRules.DeploymentRadiusInches, Is.EqualTo(radius));
                Assert.That(HordeRules.ShopRangeInches, Is.EqualTo(radius));
            });
        }

        // ── The point reward is a MOMENTARY test, and that is the book ───────────────────────────────

        [Test]
        public async Task HoldingAMarkerAtTheRewardRound_Pays()
        {
            var scene = new HordeScene();
            scene.DefenderAt(scene.Objective.Position);

            int paid = await scene.AwardPoints();

            Assert.That(paid, Is.EqualTo(HordeRules.PointsPerSeizedObjective));
        }

        [Test]
        public async Task HavingSeizedAMarkerEarlierAndWalkedAway_PaysNothing()
        {
            // THE test, and the one that makes this fixture worth having. An auditor read the reward code
            // as a bug because it re-derives "seized" instead of reading ObjectiveData.OwnerID, which the
            // engine sets three lines earlier in the same stage - two definitions of seized, side by side.
            //
            // Both are right, for different jobs. The core rules' objective control IS sticky: "Markers
            // stay under the player's control even after leaving." Horde's point reward is not:
            //
            //   "Point Rewards: At the end of rounds 3, 6, 9, and 12 if any defending unit is within 3"
            //    of an objective marker while enemies aren't, then that objective counts as seized."
            //    - Horde Mode v3.6.0, identical in all four books
            //
            // "if any defending unit is within 3"" is present tense and is checked when the reward is
            // paid. So a defender that took a marker in round 2 and left scores nothing in round 3, and
            // the recorded owner is deliberately not consulted. Changing this to read OwnerID would pay
            // for ground the defenders no longer hold.
            var scene = new HordeScene();
            scene.Objective.SetOwner(scene.Defender);          // seized earlier, still on record
            scene.DefenderAt(new Position(40f, 40f));          // and long gone

            int paid = await scene.AwardPoints();

            Assert.That(paid, Is.EqualTo(0),
                "the book re-tests occupancy at the reward round - the recorded owner is not the rule here");
            Assert.That(scene.Objective.OwnerID, Is.EqualTo(scene.Defender),
                "and the owner record is untouched: it is still what the core mission scores on");
        }

        [Test]
        public async Task AContestedMarkerPaysNothing()
        {
            // "while enemies aren't" - an enemy standing on it blocks the reward even with a defender
            // there, which is the other half of the same sentence.
            var scene = new HordeScene();
            scene.DefenderAt(scene.Objective.Position);
            scene.EnemyAt(scene.Objective.Position);

            Assert.That(await scene.AwardPoints(), Is.EqualTo(0));
        }

        [Test]
        public async Task ThePriorityBonusIsPaidOnlyWithTheMarker()
        {
            var scene = new HordeScene();
            scene.Objective.SetHordePriority(true);
            scene.DefenderAt(scene.Objective.Position);

            Assert.That(await scene.AwardPoints(),
                Is.EqualTo(HordeRules.PointsPerSeizedObjective + HordeRules.PriorityObjectiveBonus));
        }

        [Test]
        public async Task ThePriorityBonusIsNotPaidForHoldingNothing()
        {
            // Guards the arithmetic's edge: a priority objective nobody holds must not pay the bonus on
            // its own.
            var scene = new HordeScene();
            scene.Objective.SetHordePriority(true);
            scene.DefenderAt(new Position(40f, 40f));

            Assert.That(await scene.AwardPoints(), Is.EqualTo(0));
        }

        // ── Scene ───────────────────────────────────────────────────────────────────────────────────

        /// <summary>One objective, one defending team and one enemy team, and a point-reward round.</summary>
        private sealed class HordeScene
        {
            private readonly GameDataStore _store;
            private readonly TestGameContext _context;

            public PlayerID Defender { get; }
            public PlayerID Enemy { get; }
            public ObjectiveData Objective { get; }

            public HordeScene()
            {
                _store = GameDataStore.GameDataStoreBuilder.GetDefault();
                Defender = new PlayerID(Guid.NewGuid());
                Enemy = new PlayerID(Guid.NewGuid());
                _store.Create(new TeamData(0, new List<PlayerID> { Defender }));
                _store.Create(new TeamData(1, new List<PlayerID> { Enemy }));

                // Player SLOTS, not just teams: HordeRules.DefendingTeamNumber reads the team of the
                // lowest filled slot, so the defending side is "whoever is in slot 0" and a scene with
                // teams alone has no defenders at all - every objective then reads as unheld and the
                // reward is silently zero. Which is a neat illustration of why this hook needed tests.
                // And the slots must be FILLED - IsFilled is "has a controller" - or DefenderPlayers
                // comes back empty and every objective reads as unheld.
                var slotA = new PlayerSlot(0, teamNumber: 0, Defender, new ArmyListFile(), _store);
                var slotB = new PlayerSlot(1, teamNumber: 1, Enemy, new ArmyListFile(), _store);
                slotA.AssignPlayerController(new SilentController("Defenders", Defender));
                slotB.AssignPlayerController(new SilentController("Raiders", Enemy));

                GameSettings settings = GameSettings.GetDefault();
                settings.GameMode = EGameMode.Horde;
                _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);

                // AddHordePoints needs a progress record to add to; without one it returns null and the
                // reward evaporates.
                GameProgressUtilities.WriteProgress(_store, new GameProgressData(
                    stage: EResumeStage.MainPhase, roundCount: 3,
                    teamActivateOrder: new List<int> { 0, 1 },
                    currentRoundTeamFinishOrder: new List<int>(),
                    currentTeamIndex: 0,
                    currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 }, { 1, 0 } },
                    unactivatedUnits: new List<DataBinding<UnitData>>(),
                    settings: settings,
                    horde: new HordeScore(0, 0)));


                // The three-argument ctor, not the two: only a NUMBERED Horde objective gets its damage
                // and priority bindings, and SetHordePriority is documented as a no-op without them. A
                // scene built with the plain ctor silently cannot have a priority objective at all.
                Objective = new ObjectiveData(new Position(24f, 24f), _store, hordeNumber: 1);
                _store.Create(Objective);
            }

            public void DefenderAt(Position at) => Unit(Defender, "Defenders", at);

            public void EnemyAt(Position at) => Unit(Enemy, "Raiders", at);

            /// <summary>Runs the real reward hook and returns what it paid.</summary>
            public async Task<int> AwardPoints()
            {
                await new HordeRoundHooks(_context).AwardWavePoints(wave: 1);
                return GameProgressUtilities.TryGetProgress(_store)?.Horde?.PointsEarned ?? 0;
            }

            /// <summary>The least a slot needs to count as filled.</summary>
            private sealed class SilentController : IPlayerController
            {
                public SilentController(string name, PlayerID id) { Name = name; ID = id; }
                public string Name { get; }
                public PlayerID ID { get; }
                public bool IsReady => true;
                public Presentation.IPresentationSink? PresentationSink => null;
                public event Action<bool>? OnReadyStateChanged;
                public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
                public Task WaitUntilReadyAsync() => Task.CompletedTask;
                public void SendLogMessage(string logMessage, TextColor color) { }
                public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
            }

            private void Unit(PlayerID owner, string name, Position at)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
                model.SetMaxWounds(3);
                DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
                var unit = new UnitData(owner, name, quality: 4, defense: 4,
                    modelBindings: new List<DataBinding<ModelData>> { modelBinding });
                DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
                _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            }
        }
    }
}
