using Cardio.Core;
using Cardio.DDA;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Drives the difficulty policy with simulated performance and asserts that
    /// the tier moves in both directions for a stated reason.
    ///
    /// This is the Phase 4 exit criterion, verified without a scene: because
    /// <see cref="DDARules"/> is a pure function, a whole player career can be
    /// simulated in a few milliseconds. It runs against the *seeded* DDAConfig
    /// asset, so it validates the values the game will actually ship with, not
    /// a set of test doubles.
    ///
    /// Every case prints the full decision line, so the Console output doubles
    /// as the decision table for the PSM2 report.
    /// </summary>
    public static class DDASelfCheck
    {
        private static int _passed;
        private static int _failed;

        [MenuItem("PSM2/Diagnostics/Run DDA Policy Self-Check", priority = 71)]
        public static void Run()
        {
            _passed = 0;
            _failed = 0;

            DDAConfig config = Resources.Load<DDAConfig>(DDAConfig.ResourcePath);
            if (config == null)
            {
                Debug.LogError($"[PSM2 DDACheck] No DDAConfig at Resources/{DDAConfig.ResourcePath}. " +
                               "Run PSM2 > Setup > Build or Rebuild Project first.");
                return;
            }

            if (!config.IsComplete)
            {
                Debug.LogError("[PSM2 DDACheck] DDAConfig is missing tier assets.");
                return;
            }

            CheckHelpers();
            CheckPromotion(config);
            CheckDemotion(config);
            CheckHold(config);
            CheckOverridesAndGates(config);
            CheckTierValues(config);

            string summary = $"[PSM2 DDACheck] {_passed} passed, {_failed} failed.";
            if (_failed == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        // ------------------------------------------------------------------
        // Score helpers
        // ------------------------------------------------------------------

        private static void CheckHelpers()
        {
            // Par scores half the speed weight; instant scores full; twice par scores nothing.
            Approx("speed at instant", DDARules.NormalisedSpeed(0f), 1f);
            Approx("speed at par", DDARules.NormalisedSpeed(1f), 0.5f);
            Approx("speed at twice par", DDARules.NormalisedSpeed(2f), 0f);
            Approx("speed clamps below zero", DDARules.NormalisedSpeed(5f), 0f);
        }

        private static void CheckTierValues(DDAConfig config)
        {
            // The adaptive effects must actually differ between tiers, or the
            // whole mechanism is decorative.
            True("easy is gentler than hard on obstacles",
                config.Easy.ObstacleSpeedMultiplier < config.Hard.ObstacleSpeedMultiplier);
            True("easy is gentler than hard on hazards",
                config.Easy.HazardDamageMultiplier < config.Hard.HazardDamageMultiplier);
            True("easy allows more attempts than hard",
                config.Easy.MaxPuzzleAttempts > config.Hard.MaxPuzzleAttempts);
            True("easy caps complexity below hard",
                config.Easy.MaxPuzzleComplexity < config.Hard.MaxPuzzleComplexity);
            True("easy gives more time than hard",
                config.Easy.ResponseTimeAllowance > config.Hard.ResponseTimeAllowance);
            True("easy offers automatic hints", config.Easy.OffersAutomaticHints);
            False("hard does not offer automatic hints", config.Hard.OffersAutomaticHints);
        }

        // ------------------------------------------------------------------
        // Direction: up
        // ------------------------------------------------------------------

        private static void CheckPromotion(DDAConfig config)
        {
            // Strong play: perfect accuracy, twice as fast as par, no failures.
            var strong = Snapshot(accuracy: 1f, pace: 0.5f, failures: 0, resolved: 5);

            DDADecision fromEasy = DDARules.Evaluate(strong, DifficultyTier.Easy, config, int.MaxValue);
            Report("promote from Easy", fromEasy);
            Equal("promote from Easy acts", (int)fromEasy.Action, (int)DDAAction.Promote);
            Equal("promote from Easy lands on Medium", (int)fromEasy.ToTier, (int)DifficultyTier.Medium);

            DDADecision fromMedium = DDARules.Evaluate(strong, DifficultyTier.Medium, config, int.MaxValue);
            Report("promote from Medium", fromMedium);
            Equal("promote from Medium acts", (int)fromMedium.Action, (int)DDAAction.Promote);
            Equal("promote from Medium lands on Hard", (int)fromMedium.ToTier, (int)DifficultyTier.Hard);

            // Already at the ceiling: holds rather than pretending to promote.
            DDADecision atCeiling = DDARules.Evaluate(strong, DifficultyTier.Hard, config, int.MaxValue);
            Report("promote at ceiling", atCeiling);
            Equal("ceiling holds", (int)atCeiling.Action, (int)DDAAction.Hold);
            Equal("ceiling stays Hard", (int)atCeiling.ToTier, (int)DifficultyTier.Hard);
        }

        // ------------------------------------------------------------------
        // Direction: down
        // ------------------------------------------------------------------

        private static void CheckDemotion(DDAConfig config)
        {
            // Weak play: 30% accuracy, well over par, no streak yet.
            var weak = Snapshot(accuracy: 0.3f, pace: 1.6f, failures: 0, resolved: 6);

            DDADecision fromHard = DDARules.Evaluate(weak, DifficultyTier.Hard, config, int.MaxValue);
            Report("demote from Hard", fromHard);
            Equal("demote from Hard acts", (int)fromHard.Action, (int)DDAAction.Demote);
            Equal("demote from Hard lands on Medium", (int)fromHard.ToTier, (int)DifficultyTier.Medium);

            DDADecision fromMedium = DDARules.Evaluate(weak, DifficultyTier.Medium, config, int.MaxValue);
            Report("demote from Medium", fromMedium);
            Equal("demote from Medium acts", (int)fromMedium.Action, (int)DDAAction.Demote);
            Equal("demote from Medium lands on Easy", (int)fromMedium.ToTier, (int)DifficultyTier.Easy);

            DDADecision atFloor = DDARules.Evaluate(weak, DifficultyTier.Easy, config, int.MaxValue);
            Report("demote at floor", atFloor);
            Equal("floor holds", (int)atFloor.Action, (int)DDAAction.Hold);
            Equal("floor stays Easy", (int)atFloor.ToTier, (int)DifficultyTier.Easy);
        }

        // ------------------------------------------------------------------
        // Direction: neither
        // ------------------------------------------------------------------

        private static void CheckHold(DDAConfig config)
        {
            // Competent but not dominant: the band PSM1 describes as "maintain".
            var middling = Snapshot(accuracy: 0.65f, pace: 1f, failures: 0, resolved: 6);

            DDADecision decision = DDARules.Evaluate(middling, DifficultyTier.Medium, config, int.MaxValue);
            Report("hold at Medium", decision);
            Equal("middling holds", (int)decision.Action, (int)DDAAction.Hold);
            Equal("middling stays Medium", (int)decision.ToTier, (int)DifficultyTier.Medium);
            True("hold score sits between the thresholds",
                decision.Score > config.DemoteScore && decision.Score < config.PromoteScore);
        }

        // ------------------------------------------------------------------
        // Overrides, gates and stability
        // ------------------------------------------------------------------

        private static void CheckOverridesAndGates(DDAConfig config)
        {
            // 1. Consecutive failures demote even when accuracy looks healthy.
            var streak = Snapshot(accuracy: 0.9f, pace: 0.6f, failures: config.DemoteOnConsecutiveFailures, resolved: 8);
            DDADecision override1 = DDARules.Evaluate(streak, DifficultyTier.Hard, config, int.MaxValue);
            Report("consecutive-failure override", override1);
            Equal("failure streak demotes", (int)override1.Action, (int)DDAAction.Demote);

            // 2. ...and it beats the cooldown, so a stuck player is not made to wait.
            DDADecision override2 = DDARules.Evaluate(streak, DifficultyTier.Hard, config, 0);
            Report("override beats cooldown", override2);
            Equal("failure streak ignores cooldown", (int)override2.Action, (int)DDAAction.Demote);

            // 3. Too little data: defer rather than react to one lucky answer.
            var tiny = Snapshot(accuracy: 1f, pace: 0.2f, failures: 0, resolved: config.MinPuzzlesBeforeAdjusting - 1);
            DDADecision deferred = DDARules.Evaluate(tiny, DifficultyTier.Medium, config, int.MaxValue);
            Report("insufficient sample", deferred);
            Equal("small sample defers", (int)deferred.Action, (int)DDAAction.Deferred);

            // 4. Cooldown suppresses an otherwise valid promotion.
            var strong = Snapshot(accuracy: 1f, pace: 0.5f, failures: 0, resolved: 6);
            DDADecision cooling = DDARules.Evaluate(strong, DifficultyTier.Easy, config, 0);
            Report("cooldown", cooling);
            Equal("cooldown defers", (int)cooling.Action, (int)DDAAction.Deferred);

            // 5. Master switch off: never acts.
            DDAConfig disabled = Object.Instantiate(config);
            disabled.AdaptiveEnabled = false;
            DDADecision off = DDARules.Evaluate(strong, DifficultyTier.Easy, disabled, int.MaxValue);
            Report("adaptive disabled", off);
            Equal("disabled defers", (int)off.Action, (int)DDAAction.Deferred);
            Object.DestroyImmediate(disabled);

            // 6. A promotion-worthy score is still blocked by a failure streak.
            //    Needs a softened penalty to be reachable at all - with the
            //    shipped weight of 20/failure the score can never survive two
            //    failures, so this branch is a safety net for retuning rather
            //    than a path the default configuration takes.
            DDAConfig soft = Object.Instantiate(config);
            soft.FailurePenaltyPerFailure = 5f;
            var fastButShaky = Snapshot(accuracy: 1f, pace: 0.3f,
                                        failures: soft.PromoteMaxConsecutiveFailures + 1, resolved: 8);
            DDADecision blocked = DDARules.Evaluate(fastButShaky, DifficultyTier.Medium, soft, int.MaxValue);
            Report("promotion blocked by failures", blocked);
            Equal("blocked promotion holds", (int)blocked.Action, (int)DDAAction.Hold);
            True("blocked promotion scored above the threshold", blocked.Score >= soft.PromoteScore);
            Object.DestroyImmediate(soft);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static PerformanceSnapshot Snapshot(float accuracy, float pace, int failures, int resolved)
        {
            return new PerformanceSnapshot(
                sessionAccuracy01: accuracy,
                recentAccuracy01: accuracy,
                sessionAverageResponse: pace * 20f,
                recentAverageResponse: pace * 20f,
                recentPaceRatio: pace,
                consecutiveFailures: failures,
                hintsUsed: 0,
                puzzlesResolved: resolved,
                recentSampleSize: Mathf.Min(resolved, 5));
        }

        /// <summary>Prints the decision so the Console output is the decision table.</summary>
        private static void Report(string label, DDADecision decision)
        {
            Debug.Log($"[PSM2 DDACheck] {label,-32} {decision}");
        }

        private static void Equal(string label, int actual, int expected)
        {
            if (actual == expected) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 DDACheck] FAIL {label}: expected {expected}, got {actual}.");
        }

        private static void Approx(string label, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) < 0.0005f) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 DDACheck] FAIL {label}: expected {expected:0.####}, got {actual:0.####}.");
        }

        private static void True(string label, bool condition)
        {
            if (condition) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 DDACheck] FAIL {label}: expected true.");
        }

        private static void False(string label, bool condition) => True(label, !condition);
    }
}
