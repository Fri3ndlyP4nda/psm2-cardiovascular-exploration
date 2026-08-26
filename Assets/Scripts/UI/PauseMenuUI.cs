using Cardio.Core;
using Cardio.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// Pause menu: Resume, Restart, Settings, Exit to Main Menu.
    ///
    /// Listens for the pause key itself rather than having the player script
    /// push a pause request, so pausing keeps working even if the player object
    /// is destroyed or disabled.
    /// </summary>
    public class PauseMenuUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject pausePanel;
        [SerializeField] private SettingsPanel settingsPanel;

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button restartButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        private void Awake()
        {
            if (resumeButton != null) resumeButton.onClick.AddListener(Resume);
            if (restartButton != null) restartButton.onClick.AddListener(Restart);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (exitButton != null) exitButton.onClick.AddListener(ExitToMenu);

            if (pausePanel != null) pausePanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.StateChanged -= OnStateChanged;
        }

        private void Update()
        {
            if (!PlayerInputReader.PausePressed) return;

            var gm = GameManager.Instance;
            if (gm == null) return;

            // Escape inside the settings sub-panel closes that first.
            if (settingsPanel != null && settingsPanel.gameObject.activeSelf)
            {
                settingsPanel.Close();
                return;
            }

            // This component is the single owner of the Escape key. While a
            // puzzle is open, Escape backs out of it rather than pausing -
            // handling that here, instead of in PuzzleUI, keeps exactly one
            // reader of the key and removes any dependence on script order.
            if (gm.State == GameState.Puzzle)
            {
                Cardio.Gameplay.PuzzleManager.Instance?.AbandonPuzzle();
                return;
            }

            if (gm.State == GameState.Playing || gm.State == GameState.Paused) gm.TogglePause();
        }

        private void OnStateChanged(GameState state)
        {
            if (pausePanel != null) pausePanel.SetActive(state == GameState.Paused);

            if (state != GameState.Paused && settingsPanel != null && settingsPanel.gameObject.activeSelf)
            {
                settingsPanel.gameObject.SetActive(false);
            }
        }

        private void Resume() => GameManager.Instance?.ResumeGame();

        private void Restart() => GameManager.Instance?.RestartCurrentLevel();

        private void OpenSettings()
        {
            if (settingsPanel == null) return;

            if (pausePanel != null) pausePanel.SetActive(false);
            settingsPanel.Open(() =>
            {
                if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused && pausePanel != null)
                {
                    pausePanel.SetActive(true);
                }
            });
        }

        private void ExitToMenu() => GameManager.Instance?.GoToMainMenu();
    }
}
