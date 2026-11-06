using FDG.Players;

namespace FDG.Missions
{
    /// <summary>One living unit on the table at the end of a round, as the mission cards see it.</summary>
    public sealed record BoardUnit(int Slot, IReadOnlyCollection<int> InZoneOfTeams, float CentreDistance);

    /// <summary>
    /// The table at the end of a round, reduced to what the 36 cards ask about. Plain data so the card
    /// rules can be tested without building a game.
    /// </summary>
    public sealed class MissionBoard
    {
        public int Round { get; init; }
        /// <summary>Slot -> team number.</summary>
        public Dictionary<int, int> TeamOf { get; init; } = new();
        /// <summary>Objective number (1-based) -> owner slot now, -1 for nobody.</summary>
        public Dictionary<int, int> OwnersNow { get; init; } = new();
        public List<BoardUnit> Units { get; init; } = new();

        public int ObjectiveCount => OwnersNow.Count;

        public bool SameSide(int a, int b) => TeamOf.TryGetValue(a, out int ta) && TeamOf.TryGetValue(b, out int tb) && ta == tb;
        public bool Enemies(int a, int b) => a >= 0 && b >= 0 && !SameSide(a, b);
        public int TeamOfSlot(int slot) => TeamOf.TryGetValue(slot, out int t) ? t : int.MinValue;
    }

    /// <summary>
    /// What each of the 36 cards asks, read against a <see cref="MissionBoard"/> and the events the game
    /// recorded in a <see cref="MissionCardState"/>.
    ///
    /// <para>Judgement calls, each in <c>claude/house-rules.md</c>:</para>
    /// <list type="bullet">
    ///   <item>"Destroy..." counts only what died THIS round (the cards are scored every round; Decapitate is
    ///   the one card that says "this or any previous round").</item>
    ///   <item>A unit or model that dies with nobody killing it - a rout, dangerous terrain, a transport's
    ///   wreck - counts as destroyed by its enemies. A routed unit lost a melee, so it counts as destroyed
    ///   in melee (In the Trenches); everything else killer-less counts only for the cards that do not say
    ///   how (Lay Them Low, the model cards).</item>
    ///   <item>"Control" is the side's: with allied teams, an objective a team-mate holds counts for you.</item>
    ///   <item>A unit is "within" an area when any of its models stands in it (the core rules' meaning).</item>
    ///   <item>A model killed by a spell counts as destroyed, not as destroyed by shooting.</item>
    /// </list>
    /// </summary>
    public static class MissionCardRules
    {
        /// <summary>
        /// The most victory points <paramref name="slot"/> has earned on <paramref name="card"/>, and which
        /// condition earned it (null when none is met).
        /// </summary>
        public static (int Vp, string? Condition) Evaluate(MissionCardDef card, int slot, int priorityKeySlot,
            MissionBoard board, MissionCardState state, EGameSystem system)
        {
            int best = 0;
            string? why = null;
            for (int i = 0; i < card.Conditions.Count; i++)
            {
                MissionCondition c = card.Conditions[i];
                if (c.Vp > best && Met(card.Number, i, slot, priorityKeySlot, board, state, system))
                {
                    best = c.Vp;
                    why = c.Text;
                }
            }
            return (best, why);
        }

        private static bool Controls(MissionBoard board, int slot, int objective) =>
            board.OwnersNow.TryGetValue(objective, out int owner) && owner >= 0 && board.SameSide(owner, slot);

        private static bool ControlledAtStart(MissionCardState state, MissionBoard board, int slot, int objective) =>
            state.OwnersAtRoundStart.TryGetValue(objective, out int owner) && owner >= 0 && board.SameSide(owner, slot);

        private static bool EnemyHeldAtStart(MissionCardState state, MissionBoard board, int slot, int objective) =>
            state.OwnersAtRoundStart.TryGetValue(objective, out int owner) && board.Enemies(owner, slot);

        private static int UnitsKilled(MissionCardState state, MissionBoard board, int slot, Func<MissionKill, bool> how) =>
            state.Kills.Count(k => k.Round == board.Round && board.Enemies(k.VictimSlot, slot)
                && (k.KillerSlot < 0 || board.SameSide(k.KillerSlot, slot)) && how(k));

        private static int ModelsKilled(MissionCardState state, MissionBoard board, int slot, Func<MissionModelDeath, bool> what) =>
            state.ModelDeaths.Count(d => d.Round == board.Round && board.Enemies(d.VictimSlot, slot) && what(d));

