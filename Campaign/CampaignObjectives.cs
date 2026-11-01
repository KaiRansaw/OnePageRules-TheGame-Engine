using FDG.Players;

namespace FDG.Campaign
{
    /// <summary>How a special objective came out, and why.</summary>
    /// <param name="Objective">The objective's name as the book prints it.</param>
    /// <param name="Completed">Whether it was completed.</param>
    /// <param name="Detail">One line saying what was counted, for the game-over card and the history.</param>
    public readonly record struct CampaignObjectiveResult(string Objective, bool Completed, string Detail);

    /// <summary>
    /// #430 - scoring the six special objectives of Campaign Rules v3.6.0 at the end of a mission. Each
    /// is worth <see cref="CampaignNumbers.SpecialObjectiveBonus"/> into the army's treasury.
    ///
    /// <para>
    /// Until now an objective was <b>rolled and then never checked</b>: the campaign screen showed the
    /// player what they had drawn, the mission was played, and nothing ever asked whether they had done
    /// it. So the bonus could not be earned at all.
    /// </para>
    ///
    /// <para>
    /// <b>Four of the six are read off the finished table</b> - who is standing where, which objectives
    /// are held, whose heroes are dead. The other two cannot be: a resolved spell leaves no trace, and a
    /// finished table records that units died but not in what order. Those two are tallied while the game
    /// runs, on the units' own campaign sheets (<see cref="StrikeUnit.SpellsCast"/> and
    /// <see cref="StrikeUnit.DrewFirstBlood"/>) - which the engine already writes at exactly those two
    /// moments, and which cost an ordinary game nothing.
    /// </para>
    ///
    /// <para>
    /// Everything here is a read: it reports, and the caller pays. That keeps it testable against a
    /// hand-built table and keeps the treasury write in one place
    /// (<see cref="CampaignPreGame.CompleteSpecialObjective"/>).
    /// </para>
    /// </summary>
    public static class CampaignObjectives
    {
        /// <summary>Hexer's bar: "successfully cast four or more spells by the end of the game".</summary>
        public const int HexerSpells = 4;

        /// <summary>
        /// Scores one army's special objective against the finished table. An objective this scorer
        /// cannot decide comes back <c>Completed: false</c> with a Detail that says so, rather than
        /// claiming it failed - the two are not the same thing and the difference belongs in the log.
        /// </summary>
        /// <param name="objective">The objective's name, as rolled by <see cref="CampaignPreGame.RollSpecialObjective"/>.</param>
        /// <param name="player">Whose objective it is.</param>
        public static CampaignObjectiveResult Score(ITableState table, PlayerID player, string objective) =>
            objective switch
            {
                "Hexer" => Hexer(table, player),
                "Annihilator" => Annihilator(table, player),
                "Headhunter" => Headhunter(table, player),
                "Dominator" => Dominator(table, player),
                "Intimidator" => Intimidator(table, player),
                "Strategist" => Strategist(table, player),
                _ => new CampaignObjectiveResult(objective, false,
                    $"\"{objective}\" is not one of the book's six special objectives, so it was not scored."),
            };

        // -- Counted while the game runs, on the units' own campaign sheets --------------------------

        /// <summary>
        /// "Successfully cast four or more spells by the end of the game." Summed across the army's
        /// sheets, where <c>CastSpellStage</c> counts each success - a resolved cast leaves nothing on
        /// the table to count afterwards.
        /// </summary>
        private static CampaignObjectiveResult Hexer(ITableState table, PlayerID player)
        {
            int cast = Sheets(table, player).Sum(sheet => sheet.SpellsCast);
            return new CampaignObjectiveResult("Hexer", cast >= HexerSpells,
                $"cast {cast} spell(s) successfully; needed {HexerSpells}.");
        }

