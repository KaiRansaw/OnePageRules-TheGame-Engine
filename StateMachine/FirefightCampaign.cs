using FDG.Campaign;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;

namespace FDG.Stages
{
    /// <summary>
    /// GF: Firefight Campaign effects during a game: kills are counted on each strike team unit's sheet (for XP
    /// afterwards), and the hero skills and talents whose effect isn't a plain rule are applied here - auras for
    /// friendly units activating within 6" of the hero, Warden, Mastermind, Suppressor and Crazed.
    /// (Berserker, Chemist and Herbalist change the wound effect roll in <see cref="FirefightRules.ApplyWounds"/>;
    /// Motivated is in <c>MoraleUtilities.TakeMoraleTest</c>.)
    /// </summary>
    public static class FirefightCampaign
    {
        public const float AuraRange = 6f;
        public static readonly TokenType MotivatedUsed = new("CampaignMotivatedUsed");

        /// <summary>The unit has the rule, from its army file or granted during the game.</summary>
        public static bool Has(IUnit unit, string rule) => QuestChallenges.HasRule(unit, rule)
            || unit.Tokens.GetAllTokens(TokenType.RuleGrant).Select(t => t.Payload)
                .OfType<TokenPayload.RuleGrant>().Any(g => g.RuleName == rule);

        private static bool Allied(IGameContext c, PlayerID a, PlayerID b) =>
            a.Equals(b) || ITeamExtensions.AreAllied(c.TableState.Teams.Objects, a, b);

        private static float Distance(IUnit a, IUnit b) =>
            UnitCompareUtilities.MinDistanceBetweenUnits(a, b, out _, out _, includeVertical: true);

        /// <summary>Living units on <paramref name="unit"/>'s side (or the other side) with this rule within 6".</summary>
        public static IEnumerable<IUnit> Near(IGameContext c, IUnit unit, string rule, bool friendly) =>
            c.TableState.Units.Objects.Where(u => u.GetIsAlive() && Has(u, rule)
                && Allied(c, u.PlayerID, unit.PlayerID) == friendly && Distance(u, unit) <= AuraRange);

        /// <summary>Counts a kill on the killer's strike team sheet (+1 XP per unit, +2 per hero, after the game).</summary>
        public static void OnUnitDestroyed(IUnit dead, IUnit killer)
        {
            if (killer is not UnitData data || StrikeUnit.FromJson(data.StrikeUnitJson) is not { } sheet) return;
            if (Has(dead, "Hero")) sheet.HeroKills++; else sheet.Kills++;
            data.StrikeUnitJson = sheet.ToJson();
        }

        /// <summary>A unit activates: the auras of friendly Captain / Support heroes within 6", and Warden.</summary>
        public static async Task OnActivated(IGameContext c, IUnit unit)
        {
            var given = new List<string>();
            void Give(string skill, string rule, ELifetime lifetime, string text)
            {
                if (!Near(c, unit, CampaignRuleCatalog.SkillPrefix + skill, friendly: true).Any()) return;
                QuestTreasure.GrantRule(c, unit, unit, rule, lifetime);
                given.Add(text);
            }
            Give("Leader", "Trait: Headstrong", ELifetime.ThisRound, "+1 morale (Leader)");
            Give("Instigator", "+1 to hit rolls in melee", ELifetime.ThisActivation, "+1 to hit in melee (Instigator)");
            Give("Tactician", "+1 to hit rolls when shooting", ELifetime.ThisActivation, "+1 to hit shooting (Tactician)");
            Give("Vanguard", CampaignRuleCatalog.VanguardCharge.Name, ELifetime.ThisActivation, "+4\" charge (Vanguard)");
            Give("Scavenger", "+6\" range when shooting", ELifetime.ThisActivation, "+6\" range (Scavenger)");
            if (given.Count > 0) c.Log($"{unit.Name}: {string.Join(", ", given)}.");

            if (Has(unit, CampaignRuleCatalog.Warden.Name))
            {
                foreach (IUnit friend in c.TableState.Units.Objects.Where(u => !ReferenceEquals(u, unit) && u.GetIsAlive()
                             && Allied(c, u.PlayerID, unit.PlayerID) && u.Tokens.HasToken(TokenType.Shaken)
                             && Distance(u, unit) <= AuraRange).ToList())
                {
                    friend.Tokens.RemoveTokens(TokenType.Shaken, friend.Tokens.GetTokenCount(TokenType.Shaken));
                    await c.Announce($"Warden: {friend.Name} is no longer Shaken.");
                }
            }
        }

        /// <summary>An attack is about to roll to hit: Mastermind (-1 Defense near the hero), Suppressor and Crazed.</summary>
        public static void OnAttack(IGameContext c, IUnit attacker, IUnit defender, bool melee, bool charging)
        {
            if (Near(c, defender, CampaignRuleCatalog.Mastermind.Name, friendly: false).Any())
            {
                QuestActions.Grant(defender, defender, ERollKind.Save, -1, ELifetime.NextTrigger, c, CampaignRuleCatalog.Mastermind.Name);
                c.Log($"Mastermind: {defender.Name} gets -1 Defense.");
            }
            if (!melee && Has(attacker, CampaignRuleCatalog.Suppressor.Name))
            {
                QuestTreasure.GrantRule(c, attacker, defender, "-1 to hit rolls when shooting", ELifetime.ThisRound);
                c.Log($"Suppressor: {defender.Name} gets -1 to hit when shooting this round.");
            }
            if (melee && charging && Has(attacker, CampaignRuleCatalog.Crazed.Name))
                QuestTreasure.GrantRule(c, attacker, attacker, "Extra hit in melee", ELifetime.NextTrigger);
        }

        /// <summary>The wound effect roll's campaign modifiers: Berserker (+1 from the hero in melee), Chemist (-1 near
        /// a friendly Chemist).</summary>
        public static int WoundRollModifier(IGameContext c, IUnit unit, IUnit? attacker, bool melee)
        {
            int mod = 0;
            if (melee && attacker != null && Has(attacker, CampaignRuleCatalog.Berserker.Name)) mod++;
            if (Near(c, unit, CampaignRuleCatalog.Chemist.Name, friendly: true).Any()) mod--;
            return mod;
        }

        /// <summary>Herbalist: a unit within 6" of a friendly Herbalist ignores each wound on a 6.</summary>
        public static int HerbalistIgnores(IGameContext c, IUnit unit, int wounds)
        {
            if (wounds <= 0 || !Near(c, unit, CampaignRuleCatalog.Herbalist.Name, friendly: true).Any()) return 0;
            int ignored = 0;
            for (int i = 0; i < wounds; i++) if (c.DiceRoller.RollDecisiveFace() == 6) ignored++;
            if (ignored > 0) c.Log($"Herbalist: {unit.Name} ignores {ignored} wound(s).");
            return ignored;
        }

        /// <summary>Motivated: true (once per match) when the unit passes this morale test automatically.</summary>
        public static bool UseMotivated(IGameContext c, IUnit unit)
        {
            if (!Has(unit, CampaignRuleCatalog.Motivated.Name) || unit.Tokens.HasToken(MotivatedUsed)) return false;
            unit.Tokens.AddToken(new Token(MotivatedUsed, 1, new TokenClearTrigger.ManualOnly()));
            c.Log($"Motivated: {unit.Name} passes its first morale test of the match.");
            return true;
        }
    }
}
