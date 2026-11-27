using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Fog of War (Advanced Rulebooks 3.6.0: GF p46, GFF p43, AoF p46, AoFS p43).</summary>
    [TestFixture]
    public class FogOfWarTests
    {
        private GameDataStore _store = null!;
        private TestGameContext _context = null!;
        private PlayerID _me, _them;

        private sealed class Answer : IPlayerRequestByID
        {
            private readonly Func<object, object> _answer;
            public Answer(Func<object, object> answer) => _answer = answer;
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request) where TRequest : IStageTaskRequest<TReply> =>
                Task.FromResult((TReply)_answer(request!));
        }

        private void Build(int face, EAdvancedRules rules, EGameSystem system = EGameSystem.GrimdarkFuture, Func<object, object>? answer = null)
        {
            GameWideConstants.ConfigureTable(system);
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            GameSettings settings = GameSettings.GetDefault();
            settings.GameSystem = system;
            settings.AdvancedRules = rules;
            _context = new TestGameContext(_store, new FixedFaceDiceRoller(face), settings: settings,
                playerRequester: answer == null ? null : new Answer(answer));
            _me = new PlayerID(Guid.NewGuid());
            _them = new PlayerID(Guid.NewGuid());
            _store.Create(new TeamData(1, new List<PlayerID> { _me }));
            _store.Create(new TeamData(2, new List<PlayerID> { _them }));
        }

        [TearDown]
        public void Reset() => GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);

        private UnitData Unit(PlayerID owner, Position at)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
            var bindings = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(owner, $"Unit{_store.GetAllValues<UnitData>().Count() + 1}", quality: 4, defense: 4, modelBindings: bindings);
            DataBinding<UnitData> binding = _store.GetDataBinding<UnitData>(_store.Create(unit));
            _store.Create(new ArmyData(owner, new List<DataBinding<UnitData>> { binding }));
            return binding.GetValue();
        }

        [Test]
        public void TheChapter_IsInAllFourGames_NotQuest()
        {
            foreach (EGameSystem s in new[] { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish })
                Assert.That(AdvancedRules.Chapters(s), Does.Contain(AdvancedRules.FogOfWarChapter), s.ToString());
            Assert.That(AdvancedRules.Chapters(EGameSystem.GFStarQuest), Does.Not.Contain(AdvancedRules.FogOfWarChapter));
            Assert.That(AdvancedRules.In(AdvancedRules.FogOfWarChapter, EGameSystem.GrimdarkFuture).Count(), Is.EqualTo(8), "six, plus Random Arrival and Flexible Strategies");
            Assert.That((long)EAdvancedRules.EbbAndFlow & (long)BattlefieldEffects.PickMask, Is.EqualTo(0), "clear of the Special Condition pick bits");
        }

        [Test]
        public void TheTables_AreTheBooks()
        {
            Assert.That(new[] { 1, 2, 3, 4, 5 }.Select(FogOfWar.PassOn), Is.EqualTo(new int?[] { 5, 4, 3, 2, null }));
            Assert.That(new[] { 4, 5, 6, 7 }.Select(FogOfWar.ContinueOn), Is.EqualTo(new[] { 4, 5, 6, 6 }));
            Assert.That(FogOfWar.Sections(EGameSystem.AgeOfFantasy), Is.EqualTo(3));
            Assert.That(FogOfWar.Sections(EGameSystem.GFFirefight), Is.EqualTo(2));
            Assert.That(Enumerable.Range(1, 6).Select(d => FogOfWar.SectionFromDie(EGameSystem.GrimdarkFuture, d)), Is.EqualTo(new[] { 1, 1, 2, 2, 3, 3 }));
            Assert.That(Enumerable.Range(1, 6).Select(d => FogOfWar.SectionFromDie(EGameSystem.AoFSkirmish, d)), Is.EqualTo(new[] { 1, 1, 1, 2, 2, 2 }));
        }

        [Test]
        public async Task ProlongedBattle_ASuccessAddsARound_AFailureEndsIt()
        {
            Build(face: 4, EAdvancedRules.ProlongedBattle);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4));
            await FogOfWar.ProlongedRoll(_context, 3, 4);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4), "nothing before round 4");
            await FogOfWar.ProlongedRoll(_context, 4, 4);   // 4 on a 4+: continue
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(5));
            await FogOfWar.ProlongedRoll(_context, 5, 5);   // 4 on a 5+: the game ends
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(5));
            Assert.That(FogOfWar.State(_store)!.Ended, Is.True);

            Build(face: 4, EAdvancedRules.None);
            await FogOfWar.ProlongedRoll(_context, 4, 4);
            Assert.That(HordeRules.RoundLimit(_context.Settings, _store), Is.EqualTo(4), "off");
        }

        [Test]
        public async Task CombatHesitation_RollsAndPasses_OnlyWhenBehind()
        {
            Build(face: 4, EAdvancedRules.CombatHesitation, answer: _ => true);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 1, opponentHasMore: true), Is.False, "4 doesn't make 5+");
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: true), Is.True, "4 makes 4+");
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: false), Is.False, "not behind");
            Build(face: 1, EAdvancedRules.CombatHesitation, answer: _ => true);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 5, opponentHasMore: true), Is.True, "no roll after round 4");
            Build(face: 6, EAdvancedRules.CombatHesitation, answer: _ => false);
            Assert.That(await FogOfWar.Hesitate(_context, _me, 2, opponentHasMore: true), Is.False, "declined");
        }

        [Test]
        public void ShiftingFocus_PicksTheRolledSection_ThenMovesOn()
        {
            Build(face: 3, EAdvancedRules.ShiftingFocus);   // D3 from a 3 = section 2
            float w = HordePlacement.TableWidth;
            UnitData left = Unit(_me, new Position(w * 0.1f, 6f)), middle = Unit(_me, new Position(w * 0.5f, 6f));
            var focus = FogOfWar.Focus(_context, _me, new List<IUnit> { left, middle });
            Assert.That(focus!.Value.Section, Is.EqualTo(2));
            Assert.That(focus.Value.Units, Is.EquivalentTo(new[] { middle.ID }));

            Build(face: 3, EAdvancedRules.ShiftingFocus);
            UnitData only = Unit(_me, new Position(w * 0.9f, 6f));
            focus = FogOfWar.Focus(_context, _me, new List<IUnit> { only });
            Assert.That(focus!.Value.Section, Is.EqualTo(3), "section 2 empty - on to 3");

            Build(face: 3, EAdvancedRules.None);
            Assert.That(FogOfWar.Focus(_context, _me, new List<IUnit> { Unit(_me, new Position(1f, 1f)) }), Is.Null);
        }
    
        [Test]
        public void Escalating_SplitsIntoFourCloseDetachments_AndLastsSixRounds()
        {
            Build(face: 4, EAdvancedRules.EscalatingBattles);
            int[] costs = { 300, 250, 200, 150, 120, 100, 90, 60, 40, 20 };
            var units = costs.Select(p => { UnitData u = Unit(_me, new Position(1f, 1f)); u.PointCost = p; return u; }).ToList();
            var split = FogOfWar.Split(units);
            Assert.That(split, Has.Count.EqualTo(4));
            Assert.That(split.Sum(g => g.Count), Is.EqualTo(units.Count));
            var totals = split.Select(g => g.Sum(u => u.PointCost)).ToList();
            Assert.That(totals.Max() - totals.Min(), Is.LessThanOrEqualTo(100), string.Join(",", totals));
            Assert.That(HordeRules.RoundLimit(_context.Settings), Is.EqualTo(6));
        }

        // Flexible Strategies (GF p46, GFF p43, AoF p46, AoFS p43): "Before the game begins, players build armies worth
        // +50% as many points, and divide them into six detachments of equal point value instead. Detachments are
        // deployed each round until the fourth, leaving two detachments out of the game."
        [Test]
        public void FlexibleStrategies_ArmiesAreHalfAsBigAgain_InSixDetachments()
        {
            const EAdvancedRules both = EAdvancedRules.EscalatingBattles | EAdvancedRules.EscalatingFlexibleStrategies;
            Assert.That(FogOfWar.ArmyPointsLimit(2000, both, EGameSystem.GrimdarkFuture, EGameMode.Standard), Is.EqualTo(3000));
            Assert.That(FogOfWar.ArmyPointsLimit(300, both, EGameSystem.AoFSkirmish, EGameMode.Standard), Is.EqualTo(450));
            Assert.That(FogOfWar.ArmyPointsLimit(2000, EAdvancedRules.EscalatingBattles, EGameSystem.GrimdarkFuture, EGameMode.Standard),
                Is.EqualTo(2000), "Escalating Battles alone");
            Assert.That(FogOfWar.ArmyPointsLimit(2000, EAdvancedRules.EscalatingFlexibleStrategies, EGameSystem.GrimdarkFuture, EGameMode.Standard),
                Is.EqualTo(2000), "the option needs Escalating Battles");
            Assert.That(FogOfWar.ArmyPointsLimit(2000, both, EGameSystem.GrimdarkFuture, EGameMode.Horde), Is.EqualTo(2000), "not in Horde");

            Build(face: 4, both);
            Assert.That(FogOfWar.DetachmentCount(_context.Settings), Is.EqualTo(6));
            var units = Enumerable.Range(1, 12).Select(i => { UnitData u = Unit(_me, new Position(1f, 1f)); u.PointCost = 10 * i; return u; }).ToList();
            Assert.That(FogOfWar.Split(units, FogOfWar.DetachmentCount(_context.Settings)), Has.Count.EqualTo(6));
            Build(face: 4, EAdvancedRules.EscalatingBattles);
            Assert.That(FogOfWar.DetachmentCount(_context.Settings), Is.EqualTo(4));
        }

        private async Task<List<UnitData>> PlayRoundsTwoToSix(EAdvancedRules rules)
        {
            int placed = 0;
            Build(face: 4, rules, answer: req => req switch
            {
                StringSelectionRequest pick => pick.Recommended!,
                YesNoRequest => false,
                PlaceObjectsRequest<ModelData> place => new Selected<List<PlacedObjectEntry<ModelData>>>(place.ModelsToPlace
                    .Select(m => new PlacedObjectEntry<ModelData>(m, new Position(4f + 6f * placed++, 6f))).ToList()),
                _ => throw new InvalidOperationException("Unexpected request " + req.GetType().Name),
            });
            // Detachment 1 is on the table; 2-6 wait (a unit's HeldDetachment tokens are its detachment's number).
            var waiting = new List<UnitData>();
            for (int k = 2; k <= 6; k++)
            {
                UnitData u = Unit(_me, new Position(0f, 0f));
                u.PointCost = 100 * k;
                ReserveRules.PlaceInReserve(u);
                for (int i = 0; i < k; i++) u.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.HeldDetachment));
                waiting.Add(u);
            }
            for (int round = 2; round <= 6; round++) await FogOfWar.EscalatingRoundStart(_context, round);
            return waiting;
        }

        [Test]
        public async Task FlexibleStrategies_DetachmentsComeOnUntilRoundFour_TheLastTwoStayOut()
        {
            var waiting = await PlayRoundsTwoToSix(EAdvancedRules.EscalatingBattles | EAdvancedRules.EscalatingFlexibleStrategies);
            Assert.That(waiting.Count(u => !ReserveRules.IsInReserve(u)), Is.EqualTo(3), "one each in rounds 2, 3 and 4");
            Assert.That(waiting.Where(u => !ReserveRules.IsInReserve(u)).Select(u => u.PointCost), Is.EquivalentTo(new[] { 600, 500, 400 }),
                "the bot brings the biggest detachment each time");
            Assert.That(waiting.Count(u => ReserveRules.IsInReserve(u) && u.Tokens.HasToken(TokenType.HeldDetachment)), Is.EqualTo(2),
                "two detachments left out of the game");

            waiting = await PlayRoundsTwoToSix(EAdvancedRules.EscalatingBattles);
            Assert.That(waiting.Count(u => !ReserveRules.IsInReserve(u)), Is.EqualTo(5), "without the option, one comes on every round");
        }

        [Test]
        public void SurpriseEngagement_SectionsAreEqualStripsOfTheLongSide()
        {
            var zone = new RectangularZone(0f, 72f, 0f, 12f);
            var third = (RectangularZone)FogOfWar.Section(zone, 3, 2);
            Assert.That((third.Left, third.Right, third.Bottom, third.Top), Is.EqualTo((24f, 48f, 0f, 12f)));
            var tall = new RectangularZone(0f, 12f, 0f, 48f);
            var half = (RectangularZone)FogOfWar.Section(tall, 2, 1);
            Assert.That((half.Left, half.Right, half.Bottom, half.Top), Is.EqualTo((0f, 12f, 0f, 24f)));
            var poly = new PolygonZone(new[] { new float[] { 0, 0, 72, 0, 72, 12, 0, 12 } });
            IBoundedZone clipped = FogOfWar.Section(poly, 3, 1);
            Assert.That(clipped, Is.InstanceOf<ClippedZone>());
            Assert.That(clipped.IsPointWithinZone(new Float2(10f, 6f)), Is.True);
            Assert.That(clipped.IsPointWithinZone(new Float2(30f, 6f)), Is.False);
        }
    }
}
