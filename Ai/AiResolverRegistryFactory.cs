using FDG.Ai.Resolvers;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.StageResolution;

namespace FDG.Ai
{
    public static class AiResolverRegistryFactory
    {
        // Set to true to insert a 1-second pause before each AI decision, useful for
        // watching the AI play in real time.
        public const bool SlowMode = false;
        private const int SlowModeDelayMs = 1000;

        /// <param name="seed">
        /// The GAME's seed (<see cref="GameSettings.DiceSeed"/>), not a per-player one — this method
        /// derives the player's own stream from it via <paramref name="slotID"/>, so two AI players never
        /// share a sequence and a seeded game replays identically (#193). Null = unseeded, as before.
        /// </param>
        /// <param name="slotID">The player's slot index. Stable across runs and across save/resume, unlike the PlayerID GUID.</param>
        /// <param name="flavour">
        /// #419: which rulebook's decisions to carry. <see cref="ESoloFlavour.Baseline"/> (the default) is
        /// the frozen benchmark baseline every existing caller wants - Gunline, the Tactician's underlay and
        /// the sandbox all build on it, and plan D1 forbids changing its behaviour.
        /// </param>
        public static IStageResolverRegistry BuildSoloRules(ITableState tableState, PlayerID playerID,
            int? seed = null, int slotID = 0, ESoloFlavour flavour = ESoloFlavour.Baseline)
        {
            bool starQuest = flavour == ESoloFlavour.StarQuest;
            bool book = flavour == ESoloFlavour.SoloCoOp;
            int? playerSeed = GameRandom.DeriveForSlot(seed, slotID);
            Random? objectiveRng = playerSeed.HasValue ? GameRandom.Create(playerSeed, salt: 1) : null;
            Random? terrainRng = playerSeed.HasValue ? GameRandom.Create(playerSeed, salt: 2) : null;
            // #419: the D3 for the Solo & Co-Op activation order, on its own salt so adding it does not
            // shift the objective or terrain streams and re-roll every pinned benchmark.
            Random activationRng = playerSeed.HasValue
                ? GameRandom.Create(playerSeed, salt: 3)! : new Random();
            // #419: the group-to-section rolls, on their own salt and rolled once per game. Held here
            // because the engine deploys one unit at a time and the three groups must agree across all of
            // those separate requests.
            Random deploymentRng = playerSeed.HasValue
                ? GameRandom.Create(playerSeed, salt: 4)! : new Random();
            var deploymentPlan = new Solo.SoloCoOpDeploymentPlan(sides => deploymentRng.Next(sides) + 1);

            // #358: one latch per solo set - the path resolver's main-move decline tells the
            // action menu to skip the movement family once, breaking the decline-repick livelock.
            var declineLatch = new SoloMoveDeclineLatch();
            var chargeMemory = new Solo.SoloChargeMemory();

            // #423: the Caster clause's D3. Its own salt so adding it does not shift the deployment or
            // activation rolls of a seed that was already played.
            Random spellRng = playerSeed.HasValue
                ? GameRandom.Create(playerSeed, salt: 7)! : new Random();

            // #191 A5-10: the UnitData selection resolver asks the rule graph which deploy-order
            // options are transports (they go down first, so later cargo can be offered the ride).
            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());

            var stringSelection = new AiStringSelectionResolver(tableState, playerID, declineLatch);

            // #423: the official bot must hold every Ambush unit in reserve ("AI units with Ambush must
            // always be kept in reserve"), which is one prompt out of the many this resolver answers - so
            // the book flavour gets a decorator over it rather than a second copy of the rest. The
            // baseline keeps "deploy normally", which plan D1 freezes.
            IStageResolver<StageResolution.Requests.StringSelectionRequest, string> stringSelectionForFlavour =
                book ? new Solo.SoloCoOpStringSelectionResolver(stringSelection) : stringSelection;
            var questMemory = new StarQuest.StarQuestAiMemory();

