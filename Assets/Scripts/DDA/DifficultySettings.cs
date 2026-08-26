using Cardio.Core;
using UnityEngine;

namespace Cardio.DDA
{
    /// <summary>How willing the game is to offer help at a given tier.</summary>
    public enum HintFrequency
    {
        /// <summary>Hints offered early and automatically, structures highlighted.</summary>
        High = 0,

        /// <summary>Hints available on request; offered automatically only after repeated failure.</summary>
        Medium = 1,

        /// <summary>Hints available on request only. Nothing is offered unprompted.</summary>
        Low = 2
    }

    /// <summary>
    /// Every gameplay parameter that one difficulty tier controls.
    ///
    /// One asset per tier, so the whole difficulty curve can be retuned during
    /// playtesting without touching code (PSM1 rule 12 and section 26). The
    /// DDAManager never hard-codes a number - it swaps which of these three
    /// assets is active and pushes the values out to the systems that consume
    /// them.
    /// </summary>
    [CreateAssetMenu(menuName = "Cardio/Difficulty Settings", fileName = "Difficulty_New")]
    public class DifficultySettings : ScriptableObject
    {
        [Header("Identity")]
        public DifficultyTier Tier = DifficultyTier.Medium;

        [Header("Puzzles")]
        [Tooltip("Puzzles above this complexity are not offered at this tier.")]
        [Range(1, 3)] public int MaxPuzzleComplexity = 2;

        [Tooltip("Wrong answers allowed before a puzzle resolves as failed.")]
        [Range(1, 10)] public int MaxPuzzleAttempts = 3;

        [Tooltip("Multiplier on the expected (par) answering time. Above 1 is more forgiving.")]
        [Range(0.5f, 2f)] public float ResponseTimeAllowance = 1f;

        [Header("Environment")]
        [Tooltip("Multiplier on moving-obstacle speed. Consumed by the A* agents in Phase 5.")]
        [Range(0.25f, 3f)] public float ObstacleSpeedMultiplier = 1f;

        [Tooltip("Multiplier on damage dealt by hazards such as fatty plaque.")]
        [Range(0.25f, 3f)] public float HazardDamageMultiplier = 1f;

        [Header("Assistance")]
        public HintFrequency HintFrequency = HintFrequency.Medium;

        [Tooltip("Seconds on a puzzle before a hint is offered unprompted. 0 disables time-based hints.")]
        [Range(0f, 120f)] public float AutoHintDelaySeconds = 25f;

        [Tooltip("Wrong answers on a puzzle before a hint is offered unprompted. 0 disables failure-based hints.")]
        [Range(0, 5)] public int AutoHintAfterFailedAttempts = 2;

        [Tooltip("Whether an offered hint also makes the target structure glow.")]
        public bool HighlightStructureOnHint = true;

        /// <summary>True when this tier ever offers help without being asked.</summary>
        public bool OffersAutomaticHints =>
            HintFrequency != HintFrequency.Low &&
            (AutoHintDelaySeconds > 0f || AutoHintAfterFailedAttempts > 0);

        /// <summary>One-line description used in the change log and the report.</summary>
        public string Describe()
        {
            return $"{Tier} (complexity<={MaxPuzzleComplexity}, attempts={MaxPuzzleAttempts}, " +
                   $"obstacles x{ObstacleSpeedMultiplier:0.##}, hazards x{HazardDamageMultiplier:0.##}, " +
                   $"hints={HintFrequency})";
        }
    }
}
