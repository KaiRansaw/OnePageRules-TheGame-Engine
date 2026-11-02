using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #435 - the difficulty bands that size the rival's force for a campaign mission, and the picker
    /// that fills it.
    ///
    /// <para>
    /// This is a <b>house rule</b>: Campaign Rules v3.6.0 has one mission cap and expects a human on the
    /// other side of the table to decide what to bring. With a bot rival somebody has to decide, and
    /// before this the bot always filled the cap - every campaign mission was played at the hardest legal
    /// setting whether the player wanted it or not, and nothing on screen said so. The bands are
    /// expressed in the books' own underdog points so they need no per-system tuning, which is the
    /// property these tests exist to protect.
    /// </para>
    /// </summary>
    [TestFixture]
    public class MissionDifficultyTests
    {
        private static readonly CampaignNumbers[] AllFour =
        {
            CampaignNumbers.GrimdarkFuture, CampaignNumbers.Firefight,
            CampaignNumbers.AgeOfFantasy, CampaignNumbers.Skirmish,
        };

        // -- The bands -----------------------------------------------------------------------------

        /// <summary>
        /// The whole reason the bands are measured in underdog points: the two extremes land exactly on
        /// each book's own mission cap, in every system, with no per-system table. If someone later
        /// writes the bands as fractions of the cap, or as a percentage of the player's force, this is
        /// the test that notices.
        /// </summary>
        [Test]
        public void TheExtremeBandsAreExactlyTheBooksOwnCap()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                int yours = 1000;
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Brutal, yours, n),
                    Is.EqualTo(yours + n.ArmyCapOverLowest),
                    "Brutal is the top of the legal window.");
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Easy, yours, n),
                    Is.EqualTo(yours - n.ArmyCapOverLowest),
                    "Easy is the bottom of it.");
            }
        }

        [Test]
        public void NormalIsAnEvenMatch()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Normal, 740, n),
                    Is.EqualTo(740),
                    "the core rules' own 'two armies of equal point cost'.");
            }
        }

        [Test]
        public void TheBandsRiseAcrossTheWholeScale()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            int last = int.MinValue;
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                int target = MissionDifficulty.EnemyPointsTarget(d, 1000, n);
                Assert.That(target, Is.GreaterThan(last), $"{MissionDifficulty.Name(d)} must be harder "
                    + "than the setting below it.");
                last = target;
            }
        }

        /// <summary>
        /// No band may ever propose a force the books would refuse - which is the failure mode a
        /// percentage-based scale has, and the reason Quest's published 1-4 difficulty is NOT reused
        /// here (its wave budget is 25% of the party's points per level, which at 2,000 points would
        /// be 500 over a flat 325 cap).
        /// </summary>
        [Test]
        public void NoBandEverExceedsTheCap_AtAnyArmySize()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                foreach (int yours in new[] { 1, 50, 300, 750, 2000, 10_000 })
                {
                    int cap = MissionDifficulty.HardCap(yours, n);
                    foreach (EMissionDifficulty d in MissionDifficulty.All)
                    {
                        Assert.That(MissionDifficulty.EnemyPointsTarget(d, yours, n),
                            Is.LessThanOrEqualTo(cap),
                            $"{MissionDifficulty.Name(d)} at {yours} pts must stay legal.");
                    }
                }
            }
        }

        /// <summary>
        /// A target of zero or less would read as "bring nothing" rather than "bring as little as you
        /// can", which is not the same mission.
        /// </summary>
        [Test]
        public void AnEasyTargetNeverFallsToZero()
        {
            CampaignNumbers n = CampaignNumbers.Firefight;   // 125pt cap, so 100 pts goes negative
            Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Easy, 100, n),
                Is.EqualTo(1));
        }

        /// <summary>
        /// The band and the Underdog Bonus are the same number read twice: at Brutal the rival gets a
        /// full cap more AND the player is handed five points to spend on it. If someone changes a band
        /// without going through <see cref="CampaignNumbers.UnderdogPer"/>, that pairing breaks silently
        /// and the hardest setting stops paying for itself.
        /// </summary>
        [Test]
        public void EachBandPaysTheUnderdogPointsItsNameImplies()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                foreach (EMissionDifficulty d in MissionDifficulty.All)
                {
                    int yours = 1000;
                    int theirs = MissionDifficulty.EnemyPointsTarget(d, yours, n);
                    var forces = new List<CampaignEntrant>
                    {
                        new("You", yours, false), new("Them", theirs, false),
                    };
                    int expected = Math.Max(0, MissionDifficulty.UnderdogPointsOfGap(d));
                    Assert.That(CampaignPreGame.UnderdogPoints(yours, forces, n), Is.EqualTo(expected),
                        $"{MissionDifficulty.Name(d)} in a {n.ArmyCapOverLowest}pt-cap book.");
                }
            }
        }

        /// <summary>
        /// A campaign saved before difficulty existed must not silently be the hardest setting - which
        /// is what "keep today's behaviour" would have meant, since the old picker always filled the cap.
        /// </summary>
        [Test]
        public void UnsetMeansAnEvenMatch_NotBrutal()
        {
            Assert.That(MissionDifficulty.Resolve(EMissionDifficulty.Unset),
                Is.EqualTo(EMissionDifficulty.Normal));
            Assert.That(MissionDifficulty.Name(EMissionDifficulty.Unset), Is.EqualTo("Normal"));
            Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Unset, 640,
                    CampaignNumbers.GrimdarkFuture),
                Is.EqualTo(640));
        }

        // -- The rewards ---------------------------------------------------------------------------

        /// <summary>
        /// The claim that pins Kai's two answers together ("scale by the level" + "Normal is the normal
        /// payout"): on Normal the campaign pays EXACTLY what its own book prints. If it does not, the
        /// scale has stopped being a modifier on the books and become a replacement for them.
        /// </summary>
        [Test]
        public void OnNormal_TheBooksOwnPayoutIsUnchanged()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Normal, n),
                    Is.EqualTo(n.WinPoints), "Normal must pay the book's win treasury exactly.");
                Assert.That(MissionDifficulty.LosePoints(EMissionDifficulty.Normal, n),
                    Is.EqualTo(n.LosePoints));
            }
            Assert.That(MissionDifficulty.WinVp(EMissionDifficulty.Normal),
                Is.EqualTo(CampaignNumbers.WinVp), "and the book's 2 VP.");
        }

        /// <summary>Grimdark Future, written out, because this is the table Kai signed off.</summary>
        [Test]
        public void TheWinPayoutIsHalfTheBooksFigurePerLevel()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;   // win 150, 2 VP
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Easy, n), Is.EqualTo(75));
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Normal, n), Is.EqualTo(150));
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Hard, n), Is.EqualTo(225));
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Brutal, n), Is.EqualTo(300));

            Assert.That(MissionDifficulty.WinVp(EMissionDifficulty.Easy), Is.EqualTo(1));
            Assert.That(MissionDifficulty.WinVp(EMissionDifficulty.Normal), Is.EqualTo(2));
            Assert.That(MissionDifficulty.WinVp(EMissionDifficulty.Hard), Is.EqualTo(3));
            Assert.That(MissionDifficulty.WinVp(EMissionDifficulty.Brutal), Is.EqualTo(4));
        }

        /// <summary>
        /// Kai's call: <i>"Easy is scaled down, so rewards are scaled down as well"</i>. So Easy is the
        /// one setting that pays LESS than the books do - which is the opposite of how every other
        /// house rule here works, and therefore the one worth pinning.
        /// </summary>
        [Test]
        public void EasyPaysLessThanTheBook_AndTheHardSettingsPayMore()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Easy, n),
                    Is.LessThan(n.WinPoints));
                Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Hard, n),
                    Is.GreaterThan(n.WinPoints));
                Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Brutal, n),
                    Is.GreaterThan(MissionDifficulty.WinPoints(EMissionDifficulty.Hard, n)));
            }
        }

        /// <summary>
        /// Kai's call: <i>"loss payout scale does not change"</i>. The books pay the loser roughly double
        /// the winner so whoever is behind catches up; multiplying that by four on the hardest setting
        /// would make losing the profitable move and pull the rubber band backwards. Sabotage check: make
        /// LosePoints scale and this is what fails.
        /// </summary>
        [Test]
        public void TheLossPayoutIsFlatAtEverySetting()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                foreach (EMissionDifficulty d in MissionDifficulty.All)
                {
                    Assert.That(MissionDifficulty.LosePoints(d, n), Is.EqualTo(n.LosePoints),
                        $"{MissionDifficulty.Name(d)} must pay the book's flat loss figure.");
                }
            }
        }

        /// <summary>
        /// Every book's win payout must divide evenly, or Normal would not land on the book's figure.
        /// All four print an even number today; this is the guard for the day one does not.
        /// </summary>
        [Test]
        public void EveryBooksWinPayoutScalesWithoutRounding()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                int perLevel = MissionDifficulty.WinPoints(EMissionDifficulty.Easy, n);
                Assert.That(perLevel * 2, Is.EqualTo(n.WinPoints),
                    $"a {n.WinPoints}pt win does not halve cleanly, so Normal would not pay the book's "
                    + "figure. Rework the scale rather than accepting the rounding.");
            }
        }

        /// <summary>
        /// The order of the arithmetic, proved rather than asserted in a comment.
        ///
        /// <para>
        /// <c>WinPoints</c> multiplies by the level and THEN halves. All four books print an even win
        /// payout (150, 80, 150, 60), so today the other order gives identical answers and a sabotage
        /// that swaps them passes every other test here - which is how a documented precaution turns out
        /// to be protecting nothing. This test uses a book with an ODD payout, where the two orders
        /// genuinely differ, so the claim in that doc comment is now a tested one.
        /// </para>
        /// <para>
        /// With a 75pt win: multiply-first gives 37 / 75 / 112 / 150, and <b>Normal still lands exactly
        /// on 75</b>. Halve-first gives 37 / 74 / 111 / 148 - Normal pays a point less than its own book,
        /// which is the single thing this scale must never do.
        /// </para>
        /// </summary>
        [Test]
        public void WithAnOddWinPayout_NormalStillPaysTheBooksFigureExactly()
        {
            CampaignNumbers odd = CampaignNumbers.GrimdarkFuture with { WinPoints = 75 };

            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Normal, odd), Is.EqualTo(75),
                "Normal must pay the book's figure even when it does not halve cleanly. If this fails, "
                + "WinPoints is halving before it multiplies.");
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Easy, odd), Is.EqualTo(37));
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Hard, odd), Is.EqualTo(112));
            Assert.That(MissionDifficulty.WinPoints(EMissionDifficulty.Brutal, odd), Is.EqualTo(150));
        }

        [Test]
        public void TheLevelIsTheStoredNumber()
        {
            Assert.That(MissionDifficulty.Level(EMissionDifficulty.Easy), Is.EqualTo(1));
            Assert.That(MissionDifficulty.Level(EMissionDifficulty.Brutal), Is.EqualTo(4));
            Assert.That(MissionDifficulty.Level(EMissionDifficulty.Unset),
                Is.EqualTo(MissionDifficulty.Level(MissionDifficulty.Default)));
        }

        // -- The rename, and the saves written before it --------------------------------------------

        /// <summary>
        /// The four settings were renamed on 2026-11-02 (Beginner/Intermediate/Expert/Legendary ->
        /// Easy/Normal/Hard/Brutal) because sharing Quest's published vocabulary made two different
        /// mechanics read as one. The enum is stored by NAME, so without a reader that accepts the old
        /// ones a campaign army saved by the previous build would not merely lose its difficulty - the
        /// whole file would fail to deserialize and the campaign would look gone.
        /// </summary>
        [Test]
        public void ACampaignSavedUnderTheOldNamesStillLoads()
        {
            var expected = new (string Old, EMissionDifficulty Now)[]
            {
                ("Beginner", EMissionDifficulty.Easy),
                ("Intermediate", EMissionDifficulty.Normal),
                ("Expert", EMissionDifficulty.Hard),
                ("Legendary", EMissionDifficulty.Brutal),
            };
            foreach ((string old, EMissionDifficulty now) in expected)
            {
                StrikeTeamInfo? read = System.Text.Json.JsonSerializer.Deserialize<StrikeTeamInfo>(
                    $"{{\"name\":\"Old\",\"difficulty\":\"{old}\"}}",
                    Rules.Serialization.RuleJson.Options);
                Assert.That(read, Is.Not.Null, $"a file saying \"{old}\" must still load at all.");
                Assert.That(read!.Difficulty, Is.EqualTo(now));
            }
        }

        [Test]
        public void TheCurrentNamesAndNumbersRoundTrip()
        {
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                string json = System.Text.Json.JsonSerializer.Serialize(
                    new StrikeTeamInfo { Name = "A", Difficulty = d },
                    Rules.Serialization.RuleJson.Options);
                Assert.That(json, Does.Contain($"\"{MissionDifficulty.Name(d)}\""),
                    "written under its current name.");
                StrikeTeamInfo back = System.Text.Json.JsonSerializer.Deserialize<StrikeTeamInfo>(
                    json, Rules.Serialization.RuleJson.Options)!;
                Assert.That(back.Difficulty, Is.EqualTo(d));
            }

            // And the numeric form, which is what a hand-edited file or an older serializer may hold.
            StrikeTeamInfo numeric = System.Text.Json.JsonSerializer.Deserialize<StrikeTeamInfo>(
                "{\"name\":\"A\",\"difficulty\":4}", Rules.Serialization.RuleJson.Options)!;
            Assert.That(numeric.Difficulty, Is.EqualTo(EMissionDifficulty.Brutal));
        }

        /// <summary>
        /// Nonsense must not take the campaign down with it - it reads as Unset, which resolves to
        /// Normal, the setting that pays exactly what the book prints.
        /// </summary>
        [Test]
        public void AnUnknownNameReadsAsUnset_NotAnException()
        {
            StrikeTeamInfo read = System.Text.Json.JsonSerializer.Deserialize<StrikeTeamInfo>(
                "{\"name\":\"A\",\"difficulty\":\"Nightmare\"}",
                Rules.Serialization.RuleJson.Options)!;
            Assert.That(read.Difficulty, Is.EqualTo(EMissionDifficulty.Unset));
            Assert.That(MissionDifficulty.Resolve(read.Difficulty),
                Is.EqualTo(EMissionDifficulty.Normal));
        }

        /// <summary>
        /// The names must not collide with the Quest Campaign Builder's published 1-4 scale, which is the
        /// whole reason for the rename. Quest keeps Beginner/Intermediate/Expert/Legendary
        /// (StarQuestMissionRules.DifficultyNames); the campaign must share none of them.
        /// </summary>
        [Test]
        public void NoSettingSharesAQuestDifficultyName()
        {
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                Assert.That(StateMachine.StarQuestMissionRules.DifficultyNames,
                    Does.Not.Contain(MissionDifficulty.Name(d)),
                    $"\"{MissionDifficulty.Name(d)}\" is one of Quest's published difficulty names. The "
                    + "two scales are different mechanics and sharing a word made them read as one.");
            }
        }

        // -- The payout reaching a finished mission --------------------------------------------------

        /// <summary>
        /// A campaign army of the given difficulty, finished a mission. Deliberately exercises the real
        /// <see cref="CampaignRules.AfterGame"/> rather than the arithmetic above: the scale could be
        /// perfect and still never be consulted, which is exactly how #434's Underdog Bonus shipped doing
        /// nothing. Delete the two MissionDifficulty calls in AfterGame and only these tests fail.
        /// </summary>
        private static StrikeTeamInfo Played(EMissionDifficulty difficulty, bool won, CampaignNumbers n)
        {
            var army = new BuiltArmyFile { Name = "Raiders", Faction = "Test" };
            army.Units.Add(new UnitFileEntry { Name = "Squad", ModelCount = 5, Quality = 5, Defense = 5,
                                               PointCost = 100 });
            CampaignRules.Start(army, "Raiders", "raiders.fdgarmy", n, EGameSystem.GrimdarkFuture,
                ECampaignType.Endless, noPermadeath: false, isBot: false);
            StrikeTeamInfo info = army.StrikeTeam!;
            info.Difficulty = difficulty;
            info.BankedPoints = 0;
            info.Vp = 0;

            string id = CampaignRules.Sheet(army.Units[0])!.Id;
            CampaignRules.AfterGame(army, new[] { new CampaignUnitResult(id, false, 0, 0) }, won, n,
                () => 4);
            return info;
        }

        [Test]
        public void AFinishedMission_PaysTheDifficultysRate()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                StrikeTeamInfo won = Played(d, won: true, n);
                Assert.That(won.BankedPoints, Is.EqualTo(MissionDifficulty.WinPoints(d, n)),
                    $"a win on {MissionDifficulty.Name(d)} must pay its own rate.");
                Assert.That(won.Vp, Is.EqualTo(MissionDifficulty.WinVp(d)));
            }
        }

        /// <summary>
        /// Normal pays exactly what the book printed before any of this existed - so an existing campaign
        /// played at the default setting earns the same as it always did.
        /// </summary>
        [Test]
        public void AFinishedMissionOnNormal_PaysTheBooksOwnFigures()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                Assert.That(Played(EMissionDifficulty.Normal, true, n).BankedPoints,
                    Is.EqualTo(n.WinPoints));
                Assert.That(Played(EMissionDifficulty.Normal, true, n).Vp,
                    Is.EqualTo(CampaignNumbers.WinVp));
            }
        }

        [Test]
        public void AFinishedMission_PaysTheFlatLossWhateverTheDifficulty()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                StrikeTeamInfo lost = Played(d, won: false, n);
                Assert.That(lost.BankedPoints, Is.EqualTo(n.LosePoints),
                    $"a loss on {MissionDifficulty.Name(d)} must still pay the book's flat figure.");
                Assert.That(lost.Vp, Is.Zero, "and a loss is worth no VP at any setting.");
            }
        }

        /// <summary>
        /// A campaign carried over from before difficulty existed (Unset) must not quietly change what it
        /// earns - it resolves to Normal, which is the book's own rate.
        /// </summary>
        [Test]
        public void AnOlderCampaign_EarnsExactlyWhatItAlwaysDid()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            StrikeTeamInfo info = Played(EMissionDifficulty.Unset, won: true, n);
            Assert.That(info.BankedPoints, Is.EqualTo(n.WinPoints));
            Assert.That(info.Vp, Is.EqualTo(CampaignNumbers.WinVp));
        }

        /// <summary>The history line should say which setting it was played on, or a campaign's record
        /// cannot be read back - 225 pts means nothing without "on Hard" beside it.</summary>
        [Test]
        public void TheHistoryLineNamesTheDifficulty()
        {
            StrikeTeamInfo info = Played(EMissionDifficulty.Hard, won: true,
                CampaignNumbers.GrimdarkFuture);
            Assert.That(info.History, Is.Not.Empty);
            Assert.That(info.History[^1], Does.Contain("Hard"));
        }

        /// <summary>Game text is ASCII-only - the ImGui atlas renders anything above U+00FF as "?".</summary>
        [Test]
        public void EveryStringIsAscii()
        {
            var texts = new List<string>();
            foreach (EMissionDifficulty d in MissionDifficulty.All)
            {
                texts.Add(MissionDifficulty.Name(d));
                foreach (CampaignNumbers n in AllFour)
                {
                    texts.Add(MissionDifficulty.Describe(d, 500, n));
                    texts.Add(MissionDifficulty.DescribeReward(d, n));
                }
            }
            foreach (string text in texts)
            {
                Assert.That(text.All(c => c <= 0x7F), Is.True, $"non-ASCII in \"{text}\".");
                Assert.That(text, Is.Not.Empty);
            }
        }

        // -- The picker ----------------------------------------------------------------------------

        private const string TrooperId = "trooper", ScoutId = "scout";

        /// <summary>A two-entry book: 100pt troopers and 40pt scouts.</summary>
        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit
                {
                    Id = TrooperId, Name = "Troopers", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 100,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new RosterUnit
                {
                    Id = ScoutId, Name = "Scouts", Quality = 4, Defense = 5,
                    BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 40,
                    Weapons = { new WeaponFileEntry { Name = "Carbine", RangeInches = 18, Attacks = 1 } },
                },
            },
        };

        /// <summary>A bot campaign army holding the given roster entries, in the order listed.</summary>
        private static BuiltArmyFile Army(params string[] rosterIds)
        {
            BookFile book = Book();
            var list = new BuilderList { Name = "Enemy", BookName = book.Name };
            foreach (string id in rosterIds)
            {
                list.Units.Add(new BuilderUnit
                {
                    RosterUnitId = id,
                    ModelCount = book.Units.First(u => u.Id == id).BaseModelCount,
                });
            }
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            CampaignRules.Start(army, "Enemy", "enemy.fdgarmy", CampaignNumbers.Firefight,
                EGameSystem.GFFirefight, ECampaignType.Endless, noPermadeath: false, isBot: true);
            return army;
        }

        private static int PointsOf(BuiltArmyFile army, IReadOnlyCollection<string> bringing) =>
            army.Units.Where(u => CampaignRules.Sheet(u) is { } sh && bringing.Contains(sh.Id))
                .Sum(u => u.PointCost);

        /// <summary>
        /// The bug this slice exists to fix: the picker filled whatever number it was given, so handing
        /// it the cap made every mission Brutal. An easy setting must actually produce a smaller
        /// force.
        /// </summary>
        [Test]
        public void ALowerDifficultyBringsLessThanAHigherOne()
        {
            BuiltArmyFile army = Army(TrooperId, TrooperId, TrooperId, ScoutId, ScoutId);
            CampaignNumbers n = CampaignNumbers.Firefight;

            int easy = PointsOf(army, CampaignBot.PickWhatToBring(army,
                MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Easy, 200, n)));
            int hard = PointsOf(army, CampaignBot.PickWhatToBring(army,
                MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Brutal, 200, n)));

            Assert.That(easy, Is.LessThan(hard));
            Assert.That(hard, Is.LessThanOrEqualTo(MissionDifficulty.HardCap(200, n)));
        }

        /// <summary>
        /// The old code took the first unit unconditionally, sorted most-experienced-first - so an easy
        /// setting against a small force could field the rival's biggest veteran and nothing stopped it.
        /// When nothing fits, the thing that comes must be the CHEAPEST it owns.
        /// </summary>
        [Test]
        public void WhenNothingFits_TheCheapestUnitComes_NotTheVeteran()
        {
            BuiltArmyFile army = Army(TrooperId, ScoutId);
            UnitFileEntry troopers = army.Units.First(u => u.PointCost == 100);
            StrikeUnit sheet = CampaignRules.Sheet(troopers)!;
            sheet.Xp = 25;                       // the veteran is also the expensive one
            CampaignRules.Save(troopers, sheet);

            List<string> bringing = CampaignBot.PickWhatToBring(army, target: 10);

            Assert.That(bringing.Count, Is.EqualTo(1), "a mission with no enemy is worse than a small one.");
            Assert.That(PointsOf(army, bringing), Is.EqualTo(40), "and it is the 40pt scouts.");
        }

        [Test]
        public void VeteransStillComeFirstWhenTheyFit()
        {
            BuiltArmyFile army = Army(TrooperId, ScoutId, ScoutId);
            UnitFileEntry veteran = army.Units[^1];       // a scout, last in the file
            StrikeUnit sheet = CampaignRules.Sheet(veteran)!;
            sheet.Xp = 25;
            CampaignRules.Save(veteran, sheet);

            List<string> bringing = CampaignBot.PickWhatToBring(army, target: 40);

            Assert.That(bringing, Is.EqualTo(new[] { CampaignRules.Sheet(veteran)!.Id }));
        }

        /// <summary>
        /// Randomize must actually pick differently, and must still obey the target. Driven by the
        /// game's own roll delegate rather than a Random of its own, so a seeded run reproduces.
        /// </summary>
        [Test]
        public void Randomize_PicksAcrossTheRoster_AndStillRespectsTheTarget()
        {
            BuiltArmyFile army = Army(TrooperId, TrooperId, ScoutId, ScoutId, ScoutId);
            UnitFileEntry veteran = army.Units[0];
            StrikeUnit sheet = CampaignRules.Sheet(veteran)!;
            sheet.Xp = 25;
            CampaignRules.Save(veteran, sheet);

            // A roll that always returns 1 swaps every element with index 0, which reverses nothing but
            // does reorder - enough to show the veteran is no longer guaranteed to lead.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var rng = new Random(12345);
            for (int attempt = 0; attempt < 40; attempt++)
            {
                List<string> bringing = CampaignBot.PickWhatToBring(army, target: 140,
                    sides => rng.Next(1, sides + 1));
                Assert.That(PointsOf(army, bringing), Is.LessThanOrEqualTo(140),
                    "random order is not licence to break the target.");
                Assert.That(bringing, Is.Not.Empty);
                seen.Add(string.Join(",", bringing.OrderBy(x => x, StringComparer.Ordinal)));
            }

            Assert.That(seen.Count, Is.GreaterThan(1),
                "40 randomized picks that all came out identical means the shuffle is not shuffling.");
        }

        [Test]
        public void Randomize_NeverBringsARecoveringUnit()
        {
            BuiltArmyFile army = Army(TrooperId, ScoutId, ScoutId);
            UnitFileEntry hurt = army.Units[1];
            StrikeUnit sheet = CampaignRules.Sheet(hurt)!;
            sheet.MissionsOut = 2;
            CampaignRules.Save(hurt, sheet);

            var rng = new Random(7);
            for (int attempt = 0; attempt < 25; attempt++)
            {
                List<string> bringing = CampaignBot.PickWhatToBring(army, target: 10_000,
                    sides => rng.Next(1, sides + 1));
                Assert.That(bringing, Does.Not.Contain(CampaignRules.Sheet(hurt)!.Id),
                    "#427's recovering units apply however the force is picked.");
            }
        }

        /// <summary>
        /// The deterministic order has to stay deterministic: an unattended run, a replayed save and a
        /// headless sweep all depend on the same campaign producing the same mission.
        /// </summary>
        [Test]
        public void WithoutRandomize_ThePickIsRepeatable()
        {
            BuiltArmyFile army = Army(TrooperId, ScoutId, ScoutId);
            List<string> first = CampaignBot.PickWhatToBring(army, target: 180);
            List<string> again = CampaignBot.PickWhatToBring(army, target: 180);
            Assert.That(again, Is.EqualTo(first));
        }
    }
}
