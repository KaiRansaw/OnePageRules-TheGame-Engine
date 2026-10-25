using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Quest;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.StateMachine.StateMachineBuilders;

namespace FDG.StateMachine
{
    /// <summary>
    /// GF: Star Quest one-off mission from the Campaign Builder (Beginner's Guide v2.0.1, pp. 15-19): a random
    /// primary objective, then a secondary one; sentries at the start, reinforcements whenever alertness reaches 6;
    /// the heroes start in a random corner and end the mission by gathering in one corner once the primary is done.
    /// </summary>
    public static class StarQuestMissionRules
    {
        public static readonly string[] DifficultyNames = { "", "Beginner", "Intermediate", "Expert", "Legendary" };
        public static readonly string[] PrimaryNames = { "", "Clean Sweep", "Target Defense", "Retrieval", "Investigation", "Area Search", "Delivery" };
        public static readonly string[] SecondaryNames = { "", "Interrogate", "Safeguard", "Capture", "Sabotage", "Scavenge", "Escort" };

        public static readonly float[] HeroRadii = { 6f, 9f, 12f };
        public static readonly float[] EnemyRadii = { 6f, 9f, 12f, 15f };

        /// <summary>A wave is worth 25% of the party's points per difficulty level.</summary>
        public static int WaveBudget(int difficulty, int partyPoints) => Math.Max(1, difficulty) * partyPoints / 4;

        /// <summary>Alertness: +3 once the primary is done; otherwise +1, or +2 with no enemies on the table.</summary>
        public static int AlertnessGain(bool primaryDone, bool enemiesOnTable) => primaryDone ? 3 : enemiesOnTable ? 1 : 2;

        public static int HazardPay(int difficulty) => 5 * difficulty;
        public static int Xp(int difficulty, bool primary, bool secondary) => (primary ? 2 * difficulty : 0) + (secondary ? difficulty : 0);

        public static readonly string[] Injuries = { "", "Chest Wound", "Blinded Eye", "Smashed Leg", "Broken Arm", "Spinal Injury", "Crushed Spirit" };

        public static float W => HordePlacement.TableWidth;
        public static float H => HordePlacement.TableHeight;
        public static Position Centre => new(W / 2f, H / 2f);

        /// <summary>Quarter 1-4 matches corner 1-4 (1 = the 0,0 corner).</summary>
        public static Position QuarterCentre(int quarter) => quarter switch
        {
            1 => new Position(W / 4f, H / 4f),
            2 => new Position(3f * W / 4f, H / 4f),
            3 => new Position(W / 4f, 3f * H / 4f),
            _ => new Position(3f * W / 4f, 3f * H / 4f),
        };

        public static Position Corner(int corner) => HordeRules.CornerPosition(corner, W, H);

        /// <summary>A point a few inches in from a corner, along the diagonal.</summary>
        public static Position NearCorner(int corner, float inches = 4f)
        {
            Position c = Corner(corner);
            float sx = c.x < W / 2f ? 1f : -1f, sz = c.z < H / 2f ? 1f : -1f;
            float d = inches / MathF.Sqrt(2f);
            return new Position(c.x + sx * d, c.z + sz * d);
        }

        /// <summary>The corner nearest a point (1-4).</summary>
        public static int CornerOf(Position p) => Enumerable.Range(1, 4).OrderBy(c => Position.GetDistance2D(Corner(c), p)).First();
    }

    public class StarQuestMissionStateMachineBuilder : IStateMachineBuilder<IGameContext>
    {
        public Dictionary<string, StageBase<IGameContext>> BuildStateMachine(StateMachine<IGameContext> stateMachine,
            IGameContext gameContext, out StageBase<IGameContext> startingStage)
        {
            var mapSetup = new StarQuestMissionMapSetupStage(gameContext, stateMachine);
            var deployment = new StarQuestMissionDeploymentStage(gameContext, stateMachine);
            var main = new MainPhaseRoundStage(gameContext, stateMachine) { RoundHooks = new StarQuestMissionRoundHooks(gameContext) };
            var victory = new StarQuestMissionVictoryStage(gameContext, stateMachine);

            mapSetup.ToDeployment.Bind(deployment);
            deployment.ToMain.Bind(main);
            main.ToVictoryCalculation.Bind(victory);

            startingStage = mapSetup;
            return new Dictionary<string, StageBase<IGameContext>>
            {
                { mapSetup.Name, mapSetup }, { deployment.Name, deployment }, { main.Name, main }, { victory.Name, victory },
            };
        }
    }

    /// <summary>Terrain only - the mission places its own markers.</summary>
    public class StarQuestMissionMapSetupStage : ParentStage<IGameContext, IMapSetupContext>
    {
        public StageBinding ToDeployment;

        public StarQuestMissionMapSetupStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            await context.Announce("Star Quest Mission - Map Setup", tier: EBannerTier.Headline);
            await base.Enter(context);
        }

        protected override IMapSetupContext GetNewChildContext(IGameContext contextSelf) => new MapSetupContext(contextSelf);

