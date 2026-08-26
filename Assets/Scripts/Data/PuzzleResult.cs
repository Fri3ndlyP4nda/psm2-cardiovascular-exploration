using Cardio.Core;

namespace Cardio.Data
{
    /// <summary>
    /// The outcome of one puzzle attempt sequence, emitted by PuzzleManager the
    /// moment a puzzle is resolved.
    ///
    /// This struct is the contract between gameplay and measurement. Phase 3's
    /// PerformanceTracker subscribes to it and needs nothing else to compute
    /// accuracy, mean response time and failure counts; Phase 4's DDAManager
    /// then reads those aggregates. Keeping it a plain value type means the
    /// measurement layer can never accidentally mutate live puzzle state.
    /// </summary>
    public readonly struct PuzzleResult
    {
        public readonly string PuzzleId;
        public readonly LevelId Level;
        public readonly PuzzleType Type;
        public readonly int Complexity;

        /// <summary>True when the puzzle was ultimately answered correctly.</summary>
        public readonly bool Correct;

        /// <summary>Seconds from the puzzle being presented to it being resolved.</summary>
        public readonly float ResponseSeconds;

        /// <summary>Total answers submitted, including the successful one. Always >= 1.</summary>
        public readonly int Attempts;

        /// <summary>Hints the player asked for. These carry a score penalty.</summary>
        public readonly int HintsUsed;

        /// <summary>
        /// Hints offered automatically by HintManager because the DDA judged the
        /// player to be struggling. Recorded for the evaluation but not
        /// penalised - the player did not choose to take them.
        /// </summary>
        public readonly int AutoHints;

        /// <summary>Total assistance shown, however it was triggered.</summary>
        public int TotalHints => HintsUsed + AutoHints;

        public PuzzleResult(string puzzleId, LevelId level, PuzzleType type, int complexity,
                            bool correct, float responseSeconds, int attempts, int hintsUsed,
                            int autoHints = 0)
        {
            PuzzleId = puzzleId;
            Level = level;
            Type = type;
            Complexity = complexity;
            Correct = correct;
            ResponseSeconds = responseSeconds;
            Attempts = attempts;
            HintsUsed = hintsUsed;
            AutoHints = autoHints;
        }

        /// <summary>Single-line form used for the Console trace and, later, the session log.</summary>
        public override string ToString()
        {
            string hints = AutoHints > 0
                ? $"{HintsUsed} hint(s) + {AutoHints} auto"
                : $"{HintsUsed} hint(s)";

            return $"{PuzzleId} ({Type}, c{Complexity}) -> {(Correct ? "CORRECT" : "INCORRECT")} " +
                   $"in {ResponseSeconds:0.00}s, {Attempts} attempt(s), {hints}";
        }
    }
}
