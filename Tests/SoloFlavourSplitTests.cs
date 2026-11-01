using FDG.Ai;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the three solo-rules flavours are genuinely different AIs, and the baseline is frozen.
    ///
    /// <para>
    /// <b>Why this fixture exists.</b> Plan D1 (docs/ai-agent-plan.md, signed off 2026-07-09) says the
    /// solo-rules bot "is never modified behaviorally - it is the permanent benchmark baseline", and every
    /// Tactician and Strategist gate was measured against it. On 2026-10-31 I changed its target priority
    /// and its activation order while implementing the Solo &amp; Co-Op book, which silently invalidated all
    /// of those numbers; the fix was to split the book's AI out into <see cref="EAiProfile.SoloCoOp"/> and
    /// put the baseline back.
    /// </para>
    /// <para>
    /// Nothing stopped that happening, which is the real problem. These tests are the thing that would
    /// have: they assert the baseline's behaviour directly, so the next person to improve "the solo bot"
    /// finds out at once that there are two of them and only one is theirs to change.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloFlavourSplitTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _ai;
        private PlayerID _player;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _player = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _store.Create(new TeamData(1, new List<PlayerID> { _player }));
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
        }

        private DataBinding<UnitData> Unit(PlayerID owner, string name, float x, float z)
        {
            var weapons = new List<Weapon> { new Weapon("Rifle", 24f, 2, 0), new Weapon("CCW", 0f, 2, 0) };
            var model = new ModelData(0.5f, weapons, new Position(x, z), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }

        private ChooseUnitToActivateRequest ActivationRequest(params DataBinding<UnitData>[] units)
        {
            var options = units
                .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
                .ToList();
            return new ChooseUnitToActivateRequest(_ai, options,
                Array.Empty<SelectionRequest<UnitData>.InvalidOption>());
        }

        private async Task<string> ActivateWith(ESoloFlavour flavour, ChooseUnitToActivateRequest request)
        {
            IStageResolverRegistry registry = AiResolverRegistryFactory.BuildSoloRules(
                _context.TableState, _ai, seed: 12345, slotID: 0, flavour);
            DataBinding<UnitData> pick = await registry
                .ResolveRequest<ChooseUnitToActivateRequest, DataBinding<UnitData>>(request);
            return pick.GetValue().Name;
        }

        [Test]
        public async Task TheBaselineStillTakesTheFirstOptionOffered()
        {
            // THE pin on plan D1. The baseline's activation order is "whatever the engine listed first",
            // which in practice is army build order. It is not the book's rule and it is not good play -
            // it is the fixed yardstick, and that is the whole point.
            DataBinding<UnitData> first = Unit(_ai, "First", 4f, 24f);
            DataBinding<UnitData> second = Unit(_ai, "Second", 24f, 24f);
            DataBinding<UnitData> third = Unit(_ai, "Third", 44f, 24f);

            string pick = await ActivateWith(ESoloFlavour.Baseline,
                ActivationRequest(first, second, third));

            Assert.That(pick, Is.EqualTo("First"));
        }

        [Test]
        public async Task TheBaselineIgnoresShakenUnitsSpecialTreatment()
        {
            // The book holds Shaken units back; the baseline does not, and must not start to.
            DataBinding<UnitData> shaken = Unit(_ai, "Shaken", 4f, 24f);
            DataBinding<UnitData> steady = Unit(_ai, "Steady", 44f, 24f);
            shaken.GetValue().Tokens.AddToken(
                new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            string pick = await ActivateWith(ESoloFlavour.Baseline, ActivationRequest(shaken, steady));

            Assert.That(pick, Is.EqualTo("Shaken"), "first option offered, Shaken or not.");
        }

        [Test]
        public async Task TheOfficialBotHoldsShakenUnitsBack()
        {
            // The same table, the other flavour: the book's rule applies.
            DataBinding<UnitData> shaken = Unit(_ai, "Shaken", 4f, 24f);
            DataBinding<UnitData> steady = Unit(_ai, "Steady", 44f, 24f);
            shaken.GetValue().Tokens.AddToken(
                new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

            string pick = await ActivateWith(ESoloFlavour.SoloCoOp, ActivationRequest(shaken, steady));

            Assert.That(pick, Is.EqualTo("Steady"));
        }

        [Test]
        public async Task TheTwoFlavoursDisagree_WhichIsTheWholePointOfSplittingThem()
        {
            // A deliberately blunt test: if these two ever agree on every table, one of them has been
            // quietly turned into the other and the benchmark baseline is gone again.
            var units = new List<DataBinding<UnitData>>();
            for (int i = 0; i < 6; i++) units.Add(Unit(_ai, $"U{i}", 4f + i * 8f, 24f));

            var disagreements = 0;
            for (int rotation = 0; rotation < 6; rotation++)
            {
                var rotated = units.Skip(rotation).Concat(units.Take(rotation)).ToArray();
                string baseline = await ActivateWith(ESoloFlavour.Baseline, ActivationRequest(rotated));
                string official = await ActivateWith(ESoloFlavour.SoloCoOp, ActivationRequest(rotated));
                if (baseline != official) disagreements++;
            }

            Assert.That(disagreements, Is.GreaterThan(0),
                "the frozen baseline and the official OPR bot must not be the same AI.");
        }

        [Test]
        public void EveryProfileBuildsARegistry()
        {
            // Including the ones the lobby no longer offers: plan G4 keeps them selectable behind
            // --ai-profile forever, so "hidden from the menu" must not rot into "throws when asked for".
            foreach (EAiProfile profile in Enum.GetValues<EAiProfile>())
            {
                IStageResolverRegistry registry = AiProfileFactory.BuildRegistry(profile,
                    _context.TableState, _ai, seed: 7, slotID: 0);
                Assert.That(registry, Is.Not.Null, $"{profile} must still build.");
            }
        }

        [Test]
        public void TheOfficialProfileIsNotTheBaselineProfile()
        {
            Assert.That(EAiProfile.SoloCoOp, Is.Not.EqualTo(EAiProfile.SoloRules),
                "two names, two AIs - the lobby offers the first and the benchmarks pin the second.");
        }
    }
}
