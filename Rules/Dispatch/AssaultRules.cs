using System;
using System.Linq;

namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Assault (#409, OPR 3.6.0): "Counts as both a ranged weapon and a melee weapon." Promoted into the
    /// core rulebook in 3.6; the engine previously knew it only as inert army-supplement data.
    ///
    /// <para>The engine sorts weapons into the two attack pools purely by range -
    /// <c>IWeapon.IsRanged()</c> is "range greater than 0" and <c>IsMelee()</c> is its negation - so an
    /// Assault weapon, which has a real range, was already in the shooting pool and could never reach the
    /// melee one. This rule is therefore an ENGINE MARKER rather than a dispatch rule: there is no hook at
    /// which "also counts as melee" could be expressed, because weapon-pool membership is a question asked
    /// of the weapon list long before any rule evaluation happens. <c>IUnitExtensions.GetMeleeWeapons</c>
    /// consults this class the same way it consults <see cref="StrafingRules.IsStrafeOnly"/>, which solves
    /// the mirror-image problem (a range-0 bomb rack that must NOT be swung in melee).</para>
    ///
    /// <para>Name-based rather than structural, for the same reason <see cref="AircraftRules.IsAircraft"/>
    /// is: a marker rule has no effect shape to recognise it by. Case-insensitive and checking the
    /// requested name as well as the canonical one, so an army book's own embedded "Assault" and any alias
    /// it resolves through are both honoured.</para>
    /// </summary>
    public static class AssaultRules
    {
        public static bool IsAssault(IWeapon weapon) =>
            weapon.RuleDefinitions.Any(rule =>
                string.Equals(rule.Definition.Name, "Assault", StringComparison.OrdinalIgnoreCase)
                || string.Equals(rule.RequestedName, "Assault", StringComparison.OrdinalIgnoreCase));
    }
}
