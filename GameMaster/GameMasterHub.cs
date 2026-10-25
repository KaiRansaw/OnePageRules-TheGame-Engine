using System.Collections.Concurrent;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Stages;
using FDG.StateMachine;

namespace FDG.GameMasterTools
{
    /// <summary>
    /// Game Master mode (Play Style = Game Master): host-only testing tools that change the game out of
    /// order - end or jump rounds, force Horde waves / rewards / damage, move any unit anywhere, edit
    /// wounds, Shaken, XP and traits, and edit the Horde score, objectives and priority.
    /// <para>
    /// <b>Threading.</b> The front end calls the public methods from the UI thread; each one only QUEUES
    /// its change. The engine applies the queue at safe points (see <see cref="GameMasterRequester"/>):
    /// right before the game asks any player for a decision, and while it is waiting on a human's
    /// decision - never while a bot is mid-calculation, so no edit can race the bot's own reads.
    /// </para>
    /// <para>
    /// <see cref="Active"/> is the one GM game in this process (the GUI host's). Remote clients never
    /// get one, which is what keeps the tools host-only.
    /// </para>
    /// </summary>
    public sealed partial class GameMasterHub
    {
        /// <summary>The Game Master game running in this process, or null.</summary>
        public static GameMasterHub? Active { get; private set; }

        private readonly ConcurrentQueue<(string What, Func<IGameContext, Task> Apply)> _queue = new();
        private readonly object _signalLock = new();
        private TaskCompletionSource _signal = NewSignal();
        private IGameContext? _context;
        private bool _draining;

        private volatile bool _endRoundRequested;
        private int _jumpToRound;   // 0 = none

        internal static void SetActive(GameMasterHub? hub) => Active = hub;

        internal static void ClearIfActive(GameMasterHub hub)
        {
            if (ReferenceEquals(Active, hub)) Active = null;
        }

        internal void Attach(IGameContext context) => _context = context;

        /// <summary>Whether this is a Horde Mode game (the Horde tools only apply there).</summary>
        public bool IsHorde => _context?.Settings.GameMode == EGameMode.Horde;

        /// <summary>The live table, for the panel to read (units, objectives, progress).</summary>
        public ITableState? TableState => _context?.TableState;

        /// <summary>How many changes are queued and not yet applied (shown on the panel).</summary>
        public int PendingCount => _queue.Count;

        // ---------------------------------------------------------------- round control

        /// <summary>Ends the current round at the next turn: no further units activate this round.</summary>
        public void EndRoundNow()
        {
            _endRoundRequested = true;
            Post("End the round", ctx => { ctx.Log("Game Master: ending the round."); return Task.CompletedTask; });
        }

        /// <summary>Ends the current round and makes <paramref name="round"/> the next one (its start-of-round
        /// steps - Horde priority and waves - run as normal).</summary>
        public void JumpToRound(int round)
        {
            Interlocked.Exchange(ref _jumpToRound, Math.Max(1, round));
            _endRoundRequested = true;
            Post($"Jump to round {round}", ctx => { ctx.Log($"Game Master: jumping to round {round}."); return Task.CompletedTask; });
        }

        internal bool ConsumeEndRound()
        {
            if (!_endRoundRequested) return false;
            _endRoundRequested = false;
            return true;
        }

        /// <summary>
        /// A new round is starting: an "end the round" request belongs to the round it was made in, so a
        /// leftover one (pressed during deployment, or after the round had already finished) is dropped
        /// rather than ending the fresh round on its first turn.
        /// </summary>
        internal void OnRoundStart() => _endRoundRequested = false;

        internal int? ConsumeJumpToRound()
        {
            int round = Interlocked.Exchange(ref _jumpToRound, 0);
            return round > 0 ? round : null;
        }

        // ---------------------------------------------------------------- Horde state

        public void DeployWaveNow() => Post("Deploy a wave", async ctx =>
        {
            int round = ctx.TableState.Progress.RoundCount ?? 1;
            await new HordeRoundHooks(ctx).DeployWave(Math.Max(2, HordeRules.WaveOfRound(round)));
        });

        public void AwardWavePointsNow() => Post("Award wave points", async ctx =>
        {
            int round = ctx.TableState.Progress.RoundCount ?? 1;
            await new HordeRoundHooks(ctx).AwardWavePoints(HordeRules.WaveOfRound(round));
        });

        /// <summary>Opens the Horde shop right away. Every objective on the table counts as seized, so
        /// recruits can be tried at any of them.</summary>
        public void OpenShopNow() => Post("Open the shop", async ctx =>
        {
            int round = ctx.TableState.Progress.RoundCount ?? 1;
            List<ObjectiveData> all = ctx.GameDataStore.GetAllValues<ObjectiveData>().ToList();
            await HordeShop.Run(ctx, all, HordeRules.WaveOfRound(round));
        });

