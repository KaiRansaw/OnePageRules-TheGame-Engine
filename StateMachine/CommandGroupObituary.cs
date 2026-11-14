using FDG.Presentation.Beats;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// #441 - the battle-report lines for a Banner or Musician leaving the table, asked for by Kai on
    /// 2026-11-01.
    ///
    /// <para>
    /// Three messages, and which one you get depends on what actually happened rather than on the game
    /// system:
    /// </para>
    /// <list type="bullet">
    ///   <item>A bearer dies and units were relying on it:
    ///     <i>"Spearmen with Banner died! Archers and Militia lose benefits from Banner!"</i></item>
    ///   <item>A bearer dies and nothing was relying on it:
    ///     <i>"Spearmen with Banner died!"</i></item>
    ///   <item>A unit that was being covered dies:
    ///     <i>"Archers receiving Banner died!"</i></item>
    /// </list>
    ///
    /// <para>
    /// <b>Driven by the grants, not by a list of systems.</b> Kai described the long form for
    /// "Firefight/Skirmish" and the short form for "GF/AoF", but the long form belongs to any game where
    /// units were picked - which is <b>four</b> systems, Star Quest and AoF: Quest included (#439). Asking
    /// "did anything actually lose a buff" gets all six right without naming any of them, and stays right
    /// if a future book changes which systems have the picks.
    /// </para>
    ///
    /// <para>
    /// <b>Timing is the whole trick.</b> This must run BEFORE
    /// <c>TokenClearService.ClearForDestroyedOwner</c>, because that is what removes the grants - after
    /// it there is nothing left to name. <see cref="UnitDestructionNotifier"/> calls it first for that
    /// reason.
    /// </para>
    ///
    /// <para>
    /// The rule NAME is passed to the beat as a referenced rule
    /// (<see cref="Presentation.PresentationBeat.ReferencedRules"/>) so the front end can highlight it and
    /// hover its book wording. The engine deliberately does not format that itself - see that property.
    /// </para>
    /// </summary>
    public static class CommandGroupObituary
    {
        /// <summary>The report colour for a buff being lost: the same warm tone casualties use.</summary>
        private static readonly TextColor Lost = new TextColor(255, 150, 90, 255);

        /// <summary>
        /// Announce what <paramref name="dead"/>'s death does to the Command Group buffs on the table.
        /// Call before the destroyed unit's tokens are swept.
        /// </summary>
        public static async Task Announce(IGameContext gameContext, IUnit dead)
        {
            // 2026-11-13: every conferral, not just Banner and Musician - in the four wide games every aura can be
            // handed to picked units too (AuraConferral). Driven by the grants, so GF and AoF still get only the short line.
            foreach (AuraConferral.Conferral c in AuraConferral.For(dead))
            {
                List<string> covered = UnitsCoveredBy(gameContext, dead, c.Granted);
                // A bearer whose aura reached nobody else: Banner/Musician keep their #441 line; a plain aura
                // dying with its unit is not news (every dead Hero would announce it).
                if (covered.Count == 0 && !CoreRulesProfile.ConferringCommandGroupRules.Contains(c.Source)) continue;

                string text = covered.Count == 0
                    ? $"{dead.Name} with {c.Source} died!"
                    : $"{dead.Name} with {c.Source} died! {Join(covered)} "
                      + $"{(covered.Count == 1 ? "loses" : "lose")} benefits from {c.Source}!";

                await Present(gameContext, text, c.Source);
            }

            // The dead unit was RECEIVING something from somebody else's bearer.
            foreach (Token token in dead.Tokens.GetAllTokens(TokenType.RuleGrant))
            {
                if (token.OwnerUnitID is null || token.OwnerUnitID == dead.ID) continue;
                // Only the picked-unit grants: an Aura-lifetime rule grant that ends when its bearer dies.
                if (token.Payload is not TokenPayload.RuleGrant { Lifetime: ELifetime.Aura } grant) continue;
                if (token.ClearTrigger is not TokenClearTrigger.OwnerDestroyed) continue;
                await Present(gameContext, $"{dead.Name} receiving {grant.RuleName} died!", grant.RuleName);
            }
        }

        /// <summary>
        /// The living units holding a grant of <paramref name="ruleName"/> that <paramref name="bearer"/>
        /// placed. Matched on the token's <c>OwnerUnitID</c>, which is the same thing
        /// <c>TokenClearService.ClearForDestroyedOwner</c> is about to sweep on - so the message names
        /// exactly the units that are about to lose it, and cannot drift from what really happens.
        /// </summary>
        private static List<string> UnitsCoveredBy(IGameContext gameContext, IUnit bearer, string ruleName)
        {
            var names = new List<string>();

            foreach (IUnit unit in gameContext.TableState.Units.Objects)
            {
                if (ReferenceEquals(unit, bearer)) continue;
                // A unit already off the table has nothing to lose, and the book's own wording is about
                // "picked models that are DEPLOYED".
                if (!unit.GetIsAlive()) continue;

                foreach (Token token in unit.Tokens.GetAllTokens(TokenType.RuleGrant))
                {
                    if (token.OwnerUnitID != bearer.ID) continue;
                    if (token.Payload is not TokenPayload.RuleGrant grant) continue;
                    if (!string.Equals(grant.RuleName, ruleName, StringComparison.OrdinalIgnoreCase)) continue;

                    names.Add(unit.Name);
                    break;
                }
            }

            return names;
        }

        /// <summary>"A", "A and B", "A, B and C" - game text, so ASCII only (CLAUDE.md).</summary>
        private static string Join(List<string> names) => names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => string.Join(", ", names.Take(names.Count - 1)) + $" and {names[^1]}",
        };

        private static Task Present(IGameContext gameContext, string text, string ruleName)
        {
            gameContext.Log(text, Lost);
            // Toast, not Headline: a banner going down is worth seeing and worth keeping in the report,
            // but it is not worth stopping the game for.
            return gameContext.Presenter.Present(
                new BannerBeat(text, Lost, EBannerTier.Toast, new[] { ruleName }));
        }
    }
}