            IStageResolverRegistry registry = new StageResolverRegistry()
                .RegisterResolver(new AiYesNoResolver())
                .RegisterResolver<StageResolution.Requests.StringSelectionRequest, string>(stringSelectionForFlavour)
                // #191 B1 step 5a: Choose Action is its own request type; the same instance answers it.
                // #419: the official bot answers Choose Action from the Solo & Co-Op decision tree. The
                // baseline keeps the plain ranked menu, which plan D1 freezes.
                .RegisterResolver<StageResolution.Requests.ChooseActionRequest, string>(starQuest
                    ? new StarQuest.StarQuestActionResolver(tableState, playerID, declineLatch, stringSelection, questMemory)
                    : book
                        ? new Solo.SoloCoOpActionResolver(tableState, playerID, declineLatch, stringSelection, chargeMemory)
                        : stringSelection)
                .RegisterResolver(new AiChooseAbilityEffectResolver())
                // #423: the official bot picks its spell the book's way - a random spell on D3+X, cycling
                // until one is castable. The baseline keeps "the first castable spell", which plan D1
                // freezes. The roll shares the deployment RNG's seed stream so a seeded game replays.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestSpellResolver(playerSeed)
                    : book
                        ? new Solo.SoloCoOpSpellResolver(() => spellRng.Next(3) + 1)
                        : (IStageResolver<StageResolution.Requests.ChooseSpellRequest, StageResolution.Requests.ChooseSpellReply>)new AiChooseSpellResolver())
                .RegisterResolver(new AiCastAssistResolver())
                .RegisterResolver(new AiChooseDeploymentZoneResolver())
                // #419: the book's shooting priority for the official bot, the frozen ordering for the
                // baseline. The Quest resolver wraps the book one for bot-run heroes - closest-first is
                // the right policy for them too, and it is what B16's measured hero runs used.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestRangedAttackResolver()
                    : book
                        ? new Solo.SoloCoOpRangedAttackResolver(tableState)
                        : (IStageResolver<StageResolution.Requests.ChooseRangedAttackRequest,
                            CancellableResult<StageResolution.Requests.ChooseRangedAttackRequest.RangedAttackChoice>>)new AiChooseRangedAttackResolver())
                // #423: every unit-targeting pick an ABILITY raises comes through this request type, and
                // the General Special Rules clause wants "the closest unit that makes sense" - so the
                // official bot answers it with distance where the baseline answers it with the
                // melee-defender heuristic (fewest living models), which plan D1 freezes. The
                // melee-defender request itself keeps that heuristic in both flavours: it is its own
                // request type, adapted below, and the book's Melee clause is about which enemy to CHARGE
                // rather than which to fight once several are in contact.
                .RegisterResolver(book
                    ? new Solo.SoloCoOpAbilityTargetResolver(tableState)
                    : (IStageResolver<StageResolution.Requests.CancellableSelectionRequest<UnitData>,
                        StageResolution.CancellableResult<Data.DataBinding<UnitData>>>)
                    new AiChooseMeleeDefenderResolver())
                .RegisterResolver(new DerivedRequestAdapter<StageResolution.Requests.ChooseMeleeDefenderRequest,
                    StageResolution.Requests.CancellableSelectionRequest<UnitData>,
                    StageResolution.CancellableResult<Data.DataBinding<UnitData>>>(
                    book
                        ? new Solo.SoloCoOpMeleeDefenderResolver(tableState, chargeMemory)
                        : (IStageResolver<StageResolution.Requests.CancellableSelectionRequest<UnitData>,
                            StageResolution.CancellableResult<Data.DataBinding<UnitData>>>)new AiChooseMeleeDefenderResolver()))
                // #439: its own resolver rather than an adapter onto an existing unit picker - "which
                // units get my banner" is a multi-select, and no other request in the engine is one.
                .RegisterResolver(new AiPickCommandGroupUnitsResolver())
                .RegisterResolver(new AiPickUnitLeadersResolver())   // #456
                .RegisterResolver(new AiPickStringsResolver())       // #462
                .RegisterResolver(new AiConcedeCheckResolver())      // #466
                // #419: and moves to where that tree is heading, which for four of its five branches is an
                // objective rather than an enemy.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestMovementResolver(tableState, playerID, declineLatch, questMemory)
                    : book
                        ? new Solo.SoloCoOpMovementResolver(tableState, playerID, declineLatch, chargeMemory)
                        : (IStageResolver<StageResolution.Requests.DefineMovementPathRequest,
                            CancellableResult<List<StageResolution.Requests.ModelMoveEntry>>>)new AiDefineMovementResolver(tableState, playerID, declineLatch))
                .RegisterResolver(new AiAircraftAdvanceResolver())
                .RegisterResolver(new AiConsolidationMoveResolver(tableState, playerID))
                .RegisterResolver(new AiAssignWoundsResolver())
                // #419: the official bot deploys one random unit at a time, with Scout held until last.
                // The baseline keeps "first option offered", which plan D1 freezes.
                .RegisterResolver(book
                    ? new Solo.SoloCoOpDeployOrderResolver(evaluator, deploymentRng, tableState)
                    : (IStageResolver<StageResolution.Requests.SelectionRequest<UnitData>, Data.DataBinding<UnitData>>)
                    new AiSelectionResolver<UnitData>(evaluator))
                // #191 A4-1's request split: activation is its own type; solo-rules keeps the same
                // first-option behavior via the adapter (pinned by the benchmark hashes).
                // #419: the official bot gets the Solo & Co-Op activation order (D3 for a table section,
                // then a random unit in it, Shaken last). The baseline keeps "take the first option the
                // engine offered" - army build order, which let a player read the whole turn in advance,
                // but which plan D1 freezes because every benchmark number depends on it.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestActivationResolver(tableState, playerID)
                    : book
                        ? new Solo.SoloRulesActivationResolver(activationRng, tableState)
                        : (IStageResolver<StageResolution.Requests.ChooseUnitToActivateRequest, Data.DataBinding<UnitData>>)
                        new DerivedRequestAdapter<StageResolution.Requests.ChooseUnitToActivateRequest,
                        StageResolution.Requests.SelectionRequest<UnitData>, Data.DataBinding<UnitData>>(
                        new AiSelectionResolver<UnitData>()))
                .RegisterResolver(new AiSelectionResolver<ModelData>())
                .RegisterResolver(new AiSelectionResolver<RectangularZone>())
                // #419: the official bot deploys by the book - each of three groups into its own rolled
                // table section, aimed at the closest objective and off bad ground. The baseline keeps its
                // lane fan-out, which plan D1 freezes.
                .RegisterResolver(book
                    ? new Solo.SoloCoOpPlaceObjectsResolver<ModelData>(tableState, deploymentPlan)
                    : (IStageResolver<StageResolution.Requests.PlaceObjectsRequest<ModelData>,
                        CancellableResult<List<StageResolution.Requests.PlacedObjectEntry<ModelData>>>>)
                    new AiPlaceObjectsResolver<ModelData>(tableState))
                // #419: the official bot places markers by the book's squares rule; the baseline keeps the
                // mirrored-random spread that plan D1 freezes.
                .RegisterResolver(book
                    ? new Solo.SoloCoOpObjectiveResolver(tableState, objectiveRng)
                    : (IStageResolver<StageResolution.Requests.PlaceObjectiveRequest, Position>)
                    new AiPlaceObjectiveResolver(tableState, objectiveRng))
                .RegisterResolver(new AiPlaceOneTerrainResolver(tableState, terrainRng))
                .RegisterResolver(new AiHordeShopResolver());

            if (SlowMode)
                registry = new AiSlowModeRegistry(registry, SlowModeDelayMs);

            return registry;
        }

        public static ComputerPlayerController CreateSoloRulesController(
            string name, PlayerID id, FDGGame_AsLocal localGame, int? seed = null, int slotID = 0)
        {
            var registry = BuildSoloRules(localGame.TableState, id, seed, slotID);
            return new ComputerPlayerController(name, id, localGame, registry);
        }
    }

    internal sealed class AiSlowModeRegistry : IStageResolverRegistry
    {
        private readonly IStageResolverRegistry _inner;
        private readonly int _delayMs;

        internal AiSlowModeRegistry(IStageResolverRegistry inner, int delayMs)
        {
            _inner = inner;
            _delayMs = delayMs;
        }

        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>
        {
            _inner.RegisterResolver(resolver);
            return this;
        }

        public async Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            await Task.Delay(_delayMs);
            return await _inner.ResolveRequest<TRequest, TReply>(request);
        }

        public async Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
            IReadableGameDataStore gameDataStore)
        {
            await Task.Delay(_delayMs);
            return await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        }
    }
}
