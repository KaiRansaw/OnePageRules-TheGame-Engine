using FDG.Data;
using FDG.Players;
using FDG.Presentation.Beats;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FDG.Missions
{
    /// <summary>
    /// Advanced Missions - Side-Missions (GF / AoF Advanced Rulebooks 3.6.0 p27-28, GF: Firefight / AoF: Skirmish p25-26).
    /// How players get them: <see cref="Shared"/> / <see cref="Hidden"/> / <see cref="SharedAndHidden"/>, or one of the
    /// three Mad Dash variants (a fresh random side-mission each round, progressive scoring).
    /// </summary>
    public enum ESideMissionPlay
    {
        Off = 0,
        Shared,
        Hidden,
        SharedAndHidden,
        /// <summary>"roll a D3 to randomly get one" (of the first three).</summary>
        RandomPlaystyle,
        SharedDash,
        HiddenDash,
        CumulativeDash,
        /// <summary>A D3 for one of the three Mad Dash variants.</summary>
        RandomDash,
    }

    /// <summary>The selection methods (Free / Limited / Randomized, or a D3). Not used by the Mad Dash variants, whose
    /// side-missions are always rolled.</summary>
    public enum ESideMissionPick { Free = 0, Limited, Randomized, Random }

    public enum ESideCategory { TacticalGenius = 1, FieldMarshal, AstuteStrategist, CombatMaster, MonsterHunter, HeroicGeneral }

    /// <summary>One side-mission. <see cref="Id"/> is category * 10 + its number (11 Mislead ... 66 Dispatch).</summary>
    public sealed record SideMissionInfo(int Id, string Name, string Text)
    {
        public ESideCategory Category => (ESideCategory)(Id / 10);
    }

    /// <summary>The 36 side-missions in each game's own words (p28 GF / AoF, p26 GFF / AoFS).</summary>
    public static class SideMissionCatalog
    {
        /// <summary>GF: Firefight and AoF: Skirmish print smaller numbers for Combat Master and Dispatch.</summary>
        public static bool Small(EGameSystem system) => system is EGameSystem.GFFirefight or EGameSystem.AoFSkirmish;

        public static bool Available(EGameSystem system) => ExtraMissionCatalog.Available(system);

        /// <summary>"units with X+ models" (Combat Master): 5 in GF / AoF, 3 in GFF / AoFS.</summary>
        public static int SquadSize(EGameSystem system) => Small(system) ? 3 : 5;

        public static string CategoryName(ESideCategory c) => c switch
        {
            ESideCategory.TacticalGenius => "Tactical Genius",
            ESideCategory.FieldMarshal => "Field Marshal",
            ESideCategory.AstuteStrategist => "Astute Strategist",
            ESideCategory.CombatMaster => "Combat Master",
            ESideCategory.MonsterHunter => "Monster Hunter",
            _ => "Heroic General",
        };

        public static IReadOnlyList<SideMissionInfo> For(EGameSystem system)
        {
            bool small = Small(system);
            string n = small ? "3+" : "5+";
            return new[]
            {
                new SideMissionInfo(11, "Mislead", "Control no objectives."),
                new SideMissionInfo(12, "Fortify", "Control only one objective."),
                new SideMissionInfo(13, "Seize", "Control most objectives."),
                new SideMissionInfo(14, "Rule", "Control more objectives than opponent."),
                new SideMissionInfo(15, "Dominate", "Enemy must control no objectives."),
                new SideMissionInfo(16, "Equalize", "Control as many objectives as opponent."),
                new SideMissionInfo(21, "Control", "Have one or more friendly units fully within 6\" of the table centre."),
                new SideMissionInfo(22, "Intimidate", "No enemy units are fully within 6\" of the table centre."),
                new SideMissionInfo(23, "Overrun", "Have one or more friendly units fully inside each table quarter."),
                new SideMissionInfo(24, "Spread Out", "Have one or more friendly units within 12\" of each table corner."),
                new SideMissionInfo(25, "Invade", "Have two or more friendly units fully inside the enemy deployment zone."),
                new SideMissionInfo(26, "Defend", "No enemy units are fully inside the friendly deployment zone."),
                new SideMissionInfo(31, "Rush", "Be the first player to destroy an enemy unit."),
                new SideMissionInfo(32, "Finalize", "Be the last player to destroy an enemy unit."),
                new SideMissionInfo(33, "Conserve", "Keep at least 50% of friendly units alive."),
                new SideMissionInfo(34, "Erode", "Destroy at least 50% of enemy units."),
                new SideMissionInfo(35, "Terrify", "Two or more enemy units failed a Morale Test caused by friendly units."),
                new SideMissionInfo(36, "Hex", "Successfully cast four or more spells."),
                new SideMissionInfo(41, "Slaughter", small ? "Destroy two or more enemy units with 3+ models." : "Destroy three or more enemy units with 5+ models."),
                new SideMissionInfo(42, "Wipe Out", $"Destroy more enemy units with {n} models than opponent."),
                new SideMissionInfo(43, "Exterminate", $"Destroy the enemy unit with most models, must have {n} models (if there are multiple, destroy one)."),
                new SideMissionInfo(44, "Butcher", $"Destroy the most expensive enemy unit with {n} models (if there are multiple, destroy one)."),
                new SideMissionInfo(45, "Eradicate", $"Destroy the least expensive enemy unit with {n} models (if there are multiple, destroy one)."),
                new SideMissionInfo(46, "Massacre", small ? "Destroy ten or more enemy models." : "Destroy twenty or more enemy models."),
                new SideMissionInfo(51, "Eliminate", "Destroy two or more enemy units with Tough(9+)."),
                new SideMissionInfo(52, "Destroy", "Destroy more enemy units with Tough(9+) than opponent."),
                new SideMissionInfo(53, "Obliterate", "Destroy the enemy unit with the highest Tough value, must be Tough(9+) (if there are multiple, destroy one)."),
                new SideMissionInfo(54, "Erase", "Destroy the most expensive enemy unit with Tough(9+) (if there are multiple, destroy one)."),
                new SideMissionInfo(55, "Annihilate", "Destroy the least expensive enemy unit with Tough(9+) (if there are multiple, destroy one)."),
                new SideMissionInfo(56, "Decimate", "Deal twelve or more wounds to a single unit with Tough(18+)."),
                new SideMissionInfo(61, "Slay", "Destroy two or more enemy Heroes."),
                new SideMissionInfo(62, "Execute", "Destroy more enemy Heroes than opponent."),
                new SideMissionInfo(63, "Terminate", "Destroy the enemy Hero with the highest Tough value (if there are multiple, destroy one)."),
                new SideMissionInfo(64, "Assassinate", "Destroy the most expensive enemy Hero (if there are multiple, destroy one)."),
                new SideMissionInfo(65, "Murder", "Destroy the least expensive enemy Hero (if there are multiple, destroy one)."),
                new SideMissionInfo(66, "Dispatch", small ? "Destroy one enemy Hero and at least six additional models in the enemy army." : "Destroy one enemy Hero and its unit."),
            };
        }

        public static SideMissionInfo Of(EGameSystem system, int id) => For(system).FirstOrDefault(i => i.Id == id) ?? For(system)[0];

        public static string PlayName(ESideMissionPlay p) => p switch
        {
            ESideMissionPlay.Off => "Off",
            ESideMissionPlay.Shared => "Shared",
            ESideMissionPlay.Hidden => "Hidden",
            ESideMissionPlay.SharedAndHidden => "Shared + Hidden",
            ESideMissionPlay.RandomPlaystyle => "Random playstyle",
            ESideMissionPlay.SharedDash => "Mad Dash: Shared Dash",
            ESideMissionPlay.HiddenDash => "Mad Dash: Hidden Dash",
            ESideMissionPlay.CumulativeDash => "Mad Dash: Cumulative Dash",
            _ => "Mad Dash: random variant",
        };

        public static string PlayText(ESideMissionPlay p) => p switch
        {
            ESideMissionPlay.Off => "No side-missions.",
            ESideMissionPlay.Shared => "Each player selects 2 side-missions openly, both players can complete any of the side-missions.",
            ESideMissionPlay.Hidden => "Each player selects 3 side-missions in secret, players can only complete their own side-mission.",
            ESideMissionPlay.SharedAndHidden => "Each player selects 1 side-missions openly, which both players can complete. Then each player selects 1 side-mission in secret, which only they can complete.",
            ESideMissionPlay.RandomPlaystyle => "Roll a D3 for Shared, Hidden or Shared + Hidden.",
            ESideMissionPlay.SharedDash => "At the beginning of each round, roll for one random side-mission, which both players can score during that round. Discard the side-mission if it wasn't scored during that round.",
            ESideMissionPlay.HiddenDash => "At the beginning of each round, each player rolls for one random side-mission in secret, which only they can score during that round. Discard the side-mission if it wasn't scored during that round.",
            ESideMissionPlay.CumulativeDash => "At the beginning of each round, roll for one random side-mission, which both players can score during any round. Side-missions are only discarded once they have been scored, and persist between rounds.",
            _ => "Roll a D3 for Shared Dash, Hidden Dash or Cumulative Dash.",
        } + (IsDash(p) ? "\nMad dash variants are played with progressive scoring: markers score X VP each round (1 marker = 3X, 2 = 2X each, 3+ = X each), and a side-mission scores X VP in the round it is scored, where X is the round number." : "");

        public static string PickName(ESideMissionPick p) => p switch
        {
            ESideMissionPick.Free => "Free",
            ESideMissionPick.Limited => "Limited",
            ESideMissionPick.Randomized => "Randomized",
            _ => "Random method",
        };

        public static string PickText(ESideMissionPick p) => p switch
        {
            ESideMissionPick.Free => "Players may freely select any side-mission from any of the categories.",
            ESideMissionPick.Limited => "For each side-mission, players must first roll one die to get a category, and may then freely select any side-mission.",
            ESideMissionPick.Randomized => "For each side-mission, players must first roll one die to get a category, and then roll one die to get a side-mission.",
            _ => "Roll a D3 for Free, Limited or Randomized.",
        } + "\nNote that if you roll a side-mission that cannot be completed, then you must re-roll until you get one that can be completed.";

        public static bool IsDash(ESideMissionPlay p) => p is ESideMissionPlay.SharedDash or ESideMissionPlay.HiddenDash
            or ESideMissionPlay.CumulativeDash or ESideMissionPlay.RandomDash;

        public const string ScoringText = "When playing with side-missions, the scoring at the end of the game is done using a Victory Points (VP) system, instead of winning based on the mission's conditions. "
            + "At the end of the game, players score VPs for each objective marker they control (1 marker = 3 VP, 2 markers = 2 VP each, 3+ = 1 VP each), plus 1 VP for each side-mission that they completed. The player that scored most VPs wins.";
    }

    /// <summary>A side-mission in play: open to both sides (<see cref="OwnerTeam"/> -1) or one side's secret.</summary>
    public sealed class SideMissionEntry
    {
        public int Id { get; set; }
        public int OwnerTeam { get; set; } = -1;
        public int RoundDrawn { get; set; }
        /// <summary>Scored or thrown away (Mad Dash).</summary>
        public bool Done { get; set; }
        public List<int> ScoredByTeams { get; set; } = new();
        public int ScoredRound { get; set; }
    }

    /// <summary>A unit destroyed, in order (Rush / Finalize).</summary>
    public sealed class SideKill
    {
        public int Round { get; set; }
        public int VictimTeam { get; set; }
        public string Unit { get; set; } = "";
    }

    /// <summary>The side-missions' running state, saved with the game (<see cref="GameProgressData.SideMissions"/>).</summary>
    public sealed class SideMissionState
    {
        public ESideMissionPlay Play { get; set; }
        public ESideMissionPick Pick { get; set; }
        public bool Chosen { get; set; }
        public List<SideMissionEntry> Missions { get; set; } = new();
        /// <summary>Team number -> VP.</summary>
        public Dictionary<int, int> Vp { get; set; } = new();
        public List<SideKill> Kills { get; set; } = new();
        /// <summary>[round, team] per successful spell.</summary>
        public List<int[]> Spells { get; set; } = new();
        /// <summary>Unit id + its team, per failed Morale Test.</summary>
        public List<string> MoraleFails { get; set; } = new();
        /// <summary>"id:team" -> first round the side-mission was seen completed (Mad Dash: "may be scored even if they
        /// were completed on a previous round").</summary>
        public Dictionary<string, int> Completed { get; set; } = new();
        public bool Progressive { get; set; }
    }

    /// <summary>The rules that make side-missions play.</summary>
    public static class SideMissions
    {
        private static float W => GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        private static float H => GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

        /// <summary>On: picked in the lobby, a standard two-sided game of a system whose Advanced book has them, no
        /// Mission Cards and no Attack &amp; Defend mission (those are won on their own terms).</summary>
        public static ESideMissionPlay Of(GameSettings s) =>
            s.SideMissions != ESideMissionPlay.Off && s.GameMode == EGameMode.Standard && SideMissionCatalog.Available(s.GameSystem)
            && !MissionCardGame.IsOn(s) && !ExtraMissionCatalog.IsAttackDefend(ExtraMissions.Of(s))
                ? s.SideMissions : ESideMissionPlay.Off;

        public static SideMissionState? StateOf(IReadableGameDataStore store) => GameProgressUtilities.SideMissions(store);

        public static bool IsOn(IGameContext c) => StateOf(c.GameDataStore) is { Play: not ESideMissionPlay.Off };

        private static void Save(IGameContext c, SideMissionState s) => GameProgressUtilities.SetSideMissions(c.GameDataStore, s);

        private static List<ITeam> Teams(IGameContext c) => c.TableState.Teams.Objects.OrderBy(t => t.TeamNumber).ToList();

        private static int TeamOf(IGameContext c, PlayerID p) => AttackDefend.TeamOf(c, p);

        private static int Enemy(IGameContext c, int team) => Teams(c).Select(t => t.TeamNumber).FirstOrDefault(n => n != team, -1);

        private static string TeamName(IGameContext c, int team) =>
            Teams(c).FirstOrDefault(t => t.TeamNumber == team) is { } t ? c.GetTeamLeadName(t) : $"Team {team}";

        private static int D6(IGameContext c) => HordePlacement.RollD6(c);

        // ---------------------------------------------------------------- events

        public static void UnitDestroyed(IGameContext c, IUnit dead)
        {
            if (StateOf(c.GameDataStore) is not { Play: not ESideMissionPlay.Off } s) return;
            s.Kills.Add(new SideKill { Round = c.TableState.Progress.RoundCount ?? 0, VictimTeam = TeamOf(c, dead.PlayerID), Unit = dead.ID.ID.ToString() });
            Save(c, s);
        }

        public static void SpellCast(IGameContext c, IUnit caster)
        {
            if (StateOf(c.GameDataStore) is not { Play: not ESideMissionPlay.Off } s) return;
            s.Spells.Add(new[] { c.TableState.Progress.RoundCount ?? 0, TeamOf(c, caster.PlayerID) });
            Save(c, s);
        }

        public static void MoraleFailed(IGameContext c, IUnit unit)
        {
            if (StateOf(c.GameDataStore) is not { Play: not ESideMissionPlay.Off } s) return;
            string key = $"{unit.ID.ID}:{TeamOf(c, unit.PlayerID)}";
            if (!s.MoraleFails.Contains(key)) s.MoraleFails.Add(key);
            Save(c, s);
        }

        // ---------------------------------------------------------------- the table, read for a side

        private static IEnumerable<IUnit> UnitsOf(IGameContext c, int team) =>
            c.TableState.Units.Objects.Where(u => TeamOf(c, u.PlayerID) == team);

        private static bool Destroyed(IUnit u) => !u.GetIsAlive() && !u.Tokens.HasToken(TokenType.Conceded);

        private static int Size(IUnit u) => u.Models.Count;

        private static float ToughOf(IUnit u) => u.Models.Select(m => m.TotalWounds).DefaultIfEmpty(1f).Max();

        private static int CostOf(IUnit u) => u is UnitData d ? d.PointCost : 0;

        private static IEnumerable<(IUnit Unit, IModel Model)> HeroesOf(IGameContext c, int team) =>
            UnitsOf(c, team).SelectMany(u => u.Models.Where(m => MissionCardGame.IsHeroModel(u, m)).Select(m => (u, m)));

        private static bool FullyIn(IUnit u, Func<IModel, bool> inside) =>
            u.GetIsAlive() && u.GetIsOnBattlefield() && u.Models.Where(m => m.GetIsAlive()).All(inside);

        private static bool InCircle(IModel m, float cx, float cz, float r) =>
            Position.GetDistance2D(m.Position, new Position(cx, cz)) + m.BaseShape.BoundingRadiusInches <= r + 0.01f;

        private static bool InRect(IModel m, float left, float right, float bottom, float top)
        {
            float b = m.BaseShape.BoundingRadiusInches;
            return m.Position.x - b >= left - 0.01f && m.Position.x + b <= right + 0.01f
                && m.Position.z - b >= bottom - 0.01f && m.Position.z + b <= top + 0.01f;
        }

        private static int ControlledBy(IGameContext c, int team)
        {
            int n = 0;
            foreach ((int slot, int count) in ExtraMissions.Controlled(c))
            {
                IPlayerSlotInfo? seat = c.TableState.Players.Objects.FirstOrDefault(p => p.IsFilled && p.SlotID == slot);
                if (seat != null && seat.TeamNumber == team) n += count;
            }
            return n;
        }

        private static int MarkersInPlay(IGameContext c) =>
            c.TableState.Objectives.Objects.Count() + (ExtraMissions.CarriedCount(c));

        /// <summary>Whether <paramref name="team"/> has done side-mission <paramref name="id"/> as the table stands now.</summary>
        public static bool IsComplete(IGameContext c, int id, int team, SideMissionState s)
        {
            int enemy = Enemy(c, team);
            EGameSystem system = c.Settings.GameSystem;
            int squad = SideMissionCatalog.SquadSize(system);
            bool small = SideMissionCatalog.Small(system);
            List<IUnit> mine = UnitsOf(c, team).ToList(), theirs = UnitsOf(c, enemy).ToList();
            int myMarkers = ControlledBy(c, team), theirMarkers = ControlledBy(c, enemy);

            bool KilledTop(IEnumerable<IUnit> candidates, Func<IUnit, float> key, bool highest)
            {
                List<IUnit> list = candidates.ToList();
                if (list.Count == 0) return false;
                float best = highest ? list.Max(key) : list.Min(key);
                return list.Where(u => MathF.Abs(key(u) - best) < 0.01f).Any(Destroyed);
            }

            bool KilledTopHero(Func<(IUnit Unit, IModel Model), float> key, bool highest)
            {
                var list = HeroesOf(c, enemy).ToList();
                if (list.Count == 0) return false;
                float best = highest ? list.Max(key) : list.Min(key);
                return list.Where(h => MathF.Abs(key(h) - best) < 0.01f).Any(h => !h.Model.GetIsAlive());
            }

            int DeadHeroes(int side) => HeroesOf(c, side).Count(h => !h.Model.GetIsAlive());

            switch (id)
            {
                case 11: return myMarkers == 0;
                case 12: return myMarkers == 1;
                case 13: return myMarkers * 2 > MarkersInPlay(c);
                case 14: return myMarkers > theirMarkers;
                case 15: return theirMarkers == 0;
                case 16: return myMarkers == theirMarkers;
                case 21: return mine.Any(u => FullyIn(u, m => InCircle(m, W / 2f, H / 2f, 6f)));
                case 22: return !theirs.Any(u => FullyIn(u, m => InCircle(m, W / 2f, H / 2f, 6f)));
                case 23:
                {
                    var quarters = new[] { (0f, W / 2f, 0f, H / 2f), (W / 2f, W, 0f, H / 2f), (0f, W / 2f, H / 2f, H), (W / 2f, W, H / 2f, H) };
                    return quarters.All(q => mine.Any(u => FullyIn(u, m => InRect(m, q.Item1, q.Item2, q.Item3, q.Item4))));
                }
                case 24:
                {
                    var corners = new[] { (0f, 0f), (W, 0f), (0f, H), (W, H) };
                    return corners.All(k => mine.Any(u => u.GetIsAlive() && u.GetIsOnBattlefield() && u.Models.Any(m => m.GetIsAlive()
                        && Position.GetDistance2D(m.Position, new Position(k.Item1, k.Item2)) - m.BaseShape.BoundingRadiusInches <= 12f)));
                }
                case 25:
                {
                    if (GameProgressUtilities.DeploymentZones(c.GameDataStore)?.TryGetValue(enemy, out ZoneRect? z) != true || z == null) return false;
                    return mine.Count(u => FullyIn(u, m => InRect(m, z.Left, z.Right, z.Bottom, z.Top))) >= 2;
                }
                case 26:
                {
                    if (GameProgressUtilities.DeploymentZones(c.GameDataStore)?.TryGetValue(team, out ZoneRect? z) != true || z == null) return true;
                    return !theirs.Any(u => FullyIn(u, m => InRect(m, z.Left, z.Right, z.Bottom, z.Top)));
                }
                case 31: return s.Kills.FirstOrDefault() is { } first && first.VictimTeam == enemy;
                case 32: return s.Kills.LastOrDefault() is { } last && last.VictimTeam == enemy;
                case 33: return mine.Count == 0 || mine.Count(u => !Destroyed(u)) * 2 >= mine.Count;
                case 34: return theirs.Count > 0 && theirs.Count(Destroyed) * 2 >= theirs.Count;
                case 35: return s.MoraleFails.Count(k => k.EndsWith(":" + enemy)) >= 2;
                case 36: return s.Spells.Count(x => x[1] == team) >= 4;
                case 41: return theirs.Count(u => Size(u) >= squad && Destroyed(u)) >= (small ? 2 : 3);
                case 42: return theirs.Count(u => Size(u) >= squad && Destroyed(u)) > mine.Count(u => Size(u) >= squad && Destroyed(u));
                case 43: return KilledTop(theirs.Where(u => Size(u) >= squad), u => Size(u), true);
                case 44: return KilledTop(theirs.Where(u => Size(u) >= squad), u => CostOf(u), true);
                case 45: return KilledTop(theirs.Where(u => Size(u) >= squad), u => CostOf(u), false);
                case 46: return theirs.Sum(u => u.Models.Count(m => !m.GetIsAlive())) >= (small ? 10 : 20);
                case 51: return theirs.Count(u => ToughOf(u) >= 9f && Destroyed(u)) >= 2;
                case 52: return theirs.Count(u => ToughOf(u) >= 9f && Destroyed(u)) > mine.Count(u => ToughOf(u) >= 9f && Destroyed(u));
                case 53: return KilledTop(theirs.Where(u => ToughOf(u) >= 9f), ToughOf, true);
                case 54: return KilledTop(theirs.Where(u => ToughOf(u) >= 9f), u => CostOf(u), true);
                case 55: return KilledTop(theirs.Where(u => ToughOf(u) >= 9f), u => CostOf(u), false);
                case 56: return theirs.Any(u => ToughOf(u) >= 18f && u.Models.Sum(m => m.WoundsDealt) >= 12f);
                case 61: return DeadHeroes(enemy) >= 2;
                case 62: return DeadHeroes(enemy) > DeadHeroes(team);
                case 63: return KilledTopHero(h => h.Model.TotalWounds, true);
                case 64: return KilledTopHero(h => MissionCardGame.HeroCost(h.Unit, h.Model), true);
                case 65: return KilledTopHero(h => MissionCardGame.HeroCost(h.Unit, h.Model), false);
                case 66:
                    if (small)
                        return DeadHeroes(enemy) >= 1 && theirs.Sum(u => u.Models.Count(m => !m.GetIsAlive())) >= 7;
                    return HeroesOf(c, enemy).Any(h => !h.Model.GetIsAlive() && Destroyed(h.Unit));
                default: return false;
            }
        }

        /// <summary>"if you roll a side-mission that cannot be completed, then you must re-roll": judged for the side that
        /// gets it, from the armies as they are at the start.</summary>
        public static bool CanBeCompleted(IGameContext c, int id, int team)
        {
            int enemy = Enemy(c, team);
            EGameSystem system = c.Settings.GameSystem;
            int squad = SideMissionCatalog.SquadSize(system);
            bool small = SideMissionCatalog.Small(system);
            List<IUnit> mine = UnitsOf(c, team).ToList(), theirs = UnitsOf(c, enemy).ToList();
            int heroes = HeroesOf(c, enemy).Count();
            int bigUnits = theirs.Count(u => Size(u) >= squad), monsters = theirs.Count(u => ToughOf(u) >= 9f);
            int markers = c.TableState.Objectives.Objects.Count();
            return id switch
            {
                12 or 13 or 14 => markers > 0,
                23 or 24 => mine.Count >= 4,
                25 => mine.Count >= 2,
                34 => theirs.Count > 0,
                35 => theirs.Count >= 2,
                36 => mine.Any(u => MissionCardGame.HasRule(u.RuleDefinitions, "Caster") || u.Models.Any(m => MissionCardGame.HasRule(m.RuleDefinitions, "Caster"))),
                41 => bigUnits >= (small ? 2 : 3),
                42 or 43 or 44 or 45 => bigUnits >= 1,
                46 => theirs.Sum(u => u.Models.Count) >= (small ? 10 : 20),
                51 => monsters >= 2,
                52 or 53 or 54 or 55 => monsters >= 1,
                56 => theirs.Any(u => ToughOf(u) >= 18f),
                61 => heroes >= 2,
                62 or 63 or 64 or 65 => heroes >= 1,
                66 => heroes >= 1 && (!small || theirs.Sum(u => u.Models.Count) >= 7),
                _ => true,
            };
        }

        /// <summary>The bot's rough sense of how likely a side-mission is: the ones it can steer toward or that tend to
        /// happen in any game first (house rules C, "Side-Missions").</summary>
        private static int BotWeight(int id) => id switch
        {
            33 => 9, 26 => 8, 22 => 8, 34 => 7, 46 => 7, 31 => 6, 61 => 6, 62 => 6, 41 => 6, 42 => 6, 51 => 5, 52 => 5,
            21 => 5, 32 => 5, 35 => 5, 63 => 5, 64 => 5, 65 => 5, 44 => 5, 45 => 5, 43 => 5, 53 => 5, 54 => 4, 55 => 4,
            14 => 4, 16 => 4, 36 => 4, 66 => 4, 56 => 3, 25 => 3, 13 => 3, 15 => 2, 12 => 2, 24 => 2, 23 => 2, 11 => 1,
            _ => 1,
        };

        // ---------------------------------------------------------------- choosing them

        private static string Label(EGameSystem system, int id)
        {
            // Names are unique across the 36, and the pick is always within one category.
            return SideMissionCatalog.Of(system, id).Name;
        }

        /// <summary>One side-mission for <paramref name="team"/> by the pick method: Free (choose any), Limited (roll the
        /// category, choose in it), Randomized (roll both). Never one already in play, never one it can't complete.</summary>
        private static async Task<int> PickOne(IGameContext c, SideMissionState s, int team, bool secret, ESideMissionPick pick)
        {
            EGameSystem system = c.Settings.GameSystem;
            ITeam? t = Teams(c).FirstOrDefault(x => x.TeamNumber == team);
            PlayerID who = t?.Players.First() ?? default;
            var taken = s.Missions.Where(m => !m.Done && (m.OwnerTeam == -1 || m.OwnerTeam == team)).Select(m => m.Id).ToHashSet();
            bool Ok(int id) => !taken.Contains(id) && CanBeCompleted(c, id, team);
            List<SideMissionInfo> all = SideMissionCatalog.For(system).Where(i => Ok(i.Id)).ToList();
            if (all.Count == 0) return 0;

            if (pick == ESideMissionPick.Randomized)
            {
                for (int tries = 0; tries < 200; tries++)
                {
                    int id = D6(c) * 10 + D6(c);
                    if (Ok(id)) return id;
                }
                return all[0].Id;
            }

            List<SideMissionInfo> choices = all;
            if (pick == ESideMissionPick.Free)
            {
                // Free: the category first, then the side-mission in it - six buttons at a time, not 36.
                var cats = all.Select(i => i.Category).Distinct().OrderBy(x => x).ToList();
                List<string> catLabels = cats.Select(SideMissionCatalog.CategoryName).ToList();
                var catText = cats.ToDictionary(SideMissionCatalog.CategoryName,
                    k => string.Join("\n", all.Where(i => i.Category == k).Select(i => $"{i.Name} - {i.Text}")));
                ESideCategory bestCat = all.OrderByDescending(i => BotWeight(i.Id)).First().Category;
                var catRequest = new StringSelectionRequest(who,
                    $"Side-missions: pick a category{(secret ? " (secret side-mission)" : " (open side-mission)")}.",
                    catLabels, new List<StringSelectionRequest.InvalidOption>(), optionDescriptions: catText,
                    displayName: "Side-Mission Category")
                { Recommended = SideMissionCatalog.CategoryName(bestCat) };
                string catAnswer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(catRequest);
                int ci = catLabels.IndexOf(catAnswer);
                ESideCategory chosen = cats[ci < 0 ? 0 : ci];
                choices = all.Where(i => i.Category == chosen).ToList();
            }
            if (pick == ESideMissionPick.Limited)
            {
                for (int tries = 0; tries < 200; tries++)
                {
                    var cat = (ESideCategory)D6(c);
                    var inCat = all.Where(i => i.Category == cat).ToList();
                    if (inCat.Count == 0) continue;
                    c.Log($"Side-missions: {TeamName(c, team)} rolls {SideMissionCatalog.CategoryName(cat)}.");
                    choices = inCat;
                    break;
                }
            }
            List<string> labels = choices.Select(i => Label(system, i.Id)).ToList();
            var descriptions = choices.ToDictionary(i => Label(system, i.Id), i => i.Text);
            string recommended = labels[choices.Select((i, k) => (i, k)).OrderByDescending(x => BotWeight(x.i.Id)).First().k];
            var request = new StringSelectionRequest(who,
                $"Side-missions: pick a side-mission{(secret ? " (secret - only you can complete it)" : " (open - both sides can complete it)")}.",
                labels, new List<StringSelectionRequest.InvalidOption>(), optionDescriptions: descriptions,
                displayName: secret ? "Secret Side-Mission" : "Side-Mission")
            { Recommended = recommended };
            string answer = await c.PlayerRequester.RequestDecision<StringSelectionRequest, string>(request);
            int index = labels.IndexOf(answer);
            return choices[index < 0 ? 0 : index].Id;
        }

        private static void Add(IGameContext c, SideMissionState s, int id, int owner, int round)
        {
            if (id == 0) return;
            s.Missions.Add(new SideMissionEntry { Id = id, OwnerTeam = owner, RoundDrawn = round });
            SideMissionInfo info = SideMissionCatalog.Of(c.Settings.GameSystem, id);
            c.Log(owner < 0
                ? $"Side-mission (both sides): {info.Name} - {info.Text}"
                : $"Side-missions: {TeamName(c, owner)} takes a secret side-mission.");
        }

        /// <summary>A random side-mission (Mad Dash), re-rolled until <paramref name="forTeam"/> (or, shared, either side)
        /// can complete it and it isn't already in play.</summary>
        private static int Roll(IGameContext c, SideMissionState s, int forTeam)
        {
            var active = s.Missions.Where(m => !m.Done).Select(m => m.Id).ToHashSet();
            List<int> teams = forTeam >= 0 ? new List<int> { forTeam } : Teams(c).Select(t => t.TeamNumber).ToList();
            for (int tries = 0; tries < 300; tries++)
            {
                int id = D6(c) * 10 + D6(c);
                if (!active.Contains(id) && teams.Any(t => CanBeCompleted(c, id, t))) return id;
            }
            return 33;
        }

        /// <summary>"Before the game begins": settle the playstyle and method, then the picks (round 1); Mad Dash rolls
        /// each round.</summary>
        public static async Task RoundStart(IGameContext c, int round)
        {
            ESideMissionPlay picked = Of(c.Settings);
            if (picked == ESideMissionPlay.Off) return;
            SideMissionState s = StateOf(c.GameDataStore) ?? new SideMissionState();
            if (!s.Chosen)
            {
                if (Teams(c).Count != 2)
                {
                    c.Log("Side-missions need exactly two sides - not used in this game.");
                    s.Play = ESideMissionPlay.Off;
                    s.Chosen = true;
                    Save(c, s);
                    return;
                }
                if (ExtraMissionCatalog.IsAttackDefend(ExtraMissions.Current(c)))
                {
                    c.Log("Side-missions are not used with Attack & Defend missions (they are won on their own terms).");
                    s.Play = ESideMissionPlay.Off;
                    s.Chosen = true;
                    Save(c, s);
                    return;
                }
                ESideMissionPlay play = picked;
                if (play == ESideMissionPlay.RandomPlaystyle) play = (ESideMissionPlay)((D6(c) + 1) / 2);
                if (play == ESideMissionPlay.RandomDash) play = (ESideMissionPlay)((int)ESideMissionPlay.SharedDash + (D6(c) + 1) / 2 - 1);
                ESideMissionPick pick = c.Settings.SideMissionPick;
                if (pick == ESideMissionPick.Random) pick = (ESideMissionPick)((D6(c) + 1) / 2 - 1);
                s.Play = play;
                s.Pick = pick;
                s.Progressive = SideMissionCatalog.IsDash(play)
                    || (ExtraMissions.Current(c) is var m && m != EExtraMission.None && ExtraMissionCatalog.Of(m).Progressive);
                s.Chosen = true;
                Save(c, s);
                c.Log($"Side-missions: {SideMissionCatalog.PlayName(play)}{(SideMissionCatalog.IsDash(play) ? "" : $", {SideMissionCatalog.PickName(pick)} selection")}. Scoring is by victory points.");
                await c.Announce($"Side-missions: {SideMissionCatalog.PlayName(play)}", new TextColor(255, 210, 80, 255), EBannerTier.Headline);

                if (!SideMissionCatalog.IsDash(play))
                {
                    // "When selecting side-missions openly, the players must roll-off, and then alternate in picking 1
                    // side-mission each, starting with the player that won the roll-off."
                    List<ITeam> teams = Teams(c);
                    int open = play switch { ESideMissionPlay.Shared => 2, ESideMissionPlay.SharedAndHidden => 1, _ => 0 };
                    int hidden = play switch { ESideMissionPlay.Hidden => 3, ESideMissionPlay.SharedAndHidden => 1, _ => 0 };
                    if (open > 0)
                    {
                        List<ITeam> order = await DiceUtilities.RollOff_Ordered(teams, teams.Select(t => c.GetTeamLeadName(t)).ToList(),
                            c.TextOutput, c.DiceRoller, c.Presenter, "Side-Mission Roll-Off");
                        for (int i = 0; i < open; i++)
                            foreach (ITeam t in order)
                            {
                                Add(c, s, await PickOne(c, s, t.TeamNumber, false, pick), -1, round);
                                Save(c, s);
                            }
                    }
                    for (int i = 0; i < hidden; i++)
                        foreach (ITeam t in teams)
                        {
                            Add(c, s, await PickOne(c, s, t.TeamNumber, true, pick), t.TeamNumber, round);
                            Save(c, s);
                        }
                    return;
                }
            }

            if (s.Play == ESideMissionPlay.Off) return;
            // Mad Dash: a fresh one each round.
            if (s.Play is ESideMissionPlay.SharedDash or ESideMissionPlay.CumulativeDash)
                Add(c, s, Roll(c, s, -1), -1, round);
            else if (s.Play == ESideMissionPlay.HiddenDash)
                foreach (ITeam t in Teams(c)) Add(c, s, Roll(c, s, t.TeamNumber), t.TeamNumber, round);
            Save(c, s);
        }

        // ---------------------------------------------------------------- scoring

        /// <summary>Objective VP by how many markers are in play: 1 = 3 each, 2 = 2 each, 3+ = 1 each.</summary>
        public static int PerMarker(int markersInPlay) => markersInPlay <= 1 ? 3 : markersInPlay == 2 ? 2 : 1;

        /// <summary>Side-missions that are a state of the game as it is scored, never "done" earlier: Tactical Genius
        /// (objectives held) and Finalize (the last unit destroyed).</summary>
        private static bool JudgedAtTheEnd(SideMissionInfo info) => info.Category == ESideCategory.TacticalGenius || info.Id == 32;

        private static bool CanScore(SideMissionEntry e, int team) => e.OwnerTeam < 0 || e.OwnerTeam == team;

        /// <summary>
        /// The end of each round: note which side-missions each side has done (Mad Dash scores them now; Tactical
        /// Genius only counts in the round it is scored), and score markers each round in progressive play.
        /// </summary>
        public static async Task RoundEnd(IGameContext c, int round, bool lastRound)
        {
            if (StateOf(c.GameDataStore) is not { Play: not ESideMissionPlay.Off, Chosen: true } s) return;
            List<int> teams = Teams(c).Select(t => t.TeamNumber).ToList();
            foreach (SideMissionInfo info in SideMissionCatalog.For(c.Settings.GameSystem))
                foreach (int team in teams)
                {
                    string key = $"{info.Id}:{team}";
                    if (!s.Completed.ContainsKey(key) && !JudgedAtTheEnd(info) && IsComplete(c, info.Id, team, s))
                        s.Completed[key] = round;
                }

            int markers = MarkersInPlay(c);
            if (s.Progressive)
                foreach (int team in teams)
                {
                    int held = ControlledBy(c, team), gained = held * PerMarker(markers) * round;
                    s.Vp[team] = s.Vp.GetValueOrDefault(team) + gained;
                    if (gained > 0) c.Log($"Side-missions, round {round}: {TeamName(c, team)} scores {gained} VP for {held} marker{(held == 1 ? "" : "s")}.");
                }

            if (SideMissionCatalog.IsDash(s.Play))
            {
                foreach (SideMissionEntry e in s.Missions.Where(e => !e.Done).ToList())
                {
                    SideMissionInfo info = SideMissionCatalog.Of(c.Settings.GameSystem, e.Id);
                    foreach (int team in teams.Where(t => CanScore(e, t)))
                    {
                        // Tactical Genius: only in the round it is scored. Finalize ("the last player to destroy an
                        // enemy unit") can only be known when the game ends.
                        bool done = info.Category == ESideCategory.TacticalGenius ? IsComplete(c, e.Id, team, s)
                            : e.Id == 32 ? lastRound && IsComplete(c, e.Id, team, s)
                            : s.Completed.ContainsKey($"{e.Id}:{team}");
                        if (!done) continue;
                        e.ScoredByTeams.Add(team);
                        s.Vp[team] = s.Vp.GetValueOrDefault(team) + round;
                        c.Log($"Side-missions, round {round}: {TeamName(c, team)} scores {info.Name} for {round} VP.");
                    }
                    if (e.ScoredByTeams.Count > 0)
                    {
                        e.Done = true;
                        e.ScoredRound = round;
                        await c.Announce($"Side-mission scored: {info.Name}", new TextColor(255, 210, 80, 255));
                    }
                    else if (s.Play != ESideMissionPlay.CumulativeDash)
                    {
                        e.Done = true;
                        c.Log($"Side-missions: {info.Name} was not scored this round and is discarded.");
                    }
                }
            }
            Save(c, s);
        }

        /// <summary>The result (team lead seat -> VP): markers held at the end (non-progressive) plus each completed
        /// side-mission (1 VP, or X = the final round in progressive play), plus everything scored round by round.</summary>
        public static (Dictionary<int, int> Score, string Unit)? FinalScore(IGameContext c)
        {
            if (StateOf(c.GameDataStore) is not { Play: not ESideMissionPlay.Off, Chosen: true } s) return null;
            int final = c.TableState.Progress.RoundCount ?? 4;
            var total = new Dictionary<int, int>(s.Vp);
            int markers = MarkersInPlay(c);
            foreach (ITeam t in Teams(c))
            {
                int team = t.TeamNumber;
                int vp = total.GetValueOrDefault(team);
                if (!s.Progressive)
                {
                    int held = ControlledBy(c, team);
                    vp += held * PerMarker(markers);
                    c.Log($"Side-missions: {TeamName(c, team)} holds {held} marker{(held == 1 ? "" : "s")} ({held * PerMarker(markers)} VP).");
                }
                if (!SideMissionCatalog.IsDash(s.Play))
                {
                    int each = s.Progressive ? final : 1;
                    foreach (SideMissionEntry e in s.Missions.Where(e => CanScore(e, team)))
                    {
                        SideMissionInfo info = SideMissionCatalog.Of(c.Settings.GameSystem, e.Id);
                        // State side-missions (objectives, positions, Conserve) are judged as the game ends; the rest
                        // count once they have happened.
                        bool done = JudgedAtTheEnd(info) || info.Category == ESideCategory.FieldMarshal || e.Id == 33
                            ? IsComplete(c, e.Id, team, s)
                            : IsComplete(c, e.Id, team, s) || s.Completed.ContainsKey($"{e.Id}:{team}");
                        if (!done) continue;
                        vp += each;
                        c.Log($"Side-missions: {TeamName(c, team)} completed {info.Name} ({each} VP).");
                    }
                }
                total[team] = vp;
            }
            var bySeat = new Dictionary<int, int>();
            foreach (ITeam t in Teams(c))
            {
                IPlayerSlotInfo? lead = c.TableState.Players.Objects.Where(p => p.IsFilled && p.TeamNumber == t.TeamNumber).OrderBy(p => p.SlotID).FirstOrDefault();
                if (lead != null) bySeat[lead.SlotID] = total.GetValueOrDefault(t.TeamNumber);
            }
            return (bySeat, "VP");
        }
    }
}
