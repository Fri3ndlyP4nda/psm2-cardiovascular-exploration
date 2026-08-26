using System.Collections;
using Cardio.AI;
using Cardio.Core;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// Shared setup for the PlayMode suites: loads a level, waits for every
    /// system to be live, and installs scripted input.
    /// </summary>
    public static class TestLevel
    {
        /// <summary>Loads a level from a clean session and waits until it is playable.</summary>
        public static IEnumerator Load(string sceneName = null)
        {
            sceneName ??= GameConstants.SceneLevel1;

            // A previous test may have left one of the product's own faded loads
            // in flight. Loading on top of it makes the two race, and the loser
            // silently wins the active scene - which produced several confusing
            // failures before this wait was added.
            yield return WaitForNoLoadInFlight();

            // Fresh session next: the persistent managers survive scene loads by
            // design, so tier and metrics would otherwise leak between tests.
            if (GameManager.Instance != null) GameManager.Instance.StartNewSession("test", "Automated Test");

            yield return SceneManager.LoadSceneAsync(sceneName);
            yield return WaitUntilReady();

            // Wrong answers spawn leukemic blasts that chase and damage the
            // player. Most suites submit wrong answers to test something else
            // entirely, and a hostile wandering in mid-assertion makes them
            // flaky. Combat tests opt back in explicitly.
            if (PuzzleManager.Instance != null) PuzzleManager.Instance.HostileSpawningEnabled = false;
        }

        /// <summary>Turns wrong-answer spawning back on. Combat suites call this.</summary>
        public static void EnableHostileSpawning()
        {
            Assert.IsNotNull(PuzzleManager.Instance, "no PuzzleManager in the scene");
            PuzzleManager.Instance.HostileSpawningEnabled = true;
        }

        /// <summary>Waits out any load started through GameSceneManager.</summary>
        public static IEnumerator WaitForNoLoadInFlight()
        {
            GameSceneManager scenes = GameManager.Instance != null ? GameManager.Instance.Scenes : null;
            if (scenes == null) yield break;

            float deadline = Time.realtimeSinceStartup + 30f;
            while (scenes.IsLoading && Time.realtimeSinceStartup < deadline) yield return null;
        }

        public static IEnumerator WaitUntilReady()
        {
            float deadline = Time.realtimeSinceStartup + 30f;

            while (Time.realtimeSinceStartup < deadline)
            {
                // CurrentLevel is the definitive signal that *this* scene's
                // LevelController has run. Checking only for GameState.Playing
                // can pass on a stale state left over from the previous scene.
                if (GameManager.Instance != null
                    && GameManager.Instance.State == GameState.Playing
                    && GameManager.Instance.Session.CurrentLevel != LevelId.None
                    && PuzzleManager.Instance != null
                    && DDAManager.Instance != null
                    && PerformanceTracker.Instance != null
                    && ObjectiveManager.Instance != null
                    && Object.FindAnyObjectByType<PlayerController>() != null)
                {
                    yield break;
                }

                yield return null;
            }

            Assert.Fail("level did not reach a playable state within 30 seconds");
        }

        /// <summary>
        /// Finds a GameObject by name **including inactive ones**.
        ///
        /// UnityEngine.GameObject.Find skips inactive objects, and every panel
        /// worth asserting on (pause, results, prompts) starts inactive by
        /// design - so the built-in call reports them as missing.
        /// </summary>
        public static GameObject Find(string name)
        {
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                Transform found = SearchRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }

            return null;
        }

        private static Transform SearchRecursive(Transform parent, string name)
        {
            if (parent.name == name) return parent;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform found = SearchRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }

            return null;
        }

        public static PlayerController Player => Object.FindAnyObjectByType<PlayerController>();
        public static PlayerHealth Health => Object.FindAnyObjectByType<PlayerHealth>();
        public static LevelController Level => Object.FindAnyObjectByType<LevelController>();
        public static AStarPathfindingManager Pathfinding => AStarPathfindingManager.Instance;

        /// <summary>Waits a number of rendered frames.</summary>
        public static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>
        /// Waits until a condition holds, failing with a useful message rather
        /// than hanging the whole run.
        /// </summary>
        public static IEnumerator WaitUntil(System.Func<bool> condition, float timeout, string description)
        {
            float deadline = Time.realtimeSinceStartup + timeout;

            while (Time.realtimeSinceStartup < deadline)
            {
                if (condition()) yield break;
                yield return null;
            }

            Assert.Fail($"timed out after {timeout}s waiting for: {description}");
        }

        /// <summary>Moves the player somewhere safe and lets physics settle.</summary>
        public static IEnumerator PlacePlayer(Vector3 position)
        {
            PlayerController player = Player;
            Assert.IsNotNull(player, "no player in the scene");

            player.Teleport(position, Quaternion.identity);
            yield return Frames(3);
        }
    }
}
