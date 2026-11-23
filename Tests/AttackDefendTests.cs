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
    
        [Test]
        public void TheAttackerPickedInTheLobby_GetsAQuarterMorePoints_OnlyInTheBonusMissions()
        {
            Assert.That(AttackDefend.AttackerOf(0), Is.EqualTo(1), "unset = team 1");
            Assert.That(AttackDefend.AttackerOf(-1), Is.EqualTo(-1), "roll-off");
            Assert.That(AttackDefend.AttackerOf(2), Is.EqualTo(2));
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.TheRaid, 2, 2), Is.EqualTo(2500));
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.TheRaid, 2, 1), Is.EqualTo(2000), "the defender");
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.TheRaid, 0, 1), Is.EqualTo(2500), "unset = team 1 attacks");
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.TheRaid, -1, 1), Is.EqualTo(2000), "roll-off: nobody known yet");
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.LastStand, 1, 1), Is.EqualTo(2000), "no bonus in Last Stand");
            Assert.That(AttackDefend.PointsLimit(2000, EExtraMission.None, 1, 1), Is.EqualTo(2000));
            Assert.That(AttackDefend.PointsLimit(301, EExtraMission.SmashAndGrab, 1, 1), Is.EqualTo(376), "rounded down");
        }

        [Test]
        public void OwnMarkerAreas_FrontEdgeLine_OrTheBackStripOver9InchesFromTheFront()
        {
            var bottom = new ZoneRect { Left = 0f, Right = 72f, Bottom = 0f, Top = 12f };
            var edge = ExtraMissions.OwnedArea(bottom, edge: true, 72f, 48f);
            Assert.That(edge.AlongX, Is.True);
            Assert.That(edge.Inner, Is.EqualTo(12f));
            Assert.That(edge.Band.Bottom, Is.EqualTo(11f).Within(0.01f));
            Assert.That(edge.Band.Top, Is.EqualTo(13f).Within(0.01f));
            Assert.That(edge.Band.Left, Is.GreaterThan(9f), "over 9\" from the table edge");
            Assert.That(edge.Band.Right, Is.LessThan(63f));

            var top = new ZoneRect { Left = 0f, Right = 72f, Bottom = 36f, Top = 48f };
            var back = ExtraMissions.OwnedArea(top, edge: false, 72f, 48f);
            Assert.That(back.Inner, Is.EqualTo(36f));
            Assert.That(back.Band.Bottom, Is.GreaterThan(45f), "over 9\" from the zone's front edge");
            Assert.That(back.Band.Top, Is.LessThanOrEqualTo(47.5f), "on the table");

            var left = new ZoneRect { Left = 0f, Right = 12f, Bottom = 0f, Top = 48f };
            var side = ExtraMissions.OwnedArea(left, edge: true, 72f, 48f);
            Assert.That(side.AlongX, Is.False, "a short-edge zone runs up the table");
            Assert.That(side.Band.Left, Is.EqualTo(11f).Within(0.01f));
        }
}
}
