using System.Collections;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using Cardio.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// TC-01 (bootstrapping), TC-05 (level flow), TC-07 (scene loading) and the
    /// objectively assertable half of TC-08 (UI state transitions).
    ///
    /// "UI state" here means which panels are active and whether the game state
    /// drives them correctly. It does not mean layout, colour or readability -
    /// those stay manual.
    /// </summary>
    public class StateAndSceneTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();
        }

        [TearDown]
        public void TearDown()
        {
            PlayerInputReader.ClearSource();
            Time.timeScale = 1f;
        }

        // ------------------------------------------------------------------
        // TC-01 bootstrapping
        // ------------------------------------------------------------------

        [Test]
        public void PersistentSystems_ExistExactlyOnce_AndSurviveSceneLoads()
        {
            Assert.IsNotNull(GameManager.Instance, "GameBootstrap should have created the systems");
            Assert.IsNotNull(GameManager.Instance.Scenes);
            Assert.IsNotNull(GameManager.Instance.Save);
            Assert.IsNotNull(PerformanceTracker.Instance);
            Assert.IsNotNull(DDAManager.Instance);
            Assert.IsNotNull(HintManager.Instance);

            Assert.AreEqual(1, Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include).Length,
                            "there must be exactly one GameManager");
            Assert.AreEqual(1, Object.FindObjectsByType<PerformanceTracker>(FindObjectsInactive.Include).Length,
                            "there must be exactly one PerformanceTracker");

            // The systems object is parented to nothing and marked persistent.
            Assert.IsNull(GameManager.Instance.transform.parent);
        }

        [UnityTest]
        public IEnumerator LoadingASecondScene_DoesNotDuplicateTheSystems()
        {
            GameManager first = GameManager.Instance;

            yield return SceneManager.LoadSceneAsync(GameConstants.SceneLevel2);
            yield return TestLevel.WaitUntilReady();

            Assert.AreSame(first, GameManager.Instance, "the same GameManager should survive the load");
            Assert.AreEqual(1, Object.FindObjectsByType<GameManager>(FindObjectsInactive.Include).Length);
            Assert.AreEqual(1, Object.FindObjectsByType<DDAManager>(FindObjectsInactive.Include).Length);
        }

        // ------------------------------------------------------------------
        // TC-05 level flow
        // ------------------------------------------------------------------

        [Test]
        public void LevelStart_PlacesThePlayerAtTheSpawnPoint_WithFullHealth()
        {
            GameObject spawn = TestLevel.Find("SpawnPoint");
            Assert.IsNotNull(spawn, "Level 1 should contain a SpawnPoint");

            float distance = Vector3.Distance(TestLevel.Player.transform.position, spawn.transform.position);
            Assert.Less(distance, 2.5f, $"player should start at the spawn point, was {distance:0.0} units away");

            Assert.AreEqual(TestLevel.Health.MaxBloodCount, TestLevel.Health.CurrentBloodCount);
            Assert.AreEqual(LevelId.Level1_LeftVentricle, GameManager.Instance.Session.CurrentLevel);
        }

        [Test]
        public void LevelStart_PublishesObjectivesToTheBoard()
        {
            ObjectiveManager objectives = ObjectiveManager.Instance;
            Assert.Greater(objectives.Objectives.Count, 0);
            Assert.AreEqual(0, objectives.CompletedCount, "nothing should start ticked");

            // Exactly one exit objective, and it must be last so the board reads
            // as a checklist ending in "leave".
            int exitCount = 0;
            foreach (LevelObjective objective in objectives.Objectives)
            {
                if (objective.Kind == ObjectiveKind.ReachExit) exitCount++;
            }
            Assert.AreEqual(1, exitCount, "a level should have exactly one exit objective");
            Assert.AreEqual(ObjectiveKind.ReachExit, objectives.Objectives[objectives.Objectives.Count - 1].Kind);

            GameObject board = TestLevel.Find("ObjectiveBoard");
            Assert.IsNotNull(board, "the HUD should contain the objective clipboard");
        }

        [UnityTest]
        public IEnumerator GameOver_IsReachable_AndRestartRestoresPlay()
        {
            TestLevel.Health.TakeDamage(TestLevel.Health.MaxBloodCount);
            yield return TestLevel.Frames(2);

            Assert.AreEqual(GameState.GameOver, GameManager.Instance.State);

            // Restarting reloads the level and returns to Playing with full health.
            GameManager.Instance.RestartCurrentLevel();
            yield return TestLevel.WaitUntil(() => GameManager.Instance.State == GameState.Playing, 30f,
                                             "the level to reload after a restart");

            Assert.AreEqual(TestLevel.Health.MaxBloodCount, TestLevel.Health.CurrentBloodCount,
                            "a restart should restore Blood Count");
        }

        // ------------------------------------------------------------------
        // TC-07 scene loading
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator SceneManager_LoadsALevel_AndClearsItsLoadingFlag()
        {
            GameSceneManager scenes = GameManager.Instance.Scenes;
            Assert.IsFalse(scenes.IsLoading);

            scenes.LoadScene(GameConstants.SceneLevel2);
            yield return TestLevel.WaitUntil(() => !scenes.IsLoading, 30f, "the load to finish");

            Assert.AreEqual(GameConstants.SceneLevel2, SceneManager.GetActiveScene().name);
            Assert.AreEqual(1f, Time.timeScale, 0.001f, "time scale should be restored after a load");
        }

        [UnityTest]
        public IEnumerator SecondLoadRequest_IsIgnoredWhileOneIsRunning()
        {
            GameSceneManager scenes = GameManager.Instance.Scenes;

            scenes.LoadScene(GameConstants.SceneLevel2);
            yield return null;
            Assert.IsTrue(scenes.IsLoading, "the first load should be in flight");

            // The guard logs a warning; assert on behaviour, not the message.
            LogAssert.ignoreFailingMessages = true;
            scenes.LoadScene(GameConstants.SceneLevel3);
            LogAssert.ignoreFailingMessages = false;

            yield return TestLevel.WaitUntil(() => !scenes.IsLoading, 30f, "the load to finish");

            Assert.AreEqual(GameConstants.SceneLevel2, SceneManager.GetActiveScene().name,
                            "the second request must not hijack the first");
        }

        [UnityTest]
        public IEnumerator PausingThenLeavingToTheMenu_RestoresTimeScale()
        {
            GameManager.Instance.PauseGame();
            Assert.AreEqual(0f, Time.timeScale, 0.001f);

            GameManager.Instance.GoToMainMenu();
            yield return TestLevel.WaitUntil(
                () => SceneManager.GetActiveScene().name == GameConstants.SceneMainMenu, 30f,
                "the main menu to load");

            Assert.AreEqual(1f, Time.timeScale, 0.001f,
                            "leaving a paused game must not leave the game frozen");
            Assert.AreEqual(GameState.MainMenu, GameManager.Instance.State);
        }

        [Test]
        public void EveryLevelScene_IsRegisteredInBuildSettings()
        {
            foreach (string scene in GameConstants.LevelScenes)
            {
                Assert.IsTrue(Application.CanStreamedLevelBeLoaded(scene), $"{scene} is not in Build Settings");
            }

            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(GameConstants.SceneMainMenu));
            Assert.IsTrue(Application.CanStreamedLevelBeLoaded(GameConstants.SceneLogin));
        }

        // ------------------------------------------------------------------
        // TC-08 UI state transitions (objective half)
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PausePanel_FollowsTheGameState()
        {
            GameObject pausePanel = TestLevel.Find("PausePanel");
            Assert.IsNotNull(pausePanel, "the level should contain a pause panel");
            Assert.IsFalse(pausePanel.activeSelf, "pause should start hidden");

            GameManager.Instance.PauseGame();
            yield return TestLevel.Frames(2);
            Assert.IsTrue(pausePanel.activeSelf, "pausing should show the panel");

            GameManager.Instance.ResumeGame();
            yield return TestLevel.Frames(2);
            Assert.IsFalse(pausePanel.activeSelf, "resuming should hide it");
        }

        [UnityTest]
        public IEnumerator ResultPanels_FollowTheGameState()
        {
            GameObject complete = TestLevel.Find("LevelCompletePanel");
            GameObject failed = TestLevel.Find("LevelFailedPanel");
            Assert.IsNotNull(complete, "the level should contain a completion panel");
            Assert.IsNotNull(failed, "the level should contain a failure panel");

            Assert.IsFalse(complete.activeSelf);
            Assert.IsFalse(failed.activeSelf);

            TestLevel.Health.TakeDamage(TestLevel.Health.MaxBloodCount);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(failed.activeSelf, "losing all Blood Count should show the failure panel");
            Assert.IsFalse(complete.activeSelf, "the completion panel must stay hidden on failure");
        }

        /// <summary>
        /// The state machine behind cursor handling. The cursor itself is NOT
        /// asserted: batch mode has no window, so Cursor.lockState never leaves
        /// None regardless of what the game requests. Whether the cursor is
        /// really captured and released is MANUAL REQUIRED.
        /// </summary>
        [UnityTest]
        public IEnumerator PuzzleMode_IsEnteredAndExited_WithoutPausingTime()
        {
            Assert.AreEqual(GameState.Playing, GameManager.Instance.State);

            GameManager.Instance.EnterPuzzleMode();
            yield return null;

            Assert.AreEqual(GameState.Puzzle, GameManager.Instance.State);
            Assert.AreEqual(1f, Time.timeScale, 0.001f,
                            "puzzle mode must not freeze time - obstacles keep moving");

            GameManager.Instance.ExitPuzzleMode();
            yield return null;

            Assert.AreEqual(GameState.Playing, GameManager.Instance.State);
        }

        [UnityTest]
        public IEnumerator HudReflectsSessionChanges()
        {
            // The HUD is a pure view driven by SessionChanged; assert the data it
            // renders rather than the pixels.
            GameObject hud = TestLevel.Find("UI_HUD");
            Assert.IsNotNull(hud, "the level should contain the HUD canvas");

            Assert.AreEqual(DifficultyTier.Easy, GameManager.Instance.Session.CurrentDifficulty);

            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "test");
            yield return null;

            Assert.AreEqual(DifficultyTier.Hard, GameManager.Instance.Session.CurrentDifficulty,
                            "a tier change should reach the session the HUD reads");
        }
    }
}
