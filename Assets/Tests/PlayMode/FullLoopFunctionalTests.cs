using System.Collections;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// Phase 10: the full loop, played from start to finish in one go.
    ///
    /// Every other suite exercises a slice - a puzzle, a tier change, a route.
    /// This one plays a level the way a person would: solve each station in
    /// turn, watch the metrics and the difficulty respond, walk to the exit,
    /// and confirm the level closes out with progress unlocked and an attempt
    /// recorded for the dashboard.
    ///
    /// WHAT THIS IS NOT: it is not the manual TC-01..23 pass. It drives the
    /// managers through their public API, so it proves the systems are
    /// connected end to end, and proves nothing whatever about whether a person
    /// can operate the interface those systems sit behind. No button is
    /// clicked, no chip is dragged, nothing is rendered. The manual pass in
    /// TESTING.md is still owed in full.
    /// </summary>
    public class FullLoopFunctionalTests
    {
        private string _backup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            SaveManager save = GameManager.Instance.Save;
            if (System.IO.File.Exists(save.SavePath)) _backup = System.IO.File.ReadAllText(save.SavePath);
            save.ResetProgress();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;
            if (save != null)
            {
                if (_backup != null) System.IO.File.WriteAllText(save.SavePath, _backup);
                else if (System.IO.File.Exists(save.SavePath)) System.IO.File.Delete(save.SavePath);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator APlayerCanFinishLevelOne_AndEverySystemAgreesOnWhatHappened()
        {
            PuzzleManager puzzles = PuzzleManager.Instance;
            PerformanceTracker tracker = PerformanceTracker.Instance;
            ObjectiveManager objectives = ObjectiveManager.Instance;
            SaveManager save = GameManager.Instance.Save;

            int solved = 0;

            // ---- Solve every station the tier will let us open ----
            foreach (PuzzleStation station in Object.FindObjectsByType<PuzzleStation>(FindObjectsInactive.Include))
            {
                if (!puzzles.IsWithinComplexityCap(station.PuzzleId)) continue;
                if (!puzzles.BeginPuzzle(station.PuzzleId)) continue;

                yield return TestLevel.Frames(2);

                PuzzleData puzzle = puzzles.Current;
                Assert.IsNotNull(puzzle, "a puzzle that opened should be current");

                AnswerCorrectly(puzzles, puzzle);
                yield return TestLevel.Frames(3);

                solved++;

                // The panel holds for an explanation window; wait it out so the
                // next station opens cleanly rather than racing the close timer.
                yield return TestLevel.WaitUntil(() => !puzzles.IsPuzzleActive, 8f,
                                                 $"puzzle {puzzle.PuzzleId} to close");
            }

            Assert.Greater(solved, 0, "at least one station should be openable at the starting tier");

            // ---- The tracker should agree with what we just did ----
            LevelPerformance record = tracker.RecordFor(LevelId.Level1_LeftVentricle);
            Assert.AreEqual(solved, record.PuzzlesCorrect,
                            "every puzzle answered correctly should be counted exactly once");
            Assert.AreEqual(solved, record.PuzzlesAttempted,
                            "no puzzle should be double-counted (the Phase 6 re-entrancy regression)");
            Assert.Greater(record.Score, 0, "solving puzzles should score");

            // ---- Finish the remaining objectives and leave ----
            for (int i = 0; i < objectives.Objectives.Count; i++)
            {
                if (objectives.Objectives[i].Kind != ObjectiveKind.ReachExit) objectives.CompleteObjective(i);
            }
            yield return null;

            yield return TestLevel.PlacePlayer(new Vector3(0f, 1.5f, 26f));
            yield return TestLevel.WaitUntil(() => GameManager.Instance.State == GameState.LevelComplete, 8f,
                                             "the level to complete at the exit");

            // ---- Everything downstream should now agree ----
            Assert.IsTrue(objectives.AllComplete, "the exit objective should tick");
            Assert.IsTrue(save.IsLevelUnlocked(LevelId.Level2_Brain), "finishing Level 1 unlocks Level 2");
            Assert.Contains((int)LevelId.Level1_LeftVentricle, save.Progress.CompletedLevels,
                            "the level should be recorded as completed");

            Assert.AreEqual(1, save.Progress.SessionHistory.Count,
                            "the attempt should reach the dashboard history");
            SessionRecord history = save.Progress.SessionHistory[0];
            Assert.IsTrue(history.Completed);
            Assert.AreEqual(solved, history.PuzzlesCorrect,
                            "the dashboard record must match what the tracker measured");
        }

        [UnityTest]
        public IEnumerator TheAdaptiveLoopStaysConsistentAcrossAWholeLevel()
        {
            // The DDA's own promotion/demotion logic is covered by the policy
            // self-check and the integration tests. What is asserted here is
            // narrower and only checkable over a full level: that the tier the
            // player ends on is the tier the record and the session both report.
            PuzzleManager puzzles = PuzzleManager.Instance;

            foreach (PuzzleStation station in Object.FindObjectsByType<PuzzleStation>(FindObjectsInactive.Include))
            {
                if (!puzzles.IsWithinComplexityCap(station.PuzzleId)) continue;
                if (!puzzles.BeginPuzzle(station.PuzzleId)) continue;

                yield return TestLevel.Frames(2);
                AnswerCorrectly(puzzles, puzzles.Current);
                yield return TestLevel.WaitUntil(() => !puzzles.IsPuzzleActive, 8f, "puzzle to close");
            }

            GameManager.Instance.NotifyLevelCompleted(LevelId.Level1_LeftVentricle);
            yield return TestLevel.Frames(2);

            DifficultyTier sessionTier = GameManager.Instance.Session.CurrentDifficulty;
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            SessionRecord history = GameManager.Instance.Save.Progress.SessionHistory[0];

            Assert.AreEqual(sessionTier, record.FinalDifficulty,
                            "the level record should close on the tier the session is actually at");
            Assert.AreEqual((int)sessionTier, history.FinalDifficulty,
                            "the exported record must not disagree with the session about difficulty");
        }

        /// <summary>Answers whichever way the puzzle's own format requires.</summary>
        private static void AnswerCorrectly(PuzzleManager puzzles, PuzzleData puzzle)
        {
            switch (puzzle.Type)
            {
                case PuzzleType.MultipleChoice:
                    puzzles.SubmitOption(puzzle.CorrectOptionIndex);
                    break;

                case PuzzleType.BloodFlowSequence:
                    puzzles.SubmitSequence(puzzle.SequenceSteps);
                    break;

                default:
                    puzzles.SubmitStructure(puzzle.TargetStructureId);
                    break;
            }
        }
    }
}
