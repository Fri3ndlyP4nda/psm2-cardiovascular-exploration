using Cardio.Data;
using Cardio.UI;
using TMPro;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// A medical clipboard standing in the world next to the structure it asks
    /// about. Walking up to it and pressing the interact key opens its puzzle.
    ///
    /// Placing puzzles physically (rather than popping them up on a timer) is
    /// what keeps the anatomy lesson tied to exploration: to answer a question
    /// about the mitral valve you have to walk to the mitral valve.
    ///
    /// The puzzle is referenced by id rather than by asset so the scene does not
    /// depend on ScriptableObject GUIDs - reseeding the question bank cannot
    /// break a level.
    /// </summary>
    public class PuzzleStation : MonoBehaviour, IInteractable
    {
        [Header("Puzzle")]
        [Tooltip("PuzzleId in the level's QuestionBank.")]
        [SerializeField] private string puzzleId = "";

        [Tooltip("Overrides the auto-generated prompt line. Leave blank for the default.")]
        [SerializeField] private string promptOverride = "";

        [Header("Presentation")]
        [SerializeField] private TMP_Text worldLabel;
        [SerializeField] private Renderer[] stateRenderers;
        [SerializeField] private Material pendingMaterial;
        [SerializeField] private Material solvedMaterial;

        [Tooltip("Bobs gently so the station reads as interactive.")]
        [SerializeField] private Transform bobTransform;
        [SerializeField] private float bobAmplitude = 0.15f;
        [SerializeField] private float bobSpeed = 1.8f;

        private Vector3 _bobOrigin;
        private bool _solved;

        public string PuzzleId => puzzleId;

        public Vector3 InteractionPoint => transform.position;

        /// <summary>True when this station's puzzle is above the current difficulty cap.</summary>
        public bool IsLockedByDifficulty =>
            PuzzleManager.Instance != null && !PuzzleManager.Instance.IsWithinComplexityCap(puzzleId);

        public string InteractionPrompt
        {
            get
            {
                if (_solved) return "Completed";
                if (IsLockedByDifficulty) return "Too advanced for now";
                if (!string.IsNullOrWhiteSpace(promptOverride)) return promptOverride;
                return "Examine";
            }
        }

        /// <summary>
        /// Note this stays true for a difficulty-locked station. The player is
        /// still offered the prompt so the station does not read as broken
        /// scenery - <see cref="Interact"/> then explains the refusal.
        /// </summary>
        public bool CanInteract
        {
            get
            {
                if (_solved) return false;
                if (PuzzleManager.Instance == null) return false;
                return !PuzzleManager.Instance.IsPuzzleActive;
            }
        }

        private void Awake()
        {
            if (bobTransform != null) _bobOrigin = bobTransform.localPosition;
        }

        private void Start()
        {
            if (PuzzleManager.Instance != null)
            {
                PuzzleManager.Instance.PuzzleAnswered += OnPuzzleAnswered;

                // A station may already be solved if the level was re-entered
                // without reloading the manager.
                if (PuzzleManager.Instance.IsSolved(puzzleId)) MarkSolved();
            }

            RefreshLabel();
        }

        private void OnDestroy()
        {
            if (PuzzleManager.Instance != null) PuzzleManager.Instance.PuzzleAnswered -= OnPuzzleAnswered;
        }

        private void Update()
        {
            if (bobTransform == null || _solved) return;

            bobTransform.localPosition = _bobOrigin + Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobAmplitude);
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract) return;

            if (IsLockedByDifficulty)
            {
                // Never fail silently. Without this the player presses the
                // interact key at a perfectly normal-looking station and
                // nothing whatsoever happens.
                GameplayHUD.Instance?.ShowHint("This question is too advanced right now - answer a few more first.");
                return;
            }

            PuzzleManager.Instance.BeginPuzzle(puzzleId);
        }

        private void OnPuzzleAnswered(PuzzleResult result)
        {
            if (!result.Correct || result.PuzzleId != puzzleId) return;
            MarkSolved();
        }

        private void MarkSolved()
        {
            _solved = true;

            if (stateRenderers != null && solvedMaterial != null)
            {
                foreach (Renderer r in stateRenderers)
                {
                    if (r != null) r.sharedMaterial = solvedMaterial;
                }
            }

            RefreshLabel();
        }

        private void RefreshLabel()
        {
            if (worldLabel == null) return;

            worldLabel.text = _solved ? "<color=#7FBF7F>COMPLETE</color>" : "?";
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, 3f);
        }
    }
}
