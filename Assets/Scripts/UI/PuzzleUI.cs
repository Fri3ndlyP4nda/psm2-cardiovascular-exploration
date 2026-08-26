using System.Collections.Generic;
using Cardio.Data;
using Cardio.Gameplay;
using Cardio.Player;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// The puzzle panel. It renders whichever of the five PSM1 puzzle formats
    /// is active and hands raw answers back to <see cref="PuzzleManager"/>.
    ///
    /// It deliberately knows nothing about correctness - it reports "the player
    /// picked structure X" or "the player chose option 2" and the PuzzleData
    /// decides. That keeps the scoring rules in one testable place and stops
    /// the UI from drifting out of sync with them.
    ///
    /// The panel is anchored to the bottom of the screen so the chamber stays
    /// visible: structure puzzles are answered by pointing at the world above it.
    /// </summary>
    public class PuzzleUI : MonoBehaviour
    {
        [Header("Root")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text promptLabel;
        [SerializeField] private TMP_Text feedbackLabel;
        [SerializeField] private TMP_Text attemptsLabel;

        [Header("Structure mode (Identify / Drag / Valve)")]
        [SerializeField] private GameObject structureSection;
        [SerializeField] private TMP_Text structureInstruction;
        [SerializeField] private DraggableLabel dragChip;

        [Header("Multiple choice")]
        [SerializeField] private GameObject optionsSection;
        [SerializeField] private Button[] optionButtons;

        [Header("Blood flow sequence")]
        [SerializeField] private GameObject sequenceSection;
        [SerializeField] private Button[] sequenceButtons;
        [SerializeField] private TMP_Text sequenceOrderLabel;
        [SerializeField] private Button sequenceSubmitButton;
        [SerializeField] private Button sequenceResetButton;

        [Header("Controls")]
        // No hint button. Hints are earned by destroying the leukemic blast that
        // a wrong answer spawns, or offered automatically by the DDA.
        [SerializeField] private Button closeButton;

        [Header("Feedback colours")]
        [SerializeField] private Color correctColor = new Color(0.55f, 0.88f, 0.6f);
        [SerializeField] private Color incorrectColor = new Color(0.95f, 0.55f, 0.45f);
        [SerializeField] private Color neutralColor = new Color(0.85f, 0.84f, 0.86f);

        private PuzzleData _puzzle;
        private Camera _camera;
        private bool _isResolved;
        private AnatomyStructureTag _hovered;

        // Sequence state
        private readonly List<string> _sequenceChosen = new List<string>();
        private readonly List<string> _sequencePool = new List<string>();

        public bool IsOpen => _puzzle != null;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);

            if (sequenceSubmitButton != null) sequenceSubmitButton.onClick.AddListener(OnSequenceSubmit);
            if (sequenceResetButton != null) sequenceResetButton.onClick.AddListener(ResetSequence);

            if (optionButtons != null)
            {
                for (int i = 0; i < optionButtons.Length; i++)
                {
                    int index = i;   // captured per iteration, not shared
                    if (optionButtons[i] != null) optionButtons[i].onClick.AddListener(() => OnOptionClicked(index));
                }
            }

            if (sequenceButtons != null)
            {
                for (int i = 0; i < sequenceButtons.Length; i++)
                {
                    int index = i;
                    if (sequenceButtons[i] != null) sequenceButtons[i].onClick.AddListener(() => OnSequenceStepClicked(index));
                }
            }

            if (panelRoot != null) panelRoot.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen || _isResolved) return;

            // Escape is NOT handled here. PauseMenuUI owns that key for the whole
            // game and routes it to AbandonPuzzle while a puzzle is open.
            //
            // Two components reading the same key in one frame is a trap: real
            // input reports the press to every reader, so whichever ran first
            // could close the puzzle and let the second one immediately pause
            // the game - an outcome that depended on undefined script order.

            if (!_puzzle.Type.UsesWorldPicking()) return;

            // Highlight whatever the pointer is over. This is the only signal
            // that the chamber itself is clickable - without it the panel asks
            // the player to click a structure and nothing on screen suggests
            // that is possible.
            UpdateHover();

            // Click-to-identify. Drag-and-drop is handled by DraggableLabel.
            if (_puzzle.Type == PuzzleType.IdentifyStructure || _puzzle.Type == PuzzleType.ValveIdentification)
            {
                HandleWorldClick();
            }
        }

        /// <summary>
        /// Tracks the structure under the pointer and tints it.
        ///
        /// Deliberately shows no name: revealing what you are pointing at would
        /// answer "identify the left ventricle" outright. The tint confirms
        /// *that* something is selectable, not *which* one is correct.
        /// </summary>
        private void UpdateHover()
        {
            AnatomyStructureTag under = StructurePicker.IsPointerOverUI()
                ? null
                : StructurePicker.Pick(PlayerInputReader.PointerPosition, _camera);

            if (ReferenceEquals(under, _hovered)) return;

            ApplyHover(_hovered, false);
            _hovered = under;
            ApplyHover(_hovered, true);
        }

        private static void ApplyHover(AnatomyStructureTag tag, bool hovered)
        {
            if (tag != null && tag.Marker != null) tag.Marker.SetHovered(hovered);
        }

        private void ClearHover()
        {
            ApplyHover(_hovered, false);
            _hovered = null;
        }

        // ------------------------------------------------------------------
        // Presentation
        // ------------------------------------------------------------------

        /// <summary>Opens the panel for a puzzle. Called by PuzzleManager.</summary>
        public void Show(PuzzleData puzzle, int maxAttempts)
        {
            _puzzle = puzzle;
            _isResolved = false;
            _camera = Camera.main;

            if (panelRoot != null) panelRoot.SetActive(true);

            if (headerLabel != null) headerLabel.text = puzzle.Type.DisplayName();
            if (promptLabel != null) promptLabel.text = puzzle.Prompt;
            SetFeedback(string.Empty, neutralColor);
            SetAttempts(0, maxAttempts);

            ConfigureSections(puzzle);
        }

        private void ConfigureSections(PuzzleData puzzle)
        {
            bool structureMode = puzzle.Type.UsesWorldPicking();
            bool optionsMode = puzzle.Type == PuzzleType.MultipleChoice;
            bool sequenceMode = puzzle.Type == PuzzleType.BloodFlowSequence;

            if (structureSection != null) structureSection.SetActive(structureMode);
            if (optionsSection != null) optionsSection.SetActive(optionsMode);
            if (sequenceSection != null) sequenceSection.SetActive(sequenceMode);

            if (structureMode) ConfigureStructureMode(puzzle);
            if (optionsMode) ConfigureOptions(puzzle);
            if (sequenceMode) ConfigureSequence(puzzle);
        }

        private void ConfigureStructureMode(PuzzleData puzzle)
        {
            bool isDrag = puzzle.Type == PuzzleType.DragAndDropLabel;

            if (dragChip != null)
            {
                dragChip.gameObject.SetActive(isDrag);
                if (isDrag) dragChip.Initialise(this, puzzle.ResolveLabelText());
            }

            if (structureInstruction != null)
            {
                // Spells out both controls. The structure being asked about may
                // sit behind this panel or off to one side, and the player
                // cannot walk while a puzzle is open - so how to look around is
                // as important as how to answer.
                string action = isDrag
                    ? "Drag the label onto the correct structure"
                    : "Click the correct structure";

                structureInstruction.text =
                    $"{action} in the chamber.   <color=#7FBFEA>Hold right mouse to look around.</color>";
            }
        }

        private void ConfigureOptions(PuzzleData puzzle)
        {
            if (optionButtons == null) return;

            for (int i = 0; i < optionButtons.Length; i++)
            {
                Button button = optionButtons[i];
                if (button == null) continue;

                bool used = puzzle.Options != null && i < puzzle.Options.Length;
                button.gameObject.SetActive(used);
                button.interactable = true;

                if (!used) continue;

                var text = button.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = puzzle.Options[i];
            }
        }

        private void ConfigureSequence(PuzzleData puzzle)
        {
            _sequenceChosen.Clear();
            _sequencePool.Clear();

            if (puzzle.SequenceSteps != null) _sequencePool.AddRange(puzzle.SequenceSteps);

            // Shuffle so the authored (correct) order is not the displayed order.
            for (int i = _sequencePool.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (_sequencePool[i], _sequencePool[j]) = (_sequencePool[j], _sequencePool[i]);
            }

            RefreshSequenceButtons();
            RefreshSequenceOrderLabel();
        }

        // ------------------------------------------------------------------
        // Structure answers
        // ------------------------------------------------------------------

        private void HandleWorldClick()
        {
            if (!PlayerInputReader.PrimaryClickPressed) return;
            if (StructurePicker.IsPointerOverUI()) return;

            AnatomyStructureTag picked = StructurePicker.Pick(PlayerInputReader.PointerPosition, _camera);
            if (picked == null)
            {
                SetFeedback("That is not an anatomical structure - try again.", neutralColor);
                return;
            }

            PuzzleManager.Instance?.SubmitStructure(picked.StructureId);
        }

        /// <summary>Called by <see cref="DraggableLabel"/> when a drag starts.</summary>
        public void OnLabelDragBegin(DraggableLabel chip)
        {
            SetFeedback("Drop the label on the structure it names.", neutralColor);
        }

        /// <summary>Called by <see cref="DraggableLabel"/> when the chip is released.</summary>
        public void OnLabelDropped(DraggableLabel chip, Vector2 screenPosition)
        {
            if (!IsOpen || _isResolved) return;

            if (StructurePicker.IsPointerOverUI())
            {
                SetFeedback("Drop the label on the chamber, not on the panel.", neutralColor);
                return;
            }

            AnatomyStructureTag picked = StructurePicker.Pick(screenPosition, _camera);
            if (picked == null)
            {
                SetFeedback("No structure there - aim at the chamber wall, a valve or a muscle.", neutralColor);
                return;
            }

            PuzzleManager.Instance?.SubmitStructure(picked.StructureId);
        }

        // ------------------------------------------------------------------
        // Option / sequence answers
        // ------------------------------------------------------------------

        private void OnOptionClicked(int index)
        {
            if (!IsOpen || _isResolved) return;
            PuzzleManager.Instance?.SubmitOption(index);
        }

        /// <summary>
        /// Close button. Abandoning is not recorded as a failure - a player who
        /// walks away without answering has not got the question wrong, and
        /// counting it would corrupt the accuracy figure the DDA relies on.
        /// Once resolved, the panel is already closing on its own timer.
        /// </summary>
        private void OnCloseClicked()
        {
            if (!IsOpen) return;
            PuzzleManager.Instance?.AbandonPuzzle();
        }

        private void OnSequenceStepClicked(int poolIndex)
        {
            if (!IsOpen || _isResolved) return;
            if (poolIndex < 0 || poolIndex >= _sequencePool.Count) return;

            string step = _sequencePool[poolIndex];
            if (_sequenceChosen.Contains(step)) return;

            _sequenceChosen.Add(step);
            RefreshSequenceButtons();
            RefreshSequenceOrderLabel();
        }

        private void ResetSequence()
        {
            _sequenceChosen.Clear();
            RefreshSequenceButtons();
            RefreshSequenceOrderLabel();
            SetFeedback(string.Empty, neutralColor);
        }

        private void OnSequenceSubmit()
        {
            if (!IsOpen || _isResolved) return;

            if (_sequenceChosen.Count != _sequencePool.Count)
            {
                SetFeedback("Place every step before submitting.", neutralColor);
                return;
            }

            PuzzleManager.Instance?.SubmitSequence(_sequenceChosen);
        }

        private void RefreshSequenceButtons()
        {
            if (sequenceButtons == null) return;

            for (int i = 0; i < sequenceButtons.Length; i++)
            {
                Button button = sequenceButtons[i];
                if (button == null) continue;

                bool used = i < _sequencePool.Count;
                button.gameObject.SetActive(used);
                if (!used) continue;

                string step = _sequencePool[i];
                button.interactable = !_sequenceChosen.Contains(step);

                var text = button.GetComponentInChildren<TMP_Text>();
                if (text != null) text.text = step;
            }

            if (sequenceSubmitButton != null)
            {
                sequenceSubmitButton.interactable = _sequenceChosen.Count == _sequencePool.Count && _sequencePool.Count > 0;
            }
        }

        private void RefreshSequenceOrderLabel()
        {
            if (sequenceOrderLabel == null) return;

            if (_sequenceChosen.Count == 0)
            {
                sequenceOrderLabel.text = "<i>Click the steps in order...</i>";
                return;
            }

            sequenceOrderLabel.text = string.Join("  ->  ", _sequenceChosen);
        }

        // ------------------------------------------------------------------
        // Result feedback
        // ------------------------------------------------------------------

        /// <summary>Shown after a wrong answer that still has retries left.</summary>
        public void ShowIncorrect(int attemptsRemaining, string hint, bool hintAlreadyShown)
        {
            string message = attemptsRemaining == 1
                ? "Not quite. One attempt left."
                : $"Not quite. {attemptsRemaining} attempts left.";

            if (!hintAlreadyShown && !string.IsNullOrWhiteSpace(hint)) message += "  Try the hint.";

            SetFeedback(message, incorrectColor);
            SetAttempts(-1, -1);
        }

        /// <summary>Shown once the puzzle resolves, correct or failed.</summary>
        public void ShowResolution(bool correct, string explanation)
        {
            _isResolved = true;

            string headline = correct ? "Correct." : "Out of attempts.";
            string body = string.IsNullOrWhiteSpace(explanation) ? string.Empty : "\n" + explanation;

            SetFeedback(headline + body, correct ? correctColor : incorrectColor);

            SetInteractable(false);
        }

        public void ShowHint(string hint)
        {
            if (string.IsNullOrWhiteSpace(hint)) return;
            SetFeedback("Hint: " + hint, neutralColor);
        }

        public void Hide()
        {
            // Clear before dropping the puzzle, so the tint is actually removed
            // rather than being stranded on whatever was last pointed at.
            ClearHover();

            _puzzle = null;
            _isResolved = false;

            _sequenceChosen.Clear();
            _sequencePool.Clear();

            SetInteractable(true);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void SetInteractable(bool interactable)
        {
            if (optionButtons != null)
            {
                foreach (Button b in optionButtons) if (b != null) b.interactable = interactable;
            }

            if (sequenceButtons != null)
            {
                foreach (Button b in sequenceButtons) if (b != null) b.interactable = interactable;
            }

            if (sequenceSubmitButton != null) sequenceSubmitButton.interactable = interactable && _sequenceChosen.Count > 0;
            if (dragChip != null) dragChip.gameObject.SetActive(interactable && _puzzle != null && _puzzle.Type == PuzzleType.DragAndDropLabel);
        }

        private void SetFeedback(string message, Color color)
        {
            if (feedbackLabel == null) return;

            feedbackLabel.text = message;
            feedbackLabel.color = color;
        }

        /// <summary>Updates the attempt counter. Pass -1,-1 to increment the displayed count.</summary>
        private void SetAttempts(int used, int max)
        {
            if (attemptsLabel == null) return;

            if (used < 0)
            {
                attemptsLabel.text = string.Empty;   // message already carries the count
                return;
            }

            attemptsLabel.text = max > 0 ? $"Attempts allowed: {max}" : string.Empty;
        }
    }
}