        public void DamageObjectivesNow() => Post("Damage objectives", ctx => new HordeRoundHooks(ctx).DamageObjectives());

        public void RollPriorityNow() => Post("Roll a new priority objective", ctx => new HordeRoundHooks(ctx).SetPriority());

        /// <summary>Makes objective <paramref name="number"/> the priority objective (null = none).</summary>
        public void SetPriority(int? number) => Post($"Priority objective {(number?.ToString() ?? "none")}", ctx =>
        {
            foreach (ObjectiveData objective in ctx.GameDataStore.GetAllValues<ObjectiveData>())
                objective.SetHordePriority(number != null && objective.HordeNumber == number);
            ctx.Log($"Game Master: priority objective set to {(number?.ToString() ?? "none")}.");
            return Task.CompletedTask;
        });

        /// <summary>Adds (or with a negative value removes) Horde points - earned and available alike.</summary>
        public void AdjustPoints(int delta) => Post($"Points {delta:+#;-#;0}", ctx =>
        {
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(ctx.GameDataStore);
            if (progress == null) return Task.CompletedTask;   // before the main phase
            HordeScore score = progress.Horde ?? new HordeScore(0, 0);
            progress.Horde = new HordeScore(Math.Max(0, score.PointsEarned + delta), Math.Max(0, score.PointsAvailable + delta));
            GameProgressUtilities.WriteProgress(ctx.GameDataStore, progress);
            ctx.Log($"Game Master: points now {progress.Horde.PointsAvailable}.");
            return Task.CompletedTask;
        });

        /// <summary>Adds or removes damage markers on objective <paramref name="number"/>; at 3 it is destroyed.</summary>
        public void AdjustObjectiveDamage(int number, int delta) => Post($"Objective {number} damage {delta:+#;-#;0}", ctx =>
        {
            foreach (DataBinding<ObjectiveData> binding in ctx.GameDataStore.GetAllDataBindings<ObjectiveData>().ToList())
            {
                ObjectiveData objective = binding.GetValue();
                if (objective.HordeNumber != number) continue;
                int damage = Math.Clamp(objective.HordeDamage + delta, 0, HordeRules.ObjectiveDamageToDestroy);
                if (damage >= HordeRules.ObjectiveDamageToDestroy)
                {
                    ctx.GameDataStore.Destroy(binding.Reference);
                    ctx.Log($"Game Master: objective {number} destroyed.");
                }
                else
                {
                    objective.SetHordeDamage(damage);
                    ctx.Log($"Game Master: objective {number} damage set to {damage}.");
                }
            }
            return Task.CompletedTask;
        });

        // ---------------------------------------------------------------- units

        /// <summary>Moves every on-table model of the unit by the offset, ignoring all rules (kept on the table).</summary>
        public void MoveUnit(UnitID unitID, float dxInches, float dzInches) => PostUnit(unitID, "Move", (ctx, unit) =>
        {
            foreach (DataBinding<ModelData> binding in unit.ModelBindings)
            {
                ModelData model = binding.GetValue();
                if (model.Position.x == 0f && model.Position.z == 0f) continue;   // off the table
                float x = Math.Clamp(model.Position.x + dxInches, 0f, GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES);
                float z = Math.Clamp(model.Position.z + dzInches, 0f, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
                model.SetPosition(new Position(x, z));
            }
            ctx.Log($"Game Master: moved {unit.Name}.");
        });

        /// <summary>Deals <paramref name="wounds"/> wounds, one at a time to the first living model.</summary>
        public void WoundUnit(UnitID unitID, int wounds = 1)
        {
            // GF: Firefight: wounds go through the wound effects (models die one per wound, then markers and a roll).
            Post("Wound", async ctx =>
            {
                UnitData? unit = FindUnit(ctx, unitID);
                if (unit == null) return;
                if (FDG.Stages.FirefightRules.IsSkirmishGame(ctx))
                {
                    ctx.Log($"Game Master: {unit.Name} takes {wounds} wound(s).");
                    await FDG.Stages.CasualtyPresentation.ApplyUnitWounds(ctx, unit, wounds);
                    return;
                }
                WoundUnitNow(ctx, unit, wounds);
            });
        }

        private static void WoundUnitNow(IGameContext ctx, UnitData unit, int wounds)
        {
            for (int i = 0; i < wounds; i++)
            {
                ModelData? model = unit.ModelBindings.Select(b => b.GetValue()).FirstOrDefault(m => m.GetIsAlive());
                model?.DealWounds(1);
            }
            ctx.Log($"Game Master: {unit.Name} takes {wounds} wound(s).");
        }

        /// <summary>Heals <paramref name="wounds"/> wounds, one at a time (a dead model can come back).</summary>
        public void HealUnit(UnitID unitID, int wounds = 1) => PostUnit(unitID, "Heal", (ctx, unit) =>
        {
            for (int i = 0; i < wounds; i++)
            {
                // GF: Firefight: a wound marker comes off first.
                if (FDG.Stages.FirefightRules.RemoveMarker(unit)) continue;
                ModelData? model = unit.ModelBindings.Select(b => b.GetValue())
                    .Where(m => m.WoundsDealt > 0f)
                    .OrderBy(m => m.GetIsAlive() ? 0 : 1)
                    .FirstOrDefault();
                if (model == null) break;
                model.RemainingWoundsBinding.SetValue(Math.Min(model.TotalWounds, model.RemainingWoundsBinding.GetValue() + 1f));
            }
            ctx.Log($"Game Master: {unit.Name} heals {wounds} wound(s).");
        });

        /// <summary>Destroys the unit outright (no kill credit, no XP).</summary>
        public void KillUnit(UnitID unitID) => PostUnit(unitID, "Kill", (ctx, unit) =>
        {
            foreach (DataBinding<ModelData> binding in unit.ModelBindings)
            {
                ModelData model = binding.GetValue();
                if (model.GetIsAlive()) model.DealWounds(model.TotalWounds - model.WoundsDealt);
            }
            ctx.Log($"Game Master: {unit.Name} destroyed.");
        });

        /// <summary>Restores every model to full wounds and clears Shaken / Fatigued.</summary>
        public void ReviveUnit(UnitID unitID) => PostUnit(unitID, "Revive", (ctx, unit) =>
        {
            HordePlacement.Revive(unit);
            ctx.Log($"Game Master: {unit.Name} restored to full strength.");
        });

        public void ToggleShaken(UnitID unitID) => PostUnit(unitID, "Toggle Shaken", (ctx, unit) =>
        {
            if (unit.Tokens.HasToken(TokenType.Shaken))
            {
                unit.Tokens.RemoveTokens(TokenType.Shaken, unit.Tokens.GetTokenCount(TokenType.Shaken));
                ctx.Log($"Game Master: {unit.Name} is no longer Shaken.");
            }
            else
            {
                unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Shaken));
                ctx.Log($"Game Master: {unit.Name} is Shaken.");
            }
        });

