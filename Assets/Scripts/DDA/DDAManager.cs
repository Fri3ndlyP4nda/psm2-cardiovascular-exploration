using System;
using System.Collections.Generic;
using Cardio.Core;
using Cardio.Gameplay;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cardio.DDA
{
    /// <summary>
    /// The ANALYZE and ADJUST stages of the PSM1 loop, and the project's
    /// central technical contribution.
    ///
    ///     PerformanceTracker (measure)
    ///             |
    ///             v
    ///     DDARules.Evaluate (pure policy)
    ///             |
    ///             v
    ///     DDAManager.Apply -> PuzzleManager  (puzzle complexity, attempts)
    ///                      -> HintManager    (assistance level)
    ///                      -> HazardVolume   (environmental pressure)
    ///                      -> obstacle speed (consumed by A* agents in Phase 5)
    ///
    /// The policy itself lives in <see cref="DDARules"/> so it can be tested
    /// without a scene. This class handles lifecycle, applies the outcome, and
    /// keeps an auditable log of every decision.
    /// </summary>
    [DisallowMultipleComponent]
    public class DDAManager : MonoBehaviour
    {
        public static DDAManager Instance { get; private set; }

        [Header("Config (loaded from Resources at startup)")]
        [SerializeField] private DDAConfig config;

        [Header("Debug")]
        [SerializeField] private bool logEveryEvaluation = true;

        [Header("Live state (read-only)")]
        [SerializeField] private DifficultyTier currentTier = DifficultyTier.Easy;
        [SerializeField] private float lastScore;
        [SerializeField] private int puzzlesSinceLastChange;
        [SerializeField] private int tierChangesThisSession;

        private readonly List<DDADecision> _decisionLog = new List<DDADecision>();

        /// <summary>Raised on every evaluation, whether or not the tier moved.</summary>
        public event Action<DDADecision> Evaluated;

        /// <summary>Raised only when the tier actually changes.</summary>
        public event Action<DifficultySettings> TierChanged;

        public DDAConfig Config => config;
        public DifficultyTier CurrentTier => currentTier;

        /// <summary>The active tier's parameters. Never null once a config is loaded.</summary>
        public DifficultySettings Current => config != null ? config.For(currentTier) : null;

        /// <summary>Every decision made this session, for the report and the Phase 9 dashboard.</summary>
        public IReadOnlyList<DDADecision> DecisionLog => _decisionLog;

        /// <summary>
        /// Obstacle speed multiplier for the current tier.
        ///
        /// NOTE: nothing consumes this yet. The moving obstacles that read it
        /// (neutrophils, monocytes) arrive in Phase 5 - it is exposed now so the
        /// DDA side of that integration is already finished and testable.
        /// </summary>
        public float ObstacleSpeedMultiplier => Current != null ? Current.ObstacleSpeedMultiplier : 1f;

        /// <summary>Hazard damage multiplier for the current tier. Read live by HazardVolume.</summary>
        public float HazardDamageMultiplier => Current != null ? Current.HazardDamageMultiplier : 1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (config == null) config = Resources.Load<DDAConfig>(DDAConfig.ResourcePath);

            if (config == null)
            {
                Debug.LogError($"[DDAManager] No DDAConfig found at Resources/{DDAConfig.ResourcePath}. " +
                               "Run PSM2 > Setup > Build or Rebuild Project. Difficulty will stay fixed.");
            }
            else if (!config.IsComplete)
            {
                Debug.LogError("[DDAManager] DDAConfig is missing one or more tier assets. Difficulty will stay fixed.");
            }
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            if (PerformanceTracker.Instance != null) PerformanceTracker.Instance.MetricsUpdated -= OnMetricsUpdated;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            currentTier = GameManager.Instance != null
                ? GameManager.Instance.Session.StartingDifficulty
                : DifficultyTier.Easy;

            if (PerformanceTracker.Instance != null) PerformanceTracker.Instance.MetricsUpdated += OnMetricsUpdated;
            else Debug.LogWarning("[DDAManager] No PerformanceTracker - difficulty cannot adapt.");

            ApplyCurrentTier("initial tier");
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A new level's PuzzleManager starts on its own serialized defaults,
            // so the active tier has to be pushed into it again.
            puzzlesSinceLastChange = int.MaxValue;   // a new level may adjust immediately
            ApplyCurrentTier("level loaded");
        }

        // ------------------------------------------------------------------
        // Evaluation
        // ------------------------------------------------------------------

        private void OnMetricsUpdated(PerformanceSnapshot snapshot)
        {
            if (puzzlesSinceLastChange != int.MaxValue) puzzlesSinceLastChange++;

            DDADecision decision = DDARules.Evaluate(snapshot, currentTier, config, puzzlesSinceLastChange);
            lastScore = decision.Score;

            _decisionLog.Add(decision);
            Evaluated?.Invoke(decision);

            if (logEveryEvaluation) Debug.Log($"[DDAManager] {decision}");

            if (!decision.ChangedTier) return;

            currentTier = decision.ToTier;
            puzzlesSinceLastChange = 0;
            tierChangesThisSession++;

            ApplyCurrentTier(decision.Reason);

            // A tier change is the moment the player is most likely to notice the
            // game adapting, so it is called out explicitly in the log.
            Debug.Log($"[DDAManager] DIFFICULTY {decision.FromTier} -> {decision.ToTier}. {decision.Reason}");
        }

        /// <summary>
        /// Forces a tier, bypassing the rules. Used by the diagnostics harness
        /// and by the fixed-difficulty control condition in the evaluation.
        /// </summary>
        public void ForceTier(DifficultyTier tier, string reason = "forced")
        {
            if (currentTier == tier) return;

            currentTier = tier;
            puzzlesSinceLastChange = 0;
            tierChangesThisSession++;
            ApplyCurrentTier(reason);
        }

        // ------------------------------------------------------------------
        // Applying
        // ------------------------------------------------------------------

        /// <summary>Pushes the active tier's parameters into every system that consumes them.</summary>
        private void ApplyCurrentTier(string reason)
        {
            DifficultySettings settings = Current;
            if (settings == null) return;

            // Puzzles: complexity cap and attempt allowance.
            PuzzleManager puzzles = PuzzleManager.Instance;
            if (puzzles != null)
            {
                puzzles.MaxComplexity = settings.MaxPuzzleComplexity;
                puzzles.MaxAttempts = settings.MaxPuzzleAttempts;

                // Combat is the route to a hint, so this decides whether that
                // route exists. Tiers that offer automatic help also let wrong
                // answers spawn a blast; Hard offers neither and leaves the
                // player to work it out.
                puzzles.HostileSpawningEnabled = settings.HintFrequency != HintFrequency.Low;
            }

            // Assistance level.
            HintManager.Instance?.ApplyTier(settings);

            // Session + HUD.
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.Session.CurrentDifficulty = currentTier;
                gm.RaiseSessionChanged();
            }

            TierChanged?.Invoke(settings);

            if (logEveryEvaluation) Debug.Log($"[DDAManager] Applied {settings.Describe()} ({reason}).");
        }

        /// <summary>Clears the decision history. Called when a new session starts.</summary>
        public void ResetSession()
        {
            _decisionLog.Clear();
            puzzlesSinceLastChange = int.MaxValue;
            tierChangesThisSession = 0;
            lastScore = 0f;

            currentTier = GameManager.Instance != null
                ? GameManager.Instance.Session.StartingDifficulty
                : DifficultyTier.Easy;

            ApplyCurrentTier("session reset");
        }

        /// <summary>
        /// Dumps the whole adaptation history. This is the evidence for the
        /// "difficulty changed, and here is why" part of the demonstration.
        /// </summary>
        public void LogDecisionHistory()
        {
            var lines = new List<string> { $"[DDAManager] {_decisionLog.Count} evaluation(s), {tierChangesThisSession} tier change(s):" };

            for (int i = 0; i < _decisionLog.Count; i++) lines.Add($"   {i + 1,3}. {_decisionLog[i]}");
            if (_decisionLog.Count == 0) lines.Add("   (no evaluations yet)");

            Debug.Log(string.Join("\n", lines));
        }
    }
}
