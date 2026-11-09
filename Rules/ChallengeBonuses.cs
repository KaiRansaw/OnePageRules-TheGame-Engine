using System;
using System.Collections.Generic;
using System.Linq;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Stages;

namespace FDG
{
    /// <summary>
    /// The Solo &amp; Co-Op Rules v3.6.0 "Challenge Bonuses (optional)". Printed word for word the same in all
    /// four books (GF, GF: Firefight, AoF, AoF: Skirmish):
    /// <list type="bullet">
    /// <item>"Players may give the AI any of the following bonuses:"</item>
    /// <item>"When it's the AI's turn to activate a unit, it gets to activate D3 units instead"</item>
    /// <item>"If the AI is tied with the players for objective control or VPs, then all of its units get +1 to
    /// hit rolls"</item>
    /// <item>"If the AI is losing for objective control or VPs, then all of its units get +1 hit rolls and +1 to
    /// Defense rolls"</item>
    /// </list>
    ///
    /// <para>
    /// <b>The book's own difficulty for playing against the bot</b>, and a different thing from the campaign's
    /// Easy/Normal/Hard bands (#435), which size the enemy's FORCE. These buff its BEHAVIOUR, so the two stack.
    /// </para>
    ///
    /// <para>
    /// <b>Who "the AI" is:</b> every seat a bot is playing (<see cref="IGameContext.ComputerPlayers"/>), read
    /// from the live seats. In a bot-vs-bot game both sides get the bonuses, each measured against the other.
    /// </para>
    ///
    /// <para>
    /// <b>Which games:</b> the four systems with a Solo &amp; Co-Op book. The Quest games carry their own AI
    /// flowchart in their core rules and print no Challenge Bonuses, so they get none - not GF's by default.
    /// </para>
    ///
    /// <para>
    /// <b>"Tied" and "losing"</b> are read live at each roll, because the book's condition is a present-tense
    /// state ("if the AI is losing"). Scored by victory points when the Mission Cards are in play (the score
    /// that decides that game) and by objectives held otherwise; teammates pool, as they do for the result.
    /// "The players" with several opposing sides means the best of them: the AI is losing if any side is ahead
    /// of it, and tied if the best side is level with it.
    /// </para>
    /// </summary>
    public static class ChallengeBonuses
    {
        /// <summary>The name the bonuses go by on the dice chips and in the log.</summary>
        public const string Name = "Challenge Bonus";

        public enum EStanding
        {
            /// <summary>Not an AI seat, no opponent, or no score to compare.</summary>
            None,
            Ahead,
            Tied,
            Losing,
        }

        /// <summary>Whether this game system has Challenge Bonuses at all (it has a Solo &amp; Co-Op book).</summary>
        public static bool Applies(EGameSystem system) => !GameSystemInfo.IsQuest(system);

        /// <summary>The bonuses in force for this game - none outside the four Solo &amp; Co-Op systems.</summary>
        public static EChallengeBonuses Active(IGameContext game) =>
            Applies(game.Settings.GameSystem) ? game.Settings.ChallengeBonuses & EChallengeBonuses.All : EChallengeBonuses.None;

        public static bool IsAi(IGameContext game, PlayerID player) => game.ComputerPlayers.Contains(player);

        // ── +1 to hit / +1 Defense ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The Challenge Bonus to <paramref name="attacker"/>'s hit roll, as a named modifier for the dice chips:
        /// +1 when its owner is an AI that is tied (with the Tied bonus on) or losing (with the Losing bonus on).
        /// Empty otherwise. Not for spells - a spell's hits are not a hit roll.
        /// </summary>
        public static IReadOnlyList<GrantedModifier> HitModifiers(IGameContext game, IUnit attacker, bool isSpell)
        {
            if (isSpell || attacker == null) return Array.Empty<GrantedModifier>();
            EChallengeBonuses active = Active(game);
            if ((active & (EChallengeBonuses.TiedHitBonus | EChallengeBonuses.LosingBonus)) == 0)
                return Array.Empty<GrantedModifier>();
            if (!IsAi(game, attacker.PlayerID)) return Array.Empty<GrantedModifier>();

            EStanding standing = StandingOf(game, attacker.PlayerID);
            if (standing == EStanding.Tied && (active & EChallengeBonuses.TiedHitBonus) != 0)
                return new[] { new GrantedModifier($"{Name} (tied)", 1) };
            if (standing == EStanding.Losing && (active & EChallengeBonuses.LosingBonus) != 0)
                return new[] { new GrantedModifier($"{Name} (losing)", 1) };
            return Array.Empty<GrantedModifier>();
        }

        /// <summary>
        /// The Challenge Bonus to <paramref name="defender"/>'s Defense roll: +1 when its owner is an AI that is
        /// losing, with the Losing bonus on. Being tied gives no Defense bonus - the book gives that one only to
        /// a losing AI.
        /// </summary>
        public static IReadOnlyList<GrantedModifier> SaveModifiers(IGameContext game, IUnit defender)
        {
            if (defender == null) return Array.Empty<GrantedModifier>();
            if ((Active(game) & EChallengeBonuses.LosingBonus) == 0) return Array.Empty<GrantedModifier>();
            if (!IsAi(game, defender.PlayerID)) return Array.Empty<GrantedModifier>();
            return StandingOf(game, defender.PlayerID) == EStanding.Losing
                ? new[] { new GrantedModifier($"{Name} (losing)", 1) }
                : Array.Empty<GrantedModifier>();
        }