        protected override Dictionary<string, Transition> PopulateTransitions(out StageBase<IMapSetupContext> startingChild)
        {
            ToDeployment = new StageBinding(this);
            var dictionary = new TransitionSetBuilder(this)
                .AddChild(new RollForFirstTerrainPlacementStage(GameContext, this), out var rollForFirstTerrain)
                .AddChild(new PlaceTerrainStage(GameContext, this), out var placeTerrain)
                .AddSibling(nameof(ToDeployment), ToDeployment, out string toDeploymentEvent)
                .Build();
            startingChild = rollForFirstTerrain;
            rollForFirstTerrain.OnRollComplete.Bind(placeTerrain);
            placeTerrain.OnTerrainPlaced.Bind(toDeploymentEvent);
            return dictionary;
        }
    }

    /// <summary>
    /// Mission set-up: difficulty, primary objective, search tokens (D3 + heroes), sentries at quarter centres,
    /// the heroes in a random corner, then any markers that depend on the heroes' corner.
    /// </summary>
    public class StarQuestMissionDeploymentStage : StageBase<IGameContext>
    {
        public StageBinding ToMain;

        public StarQuestMissionDeploymentStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent)
        {
            ToMain = new StageBinding(this);
        }

        public override async Task Enter(IGameContext context)
        {
            await context.Announce("Mission Set-Up", tier: EBannerTier.Headline);
            List<UnitData> heroes = StarQuestHordeRules.Heroes(GameContext);
            foreach (UnitData enemy in StarQuestHordeRules.Enemies(GameContext))
                ReserveRules.PlaceInReserve(enemy);
            foreach (UnitData hero in heroes)
            {
                hero.QuestPower = StarQuestHordeRules.Hero(hero)?.Power ?? 0;
                QuestTreasure.EditSheet(hero, s => s.Conditions.Clear());
                QuestMission.ApplyInjuries(GameContext, hero);
            }

            // Campaign mission: the chapter's difficulty, and the wounds / power the heroes carry from the last mission.
            int campaignDifficulty = QuestMission.CampaignDifficulty(heroes);
            foreach (UnitData hero in heroes) QuestMission.ApplyCarryOver(GameContext, hero);

            // Difficulty (asked of the heroes' player, unless the campaign chapter set it).
            int difficulty = Math.Max(1, campaignDifficulty);
            if (heroes.Count > 0 && campaignDifficulty == 0)
            {
                var names = StarQuestMissionRules.DifficultyNames.Skip(1).ToList();
                var request = new StringSelectionRequest(heroes[0].PlayerID, "Choose the mission's difficulty",
                    names, Array.Empty<StringSelectionRequest.InvalidOption>(),
                    optionDescriptions: names.Select((n, i) => (n, i + 1)).ToDictionary(p => p.n,
                        p => $"Enemy waves worth {p.Item2 * 25}% of the party's points. Rewards: {StarQuestMissionRules.HazardPay(p.Item2)}c hazard pay, {2 * p.Item2} XP for the primary, {p.Item2} for the secondary."),
                    displayName: "Mission difficulty");
                string pick = await GameContext.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
                difficulty = Math.Max(1, names.IndexOf(pick) + 1);
            }

            await QuestChallenges.ChooseChallenges(GameContext, heroes, horde: false);
            QuestProgress quest = GameProgressUtilities.GetQuestProgress(GameContext.GameDataStore);
            quest.Difficulty = difficulty;
            quest.Primary = HordePlacement.RollD6(GameContext);
            GameProgressUtilities.SetQuestProgress(GameContext.GameDataStore, quest);
            await context.Announce($"{StarQuestMissionRules.DifficultyNames[difficulty]} mission - primary objective: {StarQuestMissionRules.PrimaryNames[quest.Primary]}",
                new TextColor(255, 215, 0, 255), EBannerTier.Headline);
            GameContext.Log($"Star Quest Mission: difficulty {difficulty}, primary {quest.Primary} ({StarQuestMissionRules.PrimaryNames[quest.Primary]}).");
            QuestMission.SetUpObjective(GameContext, quest.Primary, secondary: false, heroCorner: 0);

            // Search tokens: D3 + one per hero, half at quarter centres, half near corners.
            int tokens = (HordePlacement.RollD6(GameContext) + 1) / 2 + heroes.Count;
            var spots = new List<(int, bool)>();
            for (int i = 0; i < tokens; i++)
            {
                bool atCorner = i % 2 == 1;
                int roll = HordePlacement.RollD6(GameContext);
                int q = roll <= 4 ? roll : await QuestMission.AskArea(GameContext, heroes,
                    $"Search token {i + 1} of {tokens}: rolled {roll} - you choose the {(atCorner ? "corner" : "table quarter")}.", corner: atCorner);
                spots.Add((q, atCorner));
            }
            QuestTreasure.PlaceTokensAt(GameContext, spots, "mission set-up");

            // Sentries at the centre of random table quarters.
            int budget = StarQuestMissionRules.WaveBudget(difficulty, heroes.Sum(h => h.PointCost));
            List<UnitData> sentries = QuestMission.PickWave(GameContext, budget, salt: 0);
            GameContext.Log($"Star Quest Mission: sentries ({sentries.Sum(u => u.PointCost)}/{budget} pts): {string.Join(", ", sentries.Select(u => u.Name))}.");
            foreach (UnitData unit in sentries)
            {
                if (QuestChallenges.HoldForAmbush(GameContext, unit)) continue;
                int roll = HordePlacement.RollD6(GameContext);
                int q = roll <= 4 ? roll : await QuestMission.AskArea(GameContext, heroes,
                    $"Sentries {unit.Name}: rolled {roll} - you choose which table quarter's centre they guard.", corner: false);
                Position c = StarQuestMissionRules.QuarterCentre(q);
                HordePlacement.Revive(unit);
                await HordePlacement.PlaceUnitNear(GameContext, unit, r => new CircularZone(c.x, c.z, r),
                    HordePlacement.WaveTaskName, $"Deploying sentries: {unit.Name}", radii: StarQuestMissionRules.EnemyRadii);
            }

            // Heroes within 6" of a random corner (5-6: the heroes pick).
            int heroRoll = HordePlacement.RollD6(GameContext);
            int corner = heroRoll <= 4 || heroes.Count == 0 ? Math.Clamp(heroRoll, 1, 4)
                : await QuestMission.AskArea(GameContext, heroes, $"Your heroes rolled a {heroRoll} - choose the corner they deploy in.", corner: true);
            GameContext.Log($"Star Quest Mission: the heroes deploy in corner {corner} (rolled {heroRoll}).");
            Position hc = StarQuestMissionRules.Corner(corner);
            string taskName = TacticianPlaceObjectsResolver<ModelData>.DeploymentTaskName;
            await HordePlacement.PlacePartyNear(GameContext, heroes,
                r => new TableCornerZone(new Float2(hc.x, hc.z), r, StarQuestMissionRules.W, StarQuestMissionRules.H),
                taskName, radii: StarQuestMissionRules.HeroRadii);

            quest = GameProgressUtilities.GetQuestProgress(GameContext.GameDataStore);
            quest.HeroCorner = corner;
            GameProgressUtilities.SetQuestProgress(GameContext.GameDataStore, quest);
            QuestMission.AfterHeroesDeploy(GameContext, corner);

            HashSet<PlayerID> defenders = HordePlacement.DefenderPlayers(GameContext.TableState);
            List<ITeam> order = GameContext.TableState.Teams.Objects
                .OrderBy(t => t.Players.Any(defenders.Contains) ? 0 : 1).ThenBy(t => t.TeamNumber).ToList();
            context.SetFirstDeploymentRollOrder(order);
            await context.Announce(QuestMission.ObjectiveHint(GameContext), new TextColor(130, 200, 255, 255), EBannerTier.Notice);
            await ToMain.Activate(context);
        }
    }

    /// <summary>End of each round: carried markers drop from Shaken carriers, held markers collect tokens, wiped-out
    /// waves drop their marker, the secondary appears after the primary, alertness rises (reinforcements at 6), and
    /// the mission ends when every hero is down or the heroes gather in one corner after the primary.</summary>
    public sealed class StarQuestMissionRoundHooks : IRoundHooks
    {
        private readonly IGameContext _context;

        public StarQuestMissionRoundHooks(IGameContext context) { _context = context; }

        public async Task OnRoundStart(IMainPhaseContext context)
        {
            await QuestChallenges.DeployAmbushers(_context);
            QuestProgress quest = QuestMission.Progress(_context);
            if (!quest.FirstEventDone)
            {
                quest.FirstEventDone = true;
                QuestMission.Save(_context, quest);
                await QuestChallenges.RollRandomEvent(_context);
            }
            await QuestChallenges.ApplyRoundEffects(_context);
        }

        public async Task<bool> OnRoundEnd(IMainPhaseContext context, int roundJustFinished)
        {
            await QuestTreasure.TickItemMarkers(_context);
            QuestProgress counted = QuestMission.Progress(_context);
            counted.RoundsDone++;
            QuestMission.Save(_context, counted);
            await QuestMission.DropFromShakenCarriers(_context);
            await QuestMission.SeizeMarkers(_context);
            await QuestMission.CheckWipedWave(_context);

            QuestProgress quest = QuestMission.Progress(_context);
            if (quest.PrimaryDone && quest.Secondary == 0)
            {
                quest.Secondary = HordePlacement.RollD6(_context);
                QuestMission.Save(_context, quest);
                QuestMission.SetUpObjective(_context, quest.Secondary, secondary: true, heroCorner: quest.HeroCorner);
                await _context.Announce($"Secondary objective: {StarQuestMissionRules.SecondaryNames[quest.Secondary]}",
                    new TextColor(255, 215, 0, 255), EBannerTier.Headline);
                await _context.Announce(QuestMission.ObjectiveHint(_context), new TextColor(130, 200, 255, 255), EBannerTier.Notice);
            }

            // Boss fight (optional): once every objective is done, the heroes may call in a double wave.
            quest = QuestMission.Progress(_context);
            bool bossJustArrived = false;
            if (quest.PrimaryDone && quest.Secondary > 0 && quest.SecondaryDone
                && await QuestChallenges.OfferBossFight(_context, "Every objective is done.", askOnce: true))
            {
                await QuestMission.DeployReinforcements(_context, boss: true);
                bossJustArrived = true;
            }

            if (StarQuestHordeRules.Heroes(_context).All(h => !h.GetIsAlive()))
            {
                quest = QuestMission.Progress(_context);
                quest.Outcome = "failed";
                QuestMission.Save(_context, quest);
                return true;
            }
            if (!bossJustArrived && QuestMission.HeroesAtOneCorner(_context) is int exitCorner)
            {
                quest = QuestMission.Progress(_context);
                quest.Outcome = "success";
                QuestMission.Save(_context, quest);
                _context.Log($"Star Quest Mission: the heroes leave by corner {exitCorner}.");
                return true;
            }

            quest = QuestMission.Progress(_context);
            bool enemiesOnTable = StarQuestHordeRules.Enemies(_context).Any(u => u.GetIsAlive() && u.GetIsOnBattlefield());
            int gain = StarQuestMissionRules.AlertnessGain(quest.PrimaryDone, enemiesOnTable) + QuestChallenges.ExtraAlertness(_context);
            quest.Alertness = Math.Min(StarQuestHordeRules.AlertnessToEndWave, quest.Alertness + gain);
            QuestMission.Save(_context, quest);
            _context.Log($"Star Quest Mission: alertness +{gain} ({quest.Alertness}/6).");
            if (quest.Alertness >= StarQuestHordeRules.AlertnessToEndWave && quest.BossFight == 0)
                await QuestMission.DeployReinforcements(_context);
            else if (quest.Alertness >= StarQuestHordeRules.AlertnessToEndWave)
            {
                quest.Alertness = 0; // after a boss fight no more reinforcements arrive
                QuestMission.Save(_context, quest);
            }
            else
                await _context.Announce($"Alertness {quest.Alertness}/6", new TextColor(255, 190, 90, 255));
            return false;
        }
    }

    /// <summary>Rewards on a success (hazard pay + XP for each objective), injuries for every hero that was
    /// unconscious (all of them on a failure).</summary>
    public class StarQuestMissionVictoryStage : StageBase<IGameContext>
    {
        public StarQuestMissionVictoryStage(IGameContext gameContext, IStateMachineLayer<IGameContext> parent)
            : base(gameContext, parent) { }

        public override async Task Enter(IGameContext context)
        {
            ITableState table = GameContext.TableState;
            QuestProgress quest = QuestMission.Progress(GameContext);
            bool success = quest.Outcome == "success";
            int d = Math.Max(1, quest.Difficulty);
            List<UnitData> heroes = StarQuestHordeRules.Heroes(GameContext);
            string message;
            if (success)
            {
                int pay = StarQuestMissionRules.HazardPay(d);
                int xp = StarQuestMissionRules.Xp(d, quest.PrimaryDone, quest.SecondaryDone);
                if (QuestChallenges.BossWaveDestroyed(GameContext, clear: false)) xp += 2 * d;
                message = $"Mission complete! {StarQuestMissionRules.PrimaryNames[quest.Primary]} done"
                    + (quest.Secondary > 0 ? (quest.SecondaryDone ? $", {StarQuestMissionRules.SecondaryNames[quest.Secondary]} done" : $", {StarQuestMissionRules.SecondaryNames[quest.Secondary]} not done") : "")
                    + $". Each hero gets {pay}c and {xp} XP.";
                await context.Announce(message, new TextColor(255, 215, 0, 255), EBannerTier.Headline);
                foreach (UnitData hero in heroes)
                {
                    bool down = !hero.GetIsAlive();
                    QuestTreasure.EditSheet(hero, s => { s.Credits += pay; s.Injuries.Clear(); });
                    await QuestLevelling.GiveXp(GameContext, hero, xp);
                    if (down) await QuestMission.Injure(GameContext, hero);
                    QuestMission.RecordCarryOver(hero, "success");
                }
                // Before the next mission, heroes may hand items and credits to each other (no shop between missions).
                foreach (UnitData hero in heroes)
                    await QuestTreasure.Shop(GameContext, hero, canBuyAndSell: false);
            }
            else
            {
                message = "Mission failed - every hero went down. They all suffer injuries.";
                await context.Announce(message, new TextColor(255, 120, 90, 255), EBannerTier.Headline);
                foreach (UnitData hero in heroes)
                {
                    QuestTreasure.EditSheet(hero, s => s.Injuries.Clear());
                    await QuestMission.Injure(GameContext, hero);
                    QuestMission.RecordCarryOver(hero, "failed");
                }
            }
            GameContext.Log("Star Quest Mission: " + message);

            var players = table.Players.Objects.Where(p => p.IsFilled).OrderBy(p => p.SlotID).ToList();
            int rounds = Math.Max(0, table.Progress.RoundCount ?? 0);
            int? heroTeam = HordeRules.DefendingTeamNumber(players);
            var winners = players.Where(p => success ? p.TeamNumber == heroTeam : p.TeamNumber != heroTeam).ToList();
            GameResult result = winners.Count == 0
                ? GameResult.ForTie(table.Progress.Scores, rounds)
                : GameResult.ForWinWithMessage(winners.Select(p => p.PlayerID).ToList(), winners.Select(p => p.Name).ToList(),
                    table.Progress.Scores, rounds, message);
            GameContext.NotifyGameCompleted(result);
        }

        public override void Exit() { }
    }
}

