using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;

namespace FDG.Campaign
{
    /// <summary>
    /// #448 - one mission's journal entry: the player's own words about that mission, whether written
    /// before it as a briefing or after it as an account of what happened.
    /// </summary>
    public sealed class CampaignMissionNote
    {
        /// <summary>The mission this note belongs to, counting from 1.</summary>
        public int Mission { get; set; }

        public string Text { get; set; } = "";
    }

    /// <summary>
    /// #448 - the campaign journal. Kai, 2026-11-03: <i>"I want to be able to keep notes on every mission,
    /// and an overarching note for the entire campaign. It'll be to keep notes of the story, like a
    /// journal entry or something... to give campaigns more of a story feel than just 'do this to get
    /// this'."</i> (He first asked on 2026-11-01; this is the second time.)
    ///
    /// <para>
    /// Two levels, both stored on the player's <see cref="StrikeTeamInfo"/>:
    /// <see cref="StrikeTeamInfo.Journal"/> for the campaign as a whole, and
    /// <see cref="StrikeTeamInfo.MissionNotes"/> with one entry per mission that has a note.
    /// </para>
    ///
    /// <para>
    /// <b>Deliberately not in <see cref="StrikeTeamInfo.History"/>.</b> History is the game's own log,
    /// written by <see cref="CampaignRules.AfterGame"/>, and <see cref="CampaignRules.RecordOf"/> reads it
    /// back to recover older campaigns' win counts. Mixing the player's prose into the same list would let
    /// one overwrite the other, and a journal line that happened to start "Won the mission on" would be
    /// counted as a win.
    /// </para>
    ///
    /// <para>
    /// Everything goes through <see cref="Clean"/> on the way in, because this is game text drawn by an
    /// ImGui font that only has Basic Latin and Latin-1 (CLAUDE.md): a pasted em dash or curly quote would
    /// otherwise show as '?' in the player's own journal.
    /// </para>
    /// </summary>
    public static class CampaignJournal
    {
        /// <summary>
        /// The longest a single note may be. Generous - several pages of writing - and only there because
        /// the ImGui text box needs a buffer size, and a pasted wall of text should be cut cleanly rather
        /// than by the widget.
        /// </summary>
        public const int MaxNoteLength = 8000;

        /// <summary>
        /// Readies text the player typed or pasted for storing: ASCII-folded (dashes, quotes and ellipses
        /// to their plain forms, accents dropped), Windows line endings made plain, trailing whitespace
        /// removed, and the length capped. Never returns null.
        /// </summary>
        public static string Clean(string? text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            string folded = OprBookImporter.AsciiFold(text)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .TrimEnd();
            return folded.Length <= MaxNoteLength ? folded : folded[..MaxNoteLength].TrimEnd();
        }

        /// <summary>Sets the campaign's overarching note. Blank clears it.</summary>
        public static void SetCampaignNote(StrikeTeamInfo info, string? text) => info.Journal = Clean(text);

        /// <summary>The note for one mission, or "" when it has none.</summary>
        public static string NoteFor(StrikeTeamInfo info, int mission) =>
            Notes(info).FirstOrDefault(n => n.Mission == mission)?.Text ?? "";

        /// <summary>
        /// Writes one mission's note. Blank removes the entry rather than storing an empty one, so a note
        /// that was typed and then deleted leaves nothing behind. Entries are kept in mission order.
        /// </summary>
        public static void SetNote(StrikeTeamInfo info, int mission, string? text)
        {
            if (mission < 1) throw new ArgumentOutOfRangeException(nameof(mission), "Missions count from 1.");

            List<CampaignMissionNote> notes = Notes(info);
            string clean = Clean(text);
            CampaignMissionNote? existing = notes.FirstOrDefault(n => n.Mission == mission);

            if (clean.Length == 0)
            {
                if (existing != null) notes.Remove(existing);
                return;
            }

            if (existing != null) existing.Text = clean;
            else notes.Add(new CampaignMissionNote { Mission = mission, Text = clean });
            notes.Sort((a, b) => a.Mission.CompareTo(b.Mission));
        }

        /// <summary>
        /// The missions the journal offers a page for: every mission played, and the next one so a
        /// briefing can be written before it is fought. Any mission that already has a note is included
        /// too, so nothing written is ever unreachable - a hand-edited file, say, or a note left on a
        /// mission number past the last one played.
        /// </summary>
        public static IReadOnlyList<int> Missions(StrikeTeamInfo info)
        {
            var missions = new SortedSet<int>();
            int last = Math.Max(0, info.Matches) + 1;
            for (int m = 1; m <= last; m++) missions.Add(m);
            foreach (CampaignMissionNote note in Notes(info))
                if (note.Mission >= 1) missions.Add(note.Mission);
            return missions.ToList();
        }

        /// <summary>
        /// Whether a mission has been played yet, which is the difference between a briefing and an
        /// account.
        /// </summary>
        public static bool IsPlayed(StrikeTeamInfo info, int mission) => mission <= info.Matches;

        /// <summary>
        /// The note list, created if a file written before #448 - or by hand, with an explicit null -
        /// left it missing.
        /// </summary>
        private static List<CampaignMissionNote> Notes(StrikeTeamInfo info) => info.MissionNotes ??= new();
    }
}
