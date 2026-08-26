using System;
using System.Collections.Generic;
using UnityEngine;

namespace Cardio.Data
{
    /// <summary>
    /// One puzzle, stored as an asset so educational content lives outside
    /// gameplay code (PSM1 rule 25). A lecturer can correct the wording of a
    /// question in the Inspector without a programmer or a recompile.
    ///
    /// Fields are public because this is a data container, not a behaviour -
    /// that also lets the question-bank seeder assign them directly instead of
    /// going through SerializedObject.
    ///
    /// Answer validation lives here rather than in PuzzleManager so each format
    /// owns its own correctness rule, and so the rules can be unit tested
    /// without a scene.
    /// </summary>
    [CreateAssetMenu(menuName = "Cardio/Puzzle", fileName = "Puzzle_New")]
    public class PuzzleData : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key. Objectives reference puzzles by this id.")]
        public string PuzzleId = "puzzle_id";

        public PuzzleType Type = PuzzleType.IdentifyStructure;

        [Tooltip("1 = easy, 3 = hard. From Phase 4 the DDAManager filters the bank by this value.")]
        [Range(1, 3)]
        public int Complexity = 1;

        [Header("Question")]
        [TextArea(2, 4)]
        public string Prompt = "";

        [Header("Structure puzzles (Identify / DragAndDrop / ValveIdentification)")]
        [Tooltip("Must match an AnatomyMarker.StructureId present in the level scene.")]
        public string TargetStructureId = "";

        [Tooltip("Text shown on the draggable chip. Defaults to the structure name when blank.")]
        public string LabelText = "";

        [Header("Multiple choice")]
        public string[] Options = Array.Empty<string>();
        public int CorrectOptionIndex;

        [Header("Blood flow sequence (list in the CORRECT order)")]
        public string[] SequenceSteps = Array.Empty<string>();

        [Header("Feedback")]
        [Tooltip("Shown after answering. This is where the actual teaching happens.")]
        [TextArea(2, 5)]
        public string Explanation = "";

        [Tooltip("Shown when the player asks for help, or when the DDA offers it (Phase 4).")]
        [TextArea(2, 4)]
        public string Hint = "";

        // ------------------------------------------------------------------
        // Validation
        // ------------------------------------------------------------------

        /// <summary>Correctness rule for the structure-picking formats.</summary>
        public bool IsCorrectStructure(string structureId)
        {
            if (string.IsNullOrEmpty(structureId) || string.IsNullOrEmpty(TargetStructureId)) return false;
            return string.Equals(structureId.Trim(), TargetStructureId.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Correctness rule for <see cref="PuzzleType.MultipleChoice"/>.</summary>
        public bool IsCorrectOption(int index) => index == CorrectOptionIndex && index >= 0 && index < Options.Length;

        /// <summary>
        /// Correctness rule for <see cref="PuzzleType.BloodFlowSequence"/>.
        /// The submitted order must match <see cref="SequenceSteps"/> exactly.
        /// </summary>
        public bool IsCorrectSequence(IReadOnlyList<string> submitted)
        {
            if (submitted == null || SequenceSteps == null) return false;
            if (submitted.Count != SequenceSteps.Length) return false;

            for (int i = 0; i < SequenceSteps.Length; i++)
            {
                if (!string.Equals(submitted[i]?.Trim(), SequenceSteps[i]?.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>Chip text for drag-and-drop, falling back to the target id when unset.</summary>
        public string ResolveLabelText()
        {
            return string.IsNullOrWhiteSpace(LabelText) ? TargetStructureId.Replace('_', ' ') : LabelText;
        }

        /// <summary>
        /// Catches authoring mistakes (an option index out of range, a structure
        /// puzzle with no target) before they reach a player. Called by the
        /// question-bank validator.
        /// </summary>
        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(PuzzleId)) { error = "PuzzleId is empty."; return false; }
            if (string.IsNullOrWhiteSpace(Prompt)) { error = $"'{PuzzleId}' has no prompt."; return false; }

            if (Type.UsesWorldPicking() && string.IsNullOrWhiteSpace(TargetStructureId))
            {
                error = $"'{PuzzleId}' is a structure puzzle but TargetStructureId is empty.";
                return false;
            }

            if (Type == PuzzleType.MultipleChoice)
            {
                if (Options == null || Options.Length < 2) { error = $"'{PuzzleId}' needs at least 2 options."; return false; }
                if (CorrectOptionIndex < 0 || CorrectOptionIndex >= Options.Length)
                {
                    error = $"'{PuzzleId}' has CorrectOptionIndex {CorrectOptionIndex} outside 0..{Options.Length - 1}.";
                    return false;
                }
            }

            if (Type == PuzzleType.BloodFlowSequence && (SequenceSteps == null || SequenceSteps.Length < 3))
            {
                error = $"'{PuzzleId}' needs at least 3 sequence steps.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
