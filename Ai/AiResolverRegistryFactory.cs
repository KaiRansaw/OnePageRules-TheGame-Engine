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

            // #358: one latch per solo set - the path resolver's main-move decline tells the
            // action menu to skip the movement family once, breaking the decline-repick livelock.
            var declineLatch = new SoloMoveDeclineLatch();

            // #191 A5-10: the UnitData selection resolver asks the rule graph which deploy-order
            // options are transports (they go down first, so later cargo can be offered the ride).
            var evaluator = new Rules.Dispatch.RuleEvaluator(new ProbabilisticDiceRoller());

            var stringSelection = new AiStringSelectionResolver(tableState, playerID, declineLatch);
            var questMemory = new StarQuest.StarQuestAiMemory();

            IStageResolverRegistry registry = new StageResolverRegistry()
                .RegisterResolver(new AiYesNoResolver())
                .RegisterResolver<StageResolution.Requests.StringSelectionRequest, string>(stringSelection)
                // #191 B1 step 5a: Choose Action is its own request type; the same instance answers it.
                // #419: the official bot answers Choose Action from the Solo & Co-Op decision tree. The
                // baseline keeps the plain ranked menu, which plan D1 freezes.
                .RegisterResolver<StageResolution.Requests.ChooseActionRequest, string>(starQuest
                    ? new StarQuest.StarQuestActionResolver(tableState, playerID, declineLatch, stringSelection, questMemory)
                    : book
                        ? new Solo.SoloCoOpActionResolver(tableState, playerID, declineLatch, stringSelection)
                        : stringSelection)
                .RegisterResolver(new AiChooseAbilityEffectResolver())
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestSpellResolver(playerSeed)
                    : (IStageResolver<StageResolution.Requests.ChooseSpellRequest, StageResolution.Requests.ChooseSpellReply>)new AiChooseSpellResolver())
                .RegisterResolver(new AiCastAssistResolver())
                .RegisterResolver(new AiChooseDeploymentZoneResolver())
                // #419: the book's shooting priority for the official bot, the frozen ordering for the
                // baseline. The Quest resolver wraps the book one for bot-run heroes - closest-first is
                // the right policy for them too, and it is what B16's measured hero runs used.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestRangedAttackResolver()
                    : book
                        ? new Solo.SoloCoOpRangedAttackResolver()
                        : (IStageResolver<StageResolution.Requests.ChooseRangedAttackRequest,
                            CancellableResult<StageResolution.Requests.ChooseRangedAttackRequest.RangedAttackChoice>>)new AiChooseRangedAttackResolver())
                .RegisterResolver(new AiChooseMeleeDefenderResolver())
                .RegisterResolver(new DerivedRequestAdapter<StageResolution.Requests.ChooseMeleeDefenderRequest,
                    StageResolution.Requests.CancellableSelectionRequest<UnitData>,
                    StageResolution.CancellableResult<Data.DataBinding<UnitData>>>(
                    new AiChooseMeleeDefenderResolver()))
                // #419: and moves to where that tree is heading, which for four of its five branches is an
                // objective rather than an enemy.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestMovementResolver(tableState, playerID, declineLatch, questMemory)
                    : book
                        ? new Solo.SoloCoOpMovementResolver(tableState, playerID, declineLatch)
                        : (IStageResolver<StageResolution.Requests.DefineMovementPathRequest,
                            CancellableResult<List<StageResolution.Requests.ModelMoveEntry>>>)new AiDefineMovementResolver(tableState, playerID, declineLatch))
                .RegisterResolver(new AiAircraftAdvanceResolver())
                .RegisterResolver(new AiConsolidationMoveResolver(tableState, playerID))
                .RegisterResolver(new AiAssignWoundsResolver())
                .RegisterResolver(new AiSelectionResolver<UnitData>(evaluator))
                // #191 A4-1's request split: activation is its own type; solo-rules keeps the same
                // first-option behavior via the adapter (pinned by the benchmark hashes).
                // #419: the official bot gets the Solo & Co-Op activation order (D3 for a table section,
                // then a random unit in it, Shaken last). The baseline keeps "take the first option the
                // engine offered" - army build order, which let a player read the whole turn in advance,
                // but which plan D1 freezes because every benchmark number depends on it.
                .RegisterResolver(starQuest
                    ? new StarQuest.StarQuestActivationResolver(tableState, playerID)
                    : book
                        ? new Solo.SoloRulesActivationResolver(activationRng)
                        : (IStageResolver<StageResolution.Requests.ChooseUnitToActivateRequest, Data.DataBinding<UnitData>>)
                        new DerivedRequestAdapter<StageResolution.Requests.ChooseUnitToActivateRequest,
                        StageResolution.Requests.SelectionRequest<UnitData>, Data.DataBinding<UnitData>>(
                        new AiSelectionResolver<UnitData>()))
                .RegisterResolver(new AiSelectionResolver<ModelData>())
                .RegisterResolver(new AiSelectionResolver<RectangularZone>())
                .RegisterResolver(new AiPlaceObjectsResolver<ModelData>(tableState))
                .RegisterResolver(new AiPlaceObjectiveResolver(tableState, objectiveRng))
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
