using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - a Star Quest game survives being saved and reloaded.
    //
    // Before this fixture, NO save/load test mentioned Quest at all. That is the least acceptable gap in
    // the mode: a Horde run is ten waves long, a campaign mission is most of an evening, and the thing
    // being risked is not a position on a table but the heroes' earned XP, levels, credits and items -
    // progression the player cannot get back by replaying, because the dice will not land the same way.
    //
    // So the assertions are deliberately about the state a LONG game accumulates, not about whether the
    // file parses: which wave it is, how high alertness has climbed, which search tokens are still
    // unopened and what is inside them, which markers are on the table and how far through them the party
    // is, and every hero's sheet. Each of those is a separate chance for one field to be dropped, and a
    // dropped field reads as a perfectly loadable game that has quietly forgotten something.
    //
    // Everything goes through the real GameSaveSerializer, store to JSON to a fresh store, which is the
    // same path .fdgsave takes.
    [TestFixture]
    public class StarQuestResumeTests
    {
        private GameDataStore _store = null!;
        private PlayerID _heroes;
        private PlayerID _ai;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _heroes = new PlayerID(Guid.NewGuid());
            _ai = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _heroes }));
            _store.Create(new TeamData(1, new List<PlayerID> { _ai }));
        }

        // ── The mission's own state ─────────────────────────────────────────────────────────────────

        [Test]
        public void TheWaveAndAlertnessSurvive()
        {
            // The two numbers that say where a Horde run has got to. Losing either sends the player back
            // to wave 1 or hands them a free wave.
            QuestProgress quest = Progress(wave: 7, alertness: 4);
            quest.WavesFinished = 6;
            quest.PartyXp = 41;
            quest.WavePending = true;
            Write(quest);

            QuestProgress loaded = Reload();

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Wave, Is.EqualTo(7), "wave");
                Assert.That(loaded.Alertness, Is.EqualTo(4), "alertness");
                Assert.That(loaded.WavesFinished, Is.EqualTo(6), "waves finished - the high-score multiplier");
                Assert.That(loaded.PartyXp, Is.EqualTo(41), "party XP");
                Assert.That(loaded.WavePending, Is.True, "a wave was due to arrive");
            });
        }

        [Test]
        public void TheMissionObjectivesAndTheirProgressSurvive()
        {
            // Which mission is being played and how much of it is done. A lost PrimaryDone would reopen a
            // completed objective and block the exfiltration the party had earned.
            QuestProgress quest = Progress();
            quest.Difficulty = 3;
            quest.Primary = 4;
            quest.Secondary = 2;
            quest.PrimaryDone = true;
            quest.SecondaryDone = false;
            quest.HeroCorner = 2;
            quest.Reinforcements = 5;
            Write(quest);

            QuestProgress loaded = Reload();

            Assert.Multiple(() =>
            {
                Assert.That(loaded.Difficulty, Is.EqualTo(3), "difficulty drives rewards AND wave size");
                Assert.That(loaded.Primary, Is.EqualTo(4));
                Assert.That(loaded.Secondary, Is.EqualTo(2));
                Assert.That(loaded.PrimaryDone, Is.True);
                Assert.That(loaded.SecondaryDone, Is.False);
                Assert.That(loaded.HeroCorner, Is.EqualTo(2), "which corner the party exfiltrates to");
                Assert.That(loaded.Reinforcements, Is.EqualTo(5));
            });
        }

        [Test]
        public void UnopenedSearchTokensKeepWhatIsInsideThem()
        {
            // The sharpest case. A search token's Contents is rolled when the token is PLACED, not when
            // it is opened, so the loot is already decided and sitting in the save. Lose the list and the
            // treasure the player has been walking toward becomes empty - and nothing about the game
            // looks broken.
            QuestProgress quest = Progress();
            quest.Tokens.Add(new QuestToken
            {
                Id = 1, X = 12f, Z = 18f, IsTreasure = false,
                Contents = new List<string> { "Med Kit", "15c" },
            });
            quest.Tokens.Add(new QuestToken { Id = 2, X = 30f, Z = 6f, IsTreasure = true, Contents = new List<string> { "Frag Grenade" } });
            quest.NextTokenId = 3;
            Write(quest);

            QuestProgress loaded = Reload();

            Assert.That(loaded.Tokens.Count, Is.EqualTo(2));
            Assert.That(loaded.NextTokenId, Is.EqualTo(3), "or the next token placed would reuse an id");

            QuestToken first = loaded.Tokens.Single(t => t.Id == 1);
            Assert.That(first.X, Is.EqualTo(12f));
            Assert.That(first.Z, Is.EqualTo(18f));
            Assert.That(first.IsTreasure, Is.False);
            Assert.That(first.Contents, Is.EqualTo(new[] { "Med Kit", "15c" }).AsCollection,
                "the loot is rolled when the token is placed, so it is already in the save");

            QuestToken second = loaded.Tokens.Single(t => t.Id == 2);
            Assert.That(second.IsTreasure, Is.True);
            Assert.That(second.Contents, Is.EqualTo(new[] { "Frag Grenade" }).AsCollection);
        }

        [Test]
        public void MarkersOnTheTableKeepTheirHalfFinishedProgress()
        {
            // A marker a hero has worked twice out of six times is the player's last twenty minutes.
            // Tokens/TokensNeeded and Activated are what remember it.
            QuestProgress quest = Progress();
            quest.Markers.Add(new QuestMarker
            {
                Id = 1, Kind = "objective", X = 20f, Z = 20f, ForSecondary = false,
                Stat = 2, Action = "hold", Activated = true, Tokens = 2, TokensNeeded = 6,
            });
            quest.Markers.Add(new QuestMarker { Id = 2, Kind = "smoke", X = 8f, Z = 8f });
            quest.NextMarkerId = 3;
            Write(quest);

            QuestProgress loaded = Reload();

            Assert.That(loaded.Markers.Count, Is.EqualTo(2));
            Assert.That(loaded.NextMarkerId, Is.EqualTo(3));

            QuestMarker objective = loaded.Markers.Single(m => m.Id == 1);
            Assert.Multiple(() =>
            {
                Assert.That(objective.Kind, Is.EqualTo("objective"));
                Assert.That(objective.X, Is.EqualTo(20f));
                Assert.That(objective.Z, Is.EqualTo(20f));
                Assert.That(objective.Stat, Is.EqualTo(2), "which stat the hero tests against it");
                Assert.That(objective.Action, Is.EqualTo("hold"));
                Assert.That(objective.Activated, Is.True);
                Assert.That(objective.Tokens, Is.EqualTo(2), "two of six done");
                Assert.That(objective.TokensNeeded, Is.EqualTo(6));
            });
            Assert.That(loaded.Markers.Single(m => m.Id == 2).Kind, Is.EqualTo("smoke"),
                "a thrown smoke marker is table state too");
        }

        [Test]
        public void TheWatchedWaveSurvives()
        {
            // Which units are being watched for, and for what - the boss-wave bookkeeping. A lost list
            // means the bonus XP for killing the boss can never be awarded.
            QuestProgress quest = Progress();
            quest.WatchedWave.Add("some-unit-guid");
            quest.WatchedWave.Add("another-unit-guid");
            quest.WatchingFor = 4;
            Write(quest);

            QuestProgress loaded = Reload();

            Assert.That(loaded.WatchedWave, Is.EqualTo(new[] { "some-unit-guid", "another-unit-guid" }).AsCollection);
            Assert.That(loaded.WatchingFor, Is.EqualTo(4));
        }

        // ── The heroes ──────────────────────────────────────────────────────────────────────────────

        [Test]
        public void EveryHeroSheetSurvivesIntact()
        {
            // The part the player cannot replay for. A hero's sheet rides on its unit as JSON, so this is
            // a round trip inside a round trip, and both have to hold.
            var sheet = new QuestHero
            {
                Name = "Vex", Faction = "Human Defense Force", Class = "Soldier", BaseHeroName = "Leader",
                BaseCost = 50, Level = 5, Xp = 17, Credits = 65, Quality = 3, Defense = 3, Tough = 9,
                Power = 6, Strength = 3, Dexterity = 4, Willpower = 5, Caster = 1, SkillSlots = 2,
                Missions = 4, WoundsTaken = 2, PowerLeft = 1,
                CampaignName = "Rimward", CampaignDifficulty = 2,
            };
            sheet.Skills.AddRange(new[] { "Overwatch", "Trick Shot" });
            sheet.Items.AddRange(new[] { "Med Kit", "Med Kit", "Frag Grenade" });
            sheet.Injuries.Add("Blinded Eye");
            sheet.Conditions.Add("Crippled");
            sheet.Spells.Add("Mind Bolt");

            DataReference unitRef = HeroUnit(sheet);
            Write(Progress(wave: 3));

            GameDataStore loaded = RoundTrip();
            QuestHero? back = QuestHero.FromJson(loaded.GetValue<UnitData>(unitRef).QuestHeroJson);

            Assert.That(back, Is.Not.Null, "the hero sheet must still parse after a reload");
            Assert.Multiple(() =>
            {
                Assert.That(back!.Name, Is.EqualTo("Vex"));
                Assert.That(back.Level, Is.EqualTo(5), "level");
                Assert.That(back.Xp, Is.EqualTo(17), "XP toward the next one");
                Assert.That(back.Credits, Is.EqualTo(65), "credits");
                Assert.That(back.Tough, Is.EqualTo(9), "the stats its level-ups improved");
                Assert.That(back.Caster, Is.EqualTo(1));
                Assert.That(back.SkillSlots, Is.EqualTo(2));
                Assert.That(back.Skills, Is.EquivalentTo(new[] { "Overwatch", "Trick Shot" }), "skills");
                Assert.That(back.Items, Is.EquivalentTo(new[] { "Med Kit", "Med Kit", "Frag Grenade" }),
                    "items, duplicates included");
                Assert.That(back.Injuries, Is.EquivalentTo(new[] { "Blinded Eye" }), "injuries");
                Assert.That(back.Conditions, Is.EquivalentTo(new[] { "Crippled" }), "status conditions");
                Assert.That(back.Spells, Is.EquivalentTo(new[] { "Mind Bolt" }), "spells");
                Assert.That(back.Missions, Is.EqualTo(4));
                Assert.That(back.WoundsTaken, Is.EqualTo(2), "mid-mission wounds");
                Assert.That(back.PowerLeft, Is.EqualTo(1), "and spent power");
                Assert.That(back.CampaignName, Is.EqualTo("Rimward"), "still in its campaign");
                Assert.That(back.CampaignDifficulty, Is.EqualTo(2));
            });
        }

        [Test]
        public void AReloadedHeroIsStillRecognisedAsAHero()
        {
            // IsQuestHero is derived from the sheet, and half the mode's code paths gate on it - which
            // units the AI ignores, who takes morale, who may use skills. A sheet that survived but no
            // longer registered would leave the heroes being played as ordinary units.
            DataReference unitRef = HeroUnit(new QuestHero { Name = "Mara", Class = "Psychic" });
            Write(Progress());

            UnitData back = RoundTrip().GetValue<UnitData>(unitRef);

            Assert.That(back.IsQuestHero, Is.True);
        }

        [Test]
        public void AnOrdinaryEnemyIsNotTurnedIntoAHero()
        {
            // The negative half: nothing in the reload should stamp a sheet onto a unit that had none.
            DataReference unitRef = PlainUnit(_ai, "Raiders");
            Write(Progress());

            UnitData back = RoundTrip().GetValue<UnitData>(unitRef);

            Assert.That(back.IsQuestHero, Is.False);
            Assert.That(back.QuestHeroJson, Is.Null);
        }

        // ── The shape of the game ───────────────────────────────────────────────────────────────────

        [Test]
        public void TheGameIsStillAStarQuestGameAfterReloading()
        {
            // Settings ride in the progress record. If the system or mode were lost, the reloaded game
            // would resume as Grimdark Future - which decides which rules apply (CoreRulesProfile), which
            // army books are legal, and whether any of the Quest stages run at all.
            Write(Progress(), EGameMode.StarQuestHorde);

            GameProgressData? progress = GameProgressUtilities.TryGetProgress(RoundTrip());

            Assert.That(progress, Is.Not.Null);
            Assert.That(progress!.Settings.GameSystem, Is.EqualTo(EGameSystem.GFStarQuest));
            Assert.That(progress.Settings.GameMode, Is.EqualTo(EGameMode.StarQuestHorde));
        }

        [Test]
        public void AnEnemyWaitingInTheCollectionIsStillWaiting()
        {
            // Horde draws each wave from the units still in reserve. A reload that put them all on the
            // table, or lost the reserve flag, would either dump ten waves at once or starve every wave
            // after the first.
            DataReference unitRef = PlainUnit(_ai, "Infiltrators");
            Rules.Dispatch.ReserveRules.PlaceInReserve(_store.GetValue<UnitData>(unitRef));
            Write(Progress());

            UnitData back = RoundTrip().GetValue<UnitData>(unitRef);

            Assert.That(Rules.Dispatch.ReserveRules.IsInReserve(back), Is.True);
        }

        [Test]
        public void AnEmptyQuestStateReloadsAsEmpty_NotAsNull()
        {
            // A fresh mission has no tokens and no markers. The lists must come back as empty lists and
            // not as null, or the first thing that enumerates them throws on a resumed game.
            Write(Progress());

            QuestProgress loaded = Reload();

            Assert.That(loaded.Tokens, Is.Not.Null.And.Empty);
            Assert.That(loaded.Markers, Is.Not.Null.And.Empty);
            Assert.That(loaded.WatchedWave, Is.Not.Null.And.Empty);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private static QuestProgress Progress(int wave = 1, int alertness = 0) =>
            new QuestProgress { Wave = wave, Alertness = alertness };

        private void Write(QuestProgress quest, EGameMode mode = EGameMode.StarQuestMission)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = EGameSystem.GFStarQuest;
            settings.GameMode = mode;

            GameProgressUtilities.WriteProgress(_store, new GameProgressData(
                stage: EResumeStage.MainPhase, roundCount: 3,
                teamActivateOrder: new List<int> { 0, 1 },
                currentRoundTeamFinishOrder: new List<int>(),
                currentTeamIndex: 0,
                currentPlayerIndexPerTeam: new Dictionary<int, int> { { 0, 0 }, { 1, 0 } },
                unactivatedUnits: new List<DataBinding<UnitData>>(),
                settings: settings));
            GameProgressUtilities.SetQuestProgress(_store, quest);
        }

        private GameDataStore RoundTrip() => GameSaveSerializer.Load(GameSaveSerializer.Save(_store));

        private QuestProgress Reload() => GameProgressUtilities.GetQuestProgress(RoundTrip());

        private DataReference HeroUnit(QuestHero sheet)
        {
            DataReference unitRef = PlainUnit(_heroes, sheet.Name);
            _store.GetValue<UnitData>(unitRef).QuestHeroJson = sheet.ToJson();
            return unitRef;
        }

        private DataReference PlainUnit(PlayerID owner, string name)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), _store);
            model.SetMaxWounds(3);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(owner, name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            DataReference unitRef = _store.Create(unit);
            _store.Create(new ArmyData(owner,
                new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(unitRef) }));
            return unitRef;
        }
    }
}
