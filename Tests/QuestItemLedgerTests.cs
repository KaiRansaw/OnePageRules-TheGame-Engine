using FDG.Quest;
using FDG.Stages;
using NUnit.Framework;

namespace FDG.Tests
{
    // #414 - every shop item either works or says why it doesn't.
    //
    // The shop sells 33 consumables and the engine plays 31 of them. The other two - Agility Injector and
    // Pheromone Grenade - were buyable, carriable, listed in the shop at 5c and 25c, and did nothing when
    // used. Skills have had a NotPossible ledger naming the ones this engine can't play, with the reason
    // shown in the hero's menu; items had no equivalent, so the only feedback was a generic "isn't in the
    // game yet" line that an unwired item and an impossible item shared.
    //
    // The assertion that matters is the TOTAL one below: every item is either implemented or explained.
    // A ledger on its own would go stale the moment an item was implemented or a new one imported, and a
    // test for just these two would say nothing about the next one.
    [TestFixture]
    public class QuestItemLedgerTests
    {
        private static bool IsImplemented(string itemName) =>
            QuestSkill.Find(QuestTreasure.ItemPrefix + itemName) != null;

        [Test]
        public void EveryShopItem_IsEitherImplementedOrExplained()
        {
            var silent = QuestItems.All
                .Where(i => !IsImplemented(i.Name) && !QuestItems.NotPossible.ContainsKey(i.Name))
                .Select(i => $"{i.Name} ({i.Cost}c)")
                .ToList();

            Assert.That(silent, Is.Empty,
                "these items are sold and do nothing, with no reason shown. Either wire them up, or add "
                + "them to QuestItems.NotPossible with the reason the player should see.");
        }

        [Test]
        public void TheLedgerNamesRealItems()
        {
            // A reason attached to a name no shop sells is never shown to anyone - a typo here reads as
            // a handled case and is one.
            foreach (string name in QuestItems.NotPossible.Keys)
            {
                Assert.That(QuestItems.All.Any(i => i.Name == name), Is.True,
                    $"QuestItems.NotPossible names \"{name}\", which is not a shop item");
            }
        }

        [Test]
        public void TheLedgerDoesNotCoverAnImplementedItem()
        {
            // The other way it goes stale: an item that has since been wired up would still be greyed
            // out in the menu with a reason that is no longer true.
            foreach (string name in QuestItems.NotPossible.Keys)
            {
                Assert.That(IsImplemented(name), Is.False,
                    $"\"{name}\" is implemented now - take it out of QuestItems.NotPossible");
            }
        }

        [Test]
        public void EveryReason_IsPlainEnglishAndAscii()
        {
            // The strings go straight into the hero's skill menu, where the ImGui font atlas has Basic
            // Latin and Latin-1 only, and where a player is mid-turn and wants one sentence.
            foreach ((string name, string why) in QuestItems.NotPossible)
            {
                Assert.That(why, Does.EndWith("."), $"{name}: a reason should read as a sentence");
                Assert.That(why.Length, Is.LessThan(120), $"{name}: too long for a menu line");
                Assert.That(why.All(c => c <= 0xFF), Is.True,
                    $"{name}: game text is ASCII/Latin-1 only - anything above U+00FF renders as '?'");
            }
        }
    }
}
