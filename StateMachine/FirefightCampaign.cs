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
        public static void OnUnitDestroyed(IUnit dead, IUnit killer, IReadableGameDataStore? store = null) =>
            OnUnitDestroyed(dead, killer, store, out _);

        /// <summary>
        /// <see cref="OnUnitDestroyed(IUnit, IUnit, IReadableGameDataStore?)"/>, also saying what happened to the
        /// homebrew wanted posters (house rule B29) so the caller can announce it.
        /// </summary>
        public static void OnUnitDestroyed(IUnit dead, IUnit killer, IReadableGameDataStore? store, out WantedNews news)
        {
            news = Posters(dead, killer);
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

        /// <summary>What a death did to the wanted posters: a bounty claimed and/or a poster put up (or raised).</summary>
        public readonly record struct WantedNews(string? Claimed, int ClaimedKills, string? Posted, int PostedKills);

        /// <summary>
        /// A campaign unit routed from a melee the other side won (homebrew wanted posters, house rule B29). The books
        /// leave open whether that is "destroyed by" the winner, so it earns no XP and no kill - but for the posters it
        /// counts, the same reading the Mission Cards use (a rout is a unit destroyed in melee). It also names the winner
        /// on the memorial.
        /// </summary>
        public static WantedNews OnUnitRouted(IUnit dead, IUnit meleeWinner) => Posters(dead, meleeWinner);

        /// <summary>The memorial's FellTo, the bounty claim on the dead unit and the poster on its killer.</summary>
        private static WantedNews Posters(IUnit dead, IUnit killer)
        {
            string? claimed = null, posted = null;
            int claimedKills = 0, postedKills = 0;
            StrikeUnit? killerSheet = killer is UnitData k ? StrikeUnit.FromJson(k.StrikeUnitJson) : null;
            if (dead is UnitData deadData && StrikeUnit.FromJson(deadData.StrikeUnitJson) is { } deadSheet)
            {
                // Homebrew memorial (house rule B26): who destroyed a campaign unit, for its line on the memorial.
                deadSheet.FellTo = killer is UnitData kk ? CampaignVeterans.Title(kk.Name, killerSheet?.Nickname) : "";
                // Homebrew wanted posters (house rule B29): a WANTED unit destroyed by the other army is claimed.
                int was = deadSheet.WantedKills;
                if (CampaignWanted.Claim(deadSheet, killerSheet))
                {
                    claimed = CampaignVeterans.Title(deadData.Name, deadSheet.Nickname);
                    claimedKills = was;
                }
                deadData.StrikeUnitJson = deadSheet.ToJson();
                // ...and destroying one of the other army's units puts a poster up on the killer, live.
                if (killer is UnitData kd && killerSheet != null
                    && CampaignWanted.Post(killerSheet, deadSheet, CampaignVeterans.Title(deadData.Name, deadSheet.Nickname)))
                {
                    kd.StrikeUnitJson = killerSheet.ToJson();
                    posted = CampaignVeterans.Title(kd.Name, killerSheet.Nickname);
                    postedKills = killerSheet.WantedKills;
                }
            }
            return new WantedNews(claimed, claimedKills, posted, postedKills);
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
