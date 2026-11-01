using FDG.Ai.Solo;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #419 - the official AI's deployment, from <b>Solo &amp; Co-Op Rules v3.6.0</b>: three groups, a
    /// rolled table section each, one random unit at a time, Scout last.
    ///
    /// <para>
    /// The group-to-section rolls are the only deployment state the book needs kept between requests - the
    /// engine asks about one unit at a time, and the three groups have to agree with each other across all
    /// of those separate calls. The "re-rolling if all 3 groups would deploy in the same section" clause is
    /// the one most likely to be dropped, because a bot that puts everything in one corner still looks
    /// plausible.
    /// </para>
    /// </summary>
    [TestFixture]
    public class SoloCoOpDeploymentTests
    {
        private GameDataStore _store = null!;
        private PlayerID _ai;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _ai = new PlayerID(Guid.NewGuid());
        }

        /// <summary>Dice from a fixed script, so each clause is tested on its own.</summary>
        private static Func<int, int> Rolls(params int[] script)
        {
            int index = 0;
            return _ => script[Math.Min(index++, script.Length - 1)];
        }

        private DataBinding<UnitData> Unit(string name, bool scout = false, bool transport = false)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f), _store);
            var bindings = new List<DataBinding<ModelData>>
            {
                _store.GetDataBinding<ModelData>(_store.Create(model)),
            };
            var unit = new UnitData(_ai, name, quality: 4, defense: 4, modelBindings: bindings);
            if (scout) unit.AttachRuleDefinition(new ResolvedRule("Scout", CoreRuleCatalog.Scout,
                Array.Empty<RuleArgument>()));
            if (transport) unit.AttachRuleDefinition(new ResolvedRule("Transport", CoreRuleCatalog.Transport,
                new RuleArgument[] { new RuleArgument.Int(6) }));
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }

        private SelectionRequest<UnitData> DeployChoice(params DataBinding<UnitData>[] units) =>
            new(_ai, "Choose Unit to Deploy",
                units.Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name)).ToList(),
                Array.Empty<SelectionRequest<UnitData>.InvalidOption>(),
                allowCancel: false, displayName: "Deploying");

        // ── Groups and sections ──────────────────────────────────────────────────────────────────────

        [Test]
        public void ThreeGroupsGetASectionEach()
        {
            var plan = new SoloCoOpDeploymentPlan(Rolls(1, 2, 3));

            int[] sections = plan.Sections(SoloCoOpProfile.GrimdarkFuture);

            Assert.That(sections, Has.Length.EqualTo(3));
            Assert.That(sections, Is.EqualTo(new[] { 1, 2, 3 }).AsCollection);
        }

        [Test]
        public void TheSectionsAreRolledOnlyOnce()
        {
            // Rolled lazily on the first placement and then read: the engine deploys one unit at a time,
            // and a fresh roll per unit would scatter a group across the table.
            var plan = new SoloCoOpDeploymentPlan(Rolls(1, 2, 3, 3, 3, 3));

            int[] first = plan.Sections(SoloCoOpProfile.GrimdarkFuture);
            int[] again = plan.Sections(SoloCoOpProfile.GrimdarkFuture);

            Assert.That(again, Is.EqualTo(first).AsCollection);
        }

        [Test]
        public void AllThreeGroupsInOneSection_IsRerolled()
        {
            // "re-rolling if all 3 groups would deploy in the same section". The script rolls 2,2,2 first -
            // all one section - then 1,2,3.
            var plan = new SoloCoOpDeploymentPlan(Rolls(2, 2, 2, 1, 2, 3));

            int[] sections = plan.Sections(SoloCoOpProfile.GrimdarkFuture);

            Assert.That(sections.Distinct().Count(), Is.GreaterThan(1),
                "an army deployed entirely into one third of the edge is what this clause prevents.");
        }

        [Test]
        public void TheRerollIsBounded_SoABadStreamCannotHangDeployment()
        {
            // Dice that always say section 2. A while-loop here would spin forever; the book's intent is
            // served well enough by accepting a dull layout after a bounded number of tries.
            var plan = new SoloCoOpDeploymentPlan(Rolls(2));

            int[] sections = plan.Sections(SoloCoOpProfile.GrimdarkFuture);

            Assert.That(sections, Has.Length.EqualTo(3));
            Assert.That(sections.All(s => s == 2), Is.True);
        }

        [Test]
        public void SkirmishPutsThreeGroupsIntoTwoSections()
        {
            // Its book says "3 groups" and then "2 sections" in consecutive sentences. Followed literally:
            // three groups, two sections, and its own re-roll clause resolves the squeeze.
            var plan = new SoloCoOpDeploymentPlan(Rolls(1, 5, 2));

            int[] sections = plan.Sections(SoloCoOpProfile.AoFSkirmish);

            Assert.That(sections, Has.Length.EqualTo(3));
            Assert.That(sections.All(s => s is 1 or 2), Is.True,
                "a third section does not exist in Skirmish and must never be returned.");
            Assert.That(sections.Distinct().Count(), Is.GreaterThan(1));
        }

        [Test]
        public void UnitsAreAssignedToGroupsByDeploymentOrder()
        {
            var plan = new SoloCoOpDeploymentPlan(Rolls(1, 2, 3));
            SoloCoOpProfile gf = SoloCoOpProfile.GrimdarkFuture;

            Assert.Multiple(() =>
            {
                Assert.That(plan.SectionFor(0, gf), Is.EqualTo(1));
                Assert.That(plan.SectionFor(1, gf), Is.EqualTo(2));
                Assert.That(plan.SectionFor(2, gf), Is.EqualTo(3));
                Assert.That(plan.SectionFor(3, gf), Is.EqualTo(1), "the fourth unit joins the first group.");
            });
        }

        // ── Deployment order ────────────────────────────────────────────────────────────────────────

        [Test]
        public async Task ScoutUnitsDeployAfterEveryoneElse()
        {
            // "AI units with Scout must always be deployed after all other units." Scout deploys further
            // forward than the rest, so going last means it is placed knowing where everything else stands.
            DataBinding<UnitData> scout = Unit("Scouts", scout: true);
            DataBinding<UnitData> line = Unit("Warriors");
            var resolver = new SoloCoOpDeployOrderResolver(null, new Random(1));

            for (int attempt = 0; attempt < 12; attempt++)
            {
                DataBinding<UnitData> pick = await resolver.Resolve(DeployChoice(scout, line));
                Assert.That(pick.GetValue().Name, Is.EqualTo("Warriors"),
                    "with a non-Scout still waiting, the Scout must never be chosen.");
            }
        }

        [Test]
        public async Task OnceOnlyScoutsRemain_AScoutDeploys()
        {
            // "after all other units" is not "never": when they are all that is left, one has to go or
            // deployment cannot finish.
            DataBinding<UnitData> a = Unit("ScoutsA", scout: true);
            DataBinding<UnitData> b = Unit("ScoutsB", scout: true);
            var resolver = new SoloCoOpDeployOrderResolver(null, new Random(1));

            DataBinding<UnitData> pick = await resolver.Resolve(DeployChoice(a, b));

            Assert.That(pick.GetValue().Name, Does.StartWith("Scouts"));
        }

        [Test]
        public async Task TheOrderIsRandomAmongNonScouts()
        {
            // "deploy one random unit at a time". The baseline took the first offered - army build order,
            // which let a player read the deployment in advance.
            var units = Enumerable.Range(0, 6).Select(i => Unit($"U{i}")).ToArray();
            var picks = new HashSet<string>();

            for (int seed = 0; seed < 12; seed++)
            {
                var resolver = new SoloCoOpDeployOrderResolver(null, new Random(seed));
                DataBinding<UnitData> pick = await resolver.Resolve(DeployChoice(units));
                picks.Add(pick.GetValue().Name);
            }

            Assert.That(picks.Count, Is.GreaterThan(1),
                "twelve different seeds should not all choose the same unit.");
        }

        [Test]
        public async Task ASingleRemainingUnit_IsChosenWithoutFuss()
        {
            DataBinding<UnitData> only = Unit("Last", scout: true);
            var resolver = new SoloCoOpDeployOrderResolver(null, new Random(1));

            DataBinding<UnitData> pick = await resolver.Resolve(DeployChoice(only));

            Assert.That(pick.GetValue().Name, Is.EqualTo("Last"));
        }
    }
}
