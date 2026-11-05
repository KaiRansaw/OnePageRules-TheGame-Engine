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

        /// <summary>
        /// #450 - the game's own account of the mission, written when its result comes in: who it was
        /// fought against, the result and rewards, casualties, XP and level-ups, random events and the
        /// special objective. Kai, 2026-11-05: yes to the game pre-filling the journal from what happened.
        /// <para>
        /// Kept apart from <see cref="Text"/> rather than pasted into it, so the game never overwrites what
        /// the player wrote and the player cannot delete the record by clearing their own page. Empty for
        /// a mission not yet played, and for missions played before #450.
        /// </para>
        /// </summary>
        public List<string> Record { get; set; } = new();
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
                if (existing == null) return;
                existing.Text = "";
                // An entry that still carries the game's record stays: clearing the player's page must
                // not take the account of the battle with it.
                if (existing.Record is not { Count: > 0 }) notes.Remove(existing);
                return;
            }

            Entry(info, mission).Text = clean;
        }

        /// <summary>The game's record of one mission, or an empty list.</summary>
        public static IReadOnlyList<string> RecordFor(StrikeTeamInfo info, int mission) =>
            Notes(info).FirstOrDefault(n => n.Mission == mission)?.Record ?? new List<string>();

        /// <summary>
        /// #450 - writes the game's record of a mission, replacing any record already there (a mission's
        /// result arrives once). Lines are cleaned like everything else in the journal, and blank ones
        /// dropped. Called by <see cref="CampaignRules.AfterGame"/>; the player has no way to call it.
        /// </summary>
        public static void SetRecord(StrikeTeamInfo info, int mission, IEnumerable<string> lines)
        {
            if (mission < 1) throw new ArgumentOutOfRangeException(nameof(mission), "Missions count from 1.");
            Entry(info, mission).Record = lines.Select(Clean).Where(l => l.Length > 0).ToList();
        }

        /// <summary>
        /// #450 - adds a line to a mission's record that only the caller knows - the opponent's name and
        /// the special objective are worked out after <see cref="CampaignRules.AfterGame"/>, where every
        /// army that played is in view. <paramref name="first"/> puts it at the top.
        /// </summary>
        public static void AddToRecord(StrikeTeamInfo info, int mission, string line, bool first = false)
        {
            if (mission < 1) throw new ArgumentOutOfRangeException(nameof(mission), "Missions count from 1.");
            string clean = Clean(line);
            if (clean.Length == 0) return;
            CampaignMissionNote entry = Entry(info, mission);
            entry.Record ??= new();
            if (first) entry.Record.Insert(0, clean);
            else entry.Record.Add(clean);
        }

        /// <summary>A mission's entry, created (in mission order) if it does not exist yet.</summary>
        private static CampaignMissionNote Entry(StrikeTeamInfo info, int mission)
        {
            List<CampaignMissionNote> notes = Notes(info);
            CampaignMissionNote? entry = notes.FirstOrDefault(n => n.Mission == mission);
            if (entry != null) return entry;

            entry = new CampaignMissionNote { Mission = mission };
            notes.Add(entry);
            notes.Sort((a, b) => a.Mission.CompareTo(b.Mission));
            return entry;
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
        /// #450 - the whole journal as one plain-text document a person can read outside the game: the
        /// campaign's name, score and record, its own page, then every mission that has anything on it,
        /// with the game's record above the player's words. Kai, 2026-11-05: yes to an exportable log.
        /// <para>
        /// Plain text with simple underlined headings rather than any markup, so it reads the same in
        /// Notepad, a phone, or pasted into a message.
        /// </para>
        /// </summary>
        /// <param name="you">The player's campaign block, which owns the journal.</param>
        /// <param name="rival">The standing enemy's block, or null when there is none.</param>
        /// <param name="gameName">The game's display name, e.g. "Grimdark Future".</param>
        public static string Export(StrikeTeamInfo you, StrikeTeamInfo? rival, string gameName)
        {
            var sb = new System.Text.StringBuilder();
            void Heading(string text, char rule)
            {
                sb.AppendLine(text);
                sb.AppendLine(new string(rule, text.Length));
            }

            Heading(rival != null ? $"{you.Name} vs {rival.Name}" : you.Name, '=');
            CampaignPreGame.CampaignStanding standing = CampaignPreGame.Standing(you, rival);
            (int wins, int losses) = CampaignRules.RecordOf(you);
            sb.AppendLine($"{gameName} campaign - {standing.Describe()}");
            sb.AppendLine($"{you.Name}: {standing.YourVp} VP, {wins} won / {losses} lost");
            if (rival != null)
            {
                (int rw, int rl) = CampaignRules.RecordOf(rival);
                sb.AppendLine($"{rival.Name}: {standing.RivalVp} VP, {rw} won / {rl} lost");
            }
            if (standing.IsOver)
                sb.AppendLine(standing.Outcome switch
                {
                    ECampaignOutcome.YouWon => $"The campaign was won by {you.Name}.",
                    ECampaignOutcome.RivalWon => $"The campaign was won by {rival?.Name ?? "the enemy"}.",
                    _ => "The campaign ended in a draw.",
                });

            if (you.Journal.Length > 0)
            {
                sb.AppendLine();
                Heading("The campaign", '-');
                sb.AppendLine(you.Journal);
            }

            foreach (int mission in Missions(you))
            {
                IReadOnlyList<string> record = RecordFor(you, mission);
                string note = NoteFor(you, mission);
                if (record.Count == 0 && note.Length == 0) continue;

                sb.AppendLine();
                Heading($"Mission {mission}" + (IsPlayed(you, mission) ? "" : " (briefing - not yet played)"), '-');
                foreach (string line in record) sb.AppendLine("  * " + line);
                if (record.Count > 0 && note.Length > 0) sb.AppendLine();
                if (note.Length > 0) sb.AppendLine(note);
            }

            return sb.ToString().Replace("\r\n", "\n");
        }

        /// <summary>
        /// The note list, created if a file written before #448 - or by hand, with an explicit null -
        /// left it missing.
        /// </summary>
        private static List<CampaignMissionNote> Notes(StrikeTeamInfo info) => info.MissionNotes ??= new();
    }
}
