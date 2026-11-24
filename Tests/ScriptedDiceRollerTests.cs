using NUnit.Framework;

namespace FDG.Tests
{
    // How to Play's set dice (2026-11-24): the practice battle plans each step's rolls so the lesson's moment
    // always happens. These pin the three things the lessons lean on: a pattern answers exactly one roll, an
    // empty roll never eats an entry, and everything unplanned is ordinary dice.
    [TestFixture]
    public class ScriptedDiceRollerTests
    {
        private static int[] Faces(IDiceResults r)
        {
            var list = new System.Collections.Generic.List<int>();
            for (int face = r.SideMin; face <= r.SideMax; face++)
                for (int i = 0; i < (int)System.Math.Round(r.At(face)); i++) list.Add(face);
            return list.ToArray();
        }

        [Test]
        public void APlannedRoll_UsesThePatternsFirstFaces()
        {
            var dice = new ScriptedDiceRoller(seed: 1);
            dice.Plan(new[] { 6, 4, 2, 5, 1 });

            Assert.That(Faces(dice.Roll(3)), Is.EqualTo(new[] { 2, 4, 6 }), "6, 4, 2 - the first three faces");
            Assert.That(dice.Planned, Is.EqualTo(0), "one entry answers one roll, however many dice");
        }

        [Test]
        public void ALongerRoll_RepeatsThePattern()
        {
            var dice = new ScriptedDiceRoller(seed: 1);
            dice.Plan(new[] { 6, 1 });

            IDiceResults r = dice.Roll(5);
            Assert.That(r.At(6), Is.EqualTo(3f), "6, 1, 6, 1, 6");
            Assert.That(r.At(1), Is.EqualTo(2f));
        }

        [Test]
        public void EntriesAnswerRollsInOrder_ThenOrdinaryDiceTakeOver()
        {
            var dice = new ScriptedDiceRoller(seed: 7);
            dice.Plan(new[] { 6 }, new[] { 1 });

            Assert.That(dice.Roll(2).At(6), Is.EqualTo(2f), "first entry");
            Assert.That(dice.RollDecisive().At(1), Is.EqualTo(1f), "second entry - a morale die that must fail");

            IDiceResults free = dice.Roll(10);
            Assert.That(free.AtOrAbove(1), Is.EqualTo(10f), "unplanned rolls still roll every die");
            Assert.That(dice.Used, Is.EqualTo(2));
        }

        [Test]
        public void AZeroDiceRoll_DoesNotUseAnEntry()
        {
            // No hits means a defence roll of zero dice. If that ate the next entry, every planned roll after it
            // would land on the wrong dice.
            var dice = new ScriptedDiceRoller(seed: 1);
            dice.Plan(new[] { 5 });

            Assert.That(dice.Roll(0).AtOrAbove(1), Is.EqualTo(0f));
            Assert.That(dice.Planned, Is.EqualTo(1), "still waiting for a real roll");
            Assert.That(dice.Roll(1).At(5), Is.EqualTo(1f));
        }

        [Test]
        public void ClearPlan_DropsWhatIsLeft()
        {
            var dice = new ScriptedDiceRoller(seed: 1);
            dice.Plan(new[] { 6 }, new[] { 6 });
            dice.ClearPlan();
            Assert.That(dice.Planned, Is.EqualTo(0));
        }

        [Test]
        public void TheSameSeed_GivesTheSameUnplannedRolls()
        {
            var a = new ScriptedDiceRoller(seed: 99);
            var b = new ScriptedDiceRoller(seed: 99);
            Assert.That(Faces(a.Roll(20)), Is.EqualTo(Faces(b.Roll(20))), "the practice battle is the same every time");
        }

        [Test]
        public void FacesOutsideOneToSix_AreRefused()
        {
            var dice = new ScriptedDiceRoller(seed: 1);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => dice.Plan(new[] { 7 }));
            Assert.Throws<System.ArgumentException>(() => dice.Plan(new int[0]));
        }
    }
}

namespace FDG.Tests
{
    [TestFixture]
    public class ScriptedDiceSteeringTests
    {
        private static readonly PlayerID Me = new(System.Guid.NewGuid());
        private static readonly PlayerID Them = new(System.Guid.NewGuid());

        private static int[]? MeLucky(FDG.Rules.Definitions.ERollKind kind, PlayerID who) =>
            who.Equals(Me) ? new[] { 6, 5, 4 } : new[] { 1, 2 };

        [Test]
        public void SteeredRolls_FollowWhoseRollItIs()
        {
            var dice = new ScriptedDiceRoller(seed: 3) { Steer = MeLucky };

            IDiceResults mine = DiceRollContext.Within(FDG.Rules.Definitions.ERollKind.Hit, Me, () => dice.Roll(3));
            IDiceResults theirs = DiceRollContext.Within(FDG.Rules.Definitions.ERollKind.Save, Them, () => dice.Roll(4));

            Assert.That(mine.AtOrAbove(4), Is.EqualTo(3f), "my hit roll: 6, 5, 4");
            Assert.That(theirs.AtOrAbove(3), Is.EqualTo(0f), "their Defense roll: 1, 2, 1, 2");
            Assert.That(dice.Steered, Is.EqualTo(2));
        }

        [Test]
        public void ARollOutsideAContext_IsNotSteered_AndThePlanStillAnswersIt()
        {
            var dice = new ScriptedDiceRoller(seed: 3) { Steer = MeLucky };
            dice.Plan(new[] { 3 });

            Assert.That(dice.Roll(1).At(3), Is.EqualTo(1f), "no context: the planned entry");
            Assert.That(dice.Steered, Is.EqualTo(0));
            Assert.That(DiceRollContext.Current, Is.Null, "the scope left nothing behind");
        }

        [Test]
        public void ASteeredRoll_LeavesThePlannedEntriesAlone()
        {
            var dice = new ScriptedDiceRoller(seed: 3) { Steer = MeLucky };
            dice.Plan(new[] { 3 });
            DiceRollContext.Within(FDG.Rules.Definitions.ERollKind.Morale, Them, () => dice.RollDecisive());
            Assert.That(dice.Planned, Is.EqualTo(1));
        }
    }
}
