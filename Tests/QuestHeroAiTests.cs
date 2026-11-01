using FDG.Ai.StarQuest;
using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - where a bot-run hero decides to go.
    ///
    /// <para>
    /// The gap being closed: <see cref="StarQuestAiBrain"/> is the ENEMY's flowchart and excludes heroes, so
    /// a bot party fell through to the generic solo bot, which plays Grimdark Future - it walks at enemies
    /// and at <c>IObjective</c> markers. A Quest mission is won by standing within 1" of a
    /// <see cref="QuestMarker"/>, and none of the three things that matter (objective markers, search
    /// tokens, the exit corner) is an <c>IObjective</c>. So the party milled about in the middle of the
    /// table and a played mission scored 0 XP.
    /// </para>
    /// <para>
    /// These pin the priority order, which is a policy rather than a rule from the book - OPR has no AI for
    /// the heroes, because they are the player's side.
    /// </para>
    /// </summary>
    [TestFixture]
    public class QuestHeroAiTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _heroes = default!;
        private PlayerID _enemy = default!;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _heroes = new PlayerID(Guid.NewGuid());
            _enemy = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _heroes }));
            _store.Create(new TeamData(1, new List<PlayerID> { _enemy }));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            settings.GameSystem = EGameSystem.GFStarQuest;
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        private DataBinding<UnitData> Hero(string name, float x, float z)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(x, z), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_heroes, name, quality: 4, defense: 5,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            unit.QuestHeroJson = new QuestHero { Name = name, Power = 3 }.ToJson();
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_heroes, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        /// <summary>A plain enemy unit, so "no hero plan" can be told apart from "nothing on the table".</summary>
        private DataBinding<UnitData> Enemy(float x, float z)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, z), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(_enemy, "Grunts", quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_enemy, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private QuestProgress Quest(Action<QuestProgress> edit)
        {
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_store);
            quest.Difficulty = 1;
            edit(quest);
            GameProgressUtilities.SetQuestProgress(_store, quest);
            return quest;
        }

        private static QuestMarker Objective(int id, float x, float z, bool secondary = false,
            string action = "remove") =>
            new() { Id = id, Kind = "objective", X = x, Z = z, ForSecondary = secondary, Action = action, Stat = 1 };

        private ITableState Table => _context.TableState;

        [Test]
        public void WithAnObjectiveMarkerOnTheTable_TheHeroGoesToIt()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Objective));
            Assert.That(plan.Goal.x, Is.EqualTo(24f).Within(0.01f));
            Assert.That(plan.Goal.z, Is.EqualTo(24f).Within(0.01f));
            Assert.That(plan.StopWithin, Is.EqualTo(QuestHeroAi.WorkReachInches),
                "the Objective skill reaches 1\", so that is how close the walk has to get.");
        }

        [Test]
        public void TheLiveObjectiveIsTheOneItWalksTo()
        {
            // The secondary's markers are placed as soon as the primary is done, so both sets sit on the
            // table at once. Walking to a finished objective's marker is the shape of a party that looks
            // busy and completes nothing.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.Markers.Add(Objective(1, 8f, 8f));                        // primary's, done with
                q.Markers.Add(Objective(2, 40f, 40f, secondary: true));     // the live one, far away
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Objective));
            Assert.That(plan.Goal.x, Is.EqualTo(40f).Within(0.01f),
                "it must cross the table to the live objective, not stand on the nearer finished one.");
        }

        [Test]
        public void ASeizedAndActivatedMarker_IsNoLongerWorthStandingOn()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.Markers.Add(new QuestMarker
                {
                    Id = 1, Kind = "objective", X = 8f, Z = 8f, Action = "seize", Activated = true,
                });
                q.Tokens.Add(new QuestToken { Id = 1, X = 9f, Z = 9f });   // inside the detour range
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Token),
                "an activated seize marker is finished, so the token is the next useful thing.");
        }

        [Test]
        public void WithNoObjectiveMarker_ItGoesForASearchToken()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.Tokens.Add(new QuestToken { Id = 1, X = 10f, Z = 10f });
                q.Tokens.Add(new QuestToken { Id = 2, X = 8f, Z = 8f });
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Token));
            Assert.That(plan.Goal.x, Is.EqualTo(8f).Within(0.01f), "the nearest token.");
        }

        [Test]
        public void CarryingAMarker_ItHeadsForTheDeliveryPoint()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            string me = hero.GetValue().ID.ID.ToString();
            Quest(q =>
            {
                q.Primary = 6;   // Delivery - the one primary with delivery markers to carry to
                q.Markers.Add(new QuestMarker { Id = 1, Kind = "objective", Action = "carry", CarriedBy = me });
                q.Markers.Add(new QuestMarker { Id = 2, Kind = "delivery", X = 36f, Z = 12f });
                q.Tokens.Add(new QuestToken { Id = 1, X = 7f, Z = 7f });   // a token right next to it
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Deliver),
                "a hero holding the objective delivers it - a token underfoot does not outrank that.");
            Assert.That(plan.Goal.x, Is.EqualTo(36f).Within(0.01f));
            Assert.That(plan.StopWithin, Is.LessThan(QuestHeroAi.DeliverReachInches),
                "it must stop INSIDE the 3\" delivery reach, not at it.");
        }

        [Test]
        public void CarryingACaptureMarker_ItHeadsForTheNearestCorner()
        {
            // Capture (secondary 3) scores "end an activation within 6\" of ANY corner" - there is no
            // marker to walk to. This is the case that stalled four of eight played missions: the carrier
            // had no plan, the solo bot took over, and it went looting with the objective in its hands.
            DataBinding<UnitData> hero = Hero("Vex", 8f, 8f);
            string me = hero.GetValue().ID.ID.ToString();
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 3;
                q.HeroCorner = 2;
                q.Markers.Add(new QuestMarker
                {
                    Id = 1, Kind = "objective", Action = "carry", ForSecondary = true, CarriedBy = me,
                });
                q.Tokens.Add(new QuestToken { Id = 1, X = 9f, Z = 9f });
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Deliver));
            Position expected = StarQuestMissionRules.Corner(QuestHeroAi.NearestCorner(hero.GetValue()));
            Assert.That(plan.Goal.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(plan.Goal.z, Is.EqualTo(expected.z).Within(0.01f),
                "any corner counts, so the nearest one is the shortest walk.");
        }

        [Test]
        public void CarryingARetrievalMarker_ItHeadsForThePartysOwnCorner()
        {
            // Retrieval (primary 3) is stricter than Capture: only the heroes' OWN deployment corner
            // scores, so the nearest corner would be a walk to nowhere.
            DataBinding<UnitData> hero = Hero("Vex", 8f, 8f);
            string me = hero.GetValue().ID.ID.ToString();
            Quest(q =>
            {
                q.Primary = 3;
                q.HeroCorner = 4;
                q.Markers.Add(new QuestMarker
                {
                    Id = 1, Kind = "objective", Action = "carry", ForSecondary = false, CarriedBy = me,
                });
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Deliver));
            Position own = StarQuestMissionRules.Corner(4);
            Assert.That(plan.Goal.x, Is.EqualTo(own.x).Within(0.01f));
            Assert.That(plan.Goal.z, Is.EqualTo(own.z).Within(0.01f),
                "Retrieval only scores in the party's own corner, whichever is nearest.");
        }

        [Test]
        public void CarryingAnEscortMarker_ItHeadsForTheExtractionPoint()
        {
            DataBinding<UnitData> hero = Hero("Vex", 8f, 8f);
            string me = hero.GetValue().ID.ID.ToString();
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 6;
                q.Markers.Add(new QuestMarker
                {
                    Id = 1, Kind = "objective", Action = "carry", ForSecondary = true, CarriedBy = me,
                });
                q.Markers.Add(new QuestMarker { Id = 2, Kind = "extraction", X = 42f, Z = 42f });
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Deliver));
            Assert.That(plan.Goal.x, Is.EqualTo(42f).Within(0.01f));
        }

        [Test]
        public void EverythingDone_TheWholePartyLeavesByOneCorner()
        {
            DataBinding<UnitData> a = Hero("Vex", 6f, 6f);
            DataBinding<UnitData> b = Hero("Mara", 40f, 40f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = true;
                q.HeroCorner = 3;
            });

            QuestHeroPlan? first = QuestHeroAi.Plan(Table, a.GetValue());
            QuestHeroPlan? second = QuestHeroAi.Plan(Table, b.GetValue());

            Assert.That(first!.Aim, Is.EqualTo(EHeroAim.Exfiltrate));
            Assert.That(second!.Aim, Is.EqualTo(EHeroAim.Exfiltrate));
            Assert.That(second.Goal.x, Is.EqualTo(first.Goal.x).Within(0.01f));
            Assert.That(second.Goal.z, Is.EqualTo(first.Goal.z).Within(0.01f),
                "every hero must pick the SAME corner - HeroesAtOneCorner wants them all at one, so two "
                + "heroes heading for opposite corners means nobody ever leaves.");
            Assert.That(first.StopWithin, Is.LessThan(6f),
                "the exit test measures the whole base within 6\", so the walk aims well inside it.");
        }

        [Test]
        public void LeavingOutranksLooting()
        {
            // The bug this is here for, found by playing it: with tokens ranked above the exit, a party
            // that had finished BOTH objectives by round 19 spent six more rounds wandering between the
            // two tokens it had not opened while reinforcements arrived every other round, and died
            // holding its loot. Leaving is what banks the XP.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = true;
                q.HeroCorner = 1;
                q.Tokens.Add(new QuestToken { Id = 1, X = 7f, Z = 7f });   // right next to the hero
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Exfiltrate));
        }

        [Test]
        public void TheSecondaryIsFinishedFirst_EvenThoughThePrimaryAloneWouldLetThemLeave()
        {
            // HeroesAtOneCorner only requires the primary, so a party COULD leave early - at the cost of
            // the secondary's XP. A bot party exists to prove a mission can be finished, so it stays.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = false;
                q.HeroCorner = 1;
                q.Markers.Add(Objective(1, 40f, 40f, secondary: true));
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Objective));
        }

        [Test]
        public void WithTheprimaryBankedAndAHeroDown_ItCutsLossesAndLeaves()
        {
            // Measured, not guessed: across eight played missions the party finished the primary seven
            // times and then died six of those, because alertness sends a reinforcement wave every other
            // round for as long as the party stays. A mission ends the moment the heroes gather in a corner
            // with the primary banked, so staying for the secondary with a hero already down trades a
            // finished mission for a wipe.
            DataBinding<UnitData> standing = Hero("Vex", 20f, 20f);
            DataBinding<UnitData> down = Hero("Mara", 22f, 20f);
            foreach (IModel model in down.GetValue().Models)
                model.DealWounds(model.TotalWounds - model.WoundsDealt);

            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = false;
                q.HeroCorner = 1;
                q.Markers.Add(Objective(1, 40f, 40f, secondary: true));
            });

            Assert.That(QuestHeroAi.PartyInTrouble(Table), Is.True);
            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, standing.GetValue());
            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Exfiltrate),
                "with a hero down and the primary banked, leaving beats a secondary it may not survive.");
        }

        [Test]
        public void AWholeParty_StillGoesForTheSecondary()
        {
            // The other side of the same rule: nobody is down, so the secondary's XP is worth going for.
            // Without this the bail-out would fire on every mission and the secondary would never be
            // attempted at all.
            DataBinding<UnitData> a = Hero("Vex", 20f, 20f);
            Hero("Mara", 22f, 20f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = false;
                q.HeroCorner = 1;
                q.Markers.Add(Objective(1, 40f, 40f, secondary: true));
            });

            Assert.That(QuestHeroAi.PartyInTrouble(Table), Is.False);
            Assert.That(QuestHeroAi.Plan(Table, a.GetValue())!.Aim, Is.EqualTo(EHeroAim.Objective));
        }

        [Test]
        public void WithNothingToWork_ThereIsNoHeroPlan_AndTheSoloBotFights()
        {
            // Important that this returns null rather than inventing a destination: with no markers and no
            // tokens the right behaviour is the existing one, and null is how the resolvers hand back over.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Enemy(12f, 12f);
            Quest(_ => { });

            Assert.That(QuestHeroAi.Plan(Table, hero.GetValue()), Is.Null);
        }

        [Test]
        public void AnEnemyUnitGetsNoHeroPlanAtAll()
        {
            // The enemy has its own flowchart and must keep it.
            DataBinding<UnitData> enemy = Enemy(6f, 6f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));

            Assert.That(QuestHeroAi.Plan(Table, enemy.GetValue()), Is.Null);
            Assert.That(QuestHeroAi.IsHero(enemy.GetValue()), Is.False);
        }

        [Test]
        public void AHeroAlreadyStandingOnTheMarker_IsInReach()
        {
            DataBinding<UnitData> hero = Hero("Vex", 24f, 24.5f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Objective));
            Assert.That(QuestHeroAi.InReach(hero.GetValue(), plan), Is.True,
                "already there - the resolvers read this to leave it standing and take the Skill action.");
        }

        [Test]
        public void AHeroAcrossTheTable_IsNotInReach()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(QuestHeroAi.InReach(hero.GetValue(), plan!), Is.False);
        }

        // ── The move itself ───────────────────────────────────────────────────────────────────────────
        // The brain being right is only half of it: the hero has to actually arrive. A first played
        // mission reached the marker on some seeds and never on others, so these pin the walk on a clean
        // table, where the only thing that can go wrong is the resolver.

        [Test]
        public async Task TheHeroWalksTowardTheMarker_AndKeepsWalkingUntilItArrives()
        {
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));
            var resolver = new StarQuestMovementResolver(Table, _heroes, null, new StarQuestAiMemory());

            float before = QuestMission.DistanceToPoint(hero.GetValue(), new Position(24f, 24f));
            float previous = before;
            for (int activation = 0; activation < 8; activation++)
            {
                var request = new DefineMovementPathRequest(_heroes, "Move", hero,
                    maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                    mainActivationMove: true);
                List<ModelMoveEntry> move = Unwrap(await resolver.Resolve(request));
                Apply(move);

                float now = QuestMission.DistanceToPoint(hero.GetValue(), new Position(24f, 24f));
                Assert.That(now, Is.LessThanOrEqualTo(previous + 0.01f),
                    $"activation {activation + 1} moved the hero AWAY from the marker ({previous:0.0}\" -> {now:0.0}\").");
                previous = now;
                if (now <= QuestHeroAi.WorkReachInches) break;
            }

            Assert.That(previous, Is.LessThanOrEqualTo(QuestHeroAi.WorkReachInches),
                $"after 8 activations the hero is still {previous:0.0}\" from a marker it started {before:0.0}\" "
                + "from - this is the failure that makes a mission unfinishable.");
        }

        [Test]
        public async Task ItStopsCloseEnoughToWorkTheMarker_NotOnTopOfIt()
        {
            // One move, a reachable marker: the stopping distance is the whole point. Too far and the
            // Objective skill reports "No objective marker within 1\"" - which is exactly what a party
            // that had learned to take its Skill action but not where to stand kept doing.
            DataBinding<UnitData> hero = Hero("Vex", 20f, 24f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));
            var resolver = new StarQuestMovementResolver(Table, _heroes, null, new StarQuestAiMemory());

            var request = new DefineMovementPathRequest(_heroes, "Move", hero,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                mainActivationMove: true);
            Apply(Unwrap(await resolver.Resolve(request)));

            float gap = QuestMission.DistanceToPoint(hero.GetValue(), new Position(24f, 24f));
            Assert.That(gap, Is.LessThanOrEqualTo(QuestHeroAi.WorkReachInches),
                "within 1\" of the marker, or the Objective skill cannot be used.");
        }

        [Test]
        public async Task AHeroAlreadyInReach_DoesNotWanderOff()
        {
            // In reach means the hero's job this activation is the Skill action, not a walk. Moving would
            // give up the position it spent two activations getting to.
            DataBinding<UnitData> hero = Hero("Vex", 24f, 24.5f);
            Quest(q => q.Markers.Add(Objective(1, 24f, 24f)));
            var resolver = new StarQuestMovementResolver(Table, _heroes, null, new StarQuestAiMemory());

            var request = new DefineMovementPathRequest(_heroes, "Move", hero,
                maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                mainActivationMove: true);
            Apply(Unwrap(await resolver.Resolve(request)));

            Assert.That(QuestMission.DistanceToPoint(hero.GetValue(), new Position(24f, 24f)),
                Is.LessThanOrEqualTo(QuestHeroAi.WorkReachInches), "it stays within reach.");
        }

        [Test]
        public async Task TheExitCornerIsReachedTooAndTheWholePartyPicksTheSameOne()
        {
            DataBinding<UnitData> hero = Hero("Vex", 24f, 24f);
            Quest(q =>
            {
                q.PrimaryDone = true;
                q.Secondary = 4;
                q.SecondaryDone = true;
                q.HeroCorner = 1;
            });
            var resolver = new StarQuestMovementResolver(Table, _heroes, null, new StarQuestAiMemory());
            Position corner = StarQuestMissionRules.Corner(1);

            float previous = QuestMission.DistanceToPoint(hero.GetValue(), corner);
            for (int activation = 0; activation < 10 && previous > QuestHeroAi.ExitReachInches; activation++)
            {
                var request = new DefineMovementPathRequest(_heroes, "Move", hero,
                    maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f,
                    mainActivationMove: true);
                Apply(Unwrap(await resolver.Resolve(request)));
                previous = QuestMission.DistanceToPoint(hero.GetValue(), corner);
            }

            Assert.That(previous, Is.LessThanOrEqualTo(QuestHeroAi.ExitReachInches),
                "a party that cannot walk to its own corner can never finish a mission it has won.");
        }

        private static List<ModelMoveEntry> Unwrap(CancellableResult<List<ModelMoveEntry>> result) =>
            result is Selected<List<ModelMoveEntry>> selected
                ? selected.Value
                : throw new AssertionException("the resolver declined the move instead of returning one.");

        /// <summary>Moves the models to the end of the path the resolver returned, as the engine would.</summary>
        private static void Apply(List<ModelMoveEntry> move)
        {
            foreach (ModelMoveEntry entry in move)
            {
                if (entry.Positions.Count == 0) continue;
                Position end = entry.Positions[^1];
                entry.Model.GetValue().SetPosition(end);
            }
        }

        [Test]
        public void HordeMode_HasNoObjectiveMarkers_SoTokensAreTheHeroPlan()
        {
            // Horde's objectives are IObjective markers, not QuestMarkers, so the only thing this brain
            // adds there is the tokens - and that is new: the bots never once walked onto one.
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestHorde;
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);

            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q =>
            {
                q.Difficulty = 0;                                           // Horde runs without one
                q.Tokens.Add(new QuestToken { Id = 1, X = 9f, Z = 9f });
            });

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Token));
        }

        [Test]
        public void ATokenAcrossTheTable_IsNotWorthTheWalk()
        {
            // The cap that saved Horde Mode. Without it the party crossed the table for loot, stopped
            // fighting, and so neither killed enemies nor died - waves piled up until a game that finished
            // in under a second could not finish in ten minutes. A hero with no plan fights, which in Horde
            // is the right answer.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Enemy(12f, 12f);
            Quest(q => q.Tokens.Add(new QuestToken { Id = 1, X = 40f, Z = 40f }));

            Assert.That(QuestHeroAi.Plan(Table, hero.GetValue()), Is.Null,
                $"a token more than {QuestHeroAi.TokenDetourInches}\" away is an errand, not a detour.");
        }

        [Test]
        public void AnObjectiveMarkerIsWorthAnyDistance_UnlikeAToken()
        {
            // The asymmetry is deliberate: the objective is the mission, loot is a bonus. A marker on the
            // far side of the table is still where the hero has to be; a token there is not.
            DataBinding<UnitData> hero = Hero("Vex", 6f, 6f);
            Quest(q => q.Markers.Add(Objective(1, 44f, 44f)));

            QuestHeroPlan? plan = QuestHeroAi.Plan(Table, hero.GetValue());

            Assert.That(plan!.Aim, Is.EqualTo(EHeroAim.Objective));
            Assert.That(QuestMission.DistanceToPoint(hero.GetValue(), plan.Goal),
                Is.GreaterThan(QuestHeroAi.TokenDetourInches * 4f),
                "far further than a token would ever be chased.");
        }
    }
}
