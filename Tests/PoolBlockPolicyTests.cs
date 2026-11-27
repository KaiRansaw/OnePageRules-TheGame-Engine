using FDG.Ai;
using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.Ai.Tactician;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Lane 2 (2026-11-27): bots and Advanced Casting. Magic Pools: "players may take any number of spell dice from their
    /// magic pool, and roll them at the same time as the caster ... If the total blocking result is equal or higher than
    /// the casting result, then the spell is blocked ... if any of the casting dice has an unmodified result of 6, then the
    /// spell can't be blocked." Before, bots never blocked, always cast with the bare minimum of dice, and the Tactical
    /// bot priced every spell as a one-die 4+ even under 2D6 Casting's 10+.
    /// </summary>
    [TestFixture]
    public class PoolBlockPolicyTests
    {
        [Test]
        public void TheOdds_MatchTheBook()
        {
            // One casting die 1-5 (a 6 can't be blocked) against one blocking die that must equal or beat it: 20/36.
            Assert.That(PoolBlockPolicy.BlockChance(1, 1), Is.EqualTo(20.0 / 36.0).Within(1e-9));
            // More blocking dice block more often; more casting dice get through more often.
            Assert.That(PoolBlockPolicy.BlockChance(2, 3), Is.GreaterThan(PoolBlockPolicy.BlockChance(2, 2)));
            Assert.That(PoolBlockPolicy.BlockChance(3, 2), Is.LessThan(PoolBlockPolicy.BlockChance(2, 2)));
            // 2D6 Casting: 7+ is 21/36, 10+ is 6/36.
            Assert.That(PoolBlockPolicy.TwoD6Chance(7), Is.EqualTo(21.0 / 36.0).Within(1e-9));
            Assert.That(PoolBlockPolicy.TwoD6Chance(10), Is.EqualTo(6.0 / 36.0).Within(1e-9));
            // One die at 4+: half. Two dice for a value-1 spell: three in four.
            Assert.That(PoolBlockPolicy.CastChance(1, 1), Is.EqualTo(0.5).Within(1e-9));
            Assert.That(PoolBlockPolicy.CastChance(2, 1), Is.EqualTo(0.75).Within(1e-9));
        }

        [Test]
        public void ABot_BlocksWithTheFewestDiceForAnEvenChance_AndKeepsDiceForItsOwnCasters()
        {
            Assert.That(PoolBlockPolicy.DiceFor(castingDice: 1, pool: 6, keep: 0), Is.EqualTo(1));
            Assert.That(PoolBlockPolicy.DiceFor(castingDice: 3, pool: 12, keep: 0), Is.GreaterThanOrEqualTo(3));
            Assert.That(PoolBlockPolicy.DiceFor(castingDice: 1, pool: 2, keep: 2), Is.EqualTo(0), "the dice are kept for its casters");
            Assert.That(PoolBlockPolicy.DiceFor(castingDice: 4, pool: 1, keep: 0), Is.EqualTo(0), "one die can't hope to block four");
            Assert.That(PoolBlockPolicy.DiceFor(castingDice: 4, pool: 12, keep: 0), Is.InRange(4, 6),
                "four casting dice: about as many to block, not the whole pool");
        }

        [Test]
        public void ABot_CastsWithExtraDice_WhileTheyAreWorthTheBacklashRisk()
        {
            int dice = PoolBlockPolicy.CastDiceFor(value: 1, pool: 10);
            Assert.That(dice, Is.InRange(2, 4), "one die is a coin flip; a few more are worth their risk of a 1");
            Assert.That(PoolBlockPolicy.CastDiceFor(value: 2, pool: 2), Is.EqualTo(2), "never more than the pool");
        }

        [Test]
        public void TheTacticalBot_FindsTheSpell_BehindTheAdvancedCastingNotes()
        {
            // The picker adds "... - 7+ on 2D6" / "... from the pool" / "... - not in book" after the plain "Name (cost)".
            // The Tactical bot matched labels exactly, so with Spell Books, 2D6 Casting or Magic Pools on it never found
            // a spell and every cast fell back to the official bot's random pick.
            var bolt = new SpellDefinition("Warp Bolt", Threshold: 2,
                new TargetSelector(18f, MinCount: 1, MaxCount: 1, ETargetAffinity.Foe, RequireLineOfSight: true),
                new Effect.DealHits(3, Array.Empty<string>()));
            var army = new ArmyData(new PlayerID(Guid.NewGuid()), new List<DataBinding<UnitData>>());
            army.SetSpells(new[] { new RuntimeSpell(bolt, Array.Empty<ResolvedRule>()) });

            Assert.That(SpellValuation.FindByLabel(army, "Warp Bolt (2)")?.Name, Is.EqualTo("Warp Bolt"));
            Assert.That(SpellValuation.FindByLabel(army, "Warp Bolt (2) - 2 x 4+ from the pool")?.Name, Is.EqualTo("Warp Bolt"));
            Assert.That(SpellValuation.FindByLabel(army, "Warp Bolt (2) - 10+ on 2D6, not in book")?.Name, Is.EqualTo("Warp Bolt"));
            Assert.That(SpellValuation.FindByLabel(army, "Warp Bolt (3)"), Is.Null);
        }

        [Test]
        public void TheOfficialBot_AnswersAPoolBlock_AndStillDeclinesATokenAssist()
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var table = new TableState(store);
            var me = new PlayerID(Guid.NewGuid());
            var them = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> Unit(PlayerID owner)
            {
                var model = store.GetDataBinding<ModelData>(store.Create(new ModelData(0.5f, new List<Weapon>(), new Position(10, 10), store)));
                return store.GetDataBinding<UnitData>(store.Create(new UnitData(owner, "U", 4, 4, new List<DataBinding<ModelData>> { model })));
            }
            DataBinding<UnitData> mine = Unit(me), caster = Unit(them);
            var resolver = new AiCastAssistResolver(table);

            CastAssistReply block = resolver.Resolve(new CastAssistRequest(me, mine, caster, false, 6, "Bolt")
                { PoolBlock = true, CastingDice = 1 }).Result;
            CastAssistReply token = resolver.Resolve(new CastAssistRequest(me, mine, caster, false, 6, "Bolt")).Result;

            Assert.That(block.Tokens, Is.EqualTo(1));
            Assert.That(token.Tokens, Is.EqualTo(0), "token assists are unchanged - the book clause has no counter-casting");
        }
    }
}
