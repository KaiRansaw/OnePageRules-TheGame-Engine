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
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Legendary, yours, n),
                    Is.EqualTo(yours + n.ArmyCapOverLowest),
                    "Legendary is the top of the legal window.");
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Beginner, yours, n),
                    Is.EqualTo(yours - n.ArmyCapOverLowest),
                    "Beginner is the bottom of it.");
            }
        }

        [Test]
        public void IntermediateIsAnEvenMatch()
        {
            foreach (CampaignNumbers n in AllFour)
            {
                Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Intermediate, 740, n),
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
        public void ABeginnerTargetNeverFallsToZero()
        {
            CampaignNumbers n = CampaignNumbers.Firefight;   // 125pt cap, so 100 pts goes negative
            Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Beginner, 100, n),
                Is.EqualTo(1));
        }

        /// <summary>
        /// The band and the Underdog Bonus are the same number read twice: at Legendary the rival gets a
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
        public void UnsetMeansAnEvenMatch_NotLegendary()
        {
            Assert.That(MissionDifficulty.Resolve(EMissionDifficulty.Unset),
                Is.EqualTo(EMissionDifficulty.Intermediate));
            Assert.That(MissionDifficulty.Name(EMissionDifficulty.Unset), Is.EqualTo("Intermediate"));
            Assert.That(MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Unset, 640,
                    CampaignNumbers.GrimdarkFuture),
                Is.EqualTo(640));
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
        /// it the cap made every mission Legendary. An easy setting must actually produce a smaller
        /// force.
        /// </summary>
        [Test]
        public void ALowerDifficultyBringsLessThanAHigherOne()
        {
            BuiltArmyFile army = Army(TrooperId, TrooperId, TrooperId, ScoutId, ScoutId);
            CampaignNumbers n = CampaignNumbers.Firefight;

            int easy = PointsOf(army, CampaignBot.PickWhatToBring(army,
                MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Beginner, 200, n)));
            int hard = PointsOf(army, CampaignBot.PickWhatToBring(army,
                MissionDifficulty.EnemyPointsTarget(EMissionDifficulty.Legendary, 200, n)));

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
