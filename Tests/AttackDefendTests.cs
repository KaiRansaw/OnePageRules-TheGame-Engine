using FDG.Missions;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>Attack &amp; Defend missions (Advanced Rulebooks 3.6.0 p26, Kai 2026-11-22). Played-game checks: the bot
    /// sweep with FDG_MISSION_AUDIT=1 (every placed model re-checked against its mission's area).</summary>
    [TestFixture]
    public class AttackDefendTests
    {
        [Test]
        public void TheSixMissions_AreAttackAndDefend_AndLastSixRounds()
        {
            var six = new[] { EExtraMission.TheRaid, EExtraMission.SmashAndGrab, EExtraMission.Ambush,
                EExtraMission.LastStand, EExtraMission.VipEscort, EExtraMission.TheRescue };
            GameSettings s = GameSettings.GetDefault();
            foreach (EExtraMission m in six)
            {
                Assert.That(ExtraMissionCatalog.IsAttackDefend(m), Is.True, m.ToString());
                s.ExtraMission = m;
                Assert.That(StateMachine.HordeRules.RoundLimit(s), Is.EqualTo(6), m.ToString());
            }
            s.NumberOfRounds = 5;
            Assert.That(StateMachine.HordeRules.RoundLimit(s), Is.EqualTo(5), "a round count set in the lobby still wins");
            s.NumberOfRounds = null;
            s.ExtraMission = EExtraMission.Domination;
            Assert.That(StateMachine.HordeRules.RoundLimit(s), Is.EqualTo(4));
            Assert.That(ExtraMissionCatalog.IsAttackDefend(EExtraMission.Random), Is.False);
        }

        [Test]
        public void OnlyTheRaidSmashAndGrabAndTheRescue_GiveTheAttackerMorePoints()
        {
            var bonus = ExtraMissionCatalog.All.Where(i => i.AttackerBonusPoints).Select(i => i.Mission).ToList();
            Assert.That(bonus, Is.EquivalentTo(new[] { EExtraMission.TheRaid, EExtraMission.SmashAndGrab, EExtraMission.TheRescue }));
            foreach (ExtraMissionInfo i in ExtraMissionCatalog.All.Where(i => i.AttackerBonusPoints))
                Assert.That(i.BookText, Does.Contain("25% more points"));
        }

        [Test]
        public void MissionPlacementZone_KeepsInsideItsArea_AndOutOfItsHoles()
        {
            var zone = new MissionPlacementZone(72f, 48f, new CircularZone(36f, 24f, 12f),
                new[] { new CircularZone(36f, 24f, 3f) });
            Assert.That(zone.IsPointWithinZone(new Float2(36f, 24f)), Is.False, "inside the hole");
            Assert.That(zone.IsPointWithinZone(new Float2(44f, 24f)), Is.True);
            Assert.That(zone.IsPointWithinZone(new Float2(49f, 24f)), Is.False, "outside 12\"");
            var table = new MissionPlacementZone(72f, 48f, null, null);
            Assert.That(table.IsPointWithinZone(new Float2(1f, 1f)), Is.True);
            Assert.That(table.IsPointWithinZone(new Float2(-1f, 1f)), Is.False, "off the table");
            Assert.That(zone.Bounds.Left, Is.EqualTo(24f).Within(0.01f));
        }

        [Test]
        public void TheRescue_DropsTheMarkerSixInchesTowardTheCentre_SmashAndGrabOne()
        {
            GameWideConstants.ConfigureTable(EGameSystem.GrimdarkFuture);
            Position rescue = AttackDefend.DropSpot(null!, EExtraMission.TheRescue, new Position(2f, 24f));
            Assert.That(rescue.x, Is.EqualTo(8f).Within(0.01f));
            Position relic = AttackDefend.DropSpot(null!, EExtraMission.SmashAndGrab, new Position(36f, 2f));
            Assert.That(relic.z, Is.EqualTo(3f).Within(0.01f));
            Assert.That(AttackDefend.EdgeDistance(new Position(5f, 30f)), Is.EqualTo(5f));
        }
    }
}
