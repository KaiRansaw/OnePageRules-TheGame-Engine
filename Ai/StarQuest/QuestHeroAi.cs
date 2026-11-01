using FDG.Data;
using FDG.Stages;
using FDG.StateMachine;

namespace FDG.Ai.StarQuest
{
    /// <summary>What a bot-run hero is trying to do this activation.</summary>
    public enum EHeroAim
    {
        /// <summary>Carrying an objective marker: take it to a delivery or extraction point.</summary>
        Deliver,
        /// <summary>Stand on an objective marker so the Objective skill can work it.</summary>
        Objective,
        /// <summary>Walk onto a search token or treasure marker and pick it up for free.</summary>
        Token,
        /// <summary>Every objective is done: gather at one table corner and leave.</summary>
        Exfiltrate,
    }

    /// <summary>Where a hero is going and how close it needs to get.</summary>
    public sealed record QuestHeroPlan(EHeroAim Aim, Position Goal, float StopWithin, string Why);

    /// <summary>
    /// #419 - the hero side of the Quest AI.
    ///
    /// <para>
    /// <b>The gap this fills.</b> <see cref="StarQuestAiBrain"/> is the ENEMY's flowchart, and it says so:
    /// <c>UsesQuestAi</c> excludes heroes, so a bot-run party fell through to the generic solo bot. That bot
    /// plays a normal game of Grimdark Future - it walks at the enemy and at <see cref="IObjective"/>
    /// markers - and a Quest mission is not won that way. A mission is won by standing <b>within 1"</b> of a
    /// <see cref="QuestMarker"/> and taking the Objective skill, then leaving by a table corner; loot comes
    /// from standing within 1" of a search token. None of those three things is an <c>IObjective</c>, so the
    /// solo bot could not see any of them.
    /// </para>
    /// <para>
    /// The visible symptom was a party that walked to the middle of the table and milled about. #414 taught
    /// the bot to TAKE its free Skill action, which was the other half of the problem, but a Skill action
    /// taken 8" from the nearest marker just reports "No objective marker within 1"." So eight patches in a
    /// row have had hero-side branches that only fixtures ever reached, and a played mission scored 0 XP.
    /// </para>
    /// <para>
    /// <b>The priority order, which is a gameplay decision rather than a reading of the book.</b> The book
    /// has no AI for the heroes at all - they are the player's side - so this is a policy, picked to finish
    /// missions rather than to play them well:
    /// </para>
    /// <list type="number">
    /// <item><b>Deliver</b> what it is carrying. A carried marker is the objective, and a hero wandering off
    /// with one in its hands cannot be made to give it back.</item>
    /// <item><b>Leave</b>, once the primary and secondary are both done - or once the primary is banked and
    /// a hero is down, which is cutting losses (<see cref="PartyInTrouble"/>). Above both working an
    /// objective and looting, deliberately: see the notes in <see cref="Plan"/>, each of which is there
    /// because the first version had the order wrong and a played mission showed it.</item>
    /// <item><b>Work an objective marker</b> for whichever objective is live.</item>
    /// <item><b>Pick up a search token</b>. Free, and it is where items and money come from.</item>
    /// <item>Otherwise no opinion (<see cref="Plan"/> returns null) and the solo bot fights, which is the
    /// right answer when there is nothing on the table to work.</item>
    /// </list>
    /// <para>
    /// Deliberately NOT in this version, and recorded rather than quietly dropped: the party does not spread
    /// out over several markers (every hero walks to the nearest one, which for objectives is often correct
    /// and for tokens is wasteful); and it has no notion of holding Horde's objective markers at the end of
    /// a wave for the 2 XP each. Tokens it does pursue in Horde, which is new - the bots never once walked
    /// onto one before.
    /// </para>
    /// <para>
    /// Reads nothing but <see cref="ITableState"/>, because that is all a resolver is given.
    /// </para>
    /// </summary>
    public static class QuestHeroAi
    {
        /// <summary>The Objective skill works a marker "within 1"", and a token within 1" is picked up free.</summary>
        public const float WorkReachInches = 1f;

        /// <summary>A carried marker is delivered within 3" of a delivery or extraction point.</summary>
        public const float DeliverReachInches = 3f;

