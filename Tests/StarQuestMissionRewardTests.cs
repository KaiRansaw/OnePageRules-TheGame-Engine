using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// What a Star Quest mission pays out, pinned against **GFSQ Campaign Builder v2.1.0**.
    ///
    /// <para>
    /// QA 6.x: "every hero got 5c and 0xp". 5c is Hazard Pay at Difficulty 1, so the hazard half was
    /// working and the XP half was not. These tests exist because the payout cannot be reached by playing:
    /// the Star Quest AI is the ENEMY flowchart (shoot or charge the nearest hero, else walk to the nearest
    /// objective) and has no Discover or objective interaction, so a bot party fights a mission but never
    /// completes one - and mission rewards, the boss-fight bonus and exfiltration all sit behind "primary
    /// objective complete". Until a hero-side AI exists, this fixture IS the coverage (#413 gap note).
    /// </para>
    ///
    /// <para>The book, verbatim in substance:
    /// Hazard Pay = 5c x Difficulty, always. Primary = double the Difficulty in XP. Secondary = the
    /// Difficulty in XP. Boss wave destroyed = double the Difficulty in XP on top.</para>
    /// </summary>
    [TestFixture]
    public class StarQuestMissionRewardTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new PlayerSlotInfo(_player, slotID: 0, teamNumber: 0, name: "Host", isFilled: true));
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = EGameMode.StarQuestMission;
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings,
                playerRequester: new DefaultAnsweringRequester());
            GameWideConstants.ConfigureTable(EGameSystem.GFStarQuest);
        }

        [TearDown]
        public void TearDown()
        {
            QuestChallenges.ForceForTests = null;
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
        }

        // ── The arithmetic, straight off the book ────────────────────────────────────────────────────

        [TestCase(1, 5)]
        [TestCase(2, 10)]
        [TestCase(3, 15)]
        [TestCase(4, 20)]
        public void HazardPay_Is5cTimesDifficulty(int difficulty, int expected) =>
            Assert.That(StarQuestMissionRules.HazardPay(difficulty), Is.EqualTo(expected));

        // The primary pays double the difficulty, the secondary pays the difficulty, and neither pays
        // anything when it is not done. Difficulty 1 with the primary done is the case from QA 6.x: the
        // hero must get 5c AND 2 XP, so 0 XP there was never right.
        [TestCase(1, true, false, 2)]
        [TestCase(1, true, true, 3)]
        [TestCase(1, false, false, 0)]
        [TestCase(2, true, false, 4)]
        [TestCase(2, true, true, 6)]
        [TestCase(3, true, true, 9)]
        [TestCase(4, true, true, 12)]
        [TestCase(4, false, true, 4)]
        public void Xp_FollowsTheObjectivesCompleted(int difficulty, bool primary, bool secondary, int expected) =>
            Assert.That(StarQuestMissionRules.Xp(difficulty, primary, secondary), Is.EqualTo(expected));

        // ── The levelling table (was the v2.0.1 one until 2026-10-30) ───────────────────────────────

        // A different curve end to end, not a tweak. Written out in full so a future edit has to disagree
        // with the book explicitly rather than drift.
        [Test]
        public void XpForLevel_MatchesTheCampaignBuilder210Table()
        {
            Assert.That(QuestProgression.XpForLevel,
                Is.EqualTo(new[] { 0, 0, 3, 6, 12, 18, 27, 36, 48, 60, 75, 90, 108, 126, 147, 168 }));
            Assert.That(QuestProgression.MaxLevel, Is.EqualTo(15));
        }

        // The boundaries, because an off-by-one here is a hero levelling a whole mission early. Under the
        // old table 2 XP was already level 2; the book says level 2 costs 3.
        [TestCase(0, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(5, 2)]
        [TestCase(6, 3)]
        [TestCase(11, 3)]
        [TestCase(12, 4)]
        [TestCase(167, 14)]
        [TestCase(168, 15)]
        [TestCase(500, 15)]
        public void LevelForXp_UsesTheNewThresholds(int xp, int expectedLevel) =>
            Assert.That(QuestProgression.LevelForXp(xp), Is.EqualTo(expectedLevel));

        // Levels 4 and 8 were swapped against the book: +1 Qua sat at 4 (the book puts it at 8) and +2 Pow
        // sat at 8 (the book puts it at 4). Quality is the strongest improvement on the list, so the swap
        // handed it out four levels early.
        [Test]
        public void LevelOptions_MatchTheBookIncludingLevels4And8()
        {
            Assert.That(QuestProgression.LevelOptions[4], Is.EqualTo(new[] { "+2 Tou", "+2 Pow" }),
                "the book's level 4 is +2 Tou / +2 Pow - +1 Qua belongs at level 8.");
            Assert.That(QuestProgression.LevelOptions[8], Is.EqualTo(new[] { "+2 Tou", "+1 Qua" }));
            Assert.That(QuestProgression.LevelOptions[2], Is.EqualTo(new[] { "+1 Tou", "+1 Skill" }));
            Assert.That(QuestProgression.LevelOptions[12], Is.EqualTo(new[] { "+2 Tou", "+1 Qua" }));
            // Every level from 2 up offers something, or a hero that reaches it gets nothing at all.
            for (int level = 2; level <= QuestProgression.MaxLevel; level++)
                Assert.That(QuestProgression.LevelOptions[level], Is.Not.Empty, $"level {level} offers nothing.");
        }

        // ── The payout actually landing on the hero sheet ───────────────────────────────────────────

        // The end-to-end check QA 6.x needed: run the real victory stage and read the hero's own sheet
        // afterwards. The arithmetic being right is not the same as the credits and XP being written.
        [TestCase(1, true, false, 5, 2)]
        [TestCase(1, true, true, 5, 3)]
        [TestCase(2, true, true, 10, 6)]
        [TestCase(4, true, true, 20, 12)]
        public async Task VictoryStage_WritesHazardPayAndXpToEveryHerosSheet(
            int difficulty, bool primary, bool secondary, int expectedPay, int expectedXp)
        {
            UnitData a = MakeHero("Vex", credits: 30);
            UnitData b = MakeHero("Mara", credits: 0);
            SetProgress(difficulty, primary, secondary, outcome: "success");

            await RunVictoryStage();

            foreach ((UnitData unit, int before) in new[] { (a, 30), (b, 0) })
            {
                QuestHero sheet = Sheet(unit);
                Assert.That(sheet.Credits, Is.EqualTo(before + expectedPay),
                    $"{sheet.Name} should get {expectedPay}c of hazard pay.");
                Assert.That(sheet.Xp, Is.EqualTo(expectedXp), $"{sheet.Name} should get {expectedXp} XP.");
            }
        }

        // Hazard Pay is paid "always", so it lands even with nothing completed - that half of QA 6.x was
        // never broken, and this pins it so a fix to the XP half cannot quietly take the credits away.
        [Test]
        public async Task VictoryStage_PaysHazardPayEvenWithNoObjectivesDone()
        {
            UnitData hero = MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 3, primary: false, secondary: false, outcome: "success");

            await RunVictoryStage();

            Assert.That(Sheet(hero).Credits, Is.EqualTo(15));
            Assert.That(Sheet(hero).Xp, Is.Zero);
        }

        // "If all enemies from the boss wave are destroyed, players earn double as much XP as the chosen
        // Difficulty level when ending the mission" - on top of the objective XP.
        [Test]
        public async Task VictoryStage_AddsDoubleDifficultyXp_WhenTheBossWaveIsDestroyed()
        {
            UnitData hero = MakeHero("Vex", credits: 0);
            UnitData boss = MakeEnemy("Warlord", alive: false);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "success");
            QuestChallenges.StartBossWave(_context, new[] { boss });

            await RunVictoryStage();

            // 2x2 primary + 2 secondary + 2x2 boss = 10.
            Assert.That(Sheet(hero).Xp, Is.EqualTo(10));
        }

        // A boss wave still standing pays nothing extra. Leaving with it alive is legal (the book gates
        // Mission End on the primary objective only), it just forfeits the bonus.
        [Test]
        public async Task VictoryStage_AddsNothing_WhenTheBossWaveSurvives()
        {
            UnitData hero = MakeHero("Vex", credits: 0);
            UnitData boss = MakeEnemy("Warlord", alive: true);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "success");
            QuestChallenges.StartBossWave(_context, new[] { boss });

            await RunVictoryStage();

            Assert.That(Sheet(hero).Xp, Is.EqualTo(6), "4 for the primary + 2 for the secondary, no bonus.");
        }

        // A failed mission pays nothing at all - every hero was unconscious, so there was no mission end
        // to collect rewards at.
        [Test]
        public async Task VictoryStage_PaysNothingOnAFailure()
        {
            UnitData hero = MakeHero("Vex", credits: 30);
            SetProgress(difficulty: 4, primary: true, secondary: true, outcome: "failed");

            await RunVictoryStage();

            Assert.That(Sheet(hero).Credits, Is.EqualTo(30));
            Assert.That(Sheet(hero).Xp, Is.Zero);
        }

        // Enough XP to cross a threshold must actually level the hero, using the new table: at Difficulty 4
        // with both objectives done a fresh hero earns 12 XP, which the book puts exactly at level 4.
        [Test]
        public async Task VictoryStage_LevelsAHeroUpOnTheNewThresholds()
        {
            UnitData hero = MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 4, primary: true, secondary: true, outcome: "success");

            await RunVictoryStage();

            QuestHero sheet = Sheet(hero);
            Assert.That(sheet.Xp, Is.EqualTo(12));
            Assert.That(sheet.Level, Is.EqualTo(4), "12 XP is level 4 in the v2.1.0 table (it was level 5 in v2.0.1).");
        }

        // ── Mission End is gated on the primary objective ───────────────────────────────────────────

        // "Once the Heroes have completed the primary objective, they must all be within 6\" of the same
        // table corner at the end of any round in order to end the mission and collect rewards." Without
        // this gate a party could walk into a corner on round 1 and collect hazard pay for nothing - which
        // is the shape QA 6.x describes (5c, 0 XP).
        // NearCorner rather than Corner on purpose: a unit whose only model sits at exactly (0,0) reads as
        // OFF the battlefield (GetIsOnBattlefield treats the origin as the not-placed sentinel), and corner
        // 1 is the origin. Heroes stand within 6" of the corner in play, so this is also the real case.
        [Test]
        public void HeroesAtOneCorner_IsRefusedUntilThePrimaryObjectiveIsDone()
        {
            MakeHeroAt("Vex", StarQuestMissionRules.NearCorner(1));
            MakeHeroAt("Mara", StarQuestMissionRules.NearCorner(1, 2f));
            SetProgress(difficulty: 1, primary: false, secondary: false, outcome: "");

            Assert.That(QuestMission.HeroesAtOneCorner(_context), Is.Null,
                "the mission must not end before the primary objective is complete.");

            SetProgress(difficulty: 1, primary: true, secondary: false, outcome: "");

            Assert.That(QuestMission.HeroesAtOneCorner(_context), Is.EqualTo(1),
                "with the primary done, heroes gathered in corner 1 may leave.");
        }

        // The secondary deliberately does NOT gate the exit: "the secondary objective doesn't need to be
        // completed to end the mission".
        [Test]
        public void HeroesAtOneCorner_DoesNotWaitForTheSecondaryObjective()
        {
            MakeHeroAt("Vex", StarQuestMissionRules.NearCorner(3));
            SetProgress(difficulty: 1, primary: true, secondary: false, outcome: "");
            QuestProgress q = QuestMission.Progress(_context);
            q.Secondary = 4;
            q.SecondaryDone = false;
            QuestMission.Save(_context, q);

            Assert.That(QuestMission.HeroesAtOneCorner(_context), Is.EqualTo(3));
        }

        // ── The boss fight is OFFERED, not automatic ────────────────────────────────────────────────

        // "After completing all objectives, players may choose to trigger a boss fight." It is an OPTIONAL
        // CHALLENGE, so with the challenge switched off there is no offer at all - which is almost certainly
        // what QA 4.1 / 5.5 saw. This pins that the gate is the challenge, not a missing feature.
        [Test]
        public async Task BossFight_IsNotOfferedWhenTheChallengeIsOff()
        {
            MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "");

            Assert.That(await QuestChallenges.OfferBossFight(_context, "Every objective is done."), Is.False);
        }

        // The offer is a YesNoRequest whose own stated default is NO - a boss fight makes the game harder,
        // so nobody gets one by failing to answer. This accepts it explicitly, which is what a player
        // clicking yes does.
        [Test]
        public async Task BossFight_IsOfferedWhenTheChallengeIsOn()
        {
            MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "");
            TurnOnBossFights();
            var accepting = new TestGameContext(_store, new FixedDiceRoller(4), settings: _context.Settings,
                playerRequester: new DefaultAnsweringRequester(yes: true));

            Assert.That(await QuestChallenges.OfferBossFight(accepting, "Every objective is done."), Is.True);
        }

        // And it defaults to NO, so an unanswered prompt never springs a double wave on the party.
        [Test]
        public async Task BossFight_DefaultsToNo()
        {
            MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "");
            TurnOnBossFights();

            Assert.That(await QuestChallenges.OfferBossFight(_context, "Every objective is done."), Is.False);
        }

        // Declining with askOnce leaves a marker so the question is not repeated every round for the rest
        // of the mission.
        [Test]
        public async Task BossFight_DecliningOnceStopsTheQuestionComingBack()
        {
            MakeHero("Vex", credits: 0);
            SetProgress(difficulty: 2, primary: true, secondary: true, outcome: "");
            TurnOnBossFights();
            var declining = new TestGameContext(_store, new FixedDiceRoller(4), settings: _context.Settings,
                playerRequester: new DefaultAnsweringRequester(yes: false));

            Assert.That(await QuestChallenges.OfferBossFight(declining, "Every objective is done.", askOnce: true), Is.False);
            Assert.That(QuestMission.Progress(declining).BossFight, Is.EqualTo(-1), "declined, and recorded as declined.");
            Assert.That(await QuestChallenges.OfferBossFight(declining, "Every objective is done."), Is.False,
                "a declined boss fight is not offered again this mission.");
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private void TurnOnBossFights()
        {
            QuestProgress q = QuestMission.Progress(_context);
            q.Challenges = new List<string> { QuestChallenges.BossFights };
            QuestMission.Save(_context, q);
        }

        private void SetProgress(int difficulty, bool primary, bool secondary, string outcome)
        {
            QuestProgress q = QuestMission.Progress(_context);
            q.Difficulty = difficulty;
            q.Primary = 1;
            q.PrimaryDone = primary;
            q.SecondaryDone = secondary;
            if (secondary) q.Secondary = 1;
            q.Outcome = outcome;
            QuestMission.Save(_context, q);
        }

        private Task RunVictoryStage() =>
            new StarQuestMissionVictoryStage(_context, new NoOpLayer<IGameContext>()).Enter(_context);

        private static QuestHero Sheet(UnitData unit) => QuestHero.FromJson(unit.QuestHeroJson)!;

        private UnitData MakeHero(string name, int credits) =>
            MakeHeroAt(name, new Position(10f, 10f), credits);

        private UnitData MakeHeroAt(string name, Position at, int credits = 0)
        {
            UnitData unit = MakeUnit(name, at, alive: true);
            unit.QuestHeroJson = new QuestHero
            {
                Name = name, Faction = "Test", BaseHeroName = "Leader", BaseCost = 40,
                Class = "Soldier", Power = 3, Credits = credits, Tough = 9, Level = 1, Xp = 0,
            }.ToJson();
            unit.QuestPower = 3;
            return unit;
        }

        private UnitData MakeEnemy(string name, bool alive) => MakeUnit(name, new Position(30f, 30f), alive);

        /// <summary>
        /// A unit the engine can actually find. <c>StarQuestHordeRules.Heroes</c> walks
        /// <c>ArmyData.UnitBindings</c>, not the store's units, so a unit created on its own is invisible to
        /// every Star Quest rule - the fixture has to put it in an army or the whole mission looks empty.
        /// </summary>
        private UnitData MakeUnit(string name, Position at, bool alive)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: at, gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            // Kill by dealing its wounds, the way the game does - setting a field would not go through the
            // binding every GetIsAlive() check reads.
            if (!alive) modelBinding.GetValue().DealWounds(modelBinding.GetValue().TotalWounds);
            var unit = new UnitData(_player, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataBinding<UnitData> unitBinding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_player, new List<DataBinding<UnitData>> { unitBinding }));
            return unitBinding.GetValue();
        }

        /// <summary>
        /// Answers every request the way an automated answerer does: a yes/no question gets its own stated
        /// default (or a forced answer), a list gets its first option. The victory stage opens the
        /// between-missions item handover for each hero, so a requester that never answers would hang.
        /// </summary>
        private sealed class DefaultAnsweringRequester : IPlayerRequestByID
        {
            private readonly bool? _yes;

            public DefaultAnsweringRequester(bool? yes = null) => _yes = yes;

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                object? answer = request switch
                {
                    YesNoRequest yn => _yes ?? yn.DefaultAnswer,
                    StringSelectionRequest s => s.ValidOptions.Count > 0 ? s.ValidOptions[0] : "",
                    _ => default(TReply),
                };
                return Task.FromResult((TReply)answer!);
            }
        }
    }
}
