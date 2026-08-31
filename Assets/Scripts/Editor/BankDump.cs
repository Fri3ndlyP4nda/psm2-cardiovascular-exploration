using System.IO;
using System.Text;
using Cardio.Core;
using Cardio.Data;
using UnityEditor;
using UnityEngine;

namespace Cardio.EditorTools
{
    /// <summary>
    /// Dumps every shipped puzzle from the real QuestionBank assets.
    ///
    /// Used to source Documentation/WALKTHROUGH.md from actual data rather than
    /// from the factory source or from memory. Reads the same assets
    /// PuzzleContentTests validates, so anything written from this output is
    /// backed by the content the game actually loads.
    /// </summary>
    public static class BankDump
    {
        [MenuItem("PSM2/Content/Dump Question Banks to Text", priority = 35)]
        public static void Dump()
        {
            var sb = new StringBuilder();

            foreach (LevelId level in new[]
                     {
                         LevelId.Level1_LeftVentricle,
                         LevelId.Level2_Brain,
                         LevelId.Level3_RightVentricle
                     })
            {
                string path = $"Assets/Data/QuestionBank_{level}.asset";
                var bank = AssetDatabase.LoadAssetAtPath<QuestionBank>(path);

                if (bank == null)
                {
                    sb.AppendLine($"### {level}: BANK NOT FOUND at {path}");
                    continue;
                }

                sb.AppendLine($"### {level}  ({bank.Count} puzzles)");
                sb.AppendLine();

                foreach (PuzzleData p in bank.Puzzles)
                {
                    if (p == null) { sb.AppendLine("  <null puzzle slot>"); continue; }

                    sb.AppendLine($"ID        {p.PuzzleId}");
                    sb.AppendLine($"TYPE      {p.Type}");
                    sb.AppendLine($"COMPLEX   {p.Complexity}");
                    sb.AppendLine($"PROMPT    {p.Prompt}");

                    switch (p.Type)
                    {
                        case PuzzleType.MultipleChoice:
                            for (int i = 0; i < p.Options.Length; i++)
                            {
                                string mark = i == p.CorrectOptionIndex ? " <== CORRECT" : "";
                                sb.AppendLine($"OPTION[{i}] {p.Options[i]}{mark}");
                            }
                            break;

                        case PuzzleType.BloodFlowSequence:
                            for (int i = 0; i < p.SequenceSteps.Length; i++)
                            {
                                sb.AppendLine($"STEP[{i}]   {p.SequenceSteps[i]}");
                            }
                            break;

                        default:
                            sb.AppendLine($"TARGET    {p.TargetStructureId}");
                            if (!string.IsNullOrEmpty(p.LabelText)) sb.AppendLine($"LABEL     {p.LabelText}");
                            break;
                    }

                    sb.AppendLine($"HINT      {p.Hint}");
                    sb.AppendLine($"EXPLAIN   {p.Explanation}");
                    sb.AppendLine();
                }

                sb.AppendLine();
            }

            string outPath = Path.Combine(Application.dataPath, "..", "bankdump.txt");
            File.WriteAllText(Path.GetFullPath(outPath), sb.ToString());
            Debug.Log($"[PSM2] Bank dump written to {Path.GetFullPath(outPath)}");
        }
    }
}
