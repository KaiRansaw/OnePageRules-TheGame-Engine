using System.Text.Json;
using System.Text.Json.Serialization;

namespace FDG.Quest
{
    /// <summary>
    /// One mission of a chapter, as planned on the campaign's Structure screen (Batch E, 2026-11-07): its
    /// two objectives (or "roll when it starts"), the player's notes for it, and what happened each time it
    /// was played.
    /// </summary>
    public sealed class CampaignMission
    {
        /// <summary>Primary objective, 1-6 on the book's Primary Objective table; 0 = roll when it starts.</summary>
        public int Primary { get; set; }
        /// <summary>Secondary objective, 1-6 on the book's Secondary Objective table; 0 = roll when it starts.</summary>
        public int Secondary { get; set; }
        /// <summary>The player's own words: the briefing before the mission is played, their account of it
        /// afterwards. One field, so the briefing becomes the start of the account.</summary>
        public string Notes { get; set; } = "";
        /// <summary>What the game recorded each time this mission was played (a failed chapter restarts, so a
        /// mission can be played more than once). Oldest first.</summary>
        public List<CampaignMissionRecord> Records { get; set; } = new();

        /// <summary>True once the heroes have won this mission.</summary>
        [JsonIgnore]
        public bool Won => Records.Any(r => r.Outcome == "success");

        public CampaignMission Clone() => new()
        {
            Primary = Primary, Secondary = Secondary, Notes = Notes,
            Records = Records.Select(r => r.Clone()).ToList(),
        };
    }

    /// <summary>
    /// "What happened" in one played mission, written by the game when the mission ends - never typed in.
    /// Every line is taken from the finished game (its quest progress) or from the hero sheets before and
    /// after it, so nothing here is invented.
    /// </summary>
    public sealed class CampaignMissionRecord
    {
        /// <summary>"success" / "failed".</summary>
        public string Outcome { get; set; } = "";
        /// <summary>1 Beginner - 4 Legendary.</summary>
        public int Difficulty { get; set; }
        public int Primary { get; set; }
        public bool PrimaryDone { get; set; }
        /// <summary>0 when the mission ended before a secondary objective came up.</summary>
        public int Secondary { get; set; }
        public bool SecondaryDone { get; set; }
        public int Rounds { get; set; }
        /// <summary>Heroes who were knocked out.</summary>
        public List<string> KnockedOut { get; set; } = new();
        /// <summary>"Hero: Injury", for each injury gained in this mission.</summary>
        public List<string> Injuries { get; set; } = new();
        /// <summary>"Hero +N", XP gained per hero.</summary>
        public List<string> Xp { get; set; } = new();
        /// <summary>"Hero: Item", items a hero came back with that it did not take in.</summary>
        public List<string> Loot { get; set; } = new();
        /// <summary>"Hero +N", money gained per hero (hazard pay and found money).</summary>
        public List<string> Money { get; set; } = new();
        /// <summary>The random events rolled, in order ("33 - Dazing Spores").</summary>
        public List<string> Events { get; set; } = new();
        /// <summary>Optional challenges in play.</summary>
        public List<string> Challenges { get; set; } = new();
        /// <summary>When it was played (local time, "yyyy-MM-dd HH:mm").</summary>
        public string PlayedAt { get; set; } = "";

        public CampaignMissionRecord Clone() => new()
        {
            Outcome = Outcome, Difficulty = Difficulty, Primary = Primary, PrimaryDone = PrimaryDone,
            Secondary = Secondary, SecondaryDone = SecondaryDone, Rounds = Rounds,
            KnockedOut = KnockedOut.ToList(), Injuries = Injuries.ToList(), Xp = Xp.ToList(), Loot = Loot.ToList(),
            Money = Money.ToList(), Events = Events.ToList(), Challenges = Challenges.ToList(), PlayedAt = PlayedAt,
        };

