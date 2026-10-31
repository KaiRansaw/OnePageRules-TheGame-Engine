using System.Text.Json;

namespace FDG.Quest
{
    /// <summary>One chapter of a campaign: D3 missions at a difficulty chosen when the chapter starts.</summary>
    public sealed class CampaignChapter
    {
        public int Number { get; set; }
        public int Missions { get; set; }
        /// <summary>1 Beginner - 4 Legendary; 0 until the chapter starts.</summary>
        public int Difficulty { get; set; }
        public int MissionsWon { get; set; }
        public bool Done { get; set; }
        /// <summary>Times the heroes had to restart it.</summary>
        public int Restarts { get; set; }
    }

    /// <summary>
    /// A GF: Star Quest campaign (Beginner's Guide v2.0.1, Campaign Builder): D3+2 chapters of D3 missions each.
    /// Chapters are played in any order, but a started chapter must be finished first. The shop opens only
    /// before / after a chapter. Wounds and power carry between the missions of a chapter (heroes restore 3 of
    /// each after a mission); if every hero goes down the chapter restarts. Saved as a <c>.fdgcampaign</c> file.
    /// </summary>
    public sealed class QuestCampaign
    {
        public const string FileExtension = ".fdgcampaign";

        public int FileVersion { get; set; } = 1;
        public string Name { get; set; } = "";
        public string GameType { get; set; } = "GF Star Quest";
        /// <summary>The party's hero files.</summary>
        public List<string> HeroFiles { get; set; } = new();
        public List<CampaignChapter> Chapters { get; set; } = new();
        /// <summary>Index of the chapter being played, or -1 between chapters.</summary>
        public int CurrentChapter { get; set; } = -1;
        public List<string> History { get; set; } = new();

        public bool Finished => Chapters.Count > 0 && Chapters.All(c => c.Done);
        public CampaignChapter? Active => CurrentChapter >= 0 && CurrentChapter < Chapters.Count ? Chapters[CurrentChapter] : null;

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        public string ToJson() => JsonSerializer.Serialize(this, Options);
        public static QuestCampaign? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<QuestCampaign>(json); } catch (JsonException) { return null; }
        }

        /// <summary>
        /// A new campaign: D3+2 chapters, each of D3+1 missions.
        /// <para>
        /// #414. The missions roll was D3, which is the Beginner's Guide v2.0.1 wording. Campaign Builder
        /// v2.1.0: <i>"Each campaign is made up to D3+2 chapters, with chapters having D3+1 missions."</i>
        /// So a campaign was up to a third shorter than the book's, and a 1-mission chapter - which the
        /// book no longer allows at all - was the commonest single outcome.
        /// </para>
        /// </summary>
        public static QuestCampaign Create(string name, IEnumerable<string> heroFiles, Random rng)
        {
            int D3() => rng.Next(1, 4);
            var c = new QuestCampaign { Name = name, HeroFiles = heroFiles.ToList() };
            int chapters = D3() + 2;
            for (int i = 0; i < chapters; i++) c.Chapters.Add(new CampaignChapter { Number = i + 1, Missions = D3() + 1 });
            c.History.Add($"Campaign started: {chapters} chapters, {c.Chapters.Sum(x => x.Missions)} missions.");
            return c;
        }

        public bool CanStart(int index) => CurrentChapter < 0 && index >= 0 && index < Chapters.Count && !Chapters[index].Done;

        public void StartChapter(int index, int difficulty)
        {
            if (!CanStart(index)) return;
            CurrentChapter = index;
            Chapters[index].Difficulty = Math.Clamp(difficulty, 1, 4);
            Chapters[index].MissionsWon = 0;
            History.Add($"Chapter {Chapters[index].Number} started (difficulty {Chapters[index].Difficulty}).");
        }

        /// <summary>Records the result of the chapter's mission. Returns true when the chapter just ended
        /// (finished or failed back to the start).</summary>
        public bool RecordMission(string outcome)
        {
            CampaignChapter? ch = Active;
            if (ch == null) return false;
            if (outcome == "success")
            {
                ch.MissionsWon++;
                History.Add($"Chapter {ch.Number}: mission {ch.MissionsWon} of {ch.Missions} complete.");
                if (ch.MissionsWon >= ch.Missions)
                {
                    ch.Done = true;
                    CurrentChapter = -1;
                    History.Add(Finished ? "Campaign complete!" : $"Chapter {ch.Number} complete.");
                    return true;
                }
                return false;
            }
            ch.MissionsWon = 0;
            ch.Restarts++;
            History.Add($"Chapter {ch.Number}: the heroes fell - the chapter restarts.");
            return true;
        }
    }
}