        /// <summary>
        /// Where <paramref name="player"/>'s side stands against the other sides right now: victory points when
        /// the Mission Cards are in play, objectives held otherwise, teammates pooled.
        /// </summary>
        public static EStanding StandingOf(IGameContext game, PlayerID player)
        {
            List<IPlayerSlotInfo> seats = game.TableState.Players.Objects.Where(s => s.IsFilled).ToList();
            IPlayerSlotInfo? own = seats.FirstOrDefault(s => s.PlayerID.Equals(player));
            if (own == null) return EStanding.None;

            Dictionary<int, int> byTeam = ScoreByTeam(game, seats);
            int mine = byTeam.GetValueOrDefault(own.TeamNumber);
            List<int> others = byTeam.Where(kv => kv.Key != own.TeamNumber).Select(kv => kv.Value).ToList();
            if (others.Count == 0) return EStanding.None;

            int best = others.Max();
            if (mine < best) return EStanding.Losing;
            return mine == best ? EStanding.Tied : EStanding.Ahead;
        }

        private static Dictionary<int, int> ScoreByTeam(IGameContext game, IReadOnlyList<IPlayerSlotInfo> seats)
        {
            var byTeam = new Dictionary<int, int>();
            foreach (IPlayerSlotInfo seat in seats) byTeam.TryAdd(seat.TeamNumber, 0);

            FDG.Missions.MissionCardState? cards = GameProgressUtilities.MissionCards(game.GameDataStore);
            if (cards != null && cards.Game != FDG.Missions.EMissionCardGame.Off)
            {
                foreach (IPlayerSlotInfo seat in seats)
                    byTeam[seat.TeamNumber] += cards.Vp.GetValueOrDefault(seat.SlotID);
                return byTeam;
            }

            foreach (IObjective objective in game.TableState.Objectives.Objects)
            {
                if (objective.OwnerID is not { } owner) continue;
                IPlayerSlotInfo? seat = seats.FirstOrDefault(s => s.PlayerID.Equals(owner));
                if (seat != null) byTeam[seat.TeamNumber] += 1;
            }
            return byTeam;
        }

        // ── D3 activations ───────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Called when the ordinary turn order hands <paramref name="player"/> a turn. For an AI with the bonus
        /// on, rolls the D3 and records the extra activations the run still owes; the run itself is played out
        /// by <see cref="TryContinueRun"/>. A roll of 1 is an ordinary turn.
        /// </summary>
        public static void OnTurnGiven(IGameContext game, int round, PlayerID player)
        {
            if ((Active(game) & EChallengeBonuses.ExtraActivations) == 0) return;
            if (!IsAi(game, player)) return;
            int? slot = SlotOf(game, player);
            if (slot == null) return;
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(game.GameDataStore);
            if (progress == null) return;

            int d3 = (HordePlacement.RollD6(game) + 1) / 2;
            progress.ChallengeRun = new ChallengeActivationRun { Slot = slot.Value, Round = round, Left = d3 - 1 };
            GameProgressUtilities.WriteProgress(game.GameDataStore, progress);

            game.Log(d3 == 1
                ? $"{Name}: {game.GetPlayerName(player)} rolls a 1 - one activation this turn."
                : $"{Name}: {game.GetPlayerName(player)} rolls a {d3} - activates {d3} units this turn.");
        }

        /// <summary>
        /// Whether the AI seat partway through a D3 run takes the next activation as well. True (and one
        /// activation spent from the run) while the run has activations left, it is still the same round, and
        /// the seat still has a unit that can activate. Any other state ends the run, so it can never stall a
        /// round or carry over into the next one.
        /// </summary>
        public static bool TryContinueRun(IGameContext game, int round, Func<PlayerID, bool> hasUnitsLeft,
            out PlayerID player)
        {
            player = default;
            GameProgressData? progress = GameProgressUtilities.TryGetProgress(game.GameDataStore);
            ChallengeActivationRun? run = progress?.ChallengeRun;
            if (progress == null || run == null || run.Left <= 0) return false;

            IPlayerSlotInfo? seat = game.TableState.Players.Objects
                .FirstOrDefault(s => s.IsFilled && s.SlotID == run.Slot);
            bool continues = run.Round == round
                && seat != null
                && (Active(game) & EChallengeBonuses.ExtraActivations) != 0
                && hasUnitsLeft(seat.PlayerID);

            if (!continues)
            {
                run.Left = 0;
                GameProgressUtilities.WriteProgress(game.GameDataStore, progress);
                return false;
            }

            run.Left -= 1;
            GameProgressUtilities.WriteProgress(game.GameDataStore, progress);
            player = seat!.PlayerID;
            game.LogDebug($"{Name}: {game.GetPlayerName(player)} activates again ({run.Left} more after this).");
            return true;
        }

        private static int? SlotOf(IGameContext game, PlayerID player)
        {
            foreach (IPlayerSlotInfo slot in game.TableState.Players.Objects)
                if (slot.PlayerID.Equals(player)) return slot.SlotID;
            return null;
        }
    }
}
