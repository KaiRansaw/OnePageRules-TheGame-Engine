using FDG.Campaign;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Bug hunt 2026-11-28 (AoF campaign): who a campaign random event picks as "one random unit".
    /// <para>
    /// The Advanced Rulebooks' Campaign Rules v3.6.0 (AoF p66, GF p66, AoFS p63, GFF p63), "Rolling for Random
    /// Units": <i>"If a random event calls for randomly selecting a unit from any army you must first randomly
    /// select an army for the event, and then randomly select a unit to be the target of the event."</i> The game
    /// picked one unit among every unit on the table, so the army with more units took most of the events -
    /// 5 units against 1 took 5 of every 6. It could also pick a unit still in reserve (an Escalating Battles
    /// detachment, an Ambusher) and drop "D3 wounds" or a Dangerous Terrain Test on a unit that is not on the
    /// battlefield.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignEventRandomUnitTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _small;
        private PlayerID _big;

        private static Func<int> Dice(params int[] rolls)
        {
            int i = 0;
            return () => rolls[Math.Min(i++, rolls.Length - 1)];
        }

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _context = new TestGameContext(_store, new FixedDiceRoller(4),
                ruleResolver: CoreRuleCatalog.CreateResolver());
            _small = new PlayerID(Guid.NewGuid());
            _big = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _small }));
            _store.Create(new TeamData(2, new List<PlayerID> { _big }));
        }

        private UnitData Unit(PlayerID owner, string name, float x)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(x, 10f), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, name, quality: 4, defense: 4, modelBindings: bindings);
            unit.StrikeUnitJson = new StrikeUnit { TeamFile = "army.fdgarmy" }.ToJson();
            _store.Create(unit);
            return unit;
        }

        /// <summary>Fires 21 Swarm Attack ("one random non-Shaken unit becomes Shaken") once per face of the
        /// first die and returns who it hit each time, un-Shaking them in between.</summary>
        private async Task<List<string>> SwarmAttackOnEveryFace()
        {
            var hit = new List<string>();
            for (int face = 1; face <= 6; face++)
            {
                string line = await CampaignEventRunner.Apply(_context, CampaignEvents.ByCode(21), Dice(face, 1));
                hit.Add(line.Replace(" is Shaken.", ""));
                foreach (UnitData u in _store.GetAllValues<UnitData>())
                    u.Tokens.RemoveTokens(TokenType.Shaken, 99);
            }
            return hit;
        }

        [Test]
        public async Task OneRandomUnit_PicksTheArmyFirst_SoTheSmallArmyIsHitAsOftenAsTheBigOne()
        {
            Unit(_small, "Lone Hero", 5f);
            for (int i = 0; i < 5; i++) Unit(_big, $"Horde {i + 1}", 30f + i * 3f);

            List<string> hit = await SwarmAttackOnEveryFace();

            Assert.That(hit.Count(h => h == "Lone Hero"), Is.EqualTo(3),
                "two armies: the first die picks the army, three faces each - not 1 in 6 because the other army has 5 units. "
                + $"Hit: {string.Join(", ", hit)}");
        }

        [Test]
        public async Task OneRandomUnit_NeverPicksAUnitInReserve()
        {
            Unit(_small, "On the table", 5f);
            UnitData waiting = Unit(_big, "Waiting in reserve", 60f);
            ReserveRules.PlaceInReserve(waiting);
            Unit(_big, "Deployed", 30f);

            List<string> hit = await SwarmAttackOnEveryFace();

            Assert.That(hit, Has.None.EqualTo("Waiting in reserve"),
                $"a unit not yet on the battlefield can't be the event's target. Hit: {string.Join(", ", hit)}");
            Assert.That(hit, Has.Some.EqualTo("Deployed"));
        }

        [Test]
        public async Task ArmyRewards_StillReachAnArmyWhoseUnitsAreAllInReserve()
        {
            // "One random army gets 25pts" names the army, not a unit on the table: an army still entirely in
            // reserve can win it. The reward rides one of its units' sheets to the write-back.
            UnitData waiting = Unit(_small, "Waiting", 5f);
            ReserveRules.PlaceInReserve(waiting);
            Unit(_big, "Deployed", 30f);

            var winners = new List<string>();
            for (int face = 1; face <= 6; face++)
                winners.Add(await CampaignEventRunner.Apply(_context, CampaignEvents.ByCode(61), Dice(face, 1)));

            Assert.That(winners.Count(w => w.StartsWith("Waiting")), Is.EqualTo(3), string.Join(" | ", winners));
        }
    }
}
