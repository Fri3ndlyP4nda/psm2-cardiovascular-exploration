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
    /// FirestoreManager uploads once connectivity returns (PSM1 NFR4). The
    /// <see cref="PendingSessionLogs"/> list is already part of the save file
    /// format so that adding the sync layer does not invalidate save files
    /// produced during earlier testing.
    /// </summary>
    [Serializable]
    public class PlayerProgress
    {
        public string LastUserId = "guest";
        public string LastDisplayName = "Guest";

        /// <summary>Highest level the player may select. Level 1 is always available.</summary>
        public int HighestUnlockedLevel = 1;

        /// <summary>Level ids (ints) the player has finished at least once.</summary>
        public List<int> CompletedLevels = new List<int>();

        /// <summary>Session summaries that have not yet reached Firestore. Populated in Phase 7.</summary>
        public List<string> PendingSessionLogs = new List<string>();

        public int TotalSessionsPlayed;
    }

    [DisallowMultipleComponent]
    public class SaveManager : MonoBehaviour
    {
        private const string FileName = "psm2_progress.json";

        [Header("Debug")]
        [Tooltip("Writes the save path to the Console on start so it is easy to find during testing.")]
        [SerializeField] private bool logSavePath = true;

        public PlayerProgress Progress { get; private set; } = new PlayerProgress();

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
