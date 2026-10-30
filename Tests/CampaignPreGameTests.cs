using FDG.ArmyBuilding;
using FDG.Campaign;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #410 - step 1 of the campaign loop (army selection, underdog points, special objectives) and the
    /// 36-entry random event table, against Campaign Rules v3.6.0.
    /// </summary>
    [TestFixture]
    public class CampaignPreGameTests
    {
        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        private static CampaignEntrant Army(string name, int points, bool caster = false) =>
            new(name, points, caster);

        // -- Army selection ---------------------------------------------------------------------------

        [Test]
        public void MissionCapIsTheLowestArmyPlusTheBooksAllowance()
        {
            var armies = new[] { Army("Mine", 900), Army("Theirs", 700) };
            Assert.That(CampaignPreGame.MaxPointsForMission(armies, CampaignNumbers.GrimdarkFuture),
                Is.EqualTo(700 + 325));
            Assert.That(CampaignPreGame.MaxPointsForMission(armies, CampaignNumbers.Firefight),
                Is.EqualTo(700 + 125));
            Assert.That(CampaignPreGame.MaxPointsForMission(armies, CampaignNumbers.Skirmish),
                Is.EqualTo(700 + 100));
        }

        [Test]
        public void CannotBringOverTheCapOrNothingAtAll()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            var others = new[] { Army("Theirs", 700) };
            Assert.That(CampaignPreGame.CannotBring(1025, others, n, unitCount: 5), Is.Null,
                "700 + 325 is exactly the cap");
            Assert.That(CampaignPreGame.CannotBring(1030, others, n, unitCount: 5), Does.Contain("1025"));
            Assert.That(CampaignPreGame.CannotBring(500, others, n, unitCount: 0), Does.Contain("at least one"));
            Assert.That(CampaignPreGame.CannotBring(99999, Array.Empty<CampaignEntrant>(), n, unitCount: 1), Is.Null,
                "with no other army there is nothing to be capped against");
        }

        // -- Underdog points --------------------------------------------------------------------------

        [TestCase(900, 0, TestName = "UnderdogPoints_TheBiggestArmyGetsNone")]
        [TestCase(835, 1, TestName = "UnderdogPoints_OnePerFullStep")]
        [TestCase(770, 2, TestName = "UnderdogPoints_TwoFullSteps")]
        [TestCase(850, 0, TestName = "UnderdogPoints_PartOfAStepCountsForNothing")]
        public void UnderdogPointsAreOnePerStepBehindTheBiggestArmy(int mine, int expected)
        {
            var armies = new[] { Army("Mine", mine), Army("Theirs", 900) };
            Assert.That(CampaignPreGame.UnderdogPoints(mine, armies, CampaignNumbers.GrimdarkFuture),
                Is.EqualTo(expected), "Grimdark Future gives one point per 65 pts behind");
        }

        [Test]
        public void UnderdogStepIsPerBook()
        {
            var armies = new[] { Army("Mine", 300), Army("Theirs", 400) };
            Assert.That(CampaignPreGame.UnderdogPoints(300, armies, CampaignNumbers.Firefight), Is.EqualTo(4),
                "100 behind at 25 pts a point");
            Assert.That(CampaignPreGame.UnderdogPoints(300, armies, CampaignNumbers.Skirmish), Is.EqualTo(5),
                "100 behind at 20 pts a point");
        }

        // -- Special objectives -----------------------------------------------------------------------

        [Test]
        public void SpecialObjectiveIsRolledFromTheSixEntryTable()
        {
            CampaignEntrant caster = Army("Mine", 500, caster: true);
            Assert.That(CampaignPreGame.RollSpecialObjective(caster, Dice(1)), Is.EqualTo("Hexer"));
            Assert.That(CampaignPreGame.RollSpecialObjective(caster, Dice(3)), Is.EqualTo("Headhunter"));
            Assert.That(CampaignPreGame.RollSpecialObjective(caster, Dice(6)), Is.EqualTo("Strategist"));
        }

        [Test]
        public void HexerIsRerolledForAnArmyWithNoCaster()
        {
            CampaignEntrant noCaster = Army("Mine", 500, caster: false);
            Assert.That(CampaignPreGame.CanComplete("Hexer", noCaster), Is.False);
            // Rolls 1 (Hexer, impossible) then 2 (Annihilator).
            Assert.That(CampaignPreGame.RollSpecialObjective(noCaster, Dice(1, 2)), Is.EqualTo("Annihilator"));
            // Every other objective is completable by anyone.
            foreach ((string name, _) in CampaignTables.SpecialObjectives.Skip(1))
                Assert.That(CampaignPreGame.CanComplete(name, noCaster), Is.True, name);
        }

        [Test]
        public void CompletingTheSpecialObjectivePaysTheBooksBonus()
        {
            CampaignNumbers n = CampaignNumbers.GrimdarkFuture;
            var army = new BuiltArmyFile { Name = "Mine", Faction = "Test", GameSystem = GameSystems.GrimdarkFuture };
            army.StrikeTeam = new StrikeTeamInfo { Name = "Mine", BankedPoints = 100 };
            string line = CampaignPreGame.CompleteSpecialObjective(army, "Strategist", n);
            Assert.That(army.StrikeTeam.BankedPoints, Is.EqualTo(175), "GF pays 75 pts");
            Assert.That(line, Does.Contain("Strategist"));
        }

        [Test]
        public void EveryObjectiveHasItsBookWording()
        {
            foreach ((string name, string text) in CampaignTables.SpecialObjectives)
            {
                Assert.That(CampaignPreGame.ObjectiveText(name), Is.EqualTo(text));
                Assert.That(text, Is.Not.Empty, name);
            }
        }

        // -- Campaign length --------------------------------------------------------------------------

        [Test]
        public void EndlessNeverEnds()
        {
            Assert.That(CampaignPreGame.IsOver(ECampaignType.Endless, 999, 999, 7, 7), Is.False);
        }

        [Test]
        public void TheLimitedTypesEndOnTheirLimit()
        {
            Assert.That(CampaignPreGame.IsOver(ECampaignType.GameLimited, 6, 0, 7, 0), Is.False);
            Assert.That(CampaignPreGame.IsOver(ECampaignType.GameLimited, 7, 0, 7, 0), Is.True);
            Assert.That(CampaignPreGame.IsOver(ECampaignType.PointLimited, 0, 6, 0, 7), Is.False);
            Assert.That(CampaignPreGame.IsOver(ECampaignType.PointLimited, 0, 7, 0, 7), Is.True);
        }

        [TestCase(1, 5)]
        [TestCase(2, 5)]
        [TestCase(3, 6)]
        [TestCase(4, 6)]
        [TestCase(5, 7)]
        [TestCase(6, 7)]
        public void CampaignLengthIsD3Plus4(int roll, int expected) =>
            Assert.That(CampaignPreGame.RollCampaignLength(Dice(roll)), Is.EqualTo(expected));

        // -- Random events ----------------------------------------------------------------------------

        [Test]
        public void TheEventTableHasAllThirtySixEntries()
        {
            Assert.That(CampaignEvents.All.Count, Is.EqualTo(36));
            foreach (int first in Enumerable.Range(1, 6))
                foreach (int second in Enumerable.Range(1, 6))
                    Assert.That(() => CampaignEvents.ByCode(first * 10 + second), Throws.Nothing,
                        $"event {first}{second} is missing");
            Assert.That(CampaignEvents.All.Select(e => e.Name).Distinct().Count(), Is.EqualTo(36));
            Assert.That(CampaignEvents.All.All(e => !string.IsNullOrWhiteSpace(e.Text)));
        }

        [Test]
        public void EventsAreRolledAsFirstDigitThenSecond()
        {
            Assert.That(CampaignEvents.Roll(Dice(2, 1)).Name, Is.EqualTo("Swarm Attack"), "the book's own example");
            Assert.That(CampaignEvents.Roll(Dice(1, 1)).Name, Is.EqualTo("Falling Debris"));
            Assert.That(CampaignEvents.Roll(Dice(6, 6)).Name, Is.EqualTo("Godly Blessing"));
            Assert.That(CampaignEvents.TriggerRoll, Is.EqualTo(5), "one die at the start of each round, 5+ fires");
        }

        [Test]
        public void TheRewardEventsCarryTheirAmounts()
        {
            CampaignEvent stash = CampaignEvents.ByCode(61);
            Assert.That(stash.Kind, Is.EqualTo(ECampaignEventKind.GainPoints));
            Assert.That(stash.Amount, Is.EqualTo(25));
            Assert.That(stash.IsD3, Is.False);

            CampaignEvent holopad = CampaignEvents.ByCode(64);
            Assert.That(holopad.Amount, Is.EqualTo(25));
            Assert.That(holopad.IsD3, Is.True, "D3 * 25pts");

            CampaignEvent trap = CampaignEvents.ByCode(12);
            Assert.That(trap.Kind, Is.EqualTo(ECampaignEventKind.Hits));
            Assert.That(trap.Amount, Is.EqualTo(2));
            Assert.That(trap.IsD3, Is.True, "2D3 hits");
        }
    }
}