        /// <summary>
        /// "Be the first player to destroy an enemy unit." The unit that made the game's first kill
        /// carries the stamp (<see cref="StrikeUnit.DrewFirstBlood"/>); at most one does, so whose army
        /// it is in is the answer.
        /// </summary>
        private static CampaignObjectiveResult Annihilator(ITableState table, PlayerID player)
        {
            bool mine = Sheets(table, player).Any(sheet => sheet.DrewFirstBlood);
            if (mine)
                return new CampaignObjectiveResult("Annihilator", true,
                    "destroyed the first enemy unit of the game.");

            bool anyone = AllSheets(table).Any(sheet => sheet.DrewFirstBlood);
            return new CampaignObjectiveResult("Annihilator", false,
                anyone ? "an opponent destroyed the first enemy unit of the game."
                       : "no unit was destroyed by anyone, so nobody was first.");
        }

        // -- Read off the finished table ---------------------------------------------------------------

        /// <summary>
        /// "Kill one or more enemy Heroes." Read from the campaign sheets, which have counted hero kills
        /// per unit since the campaign existed - so this needs nothing new and is right even for a hero
        /// killed by a unit that later died itself.
        /// </summary>
        private static CampaignObjectiveResult Headhunter(ITableState table, PlayerID player)
        {
            int heroKills = Sheets(table, player).Sum(sheet => sheet.HeroKills);
            return new CampaignObjectiveResult("Headhunter", heroKills > 0,
                $"killed {heroKills} enemy Hero unit(s).");
        }

        /// <summary>
        /// "Have one unit fully within each table quarter at the end of the game."
        /// <para>
        /// Quarters are the table halved on both axes. "Fully within" means every living model of the
        /// unit is inside the same quarter, base and all - the same reading the engine's other
        /// fully-within tests use.
        /// </para>
        /// </summary>
        private static CampaignObjectiveResult Dominator(ITableState table, PlayerID player)
        {
            // The table is laid out from the origin, so the middle is half of each dimension. Taken from
            // THIS game's own system rather than from GameWideConstants' live globals: a Firefight or
            // Skirmish table is 48x48 where Grimdark Future's is 72x48, and quartering the wrong one
            // would put the dividing line in the wrong place. (The globals carry the same answer in a
            // real game, but they are mutable process-wide state and this is a pure read.)
            (float width, float height) = TableSizeFor(table);
            float midX = width / 2f;
            float midZ = height / 2f;

            var held = new HashSet<int>();
            foreach (IUnit unit in Mine(table, player))
            {
                int? quarter = QuarterOf(unit, midX, midZ);
                if (quarter.HasValue) held.Add(quarter.Value);
            }

            return new CampaignObjectiveResult("Dominator", held.Count == 4,
                $"had a unit fully within {held.Count} of the 4 table quarters.");
        }

        /// <summary>
        /// "Have most of your units inside the enemy deployment zone at the end of the game."
        /// <para>
        /// Needs the deployment zones, which #429 records for the whole game. An older save has none, and
        /// this says so rather than scoring a guess.
        /// </para>
        /// <para>
        /// <b>Judgement call:</b> "the enemy deployment zone" is singular and the book assumes two
        /// players. With more than two, it is read as <b>any</b> opposing team's zone - a unit standing in
        /// one of three enemy zones has done the thing the objective describes, and the alternative
        /// (pick one enemy and ignore the others) would make the objective depend on an arbitrary choice.
        /// Recorded in claude/house-rules.md.
        /// </para>
        /// </summary>
        private static CampaignObjectiveResult Intimidator(ITableState table, PlayerID player)
        {
            if (GameProgressUtilities.DeploymentZones(table.DataStore) is not { Count: > 0 } zones)
                return new CampaignObjectiveResult("Intimidator", false,
                    "this game did not record its deployment zones, so this one could not be scored.");

            int? myTeam = TeamOf(table, player);
            var enemyZones = zones.Where(z => myTeam == null || z.Key != myTeam.Value)
                .Select(z => z.Value).ToList();
            if (enemyZones.Count == 0)
                return new CampaignObjectiveResult("Intimidator", false,
                    "no opposing deployment zone was recorded, so this one could not be scored.");

            // SURVIVING units only. The table keeps destroyed units, and counting them would make the
            // objective harder the worse the mission went: "most of your units" at the end of the game
            // can only mean the ones still standing - a destroyed unit is not anywhere.
            List<IUnit> mine = Mine(table, player).Where(u => u.GetIsAlive()).ToList();
            int inside = mine.Count(unit => LivingModels(unit).Any(m =>
                enemyZones.Any(zone => zone.Contains(m.Position))));

            bool most = mine.Count > 0 && inside * 2 > mine.Count;
            return new CampaignObjectiveResult("Intimidator", most,
                $"had {inside} of {mine.Count} surviving unit(s) in an enemy deployment zone.");
        }

