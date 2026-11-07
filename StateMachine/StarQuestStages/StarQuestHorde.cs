using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Quest;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine.StateMachineBuilders;

namespace FDG.StateMachine
{
    /// <summary>
    /// GF: Star Quest Horde Mode v2.0.0 numbers. The heroes (the defending side) are the party; everything else
    /// is the enemy collection, drawn from in waves.
    /// </summary>
    public static class StarQuestHordeRules
    {
        /// <summary>Star Quest Horde has no round limit.</summary>
        public const int NoRoundLimit = 999;
        public const int FinalWave = 10;
        public const int AlertnessToEndWave = 6;

        /// <summary>"Heroes must be deployed within 6\" of the table centre" (wider only if it is packed).</summary>
        public static readonly float[] HeroDeployRadii = { 6f, 9f, 12f };
        /// <summary>"Waves are deployed within 6\" of a random table corner" (wider only if it is packed).</summary>
        public static readonly float[] WaveCornerRadii = { 6f, 9f, 12f, 15f };

        /// <summary>Wave X is worth X * 25% of the heroes' total points.</summary>
        public static int WaveBudget(int wave, int partyPoints) => wave * partyPoints / 4;

        /// <summary>2 XP per objective the heroes hold, +1 per objective if the priority one is among them, times
        /// the wave number.</summary>
        public static int WaveXp(int seized, bool prioritySeized, int wave) =>
            (seized * 2 + (prioritySeized ? seized : 0)) * wave;

        /// <summary>Splits XP as evenly as possible: the first heroes get the leftover points.</summary>
        public static int[] Split(int xp, int heroes)
        {
            if (heroes <= 0) return Array.Empty<int>();
            var shares = new int[heroes];
            for (int i = 0; i < heroes; i++) shares[i] = xp / heroes + (i < xp % heroes ? 1 : 0);
            return shares;
        }

        /// <summary>Alertness rises by 1 each round, or 2 when no enemies are on the table.</summary>
        public static int AlertnessGain(bool enemiesOnTable) => enemiesOnTable ? 1 : 2;

        public static QuestHero? Hero(UnitData unit) => QuestHero.FromJson(unit.QuestHeroJson);

        public static List<UnitData> Heroes(IGameContext context) =>
            HordePlacement.AllUnits(context).Where(u => u.IsQuestHero).ToList();

        public static List<UnitData> Enemies(IGameContext context)
        {
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(context.TableState);
            return HordePlacement.AllUnits(context).Where(u => !defenders.Contains(u.PlayerID) && !u.IsQuestHero).ToList();
        }
    }

    public class StarQuestHordeStateMachineBuilder : IStateMachineBuilder<IGameContext>
    {
        public Dictionary<string, StageBase<IGameContext>> BuildStateMachine(StateMachine<IGameContext> stateMachine,
            IGameContext gameContext, out StageBase<IGameContext> startingStage)
        {
            // Map setup is Horde Mode's: terrain, then objectives 1-5 (the centre and each quarter's centre).
            HordeMapSetupStage mapSetupStage = new HordeMapSetupStage(gameContext, stateMachine);
            StarQuestHordeDeploymentStage deploymentStage = new StarQuestHordeDeploymentStage(gameContext, stateMachine);
            MainPhaseRoundStage mainPhaseRoundStage = new MainPhaseRoundStage(gameContext, stateMachine);
            mainPhaseRoundStage.RoundHooks = new StarQuestHordeRoundHooks(gameContext);
            StarQuestHordeVictoryStage victoryStage = new StarQuestHordeVictoryStage(gameContext, stateMachine);

            mapSetupStage.ToDeployment.Bind(deploymentStage);
            deploymentStage.ToMain.Bind(mainPhaseRoundStage);
            mainPhaseRoundStage.ToVictoryCalculation.Bind(victoryStage);

            startingStage = mapSetupStage;
            return new Dictionary<string, StageBase<IGameContext>>
            {
                { mapSetupStage.Name, mapSetupStage },
                { deploymentStage.Name, deploymentStage },
                { mainPhaseRoundStage.Name, mainPhaseRoundStage },
                { victoryStage.Name, victoryStage },
            };
        }
    }

    /// <summary>
    /// The heroes deploy within 6" of the table centre; every enemy unit goes into the waiting pool (held off the
    /// table, like a unit in reserve); then the first priority objective is rolled and wave 1 arrives.
    /// </summary>
    public class StarQuestHordeDeploymentStage : StageBase<IGameContext>
    {
        public StageBinding ToMain;

