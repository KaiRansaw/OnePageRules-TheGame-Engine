using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Advanced Casting - Currents / Winds of Power, part 1 (GF p34, GFF p31, AoF p34, AoFS p31): Spell Books,
    /// 2D6 Casting, Heavy Backlash. The cast itself is covered in CasterRuleIntegrationTests.</summary>
    [TestFixture]
    public class AdvancedCastingTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private readonly List<StringSelectionRequest> _asked = new();
        private Func<StringSelectionRequest, string>? _answer;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private sealed class Faces : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public Faces(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_i < _faces.Length ? _faces[_i++] : 1);
        }

        private void Build(EGameSystem system, EAdvancedRules rules, params int[] faces)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _asked.Clear();
            _context = new TestGameContext(_store, new Faces(faces), settings: settings, playerRequester: new Answer(r => r switch
            {
                StringSelectionRequest q => Record(q),
                AssignWoundsRequest w => AutoFill(w),
                _ => throw new InvalidOperationException(r.GetType().Name),
            }));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
        }

        private object Record(StringSelectionRequest q)
        {
            _asked.Add(q);
            return _answer?.Invoke(q) ?? q.Recommended ?? q.ValidOptions[0];
        }

        private static object AutoFill(AssignWoundsRequest w)
        {
            var results = new AssignWoundsResults(w.UnitReceivingWounds, w.Packets);
            results.AutoFill();
            return results;
        }

        private static RuntimeSpell Spell(string name, int cost) => new RuntimeSpell(
            new SpellDefinition(name, cost, new TargetSelector(18f, 1, 1, ETargetAffinity.Self, RequireLineOfSight: false),
                new Effect.AddRule("Furious", ELifetime.NextTrigger)), Array.Empty<ResolvedRule>());

        private UnitData Unit(PlayerID owner, Position at, int models, int casterLevel = 0, int tough = 1, IReadOnlyList<RuntimeSpell>? spells = null)
        {
            var bindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < models; i++)
            {
                var model = new ModelData(new CircleBase(0.5f), tough,
                    _store.GetDataBinding<float>(_store.Create((float)tough)),
                    _store.GetDataBinding<Position>(_store.Create(new Position(at.x + i * 1.5f, at.z))),
                    _store.GetDataBinding<Float2>(_store.Create(new Float2(0f, 1f))), new List<Weapon>());
                bindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }
            var unit = new UnitData(owner, casterLevel > 0 ? "Wizard" : "Squad", quality: 4, defense: 4, modelBindings: bindings);
            if (casterLevel > 0)
                unit.AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster, new RuleArgument[] { new RuleArgument.Int(casterLevel) }));
            var army = new ArmyData(owner, new List<DataBinding<UnitData>> { _store.GetDataBinding<UnitData>(_store.Create(unit)) });
            if (spells != null) army.SetSpells(spells);
            _store.Create(army);
            return unit;
        }

        private static readonly RuntimeSpell[] SixSpells =
            { Spell("One", 1), Spell("Two", 2), Spell("Three", 3), Spell("Four", 1), Spell("Five", 2), Spell("Six", 3) };

        private AdvancedCastingState State => AdvancedCasting.State(_store)!;

        [Test]
        public void TheOptions_AreInAllFourBooks_WithEachBooksWoundCounts()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
            {
                Assert.That(AdvancedRules.Chapters(s), Does.Contain(AdvancedRules.AdvancedCastingChapter), s.ToString());
                Assert.That(AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, s).Select(o => o.Name),
                    Is.EqualTo(new[] { AdvancedCasting.SpellBooks, AdvancedCasting.TwoD6Casting, AdvancedCasting.MagicPools, AdvancedCasting.HeavyBacklash }), s.ToString());
                foreach (AdvancedRuleOption o in AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, s))
                    Assert.That((o.BookText + o.GameText).All(ch => ch < 128), $"ASCII: {s} {o.Name}");
            }
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.AdvancedCastingChapter));
            // As printed: GF and GFF say 3 wounds, AoF and AoFS 1 (house rules C).
            Assert.That(AdvancedCasting.TwoD6BacklashWounds(EGameSystem.GrimdarkFuture), Is.EqualTo(3));
            Assert.That(AdvancedCasting.TwoD6BacklashWounds(EGameSystem.GFFirefight), Is.EqualTo(3));
            Assert.That(AdvancedCasting.TwoD6BacklashWounds(EGameSystem.AgeOfFantasy), Is.EqualTo(1));
            Assert.That(AdvancedCasting.TwoD6BacklashWounds(EGameSystem.AoFSkirmish), Is.EqualTo(1));
            Assert.That(AdvancedCasting.BookText(AdvancedCasting.TwoD6Casting, EGameSystem.AgeOfFantasy), Does.Contain("taking 1 wound,"));
            Assert.That(AdvancedCasting.BookText(AdvancedCasting.HeavyBacklash, EGameSystem.GFFirefight), Does.Contain("takes 3D3 wounds"));
        }

        [Test]
        public void HeavyBacklashTables_AreEachBooks()
        {
            Assert.That(AdvancedCasting.Table(EGameSystem.GrimdarkFuture).Select(r => r.Name),
                Is.EqualTo(new[] { "Cascade", "Detonation", "Boom!", "Feedback", "Drained" }));
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                for (int total = 2; total <= 12; total++)
                    Assert.That(AdvancedCasting.Row(s, total), Is.Not.Null);
            Assert.That(AdvancedCasting.Row(EGameSystem.GrimdarkFuture, 4).Name, Is.EqualTo("Cascade"));
            Assert.That(AdvancedCasting.Row(EGameSystem.GrimdarkFuture, 5).Name, Is.EqualTo("Detonation"));
            Assert.That(AdvancedCasting.Row(EGameSystem.GrimdarkFuture, 7).Name, Is.EqualTo("Boom!"));
            Assert.That(AdvancedCasting.Row(EGameSystem.GrimdarkFuture, 9).Name, Is.EqualTo("Feedback"));
            Assert.That(AdvancedCasting.Row(EGameSystem.GrimdarkFuture, 10).Name, Is.EqualTo("Drained"));
            Assert.That(AdvancedCasting.Table(EGameSystem.AgeOfFantasy)[0].Text, Does.Contain("caster's unit takes 2D3"));
            Assert.That(AdvancedCasting.Table(EGameSystem.AoFSkirmish)[0].Text, Does.Contain("closest unit within 6\" takes 2D3"));
        }

        [Test]
        public void TwoD6_Thresholds()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting);
            UnitData caster = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2);
            Assert.That(AdvancedCasting.BaseNeeded(_context, caster, "Any", 4), Is.EqualTo(7));
            Assert.That(AdvancedCasting.ClampTwoD6(13), Is.EqualTo(12), "a 12 always casts");
            Assert.That(AdvancedCasting.ClampTwoD6(1), Is.EqualTo(3), "a 2 always fails");
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.None);
            caster = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2);
            Assert.That(AdvancedCasting.BaseNeeded(_context, caster, "Any", 4), Is.EqualTo(4), "off: the ordinary 4+");
        }

        [Test]
        public async Task SpellBooks_RollXPlusOne_PickX_BotsDropTheDearest()
        {
            // Spell numbers 2, 2 (re-rolled), 3, 6 -> Two, Three, Six; Caster(2) keeps two.
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.SpellBooks, 2, 2, 3, 6);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, spells: SixSpells);
            Unit(_them, new Position(40f, 10f), 3);   // not a caster: no book
            await AdvancedCasting.Settle(_context);
            Assert.That(_asked, Has.Count.EqualTo(1), "one question per caster");
            Assert.That(_asked[0].ValidOptions, Has.Count.EqualTo(3), "X+1 = 3 rolled");
            Assert.That(_asked[0].Recommended, Does.Contain("Three").Or.Contain("Six"), "the bot leaves out a cost-3 spell");
            List<string> book = State.Books[AdvancedCasting.Id(wizard)];
            Assert.That(book, Has.Count.EqualTo(2));
            Assert.That(book, Is.SubsetOf(new[] { "Two", "Three", "Six" }));
            Assert.That(book, Does.Contain("Two"));
        }

        [Test]
        public async Task SpellBooks_TheOwnerPicks()
        {
            Build(EGameSystem.AgeOfFantasy, EAdvancedRules.SpellBooks, 1, 2, 3);
            _answer = q => q.ValidOptions.First(o => o.Contains("One"));
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, spells: SixSpells);
            await AdvancedCasting.Settle(_context);
            Assert.That(State.Books[AdvancedCasting.Id(wizard)], Is.EqualTo(new[] { "Two", "Three" }), "One was left out");
            Assert.That(AdvancedCasting.InBook(_context, wizard, "Two"), Is.True);
            Assert.That(AdvancedCasting.InBook(_context, wizard, "One"), Is.False);
            await AdvancedCasting.Settle(_context);
            Assert.That(_asked, Has.Count.EqualTo(1), "settled once");
        }

        [Test]
        public async Task SpellBooks_Off_AskNothing_AndEverySpellIsInTheBook()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting, 1, 2, 3);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, spells: SixSpells);
            await AdvancedCasting.Settle(_context);
            Assert.That(_asked, Is.Empty);
            Assert.That(AdvancedCasting.InBook(_context, wizard, "One"), Is.True);
        }

        [Test]
        public async Task PlainBacklash_GF_LosesATokenAndThreeWounds()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 6);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(3));
            Assert.That(AdvancedCasting.IsBacklashed(_context, wizard), Is.True);
        }

        [Test]
        public async Task PlainBacklash_AoF_OneWound()
        {
            Build(EGameSystem.AgeOfFantasy, EAdvancedRules.TwoD6Casting);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 6);
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(1));
        }

        [Test]
        public async Task Heavy_Boom_GFF_ThreeWounds()
        {
            Build(EGameSystem.GFFirefight, EAdvancedRules.TwoD6Casting | EAdvancedRules.HeavyBacklash, 3, 4);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 9);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(3), "7 Boom!: 3 wounds in GFF");
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
        }

        [Test]
        public async Task Heavy_Feedback_LosesD3Tokens()
        {
            // 4+4 = 8 Feedback; the D3 comes from a 6 -> 3 tokens.
            Build(EGameSystem.AgeOfFantasy, EAdvancedRules.TwoD6Casting | EAdvancedRules.HeavyBacklash, 4, 4, 6);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 6);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 5, new TokenClearTrigger.ManualOnly()));
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2));
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(1));
        }

        [Test]
        public async Task Heavy_Drained_CostsATokenEveryRound()
        {
            // 6+6 = 12 Drained; D3 from a 2 -> 1 token.
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting | EAdvancedRules.HeavyBacklash, 6, 6, 2);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 6);
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(AdvancedCasting.DrainedLevels(_store, wizard), Is.EqualTo(1));
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 2, new TokenClearTrigger.ManualOnly()));
            AdvancedCasting.AfterRoundStartGrant(_context, wizard, 2);
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(1), "Caster(2) drained to Caster(1)");
        }

        [Test]
        public async Task Heavy_Cascade_GF_JoinedCaster_HitsItsOwnUnit()
        {
            // 1+2 = 3 Cascade; caster D3 (a 2 -> 1), unit 2D3 (6, 6 -> 3 + 3).
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting | EAdvancedRules.HeavyBacklash, 1, 2, 2, 6, 6);
            UnitData squad = Unit(_me, new Position(10f, 10f), 10, casterLevel: 1);
            UnitData bystander = Unit(_them, new Position(13f, 10f), 5);
            await AdvancedCasting.Backlash(_context, squad);
            Assert.That(squad.Models.Count(m => !m.GetIsAlive()), Is.EqualTo(7), "1 + 6 wounds on the caster's own unit");
            Assert.That(bystander.Models.All(m => m.GetIsAlive()), Is.True, "not the bystander - the caster is part of a unit");
        }

        [Test]
        public async Task Heavy_Cascade_AoFS_HitsTheClosestUnitWithin6()
        {
            // 1+1 = 2 Cascade; caster 3D3 (2, 2, 2 -> 3), closest unit 2D3 (2, 2 -> 2).
            Build(EGameSystem.AoFSkirmish, EAdvancedRules.TwoD6Casting | EAdvancedRules.HeavyBacklash, 1, 1, 2, 2, 2, 2, 2);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 1, tough: 9);
            UnitData near = Unit(_them, new Position(14f, 10f), 5);
            UnitData far = Unit(_them, new Position(30f, 10f), 5);
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(3));
            Assert.That(near.Models.Count(m => !m.GetIsAlive()), Is.EqualTo(2));
            Assert.That(far.Models.All(m => m.GetIsAlive()), Is.True);
            Assert.That(State.Recent.Last(), Does.Contain("Cascade"));
        }

        [Test]
        public void MagicPools_IsInAllFourBooks_AndExcludes2D6()
        {
            foreach (EGameSystem sys in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.In(AdvancedRules.AdvancedCastingChapter, sys).Select(o => o.Name), Does.Contain(AdvancedCasting.MagicPools));
            Assert.That(AdvancedCasting.PoolBacklashWounds(EGameSystem.GrimdarkFuture), Is.EqualTo(1));
            Assert.That(AdvancedCasting.PoolBacklashWounds(EGameSystem.GFFirefight), Is.EqualTo(3));
            Assert.That(AdvancedCasting.PoolBacklashWounds(EGameSystem.AgeOfFantasy), Is.EqualTo(1));
            Assert.That(AdvancedCasting.PoolBacklashWounds(EGameSystem.AoFSkirmish), Is.EqualTo(3));
            EAdvancedRules r = AdvancedCasting.Tick(EAdvancedRules.TwoD6Casting | EAdvancedRules.SpellBooks, EAdvancedRules.MagicPools, true);
            Assert.That(r, Is.EqualTo(EAdvancedRules.MagicPools | EAdvancedRules.SpellBooks), "ticking Pools clears 2D6");
            r = AdvancedCasting.Tick(r, EAdvancedRules.TwoD6Casting, true);
            Assert.That(r, Is.EqualTo(EAdvancedRules.TwoD6Casting | EAdvancedRules.SpellBooks), "and back");
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.TwoD6Casting | EAdvancedRules.MagicPools);
            Assert.That(AdvancedCasting.PoolsOn(_context), Is.False, "both on (an old save): 2D6 wins");
        }

        [Test]
        public void MagicPools_TokensBecomeTwoDiceEach_AfterDrained()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.MagicPools);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 3);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            AdvancedCasting.AfterRoundStartGrant(_context, wizard, 3);
            Assert.That(AdvancedCasting.PoolDice(_store, _me), Is.EqualTo(6), "2 x 3");
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(0), "no tokens under pools");
            var s = State;
            s.Drained[AdvancedCasting.Id(wizard)] = 1;
            AdvancedCasting.Save(_context, s);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 3, new TokenClearTrigger.ManualOnly()));
            AdvancedCasting.AfterRoundStartGrant(_context, wizard, 3);
            Assert.That(AdvancedCasting.PoolDice(_store, _me), Is.EqualTo(10), "Drained: 2 x 2 more");
        }

        [Test]
        public void MagicPools_RoundEnd_DiscardsDownToSixPerCaster()
        {
            Build(EGameSystem.GrimdarkFuture, EAdvancedRules.MagicPools);
            Unit(_me, new Position(10f, 10f), 1, casterLevel: 3);
            AdvancedCasting.AddPool(_context, _me, 9);
            AdvancedCasting.RoundEnd(_context);
            Assert.That(AdvancedCasting.PoolDice(_store, _me), Is.EqualTo(6));
            AdvancedCasting.AddPool(_context, _me, -2);
            AdvancedCasting.RoundEnd(_context);
            Assert.That(AdvancedCasting.PoolDice(_store, _me), Is.EqualTo(4), "under the cap: kept");
        }

        [Test]
        public async Task MagicPools_Backlash_LosesADie_GFF3Wounds()
        {
            Build(EGameSystem.GFFirefight, EAdvancedRules.MagicPools);
            UnitData wizard = Unit(_me, new Position(10f, 10f), 1, casterLevel: 2, tough: 6);
            wizard.Tokens.AddToken(new Token(TokenType.SpellTokens, 2, new TokenClearTrigger.ManualOnly()));
            AdvancedCasting.AddPool(_context, _me, 3);
            await AdvancedCasting.Backlash(_context, wizard);
            Assert.That(AdvancedCasting.PoolDice(_store, _me), Is.EqualTo(2));
            Assert.That(wizard.Tokens.GetTokenCount(TokenType.SpellTokens), Is.EqualTo(2), "tokens untouched");
            Assert.That(wizard.Models[0].WoundsDealt, Is.EqualTo(3));
        }

        [Test]
        public void PanelLines_ShowTheSystemsAndBooks()
        {
            Build(EGameSystem.AgeOfFantasy, EAdvancedRules.SpellBooks | EAdvancedRules.TwoD6Casting);
            var state = new AdvancedCastingState();
            state.Books["x"] = new List<string> { "Bolt", "Ward" };
            state.Names["x"] = "Wizard";
            AdvancedCasting.Save(_context, state);
            var lines = AdvancedCasting.PanelLines(_store, _context.Settings, 0);
            Assert.That(lines[0].Text, Does.StartWith("Winds of Power"));
            Assert.That(lines.Any(l => l.Text == "Wizard: Bolt, Ward"));
            Assert.That(lines.All(l => (l.Text + l.Hover).All(ch => ch < 128)), "ASCII");
            Assert.That(AdvancedCasting.PanelLines(_store, GameSettings.GetDefault(), 0), Is.Empty, "off: nothing");
        }
    }
}
