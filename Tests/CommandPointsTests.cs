using FDG.Data;
using FDG.Players;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Command Points (Advanced Rulebooks 3.6.0: GF p42, GFF p39, AoF p42, AoFS p39) - the framework.</summary>
    [TestFixture]
    public class CommandPointsTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;
        private UnitData _myUnit = null!, _theirUnit = null!;
        private DataBinding<UnitData> _myBinding = null!;
        private readonly List<object> _asked = new();

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private void Build(int face, int playstyle, EGameSystem system = EGameSystem.GrimdarkFuture, Func<object, object>? answer = null, IDiceRoller? roller = null)
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = CommandPoints.WithPick(EAdvancedRules.CommandPoints, playstyle);
            _context = new TestGameContext(_store, roller ?? new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: new Answer(r => { _asked.Add(r); return (answer ?? (q => ((StringSelectionRequest)q).ValidOptions[0]))(r); }));
            _asked.Clear();
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
            Army(_me, 2000);
            Army(_them, 1000);
        }

        private void Army(PlayerID owner, int points)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(5f, 5f), _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, "Unit", quality: 4, defense: 4, modelBindings: bindings) { PointCost = points };
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            if (owner == _me) { _myUnit = unit; _myBinding = binding; } else _theirUnit = unit;
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
        }

        [Test]
        public void ThePlaystyles_AreTheBooks()
        {
            Assert.That(new[] { EGameSystem.GrimdarkFuture, EGameSystem.AgeOfFantasy, EGameSystem.GFFirefight, EGameSystem.AoFSkirmish }
                .Select(CommandPoints.Step), Is.EqualTo(new[] { 1000, 750, 400, 300 }));
            Func<int> two = () => 2, three = () => 3;
            Assert.That(CommandPoints.Earned(CommandPoints.Fixed, 2000, EGameSystem.GrimdarkFuture, two), Is.EqualTo(8));
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 2000, EGameSystem.GrimdarkFuture, two), Is.EqualTo(2));
            Assert.That(CommandPoints.Earned(CommandPoints.FixedRandom, 2000, EGameSystem.GrimdarkFuture, three), Is.EqualTo(12));
            Assert.That(CommandPoints.Earned(CommandPoints.GrowingRandom, 1000, EGameSystem.GrimdarkFuture, three), Is.EqualTo(2), "D3 x 0.5, rounded up");
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 500, EGameSystem.GrimdarkFuture, two), Is.EqualTo(1), "at least one step");
            Assert.That(CommandPoints.Earned(CommandPoints.Growing, 600, EGameSystem.AoFSkirmish, two), Is.EqualTo(2));
            GameSettings s = GameSettings.GetDefault();
            s.AdvancedRules = CommandPoints.WithPick(EAdvancedRules.CommandPoints, 5);
            Assert.That(CommandPoints.Picked(s), Is.EqualTo(5));
            Assert.That(AdvancedRules.Enabled(s), Is.EqualTo(EAdvancedRules.CommandPoints), "the pick bits are not options");
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.CommandPointsChapter));
        }

        [Test]
        public async Task Fixed_GivesItsCpOnce_BeforeTheGame()
        {
            Build(face: 4, CommandPoints.Fixed);
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8));
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(4));
            await CommandPoints.RoundStart(_context, 1);
            await CommandPoints.RoundStart(_context, 2);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8), "Fixed is once only");
        }

        [Test]
        public async Task Growing_GivesCpEachRound_Temporary_LosesThemAtRoundEnd()
        {
            Build(face: 4, CommandPoints.Growing);
            await CommandPoints.RoundStart(_context, 1);
            await CommandPoints.RoundStart(_context, 2);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4));
            CommandPoints.RoundEnd(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4), "Growing keeps them");

            Build(face: 4, CommandPoints.Temporary);
            await CommandPoints.RoundStart(_context, 1);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(2));
            CommandPoints.RoundEnd(_context);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(0));
        }

        [Test]
        public async Task TheDoctrine_IsPickedOrRolled_AndCpCanBeSpent()
        {
            Build(face: 5, CommandPoints.Fixed, answer: r => ((StringSelectionRequest)r).ValidOptions[2]);   // "Defensive Doctrine"
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.DoctrineOf(_context, _me), Is.EqualTo(2));
            Build(face: 5, CommandPoints.Fixed);   // first option = roll: a 5
            await CommandPoints.Settle(_context);
            Assert.That(CommandPoints.DoctrineOf(_context, _them), Is.EqualTo(5));
            Assert.That(CommandPoints.Spend(_context, _them, 3, "test"), Is.True);
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(1));
            Assert.That(CommandPoints.Spend(_context, _them, 2, "test"), Is.False, "can't spend what it hasn't got");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(1));
        }

        // ================================================================= Universal Doctrine stratagems

        private static IDiceResults Faces(params float[] perSide) => new DiceResults(perSide);

        /// <summary>Answers the doctrine pick with "roll", a stratagem offer with its first option, and yes/no as told.</summary>
        private static Func<object, object> Says(bool yes) => r => r switch
        {
            YesNoRequest => yes,
            StringSelectionRequest q => q.ValidOptions[0],
            _ => throw new InvalidOperationException(r.GetType().Name),
        };

        [Test]
        public void PlusOne_LiftsOnlyTheDiceOneShort_NeverANatural1()
        {
            IDiceResults roll = Faces(2, 1, 3, 0, 0, 0);   // two 1s, a 2, three 3s
            Assert.That(CommandPoints.OneShort(roll, 4), Is.EqualTo(3));
            Assert.That(CommandPoints.OneShort(roll, 2), Is.EqualTo(0), "a 1 always fails, +1 or not");
            IDiceResults lifted = CommandPoints.Lift(roll, 3, 2);
            Assert.That(lifted.At(3), Is.EqualTo(1f));
            Assert.That(lifted.At(4), Is.EqualTo(2f));
            Assert.That(lifted.TotalRolls, Is.EqualTo(roll.TotalRolls));
            Assert.That(CommandPoints.Universal.Select(u => u.Cost), Is.EqualTo(new[] { 1, 2, 2, 2, 2, 3 }), "GF p42 / AoF p42 / GFF p39 / AoFS p39");
        }

        [Test]
        public async Task HighAndSupremeCommand_CostTheirCp_OncePerActivation()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);                         // me 8 CP
            IDiceResults roll = Faces(0, 0, 3, 0, 0, 0);                  // three 3s, needing 4+
            Assert.That(await CommandPoints.CommandDice(_context, _me, roll, 4, "hit roll"), Is.EqualTo(3), "Supreme Command first");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(6));
            Assert.That(await CommandPoints.CommandDice(_context, _me, roll, 4, "hit roll"), Is.EqualTo(1), "Supreme is spent this activation - High is left");
            Assert.That(await CommandPoints.CommandDice(_context, _me, roll, 4, "hit roll"), Is.EqualTo(0), "both used");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(5));
            CommandPoints.NewActivation(_context);
            Assert.That(await CommandPoints.CommandDice(_context, _me, Faces(0, 0, 1, 0, 0, 0), 4, "hit roll"), Is.EqualTo(1),
                "one die short: only High Command is offered");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4));
            int asked = _asked.Count;
            Assert.That(await CommandPoints.CommandDice(_context, _me, Faces(0, 0, 0, 0, 2, 0), 4, "hit roll"), Is.EqualTo(0));
            Assert.That(_asked.Count, Is.EqualTo(asked), "nothing missed by 1 - nothing asked");
        }

        [Test]
        public async Task NoThanks_SpendsNothing()
        {
            Build(face: 4, CommandPoints.Fixed, answer: r => r is StringSelectionRequest q && q.ValidOptions.Contains(CommandPoints.NoThanks) ? CommandPoints.NoThanks : Says(true)(r));
            await CommandPoints.Settle(_context);
            Assert.That(await CommandPoints.CommandDice(_context, _me, Faces(0, 0, 3, 0, 0, 0), 4, "hit roll"), Is.EqualTo(0));
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8));
        }

        [Test]
        public async Task AMoraleTest_OneShort_IsPassedWithHighCommand()
        {
            Build(face: 3, CommandPoints.Fixed, answer: Says(true));   // the die shows 3, Quality 4+
            await CommandPoints.Settle(_context);
            var outcome = await FDG.Stages.MoraleUtilities.TakeMoraleTest(_context, _myUnit, 4, FDG.Rules.Foundation.EMoraleTrigger.Shooting);
            Assert.That(outcome.Passed, Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(7));

            Build(face: 3, CommandPoints.Fixed, answer: Says(true));   // the rule off-table: no CP yet, no offer
            outcome = await FDG.Stages.MoraleUtilities.TakeMoraleTest(_context, _myUnit, 4, FDG.Rules.Foundation.EMoraleTrigger.Shooting);
            Assert.That(outcome.Passed, Is.False);
        }

        [Test]
        public async Task WaiveInitiative_PassesTheTurn_UnlessTheOpponentStopsIt()
        {
            // Everyone says yes: I waive (8 -> 6), they stop it (4 -> 2), then they seize (2 -> 0) and I stop that (6 -> 4).
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Assert.That(await CommandPoints.Initiative(_context, _me, new[] { _them }, 1), Is.False);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(4));
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(0));

            // The opponent lets it go: the turn passes.
            Build(face: 4, CommandPoints.Fixed, answer: r => r is YesNoRequest y ? y.DisplayName != "Deciding whether to stop a stratagem" : Says(true)(r));
            await CommandPoints.Settle(_context);
            Assert.That(await CommandPoints.Initiative(_context, _me, new[] { _them }, 1), Is.True);
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(4));
        }

        [Test]
        public async Task SaidNo_IsNotAskedAgainThatRound_AndNobodyIsAskedWithNoOneToActivate()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(false));
            await CommandPoints.Settle(_context);
            int before = _asked.Count;
            Assert.That(await CommandPoints.Initiative(_context, _me, new[] { _them }, 1), Is.False);
            Assert.That(_asked.Count - before, Is.EqualTo(2), "Waive asked of me, Seize of them");
            CommandPoints.NewActivation(_context);
            Assert.That(await CommandPoints.Initiative(_context, _me, new[] { _them }, 1), Is.False);
            Assert.That(_asked.Count - before, Is.EqualTo(2), "not again this round");
            Assert.That(await CommandPoints.Initiative(_context, _me, new[] { _them }, 2), Is.False);
            Assert.That(_asked.Count - before, Is.EqualTo(4), "asked again next round");
            Assert.That(await CommandPoints.Initiative(_context, _me, Array.Empty<PlayerID>(), 3), Is.False);
            Assert.That(_asked.Count - before, Is.EqualTo(4), "no opponent unit left - nothing to hand the turn to");
        }

        [Test]
        public async Task DelayedAndHiddenDeployment()
        {
            Build(face: 4, CommandPoints.Fixed, answer: r => r is YesNoRequest y ? y.DisplayName != "Deciding whether to stop a stratagem" : Says(true)(r));
            await CommandPoints.Settle(_context);
            Assert.That(await CommandPoints.DelayDeployment(_context, _me, new[] { _them }), Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(6));
            Assert.That(await CommandPoints.DelayDeployment(_context, _me, Array.Empty<PlayerID>()), Is.False, "nobody left to place");
            var extra = await CommandPoints.HiddenDeploys(_context, new[] { _me, _them });
            Assert.That(extra.GetValueOrDefault(_me), Is.EqualTo(3));
            Assert.That(extra.GetValueOrDefault(_them), Is.EqualTo(3), "the opponent may do the same");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(3));
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(1));
        }

        // ================================================================= doctrine stratagems

        private sealed class FaceSequence : IDiceRoller
        {
            private readonly int[] _faces;
            private int _i;
            public FaceSequence(params int[] faces) => _faces = faces;
            public IDiceResults Roll(int sideCount, float rollCount) => new FixedDiceResults(_faces[Math.Min(_i++, _faces.Length - 1)]);
        }

        private void Doctrines(int mine, int theirs)
        {
            CommandPointsState s = CommandPoints.State(_store)!;
            s.Doctrine[_me.ToString()] = mine;
            s.Doctrine[_them.ToString()] = theirs;
            CommandPoints.Save(_context, s);
        }

        [Test]
        public void TheSixDoctrines_AreTheBooks()
        {
            Assert.That(CommandPoints.DoctrineStratagems, Has.Count.EqualTo(24));
            for (int d = 1; d <= 6; d++)
                Assert.That(CommandPoints.DoctrineStratagems.Where(x => x.Doctrine == d).Select(x => x.Cost), Is.EqualTo(new[] { 1, 1, 2, 3 }),
                    $"{CommandPoints.DoctrineName(d)}: GF p43 / AoF p43 / GFF p40 / AoFS p40");
            Assert.That(CommandPoints.CostOf(CommandPoints.CodeOfHonor), Is.EqualTo(3));
            Assert.That(CommandPoints.CostOf(CommandPoints.SupremeCommand), Is.EqualTo(2));
            Assert.That(CommandPoints.DoctrineStratagems.All(x => x.Text.All(ch => ch < 128)), "game text is ASCII");
        }

        [Test]
        public async Task OnlyYourOwnDoctrine()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 4, theirs: 2);                                    // me Hunting, them Defensive
            Assert.That(CommandPoints.Can(_context, _me, CommandPoints.DisruptedSight), Is.True);
            Assert.That(CommandPoints.Can(_context, _me, CommandPoints.LightningReflexes), Is.False, "not my doctrine");
            Assert.That(CommandPoints.Can(_context, _me, CommandPoints.HighCommand), Is.True, "Universal: everyone");
            Assert.That(CommandPoints.Can(_context, _them, CommandPoints.LightningReflexes), Is.True);
            Assert.That(CommandPoints.StratagemsOf(_context, _me).Count(), Is.EqualTo(10), "6 Universal + 4 of the doctrine");
        }

        [Test]
        public async Task LightningReflexes_ThenHeightenedSenses()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 4, theirs: 2);
            // I shoot their unit, which is out of cover: they use Lightning Reflexes, I answer with Heightened Senses.
            Assert.That(await CommandPoints.CoverStratagems(_context, _myUnit, _theirUnit, 0, weaponIgnoresCover: false), Is.EqualTo(0));
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(3), "4 - 1");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(7), "8 - 1");
            Assert.That(_theirUnit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.CpLightningReflexes));
            int asked = _asked.Count;
            Assert.That(await CommandPoints.CoverStratagems(_context, _myUnit, _theirUnit, 0, false), Is.EqualTo(0),
                "the next weapon of the same shooting: both still apply, nothing asked");
            Assert.That(_asked.Count, Is.EqualTo(asked));
            CommandPoints.EndOfActivation(_context);
            Assert.That(_theirUnit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.CpLightningReflexes), Is.False, "for that shooting only");
            Assert.That(await CommandPoints.CoverStratagems(_context, _myUnit, _theirUnit, 0, weaponIgnoresCover: true), Is.EqualTo(0),
                "a weapon that ignores cover: Lightning Reflexes would do nothing - not offered");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(3));
        }

        [Test]
        public async Task DisruptedSight_IsMinusOneToHit_ForThatShooting()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 1, theirs: 4);
            await CommandPoints.OfferDisruptedSight(_context, _myUnit, _theirUnit, hitNeed: 6);
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(4), "needing 6s, -1 changes nothing - not offered");
            await CommandPoints.OfferDisruptedSight(_context, _myUnit, _theirUnit, hitNeed: 4);
            Assert.That(CommandPoints.HitModifiers(_myUnit, melee: false, spell: false).Sum(m => m.Delta), Is.EqualTo(-1));
            Assert.That(CommandPoints.HitModifiers(_myUnit, melee: true, spell: false), Is.Empty, "shooting only");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(3));
        }

        [Test]
        public async Task ArmourBreaker_AndSayingNo_ForTheRound()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(false));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 2, theirs: 1);
            Assert.That(await CommandPoints.OfferArmourBreaker(_context, _myUnit, _theirUnit, hits: 3, saveNeed: 4), Is.False);
            int asked = _asked.Count;
            CommandPoints.NewActivation(_context);
            Assert.That(await CommandPoints.OfferArmourBreaker(_context, _myUnit, _theirUnit, hits: 3, saveNeed: 4), Is.False);
            Assert.That(_asked.Count, Is.EqualTo(asked), "said no - not asked again this round");
            Assert.That(await CommandPoints.OfferArmourBreaker(_context, _myUnit, _theirUnit, hits: 3, saveNeed: 7), Is.False);

            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 2, theirs: 1);
            Assert.That(await CommandPoints.OfferArmourBreaker(_context, _myUnit, _theirUnit, hits: 3, saveNeed: 4), Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(6));
        }

        [Test]
        public async Task PushForward_OnTheMenu_GivesTwoInches_AndIsRefunded()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 1, theirs: 3);
            var options = CommandPoints.MoveOptions(_context, _myUnit, canMove: true, canCharge: true).ToList();
            Assert.That(options.Select(o => o.Stratagem), Is.EqualTo(new[] { CommandPoints.PushForward }), "Strategic: no Sweeping Move");
            Assert.That(CommandPoints.MoveOptions(_context, _myUnit, canMove: false, canCharge: false), Is.Empty);
            Assert.That(CommandPoints.TakeMoveOption(_context, _myUnit, CommandPoints.PushForward), Is.True);
            Assert.That(CommandPoints.MoveBonus(_myUnit), Is.EqualTo((2f, 2f, 0f)));
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(7));
            CommandPoints.RefundMoveOption(_context, _myUnit);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(8), "backed out of the move");
            Assert.That(CommandPoints.MoveBonus(_myUnit), Is.EqualTo((0f, 0f, 0f)));
            Assert.That(CommandPoints.MoveOptions(_context, _theirUnit, canMove: true, canCharge: true).Single().Stratagem,
                Is.EqualTo(CommandPoints.SweepingMove));
            Assert.That(CommandPoints.TakeMoveOption(_context, _theirUnit, CommandPoints.SweepingMove), Is.True);
            Assert.That(CommandPoints.MoveBonus(_theirUnit), Is.EqualTo((0f, 0f, 4f)));
        }

        [Test]
        public async Task StandStrong_ReRollsAFailedMoraleTest()
        {
            // Dice: two doctrine rolls (overwritten), the test (2 - misses by 2, so no High Command), the re-roll (5).
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 2, 5));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 5, theirs: 1);
            var outcome = await FDG.Stages.MoraleUtilities.TakeMoraleTest(_context, _myUnit, 4, FDG.Rules.Foundation.EMoraleTrigger.Shooting);
            Assert.That(outcome.Passed, Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(7));
        }

        [Test]
        public async Task Terrorize_MakesAPassReRoll()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 4, 2));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 1, theirs: 5);
            var outcome = await FDG.Stages.MoraleUtilities.TakeMoraleTest(_context, _myUnit, 4, FDG.Rules.Foundation.EMoraleTrigger.Shooting);
            Assert.That(outcome.Passed, Is.False, "passed on a 4, re-rolled a 2");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(2), "4 - 2");
        }

        [Test]
        public async Task CodeOfHonor_StopsShaken()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 5, theirs: 1);
            DataBinding<UnitData> mine = _myBinding;
            await FDG.Stages.MoraleUtilities.ApplyShakenWithPresentation(_context, mine);
            Assert.That(_myUnit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken), Is.False);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(5));
            CommandPoints.NewActivation(_context);
            await FDG.Stages.MoraleUtilities.RoutWithPresentation(_context, mine);
            Assert.That(_myUnit.GetIsAlive(), Is.True, "Shaken instead of Routed");
            Assert.That(_myUnit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Shaken), Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(2));
        }

        // ================================================================= doctrine stratagems, second half

        private void Place(UnitData unit, float x, float z) =>
            unit.Models.OfType<ModelData>().First().SetPosition(new Position(x, z));

        [Test]
        public async Task CombatFatigue_OnAFour_CountsAsFatigued_ButLeavesNoFatigue()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 5));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 1, theirs: 3);                               // they have Shock
            await CommandPoints.OfferCombatFatigue(_context, _myUnit);
            Assert.That(FDG.Stages.FatigueUtilities.CountsAsFatiguedInMelee(_myUnit), Is.True);
            FDG.Stages.FatigueUtilities.ApplyFatigued(_myUnit);
            Assert.That(_myUnit.Tokens.HasToken(FDG.Rules.Foundation.TokenType.Fatigued), Is.False, "doesn't get Fatigued from that melee");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(2));
            CommandPoints.EndOfActivation(_context);
            Assert.That(FDG.Stages.FatigueUtilities.CountsAsFatiguedInMelee(_myUnit), Is.False, "for that melee only");

            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 3));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 1, theirs: 3);
            await CommandPoints.OfferCombatFatigue(_context, _myUnit);
            Assert.That(FDG.Stages.FatigueUtilities.CountsAsFatiguedInMelee(_myUnit), Is.False, "a 3: no effect, CP still spent");
            Assert.That(CommandPoints.Of(_context, _them), Is.EqualTo(2));
        }

        [Test]
        public async Task EternalVigilance_IgnoresTheKillingWoundOnAFour()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 4, 3));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 2, theirs: 1);
            IModel m = _myUnit.Models.First();
            Assert.That(await CommandPoints.OfferEternalVigilance(_context, _myUnit, m), Is.True);
            CommandPoints.NewActivation(_context);
            Assert.That(await CommandPoints.OfferEternalVigilance(_context, _myUnit, m), Is.False, "a 3");
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(6));
            Assert.That(await CommandPoints.OfferEternalVigilance(_context, _theirUnit, _theirUnit.Models.First()), Is.False, "not their doctrine");
        }

        [Test]
        public async Task TotalShutdown_AndHinderedAdvance_BelongToTheirDoctrines()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 6, theirs: 1);
            Assert.That(await CommandPoints.OfferTotalShutdown(_context, _me, _them), Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(5));
            Assert.That(await CommandPoints.OfferTotalShutdown(_context, _them, _me), Is.False, "Strategic has no Total Shutdown");
            Assert.That(await CommandPoints.OfferHinderedAdvance(_context, _myUnit, Array.Empty<FDG.StageResolution.Requests.ModelMoveEntry>(),
                Array.Empty<ITerrain>()), Is.False, "no terrain crossed - nothing to offer");
        }

        [Test]
        public async Task TacticalRetreat_MovesDirectlyAway_WhenAnEnemyIsWithin12()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 6, 6));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 2, theirs: 1);
            Place(_myUnit, 30f, 20f);
            Place(_theirUnit, 30f, 40f);
            Assert.That(CommandPoints.CanTacticalRetreat(_context, _myUnit), Is.False, "the enemy is 20\" away");
            Place(_theirUnit, 30f, 28f);
            Assert.That(CommandPoints.CanTacticalRetreat(_context, _myUnit), Is.True);
            Assert.That(CommandPoints.MoveOptions(_context, _myUnit, canMove: false, canCharge: false).Select(o => o.Stratagem),
                Does.Contain(CommandPoints.TacticalRetreat));
            await CommandPoints.TakeTacticalRetreat(_context, _myUnit);       // 2D3+2 = 3+3+2 = 8"
            Assert.That(_myUnit.Models.First().Position.z, Is.EqualTo(12f).Within(0.01f), "straight away from the enemy");
            Assert.That(_myUnit.Models.First().Position.x, Is.EqualTo(30f).Within(0.01f));
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(5));
        }

        [Test]
        public async Task MinimizeLosses_StopsAtTheTableEdge()
        {
            Build(face: 4, CommandPoints.Fixed, answer: Says(true), roller: new FaceSequence(1, 1, 6, 6, 6, 6));
            await CommandPoints.Settle(_context);
            Doctrines(mine: 5, theirs: 1);
            Place(_myUnit, 30f, 5f);
            Place(_theirUnit, 30f, 12f);
            await CommandPoints.OfferMinimizeLosses(_context, _myUnit);       // 4D3+4 = 16", but the edge is ~4.5" away
            float r = _myUnit.Models.First().BaseShape.CircumscribedRadiusInches;
            Assert.That(_myUnit.Models.First().Position.z, Is.EqualTo(r).Within(0.05f), "stops at the edge, never leaves the table");
            Assert.That(_myUnit.GetIsAlive(), Is.True);
            Assert.That(CommandPoints.Of(_context, _me), Is.EqualTo(7));
        }
    }
}
