namespace Cardio.DDA
{
    /// <summary>
    /// A read-only view of how the player is doing *right now*.
    ///
    /// This is the single input Phase 4's DDAManager will read. It is separated
    /// from <see cref="LevelPerformance"/> on purpose: the DDA must react to
    /// recent form, not to a whole-session average that a strong opening would
    /// keep propping up for the rest of the level.
    ///
    /// Everything here is measurement. No thresholds, no difficulty decisions -
    /// those belong to the DDA so that the rule set stays in one readable place.
    /// </summary>
    public readonly struct PerformanceSnapshot
    {
        /// <summary>Accuracy across the whole level so far, 0..1.</summary>
        public readonly float SessionAccuracy01;

        /// <summary>Accuracy across the most recent puzzles only, 0..1.</summary>
        public readonly float RecentAccuracy01;

        /// <summary>Mean response time across the whole level, seconds.</summary>
        public readonly float SessionAverageResponse;

        /// <summary>Mean response time across the most recent puzzles, seconds.</summary>
        public readonly float RecentAverageResponse;

        /// <summary>
        /// Response time relative to what the puzzle's complexity allows.
        /// Below 1 is faster than par, above 1 is slower. Normalising here means
        /// the DDA can compare a hard puzzle against an easy one fairly.
        /// </summary>
        public readonly float RecentPaceRatio;

        /// <summary>Current run of consecutive failed puzzles. Reset by any correct answer.</summary>
        public readonly int ConsecutiveFailures;

        /// <summary>Hints taken this level.</summary>
        public readonly int HintsUsed;

        /// <summary>Puzzles resolved this level. The DDA should not act on a tiny sample.</summary>
        public readonly int PuzzlesResolved;

        /// <summary>Number of puzzles the recent-window figures are based on.</summary>
        public readonly int RecentSampleSize;

        public PerformanceSnapshot(float sessionAccuracy01, float recentAccuracy01,
                                   float sessionAverageResponse, float recentAverageResponse,
                                   float recentPaceRatio, int consecutiveFailures,
                                   int hintsUsed, int puzzlesResolved, int recentSampleSize)
        {
            SessionAccuracy01 = sessionAccuracy01;
            RecentAccuracy01 = recentAccuracy01;
            SessionAverageResponse = sessionAverageResponse;
            RecentAverageResponse = recentAverageResponse;
            RecentPaceRatio = recentPaceRatio;
            ConsecutiveFailures = consecutiveFailures;
            HintsUsed = hintsUsed;
            PuzzlesResolved = puzzlesResolved;
            RecentSampleSize = recentSampleSize;
        }

        public override string ToString()
        {
            return $"accuracy {SessionAccuracy01 * 100f:0.#}% (recent {RecentAccuracy01 * 100f:0.#}%), " +
                   $"pace {RecentPaceRatio:0.00}x par, {ConsecutiveFailures} consecutive failures, " +
                   $"{HintsUsed} hints, n={PuzzlesResolved}";
        }
    }
}
