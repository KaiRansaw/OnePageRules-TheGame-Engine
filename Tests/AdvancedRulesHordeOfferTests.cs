using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// Bug hunt 2026-11-28 (AoF: Skirmish Horde + Advanced options): the lobby offered Escalating Battles, Surprise
    /// Engagement, Prolonged Battle and the Tactical Phase (Forward Positions, Battle Preparations) in a Horde game,
    /// where the engine never runs them - Horde Mode deploys and ends by its own book ("First the defenders place all
    /// their units within 6" of the table centre ..."; "The defenders have successfully finished round 12"). A Horde
    /// game with any one of them ticked played out identically to one without. Now they are standard-game only: not
    /// offered in a Horde lobby, and not "on" in a Horde game even if a save or the CLI carries the flag.
    /// </summary>
    [TestFixture]
    public class AdvancedRulesHordeOfferTests
    {
        private static readonly EGameSystem[] FourGames =
            { EGameSystem.GrimdarkFuture, EGameSystem.GFFirefight, EGameSystem.AgeOfFantasy, EGameSystem.AoFSkirmish };

        [Test]
        public void HordeLobby_DoesNotOfferTheStandardGameOnlyOptions([ValueSource(nameof(FourGames))] EGameSystem system)
        {
            foreach (string chapter in AdvancedRules.Chapters(system, EGameMode.Horde))
                foreach (AdvancedRuleOption o in AdvancedRules.In(chapter, system, EGameMode.Horde))
                    Assert.That(o.Flag & AdvancedRules.StandardGameOnly, Is.EqualTo(EAdvancedRules.None),
                        $"{o.Name} is offered in a {system} Horde lobby but does nothing there");
            Assert.That(AdvancedRules.Chapters(system, EGameMode.Horde), Does.Not.Contain(AdvancedRules.TacticalPhaseChapter));
        }

        [Test]
        public void HordeLobby_StillOffersTheRoundRulesOfFogOfWar()
        {
            var names = AdvancedRules.In(AdvancedRules.FogOfWarChapter, EGameSystem.AoFSkirmish, EGameMode.Horde).Select(o => o.Name).ToList();
            Assert.That(names, Is.EquivalentTo(new[] { FogOfWar.EbbAndFlowName, FogOfWar.ShiftingFocusName, FogOfWar.CombatHesitationName }));
        }

        [Test]
        public void StandardLobby_OffersEverythingItDidBefore([ValueSource(nameof(FourGames))] EGameSystem system)
        {
            Assert.That(AdvancedRules.Chapters(system, EGameMode.Standard), Is.EqualTo(AdvancedRules.Chapters(system)));
            foreach (string chapter in AdvancedRules.Chapters(system))
                Assert.That(AdvancedRules.In(chapter, system, EGameMode.Standard), Is.EqualTo(AdvancedRules.In(chapter, system)));
        }

        [Test]
        public void HordeGame_StandardGameOnlyFlags_AreNotOn()
        {
            var settings = new GameSettings
            {
                GameSystem = EGameSystem.AoFSkirmish, GameMode = EGameMode.Horde,
                AdvancedRules = EAdvancedRules.SurpriseEngagement | EAdvancedRules.EscalatingBattles | EAdvancedRules.ForwardPositions
                                | EAdvancedRules.ProlongedBattle | EAdvancedRules.EbbAndFlow,
            };
            Assert.That(AdvancedRules.Enabled(settings), Is.EqualTo(EAdvancedRules.EbbAndFlow));
            Assert.That(AdvancedRules.IsOn(settings, EAdvancedRules.SurpriseEngagement), Is.False,
                "Surprise Engagement would still cut a Horde Ambusher's arrival to one table section");

            settings.GameMode = EGameMode.Standard;
            Assert.That(AdvancedRules.IsOn(settings, EAdvancedRules.SurpriseEngagement), Is.True);
            Assert.That(AdvancedRules.IsOn(settings, EAdvancedRules.ForwardPositions), Is.True);
        }
    }
}
