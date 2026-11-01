using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - WHICH Heroes lend their Quality for Morale Tests is a per-system question, and the engine
    /// was answering it with Grimdark Future's rule in all six systems.
    ///
    /// <para>
    /// <b>Grimdark Future and Age of Fantasy:</b> "The Hero's unit, and units within 6" of friendly Heroes
    /// <b>with Tough(9) or higher</b>, may use the Hero's Quality for Morale Tests as long as the Hero
    /// isn't Shaken." A big-monster aura - Tough(9) is well past what any joining Hero carries.
    /// </para>
    /// <para>
    /// <b>Firefight, Skirmish and both Quest systems:</b> "Units within 6" of friendly Heroes may use the
    /// Hero's Quality for Morale Tests as long as the Hero isn't Shaken." <b>No threshold at all.</b>
    /// </para>
    /// <para>
    /// Running the Grimdark Future gate in those four withheld the aura from essentially every Hero they
    /// have, because Tough(9) is a vehicle-scale number in a skirmish game. A Star Quest party standing
    /// beside its own Hero tested morale on its own Quality for the whole game - and morale is tested
    /// constantly, so this was wrong in most activations of most games in four of the six systems.
    /// </para>
    /// <para>
    /// The <see cref="Opr360RuleChangeTests"/> aura cases stay as they are: they run under the default
    /// Grimdark Future settings and pin the threshold reading, which is still correct there.
    /// </para>
    /// </summary>
    [TestFixture]
    public class HeroMoraleAuraPerSystemTests
    {
        private GameDataStore _store = null!;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _player = new PlayerID(Guid.NewGuid());
        }

        /// <summary>The four systems whose books print the aura with no Tough threshold.</summary>
        private static readonly EGameSystem[] NoThreshold =
        {
            EGameSystem.GFFirefight, EGameSystem.AoFSkirmish,
            EGameSystem.GFStarQuest, EGameSystem.AoFQuest,
        };

        /// <summary>And the two that gate it on Tough(9)+.</summary>
        private static readonly EGameSystem[] Thresholded =
        {
            EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy,
        };

        [TestCaseSource(nameof(NoThreshold))]
        public void WithNoThreshold_AnOrdinaryHeroLendsItsQuality(EGameSystem system)
        {
            TestGameContext ctx = Context(system);
            UnitData grunts = MakeUnit(ctx, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(ctx, quality: 2, at: new Position(4, 0), tough: 3);

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, ctx.TableState), Is.EqualTo(2),
                $"{system}: its book says \"units within 6\" of friendly Heroes may use the Hero's "
                + "Quality\" with no Tough threshold, so an ordinary Tough(3) Hero lends its Quality 2.");
        }

        [TestCaseSource(nameof(Thresholded))]
        public void WithTheThreshold_AnOrdinaryHeroLendsNothing(EGameSystem system)
        {
            TestGameContext ctx = Context(system);
            UnitData grunts = MakeUnit(ctx, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(ctx, quality: 2, at: new Position(4, 0), tough: 3);

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, ctx.TableState), Is.EqualTo(5),
                $"{system}: the aura is gated on Tough(9) or higher, so a Tough(3) Hero projects "
                + "nothing to a unit it has not joined.");
        }

        /// <summary>
        /// The discriminating pair, stated as one assertion so it cannot drift apart: the SAME Hero and
        /// the SAME unit, at the same distance, must answer differently in the two families. If this ever
        /// stops being true it should be because a BOOK changed, not because one system picked up the
        /// other's reading.
        /// </summary>
        [Test]
        public void TheTwoFamiliesDisagree_OnTheSameHero()
        {
            TestGameContext gf = Context(EGameSystem.GrimdarkFuture);
            UnitData gfGrunts = MakeUnit(gf, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(gf, quality: 2, at: new Position(4, 0), tough: 3);

            TestGameContext ff = Context(EGameSystem.GFFirefight);
            UnitData ffGrunts = MakeUnit(ff, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(ff, quality: 2, at: new Position(4, 0), tough: 3);

            Assert.Multiple(() =>
            {
                Assert.That(HeroStatRules.GetMoraleQuality(gfGrunts, gf.TableState), Is.EqualTo(5));
                Assert.That(HeroStatRules.GetMoraleQuality(ffGrunts, ff.TableState), Is.EqualTo(2));
            });
        }

        /// <summary>
        /// A Tough(9)+ Hero still lends in the threshold-free systems - removing the gate must not have
        /// replaced it with a different one.
        /// </summary>
        [TestCaseSource(nameof(NoThreshold))]
        public void WithNoThreshold_ABigHeroStillLends(EGameSystem system)
        {
            TestGameContext ctx = Context(system);
            UnitData grunts = MakeUnit(ctx, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(ctx, quality: 2, at: new Position(4, 0), tough: 9);

            Assert.That(HeroStatRules.GetMoraleQuality(grunts, ctx.TableState), Is.EqualTo(2));
        }

        /// <summary>
        /// Everything the aura shares across systems is still gated. These would all pass with the rule
        /// simply deleted and "always use the best friendly Quality on the table" put in its place, so
        /// they are what keeps the threshold-free reading from becoming no reading at all.
        /// </summary>
        [TestCaseSource(nameof(NoThreshold))]
        public void WithNoThreshold_TheOtherClausesStillHold(EGameSystem system)
        {
            TestGameContext far = Context(system);
            UnitData farGrunts = MakeUnit(far, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeHero(far, quality: 2, at: new Position(10, 0), tough: 3);

            TestGameContext shaken = Context(system);
            UnitData shakenGrunts = MakeUnit(shaken, quality: 5, at: new Position(0, 0), modelCount: 3);
            UnitData hero = MakeHero(shaken, quality: 2, at: new Position(4, 0), tough: 3);
            hero.Tokens.AddToken(Rules.Tokens.TokenDefinitionCatalog.Create(TokenType.Shaken));

            TestGameContext notHero = Context(system);
            UnitData notHeroGrunts = MakeUnit(notHero, quality: 5, at: new Position(0, 0), modelCount: 3);
            MakeUnit(notHero, quality: 2, at: new Position(4, 0), modelCount: 1); // no Hero rule

            Assert.Multiple(() =>
            {
                Assert.That(HeroStatRules.GetMoraleQuality(farGrunts, far.TableState), Is.EqualTo(5),
                    "8.5\" is outside the 6\" aura in every system.");
                Assert.That(HeroStatRules.GetMoraleQuality(shakenGrunts, shaken.TableState), Is.EqualTo(5),
                    "\"as long as the Hero isn't Shaken\" survives the threshold change.");
                Assert.That(HeroStatRules.GetMoraleQuality(notHeroGrunts, notHero.TableState), Is.EqualTo(5),
                    "a high-Quality model that is not a Hero lends nothing - dropping the Tough gate "
                    + "must not drop the Hero gate with it.");
            });
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// A context with its OWN store. The store is where GameProgressData lives and
        /// <c>TryGetProgress</c> returns the first one it finds, so two systems sharing a store would
        /// both resolve to whichever was written first - which is how the two-family test below passed
        /// for the wrong reason until this was separated.
        /// </summary>
        private TestGameContext Context(EGameSystem system)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            if (system == EGameSystem.GFStarQuest) settings.GameMode = EGameMode.StarQuestMission;
            if (system == EGameSystem.AoFQuest) settings.GameMode = EGameMode.StarQuestMission;

            var ctx = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
            _store.Create(new TeamData(0, new List<PlayerID> { _player }));

            // The aura reads the system off ITableState.Progress, which resolves through the store's
            // GameProgressData rather than the context's settings - a real game always has one, so this
            // seeds the equivalent. Without it every case would silently run as Grimdark Future, which
            // is exactly the bug under test and would have made the no-threshold cases pass for the
            // wrong reason.
            _store.Create(new GameProgressData(EResumeStage.MainPhase, 1,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), settings));
            return ctx;
        }

        private UnitData MakeHero(TestGameContext ctx, int quality, Position at, int tough)
        {
            UnitData hero = MakeUnit(ctx, quality, at, modelCount: 1);
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            hero.AttachRuleDefinition(new ResolvedRule("Tough", CoreRuleCatalog.Tough,
                new RuleArgument[] { new RuleArgument.Int(tough) }));
            return hero;
        }

        private UnitData MakeUnit(TestGameContext ctx, int quality, Position at, int modelCount)
        {
            var bindings = new List<DataBinding<ModelData>>(modelCount);
            for (int i = 0; i < modelCount; i++)
            {
                var model = new ModelData(0.75f, new List<Weapon>(), at, _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_player, "Unit", quality: quality, defense: 4, modelBindings: bindings);
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }
    }
}
