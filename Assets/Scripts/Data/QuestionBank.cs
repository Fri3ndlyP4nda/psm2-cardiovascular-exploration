using System.Collections.Generic;
using Cardio.Core;
using UnityEngine;

namespace Cardio.Data
{
    /// <summary>
    /// The set of puzzles for one level. One asset per level, referenced by
    /// that level's PuzzleManager.
    ///
    /// Selection is filtered by complexity so that Phase 4's DDAManager can
    /// raise or lower the difficulty of the *questions* (not just the
    /// obstacles) by changing a single integer.
    /// </summary>
    [CreateAssetMenu(menuName = "Cardio/Question Bank", fileName = "QuestionBank_New")]
    public class QuestionBank : ScriptableObject
    {
        [Tooltip("Which level this bank belongs to. Used for session logging.")]
        public LevelId Level = LevelId.Level1_LeftVentricle;

        public List<PuzzleData> Puzzles = new List<PuzzleData>();

        public int Count => Puzzles?.Count ?? 0;

        /// <summary>Finds a puzzle by its stable id. Returns null when absent.</summary>
        public PuzzleData Find(string puzzleId)
        {
            if (string.IsNullOrEmpty(puzzleId) || Puzzles == null) return null;

            foreach (PuzzleData puzzle in Puzzles)
            {
                if (puzzle != null && puzzle.PuzzleId == puzzleId) return puzzle;
            }
            return null;
        }

        /// <summary>
        /// All puzzles at or below <paramref name="maxComplexity"/>, skipping any
        /// already answered. This is the hook the DDAManager drives in Phase 4.
        /// </summary>
        public List<PuzzleData> Query(int maxComplexity, ICollection<string> excludeIds = null)
        {
            var results = new List<PuzzleData>();
            if (Puzzles == null) return results;

            foreach (PuzzleData puzzle in Puzzles)
            {
                if (puzzle == null) continue;
                if (puzzle.Complexity > maxComplexity) continue;
                if (excludeIds != null && excludeIds.Contains(puzzle.PuzzleId)) continue;

                results.Add(puzzle);
            }
            return results;
        }

        /// <summary>
        /// Reports authoring errors across the whole bank. Run from
        /// PSM2 > Content > Validate Question Banks.
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            var seenIds = new HashSet<string>();

            if (Puzzles == null || Puzzles.Count == 0)
            {
                errors.Add($"{name}: bank is empty.");
                return errors;
            }

            foreach (PuzzleData puzzle in Puzzles)
            {
                if (puzzle == null)
                {
                    errors.Add($"{name}: contains an empty (null) puzzle slot.");
                    continue;
                }

                if (!puzzle.Validate(out string error)) errors.Add($"{name}: {error}");
                if (!seenIds.Add(puzzle.PuzzleId)) errors.Add($"{name}: duplicate PuzzleId '{puzzle.PuzzleId}'.");
            }

            return errors;
        }
    }
}