namespace FDG.Stages
{
    using FDG.StateMachine;

    /// <summary>Star Quest mission objectives: setting up markers, working them with the "Objective" skill,
    /// carrying, seizing, the AI Goal, reinforcements, and injuries.</summary>
    public static class QuestMission
    {
        public const string ObjectiveSkillName = "Objective";

        private static readonly TextColor Gold = new(255, 215, 0, 255);
        private static readonly TextColor Blue = new(130, 200, 255, 255);

        public static bool IsMission(IGameContext context) => context.Settings.GameMode == EGameMode.StarQuestMission;

        public static QuestProgress Progress(IGameContext context) => GameProgressUtilities.GetQuestProgress(context.GameDataStore);
        public static void Save(IGameContext context, QuestProgress quest) => GameProgressUtilities.SetQuestProgress(context.GameDataStore, quest);

        private static Position RandomSpot(IGameContext context, IList<Position> taken)
        {
            var spots = new List<Position> { StarQuestMissionRules.Centre };
            for (int q = 1; q <= 4; q++) spots.Add(StarQuestMissionRules.QuarterCentre(q));
            spots = spots.Where(s => !taken.Any(t => Position.GetDistance2D(t, s) < 1f)).ToList();
            return spots[(HordePlacement.RollD6(context) * 7 + taken.Count) % spots.Count];
        }

