using FDG.Data;
using FDG.Quest;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StateMachine;

namespace FDG.GameMasterTools
{
    /// <summary>
    /// Game Master tools for ending the game early and every Star Quest system (waves,
    /// missions, challenges, events, search tokens, and the heroes' power, items, conditions, injuries and
    /// skills). Each one queues its change like the rest of the hub.
    /// </summary>
    public sealed partial class GameMasterHub
    {
        private volatile bool _endGameRequested;

        /// <summary>Whether this is a Star Quest game (Horde or Mission).</summary>
        public bool IsQuest => _context != null && GameSystemInfo.IsQuestMode(_context.Settings.GameMode);

        /// <summary>Whether this is a Star Quest Mission (objectives, difficulty, reinforcements).</summary>
        public bool IsQuestMission => _context?.Settings.GameMode == EGameMode.StarQuestMission;

        // ---------------------------------------------------------------- ending the game

        /// <summary>Ends the game at the end of this round (after the current activation): the victory
        /// screen comes straight after, skipping the mode's own end-of-round steps.</summary>
        public void EndGameNow()
        {
            _endGameRequested = true;
            _endRoundRequested = true;
            Post("End the game", ctx => { ctx.Log("Game Master: ending the game."); return Task.CompletedTask; });
        }

        internal bool ConsumeEndGame()
        {
            if (!_endGameRequested) return false;
            _endGameRequested = false;
            return true;
        }

        /// <summary>Star Quest: ends the game as a win or a loss, with the normal rewards, injuries and hero
        /// file updates. A loss knocks every hero out first.</summary>
        public void QuestFinish(bool win)
        {
            Post(win ? "Win the game" : "Lose the game", ctx =>
            {
                QuestProgress q = Quest(ctx);
                if (ctx.Settings.GameMode == EGameMode.StarQuestMission)
                    q.Outcome = win ? "success" : "failed";
                else
                {
                    q.Won = win;
                    if (win) q.WavesFinished = Math.Max(q.WavesFinished, q.Wave);
                }
                Save(ctx, q);
                if (!win)
                    foreach (UnitData hero in ctx.GameDataStore.GetAllValues<UnitData>().Where(u => u.IsQuestHero)) KnockOut(hero);
                ctx.Log($"Game Master: the game ends as a {(win ? "win" : "loss")}.");
                return Task.CompletedTask;
            });
            _endGameRequested = true;
            _endRoundRequested = true;
        }

        // ---------------------------------------------------------------- Star Quest: waves and alertness

        public void QuestSetAlertness(int value) => EditQuest($"Alertness {value}", (ctx, q) =>
        {
            q.Alertness = Math.Clamp(value, 0, StarQuestHordeRules.AlertnessToEndWave);
            ctx.Log($"Game Master: alertness set to {q.Alertness}/{StarQuestHordeRules.AlertnessToEndWave}.");
        });

        /// <summary>Horde: finishes the wave now (XP for objectives held, shop, injuries); the next wave
        /// arrives at the start of the next round. Finishing wave 10 wins the game.</summary>
        public void QuestEndWaveNow() => Post("End the wave", async ctx =>
        {
            QuestProgress q = Quest(ctx);
            await new StarQuestHordeRoundHooks(ctx).EndWave(q.Wave);
            if (Quest(ctx).Won) { _endGameRequested = true; _endRoundRequested = true; }
        });

        /// <summary>Horde: the next round starts this wave (1-10).</summary>
        public void QuestSetWave(int wave) => EditQuest($"Wave {wave}", (ctx, q) =>
        {
            q.Wave = Math.Clamp(wave, 1, StarQuestHordeRules.FinalWave);
            q.WavePending = true;
            q.Alertness = 0;
            ctx.Log($"Game Master: wave {q.Wave} starts at the start of the next round.");
        });

        /// <summary>Brings enemies in now: the current Horde wave, or Mission reinforcements. A boss wave is
        /// double size and counts as the boss fight.</summary>
        public void QuestDeployNow(bool boss) => Post(boss ? "Boss wave" : "Deploy enemies", async ctx =>
        {
            if (ctx.Settings.GameMode == EGameMode.StarQuestMission)
            {
                await QuestMission.DeployReinforcements(ctx, boss);
                return;
            }
            QuestProgress q = Quest(ctx);
            List<UnitData> wave = await new StarQuestHordeRoundHooks(ctx).DeployWave(q.Wave, boss ? 2 : 1);
            if (boss) QuestChallenges.StartBossWave(ctx, wave);
        });

