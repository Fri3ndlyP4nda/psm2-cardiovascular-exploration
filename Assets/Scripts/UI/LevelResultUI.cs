using Cardio.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// The two end-of-attempt panels: "Level Complete" and "Attempt Failed".
    ///
    /// Both are driven purely by GameState, so any system that can put the game
    /// into LevelComplete or GameOver automatically gets the right screen -
    /// including the Blood Count reaching zero, which is the Phase 1 failure path.
    ///
    /// The summary text already reads the session metrics. They are all zero
    /// until Phase 3 wires up the PerformanceTracker; the layout is in place so
    /// that phase only has to supply numbers.
    /// </summary>
    public class LevelResultUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject completePanel;
        [SerializeField] private GameObject failedPanel;

        [Header("Complete panel")]
        [SerializeField] private TMP_Text completeTitleLabel;
        [SerializeField] private TMP_Text completeSummaryLabel;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button completeMenuButton;

        [Header("Failed panel")]
        [SerializeField] private TMP_Text failedSummaryLabel;
        [SerializeField] private Button retryButton;
        [SerializeField] private Button failedMenuButton;

        private void Awake()
        {
            if (completePanel != null) completePanel.SetActive(false);
            if (failedPanel != null) failedPanel.SetActive(false);

            if (nextLevelButton != null) nextLevelButton.onClick.AddListener(() => GameManager.Instance?.ContinueToNextLevel());
            if (completeMenuButton != null) completeMenuButton.onClick.AddListener(() => GameManager.Instance?.GoToMainMenu());
            if (retryButton != null) retryButton.onClick.AddListener(() => GameManager.Instance?.RestartCurrentLevel());
            if (failedMenuButton != null) failedMenuButton.onClick.AddListener(() => GameManager.Instance?.GoToMainMenu());
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

        private void OnStateChanged(GameState state)
        {
            bool complete = state == GameState.LevelComplete;
            bool failed = state == GameState.GameOver;

            if (completePanel != null) completePanel.SetActive(complete);
            if (failedPanel != null) failedPanel.SetActive(failed);

            if (complete) FillCompletePanel();
            if (failed) FillFailedPanel();
        }

        private void FillCompletePanel()
        {
            SessionData s = GameManager.Instance?.Session;
            if (s == null) return;

            if (completeTitleLabel != null)
                completeTitleLabel.text = $"{GameConstants.DisplayNameFor(s.CurrentLevel)} complete";

            if (completeSummaryLabel != null)
                completeSummaryLabel.text = BuildSummary(s);

            // No level after the third one; send the player back to the menu instead.
            if (nextLevelButton != null)
            {
                bool hasNext = GameConstants.NextLevel(s.CurrentLevel) != LevelId.None;
                nextLevelButton.gameObject.SetActive(hasNext);
            }
        }

        private void FillFailedPanel()
        {
            SessionData s = GameManager.Instance?.Session;
            if (s == null || failedSummaryLabel == null) return;

            failedSummaryLabel.text =
                "Blood Count reached zero.\n\n" + BuildSummary(s);
        }

        private static string BuildSummary(SessionData s)
        {
            return
                $"Difficulty reached : {s.CurrentDifficulty}\n" +
                $"Score              : {s.Score}\n" +
                $"Puzzles solved     : {s.PuzzlesCorrect} / {s.PuzzlesAttempted}\n" +
                $"Accuracy           : {s.Accuracy01 * 100f:0.#}%\n" +
                $"Avg response time  : {s.AverageResponseTime:0.##}s\n" +
                $"Wrong answers      : {s.IncorrectAnswers}\n" +
                $"Hints used         : {s.HintsUsed}\n" +
                $"Blood Count losses : {s.LevelFailures}\n" +
                $"Session duration   : {s.SessionDurationSeconds:0}s";
        }
    }
}
