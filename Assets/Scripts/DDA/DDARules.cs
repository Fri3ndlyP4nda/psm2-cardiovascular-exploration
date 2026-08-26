using Cardio.Core;
using UnityEngine;

namespace Cardio.DDA
{
    /// <summary>
    /// The difficulty policy, as a pure function.
    ///
    /// Separated from <see cref="DDAManager"/> on purpose: the manager deals
    /// with Unity lifecycle, subscriptions and applying the result, while this
    /// class contains only arithmetic and IF rules. That means the policy can
    /// be evaluated against simulated input with no scene running, which is how
    /// the Phase 4 exit criterion is verified and how the decision table in the
    /// PSM2 report is produced.
    ///
    /// The score is deliberately simple enough to reproduce by hand:
    ///
    ///     score = AccuracyWeight x recentAccuracy
    ///           + SpeedWeight    x normalisedSpeed
    ///           - FailurePenalty x consecutiveFailures        (clamped 0..100)
    ///
    /// where normalisedSpeed maps the tier-adjusted pace ratio onto 0..1:
    /// instant = 1.0, exactly par = 0.5, twice par or slower = 0.
    /// </summary>
    public static class DDARules
    {
        /// <summary>
        /// Converts a pace ratio into a 0..1 speed score.
        /// Par (1.0) deliberately scores 0.5 rather than 0, because answering
        /// at the expected speed is competent, not poor.
        /// </summary>
        public static float NormalisedSpeed(float paceRatio)
        {
            return Mathf.Clamp01(1f - paceRatio * 0.5f);
        }

        /// <summary>
        /// Pace judged against the current tier's allowance. An Easy tier with a
        /// 1.5x allowance treats 1.5x par as "on time", so a player can earn a
        /// promotion out of Easy by being fast *for Easy*.
        /// </summary>
        public static float TierAdjustedPace(float paceRatio, DifficultySettings tier)
        {
            float allowance = tier != null ? Mathf.Max(0.01f, tier.ResponseTimeAllowance) : 1f;
            return paceRatio / allowance;
        }

        /// <summary>
        /// Runs the policy and returns the decision, including the arithmetic
        /// behind it. Does not mutate anything.
        /// </summary>
        /// <param name="puzzlesSinceLastChange">
        /// Used for the anti-oscillation cooldown. Pass int.MaxValue to ignore it.
        /// </param>
        public static DDADecision Evaluate(PerformanceSnapshot snapshot, DifficultyTier currentTier,
                                           DDAConfig config, int puzzlesSinceLastChange)
        {
            if (config == null)
            {
                return new DDADecision(DDAAction.Deferred, currentTier, currentTier, 0f, 0f, 0f, 0f,
                                       snapshot, "No DDA config loaded.");
            }

            // ---- Score ----
            DifficultySettings tierSettings = config.For(currentTier);
            float adjustedPace = TierAdjustedPace(snapshot.RecentPaceRatio, tierSettings);

            float accuracyPoints = config.AccuracyWeight * snapshot.RecentAccuracy01;
            float speedPoints = config.SpeedWeight * NormalisedSpeed(adjustedPace);
            float failurePenalty = config.FailurePenaltyPerFailure * snapshot.ConsecutiveFailures;
            float score = Mathf.Clamp(accuracyPoints + speedPoints - failurePenalty, 0f, 100f);

            // ---- Gates that stop the tier thrashing ----
            if (!config.AdaptiveEnabled)
            {
                return Decide(DDAAction.Deferred, currentTier, currentTier,
                    "Adaptive difficulty is switched off in the config.");
            }

            if (snapshot.PuzzlesResolved < config.MinPuzzlesBeforeAdjusting)
            {
                return Decide(DDAAction.Deferred, currentTier, currentTier,
                    $"Only {snapshot.PuzzlesResolved} puzzle(s) resolved; need {config.MinPuzzlesBeforeAdjusting} before adjusting.");
            }

            // ---- Hard override: repeated failure always lowers difficulty ----
            // Checked before the cooldown, because a player who is stuck should
            // not have to wait out a timer to get help (PSM1 section 11).
            if (snapshot.ConsecutiveFailures >= config.DemoteOnConsecutiveFailures)
            {
                DifficultyTier down = DDAConfig.Step(currentTier, -1);
                return Decide(down == currentTier ? DDAAction.Hold : DDAAction.Demote, currentTier, down,
                    $"{snapshot.ConsecutiveFailures} consecutive failures (limit {config.DemoteOnConsecutiveFailures})" +
                    (down == currentTier ? " but already at the easiest tier." : "."));
            }

            if (puzzlesSinceLastChange < config.MinPuzzlesBetweenChanges)
            {
                return Decide(DDAAction.Deferred, currentTier, currentTier,
                    $"Cooling down: {puzzlesSinceLastChange} of {config.MinPuzzlesBetweenChanges} puzzles since the last change.");
            }

            // ---- Normal score-driven rules ----
            if (score <= config.DemoteScore)
            {
                DifficultyTier down = DDAConfig.Step(currentTier, -1);
                return Decide(down == currentTier ? DDAAction.Hold : DDAAction.Demote, currentTier, down,
                    $"Score {score:0.#} at or below the demote threshold {config.DemoteScore:0.#}" +
                    (down == currentTier ? " but already at the easiest tier." : "."));
            }

            if (score >= config.PromoteScore)
            {
                if (snapshot.ConsecutiveFailures > config.PromoteMaxConsecutiveFailures)
                {
                    return Decide(DDAAction.Hold, currentTier, currentTier,
                        $"Score {score:0.#} would promote, but {snapshot.ConsecutiveFailures} recent failures block it.");
                }

                DifficultyTier up = DDAConfig.Step(currentTier, +1);
                return Decide(up == currentTier ? DDAAction.Hold : DDAAction.Promote, currentTier, up,
                    $"Score {score:0.#} at or above the promote threshold {config.PromoteScore:0.#}" +
                    (up == currentTier ? " but already at the hardest tier." : "."));
            }

            return Decide(DDAAction.Hold, currentTier, currentTier,
                $"Score {score:0.#} sits between {config.DemoteScore:0.#} and {config.PromoteScore:0.#}; difficulty is well matched.");

            // Local helper so every return path carries the same arithmetic.
            DDADecision Decide(DDAAction action, DifficultyTier from, DifficultyTier to, string reason)
            {
                return new DDADecision(action, from, to, score, accuracyPoints, speedPoints, failurePenalty,
                                       snapshot, reason);
            }
        }
    }
}