        /// <summary>Brings every waiting AI Ambush unit on now.</summary>
        public void QuestAmbushNow() => EditQuestAsync("Ambushers arrive", async ctx =>
        {
            QuestProgress q = Quest(ctx);
            q.Ambushers = q.Ambushers.Select(a => a.Split(':')[0] + ":0").ToList();
            Save(ctx, q);
            await QuestChallenges.DeployAmbushers(ctx);
        });

        // ---------------------------------------------------------------- Star Quest: missions

        public void QuestSetDifficulty(int difficulty) => EditQuest($"Difficulty {difficulty}", (ctx, q) =>
        {
            q.Difficulty = Math.Clamp(difficulty, 1, 4);
            ctx.Log($"Game Master: difficulty set to {StarQuestMissionRules.DifficultyNames[q.Difficulty]}.");
        });

        /// <summary>Completes the primary (or secondary) objective now. With no secondary rolled yet, one is
        /// rolled and set up first.</summary>
        public void QuestCompleteObjective(bool secondary) => Post(secondary ? "Complete the secondary" : "Complete the primary", async ctx =>
        {
            QuestProgress q = Quest(ctx);
            if (secondary && q.Secondary == 0)
            {
                if (!q.PrimaryDone) await QuestMission.Complete(ctx, secondary: false);
                q = Quest(ctx);
                q.Secondary = HordePlacement.RollD6(ctx);
                Save(ctx, q);
                QuestMission.SetUpObjective(ctx, q.Secondary, secondary: true, heroCorner: q.HeroCorner);
            }
            await QuestMission.Complete(ctx, secondary);
        });

        /// <summary>Rolls a new primary (or secondary) objective and sets up its markers.</summary>
        public void QuestRerollObjective(bool secondary, int number) => Post("New objective", ctx =>
        {
            QuestProgress q = Quest(ctx);
            int n = number is >= 1 and <= 6 ? number : HordePlacement.RollD6(ctx);
            q.Markers.RemoveAll(m => m.ForSecondary == secondary);
            if (secondary) { q.Secondary = n; q.SecondaryDone = false; }
            else { q.Primary = n; q.PrimaryDone = false; }
            q.WatchingFor = 0;
            q.WatchedWave.Clear();
            Save(ctx, q);
            QuestMission.SetUpObjective(ctx, n, secondary, q.HeroCorner);
            string name = secondary ? StarQuestMissionRules.SecondaryNames[n] : StarQuestMissionRules.PrimaryNames[n];
            ctx.Log($"Game Master: {(secondary ? "secondary" : "primary")} objective is now {name}.");
            return Task.CompletedTask;
        });

        // ---------------------------------------------------------------- Star Quest: challenges and events

        public void QuestToggleChallenge(string challenge) => EditQuest($"Toggle {challenge}", (ctx, q) =>
        {
            if (!q.Challenges.Remove(challenge)) q.Challenges.Add(challenge);
            bool on = q.Challenges.Contains(challenge);
            // Turning these on with no value yet starts them at their first step.
            if (on && challenge == QuestChallenges.TimeOfDayName && q.TimeOfDay == "") q.TimeOfDay = "Dusk";
            if (on && challenge == QuestChallenges.WeatherName && q.Weather == "") q.Weather = "Worsening";
            if (on && challenge == QuestChallenges.IncreasedAlertness && q.AlertLevel == "") q.AlertLevel = "Alarmed";
            if (!on && challenge == QuestChallenges.TimeOfDayName) q.TimeOfDay = "";
            if (!on && challenge == QuestChallenges.WeatherName) q.Weather = "";
            if (!on && challenge == QuestChallenges.IncreasedAlertness) q.AlertLevel = "";
            ctx.Log($"Game Master: {challenge} {(on ? "on" : "off")}.");
        });

