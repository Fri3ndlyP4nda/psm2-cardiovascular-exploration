using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Cardio.Core
{
    /// <summary>
    /// Locally persisted player progress (JSON on disk).
    ///
    /// Phase 1 scope: unlock/completion state and the last used profile name.
    /// Phase 7 will add the offline queue of SESSION_LOGS documents that
    /// SessionLogManager uploads once connectivity returns (PSM1 NFR4). The
    /// <see cref="PendingSessionLogs"/> list is already part of the save file
    /// format so that adding the sync layer does not invalidate save files
    /// produced during earlier testing.
    /// </summary>
    /// <summary>
    /// One finished level attempt, kept locally so the Phase 9 dashboard has a
    /// history to show.
    ///
    /// Deliberately NOT the same thing as <see cref="PlayerProgress.PendingSessionLogs"/>.
    /// That list is Phase 7's Supabase upload queue - things not yet sent. This
    /// one is the player's own record and is never drained. Merging them would
    /// mean the dashboard empties itself the first time a sync succeeds.
    ///
    /// The field list mirrors the PSM1 SESSION_LOGS document so Phase 7 can
    /// serialise from the same shape.
    /// </summary>
    [Serializable]
    public class SessionRecord
    {
        public string DateUtc = string.Empty;
        public string DisplayName = "Guest";
        public int Level;
        public int Score;
        public int PuzzlesAttempted;
        public int PuzzlesCorrect;
        public int IncorrectAnswers;
        public int PuzzlesFailed;
        public int HintsUsed;

        /// <summary>
        /// Times Blood Count reached zero during this attempt.
        ///
        /// This is the PSM1 report's FailedAttempts column. It is NOT
        /// IncorrectAnswers - that counts wrong answers. Added in Phase 7
        /// because the SESSION_LOGS schema needs it and nothing was carrying it
        /// out of LevelPerformance.
        /// </summary>
        public int LevelFailures;

        public int FinalDifficulty;
        public float AverageResponseSeconds;
        public float DurationSeconds;
        public bool Completed;

        public float Accuracy01 => PuzzlesAttempted <= 0 ? 0f : (float)PuzzlesCorrect / PuzzlesAttempted;
    }

    [Serializable]
    public class PlayerProgress
    {
        public string LastUserId = "guest";
        public string LastDisplayName = "Guest";

        /// <summary>Highest level the player may select. Level 1 is always available.</summary>
        public int HighestUnlockedLevel = 1;

        /// <summary>Level ids (ints) the player has finished at least once.</summary>
        public List<int> CompletedLevels = new List<int>();

        /// <summary>
        /// Session summaries that have not yet reached Supabase.
        ///
        /// The offline queue (PSM1 NFR4). Each entry is one serialised
        /// SESSION_LOGS row. Drained by SessionLogManager when a upload
        /// succeeds; never read by the dashboard - see SessionHistory below,
        /// which is a different list for a different purpose.
        /// </summary>
        public List<string> PendingSessionLogs = new List<string>();

        /// <summary>
        /// The anonymous Supabase user this install signs in as.
        ///
        /// Persisted so the same identity is reused across launches. Without it
        /// every launch would create a new anonymous user and strand the
        /// previous one's rows. See AuthenticationManager for the security
        /// trade-off of keeping the refresh token here.
        /// </summary>
        public string SupabaseUserId = string.Empty;
        public string SupabaseRefreshToken = string.Empty;

        /// <summary>Finished level attempts, newest last. Capped; see SaveManager.MaxSessionHistory.</summary>
        public List<SessionRecord> SessionHistory = new List<SessionRecord>();

        public int TotalSessionsPlayed;
    }

    [DisallowMultipleComponent]
    public class SaveManager : MonoBehaviour
    {
        private const string FileName = "psm2_progress.json";

        /// <summary>
        /// How many finished attempts the local history keeps.
        ///
        /// Bounded because this file is rewritten on every level completion and
        /// an unbounded list would grow without limit across a term of use. The
        /// dashboard shows the most recent handful; twenty is enough to see a
        /// trend without the file becoming something the player has to manage.
        /// </summary>
        public const int MaxSessionHistory = 20;

        [Header("Debug")]
        [Tooltip("Writes the save path to the Console on start so it is easy to find during testing.")]
        [SerializeField] private bool logSavePath = true;

        public PlayerProgress Progress { get; private set; } = new PlayerProgress();

        /// <summary>
        /// Raised after an attempt is appended to the local history.
        ///
        /// The upload path listens to this rather than being called by the
        /// tracker, so the Supabase layer stays optional: strip it out and the
        /// game still records everything locally, which is what the offline
        /// story requires anyway.
        /// </summary>
        public event System.Action<SessionRecord> SessionRecorded;

        /// <summary>Full path of the save file. Shown in the Settings panel for troubleshooting.</summary>
        public string SavePath => Path.Combine(Application.persistentDataPath, FileName);

        private void Awake()
        {
            Load();
            if (logSavePath) Debug.Log($"[SaveManager] Save file: {SavePath}");
        }

        public void Load()
        {
            try
            {
                if (File.Exists(SavePath))
                {
                    string json = File.ReadAllText(SavePath);
                    PlayerProgress loaded = JsonUtility.FromJson<PlayerProgress>(json);
                    if (loaded != null)
                    {
                        Progress = loaded;
                        // JsonUtility leaves null lists when a field was absent in an older file.
                        Progress.CompletedLevels ??= new List<int>();
                        Progress.PendingSessionLogs ??= new List<string>();
                        Progress.SupabaseUserId ??= string.Empty;
                        Progress.SupabaseRefreshToken ??= string.Empty;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                // A corrupt save must never stop the player from playing.
                Debug.LogWarning($"[SaveManager] Could not read save file, starting fresh. {e.Message}");
            }

            Progress = new PlayerProgress();
        }

        public void SaveNow()
        {
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(Progress, true));
            }
            catch (Exception e)
            {
                Debug.LogError($"[SaveManager] Failed to write save file: {e.Message}");
            }
        }

        /// <summary>True when the player has finished at least one level (enables "Continue").</summary>
        public bool HasProgress => Progress.CompletedLevels.Count > 0 || Progress.HighestUnlockedLevel > 1;

        public bool IsLevelUnlocked(LevelId level) => (int)level <= Progress.HighestUnlockedLevel;

        /// <summary>Records a completion and unlocks the following level.</summary>
        public void MarkLevelCompleted(LevelId level)
        {
            int id = (int)level;
            if (id <= 0) return;

            if (!Progress.CompletedLevels.Contains(id)) Progress.CompletedLevels.Add(id);
            if (id + 1 > Progress.HighestUnlockedLevel) Progress.HighestUnlockedLevel = Mathf.Min(id + 1, GameConstants.LevelScenes.Length);

            SaveNow();
        }

        /// <summary>
        /// Appends one finished attempt to the local history and persists it.
        ///
        /// Oldest entries are dropped once the cap is reached, so the newest
        /// record is always kept even when the list is full.
        /// </summary>
        public void AppendSessionRecord(SessionRecord record)
        {
            if (record == null) return;

            Progress.SessionHistory.Add(record);

            int excess = Progress.SessionHistory.Count - MaxSessionHistory;
            if (excess > 0) Progress.SessionHistory.RemoveRange(0, excess);

            SaveNow();
            SessionRecorded?.Invoke(record);
        }

        /// <summary>Most recent attempts, newest first, at most <paramref name="count"/>.</summary>
        public List<SessionRecord> RecentSessions(int count)
        {
            var recent = new List<SessionRecord>();
            List<SessionRecord> all = Progress.SessionHistory;

            for (int i = all.Count - 1; i >= 0 && recent.Count < count; i--)
            {
                if (all[i] != null) recent.Add(all[i]);
            }

            return recent;
        }

        /// <summary>Level the "Continue" button should resume at.</summary>
        public LevelId ResumeLevel()
        {
            int id = Mathf.Clamp(Progress.HighestUnlockedLevel, 1, GameConstants.LevelScenes.Length);
            return (LevelId)id;
        }

        public void ResetProgress()
        {
            Progress = new PlayerProgress();
            SaveNow();
        }

        private void OnApplicationQuit() => SaveNow();
    }
}
