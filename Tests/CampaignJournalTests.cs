using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FDG.Campaign;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FDG.Tests
{
    /// <summary>
    /// #448 - the campaign journal: an overarching note for the campaign and one per mission (Kai,
    /// 2026-11-01 and again 2026-11-03).
    /// </summary>
    [TestFixture]
    public class CampaignJournalTests
    {
        private static StrikeTeamInfo Info(int matches = 0) => new() { Name = "Iron Reavers", Matches = matches };

        [Test]
        public void TheCampaignNoteIsStored()
        {
            StrikeTeamInfo info = Info();
            CampaignJournal.SetCampaignNote(info, "The Reavers owe the Choir a debt of blood.");
            Assert.That(info.Journal, Is.EqualTo("The Reavers owe the Choir a debt of blood."));
        }

        [Test]
        public void AMissionNoteIsStoredAndReadBack()
        {
            StrikeTeamInfo info = Info(matches: 2);
            CampaignJournal.SetNote(info, 2, "Vargas held the bridge alone.");

            Assert.That(CampaignJournal.NoteFor(info, 2), Is.EqualTo("Vargas held the bridge alone."));
            Assert.That(CampaignJournal.NoteFor(info, 1), Is.Empty, "other missions are untouched.");
        }

        [Test]
        public void RewritingANoteReplacesItRatherThanAddingASecond()
        {
            StrikeTeamInfo info = Info(matches: 1);
            CampaignJournal.SetNote(info, 1, "First draft.");
            CampaignJournal.SetNote(info, 1, "Second draft.");

            Assert.That(info.MissionNotes, Has.Count.EqualTo(1));
            Assert.That(CampaignJournal.NoteFor(info, 1), Is.EqualTo("Second draft."));
        }

        [Test]
        public void ClearingANoteRemovesTheEntry()
        {
            StrikeTeamInfo info = Info(matches: 1);
            CampaignJournal.SetNote(info, 1, "Something.");
            CampaignJournal.SetNote(info, 1, "   ");

            Assert.That(info.MissionNotes, Is.Empty, "a deleted note must not leave an empty entry behind.");
        }

        [Test]
        public void NotesStayInMissionOrderWhateverOrderTheyAreWritten()
        {
            StrikeTeamInfo info = Info(matches: 3);
            CampaignJournal.SetNote(info, 3, "c");
            CampaignJournal.SetNote(info, 1, "a");
            CampaignJournal.SetNote(info, 2, "b");

            Assert.That(info.MissionNotes.Select(n => n.Mission), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public void MissionNumbersStartAtOne()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => CampaignJournal.SetNote(Info(), 0, "x"));
        }

        /// <summary>
        /// Every played mission gets a page, plus the next one so a briefing can be written before it is
        /// fought - which is the "before or after" question left open for Kai answered by allowing both.
        /// </summary>
        [Test]
        public void TheJournalOffersEveryPlayedMissionAndTheNextOne()
        {
            Assert.That(CampaignJournal.Missions(Info(matches: 0)), Is.EqualTo(new[] { 1 }),
                "a brand new campaign can already have its first briefing.");
            Assert.That(CampaignJournal.Missions(Info(matches: 3)), Is.EqualTo(new[] { 1, 2, 3, 4 }));

            Assert.That(CampaignJournal.IsPlayed(Info(matches: 3), 3), Is.True);
            Assert.That(CampaignJournal.IsPlayed(Info(matches: 3), 4), Is.False, "mission 4 is next, not played.");
        }

        [Test]
        public void ANoteOnAMissionPastTheEndIsStillReachable()
        {
            StrikeTeamInfo info = Info(matches: 1);
            info.MissionNotes.Add(new CampaignMissionNote { Mission = 9, Text = "hand-edited" });

            Assert.That(CampaignJournal.Missions(info), Does.Contain(9),
                "anything written must stay reachable, or it is lost without being deleted.");
        }

        /// <summary>
        /// The game's font has Basic Latin and Latin-1 only (CLAUDE.md), so a pasted em dash or curly
        /// quote would show as '?' in the player's own writing. Folded on the way in instead.
        /// </summary>
        [Test]
        public void PastedTypographyIsMadeReadable()
        {
            StrikeTeamInfo info = Info();
            CampaignJournal.SetCampaignNote(info, "They came at dawn — “no quarter”…");

            Assert.That(info.Journal, Is.EqualTo("They came at dawn - \"no quarter\"..."));
            Assert.That(info.Journal.All(c => c <= 127), Is.True);
        }

        [Test]
        public void LineBreaksSurviveAndWindowsEndingsAreNormalised()
        {
            StrikeTeamInfo info = Info();
            CampaignJournal.SetCampaignNote(info, "Day one.\r\n\r\nDay two.\r\n   ");

            Assert.That(info.Journal, Is.EqualTo("Day one.\n\nDay two."),
                "paragraphs are the point of a journal; only the trailing whitespace goes.");
        }

        [Test]
        public void AnOverlongNoteIsCutCleanly()
        {
            StrikeTeamInfo info = Info(matches: 1);
            CampaignJournal.SetNote(info, 1, new string('x', CampaignJournal.MaxNoteLength + 500));

            Assert.That(CampaignJournal.NoteFor(info, 1).Length, Is.EqualTo(CampaignJournal.MaxNoteLength));
        }

        /// <summary>
        /// The journal must not be mixed into History: RecordOf reads History back to recover older
        /// campaigns' win counts, and a note that happened to start with the winning line's wording would
        /// be counted as a win.
        /// </summary>
        [Test]
        public void JournalTextNeverReachesTheHistoryOrTheRecord()
        {
            StrikeTeamInfo info = Info(matches: 2);
            CampaignJournal.SetNote(info, 1, "Won the mission on Brutal, or so the Reavers like to tell it.");
            CampaignJournal.SetCampaignNote(info, "Won the mission on Normal: a lie told around the fire.");

            Assert.That(info.History, Is.Empty);
            Assert.That(CampaignRules.RecordOf(info), Is.EqualTo((0, 2)),
                "prose that looks like a log line must not count as a win.");
        }

        /// <summary>
        /// A campaign file is System.Text.Json with the game's own options. Both new fields must survive
        /// the trip, and a file written before them - or with an explicit null - must still load and
        /// accept notes.
        /// </summary>
        [Test]
        public void TheJournalRoundTripsAndOldFilesStillLoad()
        {
            var army = new ArmyListFile { Name = "Iron Reavers", StrikeTeam = Info(matches: 1) };
            CampaignJournal.SetCampaignNote(army.StrikeTeam!, "Premise.");
            CampaignJournal.SetNote(army.StrikeTeam!, 1, "Mission one.");

            string json = JsonSerializer.Serialize(army, RuleJson.Options);
            ArmyListFile? back = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options);
            Assert.That(back!.StrikeTeam!.Journal, Is.EqualTo("Premise."));
            Assert.That(CampaignJournal.NoteFor(back.StrikeTeam, 1), Is.EqualTo("Mission one."));

            // An explicit null, which is the worst an old or hand-edited file can do - and with the
            // campaign note removed outright, as in a file written before #448.
            var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var team = node["strikeTeam"]!.AsObject();
            Assert.That(team.ContainsKey("missionNotes"), Is.True, "the property name moved; update this test.");
            team["missionNotes"] = null;
            team.Remove("journal");
            ArmyListFile? old = JsonSerializer.Deserialize<ArmyListFile>(node.ToJsonString(), RuleJson.Options);
            Assert.That(old!.StrikeTeam!.Journal, Is.Empty, "a missing campaign note reads as empty, not null.");
            Assert.That(CampaignJournal.NoteFor(old!.StrikeTeam!, 1), Is.Empty);
            CampaignJournal.SetNote(old.StrikeTeam!, 1, "Still writable.");
            Assert.That(CampaignJournal.NoteFor(old.StrikeTeam!, 1), Is.EqualTo("Still writable."));
        }
    }
}
