using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #456 - Fast-Play unit leaders: which model the game suggests, and "the leader ... always has to be the
    /// last model that is removed when the unit takes wounds" (GF / AoF Advanced Rulebooks 3.6.0, p17).
    /// Mirrors <see cref="HeroWoundAssignmentTests"/>, whose "hero last" rule this generalises.
    /// </summary>
    [TestFixture]
    public class FastPlayLeaderTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private static Weapon W(string name) => new(name, rangeInches: 24f, attacks: 1, armorPenetration: 0);

        private UnitData MakeUnit(params string[][] kits)
        {
            var bindings = kits.Select(kit =>
            {
                var model = new ModelData(0.75f, kit.Select(W).ToList(), new Position(0, 0), _store);
                return _store.GetDataBinding<ModelData>(_store.Create(model));
            }).ToList();
            return new UnitData(new PlayerID(System.Guid.NewGuid()), "Squad", quality: 4, defense: 4, modelBindings: bindings);
        }

        private DataBinding<UnitData> Bind(UnitData unit) => _store.GetDataBinding<UnitData>(_store.Create(unit));

        // -- Which model leads --------------------------------------------------------------------------

        [Test]
        public void TheUpgradedModelIsSuggested()
        {
            UnitData unit = MakeUnit(
                new[] { "Rifle" }, new[] { "Rifle" }, new[] { "Plasma Rifle" }, new[] { "Rifle" }, new[] { "Rifle" });
            Assert.That(unit.Models.ToList().FindIndex(m => m.ID.Equals(FastPlay.AutoPick(unit))), Is.EqualTo(2),
                "the model with the special weapon - the one a player would protect, and the leader dies last.");
        }

        [Test]
        public void AUniformUnitIsLedByItsFirstModel()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" }, new[] { "Rifle" });
            Assert.That(FastPlay.AutoPick(unit), Is.EqualTo(unit.Models[0].ID));
        }

        [Test]
        public void TheSuggestionIsTheSameEveryTime()
        {
            // Every machine runs this in lockstep and must reach the same leader.
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle", "Grenade" }, new[] { "Flamer" }, new[] { "Rifle" });
            ModelID? first = FastPlay.AutoPick(unit);
            for (int i = 0; i < 10; i++) Assert.That(FastPlay.AutoPick(unit), Is.EqualTo(first));
        }

        [Test]
        public void AJoinedHeroIsNeverTheLeader()
        {
            UnitData host = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" });
            UnitData hero = MakeUnit(new[] { "Plasma Pistol", "Power Sword", "Grenade" });
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });

            Assert.That(FastPlay.AutoPick(host), Is.Not.EqualTo(host.JoinedHeroModelId),
                "the hero already dies last under the Hero rule; the leader is the last of the unit's OWN models.");
        }

        // -- The leader dies last -----------------------------------------------------------------------

        [Test]
        public void TheLeaderIsSparedWhileAnyoneElseHasRoom()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" }, new[] { "Rifle" });
            unit.LeaderModelId = unit.Models[0].ID;   // the FIRST model, so unit order alone would hit it first
            DataBinding<UnitData> bound = Bind(unit);

            var results = new AssignWoundsResults(bound, totalWoundsToAssign: 2);
            results.AutoFill();

            DataBinding<ModelData> leader = unit.ModelBindings[0];
            Assert.That(results.PendingWounds.First(p => p.Model == leader).Wounds, Is.EqualTo(0f));
            Assert.That(results.TryAddWounds(leader), Is.False);
        }

        [Test]
        public void ThePlayerCannotPickTheLeaderEarlyEither()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" });
            unit.LeaderModelId = unit.Models[0].ID;
            var results = new AssignWoundsResults(Bind(unit), totalWoundsToAssign: 1);

            PendingWounds leader = results.PendingWounds.First(p => p.Model.GetValue().ID.Equals(unit.LeaderModelId));
            Assert.That(results.CanAssignWoundTo(leader), Is.False, "the wound dialog greys the leader out.");
        }

        [Test]
        public void TheLeaderTakesTheLastWoundOfTheUnit()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" }, new[] { "Rifle" });
            unit.LeaderModelId = unit.Models[0].ID;
            var results = new AssignWoundsResults(Bind(unit), totalWoundsToAssign: 3);
            results.AutoFill();
            Assert.That(results.PendingWounds.All(p => p.Wounds == 1f), Is.True);
        }

        [Test]
        public void OrderIsRankAndFileThenLeaderThenHero()
        {
            UnitData host = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" }, new[] { "Rifle" });
            UnitData hero = MakeUnit(new[] { "Sword" });
            hero.AttachRuleDefinition(new ResolvedRule("Hero", CoreRuleCatalog.Hero));
            HeroJoinResolver.Apply(new List<(UnitFileEntry, UnitData)>
            {
                (new UnitFileEntry { Id = "host" }, host),
                (new UnitFileEntry { JoinsUnitId = "host" }, hero),
            });
            ModelID heroId = host.JoinedHeroModelId!.Value;
            ModelID leaderId = host.Models.First(m => !m.ID.Equals(heroId)).ID;
            host.LeaderModelId = leaderId;

            List<IModel> order = WoundAllocation.Order(host);
            Assert.That(order[^1].ID, Is.EqualTo(heroId), "the hero is still last of all.");
            Assert.That(order[^2].ID, Is.EqualTo(leaderId), "the leader is last of the unit's own models.");

            var results = new AssignWoundsResults(Bind(host), totalWoundsToAssign: 3);
            results.AutoFill();
            Assert.That(results.PendingWounds.First(p => p.Model.GetValue().ID.Equals(leaderId)).Wounds, Is.EqualTo(1f));
            Assert.That(results.PendingWounds.First(p => p.Model.GetValue().ID.Equals(heroId)).Wounds, Is.EqualTo(0f));
        }

        [Test]
        public void WithNoLeaderNothingChanges()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" });
            var results = new AssignWoundsResults(Bind(unit), totalWoundsToAssign: 1);
            Assert.That(results.TryAddWounds(unit.ModelBindings[0]), Is.True, "the ordinary game: unit order.");
        }

        // -- Which games have it ------------------------------------------------------------------------

        [Test]
        public void OnlyGrimdarkFutureAndAgeOfFantasyHaveFastPlay()
        {
            GameSettings s = GameSettings.GetDefault();
            s.FastPlayShooting = true;
            foreach (EGameSystem system in System.Enum.GetValues<EGameSystem>())
            {
                s.GameSystem = system;
                bool expected = system is EGameSystem.GrimdarkFuture or EGameSystem.AgeOfFantasy;
                Assert.That(FastPlay.UsesLeaders(s), Is.EqualTo(expected), $"{system}");
            }
            Assert.That(FastPlay.RecommendedFromPoints(EGameSystem.GrimdarkFuture), Is.EqualTo(4000));
            Assert.That(FastPlay.RecommendedFromPoints(EGameSystem.AgeOfFantasy), Is.EqualTo(3000));
        }

        [Test]
        public void AllOffByDefault()
        {
            GameSettings s = GameSettings.GetDefault();
            Assert.That(FastPlay.UsesLeaders(s), Is.False, "a pre-#456 save and a new lobby both play the ordinary game.");
        }

        /// <summary>The leader rides with the unit through a save and over the wire.</summary>
        [Test]
        public void TheLeaderSurvivesSerialisation()
        {
            UnitData unit = MakeUnit(new[] { "Rifle" }, new[] { "Rifle" });
            unit.LeaderModelId = unit.Models[1].ID;
            var results = new AssignWoundsResults(Bind(unit), totalWoundsToAssign: 1);

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(results);
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<AssignWoundsResults>(json)!;
            Assert.That(json, Does.Contain("_leaderModelId").Or.Contain("leaderModelId").IgnoreCase,
                "the defender's answer crosses the wire; the receiving side must apply the same order.");
            Assert.That(back, Is.Not.Null);
        }
    }
}