        /// <summary>
        /// How far a hero will go out of its way for a search token: one Advance, near enough.
        ///
        /// <para>
        /// This cap is the difference between a detour and an errand, and leaving it off broke Horde Mode.
        /// With tokens chased at any range the party stopped fighting: it crossed the table for loot, never
        /// closed with anything, and so neither killed enemies nor died. Enemies then accumulated wave on
        /// wave - Horde adds one every time alertness fills - until a game that used to finish in under a
        /// second had thirty-odd units planning moves every round and could not finish in ten minutes. The
        /// heroes were not stuck and the game was not looping; it was simply a different, much longer game.
        /// </para>
        /// <para>
        /// A short cap keeps what is worth having (a token underfoot is free, and the bots never once picked
        /// one up before) without turning a Horde party into a looting expedition. Missions are unaffected:
        /// objective markers already outrank tokens there, and the exit outranks both.
        /// </para>
        /// </summary>
        public const float TokenDetourInches = 6f;

        /// <summary>
        /// <see cref="FDG.Stages.QuestMission.HeroesAtOneCorner"/> wants every hero's whole base within 6" of
        /// the SAME corner, so the walk aims well inside that - a hero stopped at 5.9" with a 0.5" base has
        /// not arrived, and one hero short means nobody leaves.
        /// </summary>
        public const float ExitReachInches = 4f;

        public static bool IsHero(IUnit unit) => unit is UnitData { IsQuestHero: true };

        /// <summary>Where this hero should go, or null to leave it to the solo bot (which fights).</summary>
        public static QuestHeroPlan? Plan(ITableState table, IUnit hero)
        {
            if (!IsHero(hero) || !hero.Models.Any(m => m.GetIsAlive())) return null;
            QuestProgress? quest = table.Progress.Quest;
            if (quest == null) return null;

            // 1. Carrying an objective marker - take it where it goes. Checked first because a hero holding
            // the objective IS the objective, and a carry mission cannot complete any other way.
            //
            // There are FOUR destinations, not one, and they are not all markers on the table. The first
            // version of this only knew about the two that are, so a hero that picked up a Capture marker
            // had no plan at all, fell back to the solo bot, and wandered off to loot while holding the
            // objective. Four of eight played missions drew Capture or Escort as their secondary and that is
            // where every one of them stalled - the party reached the marker, picked it up, and lost
            // interest. Mirrors QuestMission.OnHeroActivationEnd, which is what actually scores it.
            string me = hero.ID.ID.ToString();
            if (quest.Markers.FirstOrDefault(m => m.CarriedBy == me) is { } carried)
            {
                int objective = carried.ForSecondary ? quest.Secondary : quest.Primary;

                // Delivery (primary 6) and Escort (secondary 6) drop it at a marker, within 3".
                if (objective == 6)
                {
                    string kind = carried.ForSecondary ? "extraction" : "delivery";
                    if (Nearest(table, hero, quest.Markers.Where(m => m.Kind == kind)) is { } drop)
                        return new QuestHeroPlan(EHeroAim.Deliver, QuestMission.Where(table, drop),
                            DeliverReachInches - 1f, $"carry it to the {kind} point");
                }

                // Retrieval (primary 3) wants the heroes' OWN corner; Capture (secondary 3) accepts any, so
                // the nearest is the shortest walk. Both measure the whole base within 6".
                if (objective == 3)
                {
                    int corner = carried.ForSecondary
                        ? NearestCorner(hero)
                        : quest.HeroCorner >= 1 && quest.HeroCorner <= 4 ? quest.HeroCorner : NearestCorner(hero);
                    return new QuestHeroPlan(EHeroAim.Deliver, StarQuestMissionRules.Corner(corner),
                        ExitReachInches, $"carry it to corner {corner}");
                }
            }

            // 2. Leave. Either every objective is done, or the primary is banked and the party is coming
            // apart (see PartyInTrouble). Ranked ABOVE working an objective on purpose: when everything is
            // done Complete() has already removed the markers so the order would not matter, but the
            // cut-losses case is exactly a live secondary marker sitting there tempting a party that cannot
            // afford it. The first version had this below, and the bail-out could never fire.
            if (quest.Difficulty != 0 && quest.PrimaryDone
                && ((quest.Secondary > 0 && quest.SecondaryDone) || PartyInTrouble(table)))
            {
                int corner = ExitCorner(table, quest);
                string why = quest.SecondaryDone ? "every objective is done" : "cut losses with the primary banked";
                return new QuestHeroPlan(EHeroAim.Exfiltrate, StarQuestMissionRules.Corner(corner),
                    ExitReachInches, $"leave by corner {corner} - {why}");
            }

            // 3. An objective marker for whichever objective is live. Mirrors QuestMission.AiGoal's choice of
            // which set is in play, and QuestMission.Workable's filter for which markers are still worth
            // standing on - a seized one that is already activated is done, and one in someone's hands is
            // being carried by a hero that has its own plan.
            bool secondaryLive = quest.PrimaryDone && quest.Secondary > 0 && !quest.SecondaryDone;
            var live = quest.Markers.Where(m => m.Kind == "objective"
                && m.ForSecondary == secondaryLive
                && string.IsNullOrEmpty(m.CarriedBy)
                && !(m.Action == "seize" && m.Activated));
            if (Nearest(table, hero, live) is { } marker)
                return new QuestHeroPlan(EHeroAim.Objective, QuestMission.Where(table, marker),
                    WorkReachInches, $"work objective marker {marker.Id} ({marker.Action})");


            // 4. A search token or a treasure marker, but only one close enough to be a DETOUR rather than an
            // errand - see TokenDetourInches, which is the whole lesson of this branch.
            QuestToken? token = quest.Tokens
                .Select(t => (Token: t, Gap: QuestMission.DistanceToPoint(hero, new Position(t.X, t.Z))))
                .Where(t => t.Gap <= TokenDetourInches)
                .OrderBy(t => t.Gap)
                .Select(t => t.Token)
                .FirstOrDefault();
            if (token != null)
                return new QuestHeroPlan(EHeroAim.Token, new Position(token.X, token.Z),
                    WorkReachInches, $"pick up {token.Label}");

            return null;
        }

