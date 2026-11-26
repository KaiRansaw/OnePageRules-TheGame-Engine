using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FDG.Data;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;
using FDG.StateMachine;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// Command Points - the six doctrines' stratagems (GF p43, GFF p40, AoF p43, AoFS p40; the same text in all four
    /// books). A player has the Universal Doctrine plus the one doctrine they picked before the game.
    ///
    /// Built so far (3.23): the ones that land on a moment the game already has - Push Forward / Sweeping Move (on the
    /// action menu), Lightning Reflexes / Heightened Senses (the cover check), Disrupted Sight (the hit roll), Armour
    /// Breaker (the Defense roll), Stand Strong / Terrorize (the Morale Test), Code of Honor (Shaken / Routed).
    /// Judgement calls: house rules C, "Command Points - doctrine stratagems".
    /// </summary>
    public static partial class CommandPoints
    {
        public const string PushForward = "Push Forward", HinderedAdvance = "Hindered Advance",
            CoordinatedMove = "Coordinated Move", StrategicRelocation = "Strategic Relocation",
            EternalVigilance = "Eternal Vigilance", LightningReflexes = "Lightning Reflexes",
            ArmourBreaker = "Armour Breaker", TacticalRetreat = "Tactical Retreat",
            SweepingMove = "Sweeping Move", HitAndRun = "Hit & Run", CombatFatigue = "Combat Fatigue",
            KillingBlow = "Killing Blow", HeightenedSenses = "Heightened Senses", DisruptedSight = "Disrupted Sight",
            FrenziedAttack = "Frenzied Attack", ClosingFire = "Closing Fire", MinimizeLosses = "Minimize Losses",
            StandStrong = "Stand Strong", Terrorize = "Terrorize", CodeOfHonor = "Code of Honor",
            RushObjective = "Rush Objective", SupremeCaster = "Supreme Caster", Vanish = "Vanish",
            TotalShutdown = "Total Shutdown";

        /// <summary>Every doctrine stratagem: its doctrine (1-6), cost and the book's text.</summary>
        public static readonly IReadOnlyList<(int Doctrine, string Name, int Cost, string Text)> DoctrineStratagems =
            new (int, string, int, string)[]
        {
            (1, PushForward, 1, "Pick one friendly unit that is using an Advance or Rush action, and it may add +2\" to its movement."),
            (1, HinderedAdvance, 1, "Pick one enemy unit that is moving through terrain, which counts as Dangerous terrain for that movement."),
            (1, CoordinatedMove, 2, "During your turn, pick two friendly units that are within 12\" of each other, which may each move by up to 3\" in any direction."),
            (1, StrategicRelocation, 3, "Pick one friendly unit that has just been activated, but that hasn't done anything yet, which may be placed anywhere within 6\" of its position."),
            (2, EternalVigilance, 1, "Pick one friendly model that just suffered a wound which would kill it and roll one die, on a 4+ that wound is ignored."),
            (2, LightningReflexes, 1, "Pick one friendly unit that is being shot at, which counts as being in Cover terrain for that shooting."),
            (2, ArmourBreaker, 2, "Pick one enemy unit that has just taken hits, which gets -1 to its Defense rolls against those hits."),
            (2, TacticalRetreat, 3, "Pick one friendly unit that is within 12\" of an enemy, and it may move up to 2D3+2\" directly away from the closest enemy."),
            (3, SweepingMove, 1, "Pick one friendly unit that is using a Charge action, and it may add +4\" to its movement."),
            (3, HitAndRun, 1, "Pick one friendly unit that has just finished fighting in melee, which may move by up to 3\" in any direction."),
            (3, CombatFatigue, 2, "Pick one enemy unit that is about to strike in melee and roll one die, on a 4+ that unit counts as Fatigued for that melee, but doesn't get Fatigued from that melee."),
            (3, KillingBlow, 3, "Pick one friendly unit that is fighting in melee. Any friendly model that is killed during this melee may strike one more time with all of its weapons before it is removed."),
            (4, HeightenedSenses, 1, "Pick one friendly unit that is shooting at an enemy in Cover terrain, which may ignore Cover for that shooting."),
            (4, DisruptedSight, 1, "Pick one enemy unit that is shooting, which gets -1 to hit rolls for that shooting."),
            (4, FrenziedAttack, 2, "Pick one friendly unit that shot at an enemy unit, which may shoot at another enemy unit within 12\" of it, but only hits on unmodified results of 6."),
            (4, ClosingFire, 3, "Pick one friendly unit that is being charged by an enemy unit, which may shoot at the charging unit at any point during its movement, but gets -1 to hit rolls."),
            (5, MinimizeLosses, 1, "Pick one friendly unit that just became Shaken, which may move up to 4D3+4\" directly away from the closest enemy."),
            (5, StandStrong, 1, "Pick one friendly unit that just failed a Morale Test, which may re-roll that Morale Test roll."),
            (5, Terrorize, 2, "Pick one enemy unit that just passed a Morale Test, which must re-roll that Morale Test roll."),
            (5, CodeOfHonor, 3, "Pick one friendly unit that would be Shaken or Routed. If it would be Shaken, it is not. If it would be Routed, then it is Shaken instead."),
            (6, RushObjective, 1, "Pick one friendly unit that is using a Rush action, and it may add +4\" to its movement if it ends up within 3\" of an objective."),
            (6, SupremeCaster, 1, "One friendly Caster gets 1 spell token, which may not be spent to give casters +1/-1 to their casting rolls."),
            (6, Vanish, 2, "At the end of the round, pick one friendly unit with Ambush and remove it from play (dropping any objectives), and which may be deployed again starting from the next round."),
            (6, TotalShutdown, 3, "At the end of the round, pick one objective marker under your control that is about to be seized by your opponent, which becomes neutral instead."),
        };

        /// <summary>The doctrine a stratagem belongs to (0 = the Universal Doctrine, everyone has it).</summary>
        public static int DoctrineOfStratagem(string stratagem) =>
            DoctrineStratagems.FirstOrDefault(d => d.Name == stratagem).Doctrine;

        /// <summary>The stratagems a player can use: the Universal ones and their own doctrine's.</summary>
        public static IEnumerable<(string Name, int Cost, string Text)> StratagemsOf(IGameContext c, PlayerID player) =>
            Universal.Concat(DoctrineStratagems.Where(d => d.Doctrine == DoctrineOf(c, player)).Select(d => (d.Name, d.Cost, d.Text)));

        private static bool HasDoctrine(IGameContext c, PlayerID player, string stratagem) =>
            DoctrineOfStratagem(stratagem) is var d && (d == 0 || DoctrineOf(c, player) == d);

        private static string RoundKey(IGameContext c, string stratagem, PlayerID p) =>
            $"{stratagem}:{GameProgressUtilities.TryGetProgress(c.GameDataStore)?.RoundCount ?? 1}:{p}";

        /// <summary>
        /// One offer: the player has the stratagem and the CP, hasn't used it this activation and (when
        /// <paramref name="askOncePerRound"/>) hasn't turned it down this round. Spends it on a yes.
        /// </summary>
        private static async Task<bool> Offer(IGameContext c, PlayerID player, string stratagem, string question,
            bool botSays, bool askOncePerRound)
        {
            if (!On(c) || !Can(c, player, stratagem)) return false;
            string key = RoundKey(c, stratagem, player);
            if (askOncePerRound && WasDeclined(c, key)) return false;
            int cost = CostOf(stratagem);
            bool yes = await c.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(player,
                $"{Head(c, player, stratagem, cost)}: {question}"
                + (askOncePerRound ? " (No = not asked again this round.)" : ""),
                defaultAnswer: botSays, displayName: "Deciding on a stratagem"));
            if (!yes) { if (askOncePerRound) Decline(c, key); return false; }
            if (!Use(c, player, stratagem)) return false;
            await c.Announce($"{c.GetPlayerName(player)}: {stratagem}!", BattlefieldEffects.Amber, EBannerTier.Toast);
            return true;
        }

        private static IEnumerable<PlayerID> OpponentsOf(IGameContext c, PlayerID player)
        {
            ITeam? mine = c.TableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(player));
            return c.TableState.Teams.Objects.Where(t => t != mine).SelectMany(t => t.Players);
        }

        private static void Mark(IUnit unit, TokenType t)
        {
            if (!unit.Tokens.HasToken(t)) unit.Tokens.AddToken(TokenDefinitionCatalog.Create(t));
        }

        private static readonly TokenType[] ActivationTokens =
        {
            TokenType.CpLightningReflexes, TokenType.CpHeightenedSenses, TokenType.CpDisruptedSight,
            TokenType.CpPushForward, TokenType.CpSweepingMove, TokenType.CpCombatFatigue,
            TokenType.CpClosingFire, TokenType.CpFrenziedAttack, TokenType.CpRushObjective,
        };

        /// <summary>The end of a unit activation: the "for that shooting / movement" stratagems end.</summary>
        public static void EndOfActivation(IGameContext c)
        {
            if (!On(c)) return;
            ShotTargets.Remove(c.GameDataStore);
            EndOfMelee(c.TableState.Units.Objects.ToArray());
            foreach (IUnit unit in c.TableState.Units.Objects)
                foreach (TokenType t in ActivationTokens)
                {
                    int n = unit.Tokens.GetTokenCount(t);
                    if (n > 0) unit.Tokens.RemoveTokens(t, n);
                }
        }

        // ================================================================= Strategic / Shock: movement on the menu

        /// <summary>Push Forward / Sweeping Move as entries on a fresh activation's action menu (name, cost text).
        /// Picked = the CP are spent and the move gets its +2" / +4"; backing out of the move refunds it.</summary>
        public static IEnumerable<(string Option, string Stratagem, string Text)> MoveOptions(IGameContext c, IUnit unit,
            bool canMove, bool canCharge)
        {
            if (!On(c)) yield break;
            if (canMove && Can(c, unit.PlayerID, PushForward) && HasDoctrine(c, unit.PlayerID, PushForward))
                yield return ($"{PushForward} (1 CP)", PushForward, "Command Points: Advance or Rush with +2\" to the move.");
            if (canCharge && Can(c, unit.PlayerID, SweepingMove) && HasDoctrine(c, unit.PlayerID, SweepingMove))
                yield return ($"{SweepingMove} (1 CP)", SweepingMove, "Command Points: Charge with +4\" to the move.");
            if (canMove && Can(c, unit.PlayerID, StrategicRelocation))
                yield return ($"{StrategicRelocation} (3 CP)", StrategicRelocation,
                    "Command Points: place this unit anywhere within 6\" of where it stands, then carry on with this activation.");
            if (canMove && Can(c, unit.PlayerID, CoordinatedMove) && CoordinatedPartners(c, unit).Any())
                yield return ($"{CoordinatedMove} (2 CP)", CoordinatedMove,
                    "Command Points: this unit and another friendly unit within 12\" each move up to 3\" in any direction.");
            if (canMove && Can(c, unit.PlayerID, RushObjective) && c.TableState.Objectives.Objects.Any())
                yield return ($"{RushObjective} (1 CP)", RushObjective,
                    "Command Points: Rush with +4\" to the move - only if it ends within 3\" of an objective.");
            if (CanSupremeCaster(c, unit))
                yield return ($"{SupremeCaster} (1 CP)", SupremeCaster,
                    "Command Points: this Caster gets 1 spell token for a spell's cost (it can't boost a casting roll).");
            if (CanTacticalRetreat(c, unit))
                yield return ($"{TacticalRetreat} (3 CP)", TacticalRetreat,
                    "Command Points: fall back 2D3+2\" directly away from the closest enemy, then carry on with this activation.");
        }

        /// <summary>Whether an action-menu entry is one of these (bots leave them alone).</summary>
        public static bool IsMoveOption(string option) =>
            option == $"{PushForward} (1 CP)" || option == $"{SweepingMove} (1 CP)" || option == $"{TacticalRetreat} (3 CP)"
            || option == $"{StrategicRelocation} (3 CP)" || option == $"{CoordinatedMove} (2 CP)"
            || option == $"{RushObjective} (1 CP)" || option == $"{SupremeCaster} (1 CP)";

        /// <summary>The move option was picked: spend it and mark the unit.</summary>
        public static bool TakeMoveOption(IGameContext c, IUnit unit, string stratagem)
        {
            if (!Use(c, unit.PlayerID, stratagem)) return false;
            Mark(unit, stratagem == PushForward ? TokenType.CpPushForward
                : stratagem == RushObjective ? TokenType.CpRushObjective : TokenType.CpSweepingMove);
            c.Log($"{unit.Name}: {stratagem} ({CostOf(stratagem)} CP).");
            return true;
        }

        /// <summary>The move was backed out of before anything happened: give the CP back.</summary>
        public static void RefundMoveOption(IGameContext c, IUnit unit)
        {
            foreach ((TokenType t, string name) in new[] { (TokenType.CpPushForward, PushForward), (TokenType.CpSweepingMove, SweepingMove),
                         (TokenType.CpRushObjective, RushObjective) })
            {
                if (!unit.Tokens.HasToken(t)) continue;
                unit.Tokens.RemoveTokens(t, unit.Tokens.GetTokenCount(t));
                if (State(c.GameDataStore) is not { } s) continue;
                s.Points[unit.PlayerID.ToString()] = s.Points.GetValueOrDefault(unit.PlayerID.ToString()) + CostOf(name);
                s.Used.Remove(UseKey(unit.PlayerID, name));
                Save(c, s);
                c.Log($"{unit.Name}: {name} withdrawn - {CostOf(name)} CP back.");
            }
        }

        /// <summary>The extra inches the unit's move gets (Advance, Rush, Charge).</summary>
        public static (float Advance, float Rush, float Charge) MoveBonus(IUnit unit) =>
            unit.Tokens.HasToken(TokenType.CpPushForward) ? (2f, 2f, 0f)
            : unit.Tokens.HasToken(TokenType.CpRushObjective) ? (0f, 4f, 0f)
            : unit.Tokens.HasToken(TokenType.CpSweepingMove) ? (0f, 0f, 4f) : (0f, 0f, 0f);

        // ================================================================= Defensive / Hunting: shooting

        /// <summary>
        /// The cover check of a shooting: the target's player may use Lightning Reflexes (not in cover -> counts as in
        /// Cover), then the shooter's player Heightened Senses (in cover -> ignore it). Returns the cover bonus to use.
        /// A "no" isn't asked again that round. Bots always say yes.
        /// </summary>
        public static async Task<int> CoverStratagems(IGameContext c, IUnit shooter, IUnit target, int bonus, bool weaponIgnoresCover)
        {
            if (!On(c)) return bonus;
            if (target.Tokens.HasToken(TokenType.CpLightningReflexes) && bonus == 0 && !weaponIgnoresCover) bonus = 1;
            else if (bonus == 0 && !weaponIgnoresCover && !shooter.Tokens.HasToken(TokenType.CpHeightenedSenses)
                && HasDoctrine(c, target.PlayerID, LightningReflexes)
                && await Offer(c, target.PlayerID, LightningReflexes,
                    $"{target.Name} is being shot at by {shooter.Name} out of cover. Count as in Cover for this shooting (+1 Defense)?",
                    botSays: true, askOncePerRound: true))
            {
                Mark(target, TokenType.CpLightningReflexes);
                bonus = 1;
            }
            if (bonus <= 0) return bonus;
            if (shooter.Tokens.HasToken(TokenType.CpHeightenedSenses)) return 0;
            if (HasDoctrine(c, shooter.PlayerID, HeightenedSenses)
                && await Offer(c, shooter.PlayerID, HeightenedSenses,
                    $"{target.Name} is in Cover. {shooter.Name} ignores Cover for this shooting?",
                    botSays: true, askOncePerRound: true))
            {
                Mark(shooter, TokenType.CpHeightenedSenses);
                return 0;
            }
            return bonus;
        }

        /// <summary>Disrupted Sight, offered to the target's player before a shooting's hit roll (only when -1 could
        /// matter: the shooter needs 5+ or better). A "no" isn't asked again that round. Bots say yes.</summary>
        public static async Task OfferDisruptedSight(IGameContext c, IUnit shooter, IUnit target, int hitNeed)
        {
            if (!On(c) || shooter.Tokens.HasToken(TokenType.CpDisruptedSight) || hitNeed >= 6) return;
            if (!HasDoctrine(c, target.PlayerID, DisruptedSight)) return;
            if (await Offer(c, target.PlayerID, DisruptedSight,
                    $"{shooter.Name} is shooting at {target.Name}. Give it -1 to hit for this shooting?",
                    botSays: true, askOncePerRound: true))
                Mark(shooter, TokenType.CpDisruptedSight);
        }

        /// <summary>The hit modifiers from the doctrine tokens (Disrupted Sight -1 when shooting).</summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IUnit attacker, bool melee, bool spell)
        {
            if (melee || spell) return Array.Empty<GrantedModifier>();
            var list = new List<GrantedModifier>();
            if (attacker.Tokens.HasToken(TokenType.CpDisruptedSight)) list.Add(new GrantedModifier(DisruptedSight, -1));
            if (attacker.Tokens.HasToken(TokenType.CpClosingFire)) list.Add(new GrantedModifier(ClosingFire, -1));
            return list;
        }

        /// <summary>Armour Breaker, offered to the attacker's player once hits have landed and the target can still
        /// save (its Defense roll needs 6+ or less). True = -1 to those Defense rolls. Bots: yes on 2+ hits.</summary>
        public static Task<bool> OfferArmourBreaker(IGameContext c, IUnit attacker, IUnit target, int hits, int saveNeed)
        {
            if (!On(c) || hits <= 0 || saveNeed > 6 || !HasDoctrine(c, attacker.PlayerID, ArmourBreaker)) return Task.FromResult(false);
            return Offer(c, attacker.PlayerID, ArmourBreaker,
                $"{target.Name} took {hits} hit{(hits == 1 ? "" : "s")} from {attacker.Name}. Give it -1 to its Defense rolls against them?",
                botSays: hits >= 2, askOncePerRound: true);
        }

        // ================================================================= Valorous: morale

        /// <summary>Stand Strong: the unit's player may re-roll a failed Morale Test. Bots say yes.</summary>
        public static Task<bool> OfferStandStrong(IGameContext c, IUnit unit) =>
            HasDoctrine(c, unit.PlayerID, StandStrong)
                ? Offer(c, unit.PlayerID, StandStrong, $"{unit.Name} failed its Morale Test. Re-roll it?", botSays: true, askOncePerRound: false)
                : Task.FromResult(false);

        /// <summary>Terrorize: an opponent may make the unit re-roll a Morale Test it just passed. Bots say yes.</summary>
        public static async Task<bool> OfferTerrorize(IGameContext c, IUnit unit)
        {
            if (!On(c)) return false;
            foreach (PlayerID o in OpponentsOf(c, unit.PlayerID))
                if (HasDoctrine(c, o, Terrorize)
                    && await Offer(c, o, Terrorize, $"{unit.Name} passed its Morale Test. Make it re-roll?", botSays: true, askOncePerRound: false))
                    return true;
            return false;
        }

        /// <summary>
        /// Code of Honor: the unit's player may spend 3 CP when it would be Shaken (it isn't) or Routed (it is Shaken
        /// instead). Not offered when a unit that is already Shaken would be Shaken again (nothing would change).
        /// Bots say yes.
        /// </summary>
        public static Task<bool> OfferCodeOfHonor(IGameContext c, IUnit unit, bool routed)
        {
            if (!On(c) || !unit.GetIsAlive() || (!routed && unit.Tokens.HasToken(TokenType.Shaken))
                || !HasDoctrine(c, unit.PlayerID, CodeOfHonor)) return Task.FromResult(false);
            return Offer(c, unit.PlayerID, CodeOfHonor,
                routed ? $"{unit.Name} would be Routed. Make it Shaken instead?" : $"{unit.Name} would be Shaken. Stop it?",
                botSays: true, askOncePerRound: false);
        }
    }
}
