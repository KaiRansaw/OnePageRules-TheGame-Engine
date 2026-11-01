using System.Collections.Generic;
using FDG.Data;
using FDG.Rules.Dispatch;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #420 - what a transport may carry is a per-system number, and the engine used Grimdark Future's
    /// in all six systems.
    ///
    /// <para>
    /// The Tough scale itself differs between the game families, so OPR scales the transport caps with
    /// it. Same sentence, three sets of numbers:
    /// </para>
    /// <list type="bullet">
    /// <item><b>GF / AoF:</b> "up to X friendly models or Heroes with up to <b>Tough(6)</b>, and
    /// non-Heroes with up to <b>Tough(3)</b> which occupy 3 spaces each." An ordinary model is
    /// Tough(1).</item>
    /// <item><b>Firefight / Skirmish:</b> models with <b>Tough(3)</b>, Heroes up to <b>Tough(18)</b>,
    /// non-Heroes up to <b>Tough(9)</b>.</item>
    /// <item><b>Star Quest / AoF: Quest:</b> models with <b>Tough(9)</b>, Heroes up to <b>Tough(54)</b>,
    /// non-Heroes up to <b>Tough(27)</b>.</item>
    /// </list>
    /// <para>
    /// Running 6/3/1 everywhere had two visible effects in the four non-Grimdark systems: passengers the
    /// book allows were <b>refused outright</b> as "too tough to be transported", and an ordinary
    /// Tough(3) Firefight model was charged the 3-space large-model rate instead of 1, so a transport
    /// held a third of what it should.
    /// </para>
    /// <para>
    /// <see cref="TransportUtilitiesTests"/> keeps asserting the Grimdark Future numbers through the
    /// no-profile overloads, which default to that profile - they were right, just not universal.
    /// </para>
    /// </summary>
    [TestFixture]
    public class TransportCapsPerSystemTests
    {
        private GameDataStore _store = null!;

        [SetUp]
        public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

        // ── Ride eligibility ────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The headline fault. A Tough(6) non-Hero is over Grimdark Future's Tough(3) cap and under
        /// Firefight's Tough(9) one, so the single shared number was the difference between "gets in"
        /// and "cannot be transported".
        /// </summary>
        [Test]
        public void ANonHeroOverGrimdarkFuturesCap_StillRidesInFirefight()
        {
            ModelData model = MakeModel(tough: 6);

            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.CanModelRide(model, isHero: false, CoreRulesProfile.FullScale),
                    Is.False, "Tough(6) is over Grimdark Future's Tough(3) non-Hero cap.");
                Assert.That(TransportUtilities.CanModelRide(model, isHero: false, CoreRulesProfile.Skirmish),
                    Is.True, "Firefight's cap is Tough(9), so the same model rides.");
                Assert.That(TransportUtilities.CanModelRide(model, isHero: false, CoreRulesProfile.StarQuest),
                    Is.True, "Star Quest's cap is Tough(27).");
            });
        }

        [Test]
        public void AHeroOverGrimdarkFuturesCap_StillRidesInFirefightAndStarQuest()
        {
            ModelData model = MakeModel(tough: 12);

            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.CanModelRide(model, isHero: true, CoreRulesProfile.FullScale),
                    Is.False, "Tough(12) is over Grimdark Future's Tough(6) Hero cap.");
                Assert.That(TransportUtilities.CanModelRide(model, isHero: true, CoreRulesProfile.Skirmish),
                    Is.True, "Firefight allows Heroes up to Tough(18).");
                Assert.That(TransportUtilities.CanModelRide(model, isHero: true, CoreRulesProfile.StarQuest),
                    Is.True, "Star Quest allows Heroes up to Tough(54).");
            });
        }

        /// <summary>
        /// The cap has to still BE a cap in each system - raising it must not have removed it. A model
        /// above even the Quest ceiling rides nowhere.
        /// </summary>
        [Test]
        public void EachSystemStillHasACeiling()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.CanModelRide(MakeModel(4), false, CoreRulesProfile.FullScale),
                    Is.False, "GF non-Hero cap is 3.");
                Assert.That(TransportUtilities.CanModelRide(MakeModel(10), false, CoreRulesProfile.Skirmish),
                    Is.False, "Firefight non-Hero cap is 9.");
                Assert.That(TransportUtilities.CanModelRide(MakeModel(28), false, CoreRulesProfile.StarQuest),
                    Is.False, "Star Quest non-Hero cap is 27.");
                Assert.That(TransportUtilities.CanModelRide(MakeModel(55), true, CoreRulesProfile.StarQuest),
                    Is.False, "Star Quest Hero cap is 54.");
            });
        }

        // ── Space cost ──────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The quieter half, and arguably the worse one: an ordinary Firefight trooper is Tough(3), and
        /// Grimdark Future's "anything over Tough(1) is a large model" charged it the 3-space rate. A
        /// 6-space transport held two of them instead of six.
        /// </summary>
        [Test]
        public void AnOrdinarySkirmishModel_CostsOneSpace_NotThree()
        {
            ModelData trooper = MakeModel(tough: 3);

            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.GetModelSpaceCost(trooper, false, CoreRulesProfile.FullScale),
                    Is.EqualTo(3), "in Grimdark Future a Tough(3) model really is a large one.");
                Assert.That(TransportUtilities.GetModelSpaceCost(trooper, false, CoreRulesProfile.Skirmish),
                    Is.EqualTo(1), "in Firefight Tough(3) IS the ordinary model, so it takes one space.");
                Assert.That(TransportUtilities.GetModelSpaceCost(trooper, false, CoreRulesProfile.StarQuest),
                    Is.EqualTo(1), "and it is well under Star Quest's Tough(9) standard model.");
            });
        }

        [Test]
        public void ALargeModelStillCostsThree_InEverySystem()
        {
            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(2), false, CoreRulesProfile.FullScale),
                    Is.EqualTo(3));
                Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(6), false, CoreRulesProfile.Skirmish),
                    Is.EqualTo(3), "over Firefight's Tough(3) standard, so it is the large rate.");
                Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(18), false, CoreRulesProfile.StarQuest),
                    Is.EqualTo(3), "over Star Quest's Tough(9) standard.");
            });
        }

        /// <summary>A Hero is always one space, whatever the system or its Tough - no scale involved.</summary>
        [Test]
        public void AHeroIsAlwaysOneSpace()
        {
            foreach (CoreRulesProfile profile in new[]
                     { CoreRulesProfile.FullScale, CoreRulesProfile.Skirmish, CoreRulesProfile.StarQuest })
            {
                Assert.That(TransportUtilities.GetModelSpaceCost(MakeModel(18), isHero: true, profile),
                    Is.EqualTo(1), $"{profile.SystemName}: a Hero takes one space.");
            }
        }

        /// <summary>
        /// The no-profile overloads keep Grimdark Future's numbers. 52 existing assertions rely on this,
        /// and so does any caller that has not been given a profile yet - a silent default is worth
        /// pinning so it cannot drift into being some other system's.
        /// </summary>
        [Test]
        public void TheDefaultProfileIsGrimdarkFutures()
        {
            ModelData trooper = MakeModel(tough: 3);

            Assert.Multiple(() =>
            {
                Assert.That(TransportUtilities.GetModelSpaceCost(trooper, isHero: false), Is.EqualTo(3));
                Assert.That(TransportUtilities.CanModelRide(MakeModel(6), isHero: false), Is.False);
                Assert.That(TransportUtilities.CanModelRide(MakeModel(6), isHero: true), Is.True);
                Assert.That(CoreRulesProfile.FullScale.TransportHeroToughCap,
                    Is.EqualTo(TransportUtilities.HeroToughCap));
                Assert.That(CoreRulesProfile.FullScale.TransportNonHeroToughCap,
                    Is.EqualTo(TransportUtilities.NonHeroToughCap));
            });
        }

        // ── Helpers ─────────────────────────────────────────────────────────────────────────────────

        private ModelData MakeModel(int tough)
        {
            var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon>(),
                initialPosition: new Position(0, 0), gameDataStore: _store);
            model.SetMaxWounds(tough);
            return model;
        }
    }
}
