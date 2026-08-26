using System.Collections;
using System.Collections.Generic;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// TC-11 (stations and interaction), TC-12/TC-13 (answering), TC-14
    /// (objectives and exit gating), TC-15 (hints) and TC-18 (metric capture).
    ///
    /// Everything here is asserted through manager state and through
    /// objectively observable scene facts - which GameObject is active, what a
    /// raycast resolves to. Nothing depends on how anything *looks*.
    /// </summary>
    public class PuzzleFlowTests
    {
        private TestInputSource _input;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            _input = new TestInputSource();
            PlayerInputReader.SetSource(_input);
        }

        [TearDown]
        public void TearDown() => PlayerInputReader.ClearSource();

        // ------------------------------------------------------------------
        // TC-11 stations and interaction
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ApproachingAStation_MakesItTheInteractionTarget_AndShowsThePrompt()
        {
            PuzzleStation station = FirstOpenStation();
            var interaction = TestLevel.Player.GetComponent<PlayerInteraction>();
            Assert.IsNotNull(interaction, "the player prefab should carry PlayerInteraction");

            GameObject prompt = TestLevel.Find("InteractionPrompt");
            Assert.IsNotNull(prompt, "the HUD should contain an InteractionPrompt object");
            Assert.IsFalse(prompt.activeSelf, "the prompt should start hidden");

            yield return TestLevel.PlacePlayer(station.transform.position + new Vector3(1.5f, 1.2f, 0f));
            yield return TestLevel.WaitUntil(() => interaction.Nearest != null, 3f, "station to be detected");

            Assert.AreSame(station, interaction.Nearest as PuzzleStation);
            Assert.IsTrue(prompt.activeSelf, "the HUD prompt should appear when a station is in range");

            // Walking away must clear it again.
            yield return TestLevel.PlacePlayer(new Vector3(0f, 1.2f, -6f));
            yield return TestLevel.WaitUntil(() => interaction.Nearest == null, 3f, "station to leave range");

            Assert.IsFalse(prompt.activeSelf, "the prompt should hide once out of range");
        }

        [UnityTest]
        public IEnumerator InteractKey_OpensThePuzzle_AndEntersPuzzleState()
        {
            PuzzleStation station = FirstOpenStation();
            var interaction = TestLevel.Player.GetComponent<PlayerInteraction>();

            yield return TestLevel.PlacePlayer(station.transform.position + new Vector3(1.5f, 1.2f, 0f));
            yield return TestLevel.WaitUntil(() => interaction.Nearest != null, 3f, "station to be detected");

            _input.PressInteract();
            yield return TestLevel.WaitUntil(() => PuzzleManager.Instance.IsPuzzleActive, 3f, "puzzle to open");

            Assert.AreEqual(GameState.Puzzle, GameManager.Instance.State, "opening a puzzle should enter Puzzle state");
            Assert.AreEqual(station.PuzzleId, PuzzleManager.Instance.Current.PuzzleId);

            // Puzzle mode keeps time running - it is not a pause. Whether the
            // cursor is really released is MANUAL REQUIRED: batch mode has no
            // window, so Cursor.lockState cannot be observed here.
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "puzzle mode is not a pause");
        }

        /// <summary>
        /// Regression test for a real defect: a station whose puzzle sits above
        /// the current complexity cap used to advertise "[E] Examine" and then
        /// do absolutely nothing when pressed.
        /// </summary>
        [UnityTest]
        public IEnumerator StationAboveTheComplexityCap_SaysSo_AndDoesNotOpenSilently()
        {
            PuzzleStation locked = FirstLockedStation();
            Assert.IsNotNull(locked, "at Easy some Level 1 stations should be above the complexity cap");

            Assert.IsTrue(locked.IsLockedByDifficulty);
            StringAssert.Contains("advanced", locked.InteractionPrompt,
                                  "a locked station must explain itself rather than promising an interaction");

            // Still offered, so it does not read as broken scenery.
            Assert.IsTrue(locked.CanInteract, "a locked station should still show a prompt");

            locked.Interact(TestLevel.Player.gameObject);
            yield return TestLevel.Frames(2);

            Assert.IsFalse(PuzzleManager.Instance.IsPuzzleActive, "a locked station must not open its puzzle");
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State);

            // Raising the tier unlocks it.
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test");
            yield return null;

            Assert.IsFalse(locked.IsLockedByDifficulty, "Hard should unlock every complexity");
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(locked.PuzzleId));
            PuzzleManager.Instance.AbandonPuzzle();
        }

        /// <summary>
        /// Repro for the reported soft-lock: pressing the interact key freezes
        /// the player and never gives control back.
        ///
        /// Every existing test drove PuzzleManager directly, so none of them
        /// ever needed the panel to be visible or the Escape key to work. This
        /// one goes through the real view.
        /// </summary>
        [UnityTest]
        public IEnumerator OpeningAPuzzle_ShowsThePanel_AndEscapeReturnsControl()
        {
            PuzzleStation station = FirstOpenStation();
            var interaction = TestLevel.Player.GetComponent<PlayerInteraction>();

            GameObject panel = TestLevel.Find("PuzzlePanel");
            Assert.IsNotNull(panel, "the level should contain the puzzle panel");

            yield return TestLevel.PlacePlayer(station.transform.position + new Vector3(1.5f, 1.2f, 0f));
            yield return TestLevel.WaitUntil(() => interaction.Nearest != null, 3f, "station to be detected");

            _input.PressInteract();
            yield return TestLevel.WaitUntil(() => PuzzleManager.Instance.IsPuzzleActive, 3f, "puzzle to open");
            yield return TestLevel.Frames(3);

            // The panel must actually be on screen, not merely "logically open".
            Assert.IsTrue(panel.activeInHierarchy,
                          "the puzzle panel must be visible once a puzzle opens");

            // Escape must hand control back.
            _input.PressPause();
            yield return TestLevel.WaitUntil(() => !PuzzleManager.Instance.IsPuzzleActive, 3f,
                                             "Escape to close the puzzle");

            Assert.AreEqual(GameState.Playing, GameManager.Instance.State,
                            "closing the puzzle must return the game to Playing");

            // And the player must genuinely be able to move again.
            Vector3 start = TestLevel.Player.transform.position;
            _input.MoveAxis = Vector2.up;
            yield return new WaitForSeconds(0.6f);
            _input.Reset();

            Assert.Greater(Vector3.Distance(start, TestLevel.Player.transform.position), 0.3f,
                           "the player must be able to move after closing a puzzle");
        }

        [UnityTest]
        public IEnumerator AbandoningAPuzzle_RecordsNothing_AndCanBeReopened()
        {
            PuzzleData puzzle = FindPuzzle(complexity: 1);
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            int attemptedBefore = record.PuzzlesAttempted;

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
            yield return null;

            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            Assert.IsFalse(PuzzleManager.Instance.IsPuzzleActive);
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State, "abandoning should return to play");
            Assert.AreEqual(attemptedBefore, record.PuzzlesAttempted, "an abandoned puzzle must not be recorded");
            Assert.IsFalse(PuzzleManager.Instance.IsSolved(puzzle.PuzzleId));

            // And it must still be answerable.
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle), "an abandoned puzzle should reopen");
            PuzzleManager.Instance.AbandonPuzzle();
        }

        [UnityTest]
        public IEnumerator SolvingAStationsPuzzle_MarksItNonInteractive()
        {
            PuzzleStation station = FirstOpenStation();
            PuzzleData puzzle = PuzzleManager.Instance.Bank.Find(station.PuzzleId);
            Assert.IsNotNull(puzzle, $"station references unknown puzzle '{station.PuzzleId}'");

            Assert.IsTrue(station.CanInteract, "an unsolved station should be interactive");

            yield return AnswerPuzzle(puzzle, correctly: true);

            Assert.IsTrue(PuzzleManager.Instance.IsSolved(puzzle.PuzzleId));
            Assert.IsFalse(station.CanInteract, "a solved station should stop accepting interaction");
        }

        // ------------------------------------------------------------------
        // TC-12 structure picking against real geometry
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator StructurePicker_ResolvesRealGeometryToItsStructureId()
        {
            // Aim a camera straight down at the chamber floor, which is tagged
            // as the left ventricle, and pick through the centre of the screen.
            var probe = new GameObject("PickProbe", typeof(Camera));
            Camera camera = probe.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 12f, -4f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            yield return null;

            var centre = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f);
            AnatomyStructureTag hit = StructurePicker.Pick(centre, camera);

            Assert.IsNotNull(hit, "aiming at the chamber floor should resolve a structure");
            Assert.AreEqual("left_ventricle", hit.StructureId);
            Assert.IsNotNull(hit.Marker, "a tagged structure should link back to its marker");

            Object.DestroyImmediate(probe);
        }

        [UnityTest]
        public IEnumerator StructurePicker_ReturnsNullWhenAimedAtNothing()
        {
            var probe = new GameObject("PickProbe", typeof(Camera));
            Camera camera = probe.GetComponent<Camera>();
            camera.transform.position = new Vector3(0f, 60f, 0f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);   // at the sky

            yield return null;

            var centre = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f);
            Assert.IsNull(StructurePicker.Pick(centre, camera), "aiming at empty space should resolve nothing");

            Object.DestroyImmediate(probe);
        }

        [UnityTest]
        public IEnumerator WrongStructure_CountsAnAttempt_ThenExhaustingThemFailsThePuzzle()
        {
            PuzzleData puzzle = FindPuzzle(complexity: 1, PuzzleType.IdentifyStructure);
            Assert.IsNotNull(puzzle, "Level 1 should have a complexity-1 identify puzzle");

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            int wrongBefore = record.IncorrectAnswers;
            int failedBefore = record.PuzzlesFailed;

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
            yield return null;

            int allowed = PuzzleManager.Instance.MaxAttempts;

            PuzzleManager.Instance.SubmitStructure("not_a_real_structure");
            yield return null;

            Assert.AreEqual(wrongBefore + 1, record.IncorrectAnswers, "a wrong answer should be counted");
            Assert.IsTrue(PuzzleManager.Instance.IsAcceptingAnswers, "one wrong answer should not end the puzzle");

            for (int i = 1; i < allowed; i++)
            {
                PuzzleManager.Instance.SubmitStructure("not_a_real_structure");
                yield return null;
            }

            Assert.IsFalse(PuzzleManager.Instance.IsAcceptingAnswers, "attempts should be exhausted");
            Assert.AreEqual(failedBefore + 1, record.PuzzlesFailed, "the puzzle should be recorded as failed");

            yield return WaitForPanelToClose();
        }

        // ------------------------------------------------------------------
        // TC-13 the non-structure formats
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MultipleChoiceAndSequence_AcceptOnlyTheCorrectAnswer()
        {
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test: unlock every complexity");
            yield return null;

            PuzzleData choice = FindPuzzle(-1, PuzzleType.MultipleChoice);
            Assert.IsNotNull(choice);

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(choice));
            yield return null;
            PuzzleManager.Instance.SubmitOption((choice.CorrectOptionIndex + 1) % choice.Options.Length);
            yield return null;
            Assert.IsFalse(PuzzleManager.Instance.IsSolved(choice.PuzzleId), "a wrong option must not solve it");

            PuzzleManager.Instance.SubmitOption(choice.CorrectOptionIndex);
            yield return null;
            Assert.IsTrue(PuzzleManager.Instance.IsSolved(choice.PuzzleId), "the right option should solve it");
            yield return WaitForPanelToClose();

            PuzzleData flow = FindPuzzle(-1, PuzzleType.BloodFlowSequence);
            Assert.IsNotNull(flow);

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(flow));
            yield return null;

            var reversed = new List<string>(flow.SequenceSteps);
            reversed.Reverse();
            PuzzleManager.Instance.SubmitSequence(reversed);
            yield return null;
            Assert.IsFalse(PuzzleManager.Instance.IsSolved(flow.PuzzleId), "a reversed order must not solve it");

            PuzzleManager.Instance.SubmitSequence(flow.SequenceSteps);
            yield return null;
            Assert.IsTrue(PuzzleManager.Instance.IsSolved(flow.PuzzleId), "the right order should solve it");
            yield return WaitForPanelToClose();
        }

        // ------------------------------------------------------------------
        // TC-18 timing and metric capture
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ResponseTime_IsMeasuredFromPresentationToAnswer()
        {
            PuzzleData puzzle = FindPuzzle(complexity: 1);
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);

            float before = record.TotalResponseSeconds;

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));

            // Deliberately dwell, so a broken timer shows up as ~0.
            yield return new WaitForSecondsRealtime(1.2f);

            SubmitCorrect(puzzle);
            yield return null;

            float measured = record.TotalResponseSeconds - before;
            Assert.Greater(measured, 1.0f, $"response time should reflect the delay, measured {measured:0.00}s");
            Assert.Less(measured, 6f, "response time should not run away");

            yield return WaitForPanelToClose();
        }

        [UnityTest]
        public IEnumerator CorrectAnswer_UpdatesEveryMetric()
        {
            PuzzleData puzzle = FindPuzzle(complexity: 1);
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);

            int attemptedBefore = record.PuzzlesAttempted;
            int correctBefore = record.PuzzlesCorrect;
            int scoreBefore = record.Score;

            yield return AnswerPuzzle(puzzle, correctly: true);

            Assert.AreEqual(attemptedBefore + 1, record.PuzzlesAttempted);
            Assert.AreEqual(correctBefore + 1, record.PuzzlesCorrect);
            Assert.Greater(record.Score, scoreBefore, "a correct answer should score points");
            Assert.AreEqual(0, PerformanceTracker.Instance.ConsecutiveFailures, "a correct answer clears the streak");

            // And the session mirror the HUD reads must agree.
            Assert.AreEqual(record.PuzzlesCorrect, GameManager.Instance.Session.PuzzlesCorrect);
        }

        // ------------------------------------------------------------------
        // TC-15 hints
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator ManualHint_IsCountedAndPenalised_SeparatelyFromAutomaticHelp()
        {
            PuzzleData puzzle = FindPuzzle(complexity: 1);
            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);

            int hintsBefore = record.HintsUsed;
            int autoBefore = record.AutoHintsGiven;

            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
            yield return null;

            PuzzleManager.Instance.RequestHint();
            yield return null;

            Assert.AreEqual(hintsBefore + 1, record.HintsUsed, "a requested hint should be counted");
            Assert.AreEqual(autoBefore, record.AutoHintsGiven, "a requested hint is not an automatic one");
            Assert.AreEqual(record.HintsUsed, GameManager.Instance.Session.HintsUsed, "the session should agree");

            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            Assert.AreEqual(hintsBefore + 1, record.HintsUsed,
                            "a hint taken on an abandoned puzzle must still count");
        }

        [UnityTest]
        public IEnumerator AutomaticHint_FiresOnFailure_AtGenerousTiers_ButNotAtHard()
        {
            // Easy offers help after a single wrong answer.
            DDAManager.Instance.ForceTier(DifficultyTier.Easy, "test");
            yield return null;

            LevelPerformance record = PerformanceTracker.Instance.RecordFor(LevelId.Level1_LeftVentricle);
            int autoBefore = record.AutoHintsGiven;

            PuzzleData puzzle = FindPuzzle(complexity: 1, PuzzleType.IdentifyStructure);
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
            yield return null;

            PuzzleManager.Instance.SubmitStructure("not_a_real_structure");
            yield return TestLevel.Frames(3);

            Assert.AreEqual(autoBefore + 1, record.AutoHintsGiven,
                            "Easy should offer a hint unprompted after a wrong answer");
            Assert.AreEqual(GameManager.Instance.Session.AutoHintsGiven, record.AutoHintsGiven);

            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            // Hard offers nothing unprompted.
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test");
            yield return null;

            int autoAtHard = record.AutoHintsGiven;
            PuzzleData harder = FindPuzzle(-1, PuzzleType.IdentifyStructure);
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(harder));
            yield return null;

            PuzzleManager.Instance.SubmitStructure("not_a_real_structure");
            yield return TestLevel.Frames(5);

            Assert.AreEqual(autoAtHard, record.AutoHintsGiven, "Hard must not offer unprompted hints");

            PuzzleManager.Instance.AbandonPuzzle();
            yield return WaitForPanelToClose();
        }

        // ------------------------------------------------------------------
        // TC-14 objectives and exit gating
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SolvingAPuzzle_TicksOnlyItsOwnObjective()
        {
            ObjectiveManager objectives = ObjectiveManager.Instance;
            Assert.Greater(objectives.Objectives.Count, 0, "Level 1 should have objectives");
            Assert.AreEqual(0, objectives.CompletedCount, "objectives should start unticked");

            LevelObjective target = null;
            foreach (LevelObjective objective in objectives.Objectives)
            {
                if (objective.Kind == ObjectiveKind.Puzzle) { target = objective; break; }
            }
            Assert.IsNotNull(target, "Level 1 should have a puzzle objective");

            PuzzleData puzzle = PuzzleManager.Instance.Bank.Find(target.PuzzleId);
            Assert.IsNotNull(puzzle, $"objective references unknown puzzle '{target.PuzzleId}'");

            yield return AnswerPuzzle(puzzle, correctly: true);

            Assert.IsTrue(target.Completed, "the matching objective should tick");
            Assert.AreEqual(1, objectives.CompletedCount, "exactly one objective should have ticked");
        }

        [UnityTest]
        public IEnumerator TheExit_StaysShutUntilEveryObjectiveIsDone()
        {
            LevelController level = TestLevel.Level;
            ObjectiveManager objectives = ObjectiveManager.Instance;

            Assert.IsFalse(level.CanCompleteLevel(), "the exit should be gated at level start");
            StringAssert.Contains("outstanding", level.BlockedExitMessage());

            // Walking into the exit must not finish the level.
            yield return TestLevel.PlacePlayer(new Vector3(0f, 1.5f, 26f));
            yield return TestLevel.Frames(5);

            Assert.AreEqual(GameState.Playing, GameManager.Instance.State,
                            "reaching a gated exit must not complete the level");

            // Satisfy every non-exit objective directly - the puzzle-to-objective
            // link is covered by its own test.
            for (int i = 0; i < objectives.Objectives.Count; i++)
            {
                if (objectives.Objectives[i].Kind != ObjectiveKind.ReachExit) objectives.CompleteObjective(i);
            }
            yield return null;

            Assert.IsTrue(level.CanCompleteLevel(), "the exit should open once objectives are done");
        }

        [UnityTest]
        public IEnumerator ReachingTheOpenExit_CompletesTheLevel_AndUnlocksTheNext()
        {
            ObjectiveManager objectives = ObjectiveManager.Instance;
            for (int i = 0; i < objectives.Objectives.Count; i++)
            {
                if (objectives.Objectives[i].Kind != ObjectiveKind.ReachExit) objectives.CompleteObjective(i);
            }
            yield return null;

            GameManager.Instance.Save.ResetProgress();

            yield return TestLevel.PlacePlayer(new Vector3(0f, 1.5f, 26f));
            yield return TestLevel.WaitUntil(() => GameManager.Instance.State == GameState.LevelComplete, 5f,
                                             "level to complete at the exit");

            Assert.IsTrue(objectives.AllComplete, "the exit objective should tick too");
            Assert.IsTrue(GameManager.Instance.Save.IsLevelUnlocked(LevelId.Level2_Brain),
                          "finishing level 1 should unlock level 2");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// A station whose puzzle the current tier actually allows.
        ///
        /// At Easy the complexity cap is 1, so half of Level 1's stations are
        /// legitimately locked - picking one at random makes tests fail for a
        /// reason that has nothing to do with what they are checking.
        /// </summary>
        private static PuzzleStation FirstOpenStation()
        {
            foreach (PuzzleStation station in Object.FindObjectsByType<PuzzleStation>(FindObjectsInactive.Exclude))
            {
                if (!station.IsLockedByDifficulty && !PuzzleManager.Instance.IsSolved(station.PuzzleId)) return station;
            }

            Assert.Fail("Level 1 should have at least one station within the current complexity cap");
            return null;
        }

        /// <summary>A station whose puzzle the current tier refuses.</summary>
        private static PuzzleStation FirstLockedStation()
        {
            foreach (PuzzleStation station in Object.FindObjectsByType<PuzzleStation>(FindObjectsInactive.Exclude))
            {
                if (station.IsLockedByDifficulty) return station;
            }

            return null;
        }

        private static PuzzleData FindPuzzle(int complexity, PuzzleType? type = null)
        {
            foreach (PuzzleData puzzle in PuzzleManager.Instance.Bank.Puzzles)
            {
                if (puzzle == null) continue;
                if (complexity > 0 && puzzle.Complexity != complexity) continue;
                if (complexity < 0 && puzzle.Complexity > PuzzleManager.Instance.MaxComplexity) continue;
                if (type.HasValue && puzzle.Type != type.Value) continue;
                if (PuzzleManager.Instance.IsSolved(puzzle.PuzzleId)) continue;

                return puzzle;
            }

            return null;
        }

        private static void SubmitCorrect(PuzzleData puzzle)
        {
            switch (puzzle.Type)
            {
                case PuzzleType.MultipleChoice:
                    PuzzleManager.Instance.SubmitOption(puzzle.CorrectOptionIndex);
                    break;
                case PuzzleType.BloodFlowSequence:
                    PuzzleManager.Instance.SubmitSequence(puzzle.SequenceSteps);
                    break;
                default:
                    PuzzleManager.Instance.SubmitStructure(puzzle.TargetStructureId);
                    break;
            }
        }

        private static IEnumerator AnswerPuzzle(PuzzleData puzzle, bool correctly)
        {
            Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle), $"could not begin '{puzzle.PuzzleId}'");
            yield return null;

            if (correctly) SubmitCorrect(puzzle);
            yield return null;

            yield return WaitForPanelToClose();
        }

        private static IEnumerator WaitForPanelToClose()
        {
            yield return TestLevel.WaitUntil(() => !PuzzleManager.Instance.IsPuzzleActive, 20f,
                                             "the puzzle panel to close");
        }
    }
}