        /// <summary>
        /// Builds the record from the finished mission's progress and each hero's sheet as it went in and as
        /// it came out. Pure, so it can be tested without a game.
        /// </summary>
        /// <param name="quest">The mission's quest progress at the end of the game.</param>
        /// <param name="heroes">Per hero: the sheet before the mission (null if it could not be read), the
        /// sheet after, and whether the hero ended the game knocked out.</param>
        /// <param name="outcome">"success" / "failed".</param>
        public static CampaignMissionRecord From(QuestProgress quest,
            IEnumerable<(QuestHero? Before, QuestHero After, bool KnockedOut)> heroes, string outcome, DateTime playedAt)
        {
            var record = new CampaignMissionRecord
            {
                Outcome = outcome,
                Difficulty = quest.Difficulty,
                Primary = quest.Primary,
                PrimaryDone = quest.PrimaryDone,
                Secondary = quest.Secondary,
                SecondaryDone = quest.SecondaryDone,
                Rounds = quest.RoundsDone,
                Events = (quest.EventLog ?? new List<string>()).ToList(),
                Challenges = (quest.Challenges ?? new List<string>()).ToList(),
                PlayedAt = playedAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture),
            };
            foreach ((QuestHero? before, QuestHero after, bool down) in heroes)
            {
                string name = after.Name;
                if (down) record.KnockedOut.Add(name);
                List<string> oldInjuries = before?.Injuries.ToList() ?? new List<string>();
                foreach (string injury in after.Injuries)
                    if (!oldInjuries.Remove(injury)) record.Injuries.Add($"{name}: {injury}");
                if (before == null) continue;
                int xp = after.Xp - before.Xp;
                if (xp > 0) record.Xp.Add($"{name} +{xp}");
                int money = after.Credits - before.Credits;
                if (money > 0) record.Money.Add($"{name} +{money}");
                List<string> oldItems = before.Items.ToList();
                foreach (string item in after.Items)
                    if (!oldItems.Remove(item)) record.Loot.Add($"{name}: {item}");
            }
            return record;
        }
    }

    /// <summary>One chapter of a campaign: its missions, played in order at one difficulty.</summary>
    public sealed class CampaignChapter
    {
        public int Number { get; set; }

        private int _missions;

        /// <summary>
        /// How many missions the chapter has. Since FileVersion 2 this is the length of
        /// <see cref="MissionPlan"/>; a version-1 file only has this number, and
        /// <see cref="QuestCampaign.Normalize"/> turns it into that many unplanned missions on load.
        /// </summary>
        public int Missions
        {
            get => MissionPlan.Count > 0 ? MissionPlan.Count : _missions;
            set => _missions = value;
        }

        /// <summary>The chapter's missions, in the order they are played.</summary>
        public List<CampaignMission> MissionPlan { get; set; } = new();

        /// <summary>1 Beginner - 4 Legendary; 0 until the chapter starts.</summary>
        public int Difficulty { get; set; }
        /// <summary>Difficulty planned on the Structure screen; 0 = pick it when the chapter starts.</summary>
        public int PlannedDifficulty { get; set; }
        /// <summary>The enemy army file the chapter is fought against; "" = pick it in the lobby.</summary>
        public string EnemyArmy { get; set; } = "";
        /// <summary>The enemy army's name, for showing when the file is not to hand.</summary>
        public string EnemyName { get; set; } = "";
        /// <summary>The player's theme note for the chapter.</summary>
        public string Theme { get; set; } = "";
        public int MissionsWon { get; set; }
        public bool Done { get; set; }
        /// <summary>Times the heroes had to restart it.</summary>
        public int Restarts { get; set; }

        public CampaignChapter Clone() => new()
        {
            Number = Number, Missions = _missions, MissionPlan = MissionPlan.Select(m => m.Clone()).ToList(),
            Difficulty = Difficulty, PlannedDifficulty = PlannedDifficulty, EnemyArmy = EnemyArmy,
            EnemyName = EnemyName, Theme = Theme, MissionsWon = MissionsWon, Done = Done, Restarts = Restarts,
        };
    }

    /// <summary>
    /// A Quest campaign (GF: Star Quest / AoF: Quest Campaign Builder v2.1.0): chapters of missions played by
    /// one party. Chapters are played in any order, but a started chapter must be finished first. The shop
    /// opens only before / after a chapter. Wounds and power carry between the missions of a chapter (heroes
    /// restore 3 of each after a mission); if every hero goes down the chapter restarts. Saved as a
    /// <c>.fdgcampaign</c> file.
    /// <para>
    /// FileVersion 2 (Batch E, 2026-11-07) adds the planned structure - per chapter a difficulty, an enemy
    /// army and a theme note; per mission its objectives, notes and the game's record of it - and the
    /// campaign's story page. A version-1 file loads as before: each chapter gets its count of unplanned
    /// missions ("roll when it starts") and no notes.
    /// </para>
    /// </summary>
    public sealed class QuestCampaign
    {
        public const string FileExtension = ".fdgcampaign";
        /// <summary>A saved campaign PLAN: the structure only (chapters, missions, notes), no party and no
        /// progress - "Save as a campaign file" / "Start from a saved campaign".</summary>
        public const string PlanExtension = ".fdgquestplan";
        public const int CurrentFileVersion = 2;

        public int FileVersion { get; set; } = CurrentFileVersion;
        public string Name { get; set; } = "";
        public string GameType { get; set; } = "GF Star Quest";
        /// <summary>The party's hero files.</summary>
        public List<string> HeroFiles { get; set; } = new();
        public List<CampaignChapter> Chapters { get; set; } = new();
        /// <summary>Index of the chapter being played, or -1 between chapters.</summary>
        public int CurrentChapter { get; set; } = -1;
        public List<string> History { get; set; } = new();
        /// <summary>The journal's first page: the campaign's story, in the player's words.</summary>
        public string Story { get; set; } = "";

        public bool Finished => Chapters.Count > 0 && Chapters.All(c => c.Done);
        public CampaignChapter? Active => CurrentChapter >= 0 && CurrentChapter < Chapters.Count ? Chapters[CurrentChapter] : null;

        /// <summary>The mission the party plays next: the active chapter's first unwon mission, or null
        /// between chapters.</summary>
        [JsonIgnore]
        public CampaignMission? NextMission =>
            Active is { } ch && ch.MissionsWon >= 0 && ch.MissionsWon < ch.MissionPlan.Count ? ch.MissionPlan[ch.MissionsWon] : null;

        /// <summary>True once any chapter has been started or finished - the structure can no longer be
        /// thrown away and rebuilt, only edited where it is still unplayed.</summary>
        [JsonIgnore]
        public bool HasStarted => CurrentChapter >= 0 || Chapters.Any(c => c.Done || c.Restarts > 0 || c.MissionsWon > 0);

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
        public string ToJson() => JsonSerializer.Serialize(this, Options);
        public static QuestCampaign? FromJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonSerializer.Deserialize<QuestCampaign>(json)?.Normalize(); } catch (JsonException) { return null; }
        }

        /// <summary>
        /// Brings a loaded file up to the current shape: a version-1 chapter's mission count becomes that many
        /// unplanned missions, chapter numbers follow their order, and nulls from a hand-edited file become
        /// empty lists. Returns this campaign.
        /// </summary>
        public QuestCampaign Normalize()
        {
            HeroFiles ??= new List<string>();
            Chapters ??= new List<CampaignChapter>();
            History ??= new List<string>();
            Story ??= "";
            for (int i = 0; i < Chapters.Count; i++)
            {
                CampaignChapter ch = Chapters[i];
                ch.Number = i + 1;
                ch.MissionPlan ??= new List<CampaignMission>();
                int wanted = ch.Missions;
                while (ch.MissionPlan.Count < Math.Max(1, wanted)) ch.MissionPlan.Add(new CampaignMission());
                foreach (CampaignMission m in ch.MissionPlan)
                {
                    m.Records ??= new List<CampaignMissionRecord>();
                    m.Notes ??= "";
                }
                ch.EnemyArmy ??= "";
                ch.EnemyName ??= "";
                ch.Theme ??= "";
            }
            FileVersion = CurrentFileVersion;
            return this;
        }

        /// <summary>
        /// "Generate everything": a new campaign of D3+2 chapters, each of D3+1 missions, every mission's
        /// primary and secondary objective rolled on the book's D6 tables.
        /// <para>
        /// #414. The missions roll was D3, which is the Beginner's Guide v2.0.1 wording. Campaign Builder
        /// v2.1.0: <i>"Each campaign is made up to D3+2 chapters, with chapters having D3+1 missions."</i>
        /// So a campaign was up to a third shorter than the book's, and a 1-mission chapter - which the
        /// book no longer allows at all - was the commonest single outcome.
        /// </para>
        /// </summary>
        public static QuestCampaign Create(string name, IEnumerable<string> heroFiles, Random rng)
        {
            var c = new QuestCampaign { Name = name, HeroFiles = heroFiles.ToList() };
            c.Generate(rng);
            c.History.Add($"Campaign started: {c.Chapters.Count} chapters, {c.Chapters.Sum(x => x.Missions)} missions.");
            return c;
        }

        /// <summary>"Build it myself": one chapter of one unplanned mission, to add to.</summary>
        public static QuestCampaign CreateEmpty(string name, IEnumerable<string> heroFiles)
        {
            var c = new QuestCampaign { Name = name, HeroFiles = heroFiles.ToList() };
            c.AddChapter();
            return c;
        }

        /// <summary>Replaces the whole structure with the book's roll (see <see cref="Create"/>). Only before the
        /// campaign has started; returns false and changes nothing after.</summary>
        public bool Generate(Random rng)
        {
            if (HasStarted) return false;
            int D3() => rng.Next(1, 4);
            int D6() => rng.Next(1, 7);
            Chapters.Clear();
            int chapters = D3() + 2;
            for (int i = 0; i < chapters; i++)
            {
                var ch = new CampaignChapter { Number = i + 1 };
                int missions = D3() + 1;
                for (int m = 0; m < missions; m++) ch.MissionPlan.Add(new CampaignMission { Primary = D6(), Secondary = D6() });
                Chapters.Add(ch);
            }
            return true;
        }

        /// <summary>Replaces the structure with a saved plan's (a copy, so editing one never edits the other).
        /// Only before the campaign has started.</summary>
        public bool ApplyPlan(QuestCampaign plan)
        {
            if (HasStarted) return false;
            QuestCampaign clean = plan.ToPlan();
            Chapters = clean.Chapters;
            if (Story.Trim().Length == 0) Story = clean.Story;
            Normalize();
            return true;
        }

        /// <summary>The structure alone - chapters, missions, objectives, notes, enemies, themes, story - with
        /// no party, no history and no progress. What "Save as a campaign file" writes.</summary>
        public QuestCampaign ToPlan()
        {
            var plan = new QuestCampaign { Name = Name, GameType = GameType, Story = Story };
            foreach (CampaignChapter ch in Chapters)
            {
                CampaignChapter copy = ch.Clone();
                copy.Difficulty = 0;
                copy.MissionsWon = 0;
                copy.Done = false;
                copy.Restarts = 0;
                foreach (CampaignMission m in copy.MissionPlan) m.Records.Clear();
                plan.Chapters.Add(copy);
            }
            return plan.Normalize();
        }

        // ── Editing the structure ────────────────────────────────────────────────────────────────────────

        /// <summary>A chapter's difficulty and enemy can be changed (and the chapter removed) until it starts.</summary>
        public bool CanEditChapter(int index) =>
            index >= 0 && index < Chapters.Count && !Chapters[index].Done && index != CurrentChapter;

        /// <summary>A mission's objectives can be changed (and the mission removed) until it has been played:
        /// in a finished chapter never; in the chapter being played, only the missions not yet won.</summary>
        public bool CanEditMission(int chapter, int mission)
        {
            if (chapter < 0 || chapter >= Chapters.Count) return false;
            CampaignChapter ch = Chapters[chapter];
            if (mission < 0 || mission >= ch.MissionPlan.Count || ch.Done) return false;
            return chapter != CurrentChapter || mission >= ch.MissionsWon;
        }

        /// <summary>Missions can be added to any chapter that is not finished.</summary>
        public bool CanAddMission(int chapter) => chapter >= 0 && chapter < Chapters.Count && !Chapters[chapter].Done;

        public void AddChapter()
        {
            var ch = new CampaignChapter { Number = Chapters.Count + 1 };
            ch.MissionPlan.Add(new CampaignMission());
            Chapters.Add(ch);
        }

        public bool RemoveChapter(int index)
        {
            if (!CanEditChapter(index) || Chapters.Count <= 1) return false;
            Chapters.RemoveAt(index);
            if (CurrentChapter > index) CurrentChapter--;
            for (int i = 0; i < Chapters.Count; i++) Chapters[i].Number = i + 1;
            return true;
        }

        public bool AddMission(int chapter)
        {
            if (!CanAddMission(chapter)) return false;
            Chapters[chapter].MissionPlan.Add(new CampaignMission());
            return true;
        }

        /// <summary>Removes an unplayed mission. A chapter always keeps at least one.</summary>
        public bool RemoveMission(int chapter, int mission)
        {
            if (!CanEditMission(chapter, mission) || Chapters[chapter].MissionPlan.Count <= 1) return false;
            Chapters[chapter].MissionPlan.RemoveAt(mission);
            return true;
        }

        /// <summary>Why the structure can't be played yet, or null.</summary>
        public string? StructureProblem()
        {
            if (Chapters.Count == 0) return "Add at least one chapter.";
            if (Chapters.Any(c => c.MissionPlan.Count == 0)) return "Every chapter needs at least one mission.";
            return null;
        }

        // ── Playing ──────────────────────────────────────────────────────────────────────────────────────

        public bool CanStart(int index) => CurrentChapter < 0 && index >= 0 && index < Chapters.Count && !Chapters[index].Done;

        /// <summary>Starts a chapter. A difficulty planned on the Structure screen wins over
        /// <paramref name="difficulty"/>.</summary>
        public void StartChapter(int index, int difficulty)
        {
            if (!CanStart(index)) return;
            CurrentChapter = index;
            CampaignChapter ch = Chapters[index];
            ch.Difficulty = Math.Clamp(ch.PlannedDifficulty > 0 ? ch.PlannedDifficulty : difficulty, 1, 4);
            ch.MissionsWon = 0;
            History.Add($"Chapter {ch.Number} started (difficulty {ch.Difficulty}).");
        }

        /// <summary>Records the result of the chapter's mission, with the game's record of it when there is one.
        /// Returns true when the chapter just ended (finished or failed back to the start).</summary>
        public bool RecordMission(string outcome, CampaignMissionRecord? record = null)
        {
            CampaignChapter? ch = Active;
            if (ch == null) return false;
            if (record != null && NextMission is { } played) played.Records.Add(record);
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
