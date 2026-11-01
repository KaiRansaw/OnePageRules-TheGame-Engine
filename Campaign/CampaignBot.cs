using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.SaveLoad;

namespace FDG.Campaign
{
    /// <summary>
    /// #428 - the bot's half of the campaign loop: the steps a human does on the campaign screen
    /// between missions (resolve the choices waiting on its sheets, pick what to bring, spend the
    /// treasury) done automatically for an army nobody is sitting in front of.
    ///
    /// <para>
    /// <b>Why this has to exist for the campaign to be a campaign.</b> Without it the enemy is a fresh
    /// army list every mission: it never levels, never recruits, never spends, and never remembers. You
    /// farm an opponent who cannot improve, which makes your own progress meaningless after about the
    /// third mission. A bot campaign army with its own treasury is the arc.
    /// </para>
    ///
    /// <para>
    /// <b>How hard it plays, and why.</b> Kai was asked and said "Unsure", so this is the engine's call:
    /// <b>the bot plays the book straight and the book's own rubber band does the balancing.</b> It gets
    /// the same treasury rewards from the same table, spends them on the same prices, and takes the same
    /// casualty rolls. No difficulty multiplier, no free points, no hidden handicap either way.
    /// </para>
    /// <para>
    /// That is not a cop-out: the rubber band is real and it is deliberate. The books pay the LOSER of a
    /// mission roughly twice what the winner gets (Grimdark Future: 300 against 150), and cap every
    /// mission army at <see cref="CampaignNumbers.ArmyCapOverLowest"/> over the smallest army present.
    /// So whoever is behind banks faster, and whoever is ahead cannot bring all of it. A campaign between
    /// two armies played straight self-corrects, which is a better opponent than any curve invented here.
    /// </para>
    /// <para>
    /// The two places it does not simply copy a human: it <b>spends rather than hoards</b> (a bot sitting
    /// on an unspent 2,000 points is the no-arc failure in a different costume), and it <b>will not build
    /// past what it could ever bring</b> - see <see cref="RosterCeiling"/>. Both are bounds on its own
    /// behaviour, not advantages.
    /// </para>
    /// </summary>
    public static class CampaignBot
    {
        /// <summary>
        /// The most the bot will let its roster be worth: the biggest rival army plus the mission cap,
        /// which is the most it could ever legally take to a table. Past that it would be buying units
        /// to leave at home, which looks like growth on the sheet and changes nothing in play - and over
        /// a long campaign it would also make its army file silly.
        /// </summary>
        public static int RosterCeiling(int rivalValue, CampaignNumbers n) =>
            rivalValue + n.ArmyCapOverLowest;

        /// <summary>
        /// Everything the bot does between missions, in the order the book's steps run: the choices
        /// waiting on its sheets first (they are free and they change what its units cost), then the
        /// treasury.
        /// </summary>
        /// <param name="bot">The bot's campaign army. Not a bot's army, nothing happens.</param>
        /// <param name="book">
        /// The book it recruits from, or null for a hand-written army with nothing to buy - in which case
        /// it still resolves its choices and still banks its points, it simply cannot spend them.
        /// </param>
        /// <param name="rivalValue">The biggest rival campaign army's value, for the roster ceiling.</param>
        /// <returns>What it did, one line each, for the campaign history.</returns>
        public static List<string> BetweenMissions(ArmyListFile bot, BookFile? book, CampaignNumbers n,
                                                   int rivalValue, Func<int> d6)
        {
            var log = new List<string>();
            if (bot.StrikeTeam is not { IsBot: true }) return log;

            log.AddRange(ResolveChoices(bot, d6));
            if (book != null) log.AddRange(Spend(bot, book, n, rivalValue, d6));
            return log;
        }

