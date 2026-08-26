using System;
using System.Collections.Generic;
using Cardio.Core;
using Cardio.Data;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Runs one puzzle at a time: presents it, times it, counts attempts,
    /// validates the answer and reports the outcome.
    ///
    /// It owns the *timing and attempt bookkeeping* but not the *analysis* -
    /// it emits a <see cref="PuzzleResult"/> and stops caring. Phase 3's
    /// PerformanceTracker subscribes to compute accuracy and response-time
    /// aggregates; Phase 4's DDAManager then drives <see cref="MaxComplexity"/>
    /// and <see cref="HintsAlwaysAvailable"/> back the other way.
    ///
    /// Validation itself lives on <see cref="PuzzleData"/>, so this class never
    /// grows a switch over puzzle types to decide correctness.
    /// </summary>
    public class PuzzleManager : MonoBehaviour
    {
        public static PuzzleManager Instance { get; private set; }

        [Header("Content")]
        [SerializeField] private QuestionBank bank;

        [Header("UI")]
        [SerializeField] private PuzzleUI puzzleUI;

        [Header("Rules")]
        [Tooltip("Incorrect answers allowed before the puzzle resolves as failed and reveals the answer.")]
        [SerializeField, Range(1, 10)] private int maxAttempts = 3;

        [Tooltip("Seconds the explanation stays on screen after a puzzle resolves.")]
        [SerializeField, Range(0.5f, 10f)] private float explanationDuration = 3.5f;

        [Header("Difficulty hooks (driven by DDAManager in Phase 4)")]
        [Tooltip("Puzzles above this complexity are not offered. 3 = no filtering.")]
        [SerializeField, Range(1, 3)] private int maxComplexity = 3;

        [Tooltip("When true, a wrong answer spawns a leukemic blast carrying that question's hint. The DDA controls this.")]
        [SerializeField] private bool hostileSpawningEnabled = true;

        // ---- Live puzzle state ----
        private PuzzleData _current;
        private float _startTime;
        private int _attempts;
        private int _hintsUsed;
        private int _autoHints;
        private float _closeAtTime = -1f;

        /// <summary>
        /// True once the puzzle has produced its result but before the panel has
        /// finished showing the explanation. Answers are refused in this window.
        /// </summary>
        private bool _resolved;

        private readonly HashSet<string> _solved = new HashSet<string>();

        /// <summary>Questions whose hint has been earned in combat and stays available.</summary>
        private readonly HashSet<string> _bankedHints = new HashSet<string>();

        /// <summary>Raised when a puzzle appears on screen.</summary>
        public event Action<PuzzleData> PuzzlePresented;

        /// <summary>Raised once per puzzle, when it resolves as correct or failed.</summary>
        public event Action<PuzzleResult> PuzzleAnswered;

        /// <summary>Raised after every individual answer, correct or not. (submittedCorrect, attemptNumber)</summary>
        public event Action<bool, int> AttemptSubmitted;

        /// <summary>
        /// Raised each time a hint is revealed, tagged with where it came from.
        /// Counted by PerformanceTracker.
        /// </summary>
        public event Action<PuzzleData, HintSource> HintRequested;

        /// <summary>Raised whenever the panel closes, by resolution or by abandoning.</summary>
        public event Action PuzzleClosed;

        public QuestionBank Bank => bank;
        public PuzzleData Current => _current;

        /// <summary>True while a puzzle is on screen, including the explanation window.</summary>
        public bool IsPuzzleActive => _current != null;

        /// <summary>
        /// True only while answers are still being taken.
        ///
        /// Distinct from <see cref="IsPuzzleActive"/> because a resolved puzzle
        /// stays on screen for a few seconds so the explanation can be read.
        /// Without this distinction a second submission in that window would
        /// resolve the puzzle twice and emit two PuzzleResults, double-counting
        /// it in every metric downstream. The UI happens to disable its buttons
        /// at that point, but correctness must not depend on the view.
        /// </summary>
        public bool IsAcceptingAnswers => _current != null && !_resolved;

        /// <summary>Driven by DDAManager: caps which puzzles the bank will offer.</summary>
        public int MaxComplexity
        {
            get => maxComplexity;
            set => maxComplexity = Mathf.Clamp(value, 1, 3);
        }

        /// <summary>
        /// Driven by DDAManager: wrong answers allowed before a puzzle fails.
        /// Changing it mid-puzzle is safe - the new limit applies from the next
        /// submission, so an easier tier can rescue a player who is already in
        /// trouble rather than only helping on the following puzzle.
        /// </summary>
        public int MaxAttempts
        {
            get => maxAttempts;
            set => maxAttempts = Mathf.Clamp(value, 1, 10);
        }

        /// <summary>
        /// DDA lever: whether a wrong answer spawns a blast cell carrying that
        /// question's hint.
        ///
        /// Formerly HintsAlwaysAvailable, which gated the hint button. With the
        /// button gone that flag had no meaning, and combat is now the route to
        /// a hint - so the same lever governs whether that route exists at all.
        /// Renamed rather than reused under the old name, because a field whose
        /// name describes the opposite of its behaviour is exactly how the
        /// FailedAttempts ambiguity started.
        /// </summary>
        public bool HostileSpawningEnabled
        {
            get => hostileSpawningEnabled;
            set => hostileSpawningEnabled = value;
        }

        public bool IsSolved(string puzzleId) => !string.IsNullOrEmpty(puzzleId) && _solved.Contains(puzzleId);

        /// <summary>
        /// True when a puzzle is simple enough for the current difficulty tier.
        ///
        /// Exposed so callers can tell the player *why* a puzzle is unavailable
        /// instead of offering it and then silently refusing: at Easy the
        /// complexity cap is 1, which locks half of Level 1's stations.
        /// </summary>
        public bool IsWithinComplexityCap(string puzzleId)
        {
            PuzzleData puzzle = bank != null ? bank.Find(puzzleId) : null;
            return puzzle != null && puzzle.Complexity <= maxComplexity;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            // Deferred close so the explanation is readable before the panel goes.
            if (_closeAtTime > 0f && Time.unscaledTime >= _closeAtTime)
            {
                _closeAtTime = -1f;
                ClosePanel();
            }
        }

        // ------------------------------------------------------------------
        // Presenting
        // ------------------------------------------------------------------

        /// <summary>Looks a puzzle up by id and presents it. Returns false if it cannot start.</summary>
        public bool BeginPuzzle(string puzzleId)
        {
            if (bank == null)
            {
                Debug.LogError("[PuzzleManager] No QuestionBank assigned.");
                return false;
            }

            PuzzleData puzzle = bank.Find(puzzleId);
            if (puzzle == null)
            {
                Debug.LogError($"[PuzzleManager] No puzzle with id '{puzzleId}' in bank '{bank.name}'.");
                return false;
            }

            return BeginPuzzle(puzzle);
        }

        /// <summary>Presents a specific puzzle. Returns false if one is already running.</summary>
        public bool BeginPuzzle(PuzzleData puzzle)
        {
            if (puzzle == null || IsPuzzleActive) return false;

            if (puzzle.Complexity > maxComplexity)
            {
                // Not an error: the DDA has decided this puzzle is too hard right now.
                Debug.Log($"[PuzzleManager] Skipping '{puzzle.PuzzleId}' - complexity {puzzle.Complexity} above current cap {maxComplexity}.");
                return false;
            }

            _current = puzzle;
            _attempts = 0;
            _hintsUsed = 0;
            _autoHints = 0;
            _closeAtTime = -1f;
            _resolved = false;

            // Unscaled so the measurement is unaffected by any time-scale effects.
            _startTime = Time.unscaledTime;

            GameManager.Instance?.EnterPuzzleMode();

            if (puzzleUI != null) puzzleUI.Show(puzzle, maxAttempts);
            else Debug.LogWarning("[PuzzleManager] No PuzzleUI assigned - the puzzle has no visible panel.");

            // A hint already earned in combat is shown straight away, without
            // being counted a second time.
            if (_bankedHints.Contains(puzzle.PuzzleId)) puzzleUI?.ShowHint(puzzle.Hint);

            PuzzlePresented?.Invoke(puzzle);
            return true;
        }

        // ------------------------------------------------------------------
        // Answering
        // ------------------------------------------------------------------

        /// <summary>Answer for IdentifyStructure / DragAndDropLabel / ValveIdentification.</summary>
        public void SubmitStructure(string structureId)
        {
            if (!IsAcceptingAnswers) return;
            Evaluate(_current.IsCorrectStructure(structureId));
        }

        /// <summary>Answer for MultipleChoice.</summary>
        public void SubmitOption(int optionIndex)
        {
            if (!IsAcceptingAnswers) return;
            Evaluate(_current.IsCorrectOption(optionIndex));
        }

        /// <summary>Answer for BloodFlowSequence.</summary>
        public void SubmitSequence(IReadOnlyList<string> orderedSteps)
        {
            if (!IsAcceptingAnswers) return;
            Evaluate(_current.IsCorrectSequence(orderedSteps));
        }

        /// <summary>
        /// Applies one answer. Correct resolves immediately; incorrect either
        /// invites a retry or, once <see cref="maxAttempts"/> is spent, resolves
        /// as a failure so the DDA gets an unambiguous signal.
        /// </summary>
        private void Evaluate(bool correct)
        {
            _attempts++;
            AttemptSubmitted?.Invoke(correct, _attempts);

            if (correct)
            {
                Resolve(true);
                return;
            }

            if (_attempts >= maxAttempts)
            {
                Resolve(false);
                return;
            }

            int remaining = maxAttempts - _attempts;
            puzzleUI?.ShowIncorrect(remaining, _current.Hint, _hintsUsed > 0);
        }

        private void Resolve(bool correct)
        {
            if (_resolved) return;   // belt and braces: never emit two results for one puzzle
            _resolved = true;

            float elapsed = Time.unscaledTime - _startTime;

            var result = new PuzzleResult(
                _current.PuzzleId, GameManager.Instance != null ? GameManager.Instance.Session.CurrentLevel : LevelId.None,
                _current.Type, _current.Complexity, correct, elapsed, _attempts, _hintsUsed, _autoHints);

            if (correct) _solved.Add(_current.PuzzleId);

            // Phase 3 replaces this Console trace with PerformanceTracker.
            Debug.Log($"[PuzzleManager] {result}");

            PuzzleAnswered?.Invoke(result);

            puzzleUI?.ShowResolution(correct, _current.Explanation);
            _closeAtTime = Time.unscaledTime + explanationDuration;
        }

        // ------------------------------------------------------------------
        // Hints and closing
        // ------------------------------------------------------------------

        /// <summary>
        /// Reveals the hint because the player asked. Carries a score penalty.
        ///
        /// The on-screen hint button was removed - hints are now earned by
        /// destroying the blast cell a wrong answer spawns. This entry point is
        /// kept because the scoring rule for a requested hint still exists and
        /// the binding may be restored; nothing in the shipped UI calls it.
        /// </summary>
        public void RequestHint()
        {
            // Refused once resolved: the answer is already on screen, so counting
            // a hint there would inflate the assistance metric for nothing.
            if (!IsAcceptingAnswers) return;

            _hintsUsed++;
            RevealHint(HintSource.Requested);
        }

        /// <summary>
        /// Reveals the hint because HintManager decided the player needs it.
        /// Counted separately and deliberately unpenalised - punishing a player
        /// for help they did not ask for would make the score measure the DDA's
        /// behaviour, not theirs.
        /// </summary>
        public void GiveAutomaticHint()
        {
            if (!IsAcceptingAnswers) return;

            _autoHints++;
            RevealHint(HintSource.Automatic);
        }

        /// <summary>
        /// Delivers the hint for a specific question because the player
        /// destroyed the blast cell spawned from it.
        ///
        /// Works whether or not that puzzle is currently open: the hint is shown
        /// on the HUD immediately *and* banked, so it is already on screen when
        /// the player next reaches that station. A hint seen three minutes
        /// earlier and then forgotten would be worth nothing.
        /// </summary>
        public void DeliverEarnedHint(string puzzleId)
        {
            PuzzleData puzzle = bank != null ? bank.Find(puzzleId) : null;
            if (puzzle == null || string.IsNullOrWhiteSpace(puzzle.Hint)) return;

            _bankedHints.Add(puzzleId);

            GameplayHUD.Instance?.ShowHint($"Hint unlocked - {puzzle.Hint}");
            if (_current == puzzle) puzzleUI?.ShowHint(puzzle.Hint);

            HintRequested?.Invoke(puzzle, HintSource.Earned);
        }

        /// <summary>True when this question's hint has already been earned.</summary>
        public bool HasBankedHint(string puzzleId) => !string.IsNullOrEmpty(puzzleId) && _bankedHints.Contains(puzzleId);

        private void RevealHint(HintSource source)
        {
            string hint = _current.Hint;
            if (string.IsNullOrWhiteSpace(hint)) return;

            GameplayHUD.Instance?.ShowHint(source == HintSource.Requested ? hint : "Hint: " + hint);
            puzzleUI?.ShowHint(hint);

            // Counting is the measurement layer's job. Emitting an event instead
            // of writing SessionData here keeps gameplay free of metrics code,
            // and means a hint taken on a puzzle the player then abandons is
            // still recorded - which a count taken at resolution time would miss.
            HintRequested?.Invoke(_current, source);
        }

        /// <summary>Closes the panel without answering. Nothing is recorded.</summary>
        public void AbandonPuzzle()
        {
            if (!IsPuzzleActive) return;

            Debug.Log($"[PuzzleManager] '{_current.PuzzleId}' abandoned after {_attempts} attempt(s) - not recorded.");
            ClosePanel();
        }

        private void ClosePanel()
        {
            _current = null;
            _closeAtTime = -1f;
            _resolved = false;

            puzzleUI?.Hide();
            GameplayHUD.Instance?.ClearHint();
            GameManager.Instance?.ExitPuzzleMode();

            // Fired last so listeners (HintManager clearing its highlights) see
            // a fully closed puzzle rather than a half-torn-down one.
            PuzzleClosed?.Invoke();
        }
    }
}