        private static int RandomCorner(IGameContext context, ICollection<int> avoid)
        {
            var corners = Enumerable.Range(1, 4).Where(c => !avoid.Contains(c)).ToList();
            return corners[(HordePlacement.RollD6(context) - 1) % corners.Count];
        }

        private static QuestMarker Marker(QuestProgress quest, Position p, bool secondary, int stat, string action,
            string kind = "objective", int tokensNeeded = 0, int completeOn = 0) => new()
        {
            Id = quest.NextMarkerId++, X = p.x, Z = p.z, ForSecondary = secondary, Stat = stat, Action = action,
            Kind = kind, TokensNeeded = tokensNeeded, CompleteOn = completeOn,
        };

        /// <summary>Places the markers for a primary (secondary = false) or secondary objective.</summary>
        public static void SetUpObjective(IGameContext context, int number, bool secondary, int heroCorner)
        {
            QuestProgress quest = Progress(context);
            var taken = new List<Position>();
            List<UnitData> heroes = StarQuestHordeRules.Heroes(context).Where(h => h.GetIsAlive()).ToList();
            UnitData? randomHero = heroes.Count == 0 ? null : heroes[(HordePlacement.RollD6(context) - 1) % heroes.Count];
            Position heroPos = randomHero?.Models.FirstOrDefault(m => m.GetIsAlive())?.Position ?? StarQuestMissionRules.Centre;
            void Add(QuestMarker m) { quest.Markers.Add(m); taken.Add(new Position(m.X, m.Z)); }

            if (!secondary)
            {
                switch (number)
                {
                    case 1: // Clean Sweep: the first reinforcement wave drops the marker.
                        quest.WatchingFor = 1;
                        break;
                    case 2: Add(Marker(quest, RandomSpot(context, taken), false, 0, "seize", tokensNeeded: 6)); break;
                    case 3: Add(Marker(quest, StarQuestMissionRules.NearCorner(RandomCorner(context, Array.Empty<int>())), false, 1, "carry")); break;
                    case 4:
                        Add(Marker(quest, RandomSpot(context, taken), false, 1, "remove"));
                        Add(Marker(quest, RandomSpot(context, taken), false, 1, "remove"));
                        break;
                    case 5:
                        for (int i = 0; i < 3; i++) Add(Marker(quest, RandomSpot(context, taken), false, 2, "remove", completeOn: 5));
                        break;
                    case 6:
                        int c1 = RandomCorner(context, Array.Empty<int>());
                        int c2 = RandomCorner(context, new[] { c1 });
                        Add(Marker(quest, StarQuestMissionRules.NearCorner(c1), false, -1, "", kind: "delivery"));
                        Add(Marker(quest, StarQuestMissionRules.NearCorner(c2), false, -1, "", kind: "delivery"));
                        break;
                }
            }
            else
            {
                switch (number)
                {
                    case 1: quest.WatchingFor = 2; quest.WatchedWave.Clear(); break; // Interrogate: the next wave
                    case 2: Add(Marker(quest, StarQuestMissionRules.Centre, true, 1, "seize", tokensNeeded: 3)); break;
                    case 3: Add(Marker(quest, StarQuestMissionRules.Centre, true, 0, "carry")); break;
                    case 4:
                        int far = Enumerable.Range(1, 4).OrderByDescending(c => Position.GetDistance2D(StarQuestMissionRules.Corner(c), heroPos)).First();
                        Add(Marker(quest, StarQuestMissionRules.NearCorner(far), true, 2, "remove"));
                        break;
                    case 5:
                        foreach (int c in Enumerable.Range(1, 4).OrderBy(c => Position.GetDistance2D(StarQuestMissionRules.Corner(c), heroPos)).Take(2))
                            Add(Marker(quest, StarQuestMissionRules.NearCorner(c), true, 1, "remove", completeOn: 4));
                        break;
                    case 6:
                        var near = new Position(Math.Clamp(heroPos.x + 2f, 1f, StarQuestMissionRules.W - 1f), Math.Clamp(heroPos.z + 2f, 1f, StarQuestMissionRules.H - 1f));
                        Add(Marker(quest, near, true, 0, "carry"));
                        int farC = Enumerable.Range(1, 4).OrderByDescending(c => Position.GetDistance2D(StarQuestMissionRules.Corner(c), heroPos)).First();
                        Add(Marker(quest, StarQuestMissionRules.NearCorner(farC), true, -1, "", kind: "extraction"));
                        break;
                }
            }
            Save(context, quest);
        }

