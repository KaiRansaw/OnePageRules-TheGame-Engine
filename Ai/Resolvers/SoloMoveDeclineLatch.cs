namespace FDG.Ai.Resolvers
{
    /// <summary>
    /// #358: the one-way handshake between the solo bot's path resolver and its action menu.
    /// <para>
    /// When <see cref="AiDefineMovementResolver"/> declines the MAIN activation move (no legal
    /// path exists for a wedged unit), the engine's Back affordance returns to Choose Action -
    /// where the deterministic Charge > Move > Shoot > Pass policy would pick Move again, decline
    /// again, and loop the menu forever (~1.5M decisions until the bench watchdog fired). The
    /// resolver arms this latch on such a decline; the next
    /// <see cref="AiStringSelectionResolver"/> action pick consumes it and skips the movement
    /// family once, ending the activation with Shoot/Pass instead. One instance is shared by the
    /// two resolvers of a solo set (the engine runs a player's stages sequentially, so the arming
    /// decline and the next menu belong to the same activation by construction). Declining an
    /// optional rule-triggered move never arms it - that decline is final, not menu-reopening.
    /// </para>
    /// </summary>
    public sealed class SoloMoveDeclineLatch
    {
        private bool _armed;

        public void Arm() => _armed = true;

        private bool _standStill;

        /// <summary>
        /// The action menu has nothing left but a move that keeps being declined - a unit within 1" of an enemy may
        /// not Pass (#206), and if its charge and its move both fail it would be offered them forever (bench,
        /// 2026-11-15: Ork Berserkers looped until the watchdog). The next main move is made as a stand-still
        /// instead, which is always legal and ends the activation.
        /// </summary>
        public void RequestStandStill() => _standStill = true;

        /// <summary>True exactly once per <see cref="RequestStandStill"/> - reading clears it.</summary>
        public bool TakeStandStill()
        {
            bool was = _standStill;
            _standStill = false;
            return was;
        }

        /// <summary>True exactly once per <see cref="Arm"/> - reading clears it.</summary>
        public bool Consume()
        {
            bool wasArmed = _armed;
            _armed = false;
            return wasArmed;
        }
    }
}
