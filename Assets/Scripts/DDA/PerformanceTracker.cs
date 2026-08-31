using System;
using System.Collections.Generic;
using Cardio.Core;
using Cardio.Data;
using Cardio.Gameplay;
using Cardio.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cardio.DDA
{
    /// <summary>
    /// The MEASURE stage of the PSM1 loop.
    ///
    /// It listens - it is never called by gameplay. PuzzleManager emits a
    /// <see cref="PuzzleResult"/> and forgets about it; PlayerHealth raises
    /// damage events; this class turns that stream into the aggregates the
    /// report and the Phase 4 DDA need. Nothing here decides anything about
    /// difficulty, which keeps the rule set out of the measurement code.
    ///
    /// It lives on the persistent [Cardio Systems] object so a session that
    /// spans several levels produces one record per level
    /// (<see cref="LevelPerformance"/>), which is exactly the granularity
    /// Firestore's SESSION_LOGS collection wants in Phase 7.
    ///
    /// Re-attachment is driven by <c>SceneManager.sceneLoaded</c> rather than by
    /// gameplay calling in, so the dependency arrow keeps pointing one way:
    /// gameplay -> measurement, never back.
    ///
    /// No player input is ever required to record any of this (PSM1 section 9).
    /// </summary>
    [DisallowMultipleComponent]
    public class PerformanceTracker : MonoBehaviour
    {
        public static PerformanceTracker Instance { get; private set; }

        [Header("Scoring")]
        [SerializeField] private ScoreSettings scoreSettings = new ScoreSettings();

        [Header("Recent-form window")]
        [Tooltip("How many recent puzzles the DDA's 'recent' figures are averaged over.")]
        [SerializeField, Range(2, 20)] private int recentWindowSize = 5;

        [Header("Debug")]
        [Tooltip("Logs every recorded result and the running aggregates to the Console.")]
        [SerializeField] private bool verboseLogging = true;

        [Header("Live metrics (read-only, populated during play)")]
        [SerializeField] private List<LevelPerformance> levelRecords = new List<LevelPerformance>();
        [SerializeField] private int consecutiveFailures;
        [SerializeField] private float displaySessionAccuracyPercent;
        [SerializeField] private float displayAverageResponseSeconds;

        // ---- Live subscriptions, cleared as scenes change ----
        private PuzzleManager _puzzleManager;
        private PlayerHealth _playerHealth;

        // ---- Recent-form ring ----
        private readonly List<PuzzleResult> _recent = new List<PuzzleResult>();

        private float _levelStartRealtime;
        private LevelId _activeLevel = LevelId.None;

        /// <summary>Raised after every recorded puzzle, once the aggregates are updated.</summary>
        public event Action<PerformanceSnapshot> MetricsUpdated;

        public ScoreSettings Scoring => scoreSettings;

        /// <summary>Per-level records for this session, in the order the levels were entered.</summary>
        public IReadOnlyList<LevelPerformance> LevelRecords => levelRecords;

        /// <summary>Current run of consecutive failed puzzles. The DDA's main distress signal.</summary>
        public int ConsecutiveFailures => consecutiveFailures;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            DetachFromScene();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            // sceneLoaded does not fire for a scene that was already active when
            // this object was created, so attach once explicitly.
            AttachToScene();
            HookGameManager();
        }

        // ------------------------------------------------------------------
        // Scene attachment
        // ------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            DetachFromScene();
            AttachToScene();
        }

        private void AttachToScene()
        {
            _puzzleManager = PuzzleManager.Instance;
            if (_puzzleManager != null)
            {
                _puzzleManager.PuzzleAnswered += OnPuzzleAnswered;
                _puzzleManager.AttemptSubmitted += OnAttemptSubmitted;
                _puzzleManager.HintRequested += OnHintRequested;
            }

            _playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (_playerHealth != null)
            {
                _playerHealth.Damaged += OnPlayerDamaged;
                _playerHealth.BloodCountChanged += OnBloodCountChanged;
            }
        }

        private void DetachFromScene()
        {
            if (_puzzleManager != null)
            {
                _puzzleManager.PuzzleAnswered -= OnPuzzleAnswered;
                _puzzleManager.AttemptSubmitted -= OnAttemptSubmitted;
                _puzzleManager.HintRequested -= OnHintRequested;
                _puzzleManager = null;
            }

            if (_playerHealth != null)
            {
                _playerHealth.Damaged -= OnPlayerDamaged;
                _playerHealth.BloodCountChanged -= OnBloodCountChanged;
                _playerHealth = null;
            }
        }

        private void HookGameManager()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            gm.StateChanged += OnGameStateChanged;
            gm.SessionChanged += OnSessionChanged;
        }

        /// <summary>
        /// Starts tracking a level when it becomes the current one.
        ///
        /// StateChanged alone is not enough. SetState ignores a transition to
        /// the state it is already in, so a level entered while already Playing
        /// - a restart, or replaying the same level - raised no event and
        /// BeginLevel never ran. That level then accumulated nothing, because
        /// FinishLevel early-returns on a None active level: its metrics and
        /// its dashboard record were both silently lost.
        ///
        /// NotifyLevelStarted always raises SessionChanged, so this fires even
        /// when the state does not change. Found by a Phase 9 test asserting
        /// that dying writes a history record.
        /// </summary>
        private void OnSessionChanged(SessionData session)
        {
            if (session == null) return;
            if (GameManager.Instance == null || GameManager.Instance.State != GameState.Playing) return;
            if (session.CurrentLevel == LevelId.None || session.CurrentLevel == _activeLevel) return;

            BeginLevel(session.CurrentLevel);
        }

        // ------------------------------------------------------------------
        // Level lifecycle
        // ------------------------------------------------------------------

        private void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.Playing when _activeLevel != CurrentLevel():
                    BeginLevel(CurrentLevel());
                    break;

                case GameState.LevelComplete:
                    FinishLevel(completed: true);
                    break;

                case GameState.GameOver:
                    RecordLevelFailure();
                    FinishLevel(completed: false);
                    break;
            }
        }

        private static LevelId CurrentLevel()
        {
            return GameManager.Instance != null ? GameManager.Instance.Session.CurrentLevel : LevelId.None;
        }

        private void BeginLevel(LevelId level)
        {
            _activeLevel = level;
            _levelStartRealtime = Time.unscaledTime;

            // Recent form is per level: carrying a previous level's streak into
            // a new environment would make the DDA's first decision there wrong.
            _recent.Clear();
            consecutiveFailures = 0;

            RecordFor(level);   // create the record up front so it shows in the Inspector
        }

        private void FinishLevel(bool completed)
        {
            if (_activeLevel == LevelId.None) return;

            LevelPerformance record = RecordFor(_activeLevel);
            record.DurationSeconds += Time.unscaledTime - _levelStartRealtime;
            record.Completed |= completed;
            record.FinalDifficulty = GameManager.Instance != null
                ? GameManager.Instance.Session.CurrentDifficulty
                : DifficultyTier.Easy;

            _levelStartRealtime = Time.unscaledTime;

            if (verboseLogging) Debug.Log($"[PerformanceTracker] Level finished - {record}");

            PersistSessionRecord(record);

            // The level is left "active" so a retry keeps accumulating into the
            // same record; BeginLevel only resets when the level actually changes.
            if (completed) _activeLevel = LevelId.None;
        }

        /// <summary>
        /// Writes one finished attempt into the local history the dashboard reads.
        ///
        /// This lives here rather than in GameManager because the architecture
        /// only lets metrics be written in one place, and because Core is not
        /// allowed to know about the DDA layer - so the tracker listens for the
        /// state change instead of being called by it, exactly as it already
        /// does for puzzles and health.
        /// </summary>
        private void PersistSessionRecord(LevelPerformance record)
        {
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save == null) return;

            SessionData session = GameManager.Instance.Session;

            save.AppendSessionRecord(new SessionRecord
            {
                DateUtc = System.DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm"),
                DisplayName = session != null ? session.DisplayName : "Guest",
                Level = (int)record.Level,
                Score = record.Score,
                PuzzlesAttempted = record.PuzzlesAttempted,
                PuzzlesCorrect = record.PuzzlesCorrect,
                IncorrectAnswers = record.IncorrectAnswers,
                PuzzlesFailed = record.PuzzlesFailed,
                HintsUsed = record.HintsUsed,
                FinalDifficulty = (int)record.FinalDifficulty,
                AverageResponseSeconds = record.AverageResponseSeconds,
                DurationSeconds = record.DurationSeconds,
                Completed = record.Completed
            });
        }

        // ------------------------------------------------------------------
        // Recording
        // ------------------------------------------------------------------

        /// <summary>Every individual submission, so wrong answers are counted even on a puzzle later solved.</summary>
        private void OnAttemptSubmitted(bool correct, int attemptNumber)
        {
            if (correct) return;

            LevelPerformance record = RecordFor(CurrentLevel());
            record.IncorrectAnswers++;
        }

        /// <summary>One resolved puzzle. This is the main measurement entry point.</summary>
        private void OnPuzzleAnswered(PuzzleResult result)
        {
            LevelPerformance record = RecordFor(result.Level != LevelId.None ? result.Level : CurrentLevel());

            record.PuzzlesAttempted++;
            record.TotalResponseSeconds += result.ResponseSeconds;

            // HintsUsed is NOT added from the result here - OnHintRequested has
            // already counted each one as it happened. Doing both would double it.

            if (result.Correct)
            {
                record.PuzzlesCorrect++;
                consecutiveFailures = 0;
            }
            else
            {
                record.PuzzlesFailed++;
                consecutiveFailures++;
                record.MaxConsecutiveFailures = Mathf.Max(record.MaxConsecutiveFailures, consecutiveFailures);
            }

            int points = ScoreRules.Calculate(result, scoreSettings);
            record.Score += points;

            PushRecent(result);
            PushToSession(record, points);

            PerformanceSnapshot snapshot = BuildSnapshot(record);
            RefreshInspectorMirrors(record);

            if (verboseLogging)
            {
                Debug.Log($"[PerformanceTracker] {result}  |  +{points} pts  |  {snapshot}");
            }

            MetricsUpdated?.Invoke(snapshot);
        }

        /// <summary>
        /// One hint reveal. Counted at the moment it happens rather than when
        /// the puzzle resolves, so hints taken on abandoned puzzles still show
        /// up in the PSM2 evaluation metrics.
        ///
        /// Requested and automatic hints are kept apart: comparing them is how
        /// the evaluation shows whether the DDA was actually carrying a
        /// struggling player, rather than the player asking for help.
        /// </summary>
        private void OnHintRequested(PuzzleData puzzle, HintSource source)
        {
            LevelPerformance record = RecordFor(CurrentLevel());
            var gm = GameManager.Instance;

            switch (source)
            {
                case HintSource.Requested:
                    record.HintsUsed++;
                    if (gm != null) gm.Session.HintsUsed++;
                    break;

                case HintSource.Automatic:
                    record.AutoHintsGiven++;
                    if (gm != null) gm.Session.AutoHintsGiven++;
                    break;

                case HintSource.Earned:
                    record.EarnedHints++;
                    if (gm != null) gm.Session.EarnedHints++;
                    break;
            }

            gm?.RaiseSessionChanged();
        }

        /// <summary>Records that a wrong answer produced a leukemic blast.</summary>
        public void RecordHostileSpawned()
        {
            RecordFor(CurrentLevel()).TotalHostilesSpawned++;

            var gm = GameManager.Instance;
            if (gm == null) return;

            gm.Session.TotalHostilesSpawned++;
            gm.RaiseSessionChanged();
        }

        /// <summary>
        /// Records a kill and applies its score effect: a cost while questions
        /// remain (the earned hint is not free), a bonus once they are all
        /// answered and there is nothing left to hint about.
        /// </summary>
        public void RecordHostileKilled(bool allQuestionsAnswered)
        {
            LevelPerformance record = RecordFor(CurrentLevel());
            record.TotalHostilesKilled++;

            int delta = allQuestionsAnswered
                ? scoreSettings.ClearedLevelKillBonus
                : -scoreSettings.HostileKillPenalty;

            record.Score = Mathf.Max(0, record.Score + delta);

            var gm = GameManager.Instance;
            if (gm == null) return;

            gm.Session.TotalHostilesKilled++;
            gm.Session.Score = Mathf.Max(0, gm.Session.Score + delta);
            gm.RaiseSessionChanged();

            if (verboseLogging)
            {
                Debug.Log($"[PerformanceTracker] Blast destroyed ({(allQuestionsAnswered ? "bonus" : "hint earned")}) " +
                          $"score {(delta >= 0 ? "+" : "")}{delta}.");
            }
        }

        private void OnPlayerDamaged(int amount)
        {
            RecordFor(CurrentLevel()).DamageTaken += amount;
        }

        private void OnBloodCountChanged(int current, int max)
        {
            LevelPerformance record = RecordFor(CurrentLevel());
            if (current < record.LowestBloodCount) record.LowestBloodCount = current;
        }

        private void RecordLevelFailure()
        {
            RecordFor(CurrentLevel()).LevelFailures++;
        }

        // ------------------------------------------------------------------
        // Aggregation
        // ------------------------------------------------------------------

        private void PushRecent(PuzzleResult result)
        {
            _recent.Add(result);
            while (_recent.Count > recentWindowSize) _recent.RemoveAt(0);
        }

        /// <summary>
        /// Mirrors the level aggregates onto SessionData, which is what the HUD
        /// and the end-of-level summary already read, and what Phase 7 uploads.
        /// </summary>
        private void PushToSession(LevelPerformance record, int pointsAwarded)
        {
            var gm = GameManager.Instance;
            if (gm == null) return;

            SessionData session = gm.Session;

            session.PuzzlesAttempted++;
            if (record != null && pointsAwarded > 0) session.Score += pointsAwarded;

            // Recompute the correct-count and time totals from the records so a
            // multi-level session stays consistent even after a scene reload.
            int correct = 0;
            float totalTime = 0f;
            int incorrect = 0;
            int failed = 0;

            foreach (LevelPerformance r in levelRecords)
            {
                correct += r.PuzzlesCorrect;
                totalTime += r.TotalResponseSeconds;
                incorrect += r.IncorrectAnswers;
                failed += r.PuzzlesFailed;
            }

            session.PuzzlesCorrect = correct;
            session.TotalResponseTimeSeconds = totalTime;
            session.IncorrectAnswers = incorrect;
            session.PuzzlesFailed = failed;
            session.MaxConsecutiveFailures = Mathf.Max(session.MaxConsecutiveFailures, consecutiveFailures);

            gm.RaiseSessionChanged();
        }

        private PerformanceSnapshot BuildSnapshot(LevelPerformance record)
        {
            int recentCorrect = 0;
            float recentTime = 0f;
            float recentPace = 0f;

            foreach (PuzzleResult r in _recent)
            {
                if (r.Correct) recentCorrect++;
                recentTime += r.ResponseSeconds;
                recentPace += ScoreRules.PaceRatio(r.ResponseSeconds, r.Complexity, scoreSettings);
            }

            int n = Mathf.Max(1, _recent.Count);

            return new PerformanceSnapshot(
                sessionAccuracy01: record.Accuracy01,
                recentAccuracy01: _recent.Count == 0 ? 0f : (float)recentCorrect / _recent.Count,
                sessionAverageResponse: record.AverageResponseSeconds,
                recentAverageResponse: recentTime / n,
                recentPaceRatio: recentPace / n,
                consecutiveFailures: consecutiveFailures,
                hintsUsed: record.HintsUsed,
                puzzlesResolved: record.PuzzlesAttempted,
                recentSampleSize: _recent.Count);
        }

        /// <summary>
        /// Latest measurements, for the Phase 4 DDAManager. Safe to call at any
        /// time; returns zeroed figures before the first puzzle is answered.
        /// </summary>
        public PerformanceSnapshot CurrentSnapshot() => BuildSnapshot(RecordFor(CurrentLevel()));

        private void RefreshInspectorMirrors(LevelPerformance record)
        {
            displaySessionAccuracyPercent = record.Accuracy01 * 100f;
            displayAverageResponseSeconds = record.AverageResponseSeconds;
        }

        // ------------------------------------------------------------------
        // Records
        // ------------------------------------------------------------------

        /// <summary>Finds or creates the record for a level.</summary>
        public LevelPerformance RecordFor(LevelId level)
        {
            foreach (LevelPerformance record in levelRecords)
            {
                if (record.Level == level) return record;
            }

            var created = new LevelPerformance { Level = level };
            levelRecords.Add(created);
            return created;
        }

        /// <summary>Clears everything. Called when a new session starts.</summary>
        public void ResetSession()
        {
            levelRecords.Clear();
            _recent.Clear();
            consecutiveFailures = 0;
            _activeLevel = LevelId.None;
            displaySessionAccuracyPercent = 0f;
            displayAverageResponseSeconds = 0f;
        }

        /// <summary>
        /// Dumps the whole session to the Console. Used in the demonstration to
        /// show that measurement happened without any manual data entry.
        /// </summary>
        public void LogSessionSummary()
        {
            var gm = GameManager.Instance;
            string header = gm != null
                ? $"Session {gm.Session.LogId} for {gm.Session.DisplayName} ({gm.Session.SessionDurationSeconds:0}s)"
                : "Session";

            var lines = new List<string> { $"[PerformanceTracker] {header}" };
            foreach (LevelPerformance record in levelRecords) lines.Add("   " + record);

            if (levelRecords.Count == 0) lines.Add("   (no levels played)");

            Debug.Log(string.Join("\n", lines));
        }
    }
}