        /// <summary>Adds Horde XP, levelling up as normal (Specialist / Fast Learner picks are made
        /// automatically here, so a GM edit never opens a second prompt on top of the current one).</summary>
        public void AddXp(UnitID unitID, int xp) => Post($"+{xp} XP", async ctx =>
        {
            UnitData? unit = FindUnit(ctx, unitID);
            if (unit == null) return;
            await HordeExperience.AddXp(ctx, unit, xp, askForChoices: false);
        });

        /// <summary>Gives the unit one specific trait (a "Trait: X" rule name).</summary>
        public void GiveTrait(UnitID unitID, string traitRuleName) => PostUnit(unitID, "Give trait", (ctx, unit) =>
        {
            if (HordeExperience.OwnedTraits(unit).Contains(traitRuleName)) return;
            HordeExperience.Grant(unit, traitRuleName);
            ctx.Log($"Game Master: {unit.Name} gains {traitRuleName.Replace("Trait: ", "")}.");
        });

        // ---------------------------------------------------------------- queue

        private void PostUnit(UnitID unitID, string what, Action<IGameContext, UnitData> apply) =>
            Post(what, ctx =>
            {
                UnitData? unit = FindUnit(ctx, unitID);
                if (unit != null) apply(ctx, unit);
                return Task.CompletedTask;
            });

        private static UnitData? FindUnit(IGameContext ctx, UnitID unitID) =>
            ctx.GameDataStore.GetAllValues<UnitData>().FirstOrDefault(u => u.ID.Equals(unitID));

        private void Post(string what, Func<IGameContext, Task> apply)
        {
            _queue.Enqueue((what, apply));
            lock (_signalLock) _signal.TrySetResult();
        }

        private static TaskCompletionSource NewSignal() =>
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes when a change is queued (engine side).</summary>
        internal Task WhenCommandQueued()
        {
            lock (_signalLock)
            {
                if (!_queue.IsEmpty) return Task.CompletedTask;
                if (_signal.Task.IsCompleted) _signal = NewSignal();
                return _signal.Task;
            }
        }

        /// <summary>True while queued changes are being applied - a prompt a change itself raises must not
        /// re-enter the queue.</summary>
        internal bool IsDraining => _draining;

        /// <summary>Applies every queued change, in order (engine side, at a safe point).</summary>
        internal async Task DrainAsync()
        {
            if (_context == null || _draining) return;
            _draining = true;
            try
            {
                while (_queue.TryDequeue(out var command))
                {
                    try
                    {
                        await command.Apply(_context);
                    }
                    catch (Exception ex)
                    {
                        // A bad edit must never take the game down - log it and carry on.
                        _context.Log($"Game Master: '{command.What}' failed - {ex.Message}");
                    }
                }
            }
            finally
            {
                _draining = false;
            }
        }
    }
}