        /// <summary>Retrieval / Delivery markers in the heroes' corner move to the centre; Delivery's objective
        /// marker goes in the heroes' deployment zone.</summary>
        public static void AfterHeroesDeploy(IGameContext context, int corner)
        {
            QuestProgress quest = Progress(context);
            foreach (QuestMarker m in quest.Markers.Where(m => !m.ForSecondary && (m.Kind == "delivery" || m.Action == "carry")))
                if (StarQuestMissionRules.CornerOf(new Position(m.X, m.Z)) == corner
                    && Position.GetDistance2D(new Position(m.X, m.Z), StarQuestMissionRules.Corner(corner)) <= 8f)
                {
                    m.X = StarQuestMissionRules.Centre.x;
                    m.Z = StarQuestMissionRules.Centre.z;
                }
            if (quest.Primary == 6)
            {
                Position p = StarQuestMissionRules.NearCorner(corner, 3f);
                quest.Markers.Add(Marker(quest, p, false, 2, "carry"));
            }
            Save(context, quest);
        }

        /// <summary>A 5-6: the heroes' player picks a table quarter or corner (1-4). Bots take the first.</summary>
        public static async Task<int> AskArea(IGameContext context, List<UnitData> heroes, string prompt, bool corner)
        {
            if (heroes.Count == 0) return 1 + HordePlacement.RollD6(context) % 4;
            var labels = Enumerable.Range(1, 4).Select(n => $"{(corner ? "Corner" : "Quarter")} {n} ({HordeRules.CornerName(n)})").ToList();
            var request = new StringSelectionRequest(heroes[0].PlayerID, prompt, labels,
                Array.Empty<StringSelectionRequest.InvalidOption>(), displayName: corner ? "Choosing a corner" : "Choosing a table quarter");
            string picked = await context.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            return Math.Max(0, labels.IndexOf(picked)) + 1;
        }

        public static string ObjectiveHint(IGameContext context) => ObjectiveHint(Progress(context));

        /// <summary>
        /// What the party has to do, in one line. The objectives are rolled openly at set-up, so this is
        /// public information and the front-end shows it for the whole mission rather than flashing it
        /// once as a banner.
        /// </summary>
        public static string ObjectiveHint(QuestProgress? progress)
        {
            if (progress is not { } q) return "";
            bool sec = q.PrimaryDone && q.Secondary > 0 && !q.SecondaryDone;
            int n = sec ? q.Secondary : q.Primary;
            if (q.PrimaryDone && (q.Secondary == 0 || q.SecondaryDone))
                return "Primary done: gather every hero within 6\" of one table corner to end the mission.";
            string hint = (sec, n) switch
            {
                (false, 1) => "Clean Sweep: wipe out the first reinforcement wave; its last model drops a marker. Skill > Objective (Str) on it.",
                (false, 2) => "Target Defense: Skill > Objective (Str) on the marker, then hold it (no enemies within 3\") for 6 rounds.",
                (false, 3) => "Retrieval: Skill > Objective (Dex) to pick up the marker, then bring it within 6\" of your starting corner.",
                (false, 4) => "Investigation: Skill > Objective (Dex) on both markers to remove them.",
                (false, 5) => "Area Search: Skill > Objective (Wil) on the markers; each removed one completes it on a 5+ (the last always does).",
                (false, 6) => "Delivery: pick up the marker in your corner (Wil), and end activations within 3\" of both delivery markers.",
                (true, 1) => "Interrogate: wipe out the next reinforcement wave; its last model drops a marker. Skill > Objective (Wil) on it.",
                (true, 2) => "Safeguard: Skill > Objective (Dex) on the centre marker, then hold it for 3 rounds.",
                (true, 3) => "Capture: pick up the centre marker (Str) and end an activation within 6\" of any corner.",
                (true, 4) => "Sabotage: Skill > Objective (Wil) on the far-corner marker.",
                (true, 5) => "Scavenge: Skill > Objective (Dex) on the markers; each removed one completes it on a 4+ (the last always does).",
                (true, 6) => "Escort: pick up the marker (Str) and end an activation within 3\" of the extraction marker.",
                _ => "",
            };
            return (sec ? "Secondary - " : "Primary - ") + hint;
        }

        // ── Positions ────────────────────────────────────────────────────────────────────────────────────

        public static IUnit? Carrier(ITableState table, QuestMarker m) =>
            string.IsNullOrEmpty(m.CarriedBy) ? null : table.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == m.CarriedBy);

        public static Position Where(ITableState table, QuestMarker m) =>
            Carrier(table, m)?.Models.FirstOrDefault()?.Position is { } p && !string.IsNullOrEmpty(m.CarriedBy)
                ? new Position(p.x, p.z) : new Position(m.X, m.Z);

