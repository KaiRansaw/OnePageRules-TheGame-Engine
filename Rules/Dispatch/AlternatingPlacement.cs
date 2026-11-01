using FDG.Players;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// #420 - "<i>Players alternate in placing X units, starting with the player that activates next.</i>"
    ///
    /// <para>
    /// Three core rules carry that sentence word for word - <b>Scout</b>, <b>Ambush</b> and
    /// <b>Re-Deployment</b> - and the engine implemented it for one of the three. Scout's placement pass
    /// and Ambush's arrival pass both walked a list built army-by-army, so one player placed every unit
    /// they owned before the other placed any.
    /// </para>
    /// <para>
    /// <b>Why it matters at the table.</b> These are the two rules whose whole point is committing blind.
    /// A Scout screen or an Ambush arrival is a guess about where the enemy will be; alternating makes
    /// both players guess at the same time. Placing all of one side's first hands the other side perfect
    /// information for every one of theirs - with two Ambushers each, the second player sees the entire
    /// enemy arrival before committing a single unit. It is not a small edge, and it is invisible: the
    /// placements are all legal, the game plays on, and nothing is logged.
    /// </para>
    /// <para>
    /// Re-Deployment got it right because it is an interactive sub-phase - a round-robin of prompts, in
    /// <c>ReDeploymentStage</c>. These two are non-interactive sweeps over a collection, which is exactly
    /// why the clause went missing: there was no loop over players to notice it was absent.
    /// </para>
    /// <para>
    /// <b>"The player that activates next"</b> is the head of the deployment roll order -
    /// <c>MainPhaseContext</c> seeds its turn order from the same <c>FirstDeploymentRollOrder</c>, so the
    /// two agree by construction rather than by coincidence.
    /// </para>
    /// </summary>
    public static class AlternatingPlacement
    {
        /// <summary>
        /// Reorders <paramref name="items"/> so owners take turns, in <paramref name="playerOrder"/>,
        /// starting with the first player in that order.
        ///
        /// <para>
        /// Each player's own units keep their relative order, so whatever the caller's list meant by
        /// "first" within one army still holds - only the interleaving between players changes. A player
        /// with more units than the others simply takes the remaining turns once the others run out,
        /// which is what alternating means when the counts are uneven.
        /// </para>
        /// <para>
        /// Players not named in <paramref name="playerOrder"/> are appended after those that are, in
        /// first-seen order, rather than dropped: losing a unit from deployment because its owner was
        /// missing from a turn order would be far worse than placing it late. An empty
        /// <paramref name="playerOrder"/> therefore returns the input order unchanged.
        /// </para>
        /// </summary>
        public static List<T> Interleave<T>(IEnumerable<T> items, Func<T, PlayerID> ownerOf,
            IEnumerable<PlayerID> playerOrder)
        {
            List<T> all = items.ToList();
            if (all.Count <= 1) return all;

            // Queue per owner, preserving each owner's internal order.
            var byOwner = new Dictionary<PlayerID, Queue<T>>();
            var seenOrder = new List<PlayerID>();
            foreach (T item in all)
            {
                PlayerID owner = ownerOf(item);
                if (!byOwner.TryGetValue(owner, out Queue<T>? queue))
                {
                    queue = new Queue<T>();
                    byOwner[owner] = queue;
                    seenOrder.Add(owner);
                }

                queue.Enqueue(item);
            }

            if (byOwner.Count == 1) return all;

            // Turn order: the stated order first, then any owner it does not mention.
            var turnOrder = new List<PlayerID>();
            foreach (PlayerID player in playerOrder)
                if (byOwner.ContainsKey(player) && !turnOrder.Contains(player))
                    turnOrder.Add(player);
            foreach (PlayerID player in seenOrder)
                if (!turnOrder.Contains(player))
                    turnOrder.Add(player);

            var result = new List<T>(all.Count);
            while (result.Count < all.Count)
            {
                bool placedAny = false;
                foreach (PlayerID player in turnOrder)
                {
                    Queue<T> queue = byOwner[player];
                    if (queue.Count == 0) continue;
                    result.Add(queue.Dequeue());
                    placedAny = true;
                }

                // Cannot happen while result.Count < all.Count, but a malformed ownerOf that returned a
                // different id on a second call would otherwise spin forever. Fail safe by appending the
                // remainder rather than hanging deployment.
                if (!placedAny)
                {
                    foreach (Queue<T> queue in byOwner.Values)
                        while (queue.Count > 0) result.Add(queue.Dequeue());
                }
            }

            return result;
        }

        /// <summary>
        /// The players in activation order, flattened from the deployment roll order - the head
        /// activates first, which is what "the player that activates next" names.
        /// </summary>
        public static List<PlayerID> ActivationOrder(IEnumerable<ITeam> firstDeploymentRollOrder) =>
            firstDeploymentRollOrder.SelectMany(team => team.Players).ToList();
    }
}
