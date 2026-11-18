using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.StageResolution.Requests;

namespace FDG.Stages
{
    /// <summary>
    /// Campaign Rules 3.6.0 Captain skill Idol ("Once per game, the Hero and its unit may ignore becoming Shaken")
    /// and Warrior skill Berserker ("... may ignore becoming Fatigued"). Asked at the moment the token would
    /// land - the Star Quest "spend power to ignore being Shaken" question sits at the same spot. The use is
    /// recorded with the same "used" marker an ActivatedAbility's once-per-game cost writes, so it rides saves.
    /// </summary>
    public static class OncePerGameIgnore
    {
        public const string Idol = "Skill: Idol";
        public const string Berserker = "Skill: Berserker";

        /// <summary>
        /// True when the unit (or a hero joined to it) has <paramref name="ruleName"/>, has not used it this game,
        /// is not already <paramref name="token"/>, and its player chooses to ignore it now. Spends the use on yes.
        /// </summary>
        public static async Task<bool> Offer(IGameContext gameContext, IUnit unit, string ruleName, TokenType token,
            string what)
        {
            if (unit.Tokens.HasToken(token) || !Has(unit, ruleName)) return false;
            TokenType used = RuleEvaluator.UsedMarker(ruleName);
            if (unit.Tokens.HasToken(used)) return false;

            string skill = ruleName.Replace("Skill: ", "");
            bool ignore = await gameContext.PlayerRequester.RequestDecision<YesNoRequest, bool>(new YesNoRequest(
                unit.PlayerID, $"{unit.Name} is about to become {what}. Use {skill} (once per game) to ignore it?",
                defaultAnswer: true, displayName: $"{unit.Name}: use {skill}?"));
            if (!ignore) return false;

            unit.Tokens.AddToken(new Token(used, 1, new TokenClearTrigger.ManualOnly()));
            gameContext.Log($"{unit.Name} uses {skill} - ignores becoming {what}.");
            return true;
        }

        private static bool Has(IUnit unit, string ruleName) =>
            unit.RuleDefinitions.Any(r => r.Definition.Name == ruleName)
            || unit.Models.Any(m => m.GetIsAlive() && m.RuleDefinitions.Any(r => r.Definition.Name == ruleName));
    }
}
