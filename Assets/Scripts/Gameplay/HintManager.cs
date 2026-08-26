using System.Collections.Generic;
using Cardio.Data;
using Cardio.DDA;
using Cardio.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Offers help without being asked, at a rate the current difficulty tier
    /// decides.
    ///
    /// This is the visible half of the PSM1 adaptive loop: poor performance
    /// lowers the tier, the lower tier offers hints sooner, and the correct
    /// structure lights up. A player who is doing well is left alone.
    ///
    /// It never decides *whether* the player is struggling - that judgement
    /// belongs to the DDAManager. All this class knows is the tier it was
    /// handed and how long the current puzzle has been open.
    ///
    /// Automatic hints are recorded separately from requested ones so the score
    /// does not punish a player for assistance they did not ask for, while the
    /// PSM2 evaluation can still see how much help was given.
    /// </summary>
    [DisallowMultipleComponent]
    public class HintManager : MonoBehaviour
    {
        public static HintManager Instance { get; private set; }

        [Header("Live state (read-only)")]
        [SerializeField] private string activeTier = "-";
        [SerializeField] private bool hintOfferedForCurrentPuzzle;
        [SerializeField] private int automaticHintsThisSession;

        private DifficultySettings _tier;
        private PuzzleManager _puzzleManager;

        private PuzzleData _activePuzzle;
        private float _puzzleOpenedAt;
        private int _failedAttempts;

        /// <summary>structureId -> the geometry tagged with it, rebuilt per scene.</summary>
        private readonly Dictionary<string, List<AnatomyStructureTag>> _structures =
            new Dictionary<string, List<AnatomyStructureTag>>();

        private readonly List<AnatomyMarker> _highlighted = new List<AnatomyMarker>();

        public int AutomaticHintsThisSession => automaticHintsThisSession;

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
            Detach();
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            Attach();
            RebuildStructureIndex();
        }

        private void Update()
        {
            if (_activePuzzle == null || hintOfferedForCurrentPuzzle || _tier == null) return;
            if (_tier.AutoHintDelaySeconds <= 0f) return;

            // Unscaled: puzzle mode leaves timeScale at 1, but this stays correct
            // if a later phase introduces slow-motion effects.
            if (Time.unscaledTime - _puzzleOpenedAt < _tier.AutoHintDelaySeconds) return;

            OfferHint($"stuck for {_tier.AutoHintDelaySeconds:0}s");
        }

        // ------------------------------------------------------------------
        // Wiring
        // ------------------------------------------------------------------

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Detach();
            Attach();
            RebuildStructureIndex();
        }

        private void Attach()
        {
            _puzzleManager = PuzzleManager.Instance;
            if (_puzzleManager == null) return;

            _puzzleManager.PuzzlePresented += OnPuzzlePresented;
            _puzzleManager.AttemptSubmitted += OnAttemptSubmitted;
            _puzzleManager.PuzzleClosed += OnPuzzleClosed;
        }

        private void Detach()
        {
            if (_puzzleManager == null) return;

            _puzzleManager.PuzzlePresented -= OnPuzzlePresented;
            _puzzleManager.AttemptSubmitted -= OnAttemptSubmitted;
            _puzzleManager.PuzzleClosed -= OnPuzzleClosed;
            _puzzleManager = null;
        }

        /// <summary>
        /// Caches every tagged structure in the scene once, so offering a hint
        /// does not trigger a scene-wide search mid-puzzle.
        /// </summary>
        private void RebuildStructureIndex()
        {
            _structures.Clear();

            // No sort mode: the overload taking one is deprecated in 6000.5, and
            // ordering is irrelevant here because the results go into a dictionary.
            AnatomyStructureTag[] tags = FindObjectsByType<AnatomyStructureTag>(FindObjectsInactive.Exclude);
            foreach (AnatomyStructureTag tag in tags)
            {
                if (tag == null || string.IsNullOrEmpty(tag.StructureId)) continue;

                if (!_structures.TryGetValue(tag.StructureId, out List<AnatomyStructureTag> list))
                {
                    list = new List<AnatomyStructureTag>();
                    _structures[tag.StructureId] = list;
                }

                list.Add(tag);
            }
        }

        /// <summary>Called by DDAManager whenever the active tier changes.</summary>
        public void ApplyTier(DifficultySettings settings)
        {
            _tier = settings;
            activeTier = settings != null ? settings.Tier.ToString() : "-";
        }

        // ------------------------------------------------------------------
        // Puzzle lifecycle
        // ------------------------------------------------------------------

        private void OnPuzzlePresented(PuzzleData puzzle)
        {
            _activePuzzle = puzzle;
            _puzzleOpenedAt = Time.unscaledTime;
            _failedAttempts = 0;
            hintOfferedForCurrentPuzzle = false;
        }

        private void OnAttemptSubmitted(bool correct, int attemptNumber)
        {
            if (correct || _tier == null || hintOfferedForCurrentPuzzle) return;

            _failedAttempts++;

            if (_tier.AutoHintAfterFailedAttempts <= 0) return;
            if (_failedAttempts < _tier.AutoHintAfterFailedAttempts) return;

            OfferHint($"{_failedAttempts} wrong answer(s)");
        }

        private void OnPuzzleClosed()
        {
            ClearHighlights();

            _activePuzzle = null;
            _failedAttempts = 0;
            hintOfferedForCurrentPuzzle = false;
        }

        // ------------------------------------------------------------------
        // Offering
        // ------------------------------------------------------------------

        /// <summary>
        /// Shows the hint and, if the tier allows, makes the answer glow.
        /// Routed through PuzzleManager so the hint text and counters stay in
        /// one place.
        /// </summary>
        private void OfferHint(string trigger)
        {
            if (_activePuzzle == null || _puzzleManager == null) return;

            hintOfferedForCurrentPuzzle = true;
            automaticHintsThisSession++;

            _puzzleManager.GiveAutomaticHint();

            if (_tier != null && _tier.HighlightStructureOnHint) HighlightAnswer(_activePuzzle);

            Debug.Log($"[HintManager] Automatic hint on '{_activePuzzle.PuzzleId}' " +
                      $"({trigger}, tier {activeTier}).");
        }

        /// <summary>Makes the puzzle's target structure glow. Only meaningful for structure puzzles.</summary>
        private void HighlightAnswer(PuzzleData puzzle)
        {
            if (!puzzle.Type.UsesWorldPicking()) return;
            if (string.IsNullOrEmpty(puzzle.TargetStructureId)) return;

            if (!_structures.TryGetValue(puzzle.TargetStructureId, out List<AnatomyStructureTag> tags))
            {
                Debug.LogWarning($"[HintManager] Cannot highlight '{puzzle.TargetStructureId}' - not present in this scene.");
                return;
            }

            // Several tags share one marker, so highlight each marker only once.
            foreach (AnatomyStructureTag tag in tags)
            {
                if (tag == null || tag.Marker == null) continue;
                if (_highlighted.Contains(tag.Marker)) continue;

                tag.Marker.SetHighlighted(true);
                _highlighted.Add(tag.Marker);
            }
        }

        private void ClearHighlights()
        {
            foreach (AnatomyMarker marker in _highlighted)
            {
                if (marker != null) marker.SetHighlighted(false);
            }

            _highlighted.Clear();
            GameplayHUD.Instance?.ClearHint();
        }

        /// <summary>Clears counters when a new session starts.</summary>
        public void ResetSession()
        {
            automaticHintsThisSession = 0;
            ClearHighlights();
        }
    }
}
