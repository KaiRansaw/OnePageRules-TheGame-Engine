using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #428 - the claim the bot campaign is built on, tested over a whole campaign rather than one
    /// mission.
    ///
    /// <para>
    /// Asked how hard the bot should play, Kai said "Unsure", so the engine's call was: <b>play the book
    /// straight and let the book's own rubber band do the balancing.</b> That is a design claim, and an
    /// unverified one is just an opinion - so these tests play ten missions and look at where the two
    /// armies end up. The rubber band is two printed rules working together:
    /// </para>
    /// <list type="bullet">
    ///   <item>the <b>loser is paid about twice the winner</b> (Grimdark Future: 300 against 150), so
    ///   whoever is behind banks faster;</item>
    ///   <item>every mission army is capped at <see cref="CampaignNumbers.ArmyCapOverLowest"/> over the
    ///   smallest army present, so whoever is ahead cannot bring all of it.</item>
    /// </list>
    ///
    /// <para>
    /// The two failures worth catching are the two ends of it: a bot that <b>never improves</b> (you farm
    /// it, and your own progress stops meaning anything) and a bot that <b>runs away</b> (nothing you do
    /// can catch it). This fixture asserts neither happens.
    /// </para>
    ///
    /// <para>
    /// The mission itself is stubbed, deliberately: these tests are about the economy between missions,
    /// not about who wins a fight. Each "mission" takes a fixed share of each side's units as casualties
    /// and hands the result to <see cref="CampaignRules.AfterGame"/>, which is the same call the real
    /// write-back makes.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignArcTests
    {
        private const string TrooperId = "trooper";
        private const string ScoutId = "scout";
        private const string CaptainId = "captain";

        private static readonly CampaignNumbers N = CampaignNumbers.GrimdarkFuture;

        /// <summary>A die that walks 1..6 forever, so a long run sees the whole of every table.</summary>
        private static Func<int> Cycling()
        {
            int i = 0;
            return () => i++ % 6 + 1;
        }

        private static BookFile Book() => new()
        {
            Name = "Test Legion",
            Faction = "Test Legion",
            Units =
            {
                new RosterUnit
                {
                    Id = ScoutId, Name = "Scouts", Quality = 4, Defense = 5,
                    BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 60,
                    Weapons = { new WeaponFileEntry { Name = "Carbine", RangeInches = 18, Attacks = 1 } },
                },
                new RosterUnit
                {
                    Id = TrooperId, Name = "Troopers", Quality = 4, Defense = 4,
                    BaseModelCount = 5, MinModels = 5, MaxModels = 5, BasePointCost = 150,
                    Weapons = { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
                new RosterUnit
                {
                    Id = CaptainId, Name = "Captain", Quality = 3, Defense = 4,
                    BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 80,
                    Weapons = { new WeaponFileEntry { Name = "Blade", Attacks = 3 } },
                    Rules = { new SpecialRuleEntry_Core("Hero") },
                },
            },
        };

        /// <summary>A started campaign army of four squads: 600 pts of GF's 750 pt treasury.</summary>
        private static BuiltArmyFile Army(string name, bool isBot)
        {
            BookFile book = Book();
            var list = new BuilderList { Name = name, BookName = book.Name };
            for (int i = 0; i < 4; i++) list.Units.Add(new BuilderUnit { RosterUnitId = TrooperId, ModelCount = 5 });
            BuiltArmyFile army = ListCompiler.Compile(book, list);
            CampaignRules.Start(army, name, name + ".fdgarmy", N, EGameSystem.GrimdarkFuture,
                ECampaignType.Endless, noPermadeath: false, isBot: isBot);
            return army;
        }

        /// <summary>
        /// One mission: both sides bring what they can under the cap, a share of each side's units are
        /// casualties, the result is written back, and the bot then spends. The same order the real
        /// write-back and campaign screen run in.
        /// </summary>
        private static (int PlayerBrought, int BotBrought) PlayMission(BuiltArmyFile player,
            BuiltArmyFile bot, bool playerWins, Func<int> d6, int casualtyEveryNth = 3)
        {
            int playerBrings = player.Units.Where(CampaignRules.CanBringToMission).Sum(u => u.PointCost);
            List<string> botBringing = CampaignBot.PickWhatToBring(bot, playerBrings + N.ArmyCapOverLowest);
            int botBrings = bot.Units
                .Where(u => CampaignRules.Sheet(u) is { } sh && botBringing.Contains(sh.Id))
                .Sum(u => u.PointCost);

            CampaignRules.AfterGame(player, Results(player, null, casualtyEveryNth), playerWins, N, d6);
            CampaignRules.AfterGame(bot, Results(bot, bot.StrikeTeam!.Bringing, casualtyEveryNth), !playerWins, N, d6);
            CampaignBot.BetweenMissions(bot, Book(), N, player.TotalPoints, d6);
            return (playerBrings, botBrings);
        }

        /// <summary>
        /// Every unit that played, with every nth one a casualty. <paramref name="bringing"/> null means
        /// the whole army played.
        /// </summary>
        private static List<CampaignUnitResult> Results(ArmyListFile army, List<string>? bringing, int nth)
        {
            var results = new List<CampaignUnitResult>();
            int index = 0;
            foreach (UnitFileEntry unit in army.Units)
            {
                if (CampaignRules.Sheet(unit) is not { } sheet) continue;
                if (!CampaignRules.CanBringToMission(unit)) continue;
                if (bringing is { Count: > 0 } && !bringing.Contains(sheet.Id)) continue;
                results.Add(new CampaignUnitResult(sheet.Id, Casualty: index % nth == 0, Kills: 1, HeroKills: 0));
                index++;
            }
            return results;
        }


        /// <summary>
        /// The farming failure. The player wins every single mission, which is the worst case for the
        /// bot: it is paid the loser's share every time and never a win bonus. It must still be
        /// visibly improving - banking points, levelling units and recruiting - or there is no campaign
        /// here, just a target.
        /// </summary>
        [Test]
        public void TenMissionsWonByThePlayer_StillLeaveTheBotImproving()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            int botStart = bot.TotalPoints;
            Func<int> d6 = Cycling();

            for (int mission = 0; mission < 10; mission++) PlayMission(player, bot, playerWins: true, d6);

            Assert.That(bot.StrikeTeam!.Matches, Is.EqualTo(10), "the bot played every mission.");
            Assert.That(bot.TotalPoints, Is.GreaterThan(botStart),
                "an enemy that loses ten times must still be worth more than it started - the book pays "
                + "the loser twice the winner's treasury precisely so that it can be.");
            Assert.That(bot.Units.Sum(u => CampaignRules.Sheet(u)?.Xp ?? 0), Is.GreaterThan(0),
                "and its units must have earned XP, so the player meets levelled enemies.");
        }

        /// <summary>
        /// The runaway failure, the other end. Ten missions the BOT wins: it must not end able to put
        /// more on the table than the player can answer.
        ///
        /// <para><b>What the right bound is, and what it is not.</b> The first version of this test
        /// asserted the bot's whole ROSTER stayed under <see cref="CampaignBot.RosterCeiling"/>, and it
        /// failed - 1,165 pts against a 1,000 pt ceiling. That was the test being wrong, not the bot:
        /// the roster ceiling is a bound on what the bot will BUY, measured at the moment it buys, and
        /// two things move it afterwards. Level-ups make a unit cost more without anything being bought,
        /// and the player's own army shrinks when it loses units for good, which drops the ceiling below
        /// what the bot already owned. Neither is the bot overspending.</para>
        ///
        /// <para>The rule that actually governs a game is the book's own: a mission army may be at most
        /// <see cref="CampaignNumbers.ArmyCapOverLowest"/> over the smallest army present. So the
        /// invariant to hold a bot to is <b>what it brings</b>, every mission, which is what
        /// <see cref="CampaignBot.PickWhatToBring"/> is for. A bot may own a stable of veterans it
        /// cannot field; it may not put them all on the table.</para>
        /// </summary>
        [Test]
        public void TenMissionsWonByTheBot_DoNotLetItOutBringThePlayer()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            Func<int> d6 = Cycling();

            for (int mission = 0; mission < 10; mission++)
            {
                (int playerBrought, int botBrought) = PlayMission(player, bot, playerWins: false, d6);
                Assert.That(botBrought, Is.LessThanOrEqualTo(playerBrought + N.ArmyCapOverLowest),
                    $"on mission {mission + 1} the bot fielded more than the book's cap allows.");
            }

            Assert.That(bot.StrikeTeam!.Vp, Is.EqualTo(10 * CampaignNumbers.WinVp),
                "precondition: the bot really did win all ten.");
        }

        /// <summary>
        /// The ordinary case: results alternate. Both armies should be growing, and the bot should be
        /// keeping pace rather than either falling away or pulling ahead.
        /// </summary>
        [Test]
        public void AlternatingResults_LeaveBothArmiesGrowingTogether()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            Func<int> d6 = Cycling();

            for (int mission = 0; mission < 10; mission++)
                PlayMission(player, bot, playerWins: mission % 2 == 0, d6);

            Assert.That(bot.StrikeTeam!.Vp, Is.GreaterThan(0), "the bot scores VP for its wins.");
            Assert.That(player.StrikeTeam!.Vp, Is.GreaterThan(0));
            Assert.That(player.TotalPoints, Is.GreaterThan(0), "and both armies survived the campaign.");
            Assert.That(bot.TotalPoints, Is.GreaterThan(0));
        }

        /// <summary>
        /// The cap holds every mission of a long campaign, not just on the one call that was
        /// unit-tested. This is the test that would catch a slow leak - a bot fielding 20 pts more than
        /// it should every mission and being unanswerable by mission thirty.
        /// </summary>
        [Test]
        public void WhatTheBotFieldsStaysUnderTheCapForTwentyMissions()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            Func<int> d6 = Cycling();

            for (int mission = 0; mission < 20; mission++)
            {
                (int playerBrought, int botBrought) = PlayMission(player, bot, playerWins: mission % 3 != 0, d6);
                Assert.That(botBrought, Is.LessThanOrEqualTo(playerBrought + N.ArmyCapOverLowest),
                    $"the bot broke the mission cap on mission {mission + 1}.");
                Assert.That(botBrought, Is.GreaterThan(0),
                    $"and it must field SOMETHING on mission {mission + 1}.");
            }
        }

        /// <summary>
        /// Nothing is left waiting on the bot's sheets at any point. A pending choice on an army nobody
        /// opens is a unit that has quietly stopped levelling, and it would never show up as an error -
        /// the campaign would just slowly stop happening to the enemy.
        /// </summary>
        [Test]
        public void TheBotNeverLeavesAChoiceUnmade()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            Func<int> d6 = Cycling();

            for (int mission = 0; mission < 15; mission++)
            {
                PlayMission(player, bot, playerWins: mission % 2 == 0, d6);
                Assert.That(bot.Units.Sum(u => CampaignRules.Sheet(u)?.Pending.Count ?? 0), Is.Zero,
                    $"a choice was left waiting on the bot's sheet after mission {mission + 1}.");
            }
        }

        /// <summary>
        /// Leftover money must always be explained by the <b>cap</b>, never by laziness. The bot spends
        /// until it is within one cheapest-unit of its roster ceiling; anything it is still holding at
        /// that point is money it cannot legally field yet.
        ///
        /// <para><b>What this measured, and what it means for play.</b> Ten missions lost in a row takes
        /// the bot from 600 pts to about 1,165 and from 0 XP to about 97, so the arc is real - the enemy
        /// the player meets on mission ten is a different army from the one on mission one. But its
        /// treasury also climbs past 2,500, because the ceiling (the player's army plus the book's cap)
        /// stops it buying more bodies long before it runs out of money. So the bot behaves as a <b>army
        /// that is always exactly as big as the rules let it be</b>, holding a reserve it spends the
        /// moment the player's own army grows.</para>
        ///
        /// <para>That is a deliberate consequence of the cap rather than a bug, and it is the honest
        /// limit of this slice: converting that reserve into <b>quality</b> - buying upgrades for its
        /// veterans rather than more units - is what a human would do with it, and is recorded as the
        /// next step rather than guessed at here.</para>
        /// </summary>
        [Test]
        public void TheBotSpendsEverythingTheCapLetsItSpend()
        {
            BuiltArmyFile player = Army("Mine", isBot: false);
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            Func<int> d6 = Cycling();
            const int cheapestUnit = 60;   // the Scouts entry, the least the bot can buy

            for (int mission = 0; mission < 10; mission++) PlayMission(player, bot, playerWins: true, d6);

            int ceiling = CampaignBot.RosterCeiling(player.TotalPoints, N);
            Assert.That(bot.TotalPoints, Is.GreaterThan(ceiling - cheapestUnit),
                "whatever it is still holding must be because nothing more would fit under the cap - "
                + "a bot stopping short of the cap with money in the bank is just hoarding.");
        }

        /// <summary>
        /// A bot army is still a campaign army: it takes the same casualty rolls and can lose units for
        /// good. Worth pinning, because it would be easy to make the enemy quietly immortal and nobody
        /// would notice for a long time.
        /// </summary>
        [Test]
        public void TheBotReallyLosesUnits()
        {
            BuiltArmyFile bot = Army("Theirs", isBot: true);
            var ids = bot.Units.Select(u => CampaignRules.Sheet(u)!.Id).ToList();

            // Every unit a casualty, every roll a 1: dead, with permadeath on.
            CampaignRules.AfterGame(bot,
                ids.Select(id => new CampaignUnitResult(id, true, 0, 0)).ToList(),
                won: false, N, () => 1);

            Assert.That(bot.Units, Is.Empty, "the enemy is not immortal.");
        }
    }
}
