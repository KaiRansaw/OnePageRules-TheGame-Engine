using FDG.Rules.Foundation;

namespace FDG.Stages
{
    /// <summary>
    /// #468 (rules audit, 2026-11-19): in the Quest games the Frenzy and Growth rules collect markers only until the next
    /// wave - the Campaign Builders' wording (2.1.0, and Army Forge's for every Quest book): "... up to a max. of six
    /// markers. For every three markers ... All markers are removed once the next wave of reinforcements is deployed."
    /// The book data counts the markers; this removes them when a wave arrives. The other games' versions of these
    /// rules have no waves and keep their tokens to the end of the game, so this runs only from the Quest wave code.
    /// </summary>
    public static class QuestWaveMarkers
    {
        /// <summary>The marker each Quest Frenzy / Growth rule counts (the token ids the books use).</summary>
        public static readonly IReadOnlyList<string> Ids = new[]
        {
            "DefensiveFrenzyMarker", "DefensiveGrowthMarker", "FortifiedGrowthMarker",
            "PiercingFrenzyMarker", "PiercingGrowthMarker", "PrecisionFrenzyMarker", "PrecisionGrowthMarker",
        };

        /// <summary>A new wave of reinforcements has been deployed: every unit loses these markers.</summary>
        public static int Clear(IGameContext context)
        {
            int removed = Clear(context.GameDataStore);
            if (removed > 0) context.Log($"New wave: {removed} Frenzy / Growth marker(s) removed, as the Quest books say.");
            return removed;
        }

        /// <summary>The removal itself, on a store: how many markers went.</summary>
        public static int Clear(Data.IReadableGameDataStore store)
        {
            int removed = 0;
            foreach (UnitData unit in store.GetAllValues<UnitData>())
                foreach (string id in Ids)
                {
                    var type = new TokenType(id);
                    int had = unit.Tokens.GetTokenCount(type);
                    if (had == 0) continue;
                    unit.Tokens.RemoveTokens(type, had);
                    removed += had;
                }
            return removed;
        }
    }
}
