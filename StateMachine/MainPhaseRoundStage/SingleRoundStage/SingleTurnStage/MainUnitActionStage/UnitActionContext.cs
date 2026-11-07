
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Utilities;
using System.Collections.Generic;
using System.Linq;

namespace FDG.Stages
{

    public interface IUnitActionContext : IGameContextAccessor
    {
        public DataBinding<UnitData> ActivatingUnit { get; }

        public bool HasMoved { get; }

        public float MoveDistance { get;}

        /// <summary>
        /// #290 — the Advance allowance that was IN FORCE for this activation's move, captured when the
        /// move resolved. The shoot gate has to compare against this rather than re-deriving the
        /// allowance from the unit's live rules, because a one-shot (NextTrigger) granted movement rule -
        /// Robot Legions' "Inspiring Bots" granting Rapid Advance, say - is spent by
        /// <c>ExecuteMoveStage</c> the moment the move resolves. Re-deriving afterwards asks "what could
        /// this unit advance NOW", gets the smaller un-granted number, and calls the legal advance a rush.
        ///
        /// <para>0 until the unit moves; only meaningful when <see cref="HasMoved"/>.</para>
        /// </summary>
        public float MoveShootAllowance { get; }

        public bool HasAttacked { get; }

        /// <summary>
        /// The custom action (#010) the player chose in <see cref="ChooseActionStage"/>, handed to
        /// <see cref="CustomActionStage"/> to resolve. Null when no custom action is pending. Set by the
        /// chooser, cleared once the custom-action stage has resolved it (and at activation start).
        /// </summary>
        public AbilityOffer? PendingCustomAction { get; }

        public void SetPendingCustomAction(AbilityOffer offer);

        public void ClearPendingCustomAction();

        /// <summary>
        /// Whether the unit was Shaken at the instant this activation began (snapshotted in
        /// <see cref="Reset"/>). This is the signal that decides Shaken recovery: a unit Shaken
        /// at activation start must idle this activation and recover, whereas a unit that becomes
        /// Shaken *during* its activation keeps the token for its next one. Captured explicitly so
        /// the recovery rule doesn't have to infer "activation start" from the action flags.
        /// </summary>
        public bool StartedActivationShaken { get; }

        /// <summary>
        /// How far <paramref name="enemyUnit"/> was from the activating unit at the instant this activation
        /// began — before the unit moved. This is the distance a charge is *declared* from: the engine models
        /// Charge as the melee attack and the approach as a separate Move action, so "charges an enemy over 9"
        /// away" means the enemy was over 9" away when the unit started its activation and set off. Snapshotted
        /// in <see cref="Reset"/> against every enemy unit, because a charge's defender isn't picked until the
        /// move has already happened and the pre-move geometry is gone.
        ///
        /// False (and 0) for a unit that was dead, unreachable, or absent at activation start. Read by
        /// <c>CombatActionContext</c> to fill <see cref="ICombatMetadata.ChargeOriginDistanceInches"/>.
        /// </summary>
        public bool TryGetActivationStartDistanceTo(UnitID enemyUnit, out float distanceInches);

        /// <param name="advanceAllowanceInches">The Advance budget that was in force for this move -
        /// see <see cref="MoveShootAllowance"/>. Callers must pass the value they computed BEFORE the move
        /// resolved, never a fresh query afterwards.</param>
        public void RegisterMoveFinished(float distance, float advanceAllowanceInches);

        public void RegisterAttackedFinished();

        /// <summary>
        /// #249: whether <paramref name="spellName"/> has already been tried this activation. Caster(X) is
        /// "spend as many tokens as the spell's value to try casting one or more spells (only one try per
        /// spell)" — so casting several *different* spells in one activation is legal, but a failed cast
        /// cannot be re-attempted with the same spell until the caster's next activation. Keyed by name,
        /// which is what identifies a spell in the army-wide list.
        /// </summary>
        public bool HasAttemptedSpell(string spellName);

