using System;
using UnityEngine;

namespace Cardio.Core
{
    /// <summary>
    /// The persistent hub of the application.
    ///
    /// Responsibilities (deliberately narrow):
    ///   * owns the <see cref="SessionData"/> that must survive scene loads
    ///   * owns the high level <see cref="GameState"/> and broadcasts changes
    ///   * owns pause / resume (Time.timeScale) and cursor locking
    ///   * exposes the other persistent services (scene loading, saving)
    ///
    /// It does NOT know about puzzles, DDA, pathfinding or the backend. Those
    /// systems will subscribe to its events instead of being called from here,
    /// which keeps this class from growing into a god object.
    ///
    /// Created automatically by <see cref="GameBootstrap"/>, so it does not need
    /// to be placed in any scene and Play Mode works from *any* scene.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("Session defaults (tunable in the Inspector at runtime)")]
        [Tooltip("Difficulty a fresh session starts at. From Phase 4 the DDAManager may change it during play.")]
        [SerializeField] private DifficultyTier startingDifficulty = DifficultyTier.Easy;

        [Header("Debug")]
        [Tooltip("Logs every state transition to the Console. Useful when demonstrating the game loop.")]
        [SerializeField] private bool logStateChanges = true;

        // ---- Services (added by GameBootstrap on the same GameObject) ----
        public GameSceneManager Scenes { get; private set; }
        public SaveManager Save { get; private set; }

        /// <summary>Data for the session currently being played. Never null after Awake.</summary>
        public SessionData Session { get; private set; }

        public GameState State { get; private set; } = GameState.Boot;

        /// <summary>Raised after <see cref="State"/> changes. Argument is the new state.</summary>
        public event Action<GameState> StateChanged;

        /// <summary>Raised when a field of <see cref="Session"/> changes and the UI should refresh.</summary>
        public event Action<SessionData> SessionChanged;

        public bool IsPaused => State == GameState.Paused;

        /// <summary>
        /// True only while the player is actually in control of the character.
        ///
        /// Every source that can hurt the player must check this. During
        /// <see cref="GameState.Puzzle"/> the character is frozen behind a panel
        /// that also covers the view, so damage taken there cannot be avoided,
        /// reacted to, or even seen - it is a drain with no counterplay rather than
        /// difficulty. <see cref="Cardio.AI.PathfindingAgent"/> has always frozen
        /// agent movement on this condition; the damage sources now agree with it.
        /// </summary>
        public bool IsGameplayActive => State == GameState.Playing;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            Scenes = GetComponent<GameSceneManager>();
            Save = GetComponent<SaveManager>();

            Session = new SessionData { StartingDifficulty = startingDifficulty, CurrentDifficulty = startingDifficulty };
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------
        // State machine
        // ------------------------------------------------------------------

        public void SetState(GameState next)
        {
            if (State == next) return;

            if (logStateChanges) Debug.Log($"[GameManager] {State} -> {next}");
            State = next;

            // Pause only freezes time; every other state runs normally.
            Time.timeScale = next == GameState.Paused ? 0f : 1f;
            ApplyCursorMode(next);

            StateChanged?.Invoke(next);
        }

        private static void ApplyCursorMode(GameState state)
        {
            bool gameplay = state == GameState.Playing;
            Cursor.lockState = gameplay ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !gameplay;
        }

        // ------------------------------------------------------------------
        // Session lifecycle
        // ------------------------------------------------------------------

        /// <summary>
        /// Discards any previous session and begins a new one. Called when the
        /// player picks "Start Game", and when a profile is signed in (Phase 7).
        /// </summary>
        public void StartNewSession(string userId = "guest", string displayName = "Guest")
        {
            Session = new SessionData
            {
                UserId = string.IsNullOrWhiteSpace(userId) ? "guest" : userId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? "Guest" : displayName,
                StartingDifficulty = startingDifficulty,
                CurrentDifficulty = startingDifficulty
            };

            // Counted in memory only. Writing the file here would mean a disk
            // write every time a test calls StartNewSession; it reaches disk on
            // the next completed level or on quit.
            if (Save != null) Save.Progress.TotalSessionsPlayed++;

            // Measurements and adaptation state belong to a session, so they are
            // discarded with it.
            Cardio.DDA.PerformanceTracker.Instance?.ResetSession();
            Cardio.Gameplay.HintManager.Instance?.ResetSession();
            Cardio.DDA.DDAManager.Instance?.ResetSession();

            RaiseSessionChanged();
        }

        /// <summary>Call after mutating <see cref="Session"/> so the HUD refreshes.</summary>
        public void RaiseSessionChanged() => SessionChanged?.Invoke(Session);

        /// <summary>Called by <see cref="Cardio.Gameplay.LevelController"/> when a level scene becomes active.</summary>
        public void NotifyLevelStarted(LevelId level)
        {
            Session.CurrentLevel = level;
            SetState(GameState.Playing);
            RaiseSessionChanged();
        }

        /// <summary>Called when the player reaches the level exit.</summary>
        public void NotifyLevelCompleted(LevelId level)
        {
            if (Save != null) Save.MarkLevelCompleted(level);
            SetState(GameState.LevelComplete);
        }

        /// <summary>Called when Blood Count reaches zero.</summary>
        public void NotifyPlayerDied()
        {
            Session.LevelFailures++;
            RaiseSessionChanged();
            SetState(GameState.GameOver);
        }

        // ------------------------------------------------------------------
        // Puzzle mode
        // ------------------------------------------------------------------

        /// <summary>Opens puzzle mode. Called by PuzzleManager when a puzzle is presented.</summary>
        public void EnterPuzzleMode()
        {
            if (State == GameState.Playing) SetState(GameState.Puzzle);
        }

        /// <summary>Returns to normal play after a puzzle panel closes.</summary>
        public void ExitPuzzleMode()
        {
            if (State == GameState.Puzzle) SetState(GameState.Playing);
        }

        // ------------------------------------------------------------------
        // Pause
        // ------------------------------------------------------------------

        public void PauseGame()
        {
            if (State == GameState.Playing) SetState(GameState.Paused);
        }

        public void ResumeGame()
        {
            if (State == GameState.Paused) SetState(GameState.Playing);
        }

        public void TogglePause()
        {
            if (State == GameState.Playing) PauseGame();
            else if (State == GameState.Paused) ResumeGame();
        }

        // ------------------------------------------------------------------
        // Navigation helpers (thin wrappers so the UI never touches SceneManager)
        // ------------------------------------------------------------------

        public void GoToMainMenu()
        {
            Scenes.LoadScene(GameConstants.SceneMainMenu, GameState.MainMenu);
        }

        public void GoToLogin()
        {
            Scenes.LoadScene(GameConstants.SceneLogin, GameState.Login);
        }

        public void PlayLevel(LevelId level)
        {
            string scene = GameConstants.SceneNameFor(level);
            if (string.IsNullOrEmpty(scene))
            {
                Debug.LogError($"[GameManager] No scene mapped for level {level}.");
                return;
            }
            Scenes.LoadScene(scene, GameState.Loading);
        }

        public void RestartCurrentLevel()
        {
            PlayLevel(Session.CurrentLevel);
        }

        /// <summary>Loads the level after the current one, or returns to the menu after Level 3.</summary>
        public void ContinueToNextLevel()
        {
            LevelId next = GameConstants.NextLevel(Session.CurrentLevel);
            if (next == LevelId.None) GoToMainMenu();
            else PlayLevel(next);
        }

        public void QuitApplication()
        {
            Save?.SaveNow();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