        /// <summary>
        /// Takes every choice sitting on the bot's sheets. A human picks these on the campaign screen;
        /// nobody is going to pick them for the enemy, and a unit with an unresolved choice is a unit
        /// that silently stopped levelling.
        /// <para>
        /// It takes the FIRST option every time rather than rolling. The options are a short list of
        /// traits, and a bot that rolls its picks is no more interesting to play against than one that
        /// takes them in order - while a deterministic pick means a campaign replays the same way from
        /// the same save, which is worth more.
        /// </para>
        /// </summary>
        public static List<string> ResolveChoices(ArmyListFile bot, Func<int> d6)
        {
            var log = new List<string>();
            foreach (UnitFileEntry unit in bot.Units.ToList())
            {
                // Choosing can add another pending entry (a skill set pick rolls its skill, which can
                // never cascade, but Fast Learner into Specialist can), so drain rather than iterate.
                for (int guard = 0; guard < 20; guard++)
                {
                    if (CampaignRules.Sheet(unit) is not { } sheet) break;
                    string? pending = sheet.Pending.FirstOrDefault();
                    if (pending == null) break;

                    List<string> options = CampaignRules.Options(sheet, pending);
                    if (options.Count == 0)
                    {
                        // Nothing left to pick - drop it rather than leaving it to block for ever.
                        sheet.Pending.Remove(pending);
                        CampaignRules.Save(unit, sheet);
                        continue;
                    }
                    log.Add(CampaignRules.Choose(unit, pending, options[0], d6));
                }
            }
            return log;
        }

        /// <summary>
        /// Steps 5 and 6 (Upgrade Armies, Recruit Units) for the bot, in the order a player would care
        /// about:
        /// <list type="number">
        ///   <item>a Hero if it has none and the book sells one it can afford - a hero army is what the
        ///   campaign rules are written around, and an enemy with no hero never rolls on the injury or
        ///   talent tables, so half the campaign's drama never happens to it;</item>
        ///   <item>then regular units, cheapest first, while the treasury holds and the roster stays
        ///   under <see cref="RosterCeiling"/>.</item>
        /// </list>
        /// <para>
        /// Cheapest-first on purpose: it spends its treasury down rather than saving for one expensive
        /// thing it may never afford, and it keeps bringing bodies, which is what makes an enemy army
        /// feel like it recovered from last mission.
        /// </para>
        /// <para>
        /// It recruits at most one Hero per call, which is the book's own "one new Hero per mission".
        /// </para>
        /// </summary>
        public static List<string> Spend(ArmyListFile bot, BookFile book, CampaignNumbers n,
                                         int rivalValue, Func<int> d6)
        {
            var log = new List<string>();
            if (bot.StrikeTeam is not { } info) return log;

            int ceiling = RosterCeiling(rivalValue, n);
            bool heroRecruited = false;

            // A Hero first, if it has none at all.
            if (CampaignEconomy.HeroCount(bot) == 0
                && CheapestAffordable(bot, book, n, info, ceiling, heroes: true) is { } hero)
            {
                CampaignPurchase bought = CampaignEconomy.Recruit(bot, book, Wanted(hero), n,
                    veteran: false, heroAlreadyRecruitedThisMission: false, d6: d6);
                if (bought.Ok) { log.Add(bought.Message); heroRecruited = true; }
            }

            // Then bodies, cheapest first, until the treasury or the ceiling stops it.
            for (int guard = 0; guard < 50; guard++)
            {
                RosterUnit? next = CheapestAffordable(bot, book, n, info, ceiling, heroes: false);
                if (next == null) break;
                CampaignPurchase bought = CampaignEconomy.Recruit(bot, book, Wanted(next), n,
                    veteran: false, heroAlreadyRecruitedThisMission: heroRecruited, d6: d6);
                if (!bought.Ok) break;
                log.Add(bought.Message);
            }

            if (log.Count == 0 && info.BankedPoints > 0)
                log.Add($"{info.Name} banked its {info.BankedPoints} pts: nothing it can afford would fit "
                        + "under the mission cap.");
            return log;
        }