        /// <summary>Records a cast attempt (see <see cref="HasAttemptedSpell"/>). Called at the moment the
        /// cost is committed, so a failed roll counts as the one try just as a successful one does.</summary>
        public void RegisterSpellAttempt(string spellName);

        /// <summary>
        /// #419: whether this activation's single casting window has closed.
        ///
        /// <para>
        /// Caster(X) used to be "<b>at any point</b> before attacking" (GF 3.5.1, Quest 2.0.1), which let a
        /// caster cast, move, and cast a different spell - the casting window was effectively the whole
        /// activation up to the first attack. Every current book - GF, AoF and AoFS 3.6.0, and both Quest
        /// systems at 2.1.0 - words it "<b>Once per activation</b>, before attacking, spend as many tokens as
        /// the spell's value to try casting one or more spells (only one try per spell)". So there is now one
        /// window per activation, several spells may be tried inside it, and leaving it is final.
        /// </para>
        /// <para>
        /// What closes it: <b>moving after a spell has been attempted</b> (the cast-move-cast pattern the new
        /// wording outlaws) and <b>attacking</b>, always. Attacking closes it even though
        /// <see cref="HasAttacked"/> already blocks casting, because Star Quest's extra action clears that
        /// flag (<see cref="BeginQuestExtraAction"/>) and an extra action is not a new activation - a hero
        /// must not be able to attack, buy an action, and open a second casting window with it.
        /// </para>
        /// <para>
        /// What does NOT close it: moving BEFORE any cast (the window is "before attacking", not "before
        /// moving", so advance-then-cast is legal), and consecutive casts with nothing in between - that is
        /// the one window being used for the "one or more spells" the rule allows.
        /// </para>
        /// </summary>
        public bool CastingWindowClosed { get; }

        /// <summary>
        /// #248: true once ANYTHING irreversible has happened this activation beyond HasMoved/HasAttacked —
        /// an activation-start rule applied (token ops, reposition, ThisActivation grant), spell tokens were
        /// spent, a custom action resolved, a teleport landed. Gates the "back out to unit selection" offer:
        /// while false (and not moved/attacked), un-picking the unit is safe because re-activating it re-runs
        /// ActivationStartStage against unchanged state.
        /// </summary>
        public bool IrreversibleActionTaken { get; }

        /// <summary>Marks the activation dirty (see <see cref="IrreversibleActionTaken"/>). Called by every
        /// stage at the exact point it commits something that cannot be undone.</summary>
        public void MarkIrreversibleAction();

        public void Reset(DataBinding<UnitData> activatingUnit);

        /// <summary>Star Quest: the hero action picked in Choose Action (Rest / Skill / extra action / feature).</summary>
        public string? PendingQuestAction { get; set; }
        /// <summary>Star Quest: skill actions taken this activation.</summary>
        public int QuestSkillsUsed { get; set; }
        /// <summary>Star Quest: the 2-power extra action has been bought this activation.</summary>
        public bool QuestExtraUsed { get; set; }
        /// <summary>Star Quest: power bought another action - the move/attack slots open up again.</summary>
        public void BeginQuestExtraAction();
        /// <summary>
        /// Star Quest: the hero used its action to Hold without shooting. The action is spent (no move, no
        /// attack this action) but nothing was attacked, so the casting window stays open and the hero
        /// still has its Skill action - "any one action plus one Skill action (in any order)".
        /// </summary>
        public void RegisterQuestHoldWithoutShooting();
        /// <summary>Star Quest: set by <see cref="RegisterQuestHoldWithoutShooting"/> until an extra action
        /// is bought; the action menu offers no shot or charge while it is set.</summary>
        public bool QuestHeldWithoutShooting { get; }
        /// <summary>Star Quest: a hero Rallied - it no longer has to stay idle this activation.</summary>
        public void ClearStartedActivationShaken();
        /// <summary>Star Quest: the "when activated" condition checks have run this activation.</summary>
        public bool QuestActivationChecked { get; set; }
        /// <summary>GF: Firefight Campaign: the hero auras have been applied this activation.</summary>
        public bool CampaignActivationChecked { get; set; }

