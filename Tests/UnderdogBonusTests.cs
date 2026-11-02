using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #434 - the campaign Underdog Bonus.
    ///
    /// <para>
    /// Campaign Rules v3.6.0, worded identically in all four books: <i>"Any army that has a point value
    /// lower than at least one other gets an underdog bonus. You get 1 underdog point for every 65pts
    /// difference between your army and the one with the highest value. Each underdog point can be spent
    /// to modify all dice in a friendly or enemy roll by +1 or -1, but only one point can be spent per
    /// roll."</i>
    /// </para>
    ///
    /// <para>
    /// The arithmetic (<see cref="CampaignPreGame.UnderdogPoints"/>) was built and tested with the rest
    /// of mission setup and is covered elsewhere. What is new, and what these tests pin, is everything
    /// that turns it into something a player can actually use: the per-mission pool, the offer at a roll,
    /// the one-point-per-roll cap, the direction of the modifier, and Kai's house rule that a bot never
    /// gets any.
    /// </para>
    /// </summary>
    [TestFixture]
    public class UnderdogBonusTests
    {
        private const int HostSlot = 0;
        private const int BotSlot = 1;

        /// <summary>Answers each selection prompt from a queue, and records every question it was asked.</summary>
        private sealed class ScriptedRequester : IPlayerRequestByID
        {
            private readonly Queue<bool> _spend;
            public ScriptedRequester(params bool[] spend) => _spend = new Queue<bool>(spend);

            public List<string> Prompts { get; } = new();
            public List<PlayerID> AskedOf { get; } = new();
            public List<IReadOnlyList<string>> OptionsOffered { get; } = new();

            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply>
            {
                if (request is StringSelectionRequest selection)
                {
                    Prompts.Add(selection.Instructions);
                    AskedOf.Add(selection.TargetPlayerID);
                    OptionsOffered.Add(selection.ValidOptions.ToList());
                    bool spend = _spend.Count > 0 && _spend.Dequeue();
                    // Option 1 is the spend; option 0 is "Don't spend".
                    return Task.FromResult((TReply)(object)selection.ValidOptions[spend ? 1 : 0]);
                }

                throw new InvalidOperationException($"unexpected request: {request.GetType().Name}");
            }
        }

        // -- The pool --------------------------------------------------------------------------------

        /// <summary>
        /// The whole point of the feature: the smaller force gets points, the larger gets none. 1,000 vs
        /// 1,260 in Grimdark Future is 260 behind at 65 a point, so 4.
        /// </summary>
        [Test]
        public void TheSmallerForceGetsPoints_AndTheLargerGetsNone()
        {
            (TestGameContext ctx, PlayerID host, PlayerID bot) = Table();
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);

            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(4),
                "260 pts behind at 1 per 65 is 4 underdog points.");
            Assert.That(UnderdogPool.Remaining(ctx, bot), Is.EqualTo(0),
                "the largest force is never the underdog.");
        }

        /// <summary>
        /// Each book's own rate, and the pairing that makes the rule coherent: the widest LEGAL gap is
        /// the mission cap, and cap / rate is exactly 5 in all four books - so a legal mission can never
        /// hand out more than five underdog points. Pinned per system because a shared rate is exactly
        /// the mistake this codebase keeps making.
        /// </summary>
        [TestCase(EGameSystem.GrimdarkFuture, 325, 65)]
        [TestCase(EGameSystem.GFFirefight, 125, 25)]
        [TestCase(EGameSystem.AgeOfFantasy, 250, 50)]
        [TestCase(EGameSystem.AoFSkirmish, 100, 20)]
        public void AtTheWidestLegalGap_EveryBookGivesExactlyFive(EGameSystem system, int cap, int rate)
        {
            CampaignNumbers numbers = CampaignNumbers.For(system);
            Assert.That(numbers.UnderdogPer, Is.EqualTo(rate), $"{system}: 1 point per {rate} pts.");
            Assert.That(numbers.ArmyCapOverLowest, Is.EqualTo(cap), $"{system}: cap {cap}.");

            (TestGameContext ctx, PlayerID host, _) = Table();
            Seed(ctx, hostPoints: 1000, botPoints: 1000 + cap, system);

            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(5),
                $"{system}: the widest legal gap is {cap}, which at 1 per {rate} is exactly 5 points.");
        }

        /// <summary>Equal forces are nobody's underdog - the standard game's case, and the common one.</summary>
        [Test]
        public void EqualForces_GiveNobodyAnything()
        {
            (TestGameContext ctx, PlayerID host, PlayerID bot) = Table();
            Seed(ctx, hostPoints: 1000, botPoints: 1000, EGameSystem.GrimdarkFuture);

            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(0));
            Assert.That(UnderdogPool.Remaining(ctx, bot), Is.EqualTo(0));
            Assert.That(UnderdogPool.AnyRemaining(ctx), Is.False);
        }

        /// <summary>
        /// Kai's house rule, 2026-10-30: <b>underdog points go to the player only, never a bot.</b> The
        /// books give them to any army worth less than another, so this is a deliberate deviation - a bot
        /// cannot judge which roll is worth a point, and five invisible free +1s would make a mission
        /// harder in a way the player could neither see nor plan around.
        /// </summary>
        [Test]
        public void ABotIsNeverGivenPoints_EvenWhenItIsTheUnderdog()
        {
            (TestGameContext ctx, PlayerID host, PlayerID bot) = Table();
            Seed(ctx, hostPoints: 1500, botPoints: 1000, EGameSystem.GrimdarkFuture);

            Assert.That(UnderdogPool.Remaining(ctx, bot), Is.EqualTo(0),
                "the bot is 500 behind and gets nothing - the house rule, enforced at seeding.");
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(0),
                "and the player is the LARGER force here, so it gets nothing either.");
            Assert.That(UnderdogPool.AnyRemaining(ctx), Is.False,
                "so no roll in this mission offers a spend at all.");
        }

        /// <summary>A standard game never seeds a pool, so nothing anywhere offers a spend.</summary>
        [Test]
        public void WithNoCampaign_ThereIsNoPool()
        {
            (TestGameContext ctx, PlayerID host, _) = Table();

            Assert.That(UnderdogPool.AnyRemaining(ctx), Is.False);
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(0));
        }

        // -- Spending --------------------------------------------------------------------------------

        /// <summary>
        /// The underdog's own roll gets <b>+1</b>, and the point leaves the pool. The sign matters: every
        /// roll stage reads the returned delta as "positive makes the rolling unit's roll easier".
        /// </summary>
        [Test]
        public async Task OnItsOwnRoll_TheUnderdogGetsPlusOne()
        {
            (TestGameContext ctx, PlayerID host, _) = Table(new ScriptedRequester(true));
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(delta, Is.EqualTo(+1), "my roll, so the point helps me.");
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(3), "and one point is gone.");
        }

        /// <summary>
        /// An enemy's roll gets <b>-1</b> - the other half of <i>"a friendly or enemy roll by +1 or
        /// -1"</i>, and the half that is easy to leave unbuilt because the happy path never needs it.
        /// </summary>
        [Test]
        public async Task OnAnEnemyRoll_TheUnderdogGetsMinusOne()
        {
            (TestGameContext ctx, PlayerID host, PlayerID bot) = Table(new ScriptedRequester(true));
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit theirs = MakeUnit(ctx, bot, "Ork Boyz");

            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, theirs);

            Assert.That(delta, Is.EqualTo(-1), "their roll, so the point hurts them.");
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(3));
        }

        /// <summary>Declining costs nothing.</summary>
        [Test]
        public async Task Declining_SpendsNothing()
        {
            (TestGameContext ctx, PlayerID host, _) = Table(new ScriptedRequester(false));
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(delta, Is.Zero);
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(4), "all four still there.");
        }

        /// <summary>
        /// <i>"only one point can be spent per roll"</i>. Asked once and answered yes, a roll gets exactly
        /// one step even with four points in the pool - the cap belongs to the roll, not to the pool.
        /// </summary>
        [Test]
        public async Task OnlyOnePointPerRoll_EvenWithFourInThePool()
        {
            var requester = new ScriptedRequester(true, true, true, true);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(delta, Is.EqualTo(+1), "one step, never two.");
            Assert.That(UnderdogPool.Remaining(ctx, host), Is.EqualTo(3), "and exactly one point left the pool.");
            Assert.That(requester.Prompts, Has.Count.EqualTo(1),
                "and the player is asked once per roll, not once per point they hold.");
        }

        /// <summary>
        /// <b>The cap across SEATS, which is the case the two-army fixture cannot reach.</b> The book's
        /// "only one point can be spent per roll" is a cap on the roll, not on each player - so with two
        /// armies both holding points (a 3+ army campaign, which the books are written for: <i>"campaigns
        /// are played with two or more players"</i>), one roll must still take exactly one point in total.
        ///
        /// <para>
        /// Added after a sabotage check walked straight past the suite: removing the cap entirely and
        /// summing every seat's spend kept all 21 tests green, because in a one-player-plus-bot game only
        /// one seat ever holds points and the loop returns after the first spend either way.
        /// </para>
        /// </summary>
        [Test]
        public async Task OnlyOnePointPerRoll_AcrossTwoSeatsThatBothHavePoints()
        {
            var requester = new ScriptedRequester(true, true);
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var small = new PlayerID(Guid.NewGuid());
            var middle = new PlayerID(Guid.NewGuid());
            var big = new PlayerID(Guid.NewGuid());
            store.Create(new PlayerSlotInfo(small, slotID: 0, teamNumber: 0, name: "Small", isFilled: true));
            store.Create(new PlayerSlotInfo(middle, slotID: 1, teamNumber: 1, name: "Middle", isFilled: true));
            store.Create(new PlayerSlotInfo(big, slotID: 2, teamNumber: 2, name: "Big", isFilled: true));
            var ctx = new TestGameContext(store, new FixedDiceRoller(4), playerRequester: requester);

            // Three real armies, none of them the bot's: the two smaller ones are both underdogs.
            UnderdogPool.Seed(ctx,
                new[]
                {
                    (0, new CampaignEntrant("Small", 1000, HasCaster: false), false),
                    (1, new CampaignEntrant("Middle", 1130, HasCaster: false), false),
                    (2, new CampaignEntrant("Big", 1260, HasCaster: false), false),
                },
                CampaignNumbers.For(EGameSystem.GrimdarkFuture));

            Assert.That(UnderdogPool.Remaining(ctx, small), Is.EqualTo(4), "260 behind at 65 a point.");
            Assert.That(UnderdogPool.Remaining(ctx, middle), Is.EqualTo(2), "130 behind at 65 a point.");

            IUnit theirs = MakeUnit(ctx, big, "Ork Boyz");
            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, theirs);

            Assert.That(delta, Is.EqualTo(-1), "one step, not two, however many seats wanted to spend.");
            int spent = (4 - UnderdogPool.Remaining(ctx, small)) + (2 - UnderdogPool.Remaining(ctx, middle));
            Assert.That(spent, Is.EqualTo(1), "and exactly one point left the table's pools in total.");
            Assert.That(requester.Prompts, Has.Count.EqualTo(1),
                "the second seat is not even asked once the roll's one point is spent.");
        }

        /// <summary>Spending across several rolls drains the pool, and the offers stop when it is empty.</summary>
        [Test]
        public async Task ThePoolRunsOut_AndThenNobodyIsAsked()
        {
            var requester = new ScriptedRequester(true, true, true, true, true);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            for (int i = 0; i < 4; i++)
            {
                Assert.That(await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine), Is.EqualTo(+1),
                    $"spend {i + 1} of 4");
            }

            Assert.That(UnderdogPool.Remaining(ctx, host), Is.Zero);
            int after = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(after, Is.Zero, "nothing left to spend.");
            Assert.That(requester.Prompts, Has.Count.EqualTo(4),
                "and the fifth roll does not ask - an empty pool must cost the player no clicks.");
        }

        /// <summary>
        /// With no pool, nothing is asked at all - a standard game must not gain a prompt per die.
        ///
        /// <para>
        /// Note what this does and does not pin. Two things stop the prompt: the early
        /// <c>AnyRemaining</c> guard, and the per-seat "no points left" check inside the loop. Removing
        /// the outer guard leaves this test green, because the inner check still refuses - so the guard
        /// is a <b>fast path</b>, not the correctness, and the comment on it says so. Found by a sabotage
        /// check that this suite walked past.
        /// </para>
        /// </summary>
        [Test]
        public async Task WithNoPool_NothingIsEverAsked()
        {
            var requester = new ScriptedRequester(true);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            int delta = await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(delta, Is.Zero);
            Assert.That(requester.Prompts, Is.Empty);
        }

        /// <summary>
        /// The offer goes to the seat holding the points, never to the roll's owner. Worth pinning
        /// because the natural way to write this - ask the rolling unit's owner - is wrong half the time,
        /// and would silently drop the "enemy roll" half of the rule.
        /// </summary>
        [Test]
        public async Task TheOfferGoesToTheUnderdog_NotToTheRollsOwner()
        {
            var requester = new ScriptedRequester(true);
            (TestGameContext ctx, PlayerID host, PlayerID bot) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit theirs = MakeUnit(ctx, bot, "Ork Boyz");

            await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, theirs);

            Assert.That(requester.AskedOf, Has.Count.EqualTo(1));
            Assert.That(requester.AskedOf[0], Is.EqualTo(host),
                "the enemy is rolling, and it is the underdog who decides whether to spend.");
        }

        /// <summary>
        /// "Don't spend" is the FIRST option, so every automated path - the CLI's EOF default, the AI
        /// fallback, an unattended run - keeps the points. The opposite of TargetMarkerSpend's
        /// spend-all-first ordering, and deliberately: a marker is use-it-or-lose-it, an underdog point
        /// is a scarce decision worth keeping. Without this, a headless sweep burns all five points on
        /// the first five rolls of every mission.
        /// </summary>
        [Test]
        public async Task NotSpendingIsTheDefault_SoAnUnattendedRunKeepsThePoints()
        {
            var requester = new ScriptedRequester(true);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            Assert.That(requester.OptionsOffered[0][0], Does.StartWith("Don't spend"),
                "first option is the safe one, because that is what every default picks.");
        }

        /// <summary>Every roll kind names itself in the prompt, so the player knows what they are buying.</summary>
        [TestCase(ERollKind.Hit, "hit roll")]
        [TestCase(ERollKind.Save, "save roll")]
        [TestCase(ERollKind.Morale, "morale test")]
        [TestCase(ERollKind.Cast, "casting roll")]
        public async Task ThePromptSaysWhichRollItIs(ERollKind roll, string expected)
        {
            var requester = new ScriptedRequester(false);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            await UnderdogSpend.ConsumeNet(ctx, roll, mine);

            Assert.That(requester.Prompts[0], Does.Contain(expected));
        }

        /// <summary>
        /// Game text is ASCII-only - the ImGui font atlas bakes Basic Latin plus Latin-1, so anything
        /// above U+00FF renders as a question mark in game (CLAUDE.md).
        /// </summary>
        [Test]
        public async Task ThePromptAndItsOptionsAreAscii()
        {
            var requester = new ScriptedRequester(false);
            (TestGameContext ctx, PlayerID host, _) = Table(requester);
            Seed(ctx, hostPoints: 1000, botPoints: 1260, EGameSystem.GrimdarkFuture);
            IUnit mine = MakeUnit(ctx, host, "Riflemen");

            await UnderdogSpend.ConsumeNet(ctx, ERollKind.Hit, mine);

            foreach (string text in requester.Prompts.Concat(requester.OptionsOffered[0]))
            {
                Assert.That(text, Is.EqualTo(new string(text.Where(c => c <= 0x7F).ToArray())),
                    $"non-ASCII renders as '?' in game: {text}");
            }
        }

        // -- Helpers ---------------------------------------------------------------------------------

        private static (TestGameContext Context, PlayerID Host, PlayerID Bot) Table(
            IPlayerRequestByID? requester = null)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var host = new PlayerID(Guid.NewGuid());
            var bot = new PlayerID(Guid.NewGuid());
            store.Create(new PlayerSlotInfo(host, slotID: HostSlot, teamNumber: 0, name: "Host", isFilled: true));
            store.Create(new PlayerSlotInfo(bot, slotID: BotSlot, teamNumber: 1, name: "Bot", isFilled: true));

            var context = new TestGameContext(store, new FixedDiceRoller(4),
                playerRequester: requester ?? new NoRequester());
            return (context, host, bot);
        }

        /// <summary>
        /// Seeds the pool the way the campaign's mission setup will: both forces, with the bot's seat
        /// flagged so the house rule applies.
        /// </summary>
        private static void Seed(TestGameContext ctx, int hostPoints, int botPoints, EGameSystem system) =>
            UnderdogPool.Seed(ctx,
                new[]
                {
                    (HostSlot, new CampaignEntrant("Iron Reavers", hostPoints, HasCaster: false), false),
                    (BotSlot, new CampaignEntrant("The Hollow Choir", botPoints, HasCaster: false), true),
                },
                CampaignNumbers.For(system));

        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply> =>
                throw new InvalidOperationException(
                    "no prompt is expected on this path: " + request.GetType().Name);
        }

        private static IUnit MakeUnit(TestGameContext ctx, PlayerID owner, string name)
        {
            var store = (GameDataStore)ctx.GameDataStore;
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), store);
                models.Add(store.GetDataBinding<ModelData>(store.Create(model)));
            }

            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: models);
            return store.GetDataBinding<UnitData>(store.Create(unit)).GetValue();
        }
    }
}
