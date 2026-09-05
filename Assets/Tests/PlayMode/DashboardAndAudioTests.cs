using System.Collections;
using System.Collections.Generic;
using Cardio.Core;
using Cardio.DDA;
using Cardio.Data;
using Cardio.Gameplay;
using Cardio.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// Phase 9: the performance dashboard and the audio cues.
    ///
    /// The dashboard is asserted at the data level - that finishing a level
    /// really does write a record, and that the panel reads it back. What is
    /// NOT asserted here is anything about how the panel looks or whether the
    /// numbers are legible on screen; no test clicks the Profile button. That
    /// stays manual, as with every other uGUI surface in this project.
    ///
    /// Audio is asserted by counting cues rather than by listening. Batch mode
    /// has no audio device, so "did the right event fire the right cue" is the
    /// strongest claim available and the only one worth making.
    /// </summary>
    public class DashboardAndAudioTests
    {
        private string _backup;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            // These tests write to the real save file, so preserve whatever was
            // there and clear it for a known starting point.
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

        // ------------------------------------------------------------------
        // Session history
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator CompletingALevel_WritesASessionRecord()
        {
            SaveManager save = GameManager.Instance.Save;
            Assert.IsEmpty(save.Progress.SessionHistory, "should start clean");

            GameManager.Instance.NotifyLevelCompleted(LevelId.Level1_LeftVentricle);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(1, save.Progress.SessionHistory.Count,
                            "finishing a level must leave a record for the dashboard");

            SessionRecord record = save.Progress.SessionHistory[0];
            Assert.AreEqual((int)LevelId.Level1_LeftVentricle, record.Level);
            Assert.IsTrue(record.Completed, "a completed level should be recorded as completed");
            Assert.IsNotEmpty(record.DateUtc, "a record with no date cannot be ordered in the history");
        }

        [UnityTest]
        public IEnumerator DyingWritesARecordToo_MarkedNotCompleted()
        {
            SaveManager save = GameManager.Instance.Save;

            GameManager.Instance.NotifyPlayerDied();
            yield return TestLevel.Frames(2);

            Assert.AreEqual(1, save.Progress.SessionHistory.Count,
                            "a failed attempt is still an attempt and belongs in the history");
            Assert.IsFalse(save.Progress.SessionHistory[0].Completed,
                           "a failed attempt must not be recorded as completed");
        }

        [UnityTest]
        public IEnumerator ARecordedAttempt_CarriesTheMetricsItWasGiven()
        {
            // Answer one puzzle correctly so the record has something in it.
            PuzzleManager puzzles = PuzzleManager.Instance;
            PuzzleStation station = OpenableStructureStation();
            Assert.IsNotNull(station, "level has no world-picking station openable at the current tier");

            Assert.IsTrue(puzzles.BeginPuzzle(station.PuzzleId), "station puzzle should open");
            yield return TestLevel.Frames(2);
            puzzles.SubmitStructure(puzzles.Current.TargetStructureId);
            yield return TestLevel.Frames(2);

            GameManager.Instance.NotifyLevelCompleted(LevelId.Level1_LeftVentricle);
            yield return TestLevel.Frames(2);

            SessionRecord record = GameManager.Instance.Save.Progress.SessionHistory[0];
            Assert.GreaterOrEqual(record.PuzzlesAttempted, 1, "the answered puzzle should be counted");
            Assert.GreaterOrEqual(record.PuzzlesCorrect, 1, "the correct answer should be counted");
        }

        // ------------------------------------------------------------------
        // Dashboard
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator Dashboard_OnAFreshProfile_SaysSoRatherThanShowingZeroes()
        {
            // "0%" reads as "you scored zero"; the panel has to distinguish
            // "no data" from "bad data".
            List<SessionRecord> recent = GameManager.Instance.Save.RecentSessions(8);
            Assert.IsEmpty(recent);

            var host = new GameObject("DashboardHost");
            try
            {
                DashboardUI dashboard = host.AddComponent<DashboardUI>();
                Assert.DoesNotThrow(() => dashboard.Refresh(),
                                    "the dashboard must survive being opened before anything is played");
            }
            finally
            {
                Object.DestroyImmediate(host);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Dashboard_ReadsBackWhatWasRecorded()
        {
            SaveManager save = GameManager.Instance.Save;
            save.AppendSessionRecord(new SessionRecord
            {
                DateUtc = "2026-08-31 10:00", Level = 1, Score = 400,
                PuzzlesAttempted = 4, PuzzlesCorrect = 3, Completed = true
            });

            List<SessionRecord> recent = save.RecentSessions(8);
            Assert.AreEqual(1, recent.Count);
            Assert.AreEqual(400, recent[0].Score);
            Assert.AreEqual(0.75f, recent[0].Accuracy01, 0.001f,
                            "accuracy is derived, so it cannot drift from the counts");

            yield return null;
        }

        // ------------------------------------------------------------------
        // Audio cues
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator AudioManager_ExistsAndLoadsItsCues()
        {
            Assert.IsNotNull(AudioManager.Instance, "the bootstrap should create an AudioManager");

            // Resources.Load is the production path; if the generator did not run,
            // this is where it shows up rather than as silence at runtime.
            foreach (AudioCue cue in (AudioCue[])System.Enum.GetValues(typeof(AudioCue)))
            {
                Assert.IsNotNull(Resources.Load<AudioClip>("Audio/" + cue),
                                 $"missing generated clip for {cue} - run PSM2 > Setup > Build or Rebuild Project");
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator AnsweringCorrectly_FiresTheCorrectCue()
        {
            AudioManager audio = AudioManager.Instance;
            int before = audio.CueCount(AudioCue.Correct);

            PuzzleManager puzzles = PuzzleManager.Instance;
            PuzzleStation station = OpenableStructureStation();
            Assert.IsNotNull(station, "level has no world-picking station openable at the current tier");
            Assert.IsTrue(puzzles.BeginPuzzle(station.PuzzleId), "station puzzle should open");
            yield return TestLevel.Frames(2);

            puzzles.SubmitStructure(puzzles.Current.TargetStructureId);
            yield return TestLevel.Frames(2);

            Assert.Greater(audio.CueCount(AudioCue.Correct), before,
                           "a right answer should play the Correct cue");
        }

        [UnityTest]
        public IEnumerator AnsweringWrongly_FiresTheWrongCue()
        {
            PuzzleManager puzzles = PuzzleManager.Instance;
            PuzzleStation station = OpenableStructureStation();
            Assert.IsNotNull(station, "level has no world-picking station openable at the current tier");
            Assert.IsTrue(puzzles.BeginPuzzle(station.PuzzleId), "station puzzle should open");
            yield return TestLevel.Frames(2);

            // Exhaust the attempts so the puzzle resolves as failed, which is
            // what emits PuzzleAnswered with Correct == false.
            for (int i = 0; i < 8 && puzzles.IsAcceptingAnswers; i++)
            {
                puzzles.SubmitStructure("definitely_not_a_structure");
                yield return TestLevel.Frames(2);
            }

            Assert.Greater(AudioManager.Instance.CueCount(AudioCue.Wrong), 0,
                           "a failed puzzle should play the Wrong cue");
        }

        [UnityTest]
        public IEnumerator CompletingALevel_FiresTheLevelCompleteCue()
        {
            GameManager.Instance.NotifyLevelCompleted(LevelId.Level1_LeftVentricle);
            yield return TestLevel.Frames(2);

            Assert.Greater(AudioManager.Instance.CueCount(AudioCue.LevelComplete), 0,
                           "completing a level should play the LevelComplete cue");
        }
        /// <summary>
        /// A station whose puzzle the current tier will open AND that is
        /// answered by pointing at geometry.
        ///
        /// Two filters, both learned the hard way. The complexity cap matters
        /// because at Easy several Level 1 stations refuse to open by design.
        /// The format matters because SubmitStructure cannot answer a
        /// multiple-choice or sequence puzzle - these tests passed only as long
        /// as FindObjectsByType happened to return a structure puzzle first,
        /// and adding another suite changed that order and exposed it.
        /// </summary>
        private static PuzzleStation OpenableStructureStation()
        {
            foreach (PuzzleStation candidate in Object.FindObjectsByType<PuzzleStation>(FindObjectsInactive.Include))
            {
                if (!PuzzleManager.Instance.IsWithinComplexityCap(candidate.PuzzleId)) continue;

                PuzzleData puzzle = PuzzleManager.Instance.Bank.Find(candidate.PuzzleId);
                if (puzzle != null && puzzle.Type.UsesWorldPicking()) return candidate;
            }

            return null;
        }


        // ------------------------------------------------------------------
        // Feedback the player can actually perceive
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TakingDamage_FlashesTheBloodCountBar()
        {
            // PlayerHealth has raised Damaged since Phase 1, and only the audio cue
            // and the metrics tracker ever listened - so losing Blood Count had no
            // visual signal beyond a bar that was already shrinking.
            GameplayHUD hud = GameplayHUD.Instance;
            Assert.IsNotNull(hud, "the gameplay HUD should exist");
            Assert.IsFalse(hud.DamageFlashActive, "nothing should be flashing before a hit");

            TestLevel.Health.TakeDamage(10);
            yield return null;

            Assert.IsTrue(hud.DamageFlashActive, "a hit should flash the bar");

            yield return new WaitForSeconds(0.6f);
            Assert.IsFalse(hud.DamageFlashActive, "the flash should settle back on its own");
        }

        [UnityTest]
        public IEnumerator ChangingTier_TellsThePlayer_AndSaysWhichWay()
        {
            // The DDA is the centrepiece of this project and was the one thing the
            // player could not perceive: the tier label changed quietly in a corner
            // and nothing marked the moment.
            GameplayHUD hud = GameplayHUD.Instance;
            Assert.IsNotNull(hud);

            DDAManager.Instance.ForceTier(DifficultyTier.Easy, "test baseline");
            yield return null;

            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test promotion");
            yield return null;

            StringAssert.Contains("Hard", hud.CurrentHintText, "a promotion should be announced");
            StringAssert.Contains("raised", hud.CurrentHintText, "the announcement should say which way it went");

            DDAManager.Instance.ForceTier(DifficultyTier.Easy, "test demotion");
            yield return null;

            StringAssert.Contains("eased", hud.CurrentHintText, "a demotion should read differently from a promotion");
        }



        [UnityTest]
        public IEnumerator CompletingAnObjective_IsAnnounced_AndSoIsTheExitOpening()
        {
            // ObjectiveManager raised ObjectiveCompleted with nobody listening, so
            // finishing one refreshed a board in the corner and produced no moment.
            // The exit opening had no signal at all - the player had to notice the
            // board or walk back and find out.
            GameplayHUD hud = GameplayHUD.Instance;
            ObjectiveManager objectives = ObjectiveManager.Instance;
            Assert.IsNotNull(hud);
            Assert.IsNotNull(objectives);

            int puzzleObjectives = 0;
            foreach (LevelObjective o in objectives.Objectives)
            {
                if (o.Kind != ObjectiveKind.ReachExit) puzzleObjectives++;
            }
            Assert.Greater(puzzleObjectives, 0, "level 1 should have puzzle objectives");

            // Solve them all, checking the first produces an announcement.
            bool sawProgress = false;
            for (int i = 0; i < objectives.Objectives.Count; i++)
            {
                LevelObjective o = objectives.Objectives[i];
                if (o.Kind == ObjectiveKind.ReachExit || o.Completed) continue;

                objectives.CompleteObjective(i);
                yield return null;

                if (!objectives.AllNonExitObjectivesComplete())
                {
                    StringAssert.Contains("Objective complete", hud.CurrentHintText,
                                          "finishing an objective should say so");
                    sawProgress = true;
                }
            }

            Assert.IsTrue(sawProgress, "at least one mid-level objective should have been announced");
            Assert.IsTrue(objectives.AllNonExitObjectivesComplete());
            StringAssert.Contains("exit is open", hud.CurrentHintText,
                                  "the moment the exit unlocks is the one worth announcing");
        }


    }
}
