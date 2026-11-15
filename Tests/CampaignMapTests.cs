using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Campaign;
using FDG.Campaign.Map;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #467 - the campaign map (Kai, 2026-11-14; project doc claude/campaign-map-spec.md): generation, the house rules
    /// for reaching and paying for land, VP, sieges, clashes, rival rolls and resuming a turn half way.
    /// </summary>
    [TestFixture]
    public class CampaignMapTests
    {
        private static readonly CampaignNumbers GF = CampaignNumbers.GrimdarkFuture;

        private static List<MapFaction> Armies(int rivals) =>
            new[] { new MapFaction { Id = CampaignMap.PlayerId, Name = "Iron Reavers", IsPlayer = true } }
                .Concat(Enumerable.Range(1, rivals).Select(i => new MapFaction { Id = "r" + i, Name = "Rival " + i, Colour = i }))
                .ToList();

        private static Dictionary<string, MapArmyState> Books(CampaignMap map, int treasury = 150, int points = 600) =>
            map.Factions.ToDictionary(f => f.Id, _ => new MapArmyState(treasury, points, 0));

        /// <summary>
        /// A hand-made line of regions: A - B - C - D - E, you hold A (capital) and B, r1 holds E (capital) and D, C is
        /// neutral.
        /// </summary>
        private static CampaignMap Line()
        {
            var map = new CampaignMap { Seed = 5 };
            map.Factions.AddRange(Armies(1));
            string[] ids = { "A", "B", "C", "D", "E" };
            for (int i = 0; i < ids.Length; i++)
                map.Regions.Add(new MapRegion { Id = ids[i], Name = "Region " + ids[i], X = i / 4f, Y = 0.5f, Type = ERegionType.Shrine });
            for (int i = 0; i + 1 < ids.Length; i++) map.Links.Add(new MapLink(ids[i], ids[i + 1]));
            map.Region("A")!.Owner = "you"; map.Region("A")!.Capital = true; map.Region("A")!.Starred = true;
            map.Region("B")!.Owner = "you";
            map.Region("D")!.Owner = "r1";
            map.Region("E")!.Owner = "r1"; map.Region("E")!.Capital = true; map.Region("E")!.Starred = true;
            return map;
        }

        // ------------------------------------------------------------------ generation

        [TestCase(EMapSize.Small, 1, 8)]
        [TestCase(EMapSize.Medium, 2, 14)]
        [TestCase(EMapSize.Large, 3, 20)]
        [TestCase(EMapSize.Small, 3, 12)] // four armies need room: two each plus four neutral
        public void Generate_MakesTheRightNumberOfRegions(EMapSize size, int rivals, int expected)
        {
            CampaignMap map = CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, size, Armies(rivals), 42);
            Assert.That(map.Regions.Count, Is.EqualTo(expected));
        }

        [Test]
        public void Generate_TheSameSeedMakesTheSameMap()
        {
            string a = CampaignMapGenerator.Generate(EGameSystem.AgeOfFantasy, EMapSize.Medium, Armies(2), 7).ToJson();
            string b = CampaignMapGenerator.Generate(EGameSystem.AgeOfFantasy, EMapSize.Medium, Armies(2), 7).ToJson();
            string c = CampaignMapGenerator.Generate(EGameSystem.AgeOfFantasy, EMapSize.Medium, Armies(2), 8).ToJson();
            Assert.That(a, Is.EqualTo(b));
            Assert.That(a, Is.Not.EqualTo(c));
        }

        [Test]
        public void Generate_StyleFollowsTheGame()
        {
            Assert.That(CampaignMapGenerator.Generate(EGameSystem.GFFirefight, EMapSize.Small, Armies(1), 1).Style, Is.EqualTo(EMapStyle.StarSystem));
            Assert.That(CampaignMapGenerator.Generate(EGameSystem.AoFSkirmish, EMapSize.Small, Armies(1), 1).Style, Is.EqualTo(EMapStyle.Realm));
        }

        [Test]
        public void Generate_ManySeeds_AreConnectedFairAndUncrossed()
        {
            int roomy = 0, touching = 0;
            for (int seed = 0; seed < 150; seed++)
            {
                int rivals = 1 + seed % 3;
                var size = (EMapSize)(seed % 3);
                CampaignMap map = CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, size, Armies(rivals), seed);
                string where = $"seed {seed}, {size}, {rivals} rivals";

                // Connected: every region reachable from the first.
                Assert.That(CampaignMapGenerator.Distances(map, map.Regions[0].Id).Count, Is.EqualTo(map.Regions.Count), "connected - " + where);
                // Every army: exactly one capital (starred, the capital type) and one starting region of the shared type.
                foreach (MapFaction f in map.Factions)
                {
                    var held = CampaignMapRules.Held(map, f.Id).ToList();
                    Assert.That(held.Count(r => r.Capital), Is.EqualTo(1), "one capital - " + where);
                    Assert.That(held.Single(r => r.Capital).Starred, Is.True);
                    Assert.That(held.Single(r => r.Capital).Type, Is.EqualTo(CampaignMapGenerator.CapitalType));
                    Assert.That(held.Count, Is.EqualTo(2), "capital + one start region - " + where);
                    Assert.That(held.Single(r => !r.Capital).Type, Is.EqualTo(CampaignMapGenerator.StartingRegionType));
                }
                // Fortresses are starred; nothing else neutral is.
                Assert.That(map.Regions.Where(r => r.Owner == null).All(r => r.Starred == (r.Type == ERegionType.Fortress)), Is.True, where);
                // Names are unique.
                Assert.That(map.Regions.Select(r => r.Name).Distinct().Count(), Is.EqualTo(map.Regions.Count), "names - " + where);
                // Ids in links exist; no duplicate links.
                Assert.That(map.Links.All(l => map.Region(l.A) != null && map.Region(l.B) != null), Is.True);
                Assert.That(map.Links.Select(l => string.CompareOrdinal(l.A, l.B) < 0 ? l.A + l.B : l.B + l.A).Distinct().Count(), Is.EqualTo(map.Links.Count));
                // On a Medium or Large map armies should rarely border each other on turn 1 (counted below).
                if (size != EMapSize.Small)
                {
                    roomy++;
                    if (map.Factions.Any(f => map.Factions.Where(o => o != f).Any(o => CampaignMapRules.HasMet(map, f.Id, o.Id)))) touching++;
                }
                // Capitals aren't next to each other.
                var caps = map.Regions.Where(r => r.Capital).ToList();
                foreach (var c in caps)
                    Assert.That(caps.Any(o => o != c && CampaignMapRules.Adjacent(map, c.Id, o.Id)), Is.False, "capitals apart - " + where);
            }
            TestContext.WriteLine($"turn-1 contact on {touching} of {roomy} Medium/Large maps");
            Assert.That(touching, Is.LessThanOrEqualTo(roomy / 5), "armies mostly start apart on the bigger maps");
        }

        [Test]
        public void Generate_RoundTripsThroughItsFile()
        {
            CampaignMap map = CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, EMapSize.Medium, Armies(2), 3);
            CampaignMap back = CampaignMap.FromJson(map.ToJson())!;
            Assert.That(back.ToJson(), Is.EqualTo(map.ToJson()));
            Assert.That(back.Regions[0].Type, Is.EqualTo(map.Regions[0].Type));
        }

        // ------------------------------------------------------------------ rules

        [Test]
        public void ClaimCost_IsTheSpecialObjectiveBonusPerRegionHeld()
        {
            CampaignMap map = Line();
            Assert.That(CampaignMapRules.ClaimCost(map, "you", GF), Is.EqualTo(75 * 2));
            map.Region("C")!.Owner = "you";
            Assert.That(CampaignMapRules.ClaimCost(map, "you", GF), Is.EqualTo(75 * 3));
            Assert.That(CampaignMapRules.ClaimCost(map, "you", CampaignNumbers.Firefight), Is.EqualTo(40 * 3), "each game's own number");
        }

        [Test]
        public void Reach_IsNextToYourLand_OrTwoStepsFromAPort()
        {
            CampaignMap map = Line();
            Assert.That(CampaignMapRules.CanReach(map, "you", map.Region("C")!), Is.True);
            Assert.That(CampaignMapRules.CanReach(map, "you", map.Region("D")!), Is.False);
            Assert.That(CampaignMapRules.CanReach(map, "you", map.Region("B")!), Is.False, "never your own");
            map.Region("B")!.Type = ERegionType.Port;
            Assert.That(CampaignMapRules.CanReach(map, "you", map.Region("D")!), Is.True, "a spaceport reaches two jumps");
            Assert.That(CampaignMapRules.CanReach(map, "you", map.Region("E")!), Is.False);
        }

        [Test]
        public void Fog_LiftsWhenARivalBordersYou()
        {
            CampaignMap map = Line();
            Assert.That(CampaignMapRules.HasMet(map, "you", "r1"), Is.False);
            map.Region("C")!.Owner = "r1";
            Assert.That(CampaignMapRules.HasMet(map, "you", "r1"), Is.True);
        }

        [Test]
        public void Vp_IsMissionVpPlusStarredRegionsHeld()
        {
            CampaignMap map = Line();
            map.Region("C")!.Starred = true;
            Assert.That(CampaignMapRules.Vp(map, "you", 4), Is.EqualTo(5));
            map.Region("C")!.Owner = "you";
            Assert.That(CampaignMapRules.Vp(map, "you", 4), Is.EqualTo(6));
        }

        [Test]
        public void Outcome_CapitalsAndTheTarget()
        {
            CampaignMap map = Line();
            var books = Books(map);
            Assert.That(CampaignMapRules.Outcome(map, books), Is.EqualTo(EMapOutcome.InProgress));
            books["you"] = new MapArmyState(0, 600, 9); // 9 + 1 starred capital = 10
            Assert.That(CampaignMapRules.Outcome(map, books), Is.EqualTo(EMapOutcome.YouWon));
            books["r1"] = new MapArmyState(0, 600, 9);
            Assert.That(CampaignMapRules.Outcome(map, books), Is.EqualTo(EMapOutcome.Draw));
            map.Faction("r1")!.Alive = false;
            Assert.That(CampaignMapRules.Outcome(map, Books(map)), Is.EqualTo(EMapOutcome.YouWon), "every rival out");
            map.Player.Alive = false;
            Assert.That(CampaignMapRules.Outcome(map, Books(map)), Is.EqualTo(EMapOutcome.YouLost));
        }

        [Test]
        public void Underdog_ShrinesCapitalsAndFortresses()
        {
            CampaignMap map = Line(); // every region in Line() is a Shrine
            Assert.That(CampaignMapRules.UnderdogBonus(map, "you", map.Region("A")!), Is.EqualTo(2 + CampaignMapRules.CapitalDefenceUnderdog));
            Assert.That(CampaignMapRules.UnderdogBonus(map, "you", map.Region("C")!), Is.EqualTo(2), "two shrines, not defending");
            map.Region("B")!.Type = ERegionType.Fortress;
            Assert.That(CampaignMapRules.UnderdogBonus(map, "you", map.Region("B")!), Is.EqualTo(1 + CampaignMapRules.FortressDefenceUnderdog));
        }

        [Test]
        public void WhyNot_TellsYouWhatIsWrong()
        {
            CampaignMap map = Line();
            var ok = new MapArmyState(1000, 600, 0);
            Assert.That(CampaignMapRules.WhyNot(map, new MapOrder("you", EOrderKind.Claim, "C"), ok, GF), Is.Null);
            Assert.That(CampaignMapRules.WhyNot(map, new MapOrder("you", EOrderKind.Claim, "C"), new MapArmyState(100, 600, 0), GF), Does.Contain("150"));
            Assert.That(CampaignMapRules.WhyNot(map, new MapOrder("you", EOrderKind.Attack, "C"), ok, GF), Is.Not.Null, "nobody to attack");
            Assert.That(CampaignMapRules.WhyNot(map, new MapOrder("you", EOrderKind.Attack, "D"), ok, GF), Is.Not.Null, "not reachable");
            Assert.That(CampaignMapRules.WhyNot(map, new MapOrder("you", EOrderKind.Rest), ok, GF), Is.Null);
        }

        // ------------------------------------------------------------------ turns

        /// <summary>Resolves the turn under way, answering each player battle with <paramref name="results"/> in order.</summary>
        private static MapTurnResult Play(CampaignMap map, params bool[] results)
        {
            int i = 0;
            while (true)
            {
                MapTurnResult r = CampaignMapTurn.Resolve(map, GF);
                if (r.Finished) return r;
                Assert.That(i, Is.LessThan(results.Length), "an unexpected player battle: " + r.WaitingFor);
                CampaignMapTurn.RecordPlayerResult(map, results[i++]);
            }
        }

        /// <summary>A turn where only the player acts (the rival is out of reach and rests).</summary>
        private static CampaignMap LineWithRivalResting()
        {
            CampaignMap map = Line();
            map.Links.RemoveAll(l => l.Touches("D") && l.Touches("C")); // the rival can't reach anything
            return map;
        }

        [Test]
        public void Claim_PaysAndTakes_WithoutABattle()
        {
            CampaignMap map = LineWithRivalResting();
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Claim, "C"), Books(map, 500), GF);
            MapTurnResult r = Play(map);
            Assert.That(r.Map.Region("C")!.Owner, Is.EqualTo("you"));
            Assert.That(r.Changes["you"].Treasury, Is.EqualTo(-150));
            Assert.That(r.Map.Turn, Is.EqualTo(2));
            Assert.That(r.Map.Pending, Is.Null);
            Assert.That(map.Region("C")!.Owner, Is.Null, "Resolve never changes the map it was given");
        }

        [Test]
        public void Garrison_IsAPlayerBattle_ThatPaysNothing()
        {
            CampaignMap map = LineWithRivalResting();
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Garrison, "C"), Books(map), GF);
            MapTurnResult first = CampaignMapTurn.Resolve(map, GF);
            Assert.That(first.WaitingFor, Is.EqualTo(new MapBattle("C", "you", null, EMapBattleKind.Garrison, false)));
            MapTurnResult won = Play(map, true);
            Assert.That(won.Map.Region("C")!.Owner, Is.EqualTo("you"));
            Assert.That(won.Changes["you"].Treasury, Is.EqualTo(0));
            Assert.That(won.Changes["you"].MissionVp, Is.EqualTo(0));
        }

        [Test]
        public void ACapital_TakesTwoWins_AndLosingItKnocksTheArmyOut()
        {
            CampaignMap map = Line();
            map.Region("C")!.Owner = "you";
            map.Region("D")!.Owner = "you";      // you are at the rival's gate
            map.Region("B")!.Owner = null;
            map.Links.RemoveAll(l => l.Touches("A") || l.Touches("B")); // keep the rival busy with nothing but its capital
            var books = Books(map);

            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Attack, "E"), books, GF);
            MapTurnResult one = Play(map, true, true); // the rival may also attack you this turn - you hold it off
            Assert.That(one.Map.Region("E")!.Owner, Is.EqualTo("r1"), "the first win only besieges");
            Assert.That(one.Map.Region("E")!.Siege, Is.EqualTo("you"));

            CampaignMap m2 = one.Map;
            CampaignMapTurn.Begin(m2, new MapOrder("you", EOrderKind.Attack, "E"), books, GF);
            MapTurnResult two = Play(m2, true, true); // the rival may also attack you this turn
            Assert.That(two.Map.Region("E")!.Owner, Is.EqualTo("you"));
            Assert.That(two.Map.Faction("r1")!.Alive, Is.False);
            Assert.That(two.Map.Region("E")!.Capital, Is.False, "a taken capital is just a region");
            Assert.That(CampaignMapRules.Outcome(two.Map, books), Is.EqualTo(EMapOutcome.YouWon));
        }

        [Test]
        public void ADefenderWin_LiftsTheSiege()
        {
            CampaignMap map = Line();
            map.Region("D")!.Owner = "you";
            map.Region("E")!.Siege = "you";
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Attack, "E"), Books(map), GF);
            MapTurnResult r = Play(map, false, false, false);
            Assert.That(r.Map.Region("E")!.Siege, Is.Null);
            Assert.That(r.Map.Region("E")!.Owner, Is.EqualTo("r1"));
        }

        [Test]
        public void TwoArmiesOnOneNeutralRegion_MeetThere_AndTheWinnerTakesIt()
        {
            CampaignMap map = Line();
            map.Region("D")!.Type = ERegionType.Hive;
            // Force the rival's order: give it a reason to want C and only C.
            var books = Books(map, 0);
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Garrison, "C"), books, GF);
            map.Pending!.Orders.RemoveAll(o => o.Faction == "r1");
            map.Pending.Orders.Add(new MapOrder("r1", EOrderKind.Garrison, "C"));

            MapTurnResult first = CampaignMapTurn.Resolve(map, GF);
            Assert.That(first.WaitingFor!.Kind, Is.EqualTo(EMapBattleKind.Clash));
            Assert.That(first.WaitingFor.OpponentOf("you"), Is.EqualTo("r1"));
            MapTurnResult r = Play(map, false);
            Assert.That(r.Map.Region("C")!.Owner, Is.EqualTo("r1"));
        }

        [Test]
        public void Rest_EarnsTheBonus_AndHivesPayEveryTurn()
        {
            CampaignMap map = LineWithRivalResting();
            map.Region("A")!.Type = ERegionType.Hive;
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Rest), Books(map), GF);
            MapTurnResult r = Play(map);
            Assert.That(r.Changes["you"].Treasury, Is.EqualTo(75 + 75), "rest + one hive");
        }

        [Test]
        public void RivalAgainstRival_IsAnOffScreenRoll_ThatPaysTheBooksRewards()
        {
            CampaignMap map = CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, EMapSize.Small, Armies(2), 11);
            // Put r1 next to r2 and point r1 at r2's start region, with you resting.
            MapRegion r2Start = map.Regions.First(r => r.Owner == "r2" && !r.Capital);
            MapRegion r1Cap = map.Regions.First(r => r.Owner == "r1" && r.Capital);
            map.Links.Add(new MapLink(r1Cap.Id, r2Start.Id));
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Rest), Books(map), GF);
            map.Pending!.Orders.RemoveAll(o => o.Faction != "you");
            map.Pending.Orders.Add(new MapOrder("r1", EOrderKind.Attack, r2Start.Id));
            MapTurnResult r = Play(map);
            int vp = r.Changes["r1"].MissionVp + r.Changes["r2"].MissionVp;
            Assert.That(vp, Is.EqualTo(CampaignNumbers.WinVp), "one winner, 2 VP");
            string winner = r.Changes["r1"].MissionVp > 0 ? "r1" : "r2", loser = winner == "r1" ? "r2" : "r1";
            Assert.That(r.Changes[winner].Treasury, Is.EqualTo(GF.WinPoints + CampaignMapRules.TurnIncome(r.Map, winner, GF)));
            Assert.That(r.Changes[loser].Treasury, Is.EqualTo(GF.LosePoints + CampaignMapRules.TurnIncome(r.Map, loser, GF)));
        }

        [Test]
        public void AHalfPlayedTurn_ReplaysTheSameAfterASaveAndLoad()
        {
            CampaignMap map = CampaignMapGenerator.Generate(EGameSystem.GrimdarkFuture, EMapSize.Medium, Armies(3), 21);
            MapRegion target = CampaignMapRules.Reachable(map, "you").First(r => r.Owner == null);
            CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Garrison, target.Id), Books(map), GF);
            CampaignMapTurn.RecordPlayerResult(map, true);
            CampaignMap reloaded = CampaignMap.FromJson(map.ToJson())!;
            MapTurnResult a = Play(map, true, true, true);
            MapTurnResult b = Play(reloaded, true, true, true);
            Assert.That(b.Map.ToJson(), Is.EqualTo(a.Map.ToJson()));
            Assert.That(b.Lines, Is.EqualTo(a.Lines));
        }

        [Test]
        public void Begin_RefusesAnOrderThePlayerCantGive()
        {
            CampaignMap map = Line();
            Assert.Throws<InvalidOperationException>(() =>
                CampaignMapTurn.Begin(map, new MapOrder("you", EOrderKind.Attack, "E"), Books(map), GF));
            Assert.That(map.Pending, Is.Null);
        }

        /// <summary>
        /// Whole campaigns, the player played by the bot and every battle a coin flip: they always end, never crash, and
        /// every order the bot gives is one the rules allow.
        /// </summary>
        [Test]
        public void WholeCampaigns_PlayedOut_AlwaysFinish()
        {
            int finished = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var rng = new Random(seed);
                CampaignMap map = CampaignMapGenerator.Generate(seed % 2 == 0 ? EGameSystem.GrimdarkFuture : EGameSystem.AgeOfFantasy,
                    (EMapSize)(seed % 3), Armies(1 + seed % 3), seed);
                var books = map.Factions.ToDictionary(f => f.Id, _ => new MapArmyState(150, 600, 0));
                for (int turn = 0; turn < 200 && CampaignMapRules.Outcome(map, books) == EMapOutcome.InProgress; turn++)
                {
                    MapOrder mine = CampaignMapBot.Order(map, "you", books, GF, rng);
                    foreach (MapFaction f in map.Alive.Where(f => !f.IsPlayer))
                        Assert.That(CampaignMapRules.WhyNot(map, CampaignMapBot.Order(map, f.Id, books, GF, new Random(turn)), books[f.Id], GF), Is.Null);
                    CampaignMapTurn.Begin(map, mine, books, GF);
                    MapTurnResult r;
                    while (!(r = CampaignMapTurn.Resolve(map, GF)).Finished) CampaignMapTurn.RecordPlayerResult(map, rng.Next(2) == 0);
                    foreach (var (id, c) in r.Changes)
                    {
                        var s = books[id];
                        books[id] = new MapArmyState(Math.Max(0, s.Treasury + c.Treasury), s.Points, s.MissionVp + c.MissionVp);
                    }
                    map = r.Map;
                }
                if (CampaignMapRules.Outcome(map, books) != EMapOutcome.InProgress) finished++;
            }
            Assert.That(finished, Is.EqualTo(60), "every campaign reached an end within 200 turns");
        }
    }
}
