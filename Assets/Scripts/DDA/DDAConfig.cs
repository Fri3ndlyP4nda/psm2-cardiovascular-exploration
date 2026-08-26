using Cardio.Core;
using UnityEngine;

namespace Cardio.DDA
{
    /// <summary>
    /// The complete rule set: the three tier assets plus every threshold and
    /// weight the DDAManager uses.
    ///
    /// Held as one asset in Resources so the runtime-created DDAManager can load
    /// it without Inspector wiring, and so the entire adaptive policy can be
    /// reviewed - and retuned during playtesting - from a single place. Nothing
    /// about the policy is compiled in.
    ///
    /// Deliberately rule-based, with no machine learning (PSM1 rule 6/7): every
    /// number here can be printed in the report and traced to a decision.
    /// </summary>
    [CreateAssetMenu(menuName = "Cardio/DDA Config", fileName = "DDAConfig")]
    public class DDAConfig : ScriptableObject
    {
        /// <summary>Resources path used by <see cref="DDAManager"/>.</summary>
        public const string ResourcePath = "DDA/DDAConfig";

        [Header("Tiers")]
        public DifficultySettings Easy;
        public DifficultySettings Medium;
        public DifficultySettings Hard;

        [Header("Master switch")]
        [Tooltip("Turn off to pin the difficulty and play at a fixed tier - used for the control group in evaluation.")]
        public bool AdaptiveEnabled = true;

        [Header("Performance score weights (max 100)")]
        [Tooltip("Points awarded at 100% recent accuracy.")]
        [Range(0f, 100f)] public float AccuracyWeight = 70f;

        [Tooltip("Points awarded for answering instantly. Half of this is awarded at exactly par.")]
        [Range(0f, 100f)] public float SpeedWeight = 30f;

        [Tooltip("Points deducted per consecutive failed puzzle.")]
        [Range(0f, 100f)] public float FailurePenaltyPerFailure = 20f;

        [Header("Decision thresholds")]
        [Tooltip("Score at or above which the tier moves up.")]
        [Range(0f, 100f)] public float PromoteScore = 75f;

        [Tooltip("Score at or below which the tier moves down.")]
        [Range(0f, 100f)] public float DemoteScore = 40f;

        [Tooltip("Consecutive failures that force a demotion regardless of score (PSM1 section 11).")]
        [Range(1, 10)] public int DemoteOnConsecutiveFailures = 3;

        [Tooltip("A promotion is blocked if the player has more consecutive failures than this.")]
        [Range(0, 5)] public int PromoteMaxConsecutiveFailures = 1;

        [Header("Stability")]
        [Tooltip("Puzzles that must be resolved in a level before any adjustment is considered.")]
        [Range(1, 10)] public int MinPuzzlesBeforeAdjusting = 3;

        [Tooltip("Puzzles that must pass between two tier changes, to stop the tier oscillating.")]
        [Range(0, 10)] public int MinPuzzlesBetweenChanges = 2;

        /// <summary>Returns the asset for a tier, or null when it has not been assigned.</summary>
        public DifficultySettings For(DifficultyTier tier)
        {
            switch (tier)
            {
                case DifficultyTier.Easy: return Easy;
                case DifficultyTier.Medium: return Medium;
                case DifficultyTier.Hard: return Hard;
                default: return Medium;
            }
        }

        /// <summary>True when all three tiers are present.</summary>
        public bool IsComplete => Easy != null && Medium != null && Hard != null;

        /// <summary>
        /// The next tier up or down, clamped at the ends. Returns the same tier
        /// when already at the boundary, which the manager reports as a hold.
        /// </summary>
        public static DifficultyTier Step(DifficultyTier tier, int direction)
        {
            int value = Mathf.Clamp((int)tier + direction, (int)DifficultyTier.Easy, (int)DifficultyTier.Hard);
            return (DifficultyTier)value;
        }
    }
}