        public void QuestSetTimeOfDay(string value) => EditQuest($"Time of day {value}", (ctx, q) =>
        {
            q.TimeOfDay = value;
            if (value != "" && !q.Challenges.Contains(QuestChallenges.TimeOfDayName)) q.Challenges.Add(QuestChallenges.TimeOfDayName);
            ctx.Log($"Game Master: time of day set to {(value == "" ? "none" : value)}.");
        });

        public void QuestSetWeather(string value) => EditQuest($"Weather {value}", (ctx, q) =>
        {
            q.Weather = value;
            if (value != "" && !q.Challenges.Contains(QuestChallenges.WeatherName)) q.Challenges.Add(QuestChallenges.WeatherName);
            ctx.Log($"Game Master: weather set to {(value == "" ? "none" : value)}.");
        });

        public void QuestSetAlertLevel(string value) => EditQuest($"Alert level {value}", (ctx, q) =>
        {
            q.AlertLevel = value;
            if (value != "" && !q.Challenges.Contains(QuestChallenges.IncreasedAlertness)) q.Challenges.Add(QuestChallenges.IncreasedAlertness);
            ctx.Log($"Game Master: increased alertness set to {(value == "" ? "none" : value)}.");
        });

        /// <summary>Resolves a random event now: <paramref name="number"/> (11-66), or a roll when null.</summary>
        public void QuestRollEvent(int? number) => Post(number == null ? "Roll an event" : $"Event {number}",
            ctx => QuestChallenges.RollRandomEvent(ctx, number ?? (HordePlacement.RollD6(ctx) * 10 + HordePlacement.RollD6(ctx))));

        public void QuestClearEvent() => EditQuest("Clear the event", (ctx, q) =>
        {
            q.Event = "";
            q.EventHero = "";
            ctx.Log("Game Master: the random event is over.");
        });

        // ---------------------------------------------------------------- Star Quest: party and tokens

        /// <summary>Gives every hero <paramref name="xp"/> XP (each level reached asks for an improvement).</summary>
        public void QuestPartyXp(int xp) => Post($"+{xp} XP each", async ctx =>
        {
            foreach (UnitData hero in StarQuestHordeRules.Heroes(ctx)) await QuestLevelling.GiveXp(ctx, hero, xp);
        });

        /// <summary>Opens the shop for every hero in turn (buy, sell, give).</summary>
        public void QuestShopNow() => Post("Open the shop", async ctx =>
        {
            foreach (UnitData hero in StarQuestHordeRules.Heroes(ctx)) await QuestTreasure.Shop(ctx, hero);
        });

        public void QuestPlaceSearchTokens() => Post("Place search tokens", ctx =>
        {
            QuestTreasure.PlaceSearchTokens(ctx, "Game Master");
            return Task.CompletedTask;
        });

        /// <summary>Shows what <paramref name="count"/> unrevealed search tokens hold (as Prophetic Vision does).</summary>
        public void QuestForeseeTokens(int count) => Post("Foresee tokens", ctx =>
        {
            QuestTreasure.ForeseeTokens(ctx, count);
            return Task.CompletedTask;
        });

        public void QuestClearTokens() => EditQuest("Clear tokens", (ctx, q) =>
        {
            q.Tokens.Clear();
            ctx.Log("Game Master: every search token and treasure marker is removed.");
        });

        public void QuestClearItemMarkers() => EditQuest("Clear item markers", (ctx, q) =>
        {
            int before = q.Markers.Count;
            q.Markers.RemoveAll(m => m.Kind is not ("objective" or "delivery" or "extraction"));
            QuestTreasure.RefreshSmoke(ctx);
            ctx.Log($"Game Master: {before - q.Markers.Count} grenade / item marker(s) removed.");
        });

        // ---------------------------------------------------------------- Star Quest: one hero

        public void HeroPower(UnitID unitID, int delta) => PostUnit(unitID, "Power", (ctx, hero) =>
        {
            hero.QuestPower = Math.Max(0, hero.QuestPower + delta);
            ctx.Log($"Game Master: {hero.Name} has {hero.QuestPower} power.");
        });

