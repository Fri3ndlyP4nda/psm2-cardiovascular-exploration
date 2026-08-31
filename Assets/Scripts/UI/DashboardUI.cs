using System.Collections.Generic;
using System.Text;
using Cardio.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Cardio.UI
{
    /// <summary>
    /// The performance dashboard (PSM1 section 9 / Phase 9).
    ///
    /// Shows what the player has actually done: levels completed, accuracy,
    /// mean response time, difficulty reached, puzzles solved, mistakes, hints
    /// used, and a history of recent attempts.
    ///
    /// Reads <see cref="SaveManager"/> only. It deliberately does not touch
    /// <see cref="Cardio.DDA.PerformanceTracker"/>, because the tracker holds
    /// the *current session* and is empty on the main menu - the dashboard has
    /// to survive being opened before anything has been played, and has to
    /// still show last week's attempts after a restart. The local history in
    /// the save file is the only source that satisfies both.
    ///
    /// Every figure shown is derived from real recorded attempts. Where there is
    /// no data the panel says so rather than displaying a confident zero, which
    /// would read as "you scored 0%" instead of "you have not played yet".
    ///
    /// This component lives on the CANVAS, not on the panel it shows and hides -
    /// see the UI rules in ARCHITECTURE.md. A component on the object it
    /// deactivates never runs its own Awake, and the panel can never reopen.
    /// </summary>
    public class DashboardUI : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Button closeButton;

        [Header("Labels")]
        [SerializeField] private TMP_Text headerLabel;
        [SerializeField] private TMP_Text summaryLabel;
        [SerializeField] private TMP_Text historyLabel;

        /// <summary>How many recent attempts the history column lists.</summary>
        private const int HistoryRows = 8;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Open()
        {
            Refresh();
            if (panelRoot != null) panelRoot.SetActive(true);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Rebuilds both columns from the save file.</summary>
        public void Refresh()
        {
            SaveManager save = GameManager.Instance != null ? GameManager.Instance.Save : null;

            if (save == null)
            {
                SetText(headerLabel, "PERFORMANCE");
                SetText(summaryLabel, "No save data available.");
                SetText(historyLabel, string.Empty);
                return;
            }

            PlayerProgress progress = save.Progress;
            List<SessionRecord> history = save.RecentSessions(HistoryRows);

            SetText(headerLabel, $"PERFORMANCE - {progress.LastDisplayName}");
            SetText(summaryLabel, BuildSummary(progress, save.Progress.SessionHistory));
            SetText(historyLabel, BuildHistory(history));
        }

        // ------------------------------------------------------------------
        // Text building
        // ------------------------------------------------------------------

        /// <summary>
        /// Aggregates across every attempt still in the local history.
        ///
        /// Accuracy is computed from summed puzzle counts rather than by
        /// averaging each attempt's percentage: a two-puzzle attempt and a
        /// fifteen-puzzle attempt should not carry equal weight.
        /// </summary>
        private static string BuildSummary(PlayerProgress progress, List<SessionRecord> all)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"<b>Levels completed</b>   {progress.CompletedLevels.Count} of {GameConstants.LevelScenes.Length}");
            sb.AppendLine($"<b>Sessions played</b>    {progress.TotalSessionsPlayed}");
            sb.AppendLine($"<b>Attempts recorded</b>  {all.Count}");
            sb.AppendLine();

            if (all.Count == 0)
            {
                sb.AppendLine("<i>No attempts recorded yet.</i>");
                sb.AppendLine("<i>Finish a level and your results appear here.</i>");
                return sb.ToString();
            }

            int attempted = 0, correct = 0, incorrect = 0, failed = 0, hints = 0, score = 0, completed = 0;
            float responseSeconds = 0f, duration = 0f;
            int bestDifficulty = 0;

            foreach (SessionRecord r in all)
            {
                if (r == null) continue;

                attempted += r.PuzzlesAttempted;
                correct += r.PuzzlesCorrect;
                incorrect += r.IncorrectAnswers;
                failed += r.PuzzlesFailed;
                hints += r.HintsUsed;
                score += r.Score;
                duration += r.DurationSeconds;
                responseSeconds += r.AverageResponseSeconds * r.PuzzlesAttempted;

                if (r.Completed) completed++;
                if (r.FinalDifficulty > bestDifficulty) bestDifficulty = r.FinalDifficulty;
            }

            float accuracy = attempted > 0 ? (float)correct / attempted : 0f;
            float meanResponse = attempted > 0 ? responseSeconds / attempted : 0f;

            sb.AppendLine($"<b>Puzzles solved</b>     {correct} of {attempted}");
            sb.AppendLine($"<b>Accuracy</b>           {accuracy:P0}");
            sb.AppendLine($"<b>Mean response</b>      {meanResponse:F1}s");
            sb.AppendLine($"<b>Wrong answers</b>      {incorrect}");
            sb.AppendLine($"<b>Puzzles failed</b>     {failed}");
            sb.AppendLine($"<b>Hints used</b>         {hints}");
            sb.AppendLine();
            sb.AppendLine($"<b>Difficulty reached</b> {DifficultyName(bestDifficulty)}");
            sb.AppendLine($"<b>Total score</b>        {score}");
            sb.AppendLine($"<b>Time played</b>        {FormatDuration(duration)}");
            sb.AppendLine($"<b>Attempts finished</b>  {completed} of {all.Count}");

            return sb.ToString();
        }

        private static string BuildHistory(List<SessionRecord> recent)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<b>RECENT ATTEMPTS</b>");
            sb.AppendLine();

            if (recent.Count == 0)
            {
                sb.AppendLine("<i>Nothing recorded yet.</i>");
                return sb.ToString();
            }

            foreach (SessionRecord r in recent)
            {
                if (r == null) continue;

                string outcome = r.Completed ? "completed" : "not finished";
                sb.AppendLine($"<b>{LevelName(r.Level)}</b>  <size=80%>{r.DateUtc} UTC</size>");
                sb.AppendLine($"  {r.PuzzlesCorrect}/{r.PuzzlesAttempted} correct " +
                              $"({r.Accuracy01:P0}), {DifficultyName(r.FinalDifficulty)}, {outcome}");
                sb.AppendLine($"  score {r.Score}, {r.HintsUsed} hints, {FormatDuration(r.DurationSeconds)}");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        // ------------------------------------------------------------------
        // Formatting
        // ------------------------------------------------------------------

        private static string DifficultyName(int tier)
        {
            switch (tier)
            {
                case 1: return "Medium";
                case 2: return "Hard";
                default: return "Easy";
            }
        }

        private static string LevelName(int level)
        {
            switch (level)
            {
                case 1: return "Level 1 - Left Ventricle";
                case 2: return "Level 2 - Brain";
                case 3: return "Level 3 - Right Ventricle";
                default: return "Unknown level";
            }
        }

        private static string FormatDuration(float seconds)
        {
            if (seconds < 60f) return $"{Mathf.RoundToInt(seconds)}s";

            int minutes = Mathf.FloorToInt(seconds / 60f);
            int remainder = Mathf.RoundToInt(seconds - minutes * 60f);
            return $"{minutes}m {remainder:00}s";
        }

        private static void SetText(TMP_Text label, string value)
        {
            if (label != null) label.text = value;
        }
    }
}
