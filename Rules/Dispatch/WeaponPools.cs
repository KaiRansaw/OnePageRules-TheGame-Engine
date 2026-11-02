namespace FDG.Rules.Dispatch
{
    /// <summary>
    /// Which attack pool a weapon belongs to - the single answer, for everywhere that asks.
    ///
    /// <para>
    /// <b>Why this exists.</b> Pool membership used to be decided independently in two places:
    /// <c>IUnitExtensions.GetMeleeWeapons</c>, which answers "can this unit fight at all", and
    /// <c>MeleeRangeUtilities.GetMeleeWeaponsFromModels</c>, which builds the pool the melee actually
    /// swings. #409 taught the first one about <see cref="AssaultRules">Assault</see> and not the second,
    /// so for every game since, an <b>Assault weapon was never offered in a melee</b> - the unit counted
    /// as melee-capable, walked into the fight, and then swung with whatever else it was carrying. A unit
    /// whose only weapon was Assault was told it had nothing to strike back with.
    /// </para>
    /// <para>
    /// The second function even carried a comment saying it mirrored the first. It did when it was
    /// written. A comment asserting that two pieces of code agree is not a mechanism for keeping them
    /// agreeing, and this class is - there is now one predicate and nowhere to add a rule to half of it.
    /// </para>
    ///
    /// <para>
    /// Both rules here are about pool membership rather than about what happens once a weapon is in a
    /// pool, which is why neither can be an ordinary dispatch rule: the question is asked of a weapon
    /// list, before any rule evaluation happens.
    /// </para>
    /// </summary>
    public static class WeaponPools
    {
        /// <summary>
        /// Whether <paramref name="weapon"/> swings in melee.
        ///
        /// <para>
        /// Range 0 is the ordinary answer. <b>Assault</b> adds a weapon that also has a range - <i>"counts
        /// as both a ranged weapon and a melee weapon"</i> (OPR 3.6.0) - and <b>Strafing</b> removes one
        /// that has none, because a bomb rack is range 0 and "may only be used in this way", so without
        /// the exclusion it would be swung like a club.
        /// </para>
        /// </summary>
        public static bool CountsAsMelee(IWeapon weapon) =>
            (weapon.IsMelee() || AssaultRules.IsAssault(weapon)) && !StrafingRules.IsStrafeOnly(weapon);

        /// <summary>
        /// Whether <paramref name="weapon"/> can be shot. An Assault weapon has a real range and is
        /// already here on that alone - it is in <i>both</i> pools, which is what the rule says.
        /// </summary>
        public static bool CountsAsRanged(IWeapon weapon) =>
            weapon.IsRanged() && !StrafingRules.IsStrafeOnly(weapon);
    }
}
