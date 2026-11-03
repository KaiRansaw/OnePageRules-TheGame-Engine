using FDG.Campaign;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #445 - a campaign can now be WON. Until this, <see cref="ECampaignType.Endless"/> was the only
    /// type wired up and both creation call sites hard-coded it, so a campaign had no end condition at
    /// all: VP accumulated and nothing ever read them.
    ///
    /// <para>
    /// The spec (claude/campaign-flow-spec.md section 3a) offers two types - a VP target you choose, or
    /// Endless - and deliberately does not offer the book's mission- and week-limited types. These cover
    /// <see cref="CampaignPreGame.Standing"/>, which is the one place that decides the question.
    /// </para>
    ///
    /// <para>
    /// The load-bearing case is <see cref="AnOldCampaignWithNoTargetIsNotInstantlyWon"/>. Every campaign
    /// saved before this field reads back with <c>VpTarget = 0</c>, and a target of zero is one that any
    /// VP total clears - so the obvious implementation ends every existing campaign the moment it is
    /// opened.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignStandingTests
    {
        private static StrikeTeamInfo Army(int vp, ECampaignType type = ECampaignType.PointLimited,
                                           int target = 7, int matches = 3) =>
            new()
            {
                Name = "Iron Reavers",
                Vp = vp,
                CampaignType = type,
                VpTarget = target,
                Matches = matches,
            };

        [Test]
        public void ARaceInProgressIsNotOver()
        {
            CampaignPreGame.CampaignStanding s = CampaignPreGame.Standing(Army(4), Army(2));

            Assert.That(s.IsOver, Is.False);
            Assert.That(s.Outcome, Is.EqualTo(ECampaignOutcome.InProgress));
            Assert.That(s.YourVp, Is.EqualTo(4));
            Assert.That(s.RivalVp, Is.EqualTo(2));
            Assert.That(s.HasTarget, Is.True);
            Assert.That(s.Describe(), Is.EqualTo("First to 7 VP"));
        }

        [Test]
        public void ReachingTheTargetWinsIt()
        {
            CampaignPreGame.CampaignStanding s = CampaignPreGame.Standing(Army(7), Army(4));

            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Outcome, Is.EqualTo(ECampaignOutcome.YouWon));
        }

        [Test]
        public void OvershootingTheTargetStillWinsIt()
        {
            // VP arrive 1-4 at a time (#435 scales them by difficulty), so landing exactly on the
            // target is the exception rather than the rule - ">=" not "==".
            Assert.That(CampaignPreGame.Standing(Army(9), Army(4)).Outcome,
                Is.EqualTo(ECampaignOutcome.YouWon));
        }

        [Test]
        public void TheRivalCanWinIt()
        {
            CampaignPreGame.CampaignStanding s = CampaignPreGame.Standing(Army(3), Army(8));

            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Outcome, Is.EqualTo(ECampaignOutcome.RivalWon),
                "the bot's campaign army scores VP exactly like the player's, so it can take the "
                + "campaign - that is the arc #428 exists for.");
        }

        /// <summary>
        /// Both over the line at once, on equal VP. Reachable: a mission pays the winner VP, and a
        /// campaign where each side has been trading wins can have both arrive together.
        /// </summary>
        [Test]
        public void BothCrossingLevelIsADraw()
        {
            CampaignPreGame.CampaignStanding s = CampaignPreGame.Standing(Army(7), Army(7));

            Assert.That(s.IsOver, Is.True);
            Assert.That(s.Outcome, Is.EqualTo(ECampaignOutcome.Draw));
        }

        [Test]
        public void BothCrossingUnlevelGoesToTheHigherTotal()
        {
            Assert.That(CampaignPreGame.Standing(Army(9), Army(7)).Outcome,
                Is.EqualTo(ECampaignOutcome.YouWon));
            Assert.That(CampaignPreGame.Standing(Army(7), Army(9)).Outcome,
                Is.EqualTo(ECampaignOutcome.RivalWon));
        }

        [Test]
        public void EndlessNeverEnds()
        {
            CampaignPreGame.CampaignStanding s =
                CampaignPreGame.Standing(Army(999, ECampaignType.Endless), Army(999, ECampaignType.Endless));

            Assert.That(s.IsOver, Is.False);
            Assert.That(s.HasTarget, Is.False, "Endless has no target even when a number is stored.");
            Assert.That(s.Describe(), Is.EqualTo("Endless"));
        }

        /// <summary>
        /// The regression guard. A campaign file written before <see cref="StrikeTeamInfo.VpTarget"/>
        /// existed deserializes with it at 0. If a zero target counted as a target, every one of those
        /// campaigns would report itself won the first time its screen was opened.
        /// </summary>
        [Test]
        public void AnOldCampaignWithNoTargetIsNotInstantlyWon()
        {
            CampaignPreGame.CampaignStanding s =
                CampaignPreGame.Standing(Army(2, ECampaignType.PointLimited, target: 0), Army(0, target: 0));

            Assert.That(s.HasTarget, Is.False);
            Assert.That(s.IsOver, Is.False);
            Assert.That(s.Describe(), Is.EqualTo("Endless"),
                "with no target there is nothing to race to, so it reads as the type it behaves as.");
        }

        [Test]
        public void ZeroVpOnBothSidesIsNotADraw()
        {
            // A brand new campaign: nobody has played a mission. Both on 0 against a target of 7.
            CampaignPreGame.CampaignStanding s =
                CampaignPreGame.Standing(Army(0, matches: 0), Army(0, matches: 0));

            Assert.That(s.IsOver, Is.False, "a campaign must not be a draw before it is played.");
            Assert.That(s.Outcome, Is.EqualTo(ECampaignOutcome.InProgress));
        }

        /// <summary>
        /// A campaign started in a folder with only one army has no standing enemy (#428). The rival
        /// sits on nothing, so only the player can reach the target - which is right, because there is
        /// nobody else scoring.
        /// </summary>
        [Test]
        public void ACampaignWithNoRivalStillRacesToTheTarget()
        {
            Assert.That(CampaignPreGame.Standing(Army(3), null).IsOver, Is.False);

            CampaignPreGame.CampaignStanding won = CampaignPreGame.Standing(Army(7), null);
            Assert.That(won.IsOver, Is.True);
            Assert.That(won.Outcome, Is.EqualTo(ECampaignOutcome.YouWon));
            Assert.That(won.RivalVp, Is.Zero);
        }

        /// <summary>
        /// The two types the spec does not offer must keep OPENING rather than ending on a limit nothing
        /// ever set. Standing passes a mission limit of 0 for exactly this reason.
        /// </summary>
        [Test]
        public void TheTypesWeDoNotOfferDoNotEndACampaign()
        {
            foreach (ECampaignType type in new[] { ECampaignType.GameLimited, ECampaignType.TimeLimited })
            {
                CampaignPreGame.CampaignStanding s = CampaignPreGame.Standing(
                    Army(50, type, target: 7, matches: 99), Army(50, type, target: 7, matches: 99));

                Assert.That(s.IsOver, Is.False, $"{type} is not offered and must not decide a campaign.");
                Assert.That(s.HasTarget, Is.False, $"{type} is not a VP race.");
            }
        }

        /// <summary>
        /// <see cref="CampaignRules.Start"/> is the only way a campaign is created, so the target has to
        /// survive the trip through it - and both armies of a campaign must come out carrying the same
        /// one. This goes through the real entry point rather than constructing the block by hand,
        /// because a test that builds the state itself cannot tell whether creation ever sets it.
        /// </summary>
        [Test]
        public void StartStoresTheTargetOnBothArmies()
        {
            CampaignNumbers n = CampaignNumbers.For(EGameSystem.GrimdarkFuture);

            var mine = new SaveLoad.ArmyListFile { Name = "Mine" };
            var theirs = new SaveLoad.ArmyListFile { Name = "Theirs" };

            CampaignRules.Start(mine, "Iron Reavers", "mine.fdgarmy", n, EGameSystem.GrimdarkFuture,
                ECampaignType.PointLimited, noPermadeath: false, isBot: false, vpTarget: 6);
            CampaignRules.Start(theirs, "The Hollow Choir", "theirs.fdgarmy", n, EGameSystem.GrimdarkFuture,
                ECampaignType.PointLimited, noPermadeath: false, isBot: true, vpTarget: 6);

            Assert.That(mine.StrikeTeam!.VpTarget, Is.EqualTo(6));
            Assert.That(theirs.StrikeTeam!.VpTarget, Is.EqualTo(6),
                "both files must agree - whichever one a reader opens has to give the same answer.");
            Assert.That(mine.StrikeTeam.CampaignType, Is.EqualTo(ECampaignType.PointLimited));

            Assert.That(CampaignPreGame.Standing(mine.StrikeTeam, theirs.StrikeTeam).Describe(),
                Is.EqualTo("First to 6 VP"));
        }

        [Test]
        public void StartRefusesANegativeTarget()
        {
            CampaignNumbers n = CampaignNumbers.For(EGameSystem.GrimdarkFuture);
            var army = new SaveLoad.ArmyListFile { Name = "Mine" };

            CampaignRules.Start(army, "Iron Reavers", "mine.fdgarmy", n, EGameSystem.GrimdarkFuture,
                ECampaignType.PointLimited, vpTarget: -5);

            Assert.That(army.StrikeTeam!.VpTarget, Is.Zero,
                "a negative target would be cleared by any VP total and end the campaign at once.");
        }

        /// <summary>
        /// The book's own way of setting the target, offered beside the field as a button. D3+4 is 5-7
        /// and must never fall outside that however the die lands.
        /// </summary>
        [Test]
        public void TheRolledTargetIsAlwaysFiveToSeven()
        {
            for (int face = 1; face <= 6; face++)
            {
                int rolled = CampaignPreGame.RollCampaignLength(() => face);
                Assert.That(rolled, Is.InRange(5, 7), $"a {face} rolled {rolled}.");
            }
        }
    }
}