        private static bool Met(int number, int condition, int slot, int priorityKeySlot, MissionBoard board,
            MissionCardState state, EGameSystem system)
        {
            int round = board.Round;
            int myTeam = board.TeamOfSlot(slot);
            switch (number)
            {
                case >= 11 and <= 16: return Controls(board, slot, number - 10);
                case >= 21 and <= 26: return Controls(board, slot, number - 20);
                case >= 31 and <= 36:
                    return ControlledAtStart(state, board, slot, number - 30) && Controls(board, slot, number - 30);

                case 41: // Move Out
                    return !board.Units.Any(u => board.SameSide(u.Slot, slot) && u.InZoneOfTeams.Contains(myTeam));
                case 42: // Infiltrate
                {
                    int n = board.Units.Count(u => board.SameSide(u.Slot, slot)
                        && u.InZoneOfTeams.Any(t => t != myTeam && board.TeamOf.Values.Contains(t)));
                    return condition == 0 ? n >= 1 : n >= 3;
                }
                case 43: // Keep at Bay
                    if (round <= 1) return false;
                    return board.Units.Count(u => board.SameSide(u.Slot, slot) && u.InZoneOfTeams.Contains(myTeam)) >= 3
                        && !board.Units.Any(u => board.Enemies(u.Slot, slot) && u.InZoneOfTeams.Contains(myTeam));
                case 44: // Changing Priority
                {
                    if (!state.Priority.TryGetValue(MissionCardState.PriorityKey(priorityKeySlot, 44), out int obj)) return false;
                    if (!Controls(board, slot, obj)) return false;
                    return condition == 0 || EnemyHeldAtStart(state, board, slot, obj);
                }
                case 45: return board.OwnersNow.Keys.Count(o => Controls(board, slot, o)) >= 3;
                case 46: return board.ObjectiveCount > 0 && board.OwnersNow.Keys.All(o => Controls(board, slot, o));

                case 51: // Fire at Will
                {
                    int n = UnitsKilled(state, board, slot, k => k.Cause == "shooting");
                    return condition == 0 ? n >= 1 : n >= 3;
                }
                case 52: // In the Trenches
                {
                    int n = UnitsKilled(state, board, slot, k => k.Cause == "melee");
                    return condition == 0 ? n >= 1 : n >= 3;
                }
                case 53: // Lay Them Low
                {
                    int n = UnitsKilled(state, board, slot, _ => true);
                    return n >= (condition switch { 0 => 1, 1 => 3, _ => 6 });
                }
                case 54: // Outmaneuver
                {
                    float r = condition == 0 ? 6f : 12f;
                    return !board.Units.Any(u => board.Enemies(u.Slot, slot) && u.CentreDistance <= r);
                }
                case 55: // Intimidate
                {
                    int n = state.MoraleFails.Count(f => f[0] == round && board.Enemies(f[1], slot));
                    return condition == 0 ? n >= 1 : n >= 3;
                }
                case 56: // Attunement
                {
                    int n = state.SpellsCast.Count(s => s[0] == round && board.SameSide(s[1], slot));
                    return condition == 0 ? n >= 1 : n >= 3;
                }
                case 61: // Decapitate
                    return state.TopHeroes.Any(kv => board.Enemies(kv.Key, slot) && kv.Value.Count > 0
                        && state.ModelDeaths.Any(d => d.VictimSlot == kv.Key && kv.Value.Contains(d.ModelId)));
                case 62: return ModelsKilled(state, board, slot, d => d.Caster) >= 1;
                case 63: return UnitsKilled(state, board, slot, k => k.Flying) >= 1;
                case 64: // Assassinate
                {
                    int n = ModelsKilled(state, board, slot, d => d.Hero);
                    return condition == 0 ? n >= 1 : n >= MissionCardCatalog.AssassinateMany(system);
                }
                case 65: // Giant Slayer
                {
                    int need = condition == 0 ? MissionCardCatalog.GiantSlayerTough(system) : MissionCardCatalog.GiantSlayerBigTough;
                    return ModelsKilled(state, board, slot, d => d.Tough >= need) >= 1;
                }
                case 66: // Show No Mercy
                {
                    int need = condition == 0 ? 3 : 6;
                    return state.MeleeWins.Any(w => w[0] == round && board.SameSide(w[1], slot) && w[2] >= need);
                }
            }
            return false;
        }
    }
}
