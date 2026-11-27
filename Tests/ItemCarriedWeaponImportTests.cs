using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests;

// Bug hunt 2026-11-27: Army Forge nests some units' base weapons inside an item (Jackals' Charger Beast carries
// its Explosive Spear in "Beast Rider", Goblins' Pump Plane its Bombing Run in "Bomber Plane"). The book
// importer kept the item's rules and dropped its weapons, so those units went to war with no attacks at all.
// The JSON below is the Charger Beast's own shape from Army Forge (GF Jackals 3.6.1), trimmed.
[TestFixture]
public class ItemCarriedWeaponImportTests
{
    private const string Json = """
    {
      "name": "Jackals", "versionString": "3.6.1",
      "units": [
        { "id": "cb", "name": "Charger Beast", "size": 1, "cost": 145, "quality": 4, "defense": 3,
          "weapons": [],
          "rules": [ {"name":"Tough","rating":6} ],
          "items": [ { "name": "Beast Rider", "type": "ArmyBookItem", "count": 1, "content": [
              { "type": "ArmyBookWeapon", "name": "Explosive Spear", "count": 1, "range": 0, "attacks": 4,
                "specialRules": [ {"type":"ArmyBookRule","name":"Shred"} ] } ] } ],
          "upgrades": ["P1"] }
      ],
      "upgradePackages": [
        { "uid": "P1", "sections": [
          { "id":"s1", "label":"Replace Beast Rider", "variant":"replace", "affects":{"type":"exactly","value":1}, "targets":["Beast Rider"],
            "options":[ { "id":"o1", "label":"Heavy Claws", "cost":5, "gains":[
                {"type":"ArmyBookWeapon","name":"Heavy Claws","count":1,"range":0,"attacks":4,"specialRules":[{"name":"AP","rating":1}]} ] } ] }
        ] }
      ]
    }
    """;

    private static BookFile Book() => OprBookImporter.Import(Json, "src", "lic");

    [Test]
    public void ItemContentWeapons_JoinTheUnitsWeapons()
    {
        RosterUnit beast = Book().Units.Single();
        WeaponFileEntry spear = beast.Weapons.Single();
        Assert.That(spear.Name, Is.EqualTo("Explosive Spear"));
        Assert.That(spear.Attacks, Is.EqualTo(4));
        Assert.That(spear.SpecialRules.Select(r => r.PrintableName), Does.Contain("Shred"));
        Assert.That(beast.Items.Single().Weapons.Single().Name, Is.EqualTo("Explosive Spear"),
            "the item remembers what it carries");
    }

    [Test]
    public void CompiledUnit_FightsWithTheItemsWeapon()
    {
        BookFile book = Book();
        UnitFileEntry beast = ListCompiler.Compile(book,
            new BuilderList { PointsLimit = 1000, Units = { new BuilderUnit { RosterUnitId = "cb" } } }).Units.Single();
        Assert.That(beast.Weapons.Select(w => w.Name), Is.EqualTo(new[] { "Explosive Spear" }));
    }

    [Test]
    public void ReplacingTheItem_TakesItsWeaponAway()
    {
        BookFile book = Book();
        UpgradeSection section = book.Units.Single().Sections.Single();
        var bu = new BuilderUnit { RosterUnitId = "cb" };
        bu.Choices.Add(new UpgradeChoice { SectionId = section.Id, OptionId = section.Options.Single().Id, Count = 1 });
        UnitFileEntry beast = ListCompiler.Compile(book, new BuilderList { PointsLimit = 1000, Units = { bu } }).Units.Single();
        Assert.That(beast.Weapons.Select(w => w.Name), Is.EqualTo(new[] { "Heavy Claws" }),
            "Army Forge: replacing the Beast Rider swaps its Explosive Spear for the Heavy Claws");
    }

    // Goblins' Beast Riders (AoF 3.6): five "Spider Mounts", whose content is "Poison Fangs" x5 - the count is
    // for the whole stack, so the unit fights with five fangs, one per mount.
    [Test]
    public void StackedItem_WeaponCountIsForTheWholeStack()
    {
        const string json = """
        { "name": "Goblins", "versionString": "3.6", "units": [
          { "id": "br", "name": "Beast Riders", "size": 5, "cost": 100, "quality": 5, "defense": 5,
            "weapons": [ { "name": "Hand Weapon", "count": 5, "range": 0, "attacks": 1, "specialRules": [] } ],
            "rules": [],
            "items": [ { "name": "Spider Mounts", "type": "ArmyBookItem", "count": 5, "content": [
                { "type": "ArmyBookWeapon", "name": "Poison Fangs", "count": 5, "range": 0, "attacks": 1,
                  "specialRules": [ {"type":"ArmyBookRule","name":"Shred"} ] },
                { "type": "ArmyBookRule", "name": "Strider" } ] } ],
            "upgrades": [] } ] }
        """;
        RosterUnit riders = OprBookImporter.Import(json, "src", "lic").Units.Single();
        Assert.That(riders.Weapons.Single(w => w.Name == "Poison Fangs").Quantity, Is.EqualTo(5));
        Assert.That(riders.Items.Single().Weapons.Single().Quantity, Is.EqualTo(1), "one per mount");
    }
}
