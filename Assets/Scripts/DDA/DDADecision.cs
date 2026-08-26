using Cardio.Core;

namespace Cardio.DDA
{
    /// <summary>What the DDA decided to do after one evaluation.</summary>
    public enum DDAAction
    {
        Hold = 0,
        Promote = 1,
        Demote = 2,

        /// <summary>Evaluated, but deliberately took no action (too little data, or cooling down).</summary>
        Deferred = 3
    }

    /// <summary>
    /// A full record of one difficulty evaluation, including the arithmetic that
    /// produced it.
    ///
    /// PSM1 requires that "every difficulty change must have a measurable
    /// reason". This struct *is* that reason: it carries the score, each
    /// weighted contribution, and the rule that fired, so the Console log and
    /// the PSM2 report can both show exactly why the tier moved rather than
    /// asserting that it did.
    /// </summary>
    public readonly struct DDADecision
    {
        public readonly DDAAction Action;
        public readonly DifficultyTier FromTier;
        public readonly DifficultyTier ToTier;

        /// <summary>Final performance score, 0-100.</summary>
        public readonly float Score;

        /// <summary>Points contributed by accuracy.</summary>
        public readonly float AccuracyPoints;

        /// <summary>Points contributed by answering pace.</summary>
        public readonly float SpeedPoints;

        /// <summary>Points deducted for consecutive failures.</summary>
        public readonly float FailurePenalty;

        /// <summary>The snapshot the decision was made from.</summary>
        public readonly PerformanceSnapshot Snapshot;

        /// <summary>Plain-English statement of the rule that fired.</summary>
        public readonly string Reason;

        public DDADecision(DDAAction action, DifficultyTier fromTier, DifficultyTier toTier,
                           float score, float accuracyPoints, float speedPoints, float failurePenalty,
                           PerformanceSnapshot snapshot, string reason)
        {
            Action = action;
            FromTier = fromTier;
            ToTier = toTier;
            Score = score;
            AccuracyPoints = accuracyPoints;
            SpeedPoints = speedPoints;
            FailurePenalty = failurePenalty;
            Snapshot = snapshot;
            Reason = reason;
        }

        public bool ChangedTier => FromTier != ToTier;

        /// <summary>The line written to the Console for every evaluation.</summary>
        public override string ToString()
        {
            string movement = ChangedTier ? $"{FromTier} -> {ToTier}" : $"{FromTier} (unchanged)";

            return $"{Action}: {movement} | score {Score:0.#}/100 " +
                   $"(accuracy +{AccuracyPoints:0.#}, pace +{SpeedPoints:0.#}, failures -{FailurePenalty:0.#}) | {Reason}";
        }
    }
}