        /// <summary>
        /// "Control most objectives at the end of the game." Counted from the markers' own owners, which
        /// is the same number the victory check uses - so this can never disagree with who won.
        /// </summary>
        private static CampaignObjectiveResult Strategist(ITableState table, PlayerID player)
        {
            int? myTeam = TeamOf(table, player);
            int mine = 0, best = 0;
            var byTeam = new Dictionary<int, int>();

            foreach (IObjective objective in table.Objectives.Objects)
            {
                if (objective.OwnerID is not { } owner || owner.ID == Guid.Empty) continue;
                int team = TeamOf(table, owner) ?? -1;
                byTeam[team] = byTeam.TryGetValue(team, out int already) ? already + 1 : 1;
            }

            foreach ((int team, int count) in byTeam)
            {
                if (myTeam != null && team == myTeam.Value) mine = count;
                if (count > best) best = count;
            }

            // "Most" means strictly more than anyone else: a tie is not control.
            bool outright = mine > 0 && mine == best && byTeam.Count(p => p.Value == best) == 1;
            return new CampaignObjectiveResult("Strategist", outright,
                $"controlled {mine} objective(s); the best any side held was {best}.");
        }

        // -- Shared ------------------------------------------------------------------------------------

        private static IEnumerable<IUnit> Mine(ITableState table, PlayerID player) =>
            table.Units.Objects.Where(u => u.PlayerID == player);

        /// <summary>This player's units' campaign sheets. Empty for a non-campaign army.</summary>
        private static IEnumerable<StrikeUnit> Sheets(ITableState table, PlayerID player) =>
            Mine(table, player).OfType<UnitData>()
                .Select(u => StrikeUnit.FromJson(u.StrikeUnitJson))
                .Where(sheet => sheet != null)!;

        /// <summary>Every campaign sheet on the table, whoever owns it.</summary>
        private static IEnumerable<StrikeUnit> AllSheets(ITableState table) =>
            table.Units.Objects.OfType<UnitData>()
                .Select(u => StrikeUnit.FromJson(u.StrikeUnitJson))
                .Where(sheet => sheet != null)!;

        private static IEnumerable<IModel> LivingModels(IUnit unit) =>
            unit.Models.Where(m => m.GetIsAlive());

        /// <summary>
        /// Which quarter a unit is FULLY within, or null when it straddles a line or has no living
        /// models. Model positions decide it; a base clipping the middle of the table is a judgement a
        /// human makes, and the engine's other fully-within tests read centres too.
        /// </summary>
        private static int? QuarterOf(IUnit unit, float midX, float midZ)
        {
            int? quarter = null;
            foreach (IModel model in LivingModels(unit))
            {
                int here = (model.Position.x < midX ? 0 : 1) + (model.Position.z < midZ ? 0 : 2);
                if (quarter == null) quarter = here;
                else if (quarter.Value != here) return null;
            }
            return quarter;
        }

        /// <summary>
        /// The table this game is played on, from the progress record's own settings, falling back to the
        /// process-wide default when no record exists (which only happens outside a real game).
        /// </summary>
        private static (float Width, float Height) TableSizeFor(ITableState table) =>
            GameProgressUtilities.TryGetProgress(table.DataStore)?.Settings is { } settings
                ? GameWideConstants.TableSizeFor(settings.GameSystem)
                : (GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

        private static int? TeamOf(ITableState table, PlayerID player)
        {
            foreach (ITeam team in table.Teams.Objects)
                if (team.Players.Any(p => p == player)) return team.TeamNumber;
            return null;
        }
    }
}
