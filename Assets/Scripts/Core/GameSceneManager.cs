using System;
using System.Collections;
using Cardio.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cardio.Core
{
    /// <summary>
    /// Asynchronous scene loading with a fade so transitions are not a hard cut.
    ///
    /// Named GameSceneManager rather than SceneManager (as sketched in the PSM1
    /// folder plan) because <c>UnityEngine.SceneManagement.SceneManager</c>
    /// already owns that type name; shadowing it forces every other file to
    /// fully qualify the Unity one, which is a common source of build errors.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameSceneManager : MonoBehaviour
    {
        [Header("Transition")]
        [SerializeField, Range(0f, 1.5f)] private float fadeOutDuration = 0.25f;
        [SerializeField, Range(0f, 1.5f)] private float fadeInDuration = 0.35f;
        [Tooltip("Minimum time the loading screen stays up, so fast loads do not flash.")]
        [SerializeField, Range(0f, 2f)] private float minimumLoadTime = 0.3f;

        /// <summary>True while a scene load is in flight; blocks double-clicks on menu buttons.</summary>
        public bool IsLoading { get; private set; }

        /// <summary>Raised with the scene name once the new scene is active.</summary>
        public event Action<string> SceneLoaded;

        /// <summary>
        /// Loads <paramref name="sceneName"/> asynchronously.
        /// </summary>
        /// <param name="stateWhileLoading">
        /// State applied for the duration of the load. Level scenes then set
        /// GameState.Playing themselves via LevelController.
        /// </param>
        public void LoadScene(string sceneName, GameState stateWhileLoading = GameState.Loading)
        {
            if (IsLoading)
            {
                Debug.LogWarning($"[GameSceneManager] Ignoring request to load '{sceneName}' - a load is already running.");
                return;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError(
                    $"[GameSceneManager] Scene '{sceneName}' is not in Build Settings. " +
                    "Run  PSM2 > Setup > Build / Rebuild Project  to register the scenes.");
                return;
            }

            StartCoroutine(LoadRoutine(sceneName, stateWhileLoading));
        }

        private IEnumerator LoadRoutine(string sceneName, GameState stateWhileLoading)
        {
            IsLoading = true;

            // Time.timeScale can be 0 if we came from the pause menu; restore it
            // before loading, otherwise WaitForSeconds inside the new scene hangs.
            Time.timeScale = 1f;
            GameManager.Instance?.SetState(stateWhileLoading);

            yield return ScreenFader.FadeOut(fadeOutDuration);

            float startTime = Time.unscaledTime;
            AsyncOperation op = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            op.allowSceneActivation = false;

            // Unity reports 0.9 when a scene is ready but not yet activated.
            while (op.progress < 0.9f) yield return null;

            float elapsed = Time.unscaledTime - startTime;
            if (elapsed < minimumLoadTime) yield return new WaitForSecondsRealtime(minimumLoadTime - elapsed);

            op.allowSceneActivation = true;
            while (!op.isDone) yield return null;

            yield return ScreenFader.FadeIn(fadeInDuration);

            IsLoading = false;
            SceneLoaded?.Invoke(sceneName);
        }
    }
}
