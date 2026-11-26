using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Missions
{
    /// <summary>
    /// The six Attack &amp; Defend missions (GF / GFF / AoF / AoFS Advanced Rulebooks 3.6.0, "Extra Missions"): The Raid,
    /// Smash &amp; Grab, Ambush, Last Stand, VIP Escort, The Rescue. Kai, 2026-11-22: "go ahead" (after the Face-Off and
    /// Progressive missions shipped in 3.04).
    ///
    /// <para>Book: "When playing Attack &amp; Defend missions, the player that wins the deployment roll-off gets to pick if
    /// they want to be attacker or defender for this match." The defender places the markers, so the roles are settled
    /// first, with their own roll-off, before the objectives (house rules C). The judgement calls are listed in
    /// <c>claude/house-rules.md</c>, "Attack &amp; Defend readings".</para>
    /// </summary>
    public static class AttackDefend
    {
        private static float W => GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        private static float H => GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

        public const string AttackOption = "Attack";
        public const string DefendOption = "Defend";

        public static bool IsOn(IGameContext c) => ExtraMissionCatalog.IsAttackDefend(ExtraMissions.Current(c));

        /// <summary>The book's length for the mission (6 rounds), or null when it is not an Attack &amp; Defend mission.</summary>
        public const int Rounds = 6;

        public static ExtraMissionState? StateOf(IReadableGameDataStore store) => GameProgressUtilities.ExtraMission(store);

        public static int TeamOf(IGameContext c, PlayerID player) =>
            c.TableState.Teams.Objects.FirstOrDefault(t => t.Players.Contains(player))?.TeamNumber ?? -1;

        private static ITeam? Team(IGameContext c, int number) =>
            c.TableState.Teams.Objects.FirstOrDefault(t => t.TeamNumber == number);

        public static bool IsAttacker(IGameContext c, PlayerID player) =>
            StateOf(c.GameDataStore) is { AttackerTeam: >= 0 } s && TeamOf(c, player) == s.AttackerTeam;

        public static bool IsDefender(IGameContext c, PlayerID player) =>
            StateOf(c.GameDataStore) is { DefenderTeam: >= 0 } s && TeamOf(c, player) == s.DefenderTeam;

        private static string TeamName(IGameContext c, int number) =>
            Team(c, number) is { } t ? c.GetTeamLeadName(t) : $"Team {number}";

        // ---------------------------------------------------------------- roles

        /// <summary>The roll-off, then the winner picks attacker or defender.</summary>
        public static async Task PickRoles(IGameContext c, ExtraMissionState state)
        {
            List<ITeam> teams = c.TableState.Teams.Objects.OrderBy(t => t.TeamNumber).ToList();
            if (teams.Count != 2) return;
            ExtraMissionInfo info = ExtraMissionCatalog.Of(state.Mission);
            // Picked in the lobby (Kai, 2026-11-23) - the lists were built to it, so no roll-off.
            if (teams.FirstOrDefault(t => t.TeamNumber == AttackerOf(c.Settings.AttackerTeam)) is { } chosen)
            {
                ITeam defender = teams.First(t => t != chosen);
                state.AttackerTeam = chosen.TeamNumber;
                state.DefenderTeam = defender.TeamNumber;
                string picked = $"{TeamName(c, state.AttackerTeam)} attacks, {TeamName(c, state.DefenderTeam)} defends.";
                c.Log($"{info.Name}: roles picked in the lobby. {picked}"
                      + (info.AttackerBonusPoints ? $" The attacker's army may be up to {PointsLimit(c.Settings.ArmyPoints, state.Mission, chosen.TeamNumber, chosen.TeamNumber)} pts." : ""));
                await c.Announce(picked, new TextColor(255, 210, 80, 255), EBannerTier.Headline);
                return;
            }
            List<ITeam> order = await DiceUtilities.RollOff_Ordered(teams, teams.Select(t => c.GetTeamLeadName(t)).ToList(),
                c.TextOutput, c.DiceRoller, c.Presenter, "Attacker / Defender Roll-Off");
            ITeam winner = order[0], other = order[1];
            var request = new StringSelectionRequest(winner.Players.First(),
                $"{info.Name}: you won the roll-off. Do you want to be the attacker or the defender?",
                new[] { AttackOption, DefendOption }, new List<StringSelectionRequest.InvalidOption>(),
                optionDescriptions: new Dictionary<string, string>
                {
                    [AttackOption] = info.AttackerBonusPoints ? "The attacker (the book gives the attacking army 25% more points)." : "The attacker.",
                    [DefendOption] = "The defender sets up the objective marker(s).",
                },
                displayName: "Attacker or Defender")
            { Recommended = AttackOption };
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            bool attacks = pick != DefendOption;
            state.AttackerTeam = attacks ? winner.TeamNumber : other.TeamNumber;
            state.DefenderTeam = attacks ? other.TeamNumber : winner.TeamNumber;
            string line = $"{TeamName(c, state.AttackerTeam)} attacks, {TeamName(c, state.DefenderTeam)} defends.";
            c.Log($"{info.Name}: {c.GetTeamLeadName(winner)} won the roll-off and chose to {(attacks ? "attack" : "defend")}. {line}");
            if (info.AttackerBonusPoints)
                c.Log($"{info.Name}: the book gives the attacking army 25% more points than the defender (roles were rolled for, so the lists were not built to it - pick the attacker in the lobby to use it).");
            await c.Announce(line, new TextColor(255, 210, 80, 255), EBannerTier.Headline);
        }

        /// <summary>The lobby's attacker setting as a team number: unset (0) = team 1, -1 = the roll-off (returns -1).</summary>
        public static int AttackerOf(int setting) => setting == 0 ? 1 : setting;

        /// <summary>
        /// A seat's points limit: "The attacking army gets 25% more points than the defender" (The Raid, Smash &amp;
        /// Grab, The Rescue) - the attacker's limit is the game's points plus a quarter (rounded down), once the
        /// attacker is picked in the lobby. Every other seat, mission or roll-off: the game's points.
        /// </summary>
        public static int PointsLimit(int armyPoints, EExtraMission mission, int attackerTeam, int seatTeam)
        {
            // Meat Grinder recruitment: "players start the game with as many points as 50% of total game size to recruit
            // units (rounding up), which are then deployed at the beginning of the game" - the starting list is bought
            // out of that half.
            if (MeatGrinder.IsRecruitment(mission)) return MeatGrinder.StartingPoints(armyPoints);
            attackerTeam = AttackerOf(attackerTeam);
            if (attackerTeam <= 0 || seatTeam != attackerTeam || !ExtraMissionCatalog.Of(mission).AttackerBonusPoints) return armyPoints;
            return armyPoints + armyPoints / 4;
        }

        // ---------------------------------------------------------------- markers

        /// <summary>Where the defender may set the mission's marker(s), as the placement band (null = the core band).</summary>
        public static RectangularZone? LegalBand(IGameContext c)
        {
            EExtraMission m = ExtraMissions.Current(c);
            if (!ExtraMissionCatalog.IsAttackDefend(m)) return null;
            switch (ExtraMissionCatalog.Of(m).Markers)
            {
                case EMissionMarkers.OneNearCentreByDefender:
                    // "within 12" of the table center": the square inside that circle (the placement band is a
                    // rectangle; a corner of the square sits exactly 12" out).
                    float half = 12f / MathF.Sqrt(2f) - 0.05f;
                    return new RectangularZone(W / 2f - half, W / 2f + half, H / 2f - half, H / 2f + half);
                case EMissionMarkers.OneNearEdgeByDefender:
                    // "within 6" of any table edge": along the near long edge (house rules C - it has to cross
                    // the table to the opposite edge, which only a long edge makes a fair race).
                    return new RectangularZone(1f, W - 1f, 0.75f, 6f);
                case EMissionMarkers.D3Plus2ByDefender:
                    // Outside the deployment zones as usual, and not already within 6" of a short edge (which would
                    // be a relic the attacker has already carried out).
                    return new RectangularZone(6.5f, W - 6.5f, 12f, H - 12f);
                default:
                    return null;
            }
        }

        /// <summary>Who places the markers: the defender alone, or (Ambush) alternating starting with the defender.
        /// Returns the order and whether only the first team places.</summary>
        public static (List<ITeam> Order, bool OnlyFirst)? PlacementOrder(IGameContext c, IReadOnlyList<ITeam> rolled)
        {
            EExtraMission m = ExtraMissions.Current(c);
            if (!ExtraMissionCatalog.IsAttackDefend(m) || StateOf(c.GameDataStore) is not { DefenderTeam: >= 0 } s) return null;
            List<ITeam> order = rolled.OrderBy(t => t.TeamNumber == s.DefenderTeam ? 0 : 1).ToList();
            return (order, ExtraMissionCatalog.Of(m).Markers != EMissionMarkers.D3AlternatingDefenderFirst);
        }

        /// <summary>
        /// Once the markers are down: Smash &amp; Grab's defender "secretly mark[s] one of them as a trap, and one of them
        /// as a relic"; VIP Escort notes which edge the marker started on.
        /// </summary>
        public static async Task AfterMarkersPlaced(IGameContext c)
        {
            if (!IsOn(c)) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            List<IObjective> markers = c.TableState.Objectives.Objects.ToList();
            if (s.Mission == EExtraMission.VipEscort && s.VipFromLowEdge == null && markers.Count > 0)
            {
                s.VipFromLowEdge = markers[0].Position.z < H / 2f;
                ExtraMissions.SaveState(c, s);
                return;
            }
            if (s.Mission == EExtraMission.TheRescue && markers.Count > 0 && s.RelicX == null)
            {
                s.RelicX = markers[0].Position.x;
                s.RelicZ = markers[0].Position.z;
                ExtraMissions.SaveState(c, s);
                return;
            }
            if (s.Mission != EExtraMission.SmashAndGrab || s.SecretsMarked || markers.Count == 0) return;
            ITeam? defender = Team(c, s.DefenderTeam);
            if (defender == null) return;
            PlayerID who = defender.Players.First();

            string Label(IObjective o, int i) => $"Marker {i + 1} at ({o.Position.x:0}, {o.Position.z:0})";
            List<string> labels = markers.Select(Label).ToList();
            // The bot hides the relic in the marker furthest from every table edge (the longest carry out) and the
            // trap in a random other one.
            int bestRelic = Enumerable.Range(0, markers.Count).OrderByDescending(i => EdgeDistance(markers[i].Position)).First();
            var relicRequest = new StringSelectionRequest(who,
                "Smash & Grab: secretly mark one marker as the RELIC (the attacker must carry it within 6\" of a table edge).",
                labels, new List<StringSelectionRequest.InvalidOption>(), displayName: "Hide the Relic")
            { Recommended = labels[bestRelic] };
            string relic = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(relicRequest);
            int relicIndex = Math.Max(0, labels.IndexOf(relic));
            s.RelicX = markers[relicIndex].Position.x;
            s.RelicZ = markers[relicIndex].Position.z;

            if (markers.Count > 1)
            {
                List<string> rest = labels.Where((_, i) => i != relicIndex).ToList();
                var trapRequest = new StringSelectionRequest(who,
                    "Smash & Grab: secretly mark one other marker as a TRAP (the unit that seizes it takes 2D3 hits).",
                    rest, new List<StringSelectionRequest.InvalidOption>(), displayName: "Set the Trap")
                { Recommended = rest[c.Rng.Next(rest.Count)] };
                string trap = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(trapRequest);
                int trapIndex = labels.IndexOf(trap);
                if (trapIndex >= 0 && trapIndex != relicIndex)
                {
                    s.TrapX = markers[trapIndex].Position.x;
                    s.TrapZ = markers[trapIndex].Position.z;
                }
            }
            s.SecretsMarked = true;
            ExtraMissions.SaveState(c, s);
            c.Log($"Smash & Grab: {c.GetPlayerName(who)} secretly marks a trap and a relic.");
        }

        // ---------------------------------------------------------------- deployment

        /// <summary>The mission's deployment order, or null for the core alternating deployment (Smash &amp; Grab).</summary>
        public static MissionDeploymentPlan? DeploymentPlan(IGameContext c)
        {
            if (!IsOn(c) || StateOf(c.GameDataStore) is not { AttackerTeam: >= 0, DefenderTeam: >= 0 } s) return null;
            int att = s.AttackerTeam, def = s.DefenderTeam;
            static int Half(int n) => (n + 1) / 2;
            static int All(int n) => int.MaxValue;
            switch (s.Mission)
            {
                case EExtraMission.TheRaid:
                    return new MissionDeploymentPlan(customZones: true, leftoversToReserve: false,
                        new MissionDeploymentStep(def, Half, EMissionZone.AroundMarker, "The defender deploys half of its units within 12\" of the marker."),
                        new MissionDeploymentStep(att, All, EMissionZone.TableEdges, "The attacker deploys all of its units within 12\" of any table edge."),
                        new MissionDeploymentStep(def, All, EMissionZone.AwayFromEnemiesAndMarker, "The defender deploys the rest over 12\" from enemy units and the marker."));
                case EExtraMission.Ambush:
                    return new MissionDeploymentPlan(customZones: true, leftoversToReserve: false,
                        new MissionDeploymentStep(def, Half, EMissionZone.OwnZone, "The defender deploys half of its units in its deployment zone."),
                        new MissionDeploymentStep(att, All, EMissionZone.AwayFromEnemiesNearFriends, "The attacker deploys all of its units over 12\" from enemy units, within 6\" of another friendly unit."),
                        new MissionDeploymentStep(def, All, EMissionZone.AwayFromEnemies, "The defender deploys the rest anywhere over 12\" from enemy units."));
                case EExtraMission.LastStand:
                    return new MissionDeploymentPlan(customZones: true, leftoversToReserve: false,
                        new MissionDeploymentStep(def, All, EMissionZone.AroundCentre, "The defender deploys all of its units within 12\" of the table centre."),
                        new MissionDeploymentStep(att, All, EMissionZone.TableEdges, "The attacker deploys all of its units within 12\" of any table edge."));
                case EExtraMission.VipEscort:
                    return new MissionDeploymentPlan(customZones: true, leftoversToReserve: false,
                        new MissionDeploymentStep(def, All, EMissionZone.AroundMarker, "The defender deploys all of its units within 12\" of the marker."),
                        new MissionDeploymentStep(att, All, EMissionZone.TableEdgesAwayFromEnemies, "The attacker deploys all of its units within 12\" of any table edge, over 12\" from enemy units."));
                case EExtraMission.TheRescue:
                    // "Players may only deploy up to half of their units, and keep the other half in reserves."
                    return new MissionDeploymentPlan(customZones: false, leftoversToReserve: true,
                        new MissionDeploymentStep(null, n => n / 2, EMissionZone.OwnZone, "Each side deploys up to half of its units; the rest wait in reserves."));
                default:
                    return null;
            }
        }

        /// <summary>The area, enemy distance and keep-out circles for one unit placed in a mission step.</summary>
        public static (IBoundedZone Zone, float MinFromEnemies, IReadOnlyList<PlacementDisc> KeepOut) Zone(IGameContext c,
            EMissionZone kind, UnitData unit, IBoundedZone ownZone)
        {
            float reach = unit.Models.Where(m => m.GetIsAlive()).Select(m => m.BaseShape.BoundingRadiusInches).DefaultIfEmpty(0.5f).Max();
            IReadOnlyList<PlacementDisc> enemies12 = AmbushArrivalRules.EnemyEdgeDiscs(unit, c.TableState, 12f);
            IObjective? marker = c.TableState.Objectives.Objects.FirstOrDefault();
            switch (kind)
            {
                case EMissionZone.AroundMarker when marker != null:
                    return (new MissionPlacementZone(W, H, new CircularZone(marker.Position.x, marker.Position.z, 12f), null), 0f, Array.Empty<PlacementDisc>());
                case EMissionZone.AroundMarker:
                case EMissionZone.AroundCentre:
                    return (new MissionPlacementZone(W, H, new CircularZone(W / 2f, H / 2f, 12f), null), 0f, Array.Empty<PlacementDisc>());
                case EMissionZone.TableEdges:
                    return (new TableEdgeBandZone(W, H, 12f), 0f, Array.Empty<PlacementDisc>());
                case EMissionZone.TableEdgesAwayFromEnemies:
                    return (new TableEdgeBandZone(W, H, 12f), 12f, enemies12);
                case EMissionZone.AwayFromEnemies:
                    return (new MissionPlacementZone(W, H, null, null), 12f, enemies12);
                case EMissionZone.AwayFromEnemiesAndMarker:
                {
                    var holes = marker == null ? null
                        : new[] { new CircularZone(marker.Position.x, marker.Position.z, 12f + reach) };
                    return (new MissionPlacementZone(W, H, null, holes), 12f, enemies12);
                }
                case EMissionZone.AwayFromEnemiesNearFriends:
                {
                    // "within 6" of at least one other friendly unit" - base edge to base edge, so each circle is
                    // widened by the friend's base and this unit's widest base. The first unit has no friend yet.
                    int team = TeamOf(c, unit.PlayerID);
                    var circles = new List<IZone>();
                    foreach (IUnit friend in c.TableState.Units.Objects)
                    {
                        if (ReferenceEquals(friend, unit) || !friend.GetIsAlive() || TeamOf(c, friend.PlayerID) != team) continue;
                        if (!friend.GetIsOnBattlefield()) continue;
                        foreach (IModel model in friend.Models)
                        {
                            if (!model.GetIsAlive() || (model.Position.x == 0f && model.Position.z == 0f)) continue;
                            circles.Add(new CircularZone(model.Position.x, model.Position.z, 6f + model.BaseShape.BoundingRadiusInches + reach));
                        }
                    }
                    IZone? include = circles.Count == 0 ? null : new CompositeZone(circles);
                    return (new MissionPlacementZone(W, H, include, null), 12f, enemies12);
                }
                default:
                    return (ownZone, 0f, Array.Empty<PlacementDisc>());
            }
        }

        // ---------------------------------------------------------------- the round

        /// <summary>
        /// The beginning of each round: VIP Escort's defender may move the marker; Last Stand's attacker reserves (6s,
        /// or destroyed when the attacker has nobody left); The Rescue's reserves (4+, from round 2).
        /// </summary>
        public static async Task RoundStart(IGameContext c, int round)
        {
            if (!IsOn(c)) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            switch (s.Mission)
            {
                case EExtraMission.VipEscort:
                    await MoveVip(c, s);
                    break;
                case EExtraMission.LastStand:
                    SendDestroyedToReserves(c);
                    s = ExtraMissions.StateFor(c);
                    await LastStandReserves(c, s);
                    break;
                case EExtraMission.TheRescue when round >= 2:
                    await RollReserves(c, s, 4, "The Rescue");
                    break;
            }
        }

        /// <summary>After every activation (and at the end of a round): Last Stand's destroyed attackers go to reserves.</summary>
        public static void AfterActivation(IGameContext c)
        {
            if (!IsOn(c)) return;
            if (ExtraMissions.Current(c) == EExtraMission.LastStand) SendDestroyedToReserves(c);
        }

        /// <summary>"When an attacker's unit is fully destroyed for the first time, it is placed in reserves."</summary>
        private static void SendDestroyedToReserves(IGameContext c)
        {
            ExtraMissionState s = ExtraMissions.StateFor(c);
            bool changed = false;
            foreach (IUnit unit in c.TableState.Units.Objects.ToList())
            {
                if (unit.GetIsAlive() || unit is not UnitData data) continue;
                if (TeamOf(c, unit.PlayerID) != s.AttackerTeam) continue;
                string id = unit.ID.ID.ToString();
                if (s.ReservedOnce.Contains(id)) continue;
                if (unit.Tokens.HasToken(TokenType.Conceded)) continue;
                HordePlacement.Revive(data);
                ReserveRules.PlaceInReserve(data);
                s.ReservedOnce.Add(id);
                s.MissionReserve.Add(id);
                changed = true;
                c.Log($"Last Stand: {unit.Name} is destroyed for the first time and goes into reserves.");
            }
            if (changed) ExtraMissions.SaveState(c, s);
        }

        private static async Task LastStandReserves(IGameContext c, ExtraMissionState s)
        {
            List<UnitData> waiting = Waiting(c, s);
            if (waiting.Count == 0) return;
            bool attackerOnTable = c.TableState.Units.Objects.Any(u => TeamOf(c, u.PlayerID) == s.AttackerTeam
                && u.GetIsAlive() && u.GetIsOnBattlefield());
            if (!attackerOnTable)
            {
                // "if at the beginning of a round the attacker has no units on the table, then any remaining reserves
                // are destroyed."
                foreach (UnitData unit in waiting)
                {
                    foreach (DataBinding<ModelData> b in unit.ModelBindings)
                    {
                        ModelData model = b.GetValue();
                        if (model.GetIsAlive()) model.DealWounds(model.TotalWounds - model.WoundsDealt);
                    }
                    s.MissionReserve.Remove(unit.ID.ID.ToString());
                    c.Log($"Last Stand: the attacker has no units on the table - {unit.Name} is destroyed in reserves.");
                }
                ExtraMissions.SaveState(c, s);
                await c.Announce("The attack is broken - reserves destroyed", new TextColor(255, 80, 60, 255), EBannerTier.Headline);
                return;
            }
            await RollReserves(c, s, 6, "Last Stand");
        }

        private static List<UnitData> Waiting(IGameContext c, ExtraMissionState s) =>
            s.MissionReserve.Select(id => c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == id))
                .OfType<UnitData>()
                .Where(u => u.GetIsAlive() && ReserveRules.IsInReserve(u))
                .ToList();

        /// <summary>Rolls one die per unit waiting in the mission's reserves; on <paramref name="needed"/>+ it is placed
        /// within 12" of any table edge (The Rescue: also over 12" from enemy units and the marker).</summary>
        private static async Task RollReserves(IGameContext c, ExtraMissionState s, int needed, string mission)
        {
            // A unit that left reserves some other way (an Ambush rule bringing it on) is no longer waiting.
            s.MissionReserve.RemoveAll(id => c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == id) is not { } u
                || !u.GetIsAlive() || !ReserveRules.IsInReserve(u));
            foreach (UnitData unit in Waiting(c, s))
            {
                int roll = HordePlacement.RollD6(c);
                if (roll < needed)
                {
                    c.Log($"{mission}: {unit.Name} rolls {roll} - stays in reserves (needs {needed}{(needed < 6 ? "+" : "")}).");
                    continue;
                }
                c.Log($"{mission}: {unit.Name} rolls {roll} - arrives within 12\" of a table edge.");
                IBoundedZone zone = new TableEdgeBandZone(W, H, 12f);
                float minEnemy = 0f;
                IReadOnlyList<PlacementDisc> keepOut = Array.Empty<PlacementDisc>();
                if (s.Mission == EExtraMission.TheRescue)
                {
                    float reach = unit.Models.Select(m => m.BaseShape.BoundingRadiusInches).DefaultIfEmpty(0.5f).Max();
                    minEnemy = 12f;
                    keepOut = AmbushArrivalRules.EnemyEdgeDiscs(unit, c.TableState, 12f);
                    if (c.TableState.Objectives.Objects.FirstOrDefault() is { } marker)
                        keepOut = keepOut.Append(new PlacementDisc(marker.Position, 12f + reach)).ToList();
                }
                var request = new PlaceObjectsRequest<ModelData>(unit.PlayerID, $"{mission}: {unit.Name} arrives from reserves",
                    zone, unit.ModelBindings, minDistanceFromEnemiesInches: minEnemy, enemyKeepOutDiscs: keepOut,
                    displayName: $"Deploying {unit.Name} from reserves");
                List<PlacedObjectEntry<ModelData>> placements = await PlacementCommitGuard.RequestClearPlacement(c, request);
                ReserveRules.ClearReserve(unit);
                foreach (PlacedObjectEntry<ModelData> p in placements)
                {
                    p.Binding.GetValue().SetPosition(p.Position);
                    FacingRules.ApplyPlaced(p.Binding.GetValue(), p.Facing, c.Settings);
                }
                s.MissionReserve.Remove(unit.ID.ID.ToString());
                await c.Announce($"{unit.Name} arrives from reserves!", new TextColor(255, 170, 60, 255));
            }
            ExtraMissions.SaveState(c, s);
        }

        public const string VipToward = "Move it up to 12\" toward the far edge";
        public const string VipChoose = "Choose where to move it";
        public const string VipStay = "Leave it where it is";

        /// <summary>"At the beginning of each round, if the defender controls the marker, then they may move it by up to
        /// 12" in any direction."</summary>
        private static async Task MoveVip(IGameContext c, ExtraMissionState s)
        {
            IObjective? marker = c.TableState.Objectives.Objects.FirstOrDefault();
            if (marker?.OwnerID is not PlayerID owner || TeamOf(c, owner) != s.DefenderTeam) return;
            bool low = s.VipFromLowEdge ?? marker.Position.z < H / 2f;
            float goalZ = low ? H - 3f : 3f;
            var request = new StringSelectionRequest(owner,
                $"VIP Escort: you control the marker. Move it up to 12\"? It must end within 6\" of the {(low ? "far" : "near")} long edge after round {Rounds}.",
                new[] { VipToward, VipChoose, VipStay }, new List<StringSelectionRequest.InvalidOption>(), displayName: "Move the VIP")
            { Recommended = VipToward };
            string pick = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            if (pick == VipStay) return;

            List<ITerrain> blocked = c.TableState.Terrain.Objects.Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible)).ToList();
            bool Clear(Position p) => p.x > 0.5f && p.x < W - 0.5f && p.z > 0.5f && p.z < H - 0.5f
                && !blocked.Any(t => t.IsPointWithinZone(new Float2(p.x, p.z)));
            Position from = marker.Position;
            Position to = from;
            if (pick == VipChoose)
            {
                var band = new RectangularZone(MathF.Max(0.5f, from.x - 12f), MathF.Min(W - 0.5f, from.x + 12f),
                    MathF.Max(0.5f, from.z - 12f), MathF.Min(H - 0.5f, from.z + 12f));
                for (int tries = 0; tries < 5; tries++)
                {
                    var place = new PlaceObjectiveRequest(owner, "VIP Escort: move the marker (up to 12\")", 1, 1, band, 0f);
                    Position wanted = await c.PlayerRequester.RequestDecision<PlaceObjectiveRequest, Position>(place);
                    if (Position.GetDistance2D(wanted, from) <= 12.01f && Clear(wanted)) { to = wanted; break; }
                    c.Log("VIP Escort: that spot is over 12\" away or inside impassable terrain - pick again.");
                }
            }
            else
            {
                float step = Math.Clamp(goalZ - from.z, -12f, 12f);
                for (float k = 1f; k > 0.05f; k -= 0.1f)
                {
                    var p = new Position(from.x, from.z + step * k);
                    if (Clear(p)) { to = p; break; }
                }
            }
            if (Position.GetDistance2D(to, from) < 0.01f) return;
            ExtraMissions.MoveObjective(c, marker, to);
            c.Log($"VIP Escort: {c.GetPlayerName(owner)} moves the marker {Position.GetDistance2D(to, from):0.#}\" to ({to.x:0.0}, {to.z:0.0}).");
        }

        // ---------------------------------------------------------------- seizing and the result

        /// <summary>
        /// The end of a round, after objectives are seized. Smash &amp; Grab: an attacker seizing a marker reveals it (trap
        /// = 2D3 hits, removed; relic = carried; anything else removed). The Rescue: an attacker seizing the marker
        /// carries it.
        /// </summary>
        public static async Task AfterSeizing(IGameContext c, ExtraMissionState s)
        {
            if (s.Mission == EExtraMission.LastStand) SendDestroyedToReserves(c);
            if (s.Mission is not (EExtraMission.SmashAndGrab or EExtraMission.TheRescue)) return;
            foreach (IObjective o in c.TableState.Objectives.Objects.ToList())
            {
                if (o.OwnerID is not PlayerID owner || TeamOf(c, owner) != s.AttackerTeam) continue;
                IUnit? seizer = Nearest(c, owner, o.Position);
                bool isRelic = At(s.RelicX, s.RelicZ, o.Position);
                if (s.Mission == EExtraMission.SmashAndGrab && !isRelic)
                {
                    bool trap = At(s.TrapX, s.TrapZ, o.Position);
                    ExtraMissions.RemoveObjective(c, o);
                    if (trap)
                    {
                        s.TrapX = s.TrapZ = null;
                        int hits = (HordePlacement.RollD6(c) + 1) / 2 + (HordePlacement.RollD6(c) + 1) / 2;
                        c.Log($"Smash & Grab: the marker was a TRAP - {seizer?.Name ?? "the unit"} takes {hits} hits. The marker is removed.");
                        await c.Announce("It's a trap!", new TextColor(255, 80, 60, 255), EBannerTier.Headline);
                        if (seizer != null) await CampaignEventRunner.DealHits(c, seizer, hits, "Trap");
                    }
                    else
                    {
                        c.Log("Smash & Grab: the marker was a decoy - it is removed.");
                    }
                    continue;
                }
                // The relic (Smash & Grab), the marker (The Rescue): carried off the table.
                if (seizer == null || seizer.Tokens.HasToken(TokenType.Shaken)) continue;
                ExtraMissions.RemoveObjective(c, o);
                s.Carried.Add(new CarriedMarker { Unit = seizer.ID.ID.ToString(), Slot = MissionCardGame.SlotOf(c, owner) });
                bool first = !s.RelicRevealed;
                s.RelicRevealed = true;
                c.Log(s.Mission == EExtraMission.SmashAndGrab
                    ? $"Smash & Grab: {(first ? "the marker was the RELIC! " : "")}{seizer.Name} seizes the relic and carries it."
                    : $"The Rescue: {seizer.Name} seizes the marker and carries it.");
                if (first && s.Mission == EExtraMission.SmashAndGrab)
                    await c.Announce("The relic is found!", new TextColor(255, 210, 80, 255), EBannerTier.Headline);
            }
        }

        private static bool At(float? x, float? z, Position p) =>
            x is float fx && z is float fz && MathF.Abs(fx - p.x) < 0.05f && MathF.Abs(fz - p.z) < 0.05f;

        private static IUnit? Nearest(IGameContext c, PlayerID owner, Position at) =>
            c.TableState.Units.Objects
                .Where(u => u.PlayerID.Equals(owner) && u.GetIsAlive() && u.GetIsOnBattlefield())
                .OrderBy(u => u.Models.Where(m => m.GetIsAlive()).Select(m => Position.GetDistance2D(m.Position, at)).DefaultIfEmpty(float.MaxValue).Min())
                .FirstOrDefault();

        public static float EdgeDistance(Position p) => MathF.Min(MathF.Min(p.x, W - p.x), MathF.Min(p.z, H - p.z));

        /// <summary>A dropped carried marker: "within 1" (placed by the opponent)" / The Rescue "within 6" (placed by the
        /// defender)" - the opponent puts it the full distance toward the table centre, away from the edges.</summary>
        public static Position DropSpot(IGameContext c, EExtraMission m, Position carrier)
        {
            float distance = m == EExtraMission.TheRescue ? 6f : 1f;
            float dx = W / 2f - carrier.x, dz = H / 2f - carrier.z;
            float len = MathF.Sqrt(dx * dx + dz * dz);
            if (len < 0.01f) return carrier;
            float k = MathF.Min(distance, len) / len;
            return new Position(carrier.x + dx * k, carrier.z + dz * k);
        }

        /// <summary>Where the carried / dropped relic (or rescued marker) is now, and how close to a table edge.</summary>
        private static float? PrizeEdgeDistance(IGameContext c, ExtraMissionState s)
        {
            if (s.Carried.FirstOrDefault() is { } carried
                && c.TableState.Units.Objects.FirstOrDefault(u => u.ID.ID.ToString() == carried.Unit) is { } unit && unit.GetIsAlive())
            {
                return unit.Models.Where(m => m.GetIsAlive())
                    .Select(m => EdgeDistance(m.Position) - m.BaseShape.BoundingRadiusInches)
                    .DefaultIfEmpty(float.MaxValue).Min();
            }
            if (s.RelicX is float x && s.RelicZ is float z) return EdgeDistance(new Position(x, z));
            return null;
        }

        /// <summary>The result: 1 for each seat of the winning side, 0 otherwise (both 0 = a draw).</summary>
        public static (Dictionary<int, int> Score, string Unit) FinalScore(IGameContext c, ExtraMissionState s)
        {
            int? winner = null;
            switch (s.Mission)
            {
                case EExtraMission.TheRaid:
                case EExtraMission.LastStand:
                {
                    // "the player that controls the marker wins"
                    IObjective? marker = c.TableState.Objectives.Objects.FirstOrDefault();
                    if (marker?.OwnerID is PlayerID owner) winner = TeamOf(c, owner);
                    break;
                }
                case EExtraMission.Ambush:
                    return (ExtraMissions.Controlled(c), "markers");
                case EExtraMission.SmashAndGrab:
                case EExtraMission.TheRescue:
                {
                    float? edge = PrizeEdgeDistance(c, s);
                    winner = edge is float e && e <= 6f ? s.AttackerTeam : s.DefenderTeam;
                    c.Log(edge is float d
                        ? $"{ExtraMissionCatalog.Of(s.Mission).Name}: the {(s.Mission == EExtraMission.SmashAndGrab ? "relic" : "marker")} is {MathF.Max(0f, d):0.#}\" from the nearest table edge."
                        : $"{ExtraMissionCatalog.Of(s.Mission).Name}: the prize is gone.");
                    break;
                }
                case EExtraMission.VipEscort:
                {
                    IObjective? marker = c.TableState.Objectives.Objects.FirstOrDefault();
                    bool low = s.VipFromLowEdge ?? true;
                    float toFar = marker == null ? float.MaxValue : (low ? H - marker.Position.z : marker.Position.z);
                    winner = toFar <= 6f ? s.DefenderTeam : s.AttackerTeam;
                    if (marker != null) c.Log($"VIP Escort: the marker is {toFar:0.#}\" from the opposite edge.");
                    break;
                }
            }
            var score = new Dictionary<int, int>();
            foreach (IPlayerSlotInfo seat in c.TableState.Players.Objects.Where(p => p.IsFilled))
                score[seat.SlotID] = winner is int w && seat.TeamNumber == w ? 1 : 0;
            return (score, "mission won");
        }
    }

    /// <summary>Where a unit in one step of an Attack &amp; Defend deployment may be placed.</summary>
    public enum EMissionZone
    {
        Defaults,
        OwnZone,
        AroundMarker,
        AroundCentre,
        TableEdges,
        TableEdgesAwayFromEnemies,
        AwayFromEnemies,
        AwayFromEnemiesAndMarker,
        AwayFromEnemiesNearFriends,
    }

    /// <summary>One step: <see cref="Team"/> (null = both sides, alternating) deploys up to <see cref="Quota"/> of its
    /// units (given how many it has left when the step starts) in <see cref="Zone"/>.</summary>
    public sealed record MissionDeploymentStep(int? Team, Func<int, int> Quota, EMissionZone Zone, string Text);

    /// <summary>
    /// The Attack &amp; Defend deployments: steps run in order instead of the core alternation. Each return to "who deploys
    /// next" counts the previous player's deployment (a unit placed, held in Ambush or embarked) against its step.
    /// </summary>
    public sealed class MissionDeploymentPlan
    {
        public IReadOnlyList<MissionDeploymentStep> Steps { get; }
        /// <summary>The mission replaces the deployment zones: Scout units deploy with the rest, and Re-Deployment
        /// (which re-places within the deployment zone) is skipped.</summary>
        public bool CustomZones { get; }
        /// <summary>Units not deployed when the steps are done go into the mission's reserves (The Rescue).</summary>
        public bool LeftoversToReserve { get; }

        private int _index = -1;
        private readonly Dictionary<int, int> _done = new();
        private readonly Dictionary<int, int> _quota = new();
        private int? _handedTo;
        private int? _lastTeam;
        private readonly Dictionary<int, int> _nextPlayer = new();

        public MissionDeploymentPlan(bool customZones, bool leftoversToReserve, params MissionDeploymentStep[] steps)
        {
            CustomZones = customZones;
            LeftoversToReserve = leftoversToReserve;
            Steps = steps;
        }

        public MissionDeploymentStep? Current => _index >= 0 && _index < Steps.Count ? Steps[_index] : null;

        private static int Remaining(IDeploymentTurnContext ctx, ITeam team) =>
            team.Players.Sum(p => ctx.UndeployedUnits.TryGetValue(p, out var pool) ? pool.Count : 0);

        private IEnumerable<ITeam> Involved(IDeploymentTurnContext ctx) =>
            ctx.FirstDeploymentRollOrder.Where(t => Current!.Team is not int only || t.TeamNumber == only);

        private bool TeamDone(IDeploymentTurnContext ctx, ITeam team) =>
            Remaining(ctx, team) == 0 || _done.GetValueOrDefault(team.TeamNumber) >= _quota.GetValueOrDefault(team.TeamNumber);

        /// <summary>The player who deploys next, or null when the mission's deployment is finished.</summary>
        public PlayerID? NextPlayer(IDeploymentTurnContext ctx)
        {
            if (_handedTo is int last) _done[last] = _done.GetValueOrDefault(last) + 1;
            _handedTo = null;
            while (true)
            {
                if (Current == null || Involved(ctx).All(t => TeamDone(ctx, t)))
                {
                    _index++;
                    if (_index >= Steps.Count) return null;
                    _done.Clear();
                    _quota.Clear();
                    foreach (ITeam team in Involved(ctx)) _quota[team.TeamNumber] = Current!.Quota(Remaining(ctx, team));
                    ctx.Log($"Deployment: {Current!.Text}");
                    continue;
                }
                List<ITeam> open = Involved(ctx).Where(t => !TeamDone(ctx, t)).ToList();
                ITeam team2 = open.FirstOrDefault(t => t.TeamNumber != _lastTeam) ?? open[0];
                List<PlayerID> players = team2.Players.Where(p => ctx.UndeployedUnits.TryGetValue(p, out var pool) && pool.Count > 0).ToList();
                int i = _nextPlayer.GetValueOrDefault(team2.TeamNumber) % players.Count;
                _nextPlayer[team2.TeamNumber] = i + 1;
                _handedTo = team2.TeamNumber;
                _lastTeam = team2.TeamNumber;
                return players[i];
            }
        }

        private readonly List<(UnitData Unit, EMissionZone Zone)> _placed = new();

        /// <summary>Notes where a unit was placed from, for the deployment check below.</summary>
        public void Record(UnitData unit)
        {
            if (Current is { } step) _placed.Add((unit, step.Zone));
        }

        /// <summary>
        /// FDG_MISSION_AUDIT=1 (bot sweeps): once everyone is down, every placed model is checked against its step's area
        /// again - "[MISSION AUDIT]" lines. Later placements only add enemies that were themselves kept 12" away, so the
        /// area still holds at the end.
        /// </summary>
        private void Audit(IGameContext c)
        {
            if (Environment.GetEnvironmentVariable("FDG_MISSION_AUDIT") != "1") return;
            int checkedUnits = 0, bad = 0;
            foreach ((UnitData unit, EMissionZone kind) in _placed)
            {
                if (kind == EMissionZone.OwnZone || !unit.GetIsOnBattlefield()) continue;
                checkedUnits++;
                (IBoundedZone zone, float _, IReadOnlyList<PlacementDisc> keepOut) = AttackDefend.Zone(c, kind, unit, new RectangularZone(0, 1, 0, 1));
                foreach (IModel m in unit.Models.Where(m => m.GetIsAlive()))
                {
                    var at = new Float2(m.Position.x, m.Position.z);
                    // The near-a-friend area is judged against every OTHER friendly unit, which now includes ones
                    // placed after this one; the first unit placed had no friend to be near.
                    bool inArea = zone.IsPointWithinZone(at);
                    PlacementDisc? hit = keepOut.FirstOrDefault(d => Position.GetDistance2D(d.Center, m.Position) < d.RadiusInches - 0.05f);
                    if (!inArea || hit != null)
                    {
                        bad++;
                        c.Log($"[MISSION AUDIT] VIOLATION {unit.Name} at ({m.Position.x:0.0}, {m.Position.z:0.0}): {kind} {(inArea ? "" : "outside the area ")}{(hit != null ? $"inside a keep-out circle at ({hit.Center.x:0.0}, {hit.Center.z:0.0}) r {hit.RadiusInches:0.0}" : "")}");
                        break;
                    }
                }
            }
            c.Log($"[MISSION AUDIT] deployment checked: {checkedUnits} units, {bad} out of place.");
        }

        /// <summary>The steps are done: what is left goes to reserves (The Rescue) - nothing is left otherwise.</summary>
        public void Finish(IGameContext c, IDeploymentTurnContext ctx)
        {
            Audit(c);
            if (!LeftoversToReserve) return;
            ExtraMissionState s = ExtraMissions.StateFor(c);
            foreach ((PlayerID _, List<DataBinding<UnitData>> pool) in ctx.UndeployedUnits)
            {
                foreach (DataBinding<UnitData> binding in pool)
                {
                    UnitData unit = binding.GetValue();
                    ReserveRules.PlaceInReserve(unit);
                    s.MissionReserve.Add(unit.ID.ID.ToString());
                    c.Log($"{unit.Name} waits in reserves.");
                }
                pool.Clear();
            }
            ExtraMissions.SaveState(c, s);
        }
    }
}
