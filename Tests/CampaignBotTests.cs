using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #428 - the bot's half of the campaign loop, which is what gives a campaign an arc. Without it the
    /// enemy is a fresh army list every mission: it never levels, never recruits, never spends and never
    /// remembers losing, so the player's own progress stops meaning anything after about the third
    /// mission.
    ///
    /// <para>
    /// How hard it plays was the engine's call, Kai having answered "Unsure": <b>it plays the book
    /// straight and the book's own rubber band does the balancing</b> - same rewards table, same prices,
    /// same casualty rolls, no multiplier either way. These tests pin the two places it deliberately
    /// does not behave like a hoarding human (it spends, and it will not build past what it could bring)
    /// and the choices that make its levelling visible across the table.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignBotTests
    {
        private const string TrooperId = "trooper";
        private const string ScoutId = "scout";
        private const string CaptainId = "captain";

        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        /// <summary>A three-entry book: a 40pt scout, a 100pt squad and a 50pt Hero.</summary>
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
                new RosterUnit
                {
                    Id = CaptainId, Name = "Captain", Quality = 3, Defense = 4,
                    BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 50,
                    Weapons = { new WeaponFileEntry { Name = "Blade", Attacks = 3 } },
                    Rules = { new SpecialRuleEntry_Core("Hero") },
                },
            },
        };

        /// <summary>A bot campaign army of one 100pt squad, with a treasury to spend.</summary>
        private static (BuiltArmyFile Army, BookFile Book) Bot(int treasury, bool isBot = true)
        {
            BookFile book = Book();
            var list = new BuilderList { Name = "Enemy", BookName = book.Name };
            list.Units.Add(new BuilderUnit { RosterUnitId = TrooperId, ModelCount = 5 });
            BuiltArmyFile army = ListCompiler.Compile(book, list);

            CampaignRules.Start(army, "Enemy", "enemy.fdgarmy", CampaignNumbers.Firefight,
                EGameSystem.GFFirefight, ECampaignType.Endless, noPermadeath: false, isBot: isBot);
            army.StrikeTeam!.BankedPoints = treasury;
            return (army, book);
        }

        // -- It only ever acts for a bot --------------------------------------------------------------

        [Test]
        public void BetweenMissions_DoesNothingForAPlayersArmy()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 1000, isBot: false);
            int unitsBefore = army.Units.Count;

            List<string> log = CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight,
                rivalValue: 2000, Dice(3));

            Assert.That(log, Is.Empty, "a player's army is spent by the player, on the campaign screen.");
            Assert.That(army.Units.Count, Is.EqualTo(unitsBefore));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(1000), "and its treasury is untouched.");
        }

        // -- Spending ---------------------------------------------------------------------------------

        /// <summary>
        /// The whole point: a bot with points in the bank turns them into army. A bot that hoards is the
        /// no-arc failure in a different costume - the player would see a treasury number climb on a
        /// screen they never open, and face the same army every mission.
        /// </summary>
        [Test]
        public void Spend_TurnsTheTreasuryIntoUnits()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 300);
            int before = army.TotalPoints;

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 1000, Dice(3));

            Assert.That(army.Units.Count, Is.GreaterThan(1), "it must have recruited something.");
            Assert.That(army.TotalPoints, Is.GreaterThan(before));
            Assert.That(army.StrikeTeam!.BankedPoints, Is.LessThan(300), "and paid for it.");
        }

        /// <summary>
        /// A Hero first when it has none. The campaign rules are written around heroes - the injury and
        /// talent tables only ever come up for one - so an enemy army without a hero never has half the
        /// campaign happen to it.
        /// </summary>
        [Test]
        public void Spend_BuysAHeroFirstWhenItHasNone()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 60);
            Assert.That(CampaignEconomy.HeroCount(army), Is.Zero, "precondition: no hero.");

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 1000, Dice(3));

            Assert.That(CampaignEconomy.HeroCount(army), Is.EqualTo(1),
                "60 pts buys the 50 pt Captain before the 40 pt Scouts.");
        }

        [Test]
        public void Spend_BuysBodiesOnceItHasAHero()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 500);

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 2000, Dice(3));

            Assert.That(CampaignEconomy.HeroCount(army), Is.EqualTo(1), "one hero, not an army of them.");
            Assert.That(army.Units.Count(u => !CampaignRules.IsHero(u)), Is.GreaterThan(1),
                "the rest of the treasury goes on units.");
        }

        [Test]
        public void Spend_NeverGoesIntoDebt()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 45);

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 5000, Dice(3));

            Assert.That(army.StrikeTeam!.BankedPoints, Is.GreaterThanOrEqualTo(0));
            Assert.That(army.StrikeTeam.BankedPoints, Is.EqualTo(5),
                "45 pts buys the 40 pt Scouts and nothing else fits.");
        }

        [Test]
        public void Spend_WithNothingAffordable_KeepsThePointsAndSaysSo()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 10);

            List<string> log = CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight,
                rivalValue: 5000, Dice(3));

            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(10));
            Assert.That(log.Any(l => l.Contains("banked")), Is.True,
                "it must report a no-op rather than looking like it did something.");
        }

        /// <summary>
        /// The roster ceiling: it will not build past the most it could ever legally bring (the biggest
        /// rival plus the mission cap). Past that it is buying units to leave at home - growth on the
        /// sheet that changes nothing on the table, and an army file that gets silly over fifty missions.
        /// </summary>
        [Test]
        public void Spend_StopsAtWhatItCouldEverBring()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 5000);
            CampaignNumbers n = CampaignNumbers.Firefight;
            int ceiling = CampaignBot.RosterCeiling(rivalValue: 200, n);

            CampaignBot.BetweenMissions(army, book, n, rivalValue: 200, Dice(3));

            Assert.That(army.TotalPoints, Is.LessThanOrEqualTo(ceiling),
                $"a 200 pt rival and a {n.ArmyCapOverLowest} pt cap means it stops at {ceiling}.");
            Assert.That(army.StrikeTeam!.BankedPoints, Is.GreaterThan(0),
                "the rest stays banked - it is not spent on something it cannot bring.");
        }

        [Test]
        public void RosterCeiling_IsTheRivalPlusTheBooksMissionCap()
        {
            Assert.That(CampaignBot.RosterCeiling(1000, CampaignNumbers.GrimdarkFuture), Is.EqualTo(1325));
            Assert.That(CampaignBot.RosterCeiling(1000, CampaignNumbers.Firefight), Is.EqualTo(1125));
            Assert.That(CampaignBot.RosterCeiling(1000, CampaignNumbers.Skirmish), Is.EqualTo(1100));
        }

        [Test]
        public void Spend_WithNoBook_BanksItsPointsInsteadOfThrowing()
        {
            (BuiltArmyFile army, _) = Bot(treasury: 400);

            List<string> log = CampaignBot.BetweenMissions(army, null, CampaignNumbers.Firefight,
                rivalValue: 2000, Dice(3));

            Assert.That(army.StrikeTeam!.BankedPoints, Is.EqualTo(400),
                "a hand-written enemy army has nothing to buy from, and must not break the write-back.");
            Assert.That(log, Is.Empty);
        }

        // -- Pending choices --------------------------------------------------------------------------

        /// <summary>
        /// Nobody is going to pick the enemy's traits for it, and a unit with an unresolved choice is a
        /// unit that has quietly stopped levelling - so this is the difference between a bot that levels
        /// and a bot that appears to.
        /// </summary>
        [Test]
        public void ResolveChoices_TakesEveryChoiceWaitingOnItsSheets()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 0);
            UnitFileEntry unit = army.Units[0];
            StrikeUnit sheet = CampaignRules.Sheet(unit)!;
            sheet.Pending.Add(CampaignTables.FastLearner);
            CampaignRules.Save(unit, sheet);

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 100, Dice(3));

            StrikeUnit after = CampaignRules.Sheet(unit)!;
            Assert.That(after.Pending, Is.Empty, "nothing may be left waiting on a bot's sheet.");
            Assert.That(after.Traits, Is.Not.Empty, "and the choice must actually have been taken.");
        }

        [Test]
        public void ResolveChoices_DropsAChoiceWithNothingLeftToPick()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 0);
            UnitFileEntry unit = army.Units[0];
            StrikeUnit sheet = CampaignRules.Sheet(unit)!;
            // Every level-up trait already taken, so Fast Learner has no options at all.
            sheet.Traits.AddRange(CampaignRules.TraitsLeft(sheet));
            sheet.Pending.Add(CampaignTables.FastLearner);
            CampaignRules.Save(unit, sheet);

            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 100, Dice(3));

            Assert.That(CampaignRules.Sheet(unit)!.Pending, Is.Empty,
                "an unpickable choice must be dropped, not left to block the sheet for ever.");
        }

        // -- What it brings ---------------------------------------------------------------------------

        /// <summary>
        /// Veterans first. A bot that filled its cap in file order would leave its levelled units at
        /// home, and its levelling would never be felt across the table - which is the only reason it
        /// has a campaign sheet at all.
        /// </summary>
        [Test]
        public void PickWhatToBring_TakesItsMostExperiencedFirst()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 500);
            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 2000, Dice(3));
            Assert.That(army.Units.Count, Is.GreaterThan(2), "precondition: more units than fit the cap.");

            // Give the LAST unit in the file the experience, so file order and XP order disagree.
            UnitFileEntry veteran = army.Units[^1];
            StrikeUnit sheet = CampaignRules.Sheet(veteran)!;
            sheet.Xp = 20;
            CampaignRules.Save(veteran, sheet);

            List<string> bringing = CampaignBot.PickWhatToBring(army, cap: veteran.PointCost);

            Assert.That(bringing, Does.Contain(CampaignRules.Sheet(veteran)!.Id),
                "the veteran must be the one that comes.");
            Assert.That(bringing.Count, Is.EqualTo(1));
        }

        [Test]
        public void PickWhatToBring_StaysUnderTheCap()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 500);
            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 2000, Dice(3));

            List<string> bringing = CampaignBot.PickWhatToBring(army, cap: 150);

            int total = army.Units
                .Where(u => CampaignRules.Sheet(u) is { } sh && bringing.Contains(sh.Id))
                .Sum(u => u.PointCost);
            Assert.That(total, Is.LessThanOrEqualTo(150));
            Assert.That(army.StrikeTeam!.Bringing, Is.EqualTo(bringing), "and it is recorded on the army.");
        }

        /// <summary>
        /// A mission with no enemy is worse than a mission with an oversized one, so one unit always
        /// comes even when a cap of zero would admit none.
        /// </summary>
        [Test]
        public void PickWhatToBring_AlwaysBringsAtLeastOneUnit()
        {
            (BuiltArmyFile army, _) = Bot(treasury: 0);

            List<string> bringing = CampaignBot.PickWhatToBring(army, cap: 0);

            Assert.That(bringing.Count, Is.EqualTo(1));
        }

        [Test]
        public void PickWhatToBring_LeavesRecoveringUnitsAtHome()
        {
            (BuiltArmyFile army, BookFile book) = Bot(treasury: 500);
            CampaignBot.BetweenMissions(army, book, CampaignNumbers.Firefight, rivalValue: 2000, Dice(3));
            UnitFileEntry hurt = army.Units[0];
            StrikeUnit sheet = CampaignRules.Sheet(hurt)!;
            sheet.MissionsOut = 2;
            CampaignRules.Save(hurt, sheet);

            List<string> bringing = CampaignBot.PickWhatToBring(army, cap: 10_000);

            Assert.That(bringing, Does.Not.Contain(CampaignRules.Sheet(hurt)!.Id),
                "#427's recovering units apply to the bot as much as to the player.");
            Assert.That(bringing, Is.Not.Empty);
        }

        // -- Its special objective --------------------------------------------------------------------

        [Test]
        public void RollSpecialObjective_PutsOneOnTheBotsOwnSheet()
        {
            (BuiltArmyFile army, _) = Bot(treasury: 0);

            string objective = CampaignBot.RollSpecialObjective(army, Dice(2));

            Assert.That(objective, Is.Not.Empty);
            Assert.That(army.StrikeTeam!.SpecialObjective, Is.EqualTo(objective),
                "the enemy earns on the same terms as the player, so it needs one too.");
            Assert.That(CampaignTables.SpecialObjectives.Any(o => o.Name == objective), Is.True);
        }

        [Test]
        public void RollSpecialObjective_NeverGivesHexerToAnArmyWithNoCaster()
        {
            (BuiltArmyFile army, _) = Bot(treasury: 0);

            // A die that always rolls Hexer's face must still not produce it.
            int hexer = Array.FindIndex(CampaignTables.SpecialObjectives, o => o.Name == "Hexer") + 1;
            string objective = CampaignBot.RollSpecialObjective(army, Dice(hexer));

            Assert.That(objective, Is.Not.EqualTo("Hexer"),
                "the book re-rolls an objective the army could not complete.");
        }
    }
}
