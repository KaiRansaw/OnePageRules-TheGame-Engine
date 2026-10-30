using FDG.Data;
using FDG.Players;
using FDG.Quest;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Star Quest starting skills that work through "this round" flags (S2): Reckless, Vengeful Strike,
    /// Dread Attack, Martial Strike, Infiltrating Step, and the caster skills' spell range.
    /// </summary>
    [TestFixture]
    public class QuestStartingSkillsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _heroes, _enemies;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _heroes = new PlayerID(Guid.NewGuid());
            _enemies = new PlayerID(Guid.NewGuid());
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.StarQuestMission;
            _context = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
        }

        private UnitData Unit(PlayerID owner, string name, bool hero)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(), initialPosition: new Position(10f, 10f), gameDataStore: _store);
            DataBinding<ModelData> binding = _store.GetDataBinding<ModelData>(_store.Create(model));
            var unit = new UnitData(owner, name, quality: 4, defense: 5, modelBindings: new List<DataBinding<ModelData>> { binding });
            if (hero) unit.QuestHeroJson = new QuestHero { Name = name, Power = 3 }.ToJson();
            return _store.GetDataBinding<UnitData>(_store.Create(unit)).GetValue();
        }

        private QuestProgress Quest => GameProgressUtilities.GetQuestProgress(_store);

        [Test]
        public void EveryStartingSkill_IsInTheGame()
        {
            foreach (string name in new[] { "Reckless", "Vengeful Strike", "Dread Attack", "Martial Strike", "Infiltrating Step",
                         "Conjuration", "Distant Spell", "Pact of Strength", "Enchantment", "Tides of Chaos" })
                Assert.That(QuestSkill.Find(name), Is.Not.Null, name);
        }

        [Test]
        public void Flags_SetTakeAndClearAtTheEndOfTheRound()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            QuestFlags.Set(_context, hero, QuestFlags.Reckless);
            QuestFlags.AddSpellRange(_context, hero, 6);
            QuestFlags.AddSpellRange(_context, hero, 12);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Reckless), Is.True);
            Assert.That(QuestFlags.SpellRangeBonus(Quest, hero), Is.EqualTo(18f), "Conjuration and Distant Spell stack");
            Assert.That(QuestFlags.Describe(Quest, hero), Does.Contain("Reckless"));

            Assert.That(QuestFlags.Take(_context, hero, QuestFlags.SpellRange), Is.EqualTo("18"));
            Assert.That(QuestFlags.SpellRangeBonus(Quest, hero), Is.EqualTo(0f), "used up by the next spell");

            QuestProgress q = Quest;
            QuestFlags.ClearRound(q);
            GameProgressUtilities.SetQuestProgress(_store, q);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Reckless), Is.False);
        }

        [Test]
        public void Reckless_DealsAndTakesOneExtraWound()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.Reckless);

            Assert.That(QuestFlags.RecklessExtraWounds(_context, hero, enemy, melee: true, spell: false, 0f, 2f), Is.EqualTo(1f));
            Assert.That(QuestFlags.RecklessExtraWounds(_context, hero, enemy, melee: false, spell: false, 8f, 1f), Is.EqualTo(1f));
            Assert.That(QuestFlags.RecklessExtraWounds(_context, hero, enemy, melee: false, spell: false, 18f, 1f), Is.EqualTo(0f), "shooting from over 12\"");
            Assert.That(QuestFlags.RecklessExtraWounds(_context, hero, enemy, melee: true, spell: false, 0f, 0f), Is.EqualTo(0f), "no wound, no extra");
            Assert.That(QuestFlags.RecklessExtraWounds(_context, enemy, hero, melee: false, spell: false, 20f, 1f), Is.EqualTo(1f),
                "the Reckless hero takes one extra wound from any attack");
        }

        [Test]
        public void VengefulStrike_PlusOneAgainstShakenCloseUp()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.Vengeful);

            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f).Net, Is.EqualTo(0), "not Shaken");
            enemy.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f).Net, Is.EqualTo(1));
            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: false, 10f).Net, Is.EqualTo(1));
            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: false, 20f).Net, Is.EqualTo(0), "shooting from over 12\"");
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Vengeful), Is.True, "lasts the whole round");
        }

        // #407: the point of itemising OnAttack's return - the contribution names itself, so the Battle
        // Report can say "Vengeful Strike +1" rather than a generic "buff +1". That generic chip is what
        // left Kai unable to tell from play whether the skill had fired at all.
        [Test]
        public void VengefulStrike_NamesItselfInItsContribution()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.Vengeful);
            enemy.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));

            QuestAttackModifiers result = QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f);

            Assert.That(result.HitModifiers.Count, Is.EqualTo(1));
            Assert.That(result.HitModifiers[0].Source, Is.EqualTo("Vengeful Strike"));
            Assert.That(result.HitModifiers[0].Delta, Is.EqualTo(1));
            Assert.That(result.HitModifiers[0].Describe(), Is.EqualTo("Vengeful Strike +1"));
        }

        [Test]
        public void DreadAndMartial_WaitForTheRightAttack_ThenNoteTheTarget()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.Dread);
            QuestFlags.Set(_context, hero, QuestFlags.Martial);

            QuestFlags.OnAttack(_context, hero, enemy, melee: false, 8f);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Dread), Is.True, "a shot from within 12\" doesn't trigger Dread Attack");
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Martial), Is.True, "a shot doesn't trigger Martial Strike");

            QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Dread), Is.False);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.Martial), Is.False);
            Assert.That(QuestFlags.Get(Quest, hero, QuestFlags.DreadTarget), Is.EqualTo(enemy.ID.ID.ToString()));
            Assert.That(QuestFlags.Get(Quest, hero, QuestFlags.MartialTarget), Is.EqualTo(enemy.ID.ID.ToString()));
        }

        [Test]
        public void InfiltratingStep_HidesTheHeroUntilItTargetsAnEnemy()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.Infiltrating);
            Assert.That(QuestFlags.CannotBeTargeted(Quest, hero), Is.True);
            Assert.That(QuestFlags.CannotBeTargeted(Quest, enemy), Is.False);

            QuestFlags.OnAttack(_context, hero, enemy, melee: false, 10f);
            Assert.That(QuestFlags.CannotBeTargeted(Quest, hero), Is.False, "attacking ends it");
        }

        [Test]
        public void AdvancedAttackFlags_HitModifiersBothWays()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);

            QuestFlags.Set(_context, hero, QuestFlags.Elude);
            Assert.That(QuestFlags.OnAttack(_context, enemy, hero, melee: true, 0f).Net, Is.EqualTo(-1), "Elude: -1 to hit the hero in melee");
            Assert.That(QuestFlags.OnAttack(_context, enemy, hero, melee: false, 20f).Net, Is.EqualTo(0), "not from over 12\"");

            QuestFlags.Set(_context, hero, QuestFlags.HunterMark, $"{QuestFlags.Id(enemy)}:2");
            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: false, 20f).Net, Is.EqualTo(2), "Hunter's Mark +D3 (2) on the marked enemy");

            QuestFlags.Set(_context, enemy, QuestFlags.GuidingBolt, QuestFlags.Id(hero));
            Assert.That(QuestFlags.OnAttack(_context, hero, enemy, melee: false, 20f).Net, Is.EqualTo(3), "Guiding Bolt adds +1 for the hero's side");
        }

        [Test]
        public void BloodFrenzyAndVow_AddWounds_AndTheHeroPaysForFrenzy()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.BloodFrenzy);
            Assert.That(QuestFlags.ExtraWounds(_context, hero, enemy, melee: true, charging: false, false, 0f, 1f), Is.EqualTo(0f), "only on a charge");
            Assert.That(QuestFlags.ExtraWounds(_context, hero, enemy, melee: true, charging: true, false, 0f, 1f), Is.EqualTo(2f));
            Assert.That(QuestFlags.Get(Quest, hero, QuestFlags.SelfWound), Is.EqualTo("1"), "Blood Frenzy costs the hero a wound");

            QuestFlags.Set(_context, enemy, QuestFlags.Vow, QuestFlags.Id(hero));
            Assert.That(QuestFlags.ExtraWounds(_context, hero, enemy, melee: false, charging: false, false, 20f, 1f), Is.EqualTo(1f));
            Assert.That(QuestFlags.Has(_context, enemy, QuestFlags.Vow), Is.False, "Spiteful Vow is used up");
        }

        [Test]
        public void Charm_StopsOnlyThatEnemyTargetingTheHero()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData charmed = Unit(_enemies, "Charmed", hero: false);
            UnitData other = Unit(_enemies, "Other", hero: false);
            QuestFlags.AddToList(_context, charmed, QuestFlags.CharmedBy, QuestFlags.Id(hero));
            Assert.That(QuestFlags.CannotBeTargeted(Quest, hero, charmed), Is.True);
            Assert.That(QuestFlags.CannotBeTargeted(Quest, hero, other), Is.False);
        }

        [Test]
        public void ChargeSkills_GrantTheirExtraHits_OnlyOnACharge()
        {
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(_context, hero, QuestFlags.ChargeHit);
            QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f, charging: false);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.ChargeHit), Is.True, "waits for a charge");
            QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f, charging: true);
            Assert.That(QuestFlags.Has(_context, hero, QuestFlags.ChargeHit), Is.False);

            QuestFlags.Set(_context, hero, QuestFlags.Sweeping);
            QuestFlags.OnAttack(_context, hero, enemy, melee: true, 0f, charging: true);
            Assert.That(QuestFlags.FreeAction(_context, hero), Does.StartWith("Sweeping Attack"), "a free action follows the charge");
        }

        [Test]
        public void AdvancedSkills_Exist()
        {
            foreach (string name in new[] { "Aimed Strike", "Vicious", "Charm", "Holy Protection", "Circle of Spores", "Sweeping Attack",
                         "Spiteful Control", "Hunter's Mark", "Assassination", "Twin Spell", "Tinkered Luck", "Necromancy", "Sanctuary", "Steel Will" })
                Assert.That(QuestSkill.Find(name), Is.Not.Null, name);
            Assert.That(QuestAdvancedSkills.NotPossible.ContainsKey("Leap"), Is.True);
        }

        [Test]
        public void OutsideStarQuest_NothingHappens()
        {
            GameSettings settings = GameSettings.GetDefault();
            settings.GameMode = EGameMode.Standard;
            var plain = new TestGameContext(_store, new FixedDiceRoller(4), settings: settings);
            UnitData hero = Unit(_heroes, "Hero", hero: true);
            UnitData enemy = Unit(_enemies, "Enemy", hero: false);
            QuestFlags.Set(plain, hero, QuestFlags.Reckless);
            Assert.That(QuestFlags.Has(plain, hero, QuestFlags.Reckless), Is.False);
            Assert.That(QuestFlags.RecklessExtraWounds(plain, hero, enemy, true, false, 0f, 3f), Is.EqualTo(0f));
        }
    }
}
