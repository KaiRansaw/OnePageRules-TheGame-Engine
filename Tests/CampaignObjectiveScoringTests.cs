using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #430 - scoring the six special objectives of Campaign Rules v3.6.0 at the end of a mission.
    ///
    /// <para>
    /// Before this they were <b>rolled and then never checked</b>: the campaign screen showed the player
    /// what they had drawn, the mission was played, and nothing ever asked whether they had done it - so
    /// the treasury bonus could not be earned at all.
    /// </para>
    ///
    /// <para>
    /// Four are read off the finished table. Two cannot be, and that is the interesting half: a resolved
    /// spell leaves no trace, and a finished table records that units died but never in what order. Those
    /// are tallied while the game runs, on the units' own campaign sheets - which is why #429 and #430 had to
    /// put data into the game rather than only read it.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignObjectiveScoringTests
    {
        private GameDataStore _store = null!;
        private TableState _table = null!;
        private PlayerID _me;
        private PlayerID _them;

        /// <summary>Grimdark Future's table, so the quarters divide at 36 / 24.</summary>
        private const float MidX = 36f, MidZ = 24f;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _table = new TableState(_store);
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
            WriteProgress(EGameSystem.GrimdarkFuture);
        }

        /// <summary>
        /// A progress record, which the scorer reads the game's system from. Written through
        /// GameProgressUtilities rather than created directly - TableState.Progress reads the FIRST
        /// GameProgressData in the store, so a second Create would be silently ignored.
        /// </summary>
        private void WriteProgress(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            GameProgressUtilities.WriteProgress(_store, new GameProgressData(
                EResumeStage.MainPhase, roundCount: 4, teamActivateOrder: new List<int> { 1, 2 },
                currentRoundTeamFinishOrder: new List<int>(), currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int>(),
                unactivatedUnits: new List<DataBinding<UnitData>>(), settings: settings));
        }

        private DataBinding<UnitData> Unit(PlayerID owner, params Position[] modelPositions)
        {
            var models = new List<DataBinding<ModelData>>();
            foreach (Position position in modelPositions)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), position, _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, $"Unit {modelPositions[0].x:0}/{modelPositions[0].z:0}",
                quality: 4, defense: 4, modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private void GiveSheet(DataBinding<UnitData> unit, int heroKills = 0, int spellsCast = 0,
                               bool firstBlood = false)
        {
            var sheet = new StrikeUnit
            {
                HeroKills = heroKills, SpellsCast = spellsCast, DrewFirstBlood = firstBlood,
                TeamFile = "army.fdgarmy",
            };
            unit.GetValue().StrikeUnitJson = sheet.ToJson();
        }

        private void Objective(Position at, PlayerID? owner)
        {
            var objective = new ObjectiveData(at, _store);
            if (owner.HasValue) objective.SetOwner(owner.Value);
            _store.Create(objective);
        }

        // -- Hexer: counted while the game runs ------------------------------------------------------

        [Test]
        public void Hexer_NeedsFourSuccessfulCasts()
        {
            DataBinding<UnitData> mine = Unit(_me, new Position(10f, 10f));
            GiveSheet(mine, spellsCast: CampaignObjectives.HexerSpells - 1);

            Assert.That(CampaignObjectives.Score(_table, _me, "Hexer").Completed, Is.False,
                "three casts is not four.");

            GiveSheet(mine, spellsCast: CampaignObjectives.HexerSpells);

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Hexer");
            Assert.That(scored.Completed, Is.True);
            Assert.That(scored.Detail, Does.Contain("4"));
        }

        /// <summary>
        /// The army's casts, not one unit's: two casters with two each is four, which is what the book
        /// asks for ("successfully cast four or more spells", of the army's own doing).
        /// </summary>
        [Test]
        public void Hexer_SumsTheWholeArmysCasters()
        {
            GiveSheet(Unit(_me, new Position(10f, 10f)), spellsCast: 2);
            GiveSheet(Unit(_me, new Position(14f, 10f)), spellsCast: 2);

            Assert.That(CampaignObjectives.Score(_table, _me, "Hexer").Completed, Is.True);
        }

        [Test]
        public void Hexer_CountsOnlyYourOwnCasts()
        {
            GiveSheet(Unit(_them, new Position(60f, 40f)), spellsCast: 6);
            GiveSheet(Unit(_me, new Position(10f, 10f)));

            Assert.That(CampaignObjectives.Score(_table, _me, "Hexer").Completed, Is.False,
                "the enemy's casters do not complete your objective.");
            Assert.That(CampaignObjectives.Score(_table, _them, "Hexer").Completed, Is.True);
        }

        // -- Annihilator: who was FIRST --------------------------------------------------------------

        [Test]
        public void Annihilator_GoesToWhoeverHoldsTheFirstKill()
        {
            GiveSheet(Unit(_me, new Position(10f, 10f)), firstBlood: true);
            GiveSheet(Unit(_them, new Position(60f, 40f)));

            Assert.That(CampaignObjectives.Score(_table, _me, "Annihilator").Completed, Is.True);

            CampaignObjectiveResult theirs = CampaignObjectives.Score(_table, _them, "Annihilator");
            Assert.That(theirs.Completed, Is.False,
                "only the first kill of the game counts, and it was not theirs.");
            Assert.That(theirs.Detail, Does.Contain("an opponent"),
                "and they should be told it went to somebody, not that nothing died.");
        }

        [Test]
        public void Annihilator_FailsForEveryoneWhenNothingDied()
        {
            GiveSheet(Unit(_me, new Position(10f, 10f)));

            Assert.That(CampaignObjectives.Score(_table, _me, "Annihilator").Completed, Is.False);
            Assert.That(CampaignObjectives.Score(_table, _me, "Annihilator").Detail,
                Does.Contain("nobody was first"));
        }

        // -- Headhunter: off the campaign sheets -----------------------------------------------------

        [Test]
        public void Headhunter_ReadsTheHeroKillsOnYourOwnSheets()
        {
            DataBinding<UnitData> mine = Unit(_me, new Position(10f, 10f));
            GiveSheet(mine, heroKills: 1);
            DataBinding<UnitData> theirs = Unit(_them, new Position(60f, 40f));
            GiveSheet(theirs, heroKills: 3);

            Assert.That(CampaignObjectives.Score(_table, _me, "Headhunter").Completed, Is.True);
            Assert.That(CampaignObjectives.Score(_table, _me, "Headhunter").Detail, Does.Contain("1"));
        }

        [Test]
        public void Headhunter_FailsWithNoHeroKills()
        {
            DataBinding<UnitData> mine = Unit(_me, new Position(10f, 10f));
            GiveSheet(mine);

            Assert.That(CampaignObjectives.Score(_table, _me, "Headhunter").Completed, Is.False);
        }

        /// <summary>
        /// A hero killed by a unit that then died itself still counts: the sheet rides the unit and the
        /// unit is still on the table, dead. Worth pinning - "killed one or more enemy Heroes" says
        /// nothing about surviving to tell the tale.
        /// </summary>
        [Test]
        public void Headhunter_CountsAKillByAUnitThatDiedItself()
        {
            DataBinding<UnitData> mine = Unit(_me, new Position(10f, 10f));
            GiveSheet(mine, heroKills: 1);
            foreach (IModel model in mine.GetValue().Models) model.DealWounds(model.TotalWounds);
            Assert.That(mine.GetValue().GetIsAlive(), Is.False, "precondition: the killer is dead.");

            Assert.That(CampaignObjectives.Score(_table, _me, "Headhunter").Completed, Is.True);
        }

        // -- Dominator: one unit fully within each quarter -------------------------------------------

        [Test]
        public void Dominator_NeedsAUnitFullyWithinAllFourQuarters()
        {
            Unit(_me, new Position(10f, 10f));                       // bottom-left
            Unit(_me, new Position(60f, 10f));                       // bottom-right
            Unit(_me, new Position(10f, 40f));                       // top-left
            Assert.That(CampaignObjectives.Score(_table, _me, "Dominator").Completed, Is.False,
                "three quarters is not four.");

            Unit(_me, new Position(60f, 40f));                       // top-right

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Dominator");
            Assert.That(scored.Completed, Is.True);
            Assert.That(scored.Detail, Does.Contain("4 of the 4"));
        }

        /// <summary>
        /// "Fully within" is the book's wording, so a unit with a foot in two quarters holds neither.
        /// Without this a single straddling unit would be credited with a quarter it is only half in.
        /// </summary>
        [Test]
        public void Dominator_AStraddlingUnitHoldsNoQuarter()
        {
            Unit(_me, new Position(MidX - 2f, 10f), new Position(MidX + 2f, 10f));
            Unit(_me, new Position(60f, 10f));
            Unit(_me, new Position(10f, 40f));
            Unit(_me, new Position(60f, 40f));

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Dominator");
            Assert.That(scored.Completed, Is.False);
            Assert.That(scored.Detail, Does.Contain("3 of the 4"),
                "the straddling unit holds nothing, so only three quarters are held.");
        }

        [Test]
        public void Dominator_IgnoresEnemyUnitsInTheEmptyQuarters()
        {
            Unit(_me, new Position(10f, 10f));
            Unit(_me, new Position(60f, 10f));
            Unit(_me, new Position(10f, 40f));
            Unit(_them, new Position(60f, 40f));

            Assert.That(CampaignObjectives.Score(_table, _me, "Dominator").Completed, Is.False);
        }

        /// <summary>
        /// The quarters divide at the middle of THIS game's table. Firefight plays on 48x48 where
        /// Grimdark Future plays on 72x48, so a unit at x=40 is in the right half of a Firefight table
        /// and the left half of a GF one. Reading the wrong table size would put the line in the wrong
        /// place and score the objective against a different board than the one that was played.
        /// </summary>
        [Test]
        public void Dominator_QuartersTheTableTheGameWasPlayedOn()
        {
            WriteProgress(EGameSystem.GFFirefight);          // 48 x 48, so the quarters split at 24/24
            Unit(_me, new Position(10f, 10f));
            Unit(_me, new Position(40f, 10f));
            Unit(_me, new Position(10f, 40f));
            Unit(_me, new Position(40f, 40f));

            Assert.That(CampaignObjectives.Score(_table, _me, "Dominator").Completed, Is.True,
                "on a 48x48 table those four corners are four different quarters.");
        }

        // -- Intimidator: needs #429's deployment zones ----------------------------------------------

        [Test]
        public void Intimidator_NeedsMostOfYourUnitsInAnEnemyZone()
        {
            GameProgressUtilities.SetDeploymentZones(_store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
                [2] = new ZoneRect(0f, 72f, 36f, 48f),
            });

            Unit(_me, new Position(30f, 40f));     // in the enemy zone
            Unit(_me, new Position(32f, 42f));     // in the enemy zone
            Unit(_me, new Position(30f, 6f));      // back home

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Intimidator");
            Assert.That(scored.Completed, Is.True, "two of three is most.");
            Assert.That(scored.Detail, Does.Contain("2 of 3"));
        }

        [Test]
        public void Intimidator_HalfIsNotMost()
        {
            GameProgressUtilities.SetDeploymentZones(_store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
                [2] = new ZoneRect(0f, 72f, 36f, 48f),
            });

            Unit(_me, new Position(30f, 40f));
            Unit(_me, new Position(30f, 6f));

            Assert.That(CampaignObjectives.Score(_table, _me, "Intimidator").Completed, Is.False,
                "\"most\" is strictly more than half.");
        }

        /// <summary>
        /// Destroyed units are not counted against you. The table keeps them, so counting them would make
        /// the objective HARDER the worse the mission went - and "most of your units" at the end of the
        /// game can only mean the ones still standing. Found by reading the code back, not by a test
        /// failing: the first version counted the dead in the denominator.
        /// </summary>
        [Test]
        public void Intimidator_CountsOnlySurvivingUnits()
        {
            GameProgressUtilities.SetDeploymentZones(_store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
                [2] = new ZoneRect(0f, 72f, 36f, 48f),
            });

            Unit(_me, new Position(30f, 40f));                                    // alive, in their zone
            DataBinding<UnitData> lost = Unit(_me, new Position(30f, 6f));        // dead, back home
            DataBinding<UnitData> alsoLost = Unit(_me, new Position(32f, 6f));    // dead, back home
            foreach (DataBinding<UnitData> gone in new[] { lost, alsoLost })
                foreach (IModel model in gone.GetValue().Models) model.DealWounds(model.TotalWounds);

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Intimidator");
            Assert.That(scored.Completed, Is.True,
                "one surviving unit, and it is in the enemy zone - that is most of what is left.");
            Assert.That(scored.Detail, Does.Contain("1 of 1"));
        }

        [Test]
        public void Intimidator_YourOwnZoneDoesNotCount()
        {
            GameProgressUtilities.SetDeploymentZones(_store, new Dictionary<int, ZoneRect>
            {
                [1] = new ZoneRect(0f, 72f, 0f, 12f),
                [2] = new ZoneRect(0f, 72f, 36f, 48f),
            });

            Unit(_me, new Position(30f, 6f));
            Unit(_me, new Position(32f, 6f));
            Unit(_me, new Position(34f, 6f));

            Assert.That(CampaignObjectives.Score(_table, _me, "Intimidator").Completed, Is.False,
                "standing in your own deployment zone intimidates nobody.");
        }

        /// <summary>
        /// A game that recorded no zones - an older save - must say it could not be scored rather than
        /// report a failure. "You did not do it" and "nobody knows" are different answers, and the
        /// difference belongs in the log the player reads.
        /// </summary>
        [Test]
        public void Intimidator_SaysSoWhenTheGameRecordedNoZones()
        {
            Unit(_me, new Position(30f, 40f));

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Intimidator");
            Assert.That(scored.Completed, Is.False);
            Assert.That(scored.Detail, Does.Contain("could not be scored"));
        }

        // -- Strategist: most objectives held --------------------------------------------------------

        [Test]
        public void Strategist_NeedsMoreObjectivesThanAnyoneElse()
        {
            Objective(new Position(20f, 20f), _me);
            Objective(new Position(40f, 20f), _me);
            Objective(new Position(60f, 20f), _them);

            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Strategist");
            Assert.That(scored.Completed, Is.True);
            Assert.That(scored.Detail, Does.Contain("2 objective"));
        }

        [Test]
        public void Strategist_ATieIsNotControl()
        {
            Objective(new Position(20f, 20f), _me);
            Objective(new Position(60f, 20f), _them);

            Assert.That(CampaignObjectives.Score(_table, _me, "Strategist").Completed, Is.False,
                "\"most\" means more than anyone else, and one each is not more.");
        }

        [Test]
        public void Strategist_UnheldMarkersCountForNobody()
        {
            Objective(new Position(20f, 20f), null);
            Objective(new Position(40f, 20f), null);
            Objective(new Position(60f, 20f), _me);

            Assert.That(CampaignObjectives.Score(_table, _me, "Strategist").Completed, Is.True,
                "one held beats two held by nobody.");
        }

        [Test]
        public void Strategist_FailsWhenYouHoldNothing()
        {
            Objective(new Position(20f, 20f), _them);

            Assert.That(CampaignObjectives.Score(_table, _me, "Strategist").Completed, Is.False);
        }

        // -- The six, and nothing else ---------------------------------------------------------------

        /// <summary>
        /// Every objective the book rolls must be scorable. This is the guard against the table and the
        /// scorer drifting apart - a renamed entry would otherwise silently become unscoreable, and the
        /// player would just never earn that bonus again.
        /// </summary>
        [Test]
        public void EveryObjectiveTheBookRollsIsScored()
        {
            Unit(_me, new Position(10f, 10f));
            foreach ((string name, _) in CampaignTables.SpecialObjectives)
            {
                CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, name);
                Assert.That(scored.Objective, Is.EqualTo(name));
                Assert.That(scored.Detail, Does.Not.Contain("not one of the book's six"),
                    $"{name} is in the book's table but the scorer does not know it.");
            }
        }

        [Test]
        public void AnUnknownObjectiveIsReportedRatherThanSilentlyFailed()
        {
            CampaignObjectiveResult scored = CampaignObjectives.Score(_table, _me, "Hoarder");

            Assert.That(scored.Completed, Is.False);
            Assert.That(scored.Detail, Does.Contain("not one of the book's six"));
        }
    }
}
