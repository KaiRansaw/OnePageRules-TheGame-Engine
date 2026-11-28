using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Bug hunt 2026-11-28 (AoF campaign): the two Hero lines of "Earning XP" in the GF and AoF Campaign Rules
    /// v3.6.0 - <i>"+3 XP: Killed a Hero and its unit"</i> and <i>"Note that Heroes attached to units also earn XP
    /// with the unit."</i> Before this, killing a unit led by a joined Hero counted only the Hero (+2, not +3), and
    /// a Hero joined to a unit never counted a kill at all - its sheet only ever got the +1 for not being a
    /// casualty. The skirmish books (GF: Firefight, AoF: Skirmish) print neither line, so there nothing changes.
    /// </summary>
    [TestFixture]
    public class CampaignHeroKillXpTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        private DataBinding<ModelData> Model() =>
            _store.GetDataBinding<ModelData>(_store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(0, 0), _store)));

        private static string Sheet(string id) => new StrikeUnit { Id = id, TeamFile = "army.fdgarmy" }.ToJson();

        /// <summary>A three-model unit with a Hero joined in, both carrying campaign sheets.</summary>
        private UnitData HeroLedUnit(string name, out DataBinding<ModelData> heroModel)
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, 4, 4,
                new List<DataBinding<ModelData>> { Model(), Model(), Model() })
            { StrikeUnitJson = Sheet(name) };
            heroModel = Model();
            var attachment = new HeroAttachment(heroModel.GetValue().ID, quality: 3, defense: 3, heroWounds: 3)
            { StrikeUnitJson = new StrikeUnit { Id = name + " hero", IsHero = true, TeamFile = "army.fdgarmy" }.ToJson() };
            unit.AttachHero(attachment, new List<DataBinding<ModelData>> { heroModel });
            return unit;
        }

        private static StrikeUnit Of(string? json) => StrikeUnit.FromJson(json)!;

        [Test]
        public void KillingAUnitLedByAJoinedHero_IsAHeroAndItsUnit_ThreeXp()
        {
            var killer = new UnitData(new PlayerID(Guid.NewGuid()), "Spearmen", 4, 4, new List<DataBinding<ModelData>>())
            { StrikeUnitJson = Sheet("spear") };
            UnitData dead = HeroLedUnit("Knights", out _);

            FirefightCampaign.OnUnitDestroyed(dead, killer, null, heroAndItsUnit: true);

            StrikeUnit sheet = Of(killer.StrikeUnitJson);
            Assert.That(sheet.Kills + 2 * sheet.HeroKills, Is.EqualTo(3),
                "\"+3 XP: Killed a Hero and its unit\" - the after-game sum is Kills + 2 x HeroKills.");
        }

        [Test]
        public void InTheSkirmishBooks_AHeroLedUnitIsStillAHeroUnit_TwoXp()
        {
            var killer = new UnitData(new PlayerID(Guid.NewGuid()), "Spearmen", 4, 4, new List<DataBinding<ModelData>>())
            { StrikeUnitJson = Sheet("spear") };
            UnitData dead = HeroLedUnit("Knights", out _);

            FirefightCampaign.OnUnitDestroyed(dead, killer, null, heroAndItsUnit: false);

            StrikeUnit sheet = Of(killer.StrikeUnitJson);
            Assert.That(sheet.Kills + 2 * sheet.HeroKills, Is.EqualTo(2), "AoF: Skirmish / GF: Firefight: \"+2 XP: Killed a Hero unit\".");
        }

        [Test]
        public void AJoinedHero_EarnsTheUnitsKillsWithIt()
        {
            UnitData killer = HeroLedUnit("Knights", out _);
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Goblins", 4, 5, new List<DataBinding<ModelData>>());

            FirefightCampaign.OnUnitDestroyed(dead, killer, null, heroAndItsUnit: true);

            Assert.That(Of(killer.StrikeUnitJson).Kills, Is.EqualTo(1), "the unit's own kill.");
            Assert.That(Of(killer.HeroAttachment!.StrikeUnitJson).Kills, Is.EqualTo(1),
                "\"Heroes attached to units also earn XP with the unit\" - the Hero's own sheet counts it too.");
        }

        [Test]
        public void AJoinedHeroThatHasFallen_EarnsNothingMore()
        {
            UnitData killer = HeroLedUnit("Knights", out DataBinding<ModelData> hero);
            hero.GetValue().DealWounds(99);
            var dead = new UnitData(new PlayerID(Guid.NewGuid()), "Goblins", 4, 5, new List<DataBinding<ModelData>>());

            FirefightCampaign.OnUnitDestroyed(dead, killer, null, heroAndItsUnit: true);

            Assert.That(Of(killer.HeroAttachment!.StrikeUnitJson).Kills, Is.EqualTo(0));
            Assert.That(Of(killer.StrikeUnitJson).Kills, Is.EqualTo(1));
        }
    }
}
