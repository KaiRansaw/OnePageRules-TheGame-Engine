using FDG.Campaign;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// Campaign effects during a game. Since the 3.6 move this is only kill counting: each campaign
    /// unit's sheet records what it destroyed, so XP can be worked out afterwards by
    /// <see cref="CampaignRules.AfterGame"/>.
    /// <para>
    /// It used to carry hand-written implementations of the v3.5.1 Firefight hero skills - auras for
    /// friendly units activating within 6" of the hero, Warden, Mastermind, Suppressor, Berserker,
    /// Chemist, Herbalist, Motivated and Crazed. Campaign Rules v3.6.0 reuses nine of those names for
    /// DIFFERENT effects, so leaving the old code in place would have silently applied the wrong rule to
    /// a hero that rolled the new one. The 3.6 versions are either ordinary rule definitions the rules
    /// engine applies on its own (<see cref="Rules.Dispatch.Campaign36RuleCatalog"/>) or once-per-game
    /// effects the engine has no offer for yet, which are markers there. Either way nothing needs a
    /// special case here. See WorkItems/410.
    /// </para>
    /// </summary>
    public static class FirefightCampaign
    {
        /// <summary>The unit has the rule, from its army file or granted during the game.</summary>
        public static bool Has(IUnit unit, string rule) => QuestChallenges.HasRule(unit, rule)
            || unit.Tokens.GetAllTokens(TokenType.RuleGrant).Select(t => t.Payload)
                .OfType<TokenPayload.RuleGrant>().Any(g => g.RuleName == rule);

        /// <summary>
        /// Counts a kill on the killer's campaign sheet (+1 XP per unit, +2 per Hero, after the game),
        /// and - when <paramref name="store"/> is given - stamps the first kill of the game for the
        /// campaign's Annihilator objective.
        /// </summary>
        public static void OnUnitDestroyed(IUnit dead, IUnit killer, IReadableGameDataStore? store = null)
        {
            if (killer is not UnitData data || StrikeUnit.FromJson(data.StrikeUnitJson) is not { } sheet) return;
            // #414: killing a named character is a Hero kill. It carries "Unique Hero" instead of "Hero",
            // so this scored it as an ordinary kill - 1 XP rather than 2. (In a live game the supplement's
            // addRule grant has usually landed by now and Has would find "Hero" anyway; checking both
            // names means the tally does not depend on that having fired.)
            if (Rules.Dispatch.CoreRuleCatalog.HeroRuleNames.Any(name => Has(dead, name)))
                sheet.HeroKills++;
            else sheet.Kills++;

            // #430 Annihilator: the first kill of the game, stamped on the unit that made it. "First" is
            // the whole question and a finished table cannot answer it, so it has to be recorded as it
            // happens. Claimed only if nobody else holds it - one scan of the table's units per kill,
            // which is nothing beside the attack that just resolved.
            if (store != null && !sheet.DrewFirstBlood && !AnyoneHasFirstBlood(store))
                sheet.DrewFirstBlood = true;

            data.StrikeUnitJson = sheet.ToJson();
        }

        /// <summary>Whether any unit on the table already claims the game's first kill.</summary>
        private static bool AnyoneHasFirstBlood(IReadableGameDataStore store)
        {
            if (!store.IsTypeAssigned<UnitData>()) return false;
            foreach (UnitData unit in store.GetAllValues<UnitData>())
                if (StrikeUnit.FromJson(unit.StrikeUnitJson) is { DrewFirstBlood: true }) return true;
            return false;
        }
    }
}