        /// <summary>
        /// Which units the bot takes to the next mission: the most experienced first, then the most
        /// expensive, while they fit under <paramref name="cap"/>.
        /// <para>
        /// Veterans first is the point. A bot that filled its cap with whatever came first in the file
        /// would leave its levelled units at home, and the levelling would never be felt across the
        /// table - which is the whole reason the bot has a campaign sheet at all. Recovering units (No
        /// Permadeath) are never picked, and at least one unit always comes even if it is over the cap,
        /// because a mission with no enemy is worse than a mission with an oversized one.
        /// </para>
        /// </summary>
        public static List<string> PickWhatToBring(ArmyListFile bot, int cap)
        {
            var bringing = new List<string>();
            int total = 0;

            var available = bot.Units
                .Where(CampaignRules.CanBringToMission)
                .Select(u => (Unit: u, Sheet: CampaignRules.Sheet(u)))
                .Where(x => x.Sheet != null)
                .OrderByDescending(x => x.Sheet!.Xp)
                .ThenByDescending(x => x.Unit.PointCost)
                .ThenBy(x => x.Unit.Name, StringComparer.Ordinal)
                .ToList();

            foreach ((UnitFileEntry unit, StrikeUnit? sheet) in available)
            {
                if (bringing.Count > 0 && total + unit.PointCost > cap) continue;
                bringing.Add(sheet!.Id);
                total += unit.PointCost;
            }

            if (bot.StrikeTeam is { } info) info.Bringing = bringing;
            return bringing;
        }

        /// <summary>
        /// The bot's own special objective for the next mission, rolled the same way the player's is.
        /// Worth the same bonus into its treasury, so the two armies are earning on the same terms.
        /// </summary>
        public static string RollSpecialObjective(ArmyListFile bot, Func<int> d6)
        {
            bool caster = bot.Units.Any(u => u.SpecialRules.Any(r =>
                r.PrintableName.Contains("Caster", StringComparison.OrdinalIgnoreCase)));
            string objective = CampaignPreGame.RollSpecialObjective(
                new CampaignEntrant(bot.StrikeTeam?.Name ?? bot.Name, bot.TotalPoints, caster), d6);
            if (bot.StrikeTeam is { } info) info.SpecialObjective = objective;
            return objective;
        }

        // -- Picking from the book -----------------------------------------------------------------

        private static BuilderUnit Wanted(RosterUnit roster) =>
            new() { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };

        /// <summary>
        /// The cheapest roster entry the bot can both afford and fit under the ceiling, or null. Asks
        /// <see cref="CampaignEconomy.CannotRecruit"/> rather than comparing points itself, so the
        /// book's own hero cap and compile failures are honoured exactly as they are for a player.
        /// </summary>
        private static RosterUnit? CheapestAffordable(ArmyListFile bot, BookFile book, CampaignNumbers n,
                                                      StrikeTeamInfo info, int ceiling, bool heroes)
        {
            int room = ceiling - bot.TotalPoints;
            if (room <= 0) return null;

            foreach (RosterUnit roster in book.Units.OrderBy(r => r.BasePointCost).ThenBy(r => r.Name, StringComparer.Ordinal))
            {
                if (IsHeroEntry(roster) != heroes) continue;
                if (roster.BasePointCost > info.BankedPoints || roster.BasePointCost > room) continue;
                if (CampaignEconomy.CannotRecruit(bot, book, Wanted(roster), false) != null) continue;
                return roster;
            }
            return null;
        }

        /// <summary>
        /// Whether a roster entry is a Hero. Reads the catalog's hero rule names rather than the literal
        /// string "Hero", so a named character (#414's "Unique Hero", which the book defines as counting
        /// as having Hero) is recruited as one and counts against the three-hero cap.
        /// </summary>
        private static bool IsHeroEntry(RosterUnit roster) =>
            roster.Rules.Any(r => r is SpecialRuleEntry_Core core
                                  && Rules.Dispatch.CoreRuleCatalog.IsHeroRuleName(core.Name));
    }
}