        public void HeroFullPower(UnitID unitID) => PostUnit(unitID, "Full power", (ctx, hero) =>
        {
            hero.QuestPower = Math.Max(hero.QuestPower, QuestActions.Sheet(hero)?.Power ?? hero.QuestPower);
            ctx.Log($"Game Master: {hero.Name} is back to full power ({hero.QuestPower}).");
        });

        public void HeroCredits(UnitID unitID, int delta) => PostUnit(unitID, "Credits", (ctx, hero) =>
        {
            int now = 0;
            QuestTreasure.EditSheet(hero, s => { s.Credits = Math.Max(0, s.Credits + delta); now = s.Credits; });
            ctx.Log($"Game Master: {hero.Name} has {now}c.");
        });

        /// <summary>
        /// Star Quest testing tool: nudges one of a hero's stats straight on its sheet. Quality and Defense
        /// are mirrored onto the unit profile, which is what the dice actually read.
        ///
        /// <para><paramref name="delta"/> changes the printed number, so for the roll targets (Qua, Def,
        /// Str, Dex, Wil) a smaller number is the better hero.</para>
        /// </summary>
        public void HeroStat(UnitID unitID, string stat, int delta) => PostUnit(unitID, $"{stat} {delta:+#;-#;0}", (ctx, hero) =>
        {
            string now = "";
            QuestTreasure.EditSheet(hero, s =>
            {
                switch (stat)
                {
                    case "Quality":
                        s.Quality = Math.Clamp(s.Quality + delta, 2, 6);
                        hero.Quality = s.Quality;
                        now = $"Qua {s.Quality}+";
                        break;
                    case "Defense":
                        s.Defense = Math.Clamp(s.Defense + delta, 2, 6);
                        hero.Defense = s.Defense;
                        now = $"Def {s.Defense}+";
                        break;
                    case "Strength":  s.Strength = Math.Clamp(s.Strength + delta, 2, 6);   now = $"Str {s.Strength}+"; break;
                    case "Dexterity": s.Dexterity = Math.Clamp(s.Dexterity + delta, 2, 6); now = $"Dex {s.Dexterity}+"; break;
                    case "Willpower": s.Willpower = Math.Clamp(s.Willpower + delta, 2, 6); now = $"Wil {s.Willpower}+"; break;
                    case "Power":
                        s.Power = Math.Max(0, s.Power + delta);
                        hero.QuestPower = Math.Min(hero.QuestPower, s.Power);
                        now = $"max power {s.Power}";
                        break;
                    case "Level":   s.Level = Math.Max(1, s.Level + delta);       now = $"level {s.Level}"; break;
                    case "Credits": s.Credits = Math.Max(0, s.Credits + delta);   now = $"{s.Credits}c"; break;
                    case "Xp":      s.Xp = Math.Max(0, s.Xp + delta);             now = $"{s.Xp} XP"; break;
                }
            });
            if (now.Length > 0) ctx.Log($"Game Master: {hero.Name} is now {now}.");
        });

        /// <summary>
        /// Star Quest testing tool: forces the hero's next Str/Dex/Wil test to pass or to fail, then clears
        /// itself. <paramref name="mode"/> is 1 to pass, -1 to fail, 0 to cancel. Without it, testing a
        /// skill that only does something on a pass means re-rolling until the dice agree.
        /// </summary>
        public void HeroForceNextTest(UnitID unitID, int mode) => PostUnit(unitID, "Force the next test", (ctx, hero) =>
        {
            if (mode == 0)
            {
                QuestActions.ForcedNextTest.Remove(hero.ID);
                ctx.Log($"Game Master: {hero.Name}'s next test rolls normally again.");
                return;
            }
            QuestActions.ForcedNextTest[hero.ID] = mode;
            ctx.Log($"Game Master: {hero.Name}'s next stat test will {(mode > 0 ? "pass" : "fail")}.");
        });

        public void HeroXp(UnitID unitID, int xp) => Post($"+{xp} XP", async ctx =>
        {
            if (FindUnit(ctx, unitID) is UnitData hero) await QuestLevelling.GiveXp(ctx, hero, xp);
        });

        public void HeroSpellTokens(UnitID unitID, int count) => Post("Spell tokens", async ctx =>
        {
            if (FindUnit(ctx, unitID) is UnitData hero) await QuestTreasure.AddSpellTokens(ctx, hero, count);
        });