        public StarQuestHordeDeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            await context.Announce("Deployment", tier: EBannerTier.Headline);
            float w = HordePlacement.TableWidth, h = HordePlacement.TableHeight;

            foreach (UnitData enemy in StarQuestHordeRules.Enemies(GameContext))
                ReserveRules.PlaceInReserve(enemy);
            // Heroes start the mission with full power.
            foreach (UnitData hero in StarQuestHordeRules.Heroes(GameContext))
            {
                hero.QuestPower = StarQuestHordeRules.Hero(hero)?.Power ?? 0;
                // Status conditions don't carry over from the last mission.
                QuestTreasure.EditSheet(hero, s => s.Conditions.Clear());
            }

            GameContext.Log($"{GameSystemInfo.QuestName(GameContext.Settings.GameSystem)} Horde: the heroes deploy within 6\" of the table centre.");
            string taskName = TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName;
            await HordePlacement.PlacePartyNear(GameContext, StarQuestHordeRules.Heroes(GameContext),
                radius => new CircularZone(w / 2f, h / 2f, radius), taskName,
                radii: StarQuestHordeRules.HeroDeployRadii);

            await QuestChallenges.ChooseChallenges(GameContext, StarQuestHordeRules.Heroes(GameContext), horde: true);
            QuestTreasure.PlaceSearchTokens(GameContext, "mission start");

            var hooks = new StarQuestHordeRoundHooks(GameContext);
            await hooks.StartWave(1);