        /// <summary>
        /// The Extra Action (Advanced Rulebooks 3.6.0) this unit declared this activation - one of the
        /// <c>FDG.ExtraActions</c> names - or null for a regular action. It shapes what the menu offers
        /// after the declaration (a Hold-type action keeps the unit still, Stealth Move bars shooting...).
        /// </summary>
        public string? ExtraAction { get; set; }

        /// <summary>Heavy Charge: the enemies that were 3" to 6" away when it was declared - the only legal targets.</summary>
        public HashSet<UnitID>? HeavyChargeTargets { get; set; }
    }

    public class UnitActionContext : IUnitActionContext
    {
        public IGameContext GameContext { get; private set; }

        public DataBinding<UnitData> ActivatingUnit { get; private set; }

        public bool HasMoved { get; private set; }

        public float MoveDistance { get; private set; } = 0f;

        public float MoveShootAllowance { get; private set; } = 0f;

        public bool HasAttacked { get; private set; }

        public bool StartedActivationShaken { get; private set; }

        public AbilityOffer? PendingCustomAction { get; private set; }

        // Enemy UnitID -> distance at activation start. Rebuilt each Reset; empty until then.
        private readonly Dictionary<UnitID, float> _distanceAtActivationStart = new Dictionary<UnitID, float>();


        public UnitActionContext(IGameContext gameContext, DataBinding<UnitData> activatingUnit)
        {
            GameContext = gameContext;
            ActivatingUnit = activatingUnit;
        }

        public bool TryGetActivationStartDistanceTo(UnitID enemyUnit, out float distanceInches)
        {
            return _distanceAtActivationStart.TryGetValue(enemyUnit, out distanceInches);
        }

        public void SetPendingCustomAction(AbilityOffer offer)
        {
            PendingCustomAction = offer;
        }

        public void ClearPendingCustomAction()
        {
            PendingCustomAction = null;
        }

        public void RegisterMoveFinished(float distance, float advanceAllowanceInches)
        {
            HasMoved = true;
            MoveDistance = distance;
            MoveShootAllowance = advanceAllowanceInches;

            // #419 - a move only closes the casting window if the caster has already used it. Moving first
            // and casting afterwards is still legal ("once per activation, BEFORE ATTACKING"); it is
            // cast-move-cast that the 3.6.0 / 2.1.0 wording outlaws.
            if (_attemptedSpells.Count > 0) CastingWindowClosed = true;
        }

        public void RegisterAttackedFinished()
        {
            HasAttacked = true;

            // #419 - final, and deliberately not reopened by Star Quest's extra action below.
            CastingWindowClosed = true;
        }

        // #249 — spells tried this activation, by name. Cleared each Reset, like every other per-activation flag.
        private readonly HashSet<string> _attemptedSpells = new HashSet<string>(System.StringComparer.Ordinal);

        public bool HasAttemptedSpell(string spellName) => _attemptedSpells.Contains(spellName);

        public void RegisterSpellAttempt(string spellName) => _attemptedSpells.Add(spellName);

        public bool CastingWindowClosed { get; private set; }

        public bool IrreversibleActionTaken { get; private set; }

        public void MarkIrreversibleAction()
        {
            IrreversibleActionTaken = true;
        }

        //TODO: This pattern sucks, make a new instance of the context each time.
        public string? PendingQuestAction { get; set; }
        public int QuestSkillsUsed { get; set; }
        public bool QuestExtraUsed { get; set; }
        public bool QuestActivationChecked { get; set; }
        public bool CampaignActivationChecked { get; set; }
        public string? ExtraAction { get; set; }
        public HashSet<UnitID>? HeavyChargeTargets { get; set; }

        public void BeginQuestExtraAction()
        {
            QuestHeldWithoutShooting = false;
            HasMoved = false;
            MoveDistance = 0f;
            MoveShootAllowance = 0f;
            HasAttacked = false;

            // #419: CastingWindowClosed and _attemptedSpells are deliberately NOT cleared. An extra action
            // buys another action, not another activation, and Caster(X) is "once per activation".
        }

