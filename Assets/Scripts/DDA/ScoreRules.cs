using System;
using Cardio.Data;
using UnityEngine;

namespace Cardio.DDA
{
    /// <summary>
    /// Tunable weights for <see cref="ScoreRules"/>. Serialized on
    /// PerformanceTracker so every number is editable in the Inspector rather
    /// than compiled in (PSM1 implementation rule 12).
    /// </summary>
    [Serializable]
    public class ScoreSettings
    {
        [Header("Base award")]
        [Tooltip("Points for a correct answer, multiplied by the puzzle's complexity (1-3).")]
        [Range(10, 500)] public int PointsPerComplexity = 100;

        [Header("Penalties")]
        [Tooltip("Deducted for each wrong submission on the puzzle.")]
        [Range(0, 200)] public int IncorrectAttemptPenalty = 25;

        [Tooltip("Deducted for each hint revealed on the puzzle.")]
        [Range(0, 200)] public int HintPenalty = 20;

        [Tooltip("Floor for a correct answer, so a hard-won solve is never worth nothing.")]
        [Range(0, 100)] public int MinimumCorrectScore = 10;

        [Header("Combat")]
        [Tooltip("Deducted for each leukemic blast destroyed while questions remain unanswered.")]
        [Range(0, 200)] public int HostileKillPenalty = 10;

        [Tooltip("Awarded for a kill once every question in the level is answered.")]
        [Range(0, 200)] public int ClearedLevelKillBonus = 25;

        [Header("Speed bonus")]
        [Tooltip("Maximum bonus for answering well inside the par time.")]
        [Range(0, 200)] public int MaxSpeedBonus = 50;

        [Tooltip("Par seconds for a complexity-1 puzzle. Higher complexities scale up.")]
        [Range(5f, 120f)] public float ParSecondsAtComplexity1 = 20f;

        [Tooltip("Extra par seconds granted per complexity step above 1.")]
        [Range(0f, 60f)] public float ParSecondsPerComplexity = 12f;

        /// <summary>Seconds a puzzle of this complexity is expected to take.</summary>
        public float ParSecondsFor(int complexity)
        {
            int steps = Mathf.Max(0, complexity - 1);
            return ParSecondsAtComplexity1 + steps * ParSecondsPerComplexity;
        }
    }

    /// <summary>
    /// The scoring rule, kept as a pure function so it can be reasoned about,
    /// tabulated in the report, and tested without a running scene.
    ///
    ///     correct:  (100 x complexity)
    ///               - (25 x wrong attempts)
    ///               - (20 x hints)
    ///               + speed bonus (up to 50, scaled by how far inside par)
    ///               floored at 10
    ///
    ///     failed:   0        (never negative - the Blood Count system already
    ///                         supplies the punishment for mistakes, and a
    ///                         negative score is discouraging without adding
    ///                         any information the DDA does not already have)
    ///
    /// Combat is scored separately, per kill rather than per puzzle:
    ///
    ///     kill while questions remain : -10   (the earned hint's cost)
    ///     kill once all are answered  : +25
    ///
    /// The kill penalty is deliberately half the HintPenalty. A hint from the
    /// old button was free and instant; an earned one is already paid for by the
    /// -25 wrong answer that spawned the blast, plus the Blood Count and time
    /// the fight costs. Charging the full 20 on top would make the replacement
    /// strictly worse than what it replaced.
    /// </summary>
    public static class ScoreRules
    {
        /// <summary>Points awarded for one resolved puzzle.</summary>
        public static int Calculate(PuzzleResult result, ScoreSettings settings)
        {
            if (settings == null) return 0;
            if (!result.Correct) return 0;

            int score = settings.PointsPerComplexity * Mathf.Clamp(result.Complexity, 1, 3);

            // Attempts includes the successful one, so wrong submissions is Attempts - 1.
            int wrongAttempts = Mathf.Max(0, result.Attempts - 1);
            score -= wrongAttempts * settings.IncorrectAttemptPenalty;
            score -= result.HintsUsed * settings.HintPenalty;

            score += SpeedBonus(result, settings);

            return Mathf.Max(settings.MinimumCorrectScore, score);
        }

        /// <summary>
        /// Linear bonus that reaches its maximum at an instant answer and
        /// decays to zero at par. Never negative: answering slowly loses the
        /// bonus rather than incurring a penalty, because slow-but-correct is
        /// still a correct answer.
        /// </summary>
        public static int SpeedBonus(PuzzleResult result, ScoreSettings settings)
        {
            float par = settings.ParSecondsFor(result.Complexity);
            if (par <= 0f) return 0;

            float fractionRemaining = Mathf.Clamp01((par - result.ResponseSeconds) / par);
            return Mathf.RoundToInt(settings.MaxSpeedBonus * fractionRemaining);
        }

        /// <summary>
        /// Response time as a multiple of par. 0.5 means twice as fast as
        /// expected, 2.0 means twice as slow. This is what the DDA reads so a
        /// complexity-3 puzzle is not judged against a complexity-1 clock.
        /// </summary>
        public static float PaceRatio(float responseSeconds, int complexity, ScoreSettings settings)
        {
            float par = settings.ParSecondsFor(complexity);
            if (par <= 0f) return 1f;

            return responseSeconds / par;
        }
    }
}