            // The heroes always activate first.
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(GameContext.TableState);
            List<ITeam> order = GameContext.TableState.Teams.Objects
                .OrderBy(t => t.Players.Any(defenders.Contains) ? 0 : 1)
                .ThenBy(t => t.TeamNumber)
                .ToList();
            context.SetFirstDeploymentRollOrder(order);
            await ToMain.Activate(context);
        }
    }

    /// <summary>
    /// Star Quest Horde's rounds. End of every round: enemies damage objectives they hold, alertness rises. At 6
    /// alertness the wave ends: the heroes earn XP for the objectives they hold and level up, and the next round
    /// starts the next wave (a new priority objective, then a wave worth wave x 25% of the party's points).
    /// </summary>
    public sealed class StarQuestHordeRoundHooks : IRoundHooks
    {
        private readonly IGameContext _gameContext;
        private readonly HordeRoundHooks _horde;

        public StarQuestHordeRoundHooks(IGameContext gameContext)
        {
            _gameContext = gameContext;
            _horde = new HordeRoundHooks(gameContext);
        }

        public async Task OnRoundStart(IMainPhaseContext context)
        {
            await QuestChallenges.DeployAmbushers(_gameContext);
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            if (quest.WavePending) await StartWave(quest.Wave);
            await QuestChallenges.ApplyRoundEffects(_gameContext);
        }

        /// <summary>Set Priority, then Deploy Wave.</summary>
        internal async Task StartWave(int wave)
        {
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            quest.Wave = wave;
            quest.WavePending = false;
            quest.Alertness = 0;
            Save(quest);

            if (wave > 1) await QuestChallenges.AdvanceTimeAndWeather(_gameContext);
            await _horde.SetPriority();
            bool boss = (wave == 4 || wave == 7 || wave == 10)
                && await QuestChallenges.OfferBossFight(_gameContext, $"Wave {wave}:");
            List<UnitData> deployed = await DeployWave(wave, boss ? 2 : 1);
            if (boss) QuestChallenges.StartBossWave(_gameContext, deployed);
            await QuestChallenges.RollRandomEvent(_gameContext);
        }

        public async Task<bool> OnRoundEnd(IMainPhaseContext context, int roundJustFinished)
        {
            await _horde.DamageObjectives();
            await QuestTreasure.TickItemMarkers(_gameContext);

            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            quest.RoundsDone++;
            bool enemiesOnTable = StarQuestHordeRules.Enemies(_gameContext)
                .Any(u => u.GetIsAlive() && u.GetIsOnBattlefield());
            int gain = StarQuestHordeRules.AlertnessGain(enemiesOnTable) + QuestChallenges.ExtraAlertness(_gameContext);
            quest.Alertness = Math.Min(StarQuestHordeRules.AlertnessToEndWave, quest.Alertness + gain);
            _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: alertness +{gain} ({quest.Alertness}/{StarQuestHordeRules.AlertnessToEndWave})" +
                (enemiesOnTable ? "." : " - no enemies on the table."));
            Save(quest);

            if (AllHeroesDown())
            {
                _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: every hero is down.");
                return true;
            }
            if (!_gameContext.TableState.Objectives.Objects.Any())
            {
                _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: every objective marker is destroyed.");
                return true;
            }

            if (quest.Alertness >= StarQuestHordeRules.AlertnessToEndWave)
            {
                await EndWave(quest.Wave);
                quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
                if (quest.Won) return true;
            }
            else
            {
                await _gameContext.Announce($"Alertness {quest.Alertness}/{StarQuestHordeRules.AlertnessToEndWave}",
                    new TextColor(255, 190, 90, 255));
            }
            return false;
        }

        private bool AllHeroesDown() => StarQuestHordeRules.Heroes(_gameContext).All(u => !u.GetIsAlive());

        private void Save(QuestProgress quest) => GameProgressUtilities.SetQuestProgress(_gameContext.GameDataStore, quest);

        // ── Deploy Wave ──────────────────────────────────────────────────────────────────────────────────

        internal async Task<List<UnitData>> DeployWave(int wave, int multiplier = 1)
        {
            List<UnitData> heroes = StarQuestHordeRules.Heroes(_gameContext);
            int partyPoints = heroes.Sum(u => u.PointCost);
            int budget = StarQuestHordeRules.WaveBudget(wave, partyPoints) * multiplier;

            // The pool: enemies waiting off the table, and enemies destroyed in earlier waves (they come back).
            List<UnitData> pool = StarQuestHordeRules.Enemies(_gameContext)
                .Where(u => (ReserveRules.IsInReserve(u) || !u.GetIsAlive()) && !QuestChallenges.IsWaitingToAmbush(_gameContext, u))
                .ToList();
            var rng = new Random(unchecked((_gameContext.Settings.DiceSeed ?? 0) * 31 + wave * 7919));
            pool = pool.OrderBy(_ => rng.Next()).ToList();

            var chosen = new List<UnitData>();
            int spent = 0;
            foreach (UnitData unit in pool)
            {
                if (spent + unit.PointCost > budget) continue;
                chosen.Add(unit);
                spent += unit.PointCost;
            }
            if (chosen.Count == 0 && pool.Count > 0)
            {
                UnitData cheapest = pool.OrderBy(u => u.PointCost).First();
                chosen.Add(cheapest);
                spent = cheapest.PointCost;
            }

            if (chosen.Count == 0)
            {
                // QA 5.2: say so, rather than a "Wave N incoming!" splash that nothing follows.
                await _gameContext.Announce($"Wave {wave}: no enemies are left to send.",
                    new TextColor(255, 120, 80, 255), EBannerTier.Headline);
                _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: wave {wave} - every enemy in the collection is already on the table, nobody new arrives.");
                return chosen;
            }
            await _gameContext.Announce(multiplier > 1 ? $"Boss fight! Wave {wave} incoming!" : $"Wave {wave} incoming!",
                new TextColor(255, 120, 80, 255), EBannerTier.Headline);
            _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: wave {wave} ({spent}/{budget} pts): {string.Join(", ", chosen.Select(u => u.Name))}.");
            await HordePlacement.PlaceWaveAtCorners(_gameContext, chosen, $"wave {wave}", StarQuestHordeRules.WaveCornerRadii);
            return chosen;
        }

        // ── End of Wave ──────────────────────────────────────────────────────────────────────────────────

        internal async Task EndWave(int wave)
        {
            ITableState table = _gameContext.TableState;
            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(table);
            var modelToUnit = new Dictionary<IModel, IUnit>();
            foreach (IUnit unit in table.Units.Objects)
                foreach (IModel model in unit.Models)
                    modelToUnit[model] = unit;

            int seized = 0;
            bool prioritySeized = false;
            var seizedSpots = new List<Position>();
            foreach (ObjectiveData objective in _gameContext.GameDataStore.GetAllValues<ObjectiveData>().ToList())
            {
                HashSet<PlayerID> near = ReconcileObjectivesStage.PlayersNearObjective(objective, table.Models.Objects, modelToUnit);
                if (!near.Any(defenders.Contains) || near.Any(p => !defenders.Contains(p))) continue;
                seized++;
                seizedSpots.Add(objective.Position);
                if (objective.IsHordePriority) prioritySeized = true;
            }

            int xp = StarQuestHordeRules.WaveXp(seized, prioritySeized, wave);
            if (QuestChallenges.BossWaveDestroyed(_gameContext, clear: true) && xp > 0)
            {
                xp *= 2;
                await _gameContext.Announce("The boss wave is destroyed - double XP!", new TextColor(255, 215, 0, 255), EBannerTier.Notice);
            }
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            quest.PartyXp += xp;
            quest.WavesFinished = wave;
            Save(quest);

            string held = seized == 0 ? "no objectives held" : $"{seized} objective{(seized == 1 ? "" : "s")} held"
                + (prioritySeized ? " incl. the priority" : "");
            _gameContext.Log($"{GameSystemInfo.QuestName(_gameContext.Settings.GameSystem)} Horde: wave {wave} complete - {held}: {xp} XP for the party.");
            await _gameContext.Announce($"Wave {wave} complete: +{xp} XP", new TextColor(255, 215, 0, 255), EBannerTier.Headline);

            List<UnitData> heroes = StarQuestHordeRules.Heroes(_gameContext);
            int[] shares = StarQuestHordeRules.Split(xp, heroes.Count);
            for (int i = 0; i < heroes.Count; i++)
                await GiveXp(heroes[i], shares[i]);

            if (wave < StarQuestHordeRules.FinalWave)
            {
                // Shop: each hero may buy and sell items.
                foreach (UnitData hero in heroes)
                    await QuestTreasure.Shop(_gameContext, hero);
                if (!QuestTreasure.AnySearchTokensLeft(_gameContext))
                    QuestTreasure.PlaceSearchTokens(_gameContext, $"after wave {wave}");
            }

            await HealAndInjure(wave, seizedSpots);

            quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            if (wave >= StarQuestHordeRules.FinalWave)
            {
                quest.Won = true;
                Save(quest);
                return;
            }
            quest.Wave = wave + 1;
            quest.WavePending = true;
            quest.Alertness = 0;
            Save(quest);
        }

        internal Task GiveXp(UnitData unit, int xp) => QuestLevelling.GiveXp(_gameContext, unit, xp);

        /// <summary>Hero Injuries: last wave's injuries heal; an unconscious hero may come back with a new one,
        /// within 3" of an objective the heroes hold (only if they hold one).</summary>
        private async Task HealAndInjure(int wave, List<Position> seizedSpots)
        {
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
            foreach (string entry in quest.WaveInjuries.ToList())
            {
                string[] p = entry.Split('|');
                if (p.Length != 3 || !int.TryParse(p[2], out int from) || from >= wave) continue;
                UnitData? healed = StarQuestHordeRules.Heroes(_gameContext).FirstOrDefault(h => h.ID.ID.ToString() == p[0]);
                if (healed != null) QuestMission.RemoveInjury(healed, p[1]);
                quest.WaveInjuries.Remove(entry);
            }
            GameProgressUtilities.SetQuestProgress(_gameContext.GameDataStore, quest);

            if (!QuestChallenges.IsOn(_gameContext, QuestChallenges.HeroInjuries) || seizedSpots.Count == 0) return;
            foreach (UnitData hero in StarQuestHordeRules.Heroes(_gameContext).Where(h => !h.GetIsAlive()))
            {
                bool revive = await _gameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(hero.PlayerID,
                    $"{hero.Name} is unconscious. Bring it back with an injury (lasts until the end of the next wave)?",
                    defaultAnswer: true, displayName: $"{hero.Name}: revive with an injury?"));
                if (!revive) continue;
                QuestActions.Revive(hero, 3);
                string injury = StarQuestMissionRules.Injuries[HordePlacement.RollD6(_gameContext)];
                QuestTreasure.EditSheet(hero, s => { if (!s.Injuries.Contains(injury)) s.Injuries.Add(injury); });
                QuestMission.ApplyInjury(_gameContext, hero, injury);
                quest = GameProgressUtilities.GetQuestProgress(_gameContext.GameDataStore);
                quest.WaveInjuries.Add($"{hero.ID.ID}|{injury}|{wave}");
                GameProgressUtilities.SetQuestProgress(_gameContext.GameDataStore, quest);
                Position spot = seizedSpots[0];
                await HordePlacement.PlaceUnitNear(_gameContext, hero, r => new CircularZone(spot.x, spot.z, r),
                    TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName, $"Placing {hero.Name}", radii: new[] { 3f, 6f, 9f });
                await _gameContext.Announce($"{hero.Name} is back - with a {injury}.", new TextColor(255, 140, 110, 255), EBannerTier.Notice);
            }
        }
    }

    /// <summary>Star Quest XP and level-ups, shared by Horde and missions.</summary>
    public static class QuestLevelling
    {
        /// <summary>Adds XP to a hero and asks its player to pick an improvement for every level it reaches.</summary>
        public static async Task GiveXp(IGameContext _gameContext, UnitData unit, int xp)
        {
            QuestHero? hero = StarQuestHordeRules.Hero(unit);
            if (hero == null || xp <= 0) return;
            int before = hero.Level;
            hero.Xp += xp;
            int after = QuestProgression.LevelForXp(hero.Xp);
            _gameContext.Log($"  {hero.Name} gains {xp} XP ({hero.Xp} total).");
            for (int level = before + 1; level <= after; level++)
            {
                string[] options = QuestProgression.LevelOptions[level];
                string pick = options[0];
                if (options.Length > 1)
                {
                    var request = new StringSelectionRequest(unit.PlayerID,
                        $"{hero.Name} reached level {level}! Pick an improvement.",
                        options.ToList(), Array.Empty<StringSelectionRequest.InvalidOption>(),
                        options.ToDictionary(o => o, Describe),
                        displayName: $"{hero.Name} levels up");
                    pick = await _gameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                    if (!options.Contains(pick)) pick = options[0];
                }
                QuestProgression.Apply(hero, pick);
                hero.Level = level;
                ApplyLive(unit, hero, pick);
                _gameContext.Log($"  {hero.Name} is now level {level}: {pick}.");
                await _gameContext.Announce($"{hero.Name}: level {level} ({pick})", new TextColor(130, 220, 255, 255));
            }
            unit.QuestHeroJson = hero.ToJson();
        }

        private static string Describe(string option) => option switch
        {
            var o when o.EndsWith("Tou") => "More wounds before the hero goes down.",
            var o when o.EndsWith("Qua") => "Hits and passes morale on a lower roll.",
            var o when o.EndsWith("Pow") => "More power to spend on extra actions and re-rolls.",
            var o when o.EndsWith("Skill") => "Learn more skills (pick them in Star Quest Heroes after the mission).",
            var o when o.EndsWith("Caster") => "More spell tokens each round.",
            var o when o.EndsWith("Str") => "Better Str tests (Push, Afflicted...).",
            var o when o.EndsWith("Dex") => "Better Dex tests (Scurry, jumping...).",
            var o when o.EndsWith("Wil") => "Better Wil tests (Discover, Diseased...).",
            _ => "",
        };

        /// <summary>The level-up changes that matter on the table right away: Tough and Quality.</summary>
        private static void ApplyLive(UnitData unit, QuestHero hero, string pick)
        {
            if (pick.EndsWith("Qua")) unit.Quality = hero.Quality;
            if (pick.EndsWith("Tou"))
                foreach (DataBinding<ModelData> binding in unit.ModelBindings)
                {
                    ModelData model = binding.GetValue();
                    if (!model.GetIsAlive()) continue;
                    float dealt = model.WoundsDealt;
                    model.SetMaxWounds(hero.Tough);
                    if (dealt > 0f) model.DealWounds(dealt);
                }
        }
    }

    /// <summary>The mission is over: the high score is the party's XP times the last wave finished.</summary>
    public class StarQuestHordeVictoryStage : StageBase<IGameContext>
    {
        public StarQuestHordeVictoryStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            ITableState table = GameContext.TableState;
            var players = table.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
            int rounds = Math.Max(0, table.Progress.RoundCount ?? 0);
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(GameContext.GameDataStore);
            int highScore = quest.PartyXp * quest.WavesFinished;
            bool heroesDown = StarQuestHordeRules.Heroes(GameContext).All(u => !u.GetIsAlive());
            string how = quest.Won
                ? "The heroes survived all 10 waves!"
                : heroesDown ? $"The heroes fell during wave {quest.Wave}."
                : $"Every objective was destroyed during wave {quest.Wave}.";
            string message = $"{how} {quest.PartyXp} XP x {quest.WavesFinished} wave{(quest.WavesFinished == 1 ? "" : "s")} = high score {highScore}.";
            GameContext.Log($"{GameSystemInfo.QuestName(GameContext.Settings.GameSystem)} Horde ended after round {rounds}: {message}");

            int? defendingTeam = HordeRules.DefendingTeamNumber(players);
            var heroesSide = players.Where(p => p.TeamNumber == defendingTeam).ToList();
            var enemies = players.Where(p => p.TeamNumber != defendingTeam).ToList();
            var winners = quest.Won ? heroesSide : enemies;
            GameResult result = winners.Count == 0
                ? GameResult.ForTie(table.Progress.Scores, rounds)
                : GameResult.ForWinWithMessage(winners.Select(p => p.PlayerID).ToList(),
                    winners.Select(p => p.Name).ToList(), table.Progress.Scores, rounds, message);
            await context.Announce(result.Message, new TextColor(255, 215, 0, 255), EBannerTier.Headline);
            GameContext.NotifyGameCompleted(result);
        }

        public override void Exit() { }
    }
}