        public static float DistanceToPoint(IUnit unit, Position p) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => MathF.Max(0f, Position.GetDistance2D(m.Position, p) - m.BaseRadiusInches))
                .DefaultIfEmpty(float.MaxValue).Min();

        private static float FarthestToPoint(IUnit unit, Position p) =>
            unit.Models.Where(m => m.GetIsAlive())
                .Select(m => Position.GetDistance2D(m.Position, p) + m.BaseRadiusInches)
                .DefaultIfEmpty(float.MaxValue).Max();

        /// <summary>The corner every conscious hero is within 6" of, once the primary is done (else null).</summary>
        public static int? HeroesAtOneCorner(IGameContext context)
        {
            QuestProgress q = Progress(context);
            if (!q.PrimaryDone) return null;
            var up = StarQuestHordeRules.Heroes(context).Where(h => h.GetIsAlive() && h.GetIsOnBattlefield()).ToList();
            if (up.Count == 0) return null;
            foreach (int c in Enumerable.Range(1, 4))
                if (up.All(h => FarthestToPoint(h, StarQuestMissionRules.Corner(c)) <= 6f)) return c;
            return null;
        }

        // ── The AI Goal ──────────────────────────────────────────────────────────────────────────────────

        /// <summary>Where the AI heads when it can't see a hero, for the objective in play.</summary>
        public static Position? AiGoal(ITableState table)
        {
            QuestProgress? q = table.Progress.Quest;
            if (q == null || q.Difficulty == 0) return null;
            bool sec = q.PrimaryDone && q.Secondary > 0 && !q.SecondaryDone;
            int n = sec ? q.Secondary : q.Primary;
            var markers = q.Markers.Where(m => m.ForSecondary == sec && m.Kind == "objective").ToList();
            if (q.PrimaryDone && (q.Secondary == 0 || q.SecondaryDone)) return HeroNearestEnemy(table) is { } h ? Pos(h) : null;
            if (n == 1 || markers.Count == 0) return StarQuestMissionRules.Centre;
            bool closestToHero = (!sec && (n == 4 || n == 5)) || (sec && n == 5);
            if (!closestToHero) return Where(table, markers[0]);
            IUnit? hero = HeroNearestEnemy(table);
            if (hero == null) return Where(table, markers[0]);
            return markers.Select(m => Where(table, m)).OrderBy(p => DistanceToPoint(hero, p)).First();
        }

        private static Position? Pos(IUnit u) => u.Models.FirstOrDefault(m => m.GetIsAlive())?.Position;

        private static IUnit? HeroNearestEnemy(ITableState table)
        {
            var units = table.Units.Objects.Where(u => u.GetIsAlive() && u.GetIsOnBattlefield()).ToList();
            var heroes = units.Where(u => u is UnitData { IsQuestHero: true }).ToList();
            var enemies = units.Where(u => u is not UnitData { IsQuestHero: true }).ToList();
            if (heroes.Count == 0) return null;
            if (enemies.Count == 0) return heroes[0];
            return heroes.OrderBy(h => enemies.Min(e => FDG.Utilities.UnitCompareUtilities.MinDistanceBetweenUnits(h, e, out _, out _, includeVertical: false))).First();
        }

        // ── The Objective skill ──────────────────────────────────────────────────────────────────────────

        private static List<QuestMarker> Workable(IGameContext context, UnitData hero)
        {
            QuestProgress q = Progress(context);
            string me = hero.ID.ID.ToString();
            bool carrying = q.Markers.Any(m => m.CarriedBy == me);
            return q.Markers.Where(m => m.Kind == "objective" && string.IsNullOrEmpty(m.CarriedBy)
                    && !(m.Action == "seize" && m.Activated) && !(m.Action == "carry" && carrying)
                    && DistanceToPoint(hero, new Position(m.X, m.Z)) <= 1f)
                .ToList();
        }

        public static readonly IReadOnlyList<QuestSkill> MissionSkills = new List<QuestSkill>
        {
            new(ObjectiveSkillName, -1, "Work an objective marker within 1\": take its stat test (2 power can buy a pass) to remove, secure or pick it up.",
                (c, h) => !IsMission(c) ? "Only in Star Quest missions." : Workable(c, h).Count > 0 ? null : "No objective marker within 1\".",
                async (c, h) =>
                {
                    List<QuestMarker> list = Workable(c, h);
                    if (list.Count == 0) return;
                    QuestMarker m = list[0];
                    string stat = QuestActions.StatName(m.Stat);
                    if (m.Stat >= 0 && !await QuestActions.StatTest(c, h, m.Stat, "objective", offerPower: true))
                    {
                        await QuestActions.Toast(c, $"{h.Name} fails to work the objective.");
                        return;
                    }
                    await Work(c, h, m.Id);
                }, common: true),
        };

        private static async Task Work(IGameContext context, UnitData hero, int markerId)
        {
            QuestProgress q = Progress(context);
            QuestMarker? m = q.Markers.FirstOrDefault(x => x.Id == markerId);
            if (m == null) return;
            switch (m.Action)
            {
                case "seize":
                    m.Activated = true;
                    Save(context, q);
                    await QuestActions.Toast(context, $"{hero.Name} secures the marker - hold it (no enemies within 3\") at the end of each round.");
                    return;
                case "carry":
                    m.CarriedBy = hero.ID.ID.ToString();
                    Save(context, q);
                    await QuestActions.Toast(context, $"{hero.Name} picks up the objective marker.");
                    return;
                default:
                    q.Markers.Remove(m);
                    bool last = !q.Markers.Any(x => x.ForSecondary == m.ForSecondary && x.Kind == "objective");
                    Save(context, q);
                    bool done = last;
                    if (!last && m.CompleteOn > 0)
                    {
                        int roll = HordePlacement.RollD6(context);
                        done = roll >= m.CompleteOn;
                        await QuestActions.Toast(context, $"Marker removed - rolled {roll} (needs {m.CompleteOn}+)" + (done ? ": found it!" : "."));
                    }
                    else if (!last) await QuestActions.Toast(context, "Marker removed.");
                    if (done) await Complete(context, m.ForSecondary);
                    return;
            }
        }

        public static async Task Complete(IGameContext context, bool secondary)
        {
            QuestProgress q = Progress(context);
            if (secondary ? q.SecondaryDone : q.PrimaryDone) return;
            if (secondary) q.SecondaryDone = true; else q.PrimaryDone = true;
            q.Markers.RemoveAll(m => m.ForSecondary == secondary);
            Save(context, q);
            string name = secondary ? StarQuestMissionRules.SecondaryNames[q.Secondary] : StarQuestMissionRules.PrimaryNames[q.Primary];
            context.Log($"Star Quest Mission: {(secondary ? "secondary" : "primary")} objective {name} complete.");
            await context.Announce($"{(secondary ? "Secondary" : "Primary")} objective complete: {name}!", Gold, EBannerTier.Headline);
            if (!secondary)
                await context.Announce("Gather every hero within 6\" of one table corner to end the mission (a secondary objective appears at the end of the round).", Blue, EBannerTier.Notice);
        }

        // ── Hero activation end, and the end of the round ────────────────────────────────────────────────

        /// <summary>A carrying hero that ends its activation in the right place delivers.</summary>
        public static async Task OnHeroActivationEnd(IGameContext context, UnitData hero)
        {
            if (!IsMission(context)) return;
            QuestProgress q = Progress(context);
            string me = hero.ID.ID.ToString();
            QuestMarker? carried = q.Markers.FirstOrDefault(m => m.CarriedBy == me);
            if (carried == null) return;
            if (hero.Tokens.GetTokenCount(TokenType.Shaken) > 0 || !hero.GetIsAlive()) { await Drop(context, carried.Id); return; }
            int n = carried.ForSecondary ? q.Secondary : q.Primary;
            if (!carried.ForSecondary && n == 3 && FarthestToPoint(hero, StarQuestMissionRules.Corner(q.HeroCorner)) <= 6f)
                await Complete(context, false);
            else if (carried.ForSecondary && n == 3 && Enumerable.Range(1, 4).Any(c => FarthestToPoint(hero, StarQuestMissionRules.Corner(c)) <= 6f))
                await Complete(context, true);
            else if (!carried.ForSecondary && n == 6)
            {
                QuestMarker? drop = q.Markers.FirstOrDefault(m => m.Kind == "delivery" && DistanceToPoint(hero, new Position(m.X, m.Z)) <= 3f);
                if (drop == null) return;
                q.Markers.Remove(drop);
                Save(context, q);
                if (!q.Markers.Any(m => m.Kind == "delivery")) await Complete(context, false);
                else await QuestActions.Toast(context, $"{hero.Name} makes a delivery - one more to go.");
            }
            else if (carried.ForSecondary && n == 6
                     && q.Markers.FirstOrDefault(m => m.Kind == "extraction") is { } ex
                     && DistanceToPoint(hero, new Position(ex.X, ex.Z)) <= 3f)
                await Complete(context, true);
        }

        private static async Task Drop(IGameContext context, int markerId)
        {
            QuestProgress q = Progress(context);
            QuestMarker? m = q.Markers.FirstOrDefault(x => x.Id == markerId);
            if (m == null) return;
            Position p = Where(context.TableState, m);
            m.X = p.x; m.Z = p.z; m.CarriedBy = "";
            Save(context, q);
            await QuestActions.Toast(context, "The objective marker is dropped.");
        }

        public static async Task DropFromShakenCarriers(IGameContext context)
        {
            foreach (QuestMarker m in Progress(context).Markers.Where(m => !string.IsNullOrEmpty(m.CarriedBy)).ToList())
                if (Carrier(context.TableState, m) is not { } u || !u.GetIsAlive() || u.Tokens.GetTokenCount(TokenType.Shaken) > 0)
                    await Drop(context, m.Id);
        }

        /// <summary>Secured markers held by a hero (no enemies within 3") collect a token.</summary>
        public static async Task SeizeMarkers(IGameContext context)
        {
            QuestProgress q = Progress(context);
            var heroes = StarQuestHordeRules.Heroes(context).Where(h => h.GetIsAlive() && h.Tokens.GetTokenCount(TokenType.Shaken) == 0).ToList();
            var enemies = StarQuestHordeRules.Enemies(context).Where(e => e.GetIsAlive() && e.GetIsOnBattlefield()).ToList();
            var completed = new List<bool>();
            foreach (QuestMarker m in q.Markers.Where(m => m.Action == "seize" && m.Activated))
            {
                var p = new Position(m.X, m.Z);
                if (!heroes.Any(h => DistanceToPoint(h, p) <= 3f) || enemies.Any(e => DistanceToPoint(e, p) <= 3f)) continue;
                m.Tokens++;
                context.Log($"Star Quest Mission: the heroes hold the marker ({m.Tokens}/{m.TokensNeeded}).");
                if (m.Tokens >= m.TokensNeeded) completed.Add(m.ForSecondary);
            }
            Save(context, q);
            foreach (bool sec in completed) await Complete(context, sec);
        }

        /// <summary>Clean Sweep / Interrogate: when every unit of the watched wave is dead, it drops a marker.</summary>
        public static async Task CheckWipedWave(IGameContext context)
        {
            QuestProgress q = Progress(context);
            if (q.WatchingFor == 0 || q.WatchedWave.Count == 0) return;
            var units = HordePlacement.AllUnits(context).Where(u => q.WatchedWave.Contains(u.ID.ID.ToString())).ToList();
            if (units.Count == 0 || units.Any(u => u.GetIsAlive())) return;
            IModel? lastModel = units.SelectMany(u => u.Models).FirstOrDefault(m => m.Position.x != 0f || m.Position.z != 0f);
            Position p = lastModel?.Position ?? StarQuestMissionRules.Centre;
            bool secondary = q.WatchingFor == 2;
            q.Markers.Add(Marker(q, new Position(p.x, p.z), secondary, secondary ? 2 : 0, "remove"));
            q.WatchingFor = 0;
            q.WatchedWave.Clear();
            Save(context, q);
            await context.Announce("The wave is wiped out - its last model drops an objective marker!", Gold, EBannerTier.Notice);
        }

        // ── Enemies ──────────────────────────────────────────────────────────────────────────────────────

        /// <summary>Units for a wave from the pool (waiting off the table or destroyed earlier), up to the budget.</summary>
        public static List<UnitData> PickWave(IGameContext context, int budget, int salt)
        {
            List<UnitData> pool = StarQuestHordeRules.Enemies(context)
                .Where(u => (ReserveRules.IsInReserve(u) || !u.GetIsAlive()) && !QuestChallenges.IsWaitingToAmbush(context, u)).ToList();
            var rng = new Random(unchecked((context.Settings.DiceSeed ?? 0) * 31 + salt * 7919 + 17));
            pool = pool.OrderBy(_ => rng.Next()).ToList();
            var chosen = new List<UnitData>();
            int spent = 0;
            foreach (UnitData unit in pool)
            {
                if (spent + unit.PointCost > budget) continue;
                chosen.Add(unit);
                spent += unit.PointCost;
            }
            if (chosen.Count == 0 && pool.Count > 0) chosen.Add(pool.OrderBy(u => u.PointCost).First());
            return chosen;
        }

        public static async Task DeployReinforcements(IGameContext context, bool boss = false)
        {
            QuestProgress q = Progress(context);
            q.Alertness = 0;
            q.Reinforcements++;
            List<UnitData> heroes = StarQuestHordeRules.Heroes(context);
            int budget = StarQuestMissionRules.WaveBudget(q.Difficulty, heroes.Sum(h => h.PointCost)) * (boss ? 2 : 1);
            List<UnitData> wave = PickWave(context, budget, q.Reinforcements);
            if (q.WatchingFor != 0 && q.WatchedWave.Count == 0)
                q.WatchedWave = wave.Select(u => u.ID.ID.ToString()).ToList();
            Save(context, q);
            if (boss) QuestChallenges.StartBossWave(context, wave);
            await context.Announce(boss ? "Boss fight! A double wave arrives!" : $"Reinforcements arrive! (wave {q.Reinforcements})",
                new TextColor(255, 120, 80, 255), EBannerTier.Headline);
            context.Log($"Star Quest Mission: reinforcements {q.Reinforcements} ({wave.Sum(u => u.PointCost)}/{budget} pts): {string.Join(", ", wave.Select(u => u.Name))}.");
            foreach (UnitData unit in wave)
            {
                if (QuestChallenges.HoldForAmbush(context, unit)) continue;
                HordePlacement.Revive(unit);
                await HordePlacement.PlaceAtRandomCorner(context, unit, $"reinforcements {q.Reinforcements}", StarQuestMissionRules.EnemyRadii);
            }
            await QuestChallenges.AdvanceTimeAndWeather(context);
            await QuestChallenges.RollRandomEvent(context);
        }

        // ── Injuries ─────────────────────────────────────────────────────────────────────────────────────

        public static async Task Injure(IGameContext context, UnitData hero)
        {
            int roll = HordePlacement.RollD6(context);
            string injury = StarQuestMissionRules.Injuries[roll];
            QuestTreasure.EditSheet(hero, s => { if (!s.Injuries.Contains(injury)) s.Injuries.Add(injury); });
            await context.Announce($"{hero.Name} was knocked out and suffers a {injury} for the next mission.",
                new TextColor(255, 140, 110, 255), EBannerTier.Notice);
        }

        /// <summary>Injuries last one mission: Chest Wound -1 Def, Blinded Eye -1 Qua (hit and morale), Smashed Leg =
        /// Slow; Broken Arm / Spinal Injury / Crushed Spirit -1 to Str / Dex / Wil (in <see cref="InjuryPenalty"/>).</summary>
        public static void ApplyInjuries(IGameContext context, UnitData hero)
        {
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null) return;
            foreach (string injury in sheet.Injuries) ApplyInjury(context, hero, injury);
            if (sheet.Injuries.Count > 0) context.Log($"{hero.Name} plays this mission injured: {string.Join(", ", sheet.Injuries)}.");
        }

        /// <summary>The table effects of one injury (Str/Dex/Wil injuries work through <see cref="InjuryPenalty"/>).</summary>
        public static void ApplyInjury(IGameContext context, UnitData hero, string injury)
        {
            switch (injury)
            {
                case "Chest Wound": QuestActions.Grant(hero, hero, ERollKind.Save, -1, ELifetime.UntilEndOfGame, context); break;
                case "Blinded Eye":
                    QuestActions.Grant(hero, hero, ERollKind.Hit, -1, ELifetime.UntilEndOfGame, context);
                    QuestActions.Grant(hero, hero, ERollKind.Morale, -1, ELifetime.UntilEndOfGame, context);
                    break;
                case "Smashed Leg": QuestTreasure.GrantRule(context, hero, hero, "Slow", ELifetime.UntilEndOfGame); break;
            }
        }

        /// <summary>Takes one injury's table effects off again (Horde injuries heal at the end of the next wave).</summary>
        public static void RemoveInjury(UnitData hero, string injury)
        {
            QuestTreasure.EditSheet(hero, s => s.Injuries.Remove(injury));
            void Drop(Func<Token, bool> match)
            {
                Token? t = hero.Tokens.GetAllTokens().FirstOrDefault(match);
                if (t != null) hero.Tokens.RemoveTokensWithPayload(t.Type, t.OwnerUnitID, t.Payload, 1);
            }
            bool Minus1(Token t, TokenType type) => t.Type == type && t.Payload is TokenPayload.StatModifier { Delta: -1 }
                && t.ClearTrigger is not TokenClearTrigger.RoundEnd and not TokenClearTrigger.ActivationEnd and not TokenClearTrigger.FirstTrigger;
            switch (injury)
            {
                case "Chest Wound": Drop(t => Minus1(t, TokenType.SaveRollModifier)); break;
                case "Blinded Eye": Drop(t => Minus1(t, TokenType.HitRollModifier)); Drop(t => Minus1(t, TokenType.MoraleRollModifier)); break;
                case "Smashed Leg": Drop(t => t.Payload is TokenPayload.RuleGrant { RuleName: "Slow", Lifetime: ELifetime.UntilEndOfGame }); break;
            }
        }

        // ── Campaign carry-over ──────────────────────────────────────────────────────────────────────────

        /// <summary>The chapter difficulty when every hero is on the same campaign mission, else 0.</summary>
        public static int CampaignDifficulty(List<UnitData> heroes)
        {
            var sheets = heroes.Select(h => QuestHero.FromJson(h.QuestHeroJson)).ToList();
            if (sheets.Count == 0 || sheets.Any(s => s == null || s.CampaignDifficulty <= 0)) return 0;
            var names = sheets.Select(s => s!.CampaignName).Distinct().ToList();
            return names.Count == 1 ? sheets.Max(s => s!.CampaignDifficulty) : 0;
        }

        /// <summary>Campaign: start the mission with the wounds and power left from the last one.</summary>
        public static void ApplyCarryOver(IGameContext context, UnitData hero)
        {
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null || sheet.CampaignDifficulty <= 0) return;
            if (sheet.PowerLeft >= 0) hero.QuestPower = Math.Min(sheet.PowerLeft, sheet.Power);
            if (sheet.WoundsTaken > 0)
                foreach (DataBinding<ModelData> b in hero.ModelBindings)
                {
                    ModelData m = b.GetValue();
                    float dealt = MathF.Min(sheet.WoundsTaken, m.TotalWounds - 1f);
                    if (dealt > 0f) m.DealWounds(dealt);
                }
            context.Log($"{hero.Name} carries {sheet.WoundsTaken} wound(s) and {hero.QuestPower} power into this campaign mission.");
        }

        /// <summary>Campaign: after the mission each hero restores 3 wounds and 3 power (a knocked-out hero is
        /// revived); after a failure the chapter restarts with everyone fresh.</summary>
        public static void RecordCarryOver(UnitData hero, string outcome)
        {
            QuestTreasure.EditSheet(hero, s =>
            {
                s.LastOutcome = outcome;
                if (s.CampaignDifficulty <= 0) return;
                if (outcome != "success") { s.WoundsTaken = 0; s.PowerLeft = -1; return; }
                bool down = !hero.GetIsAlive();
                float dealt = hero.Models.Sum(m => m.WoundsDealt);
                int taken = down ? Math.Max(0, s.Tough - 3) : (int)MathF.Round(dealt);
                s.WoundsTaken = Math.Max(0, taken - 3);
                s.PowerLeft = Math.Min(s.Power, (down ? 0 : hero.QuestPower) + 3);
            });
        }

        /// <summary>-1 to a Str/Dex/Wil roll for the matching injury.</summary>
        public static int InjuryPenalty(UnitData hero, int statType)
        {
            QuestHero? sheet = QuestHero.FromJson(hero.QuestHeroJson);
            if (sheet == null) return 0;
            string injury = statType switch { 0 => "Broken Arm", 1 => "Spinal Injury", 2 => "Crushed Spirit", _ => "" };
            return sheet.Injuries.Contains(injury) ? 1 : 0;
        }
    }
}
