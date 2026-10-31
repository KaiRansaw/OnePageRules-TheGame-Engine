using System.Threading.Tasks;
using FDG.Data;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;

namespace FDG.Stages
{
    /// <summary>
    /// What GF: Firefight and AoF: Skirmish still do differently from the full game, **as of OPR 3.6.0**.
    ///
    /// <para>
    /// **#414 - this file used to be much larger, and most of it was deleted rather than fixed.** It
    /// implemented the **v3.5.1** Firefight, which replaced two whole subsystems:
    /// </para>
    /// <list type="bullet">
    ///   <item><b>Wound effects.</b> Each wound killed a model until one was left; the last model then
    ///   collected wound markers, and once it had Tough(X) of them it rolled one die + markers (+1 if
    ///   Shaken, + the attacker's Fear(X) in melee), 5+X knocking it out and anything lower Shaking the
    ///   unit.</item>
    ///   <item><b>Morale.</b> No morale from wounds or lost melees at all. Instead every unit in an army
    ///   down to half its starting UNITS tested at the end of each round, a failure Shaking it and a
    ///   failure while Shaken routing it.</item>
    /// </list>
    /// <para>
    /// **GFF Core Rules v3.6.0 contains neither.** It reads like the full game - *"Once a model has taken
    /// as many wounds as its Tough value, it is killed (remove from play)"*, the standard Morale Tests and
    /// Melee Results, and Fear(X) counting toward *who won the melee* rather than into a wound roll. So
    /// the wound markers, the wound-effect roll, the army-wide round-end test and this file's Fear reader
    /// are gone, and Firefight wounds and morale now run down exactly the same code as Grimdark Future.
    /// Kai's call, 2026-10-30: <i>"keep the game rules as close to 'as written' as possible"</i>.
    /// </para>
    ///
    /// <para>
    /// What is left that is genuinely Firefight: the 4'x4' table, 6" coherency and the per-400pt Force Org
    /// numbers (all on <see cref="GameWideConstants"/>), Jumping and Falling (still to do), and
    /// <see cref="TryPush"/> below.
    /// </para>
    /// </summary>
    public static class FirefightRules
    {
        /// <summary>True when the current game is a skirmish game (GF: Firefight / AoF: Skirmish).</summary>
        public static bool IsSkirmishGame(IGameContext c) => GameSystemInfo.IsSkirmish(c.Settings.GameSystem);

        /// <summary>
        /// **Pushing** (GFF Core Rules v3.6.0, Special Movement): *"Whenever a unit is Shaken after being
        /// attacked, the attacker may try to push it. Roll one die, and on a 4+ the attacker may move one
        /// model from the target by up to 2" in any direction, and then the opponent must place any other
        /// models from the unit within 1" of it."*
        ///
        /// <para>
        /// This survived 3.6.0 and is the one Firefight-only mechanic in this file. Its **trigger moved**,
        /// though, and that is the part worth knowing: under 3.5.1 it hung off the wound-effect roll (the
        /// only way a unit went Shaken from an attack), and that roll no longer exists. It now hangs off
        /// the two places an attack can Shake a unit - the wound-driven test after shooting or strafing,
        /// and the melee-loss test - so callers invoke it when a test they ran has just left the target
        /// Shaken. Nothing calls it for a unit that Shook itself (a Star Quest skill, a treasure, a
        /// random event): the book says "after being attacked", and there is no attacker in those.
        /// </para>
        ///
        /// <para>
        /// **Recorded approximation:** the book pushes ONE model and then has the defender place the rest
        /// within 1" of it. This moves the unit as a unit, up to 2", with the attacker's player steering -
        /// which the engine's own coherency rules then keep legal. The same approximation the 3.5.1
        /// implementation made, kept deliberately: a one-model push plus a defender-driven re-placement is
        /// two new prompts in a row, and the net effect on the table is within an inch of this.
        /// </para>
        /// </summary>
        /// <returns>True when the die came up 4+ and the push was offered.</returns>
        public static async Task<bool> TryPush(IGameContext c, IUnit target, IUnit? attacker)
        {
            if (!ShouldPush(c, target, attacker)) return false;

            await c.Announce($"Pushing: move {target.Name} up to 2\".");
            await new GameOperationServices(c).MoveUnit(target, 2f, isOptional: true,
                controller: attacker!.PlayerID);
            return true;
        }

        /// <summary>
        /// The rules half of <see cref="TryPush"/>: every gate the book puts on a push, plus the die.
        /// Split out from the move itself so the conditions can be tested without a live move prompt -
        /// the move needs a player (or resolver) to answer where the target goes, and a unit test that
        /// drives it just hangs waiting. Returns true when the attacker has earned the push.
        /// </summary>
        public static bool ShouldPush(IGameContext c, IUnit target, IUnit? attacker)
        {
            if (!IsSkirmishGame(c)) return false;
            if (attacker == null || !attacker.GetIsAlive() || !target.GetIsAlive()) return false;
            // A unit that is not actually Shaken was rescued by something (Fearless, No Retreat), and the
            // book's trigger is the Shaken itself, not the failed test.
            if (!target.Tokens.HasToken(TokenType.Shaken)) return false;

            int push = c.DiceRoller.RollDecisiveFace();
            if (push < 4)
            {
                c.Log($"Pushing: {attacker.Name} rolled {push} - no push.");
                return false;
            }

            c.Log($"Pushing: {attacker.Name} rolled {push} - may push {target.Name} up to 2\".");
            return true;
        }
    }
}
