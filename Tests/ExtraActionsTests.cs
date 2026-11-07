using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Ai;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Extra Actions (Advanced Rulebooks 3.6.0, "Advanced Actions - Extra Actions"): the menu, the Shaken
    /// prompt, every modifier, the movement caps, the objective bar, and a real bot game with all eight on.
    /// </summary>
    [TestFixture]
    public class ExtraActionsTests
    {
        private GameDataStore _store = null!;
        private WoundTestContext _ctx = null!;
        private ActivationBackOutTests.ActionMenuRequester _requester = null!;
        private int _team;

        [SetUp]
        public void SetUp()
        {
            _store = GameDataStore.GameDataStoreBuilder.GetDefault();
            _requester = new ActivationBackOutTests.ActionMenuRequester();
            _ctx = new WoundTestContext(_store, _requester);
            GameSettings settings = GameSettings.GetDefault();
            settings.ExtraActions = EExtraActions.All;
            _ctx.Settings = settings;
        }

        // ---- the menu -------------------------------------------------------------------------------

        [Test]
        public async Task Off_TheMenuIsTheStandardOne()
        {
            GameSettings settings = _ctx.Settings;
            settings.ExtraActions = EExtraActions.None;
            _ctx.Settings = settings;
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ReplyOption = ChooseActionStage.PASS_CHOICE_NAME;

            await Run(unit);

            IEnumerable<string> all = _requester.LastActionRequest!.ValidOptions
                .Concat(_requester.LastActionRequest.InvalidOptions.Select(o => o.Option));
            Assert.That(all.Intersect(ExtraActions.All.Select(a => a.Name)), Is.Empty);
        }

        [Test]
        public async Task On_APristineMenuOffersThem_AndHeavyChargeNeedsATarget3To6Away()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ReplyOption = ChooseActionStage.PASS_CHOICE_NAME;

            await Run(unit);

            ChooseActionRequest menu = _requester.LastActionRequest!;
            Assert.That(menu.ValidOptions, Is.SupersetOf(new[]
            {
                ExtraActions.HunkerDown, ExtraActions.DefensiveStance, ExtraActions.FocusedFire,
                ExtraActions.StealthMove, ExtraActions.CoveringFire, ExtraActions.Overwatch,
            }));
            Assert.That(menu.ValidOptions, Does.Not.Contain(ExtraActions.LastStand), "Last Stand is for Shaken units");
            Assert.That(menu.InvalidOptions.Select(o => o.Option), Does.Contain(ExtraActions.HeavyCharge),
                "the enemy is 8.5\" away - too far for a Heavy Charge");
            Assert.That(menu.OptionDescriptions![ExtraActions.Overwatch], Does.Contain("24"));
        }

        [Test]
        public async Task HeavyCharge_IsOfferedAgainstAnEnemy3To6InchesAway()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(6f, 0f, 0f)); // 6 - 1.5 bases = 4.5" edge to edge
            _requester.ReplyOption = ExtraActions.HeavyCharge;

            var (context, routes) = await Run(unit);

            Assert.That(routes, Does.Contain("toMovement"));
            Assert.That(context.ExtraAction, Is.EqualTo(ExtraActions.HeavyCharge));
            Assert.That(context.HeavyChargeTargets, Has.Count.EqualTo(1));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.HeavyCharge), Is.True);
        }

        [Test]
        public async Task HunkerDown_EndsTheActivation_AndLeavesItsToken()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ReplyOption = ExtraActions.HunkerDown;

            var (context, routes) = await Run(unit);

            Assert.That(routes, Does.Contain("reconcile"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.HunkerDown), Is.True);
            Assert.That(context.ExtraAction, Is.EqualTo(ExtraActions.HunkerDown));
        }

        [Test]
        public async Task FocusedFire_GoesToTheShot_AndIsWithdrawnIfThePlayerBacksOut()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ActionReplies.Enqueue(ExtraActions.FocusedFire);

            var (context, routes) = await Run(unit);
            Assert.That(routes, Does.Contain("toShoot"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.FocusedFire), Is.True);

            // Back from the weapon menu without firing: the same menu again, declaration withdrawn.
            _requester.ActionReplies.Enqueue(ChooseActionStage.PASS_CHOICE_NAME);
            await Run(unit, context);
            Assert.That(context.ExtraAction, Is.Null);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.FocusedFire), Is.False);
            Assert.That(_requester.LastActionRequest!.ValidOptions, Does.Contain(ChooseActionStage.MOVEMENT_CHOICE_NAME));
        }

        [Test]
        public async Task FocusedFire_AfterTheShot_TheUnitMayNotMove()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ActionReplies.Enqueue(ExtraActions.FocusedFire);
            var (context, _) = await Run(unit);

            context.RegisterAttackedFinished();
            int menus = _requester.ActionMenuCount;
            var (_, routes) = await Run(unit, context);

            // Shot already, and a Hold may not move: nothing is left, so the activation ends without a menu.
            Assert.That(_requester.ActionMenuCount, Is.EqualTo(menus), "no Move after a Focused Fire");
            Assert.That(routes, Does.Contain("reconcile"));
            Assert.That(context.ExtraAction, Is.EqualTo(ExtraActions.FocusedFire), "a real shot keeps the declaration");
        }

        [Test]
        public async Task StealthMove_AfterTheMove_TheUnitMayNotShoot()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            _requester.ActionReplies.Enqueue(ExtraActions.StealthMove);
            var (context, routes) = await Run(unit);
            Assert.That(routes, Does.Contain("toMovement"));

            context.RegisterMoveFinished(3f, GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES);
            int menus = _requester.ActionMenuCount;
            var (_, after) = await Run(unit, context);

            // The enemy is in rifle range after a 3" move, so an ordinary Advance would be asked about the
            // shot. A Stealth Move may not shoot, so nothing is left and the activation just ends.
            Assert.That(_requester.ActionMenuCount, Is.EqualTo(menus), "no Shoot after a Stealth Move");
            Assert.That(after, Does.Contain("reconcile"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.StealthMove), Is.True);
        }

        // ---- Shaken -----------------------------------------------------------------------------------

        [Test]
        public async Task Shaken_MayHunkerDown_AndRecovers()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            _requester.ActionReplies.Enqueue(ExtraActions.HunkerDown);

            var (_, routes) = await Run(unit);

            Assert.That(_requester.LastActionRequest!.ValidOptions,
                Is.EquivalentTo(new[] { ExtraActions.LastStand, ExtraActions.HunkerDown, ExtraActions.DefensiveStance,
                    ChooseActionStage.PASS_CHOICE_NAME }));
            Assert.That(routes, Does.Contain("reconcile"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.HunkerDown), Is.True);
        }

        [Test]
        public async Task Shaken_LastStand_OpensTheRegularMenu_WithNoCastingAndNoBackOut()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            _requester.ActionReplies.Enqueue(ExtraActions.LastStand);
            _requester.ActionReplies.Enqueue(ChooseActionStage.SHOOT_CHOICE_NAME);

            var (context, routes) = await Run(unit);

            Assert.That(_requester.ActionMenuCount, Is.EqualTo(2), "the Shaken prompt, then the regular menu");
            Assert.That(_requester.LastActionRequest!.AllowCancel, Is.False);
            Assert.That(routes, Does.Contain("toShoot"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False);
            Assert.That(ExtraActions.InLastStand(unit.GetValue()), Is.True);
            Assert.That(context.StartedActivationShaken, Is.False);
        }

        [Test]
        public async Task Shaken_Pass_IsTheOrdinaryIdle()
        {
            DataBinding<UnitData> unit = MakeUnit(new Position(0f, 0f, 0f));
            MakeEnemy(new Position(10f, 0f, 0f));
            unit.GetValue().Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
            _requester.ActionReplies.Enqueue(ChooseActionStage.PASS_CHOICE_NAME);

            var (_, routes) = await Run(unit);

            Assert.That(routes, Does.Contain("reconcile"));
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.Shaken), Is.False, "recovers as usual");
            Assert.That(ExtraActions.AnyOn(_ctx), Is.True);
            Assert.That(unit.GetValue().Tokens.HasToken(TokenType.HunkerDown), Is.False);
        }

        // ---- modifiers --------------------------------------------------------------------------------

        [Test]
        public void HitModifiers_FollowTheBook()
        {
            UnitData shooter = MakeUnit(new Position(0f, 0f, 0f)).GetValue();
            UnitData target = MakeUnit(new Position(10f, 0f, 0f)).GetValue();

            ExtraActions.Mark(shooter, TokenType.FocusedFire);
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(shooter, target, false, false, 11f)), Is.EqualTo(1));
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(shooter, target, false, false, 13f)), Is.EqualTo(0),
                "Focused Fire is for targets within 12\"");
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(shooter, target, true, false, 1f)), Is.EqualTo(0),
                "and only when shooting");

            ExtraActions.Mark(target, TokenType.StealthMove);
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(shooter, target, false, false, 13f)), Is.EqualTo(-1),
                "Stealth Move: -1 from over 12\"");

            ExtraActions.Mark(shooter, TokenType.HeavyCharge);
            Assert.That(ExtraActions.Net(ExtraActions.HitModifiers(shooter, target, true, false, 1f)), Is.EqualTo(1));

            ExtraActions.Mark(shooter, TokenType.OverwatchFiring);
            ExtraActions.Mark(shooter, TokenType.LastStand);
            Assert.That(ExtraActions.HitModifiers(shooter, target, false, false, 5f).Select(m => m.Source),
                Is.EquivalentTo(new[] { ExtraActions.LastStand, ExtraActions.FocusedFire, ExtraActions.Overwatch }));
        }

        [Test]
        public void SaveModifiers_FollowTheBook()
        {
            UnitData unit = MakeUnit(new Position(0f, 0f, 0f)).GetValue();
            ExtraActions.Mark(unit, TokenType.HunkerDown);
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(unit, isMelee: false, isSpell: false)), Is.EqualTo(1));
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(unit, isMelee: true, isSpell: false)), Is.EqualTo(0));
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(unit, isMelee: false, isSpell: true)), Is.EqualTo(0),
                "a spell is not shooting");

            unit.Tokens.RemoveTokens(TokenType.HunkerDown);
            ExtraActions.Mark(unit, TokenType.DefensiveStance);
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(unit, isMelee: true, isSpell: false)), Is.EqualTo(1));
            Assert.That(ExtraActions.MayNotStrikeBack(unit), Is.True);

            ExtraActions.Mark(unit, TokenType.LastStand);
            Assert.That(ExtraActions.Net(ExtraActions.SaveModifiers(unit, isMelee: true, isSpell: false)), Is.EqualTo(0));
            Assert.That(ExtraActions.MoraleModifier(unit), Is.EqualTo(-1));
        }

        [Test]
        public void LastStand_CountsAsShakenForRules_ButNotForObjectivesOrTheCoreToken()
        {
            UnitData unit = MakeUnit(new Position(0f, 0f, 0f)).GetValue();
            ExtraActions.Mark(unit, TokenType.LastStand);

            Assert.That(unit.Tokens.HasToken(TokenType.Shaken), Is.False);
            Assert.That(ExtraActions.ShakenForSpecialRules(unit.Tokens, TokenType.Shaken), Is.EqualTo(1));
            Assert.That(ExtraActions.ShakenForSpecialRules(unit.Tokens, TokenType.Fatigued), Is.EqualTo(0));
            Assert.That(FDG.Ai.Tactician.TacticalAnalysis.CanSeizeObjectives(unit), Is.False);
        }

        [Test]
        public void Movement_StealthMoveIsAnAdvance_AndLastStandHalves()
        {
            DataBinding<UnitData> stealthy = MakeUnit(new Position(0f, 0f, 0f));
            ExtraActions.Mark(stealthy.GetValue(), TokenType.StealthMove);
            var stealth = new MovementActionContext(_ctx, stealthy);
            Assert.That(stealth.MaxRushDistance, Is.EqualTo(stealth.MaxAdvanceDistance));
            Assert.That(stealth.MaxChargeDistance, Is.EqualTo(stealth.MaxAdvanceDistance));

            DataBinding<UnitData> last = MakeUnit(new Position(20f, 0f, 0f));
            ExtraActions.Mark(last.GetValue(), TokenType.LastStand);
            var halved = new MovementActionContext(_ctx, last);
            Assert.That(halved.MaxAdvanceDistance, Is.EqualTo(GameWideConstants.MOVE_SHOOT_DISTANCE_INCHES / 2f).Within(0.01f));
            Assert.That(halved.MaxRushDistance, Is.EqualTo(GameWideConstants.RUSH_DISTANCE_INCHES / 2f).Within(0.01f));
        }

        [Test]
        public void TheirStateClearsAtTheNextActivation_AndTheQuestSystemsHaveNone()
        {
            UnitData unit = MakeUnit(new Position(0f, 0f, 0f)).GetValue();
            foreach (TokenType t in new[] { TokenType.LastStand, TokenType.HunkerDown, TokenType.DefensiveStance,
                         TokenType.StealthMove, TokenType.Overwatch })
                ExtraActions.Mark(unit, t);
            ExtraActions.ClearAtActivation(unit);
            Assert.That(unit.Tokens.GetAllTokens().Any(), Is.False);

            GameSettings quest = GameSettings.GetDefault();
            quest.ExtraActions = EExtraActions.All;
            quest.GameSystem = EGameSystem.GFStarQuest;
            Assert.That(ExtraActions.Enabled(quest), Is.EqualTo(EExtraActions.None));
        }

        // ---- a real game ------------------------------------------------------------------------------

        /// <summary>The official bot on both sides with all eight on: the game ends, and the bot used them.</summary>
        [TestCase(EExtraActions.All, 9100)]
        [TestCase(EExtraActions.HunkerDown | EExtraActions.DefensiveStance | EExtraActions.FocusedFire, 9200)]
        [CancelAfter(300_000)]
        public async Task BotGame_WithExtraActions_PlaysToTheEnd(EExtraActions extras, int seed)
        {
            var store = GameDataStore.GameDataStoreBuilder.GetDefault();
            var bus = new InProcessBus();
            var log = new List<string>();
            var slots = new PlayerSlot[2];
            string[] armies = { "GDF/1k - Alien Hives.fdgarmy", "GDF/1k - Human Defense Force.fdgarmy" };
            for (int i = 0; i < 2; i++)
            {
                slots[i] = new PlayerSlot(i, teamNumber: i, new PlayerID(Guid.NewGuid()),
                    SoloAiAuditPlaythroughs.LoadArmy(armies[i]), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                var registry = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, aiGame.TableState,
                    slots[i].PlayerID, seed + i, slots[i].SlotID);
                IPlayerController ai = new ComputerPlayerController($"AI {i}", slots[i].PlayerID, aiGame, registry);
                slots[i].AssignPlayerController(i == 0 ? new LogTap(ai, log) : ai);
            }
            GameSettings settings = GameSettings.GetDefault();
            settings.RandomnessType = ERandomnessType.Probabilistic;
            settings.DiceSeed = seed;
            settings.ExtraActions = extras;

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, settings, slots);
            server.OnGameCompleted += r => completed.TrySetResult(r);
            Assert.That(await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(280))), Is.SameAs(completed.Task),
                "the game must finish");

            string all;
            lock (log) all = string.Join("\n", log);
            TestContext.Out.WriteLine(string.Join("\n", log.Where(l => l.Contains("Overwatch") || l.Contains("Hunker")
                || l.Contains("Focused Fire") || l.Contains("Heavy Charge") || l.Contains("Defensive Stance")
                || l.Contains("Covering Fire")).Take(60)));
            Assert.That(all, Does.Contain("uses Focused Fire").Or.Contain("declares a Heavy Charge")
                .Or.Contain("uses Overwatch").Or.Contain("uses Hunker Down"),
                "the bot should have taken at least one Extra Action");
        }

        /// <summary>
        /// Overwatch end to end, through the real stages and the official bot on both seats: the Reds' Troops
        /// shoot the Blues' Troops, which are on Overwatch 20" away, and the Blues shoot back at -1.
        /// </summary>
        [TestCase(38f, TestName = "Overwatch_ShotAt_ShootsBack")]
        [CancelAfter(120_000)]
        public async Task Overwatch_Reacts(float bluesX)
        {
            var scenario = new ScenarioFile
            {
                Name = "Overwatch",
                Round = 4,
                ActivePlayer = 0,
                Settings = new ScenarioSettings { Randomness = "Realistic", DiceSeed = 4400,
                    ExtraActions = new() { "Overwatch" } },
                Players = new()
                {
                    new ScenarioPlayer
                    {
                        Army = "reds.fdgarmy",
                        Units = new() { new ScenarioUnit { Unit = "Troops",
                            Models = new() { new[] { 20f, 20f }, new[] { 21f, 20f }, new[] { 22f, 20f } } } },
                    },
                    new ScenarioPlayer
                    {
                        Army = "blues.fdgarmy",
                        Units = new() { new ScenarioUnit { Unit = "Troops",
                            Models = new() { new[] { bluesX, 26f }, new[] { bluesX + 1f, 26f }, new[] { bluesX + 2f, 26f } },
                            Tokens = new() { new ScenarioToken { Type = "Overwatch" } } } },
                    },
                },
            };
            GameDataStore store = ScenarioCompiler.Compile(scenario, new[] { TroopsArmy("Reds"), TroopsArmy("Blues") });
            List<PlayerSlotInfo> infos = store.GetAllValues<PlayerSlotInfo>().OrderBy(i => i.SlotID).ToList();
            foreach (DataReference old in store.GetAllDataReferences<PlayerSlotInfo>().ToList()) store.Destroy(old);

            var bus = new InProcessBus();
            var log = new List<string>();
            var slots = new PlayerSlot[infos.Count];
            for (int i = 0; i < infos.Count; i++)
            {
                slots[i] = new PlayerSlot(i, infos[i].TeamNumber, infos[i].PlayerID, new ArmyListFile(), store);
                var aiGame = new FDGGame_AsLocal(store, bus);
                IPlayerController ai = new ComputerPlayerController($"AI {i}", infos[i].PlayerID, aiGame,
                    AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, aiGame.TableState, infos[i].PlayerID, 4400 + i, i));
                slots[i].AssignPlayerController(i == 0 ? new LogTap(ai, log) : ai);
            }

            var completed = new TaskCompletionSource<GameResult>();
            var server = new FDGServer(store, bus, slots);
            server.OnGameCompleted += r => completed.TrySetResult(r);
            Assert.That(await Task.WhenAny(completed.Task, Task.Delay(TimeSpan.FromSeconds(100))), Is.SameAs(completed.Task));

            string all;
            lock (log) all = string.Join("\n", log);
            Assert.That(all, Does.Contain("reacts from Overwatch, shooting at"), all);
        }

        /// <summary>
        /// The movement half, driven directly: the stage that runs after a move offers the watcher its shot,
        /// fires it through the real shooting pipeline (the official bot's resolvers answering), spends the
        /// Overwatch, and on re-entry finishes.
        /// </summary>
        [Test]
        public async Task Overwatch_AfterAnEnemyMove_FiresOnce_ThenFinishes()
        {
            var lazy = new LazyRequester();
            _ctx = new WoundTestContext(_store, lazy);
            GameSettings settings = GameSettings.GetDefault();
            settings.ExtraActions = EExtraActions.Overwatch;
            _ctx.Settings = settings;

            PlayerID moverPlayer = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> mover = MakeUnit(moverPlayer, new Position(10f, 0f, 10f));
            _store.Create(new ArmyData(moverPlayer, new List<DataBinding<UnitData>> { mover }));
            PlayerID watcherPlayer = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> watcher = MakeUnit(watcherPlayer, new Position(22f, 0f, 10f));
            _store.Create(new ArmyData(watcherPlayer, new List<DataBinding<UnitData>> { watcher }));
            ExtraActions.Mark(watcher.GetValue(), TokenType.Overwatch);

            lazy.Inner = new FDG.Simulation.DirectPlayerRequester(new Dictionary<PlayerID, FDG.StageResolution.IStageResolverRegistry>
            {
                [moverPlayer] = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, _ctx.TableState, moverPlayer, 1, 0),
                [watcherPlayer] = AiProfileFactory.BuildRegistry(EAiProfile.SoloCoOp, _ctx.TableState, watcherPlayer, 2, 1),
            });

            var context = new UnitActionContext(_ctx, mover);
            context.Reset(mover);
            var stage = new OverwatchStage(_ctx, new NoOpLayer<IUnitActionContext>(), OverwatchStage.ETrigger.Movement);
            bool finished = false;
            stage.ToFinished.Bind("done");
            stage.OnShotDone.Bind("shotDone");
            stage.ToFinished.OnWillActivate += _ => finished = true;
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(watcher, _ctx), Is.True,
                "set-up: the watcher can see the mover");
            Assert.That(ChooseRangedAttackStage.HasAnyFireableTarget(watcher, _ctx, onlyTarget: mover.GetValue().ID), Is.True,
                "set-up: the watcher can see the mover, as its only target");

            await stage.Enter(context);
            Assert.That(watcher.GetValue().Tokens.HasToken(TokenType.Overwatch), Is.False, "the reaction is spent");
            Assert.That(watcher.GetValue().Tokens.HasToken(TokenType.OverwatchFiring), Is.True, "it fired, at -1");
            Assert.That(finished, Is.False);

            await stage.Enter(context); // OnShotDone re-enters the stage
            Assert.That(watcher.GetValue().Tokens.HasToken(TokenType.OverwatchFiring), Is.False);
            Assert.That(finished, Is.True, "nobody else is on Overwatch");
        }

        private sealed class LazyRequester : IPlayerRequestByID
        {
            public IPlayerRequestByID? Inner { get; set; }
            public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
                where TRequest : FDG.StageResolution.IStageTaskRequest<TReply> =>
                Inner!.RequestDecision<TRequest, TReply>(request);
        }

        private static ArmyListFile TroopsArmy(string name) => new ArmyListFile
        {
            Name = name,
            Units = new()
            {
                new UnitFileEntry
                {
                    Name = "Troops", ModelCount = 3, Quality = 4, Defense = 4,
                    Weapons = new() { new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 } },
                },
            },
        };

        private sealed class LogTap : IPlayerController
        {
            private readonly IPlayerController _inner;
            private readonly List<string> _lines;
            internal LogTap(IPlayerController inner, List<string> lines) { _inner = inner; _lines = lines; }
            public string Name => _inner.Name;
            public PlayerID ID => _inner.ID;
            public bool IsReady => _inner.IsReady;
            public IPresentationSink? PresentationSink => _inner.PresentationSink;
            public event Action<bool> OnReadyStateChanged { add => _inner.OnReadyStateChanged += value; remove => _inner.OnReadyStateChanged -= value; }
            public event Action<PlayerID, EChatMessageType, string> OnMessageSentByPlayer { add => _inner.OnMessageSentByPlayer += value; remove => _inner.OnMessageSentByPlayer -= value; }
            public Task WaitUntilReadyAsync() => _inner.WaitUntilReadyAsync();
            public void SendLogMessage(string logMessage, TextColor color) { lock (_lines) _lines.Add(logMessage); _inner.SendLogMessage(logMessage, color); }
            public void SendPlayerMessage(string n, EChatMessageType t, string m) => _inner.SendPlayerMessage(n, t, m);
        }

        // ---- helpers ----------------------------------------------------------------------------------

        private async Task<(UnitActionContext Context, List<string> Routes)> Run(DataBinding<UnitData> unit,
            UnitActionContext? existing = null)
        {
            var context = existing ?? new UnitActionContext(_ctx, unit);
            if (existing == null) context.Reset(unit);
            var routes = new List<string>();
            var stage = new ChooseActionStage(_ctx, new NoOpLayer<IUnitActionContext>());
            stage.ToBackOut.Bind("backOut");
            stage.ToMovement.Bind("toMovement");
            stage.ToShoot.Bind("toShoot");
            stage.ToCharge.Bind("toCharge");
            stage.ToReconcileEndOfActivation.Bind("reconcile");
            stage.ToMovement.OnWillActivate += _ => routes.Add("toMovement");
            stage.ToShoot.OnWillActivate += _ => routes.Add("toShoot");
            stage.ToCharge.OnWillActivate += _ => routes.Add("toCharge");
            stage.ToReconcileEndOfActivation.OnWillActivate += _ => routes.Add("reconcile");
            await stage.Enter(context);
            return (context, routes);
        }

        private void MakeEnemy(Position at)
        {
            PlayerID enemy = new PlayerID(Guid.NewGuid());
            DataBinding<UnitData> unit = MakeUnit(enemy, at);
            _store.Create(new ArmyData(enemy, new List<DataBinding<UnitData>> { unit }));
        }

        private DataBinding<UnitData> MakeUnit(Position at) => MakeUnit(new PlayerID(Guid.NewGuid()), at);

        private DataBinding<UnitData> MakeUnit(PlayerID player, Position at)
        {
            _store.Create(new TeamData(_team++, new List<PlayerID> { player }));
            var weapons = new List<Weapon>
            {
                new Weapon("Blade", rangeInches: 0f, attacks: 1, armorPenetration: 0),
                new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0),
            };
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: weapons, initialPosition: at, gameDataStore: _store);
            var models = new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) };
            var unit = new UnitData(player, "TestUnit", quality: 4, defense: 4, modelBindings: models);
            return _store.GetDataBinding<UnitData>(_store.Create(unit));
        }
    }
}