        /// <summary>
        /// The corner the whole party heads for. Every hero must pick the SAME one or nobody leaves, and the
        /// heroes' own deployment corner is the one answer every hero computes identically without needing to
        /// agree on anything. Falls back to the corner nearest the party's centre of mass.
        /// </summary>
        public static int ExitCorner(ITableState table, QuestProgress quest)
        {
            if (quest.HeroCorner >= 1 && quest.HeroCorner <= 4) return quest.HeroCorner;

            var models = table.Units.Objects.Where(IsHero).SelectMany(u => u.Models)
                .Where(m => m.GetIsAlive()).ToList();
            if (models.Count == 0) return 1;
            var centre = new Position(models.Average(m => m.Position.x), models.Average(m => m.Position.z));
            return StarQuestMissionRules.CornerOf(centre);
        }

        /// <summary>
        /// Time to cut losses: a hero is down. Measured rather than guessed - across eight played missions
        /// the party completed the primary seven times and then died six of those, to between two and nine
        /// waves of reinforcements, because alertness climbs +3 a round once the primary is done and sends a
        /// wave every other round for as long as the party stays. A mission ends the moment the heroes
        /// gather in a corner with the primary banked, so staying for the secondary with a hero already down
        /// trades a finished mission for a wipe - and a wipe pays nothing at all.
        /// <para>
        /// An unconscious hero still has its models on the table (that is how Revive finds it), so "down" is
        /// a hero with no LIVING models rather than one that has been removed.
        /// </para>
        /// </summary>
        public static bool PartyInTrouble(ITableState table)
        {
            var heroes = table.Units.Objects.Where(IsHero).ToList();
            return heroes.Count > 0 && heroes.Any(h => !h.Models.Any(m => m.GetIsAlive()));
        }

        /// <summary>The table corner this hero is nearest to - the shortest walk for a Capture marker.</summary>
        public static int NearestCorner(IUnit hero)
        {
            IModel? model = hero.Models.FirstOrDefault(m => m.GetIsAlive());
            return model == null ? 1 : StarQuestMissionRules.CornerOf(model.Position);
        }

        /// <summary>True when the hero is close enough to act - no point moving.</summary>
        public static bool InReach(IUnit hero, QuestHeroPlan plan) =>
            QuestMission.DistanceToPoint(hero, plan.Goal) <= plan.StopWithin;

        private static QuestMarker? Nearest(ITableState table, IUnit hero, IEnumerable<QuestMarker> markers) =>
            markers.OrderBy(m => QuestMission.DistanceToPoint(hero, QuestMission.Where(table, m)))
                .FirstOrDefault();
    }
}
