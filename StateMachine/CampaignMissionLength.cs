using FDG.Campaign;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// #466 - the two "Play Mission" rules of the 3.6.0 campaign books that decide how long a campaign
    /// mission lasts. GF, GF: Firefight, AoF and AoF: Skirmish all print them word for word:
    ///
    /// <list type="bullet">
    /// <item><b>Mission Length:</b> "Missions are played using the standard rules, however if after 4 rounds
    /// there is no winner, the game continues onto the next round until there is one winner."</item>
    /// <item><b>Conceding:</b> "At the beginning of any round players may concede, removing all of their
    /// units from the table, which don't count as casualties for this mission."</item>
    /// </list>
    ///
    /// <para>
    /// Both apply to a campaign game only (a unit on the table carries a campaign sheet - the same test the
    /// random events use) and never to the Quest games, whose Campaign Builders are their own books.
    /// </para>
    /// </summary>
    public static class CampaignMissionLength
    {
        /// <summary>
        /// House rule: the "until there is one winner" rounds stop here, as a tie. The books set no limit;
        /// a game that cannot produce a winner (two armies that never reach an objective) would otherwise
        /// never end. Twelve is the longest game OPR prints (Horde Mode). Recorded in claude/house-rules.md.
        /// </summary>
        public const int LastRound = 12;

        /// <summary>Whether this game is a campaign mission played by the four campaign books.</summary>
        public static bool Applies(IGameContext gameContext) =>
            gameContext.Settings.GameMode == EGameMode.Standard
            && !QuestActions.IsQuestGame(gameContext)
            && CampaignEventRunner.IsCampaignGame(gameContext.TableState);

        // ── Mission Length ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Whether a campaign mission plays another round after <paramref name="roundJustFinished"/>, which
        /// has reached the game's normal length. True when there is no one winner yet and the house-rule
        /// cap is not reached. Reads the state only - Mission Cards' final objectives are scored by the
        /// victory stage, once, when the game really ends.
        /// </summary>
        public static bool PlaysOn(IGameContext gameContext, int roundJustFinished)
        {
            if (!Applies(gameContext)) return false;
            if (roundJustFinished >= LastRound) return false;
            if (TeamsStillIn(gameContext.TableState).Count < 2) return false;
            return !HasOneWinner(gameContext);
        }

        /// <summary>
        /// Whether the game, ended now, would have exactly one winning side - by the same arithmetic the
        /// victory stage uses: the most objectives, pooled by team, or in a Mission Cards game the most
        /// victory points once each held objective has added its 1 VP.
        /// </summary>
        public static bool HasOneWinner(IGameContext gameContext)
        {
            ITableState table = gameContext.TableState;
            List<IPlayerSlotInfo> players = InPlayers(table);

            if (FDG.Missions.MissionCardGame.IsOn(gameContext)
                && GameProgressUtilities.MissionCards(gameContext.GameDataStore) is { } state)
            {
                var vpBySlot = new Dictionary<int, int>(state.Vp);
                if (!state.FinalObjectivesScored)
                {
                    foreach (IObjective objective in table.Objectives.Objects)
                    {
                        if (objective.OwnerID is not PlayerID owner) continue;
                        int slot = FDG.Missions.MissionCardGame.SlotOf(gameContext, owner);
                        if (slot < 0) continue;
                        vpBySlot[slot] = vpBySlot.TryGetValue(slot, out int had) ? had + 1 : 1;
                    }
                }
                var byTeam = players.GroupBy(p => p.TeamNumber)
                    .Select(g => g.Sum(p => vpBySlot.TryGetValue(p.SlotID, out int v) ? v : 0))
                    .OrderByDescending(v => v).ToList();
                return byTeam.Count == 1 || (byTeam.Count > 1 && byTeam[0] != byTeam[1]);
            }

            return TeamScoreTally.TopTeams(TeamScoreTally.Build(players, table.Objectives.Objects)).Count == 1;
        }

        // ── Conceding ────────────────────────────────────────────────────────────────────────────

        /// <summary>Whether <paramref name="player"/> has conceded this mission.</summary>
        public static bool HasConceded(ITableState table, PlayerID player) =>
            table.Units.Objects.Any(u => u.PlayerID.Equals(player) && u.Tokens.HasToken(TokenType.Conceded));

        /// <summary>The filled player slots that have not conceded.</summary>
        public static List<IPlayerSlotInfo> InPlayers(ITableState table) =>
            table.Players.Objects.Where(p => p.IsFilled && !HasConceded(table, p.PlayerID)).ToList();

        /// <summary>The teams with at least one player who has not conceded, in slot order.</summary>
        public static List<int> TeamsStillIn(ITableState table) =>
            InPlayers(table).OrderBy(p => p.SlotID).Select(p => p.TeamNumber).Distinct().ToList();

        /// <summary>
        /// The round-start step: every player still in is asked whether they concede (their machine
        /// answers with the Concede button's state, a bot always "no"). Each one that does leaves the
        /// table. Returns true when only one side is left, so the caller goes straight to the result.
        /// </summary>
        public static async Task<bool> RunRoundStart(IGameContext gameContext, int round)
        {
            if (!Applies(gameContext)) return false;
            ITableState table = gameContext.TableState;

            bool anyConceded = false;
            foreach (IPlayerSlotInfo player in InPlayers(table).OrderBy(p => p.SlotID))
            {
                bool concede = await gameContext.PlayerRequester.RequestDecision<ConcedeCheckRequest, bool>(
                    new ConcedeCheckRequest(player.PlayerID, round));
                if (!concede) continue;

                await Concede(gameContext, player);
                anyConceded = true;
            }

            return anyConceded && TeamsStillIn(table).Count < 2;
        }

        /// <summary>
        /// "Removing all of their units from the table, which don't count as casualties": every living unit
        /// of <paramref name="player"/> leaves the table the way an Ambush unit does (models parked off the
        /// table, held in reserve) and is marked <see cref="TokenType.Conceded"/>, which keeps it from ever
        /// arriving again. Its models stay alive, so the campaign's casualty check passes it over.
        /// Objectives the player held go neutral: nobody of theirs is left to hold them (a judgement call -
        /// the books do not say - recorded in claude/house-rules.md).
        /// </summary>
        public static async Task Concede(IGameContext gameContext, IPlayerSlotInfo player)
        {
            ITableState table = gameContext.TableState;

            foreach (IUnit unit in table.Units.Objects.Where(u => u.PlayerID.Equals(player.PlayerID)).ToList())
            {
                // The mark goes on every unit, the dead too, so a player whose army is already gone still
                // reads as conceded. A dead unit stays a casualty: the casualty check reads whether it is
                // alive, not this mark.
                if (!unit.GetIsAlive())
                {
                    unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Conceded));
                    continue;
                }

                // Positions first, then reserve - the same order as an Ambush unit leaving the table
                // (GameOperationServices), so a networked client never draws a half-removed unit.
                foreach (IModel model in unit.Models)
                    if (model is ModelData data) data.SetPosition(new Position());

                ReserveRules.PlaceInReserve(unit);
                unit.Tokens.RemoveTokens(TokenType.PendingAmbushArrival);
                unit.Tokens.RemoveTokens(TokenType.PendingReinforcementArrival);
                unit.Tokens.AddToken(TokenDefinitionCatalog.Create(TokenType.Conceded));
            }

            foreach (IObjective objective in table.Objectives.Objects)
                if (objective.OwnerID is PlayerID owner && owner.Equals(player.PlayerID))
                    objective.SetOwner(null);

            await gameContext.Announce($"{player.Name} concedes the mission and withdraws from the table.",
                new TextColor(255, 170, 90, 255), EBannerTier.Headline);
            gameContext.Log($"{player.Name}'s units leave the table. They do not count as casualties.");
        }
    }
}
