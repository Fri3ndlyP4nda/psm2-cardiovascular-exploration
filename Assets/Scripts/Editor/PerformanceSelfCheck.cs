using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// White-box verification of the Phase 3 measurement arithmetic.
    ///
    /// The scoring rule and the aggregate formulas are pure functions, so they
    /// can be checked without entering Play mode or touching a scene. This is
    /// the PSM1 section 27 white-box testing for the DDA input layer, and it is
    /// what backs the Phase 3 exit criterion "confirm that metrics are
    /// correctly calculated".
    ///
    /// Run with  PSM2 > Diagnostics > Run Performance Metric Self-Check,
    /// or headless via -executeMethod.
    ///
    /// This is a diagnostic, not a replacement for the Unity Test Framework -
    /// proper EditMode tests need the runtime scripts behind assembly
    /// definitions, which is Phase 10 work.
    /// </summary>
    public static class PerformanceSelfCheck
    {
        private static int _passed;
        private static int _failed;

        [MenuItem("PSM2/Diagnostics/Run Performance Metric Self-Check", priority = 70)]
        public static void Run()
        {
            _passed = 0;
            _failed = 0;

            var s = new ScoreSettings();   // defaults, as shipped

            CheckParTimes(s);
            CheckScoring(s);
            CheckPaceRatio(s);
            CheckLevelAggregates();

            string summary = $"[PSM2 SelfCheck] {_passed} passed, {_failed} failed.";
            if (_failed == 0) Debug.Log(summary);
            else Debug.LogError(summary);
        }

        // ------------------------------------------------------------------
        // Cases
        // ------------------------------------------------------------------

        private static void CheckParTimes(ScoreSettings s)
        {
            // 20s at complexity 1, +12s per step.
            Approx("par c1", s.ParSecondsFor(1), 20f);
            Approx("par c2", s.ParSecondsFor(2), 32f);
            Approx("par c3", s.ParSecondsFor(3), 44f);
        }

        private static void CheckScoring(ScoreSettings s)
        {
            // Instant, first attempt, no hints: full base + full speed bonus.
            Equal("c1 instant perfect", ScoreRules.Calculate(Result(1, true, 0f, 1, 0), s), 150);
            Equal("c3 instant perfect", ScoreRules.Calculate(Result(3, true, 0f, 1, 0), s), 350);

            // Exactly at par: base only, no bonus, no penalties.
            Equal("c1 at par, clean", ScoreRules.Calculate(Result(1, true, 20f, 1, 0), s), 100);

            // Half of par: half the speed bonus.
            Equal("c1 half par", ScoreRules.Calculate(Result(1, true, 10f, 1, 0), s), 125);

            // Two wrong attempts and one hint at par: 100 - 50 - 20.
            Equal("c1 penalised", ScoreRules.Calculate(Result(1, true, 20f, 3, 1), s), 30);

            // Penalties exceeding the base are floored, never negative.
            Equal("floor applies", ScoreRules.Calculate(Result(1, true, 20f, 5, 2), s), s.MinimumCorrectScore);

            // A failed puzzle scores zero, not a negative number.
            Equal("failed scores zero", ScoreRules.Calculate(Result(2, false, 30f, 3, 1), s), 0);

            // Slower than par loses the bonus but is not punished further.
            Equal("slower than par", ScoreRules.Calculate(Result(1, true, 999f, 1, 0), s), 100);
        }

        private static void CheckPaceRatio(ScoreSettings s)
        {
            Approx("pace at par", ScoreRules.PaceRatio(20f, 1, s), 1f);
            Approx("pace twice as fast", ScoreRules.PaceRatio(10f, 1, s), 0.5f);
            Approx("pace twice as slow", ScoreRules.PaceRatio(40f, 1, s), 2f);

            // Complexity-aware: 22s on a complexity-2 puzzle is inside par.
            Approx("pace scales with complexity", ScoreRules.PaceRatio(22f, 2, s), 22f / 32f);
        }

        private static void CheckLevelAggregates()
        {
            var record = new LevelPerformance { Level = LevelId.Level1_LeftVentricle };

            // Simulate: 3 puzzles resolved, 2 correct, 30s total, 4 wrong submissions.
            record.PuzzlesAttempted = 3;
            record.PuzzlesCorrect = 2;
            record.PuzzlesFailed = 1;
            record.IncorrectAnswers = 4;
            record.TotalResponseSeconds = 30f;

            Approx("accuracy 2/3", record.Accuracy01, 2f / 3f);
            Approx("mean response", record.AverageResponseSeconds, 10f);
            Approx("mean wrong per puzzle", record.AverageIncorrectPerPuzzle, 4f / 3f);

            // Empty record must not divide by zero.
            var empty = new LevelPerformance();
            Approx("empty accuracy", empty.Accuracy01, 0f);
            Approx("empty response", empty.AverageResponseSeconds, 0f);
            Equal("empty blood count", empty.LowestBloodCountOrZero, 0);

            // SessionData mirrors the same formulas.
            var session = new SessionData { PuzzlesAttempted = 4, PuzzlesCorrect = 3, TotalResponseTimeSeconds = 48f };
            Approx("session accuracy", session.Accuracy01, 0.75f);
            Approx("session mean response", session.AverageResponseTime, 12f);

            var emptySession = new SessionData();
            Approx("empty session accuracy", emptySession.Accuracy01, 0f);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static PuzzleResult Result(int complexity, bool correct, float seconds, int attempts, int hints)
        {
            return new PuzzleResult("selfcheck", LevelId.Level1_LeftVentricle, PuzzleType.MultipleChoice,
                                    complexity, correct, seconds, attempts, hints);
        }

        private static void Equal(string label, int actual, int expected)
        {
            if (actual == expected) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 SelfCheck] FAIL {label}: expected {expected}, got {actual}.");
        }

        private static void Approx(string label, float actual, float expected)
        {
            if (Mathf.Abs(actual - expected) < 0.0005f) { _passed++; return; }

            _failed++;
            Debug.LogError($"[PSM2 SelfCheck] FAIL {label}: expected {expected:0.####}, got {actual:0.####}.");
        }
    }
}
