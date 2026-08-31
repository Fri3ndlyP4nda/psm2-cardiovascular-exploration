using System;
using UnityEngine;

namespace Cardio.Core
{
    /// <summary>
    /// Runtime container for everything that describes the *current play session*.
    /// It survives scene loads because it lives on the persistent GameManager.
    ///
    /// PSM1 mapping: this object is the in-memory shape of the SESSION_LOGS
    /// SESSION_LOGS row. Phase 3 (PerformanceTracker) writes the accuracy
    /// and response-time fields; Phase 7 (SessionLogManager) uploads it.
    /// The fields exist now so that later phases only add producers, never
    /// have to reshape the data model.
    /// </summary>
    [Serializable]
    public class SessionData
    {
        // ---- Identity ----
        public string LogId;
        public string UserId = "guest";
        public string DisplayName = "Guest";

        // ---- Progress ----
        public LevelId CurrentLevel = LevelId.None;
        public DifficultyTier StartingDifficulty = DifficultyTier.Easy;
        public DifficultyTier CurrentDifficulty = DifficultyTier.Easy;

        // ---- Metrics (written by PerformanceTracker from Phase 3 onwards) ----
        public int Score;

        /// <summary>Hints the player asked for.</summary>
        public int HintsUsed;

        /// <summary>Hints the DDA offered unprompted (Phase 4). Not penalised in the score.</summary>
        public int AutoHintsGiven;

        /// <summary>Hints earned by destroying a leukemic blast. The kill carries the cost, not the hint.</summary>
        public int EarnedHints;

        /// <summary>Leukemic blasts spawned by wrong answers this session.</summary>
        public int TotalHostilesSpawned;

        /// <summary>Leukemic blasts destroyed this session.</summary>
        public int TotalHostilesKilled;

        /// <summary>
        /// Times Blood Count reached zero. Named LevelFailures rather than the
        /// PSM1 report's "FailedAttempts" because that phrase is ambiguous once
        /// puzzles exist - see <see cref="IncorrectAnswers"/> and
        /// <see cref="PuzzlesFailed"/>, which are the puzzle-side counters.
        /// </summary>
        public int LevelFailures;

        public int PuzzlesAttempted;
        public int PuzzlesCorrect;

        /// <summary>Every wrong submission, including repeat attempts on one puzzle.</summary>
        public int IncorrectAnswers;

        /// <summary>Puzzles that used up every attempt without being solved.</summary>
        public int PuzzlesFailed;

        /// <summary>Longest run of consecutive failed puzzles seen this session.</summary>
        public int MaxConsecutiveFailures;

        public float TotalResponseTimeSeconds;

        // ---- Timing ----
        public string SessionDateUtc;
        [NonSerialized] public float SessionStartRealtime;

        public SessionData()
        {
            LogId = Guid.NewGuid().ToString("N");
            SessionDateUtc = DateTime.UtcNow.ToString("o");
            SessionStartRealtime = Time.realtimeSinceStartup;
        }

        /// <summary>Session length in seconds. Used by the PSM2 evaluation metrics.</summary>
        public float SessionDurationSeconds => Mathf.Max(0f, Time.realtimeSinceStartup - SessionStartRealtime);

        /// <summary>
        /// Ratio of correct puzzle answers, 0..1. Returns 0 while no puzzle has been
        /// attempted so the HUD never shows a misleading 100%.
        /// </summary>
        public float Accuracy01 => PuzzlesAttempted <= 0 ? 0f : (float)PuzzlesCorrect / PuzzlesAttempted;

        /// <summary>Mean seconds per answered puzzle. 0 while nothing has been answered.</summary>
        public float AverageResponseTime => PuzzlesAttempted <= 0 ? 0f : TotalResponseTimeSeconds / PuzzlesAttempted;
    }
}
