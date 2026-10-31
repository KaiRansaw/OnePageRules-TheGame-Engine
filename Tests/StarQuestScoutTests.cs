using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Utilities;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - Scout in Star Quest, which is one line and was doing nothing.
    //
    // GFSQ Core Rules v2.1.0 says the whole rule in one sentence: "May be deployed anywhere fully within
    // 9" of its deployment zone." Compare Grimdark Future, Age of Fantasy, Firefight and Skirmish, which
    // all say: "May be set aside before deployment. After all other units are deployed, may be deployed
    // anywhere fully within 9" of their deployment zone, over 9" away from non-Scout enemy units. Players
    // alternate in placing Scout units, starting with the player that activates next."
    //
    // Four clauses Star Quest does not have. What is left is "this unit's deployment area is 9" bigger".
    //
    // And it was dead. The rule's only hook defers the unit to the Scout placement pass, and a Star Quest
    // enemy never goes through normal deployment at all: it waits in the collection and arrives with a
    // wave, placed within 6" of a table corner. 158 units across the Star Quest books carry Scout, plus
    // 10 player heroes, and for every one of them the hook fired and nothing happened - the deferred pass
    // does not run for a reserved unit, and the wave placed it like anything else. Confirmed by running a
    // Star Quest Horde game and finding no "deployed via Scout" line anywhere in it.
    //
    // So the widening is applied where a Star Quest unit actually reaches the table.
    [TestFixture]
    public class StarQuestScoutTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private TestGameContext Context(EGameSystem system)
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            if (system == EGameSystem.GFStarQuest) settings.GameMode = EGameMode.StarQuestHorde;
            return new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        private static readonly float[] WaveRadii = { 6f, 9f, 12f, 15f };

        // ── The profile ─────────────────────────────────────────────────────────────────────────────

        [TestCase(EGameSystem.GFStarQuest, true)]
        [TestCase(EGameSystem.GrimdarkFuture, false)]
        [TestCase(EGameSystem.AgeOfFantasy, false)]
        [TestCase(EGameSystem.GFFirefight, false)]
        [TestCase(EGameSystem.AoFSkirmish, false)]
        public void OnlyStarQuest_HasTheSimpleScout(EGameSystem system, bool simple)
        {
            Assert.That(CoreRulesProfile.For(system).ScoutIsSimpleRedeploy, Is.EqualTo(simple));
        }

        // ── The widening ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void InStarQuest_AScoutUnitGetsNineMoreInches()
        {
            // The rule, stated as the number a player would measure. Every step of the crowding
            // escalation widens, not just the first, so a Scout in a packed corner keeps its advantage
            // over the non-Scouts beside it.
            IUnit scout = MakeUnit("Shadow Hound", withScout: true);

            float[] widened = ScoutDeploymentRules.WidenForScout(Context(EGameSystem.GFStarQuest), scout, WaveRadii);

            Assert.That(widened, Is.EqualTo(new[] { 15f, 18f, 21f, 24f }).AsCollection);
            Assert.That(widened[0] - WaveRadii[0], Is.EqualTo(ScoutDeploymentRules.ForwardRangeInches),
                "GFSQ 2.1.0: \"anywhere fully within 9\\\" of its deployment zone\"");
        }

        [Test]
        public void InStarQuest_AUnitWithoutScoutIsUnchanged()
        {
            // The negative half. Widening everything would just be a bigger table.
            IUnit plain = MakeUnit("Grunts", withScout: false);

            Assert.That(ScoutDeploymentRules.WidenForScout(Context(EGameSystem.GFStarQuest), plain, WaveRadii),
                Is.SameAs(WaveRadii), "and the array is handed straight back, not copied");
        }

        [TestCase(EGameSystem.GrimdarkFuture)]
        [TestCase(EGameSystem.AgeOfFantasy)]
        [TestCase(EGameSystem.GFFirefight)]
        [TestCase(EGameSystem.AoFSkirmish)]
        public void InEveryOtherSystem_AScoutUnitIsUnchangedHere(EGameSystem system)
        {
            // The other four books set the unit aside and place it forward of its own zone with a
            // keep-out from enemies, which is PlaceDeferredUnitsStage's job and must stay there. A Scout
            // that ALSO got a wider arrival radius would be getting the rule twice.
            IUnit scout = MakeUnit("Pathfinders", withScout: true);

            Assert.That(ScoutDeploymentRules.WidenForScout(Context(system), scout, WaveRadii),
                Is.SameAs(WaveRadii));
        }

        [Test]
        public void TheWideningDoesNotMutateTheCallersArray()
        {
            // HordePlacement hands in a shared static (StarQuestHordeRules.WaveCornerRadii). Widening it
            // in place would make every later wave 9" wider, Scout or not, and compound each time.
            var radii = new[] { 6f, 9f };
            ScoutDeploymentRules.WidenForScout(Context(EGameSystem.GFStarQuest),
                MakeUnit("Shadow Hound", withScout: true), radii);

            Assert.That(radii, Is.EqualTo(new[] { 6f, 9f }).AsCollection);
        }

        [Test]
        public void ScoutIsRecognisedFromABooksOwnRuleName()
        {
            // Star Quest's books carry "Scout" as their own entry rather than resolving it against the
            // core catalog, so the match is by name. Worth its own test: a reference comparison would
            // have left all 158 carriers unrecognised and this whole fix would have been a no-op.
            IUnit fromBook = MakeUnit("Knight Infiltration Brother", withScout: true, bookOwnDefinition: true);

            Assert.That(ScoutDeploymentRules.HasScout(fromBook), Is.True);
            Assert.That(ScoutDeploymentRules.WidenForScout(Context(EGameSystem.GFStarQuest), fromBook, WaveRadii)[0],
                Is.EqualTo(15f));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private IUnit MakeUnit(string name, bool withScout, bool bookOwnDefinition = false)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });

            if (withScout)
            {
                // A book's own "Scout" is a definition of its own with the same name, which is exactly
                // what HasScout has to cope with.
                Rules.Definitions.SpecialRuleDefinition definition = bookOwnDefinition
                    ? new Rules.Definitions.SpecialRuleDefinition("Scout",
                        Array.Empty<Rules.Definitions.HookEntry>(),
                        Array.Empty<Rules.Definitions.ActivatedAbility>())
                    : CoreRuleCatalog.Scout;
                unit.AttachRuleDefinition(new ResolvedRule("Scout", definition,
                    Array.Empty<Rules.Foundation.RuleArgument>()));
            }

            return unit;
        }
    }
}
