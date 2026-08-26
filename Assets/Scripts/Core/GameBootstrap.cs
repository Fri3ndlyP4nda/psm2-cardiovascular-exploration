using UnityEngine;

namespace Cardio.Core
{
    /// <summary>
    /// Creates the persistent service object before the first scene loads.
    ///
    /// Why this exists: without it, GameManager would have to be dragged into
    /// every scene, and pressing Play directly inside Level1 (which is what you
    /// do 95% of the time while developing) would crash on a null Instance.
    /// With this bootstrapper the game can be started from any scene.
    ///
    /// Later phases add their managers to the same GameObject here
    /// (DDAManager, PerformanceTracker, FirebaseManager, ...).
    /// </summary>
    public static class GameBootstrap
    {
        private const string ObjectName = "[Cardio Systems]";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void CreateSystems()
        {
            if (GameManager.Instance != null) return;

            var go = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(go);

            // Order matters. AddComponent runs Awake() immediately, and
            // GameManager.Awake() resolves the scene and save services with
            // GetComponent, so those two must exist before it.
            go.AddComponent<GameSceneManager>();
            go.AddComponent<SaveManager>();
            go.AddComponent<GameManager>();

            // Added after GameManager because these read GameManager.Instance
            // from Start onwards.
            //
            // Order within this group matters: the tracker must exist before the
            // DDAManager subscribes to it, and HintManager before the DDAManager
            // pushes the opening tier into it.
            go.AddComponent<Cardio.DDA.PerformanceTracker>();
            go.AddComponent<Cardio.Gameplay.HintManager>();
            go.AddComponent<Cardio.DDA.DDAManager>();
        }
    }
}
