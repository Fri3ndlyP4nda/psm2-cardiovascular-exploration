using System.Globalization;
using System.IO;
using System.Text;
using Cardio.Core;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Exports recorded play metrics as CSV for User Acceptance Testing.
    ///
    /// PSM1 evaluates the adaptive mechanic by comparing what players *did*
    /// against what they *said* in the questionnaire and interview. That
    /// comparison needs the objective half in a form a spreadsheet or SPSS can
    /// open, which is what this produces - one row per completed attempt, from
    /// the local session history.
    ///
    /// Deliberately reads the save file rather than live memory, so it can be
    /// run after the participant has finished and closed the game. For a
    /// multi-participant study the researcher collects each machine's
    /// psm2_progress.json and exports them one at a time.
    ///
    /// This is data plumbing, not analysis. It does not aggregate across
    /// participants, compute significance, or draw conclusions - those are the
    /// researcher's job and depend on the study design.
    /// </summary>
    public static class UatExport
    {
        [MenuItem("PSM2/UAT/Export Session Metrics to CSV", priority = 80)]
        public static void ExportCsv()
        {
            string savePath = Path.Combine(Application.persistentDataPath, "psm2_progress.json");

            if (!File.Exists(savePath))
            {
                Debug.LogError($"[PSM2 UAT] No save file at {savePath}. " +
                               "Play at least one level, or copy a participant's file there first.");
                return;
            }

            PlayerProgress progress;
            try
            {
                progress = JsonUtility.FromJson<PlayerProgress>(File.ReadAllText(savePath));
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PSM2 UAT] Could not read {savePath}: {e.Message}");
                return;
            }

            if (progress == null || progress.SessionHistory == null || progress.SessionHistory.Count == 0)
            {
                Debug.LogWarning("[PSM2 UAT] The save file has no recorded attempts. " +
                                 "Nothing to export - finish a level first.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("participant,date_utc,level,completed,final_difficulty,score," +
                          "puzzles_attempted,puzzles_correct,accuracy,incorrect_answers,puzzles_failed," +
                          "hints_used,mean_response_seconds,duration_seconds");

            foreach (SessionRecord r in progress.SessionHistory)
            {
                if (r == null) continue;

                sb.AppendLine(string.Join(",", new[]
                {
                    Csv(progress.LastDisplayName),
                    Csv(r.DateUtc),
                    r.Level.ToString(CultureInfo.InvariantCulture),
                    r.Completed ? "1" : "0",
                    DifficultyName(r.FinalDifficulty),
                    r.Score.ToString(CultureInfo.InvariantCulture),
                    r.PuzzlesAttempted.ToString(CultureInfo.InvariantCulture),
                    r.PuzzlesCorrect.ToString(CultureInfo.InvariantCulture),
                    r.Accuracy01.ToString("F4", CultureInfo.InvariantCulture),
                    r.IncorrectAnswers.ToString(CultureInfo.InvariantCulture),
                    r.PuzzlesFailed.ToString(CultureInfo.InvariantCulture),
                    r.HintsUsed.ToString(CultureInfo.InvariantCulture),
                    r.AverageResponseSeconds.ToString("F2", CultureInfo.InvariantCulture),
                    r.DurationSeconds.ToString("F1", CultureInfo.InvariantCulture)
                }));
            }

            string outputPath = Path.Combine(Application.persistentDataPath, "psm2_uat_metrics.csv");
            File.WriteAllText(outputPath, sb.ToString());

            Debug.Log($"[PSM2 UAT] Exported {progress.SessionHistory.Count} attempts to {outputPath}");
            // Batch mode has no file browser, and opening one there silently
            // does nothing useful - the same trap DisplayDialog set earlier.
            if (!Application.isBatchMode) EditorUtility.RevealInFinder(outputPath);
        }

        /// <summary>Quotes a field so a comma in a display name cannot shift every column.</summary>
        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "\"\"";
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string DifficultyName(int tier)
        {
            switch (tier)
            {
                case 1: return "Medium";
                case 2: return "Hard";
                default: return "Easy";
            }
        }
    }
}
