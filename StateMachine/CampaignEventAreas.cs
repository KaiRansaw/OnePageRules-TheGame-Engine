using System.Runtime.CompilerServices;
using FDG.Campaign;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// The four campaign random events whose effect depends on WHERE a unit is (3.6.0 campaign books, all
    /// four systems; deferred by #432 and built 2026-11-06):
    /// <list type="bullet">
    /// <item><b>36 Psychic Pool</b> - "whilst any unit is within 3" of one random objective marker, it gets
    /// +1 to Defense rolls."</item>
    /// <item><b>44 Psychic Fog</b> - "any unit that activates whilst fully within a random table quarter,
    /// moves +1" on Advance and +2" on Rush/Charge actions."</item>
    /// <item><b>45 Shifting Illusions</b> - "whilst any unit is fully within a random table quarter, it gets
    /// +1 to Morale Test rolls."</item>
    /// <item><b>46 Support Robots</b> - "whilst any unit is fully within a random table quarter, it gets +1
    /// to Defense rolls."</item>
    /// </list>
    ///
    /// <para>
    /// "Whilst" is checked at the moment it matters, not once at the top of the round: just before a
    /// unit rolls Defense, just before it takes a Morale Test, and (Psychic Fog, which says "activates")
    /// as it activates. A grant handed out at round start would be wrong both ways - a unit that walks in
    /// later would not get it, and one that walks out would keep it.
    /// </para>
    ///
    /// <para>
    /// The areas live with the game for the round they were rolled in and are dropped at its end. They are
    /// host-side state, like every rule the engine runs; the event's own announcement tells every player
    /// where the area is. Not written into a save: a game saved and reloaded mid-round loses that round's
    /// area (recorded in claude/fdg-todo.md).
    /// </para>
    /// </summary>
    public static class CampaignEventAreas
    {
        public enum EAreaEffect { DefenseBonus, MoraleBonus, MoveBonusOnActivation }

        /// <summary>One active area: a circle around a marker ("within") or a quarter ("fully within").</summary>
        public sealed record Area(CampaignEvent Event, EAreaEffect Effect, ZoneRect? Quarter,
            Position? MarkerCentre, float Radius)
        {
            public string Source => $"{Event.Name} (campaign event)";
        }

        private static readonly ConditionalWeakTable<object, List<Area>> Active = new();

        public static IReadOnlyList<Area> For(IReadableGameDataStore store) =>
            Active.TryGetValue(store, out List<Area>? areas) ? areas : (IReadOnlyList<Area>)Array.Empty<Area>();

        public static void Add(IReadableGameDataStore store, Area area) =>
            Active.GetValue(store, _ => new List<Area>()).Add(area);

        /// <summary>End of the round: every area is gone.</summary>
        public static int Clear(IReadableGameDataStore store)
        {
            if (!Active.TryGetValue(store, out List<Area>? areas)) return 0;
            int n = areas.Count;
            areas.Clear();
            return n;
        }

        /// <summary>Which of the four this event is, or null for any other.</summary>
        public static EAreaEffect? EffectOf(CampaignEvent fired) => fired switch
        {
            { Kind: ECampaignEventKind.RollModifier, Roll: ECampaignEventRoll.Defense,
              Scope: ECampaignEventScope.NearRandomObjective or ECampaignEventScope.RandomQuarter } => EAreaEffect.DefenseBonus,
            { Kind: ECampaignEventKind.RollModifier, Roll: ECampaignEventRoll.Morale,
              Scope: ECampaignEventScope.RandomQuarter } => EAreaEffect.MoraleBonus,
            { Kind: ECampaignEventKind.MovementChange, Scope: ECampaignEventScope.RandomQuarter } => EAreaEffect.MoveBonusOnActivation,
            _ => null,
        };

        // ── Who is inside ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// "Within 3 inches of the marker": the unit has a living model within 3" (the same measure the
        /// other objective events use). "Fully within a quarter": every living model's base lies inside it.
        /// A unit off the table (in reserve) is in no area.
        /// </summary>
        public static bool IsInside(IUnit unit, Area area)
        {
            if (ReserveRules.IsInReserve(unit)) return false;
            var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
            if (living.Count == 0) return false;

            if (area.MarkerCentre is Position centre)
                return living.Any(m => Position.GetDistance2D(m.Position, centre) <= area.Radius);

            ZoneRect q = area.Quarter!;
            return living.All(m =>
            {
                float r = m.BaseRadiusInches;
                return m.Position.x - r >= q.Left && m.Position.x + r <= q.Right
                    && m.Position.z - r >= q.Bottom && m.Position.z + r <= q.Top;
            });
        }

        // ── Applying ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Brings <paramref name="unit"/>'s area bonuses for <paramref name="roll"/> (Save or Morale) up to
        /// date with where it stands now: any earlier area bonus of that kind is taken off, and one is put on
        /// for each active area it is inside. Called by the save and morale stages just before they read
        /// granted modifiers, so the roll sees exactly the areas the unit is in at that moment.
        /// </summary>
        public static void RefreshRollBonus(IGameContext gameContext, IUnit unit, ERollKind roll)
        {
            EAreaEffect? wanted = roll switch
            {
                ERollKind.Save => EAreaEffect.DefenseBonus,
                ERollKind.Morale => EAreaEffect.MoraleBonus,
                _ => null,
            };
            if (wanted is null) return;
            IReadOnlyList<Area> areas = For(gameContext.GameDataStore);

            TokenType type = RollModifierTokens.TypeFor(roll);
            foreach (Token old in unit.Tokens.GetAllTokens(type).ToList())
                if (old.Payload is TokenPayload.StatModifier { Source: { } s } && IsAreaSource(s))
                    unit.Tokens.RemoveTokensWithPayload(type, old.OwnerUnitID, old.Payload, old.Count);

            foreach (Area area in areas)
            {
                if (area.Effect != wanted || !IsInside(unit, area)) continue;
                unit.Tokens.AddToken(new Token(type, 1, new TokenClearTrigger.RoundEnd(),
                    Payload: new TokenPayload.StatModifier(area.Event.Amount, area.Source)));
            }
        }

        /// <summary>Psychic Fog: a unit that activates fully inside the quarter moves faster for the round.</summary>
        public static void OnActivationStart(IGameContext gameContext, IUnit unit)
        {
            foreach (Area area in For(gameContext.GameDataStore))
            {
                if (area.Effect != EAreaEffect.MoveBonusOnActivation || !IsInside(unit, area)) continue;
                bool has = unit.Tokens.GetAllTokens(TokenType.RuleGrant).Any(t =>
                    t.Payload is TokenPayload.RuleGrant { RuleName: CampaignEventRuleCatalog.HastenedName });
                if (has) return;
                unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.RoundEnd(),
                    Payload: new TokenPayload.RuleGrant(CampaignEventRuleCatalog.HastenedName, ELifetime.ThisRound)));
                gameContext.Log($"{unit.Name} activates inside the {area.Event.Name} and moves +1\" on Advance, "
                    + "+2\" on Rush/Charge this round.");
                return;
            }
        }

        private static bool IsAreaSource(string source) => source.EndsWith(" (campaign event)", StringComparison.Ordinal)
            && Campaign.CampaignEvents.All.Any(e => EffectOf(e) != null && source.StartsWith(e.Name, StringComparison.Ordinal));
    }
}