        public void HeroGiveItem(UnitID unitID, string item) => Post($"Give {item}", async ctx =>
        {
            if (FindUnit(ctx, unitID) is UnitData hero) await QuestTreasure.Give(ctx, hero, new[] { item });
        });

        public void HeroRemoveItem(UnitID unitID, string item) => PostUnit(unitID, $"Remove {item}", (ctx, hero) =>
        {
            QuestTreasure.EditSheet(hero, s => s.Items.Remove(item));
            ctx.Log($"Game Master: {hero.Name} loses {item}.");
        });

        public void HeroToggleCondition(UnitID unitID, string condition) => PostUnit(unitID, $"Toggle {condition}", (ctx, hero) =>
        {
            bool had = QuestTreasure.Has(hero, condition);
            if (had) QuestTreasure.RemoveCondition(hero, condition); else QuestTreasure.AddCondition(hero, condition);
            ctx.Log($"Game Master: {hero.Name} {(had ? "is no longer" : "is now")} {condition}.");
        });

        /// <summary>Gives or takes away an injury, with its table effects.</summary>
        public void HeroToggleInjury(UnitID unitID, string injury) => PostUnit(unitID, $"Toggle {injury}", (ctx, hero) =>
        {
            bool had = QuestActions.Sheet(hero)?.Injuries.Contains(injury) == true;
            if (had) QuestMission.RemoveInjury(hero, injury);
            else
            {
                QuestTreasure.EditSheet(hero, s => s.Injuries.Add(injury));
                QuestMission.ApplyInjury(ctx, hero, injury);
            }
            ctx.Log($"Game Master: {hero.Name} {(had ? "loses" : "gets")} a {injury}.");
        });

        /// <summary>Teaches (or forgets) a skill, so any skill can be tried without levelling up.</summary>
        public void HeroToggleSkill(UnitID unitID, string skill) => PostUnit(unitID, $"Toggle {skill}", (ctx, hero) =>
        {
            bool had = QuestActions.Sheet(hero)?.Skills.Contains(skill) == true;
            QuestTreasure.EditSheet(hero, s => { if (had) s.Skills.Remove(skill); else s.Skills.Add(skill); });
            ctx.Log($"Game Master: {hero.Name} {(had ? "forgets" : "learns")} {skill}.");
        });

        /// <summary>Makes the class feature usable again this game.</summary>
        public void HeroResetFeat(UnitID unitID) => PostUnit(unitID, "Reset feat", (ctx, hero) =>
        {
            hero.QuestFeatsUsed.Clear();
            ctx.Log($"Game Master: {hero.Name} can use its class feature again.");
        });

        public void HeroKnockOut(UnitID unitID) => PostUnit(unitID, "Knock out", (ctx, hero) =>
        {
            KnockOut(hero);
            ctx.Log($"Game Master: {hero.Name} is knocked out.");
        });

        /// <summary>Wakes an unconscious hero with 3 wounds, where it fell.</summary>
        public void HeroRevive(UnitID unitID) => PostUnit(unitID, "Revive hero", (ctx, hero) =>
        {
            QuestActions.Revive(hero, 3);
            ctx.Log($"Game Master: {hero.Name} is revived with 3 wounds.");
        });

        // ---------------------------------------------------------------- helpers

        private static void KnockOut(UnitData hero)
        {
            foreach (DataBinding<ModelData> binding in hero.ModelBindings)
            {
                ModelData model = binding.GetValue();
                if (model.GetIsAlive()) model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
        }

        private static QuestProgress Quest(IGameContext ctx) => GameProgressUtilities.GetQuestProgress(ctx.GameDataStore);

        private static void Save(IGameContext ctx, QuestProgress q) => GameProgressUtilities.SetQuestProgress(ctx.GameDataStore, q);

        private void EditQuest(string what, Action<IGameContext, QuestProgress> change) => Post(what, ctx =>
        {
            QuestProgress q = Quest(ctx);
            change(ctx, q);
            Save(ctx, q);
            return Task.CompletedTask;
        });

        private void EditQuestAsync(string what, Func<IGameContext, Task> change) => Post(what, change);
    }
}
