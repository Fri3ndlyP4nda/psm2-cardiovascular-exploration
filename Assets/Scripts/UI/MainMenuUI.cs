using Cardio.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// Main menu: Start Game, Continue, Profile, Settings, Exit.
    ///
    /// The buttons are wired in code (not through Inspector UnityEvents) so the
    /// navigation logic is visible in source and reviewable for the report.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button startButton;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button profileButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button exitButton;

        [Header("Panels")]
        [SerializeField] private GameObject rootPanel;
        [SerializeField] private GameObject levelSelectPanel;
        [SerializeField] private SettingsPanel settingsPanel;
        [SerializeField] private DashboardUI dashboard;

        [Header("Level select")]
        [SerializeField] private Button level1Button;
        [SerializeField] private Button level2Button;
        [SerializeField] private Button level3Button;
        [SerializeField] private Button levelSelectBackButton;

        [Header("Labels")]
        [SerializeField] private TMP_Text signedInAsLabel;
        [SerializeField] private TMP_Text versionLabel;
        [SerializeField] private TMP_Text noticeLabel;

        private void Start()
        {
            GameManager.Instance?.SetState(GameState.MainMenu);

            // Volume and fullscreen are applied here as well as at level start,
            // so the stored options take effect from the first screen.
            SettingsPanel.ApplySavedSettings();

            WireButtons();
            RefreshLabels();
            RefreshLevelButtons();
            ShowRoot();
        }

        private void WireButtons()
        {
            if (startButton != null) startButton.onClick.AddListener(OnStartGame);
            if (continueButton != null) continueButton.onClick.AddListener(OnContinue);
            if (profileButton != null) profileButton.onClick.AddListener(OnProfile);
            if (settingsButton != null) settingsButton.onClick.AddListener(OnSettings);
            if (exitButton != null) exitButton.onClick.AddListener(OnExit);

            if (level1Button != null) level1Button.onClick.AddListener(() => PlayLevel(LevelId.Level1_LeftVentricle));
            if (level2Button != null) level2Button.onClick.AddListener(() => PlayLevel(LevelId.Level2_Brain));
            if (level3Button != null) level3Button.onClick.AddListener(() => PlayLevel(LevelId.Level3_RightVentricle));
            if (levelSelectBackButton != null) levelSelectBackButton.onClick.AddListener(ShowRoot);
        }

        private void RefreshLabels()
        {
            var gm = GameManager.Instance;

            if (signedInAsLabel != null)
                signedInAsLabel.text = gm != null ? $"Signed in as: {gm.Session.DisplayName}" : "Signed in as: Guest";

            if (versionLabel != null)
                versionLabel.text = $"v{Application.version}  |  Educational prototype - not a medical device";

            if (noticeLabel != null) noticeLabel.text = string.Empty;
        }

        private void RefreshLevelButtons()
        {
            var save = GameManager.Instance?.Save;

            // Continue is only meaningful once something has been finished.
            if (continueButton != null) continueButton.interactable = save != null && save.HasProgress;

            SetLevelButton(level2Button, save == null || save.IsLevelUnlocked(LevelId.Level2_Brain));
            SetLevelButton(level3Button, save == null || save.IsLevelUnlocked(LevelId.Level3_RightVentricle));
        }

        private static void SetLevelButton(Button button, bool unlocked)
        {
            if (button == null) return;

            button.interactable = unlocked;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null && !unlocked && !label.text.EndsWith("(locked)")) label.text += "  (locked)";
        }

        // ------------------------------------------------------------------
        // Button handlers
        // ------------------------------------------------------------------

        private void OnStartGame()
        {
            GameManager.Instance?.StartNewSession(
                GameManager.Instance.Save?.Progress.LastUserId,
                GameManager.Instance.Save?.Progress.LastDisplayName);

            ShowLevelSelect();
        }

        private void OnContinue()
        {
            var gm = GameManager.Instance;
            if (gm?.Save == null) return;

            gm.StartNewSession(gm.Save.Progress.LastUserId, gm.Save.Progress.LastDisplayName);
            gm.PlayLevel(gm.Save.ResumeLevel());
        }

        private void OnProfile()
        {
            // The dashboard half of this screen landed in Phase 9 and reads the
            // local save history. The *account* half still depends on Firebase
            // Auth, so the panel names the player from local progress and says
            // nothing it cannot back up.
            if (dashboard == null)
            {
                ShowNotice("Dashboard unavailable - re-run PSM2 > Setup > Build or Rebuild Project.");
                return;
            }

            dashboard.Open();
        }

        private void OnSettings()
        {
            if (settingsPanel == null)
            {
                ShowNotice("Settings panel is not assigned.");
                return;
            }

            settingsPanel.Open(ShowRoot);
            if (rootPanel != null) rootPanel.SetActive(false);
        }

        private void OnExit() => GameManager.Instance?.QuitApplication();

        private void PlayLevel(LevelId level)
        {
            GameManager.Instance?.PlayLevel(level);
        }

        // ------------------------------------------------------------------
        // Panel switching
        // ------------------------------------------------------------------

        public void ShowRoot()
        {
            if (rootPanel != null) rootPanel.SetActive(true);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
        }

        private void ShowLevelSelect()
        {
            RefreshLevelButtons();
            if (rootPanel != null) rootPanel.SetActive(false);
            if (levelSelectPanel != null) levelSelectPanel.SetActive(true);
        }

        private void ShowNotice(string message)
        {
            if (noticeLabel != null) noticeLabel.text = message;
            Debug.Log($"[MainMenuUI] {message}");
        }
    }
}
