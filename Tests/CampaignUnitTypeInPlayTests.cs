using System;
using System.Collections.Generic;
using FDG.Campaign;
using FDG.Data;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #454 - in a game, a renamed campaign unit says what it is. Kai, 2026-11-05: "the renamed units ...
    /// show the name but not the unit type. I need to know which unit type the unit is."
    ///
    /// <para>
    /// The type travels on the campaign sheet the unit already carries onto the table
    /// (<see cref="UnitData.StrikeUnitJson"/>, which is how kills have always been counted), so these build
    /// the sheet through the real campaign code - Start, then Rename - and read it back the way the hover
    /// card and the table label do.
    /// </para>
    /// </summary>
    [TestFixture]
    public class CampaignUnitTypeInPlayTests
    {
        private static UnitData InPlay(UnitFileEntry entry) =>
            new(new PlayerID(Guid.NewGuid()), entry.Name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>>())
            {
                StrikeUnitJson = entry.StrikeUnitJson,
            };

        private static ArmyListFile Campaign(params string[] unitNames)
        {
            var army = new ArmyListFile { Name = "Iron Reavers" };
            foreach (string n in unitNames) army.Units.Add(new UnitFileEntry { Name = n, ModelCount = 5, PointCost = 100 });
            CampaignRules.Start(army, "Iron Reavers", "x.fdgarmy", CampaignNumbers.Firefight, EGameSystem.GFFirefight);
            return army;
        }

        [Test]
        public void ARenamedUnitShowsItsType()
        {
            ArmyListFile army = Campaign("Assault Grunts");
            CampaignRules.Rename(army, army.Units[0], "Vargas Squad", null);

            UnitData unit = InPlay(army.Units[0]);
            Assert.That(unit.Name, Is.EqualTo("Vargas Squad"));
            Assert.That(unit.CampaignTypeIfRenamed, Is.EqualTo("Assault Grunts"));
        }

        [Test]
        public void AUnitStillCalledByItsTypeShowsNothingExtra()
        {
            ArmyListFile army = Campaign("Assault Grunts");
            Assert.That(InPlay(army.Units[0]).CampaignTypeIfRenamed, Is.Null,
                "'Assault Grunts (Assault Grunts)' would be noise.");
        }

        [Test]
        public void ASecondSquadOfTheSameTypeSaysWhatItIs()
        {
            ArmyListFile army = Campaign("Assault Grunts", "Assault Grunts");
            Assert.That(army.Units[1].Name, Is.EqualTo("Assault Grunts 2"));
            Assert.That(InPlay(army.Units[1]).CampaignTypeIfRenamed, Is.EqualTo("Assault Grunts"));
        }

        [Test]
        public void AnOrdinaryUnitHasNoCampaignType()
        {
            var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Warriors", 4, 4, new List<DataBinding<ModelData>>());
            Assert.That(unit.CampaignTypeIfRenamed, Is.Null);
        }

        [Test]
        public void AnOlderCampaignUnitWithNoStoredTypeShowsNothingRatherThanAGuess()
        {
            ArmyListFile army = Campaign("Assault Grunts");
            StrikeUnit sheet = CampaignRules.Sheet(army.Units[0])!;
            sheet.UnitType = "";
            CampaignRules.Save(army.Units[0], sheet);

            Assert.That(InPlay(army.Units[0]).CampaignTypeIfRenamed, Is.Null);
        }

        /// <summary>
        /// The answer is cached, and the sheet is rewritten during a game (kills, events). A rewrite must be
        /// seen - the cache is keyed on the sheet itself, not taken once.
        /// </summary>
        [Test]
        public void ARewrittenSheetIsReadAgain()
        {
            ArmyListFile army = Campaign("Assault Grunts");
            CampaignRules.Rename(army, army.Units[0], "Vargas Squad", null);
            UnitData unit = InPlay(army.Units[0]);
            Assert.That(unit.CampaignTypeIfRenamed, Is.EqualTo("Assault Grunts"));

            StrikeUnit sheet = StrikeUnit.FromJson(unit.StrikeUnitJson)!;
            sheet.Kills = 2;
            sheet.UnitType = "Storm Grunts";
            unit.StrikeUnitJson = sheet.ToJson();

            Assert.That(unit.CampaignTypeIfRenamed, Is.EqualTo("Storm Grunts"));
        }
    }
}
