using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Ai.Solo;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #423 - the Transport clause of <b>Solo &amp; Co-Op Rules v3.6.0</b>, which appears in the
    /// <b>Grimdark Future book only</b> (checked: Firefight, AoF and AoF: Skirmish have no Transport rule
    /// or no clause for it). Quoted in full, because it is three instructions in one paragraph:
    ///
    /// <para>
    /// "<b>Transport:</b> The AI must always place random units in each Transport, <b>trying to fill up
    /// their cargo limit</b>. AI units with Transport <b>always activate before their cargo on the first
    /// round</b>, and units inside of them <b>must always disembark on their first activation</b> (if
    /// possible)."
    /// </para>
    /// <para>
    /// One fixture for all three because they are one clause and they interact: cargo that never gets out
    /// is the reason the activation order matters, and both only matter because the AI loads its
    /// transports in the first place.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloAiTransportClauseTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _ai;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(0, new List<PlayerID> { _ai }));
            _context = new TestGameContext(_store, new FixedDiceRoller(4));
            // The round is per-test: TableState.Progress reads a GameProgressData out of the STORE, and
            // two of them in one store is an ambiguity rather than an override - the same trap that cost
            // an hour in #420's per-system tests. So SetUp writes none and every test states its round.
            SetRound(1);
        }

        private ITableState Table => _context.TableState;

        /// <summary>
        /// A context playing the given system, for the clause's GF-only scoping. The system is read off
        /// TableState.Progress, so it is the progress record that carries it.
        /// </summary>
        private TestGameContext ContextFor(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            GameProgressUtilities.WriteProgress(_store, new GameProgressData(EResumeStage.MainPhase, 1,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), settings));
            return new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        // ── "trying to fill up their cargo limit" ─────────────────────────────────────────────────────

        // Two transports, one already half loaded. The book wants the cargo concentrated, so the load goes
        // into the one with less room left rather than the first one offered - which is what the engine
        // did, and what spreads a force thinly across two half-empty hulls.
        [Test]
        public void TheFullestTransportWithRoom_TakesTheLoad()
        {
            DataBinding<UnitData> empty = Transport("Empty Rhino", capacity: 10);
            DataBinding<UnitData> halfFull = Transport("Loaded Rhino", capacity: 10);
            Occupy(halfFull, models: 6);

            DataBinding<UnitData> pick = Resolve(EmbarkOffer(empty, halfFull));

            Assert.That(pick.GetValue().Name, Is.EqualTo("Loaded Rhino"),
                "four spaces left beats ten: filling one transport up is the clause, and the empty one "
                + "was first in the list.");
        }

        // The same board with the options the other way round, so the pick is the capacities talking and
        // not list order.
        [Test]
        public void ListOrderDoesNotDecideIt()
        {
            DataBinding<UnitData> halfFull = Transport("Loaded Rhino", capacity: 10);
            Occupy(halfFull, models: 6);
            DataBinding<UnitData> empty = Transport("Empty Rhino", capacity: 10);

            DataBinding<UnitData> pick = Resolve(EmbarkOffer(halfFull, empty));

            Assert.That(pick.GetValue().Name, Is.EqualTo("Loaded Rhino"));
        }

        // ── "always activate before their cargo on the first round" ───────────────────────────────────

        [Test]
        public void OnRoundOne_TheTransportActivatesBeforeItsCargo()
        {
            DataBinding<UnitData> rhino = Transport("Rhino", capacity: 10, x: 10f);
            DataBinding<UnitData> cargo = Squad("Grunts", x: 10f);
            TransportUtilities.Embark(cargo.GetValue(), rhino.GetValue());

            // Every roll comes back 1, so the section walk is deterministic and the only thing that can
            // change the answer is the hold-back.
            var resolver = new SoloRulesActivationResolver(new Random(1), Table);

            DataBinding<UnitData> pick = resolver
                .Resolve(ActivationMenu(cargo, rhino)).GetAwaiter().GetResult();

            Assert.That(pick.GetValue().Name, Is.EqualTo("Rhino"),
                "the cargo waits for its ride on round 1, whichever way the section roll falls.");
        }

        // Once the ride has gone, the cargo is free - the clause is about order, not about banning the
        // cargo from the round.
        [Test]
        public void OnceTheTransportHasActivated_ItsCargoMayGo()
        {
            DataBinding<UnitData> rhino = Transport("Rhino", capacity: 10, x: 10f);
            DataBinding<UnitData> cargo = Squad("Grunts", x: 10f);
            TransportUtilities.Embark(cargo.GetValue(), rhino.GetValue());
            rhino.GetValue().Tokens.AddToken(
                TokenDefinitionCatalog.Create(TokenType.ActivatedThisRound));

            var resolver = new SoloRulesActivationResolver(new Random(1), Table);

            DataBinding<UnitData> pick = resolver
                .Resolve(ActivationMenu(cargo)).GetAwaiter().GetResult();

            Assert.That(pick.GetValue().Name, Is.EqualTo("Grunts"));
        }

        // "on the first round" is in the text, so round 2 is left alone. A unit that re-boarded later has
        // no clause about it, and the disembark clause means cargo is normally out by then anyway.
        [Test]
        public void AfterRoundOne_TheClauseDoesNotApply()
        {
            SetRound(2);
            DataBinding<UnitData> rhino = Transport("Rhino", capacity: 10, x: 10f);
            DataBinding<UnitData> cargo = Squad("Grunts", x: 10f);
            TransportUtilities.Embark(cargo.GetValue(), rhino.GetValue());

            var resolver = new SoloRulesActivationResolver(new Random(1), Table);

            // With the hold-back inactive the pick is whatever the rolls say; the assertion is only that
            // the cargo is not excluded, so both units must be reachable.
            var picks = new HashSet<string>();
            for (int seed = 0; seed < 25; seed++)
            {
                picks.Add(new SoloRulesActivationResolver(new Random(seed), Table)
                    .Resolve(ActivationMenu(cargo, rhino)).GetAwaiter().GetResult().GetValue().Name);
            }

            Assert.That(picks, Does.Contain("Grunts"),
                "past round 1 the cargo is an ordinary candidate again.");
        }

        // The clause is in the GRIMDARK FUTURE book only. Age of Fantasy has the Transport rule - its own
        // Tough caps and all - and no AI clause for it, so the activation order there stays as the rest of
        // the book sets it. (The disembark half is deliberately NOT gated: the alternative in AoF is cargo
        // that never gets out, which is a failure to act rather than a reading of silence. See
        // SoloCoOpActionResolver.)
        [Test]
        public void InAgeOfFantasy_TheActivationOrderClauseDoesNotApply()
        {
            var ctx = ContextFor(EGameSystem.AgeOfFantasy);
            DataBinding<UnitData> rhino = Transport("Rhino", capacity: 10, x: 10f);
            DataBinding<UnitData> cargo = Squad("Grunts", x: 10f);
            TransportUtilities.Embark(cargo.GetValue(), rhino.GetValue());

            var picks = new HashSet<string>();
            for (int seed = 0; seed < 25; seed++)
            {
                picks.Add(new SoloRulesActivationResolver(new Random(seed), ctx.TableState)
                    .Resolve(ActivationMenu(cargo, rhino)).GetAwaiter().GetResult().GetValue().Name);
            }

            Assert.That(picks, Does.Contain("Grunts"),
                "AoF's Solo book prints no Transport clause, so its cargo is an ordinary candidate.");
        }

        // The safety valve, which matters because an activation request must always be answerable: if
        // holding the cargo back would leave nothing to activate, nothing is held back.
        [Test]
        public void CargoAloneOnTheMenu_StillActivates()
        {
            DataBinding<UnitData> rhino = Transport("Rhino", capacity: 10, x: 10f);
            DataBinding<UnitData> cargo = Squad("Grunts", x: 10f);
            TransportUtilities.Embark(cargo.GetValue(), rhino.GetValue());

            var resolver = new SoloRulesActivationResolver(new Random(1), Table);

            DataBinding<UnitData> pick = resolver
                .Resolve(ActivationMenu(cargo)).GetAwaiter().GetResult();

            Assert.That(pick.GetValue().Name, Is.EqualTo("Grunts"),
                "the transport is not on this menu (already activated, or dead) - the cargo cannot wait "
                + "forever for it.");
        }

        // ── Fixtures ──────────────────────────────────────────────────────────────────────────────────

        // GameProgressUtilities.WriteProgress is the engine's own "the store has exactly one progress
        // record" writer: it updates the existing one in place rather than adding a second, which a plain
        // Create would do - and TryGetProgress then returns whichever came first, so the round would
        // silently stay at 1.
        private void SetRound(int round) =>
            GameProgressUtilities.WriteProgress(_store, new GameProgressData(EResumeStage.MainPhase, round,
                new List<int>(), new List<int>(), 0, new Dictionary<int, int>(),
                new List<DataBinding<UnitData>>(), GameSettings.GetDefault()));

        private DataBinding<UnitData> Resolve(SelectionRequest<UnitData> request) =>
            new SoloCoOpDeployOrderResolver(CoreRuleCatalog.CreateResolver() is { } r
                    ? new RuleEvaluator(new FixedDiceRoller(4), ruleResolver: r)
                    : null,
                new Random(1), Table)
                .Resolve(request).GetAwaiter().GetResult();

        /// <summary>
        /// The deploy-time embark prompt as <c>ChooseDeployActionStage</c> raises it: the offered
        /// transports, cancellable, with the cancel labelled "Deploy Normally" - which is the
        /// discriminator both AI resolvers key on.
        /// </summary>
        private SelectionRequest<UnitData> EmbarkOffer(params DataBinding<UnitData>[] transports)
        {
            List<SelectionRequest<UnitData>.ValidOption> options = transports
                .Select(t => new SelectionRequest<UnitData>.ValidOption(
                    t, $"Embark into {t.GetValue().Name}"))
                .ToList();

            return new SelectionRequest<UnitData>(_ai, "Deploy Grunts inside a transport, or on the table?",
                options, new List<SelectionRequest<UnitData>.InvalidOption>(), allowCancel: true,
                displayName: "Deploying Grunts",
                cancelLabel: ChooseUnitToDeployStage.DEPLOY_NORMALLY_CHOICE);
        }

        private ChooseUnitToActivateRequest ActivationMenu(params DataBinding<UnitData>[] units)
        {
            var options = units
                .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
                .ToList();

            return new ChooseUnitToActivateRequest(_ai, options,
                Array.Empty<SelectionRequest<UnitData>.InvalidOption>());
        }

        private DataBinding<UnitData> Transport(string name, int capacity, float x = 0f)
        {
            DataBinding<UnitData> unit = Squad(name, x);
            unit.GetValue().AttachRuleDefinition(new ResolvedRule("Transport", CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(capacity) }));
            return unit;
        }

        /// <summary>Fills <paramref name="transport"/> with a squad of the given size, to eat its space.</summary>
        private void Occupy(DataBinding<UnitData> transport, int models)
        {
            DataBinding<UnitData> cargo = Squad($"Cargo of {transport.GetValue().Name}", x: 0f, models: models);
            TransportUtilities.Embark(cargo.GetValue(), transport.GetValue());
        }

        private DataBinding<UnitData> Squad(string name, float x, int models = 1)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, i * 1.2f), _store);
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(_ai, name, quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(_ai, new List<DataBinding<UnitData>> { binding }));
            return binding;
        }
    }
}