        public void RegisterQuestHoldWithoutShooting()
        {
            // HasMoved closes the move (and makes the activation non-fresh); the shot and charge are closed by
            // QuestHeldWithoutShooting. HasAttacked stays false and CastingWindowClosed is untouched - holding
            // is not attacking, so the hero may still cast.
            HasMoved = true;
            MoveDistance = 0f;
            MoveShootAllowance = 0f;
            QuestHeldWithoutShooting = true;
        }

        public bool QuestHeldWithoutShooting { get; private set; }

        public void ClearStartedActivationShaken() => StartedActivationShaken = false;

        public void Reset(DataBinding<UnitData> activatingUnit)
        {
            PendingQuestAction = null;
            ExtraAction = null;
            HeavyChargeTargets = null;
            QuestSkillsUsed = 0;
            QuestExtraUsed = false;
            QuestHeldWithoutShooting = false;
            QuestActivationChecked = false;
            CampaignActivationChecked = false;
            ActivatingUnit = activatingUnit;
            HasMoved = false;
            MoveDistance = 0f;
            MoveShootAllowance = 0f;
            HasAttacked = false;
            IrreversibleActionTaken = false;
            PendingCustomAction = null;
            _attemptedSpells.Clear();
            CastingWindowClosed = false;
            StartedActivationShaken = activatingUnit.GetValue().Tokens.HasToken(TokenType.Shaken);
            SnapshotDistancesToEnemies(activatingUnit);
        }

        // #197: the pre-move geometry a charge is declared from. Captured here, once, because by the time a
        // charge's defender is known the unit has already moved into base contact and the distance it charged
        // from is unrecoverable. Cheap: one min-distance pass per enemy unit, once per activation.
        private void SnapshotDistancesToEnemies(DataBinding<UnitData> activatingUnit)
        {
            _distanceAtActivationStart.Clear();

            IUnit self = activatingUnit.GetValue();
            PlayerID owner = self.PlayerID;

            // #097: an embarked unit's models sit at the origin, so measuring from them records the distance
            // from the table corner — which for a unit that disembarks and charges made every "charges an
            // enemy over 9in away" rule (#197) read garbage. At activation start the unit is physically
            // inside its transport, so the transport's geometry is what the charge is declared from.
            IUnit measureFrom = self;
            if (TransportUtilities.GetTransportId(self) is UnitID transportId)
            {
                UnitData? transport = GameContext.GameDataStore().GetAllValues<UnitData>()
                    .FirstOrDefault(unit => unit.ID == transportId);
                if (transport != null) measureFrom = transport;
            }

            TeamData? ownerTeam = GameContext.GameDataStore().GetAllValues<TeamData>()
                .FirstOrDefault(t => t.IsPlayerOnTeam(owner));
            IReadOnlyList<PlayerID> alliedPlayers = ownerTeam != null
                ? ownerTeam.Players
                : new List<PlayerID> { owner };

            foreach (ArmyData enemyArmy in GameContext.GameDataStore().GetAllValues<ArmyData>()
                         .Where(a => !alliedPlayers.Contains(a.PlayerID)))
            {
                foreach (DataBinding<UnitData> enemyBinding in enemyArmy.UnitBindings)
                {
                    IUnit enemy = enemyBinding.GetValue();
                    if (enemy.GetIsDead()) continue;

                    // Same measurement the charge/melee gates use: living model to living model, closest pair,
                    // including the vertical component.
                    _distanceAtActivationStart[enemy.ID] = UnitCompareUtilities.MinDistanceBetweenUnits(
                        measureFrom, enemy, out _, out _, includeVertical: true);
                }
            }
        }
    }

    public static class IUnitActionContextExtensions
    {
        public static PlayerID ActivatingPlayer(this IUnitActionContext context)
        {
            return context.ActivatingUnit.GetValue().PlayerID;
        }
    }
}