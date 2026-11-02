using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Players;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #441 - the battle-report lines when a Banner or Musician leaves the table (Kai, 2026-11-01).
    ///
    /// <para>
    /// Three messages, and which one appears depends on what actually happened rather than on the game
    /// system. Kai described the long form for "Firefight/Skirmish" and the short form for "GF/AoF", but
    /// the long form belongs to any game where units were really picked - which is <b>four</b> systems,
    /// Star Quest and AoF: Quest included (#439). So the code asks "did anything lose a buff", and these
    /// tests pin that BEHAVIOUR rather than a list of system names.
    /// </para>
    ///
    /// <para>
    /// Each line also carries the rule's name as a referenced rule, so the report can highlight it and
    /// hover OPR's own wording. That the name is carried is asserted here; what the hover SAYS is the
    /// front end's business and comes from <c>OfficialRuleText</c> (the book text, per system).
    /// </para>
    /// </summary>
    [TestFixture]
    public class CommandGroupObituaryTests
    {
        private GameDataStore _store = null!;
        private RuleResolver _resolver = null!;
        private PlayerID _us;

        /// <summary>Banner as #438 gates it - the combat-only morale bonus.</summary>
        private static SpecialRuleDefinition BannerDefinition() => new("Banner",
            new[]
            {
                new HookEntry(EHookID.Morale_OnPreMoraleTest,
                    new Condition.MoraleTriggeredByCombat(),
                    new Effect.RollModifier(ERollKind.Morale, Delta: +1),
                    ELifetime.ThisActivation),
            },
            Array.Empty<ActivatedAbility>());

        /// <summary>
        /// Collects the beats the obituary presents - which is the seam the battle report reads from, so
        /// asserting here is asserting what the player will actually see listed.
        /// </summary>
        private sealed class BeatCollector : IPresentationSink
        {
            public readonly List<BannerBeat> Banners = new();

            public void OnBeat(PresentationBeat beat)
            {
                if (beat is BannerBeat banner)
                {
                    Banners.Add(banner);
                }
            }
        }

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _resolver = CoreRuleCatalog.CreateResolver();
            _resolver.Register(BannerDefinition());
            _us = new PlayerID(Guid.NewGuid());
        }

        // -- A bearer dies, and units were relying on it -----------------------------------------------

        [Test]
        public async Task ABearerDies_AndTheLineNamesTheUnitsThatLoseIt()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> first = MakeUnit("Archers");
            DataBinding<UnitData> second = MakeUnit("Militia");
            PickCommandGroupUnitsStage.Grant(first, "Banner", bearer.GetValue());
            PickCommandGroupUnitsStage.Grant(second, "Banner", bearer.GetValue());

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo(
                "Spearmen with Banner died! Archers and Militia lose benefits from Banner!"));
        }

        [Test]
        public async Task OneCoveredUnit_ReadsAsSingular()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> only = MakeUnit("Archers");
            PickCommandGroupUnitsStage.Grant(only, "Banner", bearer.GetValue());

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo(
                    "Spearmen with Banner died! Archers loses benefits from Banner!"),
                "one unit \"loses\", two \"lose\" - a person reads this line.");
        }

        /// <summary>
        /// The short form: what Grimdark Future and Age of Fantasy always produce (their Banner reads
        /// "this model AND ITS UNIT", so there are no picks to lose), and what the other four produce
        /// when the player picked nobody.
        /// </summary>
        [Test]
        public async Task ABearerWithNoPicks_JustAnnouncesItsOwnDeath()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            MakeUnit("Archers");   // on the table, but never picked

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo("Spearmen with Banner died!"));
            Assert.That(Texts(sink), Has.None.Contains("lose"),
                "nothing was relying on it, so there is nothing to report losing - and an unpicked unit "
                + "standing nearby must not be swept into the line.");
        }

        /// <summary>
        /// A unit covered by somebody ELSE's banner is not covered by this one. Without the
        /// <c>OwnerUnitID</c> match the line would name it anyway and tell the player a lie about which
        /// buffs just went away.
        /// </summary>
        [Test]
        public async Task TheLine_NamesOnlyThisBearersPicks()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> dyingBearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> otherBearer = MakeUnit("Honour Guard", attachBanner: true);
            DataBinding<UnitData> ours = MakeUnit("Archers");
            DataBinding<UnitData> theirs = MakeUnit("Militia");
            PickCommandGroupUnitsStage.Grant(ours, "Banner", dyingBearer.GetValue());
            PickCommandGroupUnitsStage.Grant(theirs, "Banner", otherBearer.GetValue());

            KillEveryModel(dyingBearer);
            await CommandGroupObituary.Announce(ctx, dyingBearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo(
                "Spearmen with Banner died! Archers loses benefits from Banner!"));
            Assert.That(Texts(sink), Has.None.Contains("Militia"),
                "Militia is covered by a banner that is still standing.");
        }

        /// <summary>
        /// A picked unit that is already dead has nothing left to lose, so naming it would be noise -
        /// and the books' own wording is about "picked models THAT ARE DEPLOYED".
        /// </summary>
        [Test]
        public async Task AnAlreadyDeadPick_IsNotNamed()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> alive = MakeUnit("Archers");
            DataBinding<UnitData> gone = MakeUnit("Militia");
            PickCommandGroupUnitsStage.Grant(alive, "Banner", bearer.GetValue());
            PickCommandGroupUnitsStage.Grant(gone, "Banner", bearer.GetValue());

            KillEveryModel(gone);
            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo(
                "Spearmen with Banner died! Archers loses benefits from Banner!"));
        }

        // -- A covered unit dies -----------------------------------------------------------------------

        [Test]
        public async Task ACoveredUnitDies_AndSaysWhatItWasReceiving()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> covered = MakeUnit("Archers");
            PickCommandGroupUnitsStage.Grant(covered, "Banner", bearer.GetValue());

            KillEveryModel(covered);
            await CommandGroupObituary.Announce(ctx, covered.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo("Archers receiving Banner died!"));
        }

        /// <summary>An ordinary unit's death says nothing about Command Groups at all.</summary>
        [Test]
        public async Task AnOrdinaryUnitDies_AndNothingIsAnnounced()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bystander = MakeUnit("Scouts");

            KillEveryModel(bystander);
            await CommandGroupObituary.Announce(ctx, bystander.GetValue());

            Assert.That(sink.Banners, Is.Empty);
        }

        /// <summary>
        /// Musician is the other conferring upgrade and gets the same treatment, by name. Pinned because
        /// the obituary walks <c>ConferringCommandGroupRules</c> - hard-coding "Banner" anywhere in it
        /// would pass every test above and leave half the feature missing.
        /// </summary>
        [Test]
        public async Task Musician_GetsTheSameLines()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Drummers");
            bearer.GetValue().AttachRuleDefinition(
                new ResolvedRule("Musician", new SpecialRuleDefinition("Musician",
                    Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>())));
            DataBinding<UnitData> covered = MakeUnit("Archers");
            PickCommandGroupUnitsStage.Grant(covered, "Musician", bearer.GetValue());

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(Texts(sink), Has.One.EqualTo(
                "Drummers with Musician died! Archers loses benefits from Musician!"));
        }

        // -- The rule name rides along for the hover ---------------------------------------------------

        [Test]
        public async Task EveryLine_CarriesTheRuleNameForTheHover()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> covered = MakeUnit("Archers");
            PickCommandGroupUnitsStage.Grant(covered, "Banner", bearer.GetValue());

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(sink.Banners, Is.Not.Empty);
            foreach (BannerBeat beat in sink.Banners)
            {
                Assert.That(beat.ReferencedRules, Is.EqualTo(new[] { "Banner" }),
                    "the front end finds the name in the text by substring, so it must be spelled "
                    + "exactly as the line spells it.");
                Assert.That(beat.BannerText, Does.Contain("Banner"),
                    "a referenced rule is a promise that the name occurs in the text.");
            }
        }

        /// <summary>
        /// A Toast, not a Headline: a banner going down belongs in the report and is worth seeing, but
        /// it must not stop play - <see cref="EBannerTier.Headline"/> would pause the game on every
        /// banner casualty.
        /// </summary>
        [Test]
        public async Task TheLine_DoesNotStopTheGame()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(sink.Banners.Select(b => b.Tier), Is.All.EqualTo(EBannerTier.Toast));
        }

        /// <summary>
        /// The obituary must read the grants BEFORE the sweep that removes them -
        /// <see cref="UnitDestructionNotifier"/> calls it first for exactly that reason. Reorder the two
        /// and every long line silently degrades to the short one, naming nobody; nothing else in the
        /// suite would notice. So this goes through the real production seam rather than calling
        /// <c>Announce</c> directly.
        /// </summary>
        [Test]
        public async Task TheWholeMessageDependsOnRunningBeforeTheTokenSweep()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            DataBinding<UnitData> covered = MakeUnit("Archers");
            PickCommandGroupUnitsStage.Grant(covered, "Banner", bearer.GetValue());
            KillEveryModel(bearer);

            await UnitDestructionNotifier.NotifyUnitDestroyed(ctx, bearer.GetValue(), killer: null);

            Assert.That(Texts(sink), Has.One.EqualTo(
                    "Spearmen with Banner died! Archers loses benefits from Banner!"),
                "NotifyUnitDestroyed must announce before it sweeps the grants.");
            Assert.That(PickCommandGroupUnitsStage.HoldsGrantFor(covered.GetValue(), "Banner"), Is.False,
                "and the sweep still happened - the message is not standing in for the revocation.");
        }

        /// <summary>
        /// The log gets the same text, plain. The battle report reads the beats, but the scrolling log is
        /// where a player looks for "what just happened", and a line that only exists as a beat would be
        /// missing from any transcript.
        /// </summary>
        [Test]
        public async Task TheSameLine_AlsoGoesToTheLog()
        {
            var log = new CapturingTextOutput();
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink, log);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(log.Lines, Has.One.EqualTo("Spearmen with Banner died!"));
        }

        /// <summary>
        /// Game text is ASCII-only - the ImGui font atlas bakes Basic Latin + Latin-1, so anything above
        /// U+00FF renders as a question mark in-game (CLAUDE.md). Unit names come from book data, which
        /// is folded on import, so what is checked here is the obituary's own joining punctuation.
        /// </summary>
        [Test]
        public async Task TheLines_AreAscii()
        {
            var sink = new BeatCollector();
            TestGameContext ctx = Ctx(sink);
            DataBinding<UnitData> bearer = MakeUnit("Spearmen", attachBanner: true);
            PickCommandGroupUnitsStage.Grant(MakeUnit("Archers"), "Banner", bearer.GetValue());
            PickCommandGroupUnitsStage.Grant(MakeUnit("Militia"), "Banner", bearer.GetValue());
            PickCommandGroupUnitsStage.Grant(MakeUnit("Scouts"), "Banner", bearer.GetValue());

            KillEveryModel(bearer);
            await CommandGroupObituary.Announce(ctx, bearer.GetValue());

            Assert.That(sink.Banners, Is.Not.Empty);
            foreach (BannerBeat beat in sink.Banners)
            {
                Assert.That(beat.BannerText, Is.EqualTo(Ascii(beat.BannerText)),
                    $"non-ASCII renders as '?' in game: {beat.BannerText}");
            }
        }

        // -- Helpers -----------------------------------------------------------------------------------

        private static string Ascii(string text) =>
            new string(text.Where(c => c <= 0x7F).ToArray());

        private static List<string> Texts(BeatCollector sink) =>
            sink.Banners.Select(b => b.BannerText).ToList();

        private TestGameContext Ctx(BeatCollector sink, ITextOutput? log = null) =>
            new(_store, new FixedDiceRoller(4), textOutput: log,
                presenter: new LocalPresenter(sink, new InstantPresentationClock()),
                ruleResolver: _resolver, playerRequester: new NoRequester());

        private sealed class CapturingTextOutput : ITextOutput
        {
            public readonly List<string> Lines = new();

            public void Log(string message, TextColor? color = null) => Lines.Add(message);

            public void LogDebug(string message, TextColor? color = null) { }
        }

        /// <summary>
        /// An obituary asks nobody anything. Throwing rather than answering means a future change that
        /// starts prompting from this path fails loudly instead of taking a default.
        /// </summary>
        private sealed class NoRequester : IPlayerRequestByID
        {
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : IStageTaskRequest<TReply> =>
                throw new InvalidOperationException(
                    "no prompt is expected on this path: " + request.GetType().Name);
        }

        private static void KillEveryModel(DataBinding<UnitData> unit)
        {
            foreach (DataBinding<ModelData> model in unit.GetValue().ModelBindings)
            {
                model.GetValue().DealWounds(model.GetValue().TotalWounds);
            }
        }

        private DataBinding<UnitData> MakeUnit(string name, bool attachBanner = false)
        {
            var models = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f + i, 10f), _store);
                models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_us, name, quality: 4, defense: 4, modelBindings: models);
            if (attachBanner)
            {
                unit.AttachRuleDefinition(new ResolvedRule("Banner", BannerDefinition()));
            }

            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
