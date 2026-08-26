using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Cardio.UI
{
    /// <summary>One row on the objective clipboard.</summary>
    public struct ObjectiveEntry
    {
        public string Text;
        public bool Completed;

        public ObjectiveEntry(string text, bool completed = false)
        {
            Text = text;
            Completed = completed;
        }
    }

    /// <summary>
    /// The medical clipboard style objective board from the PSM1 UI design.
    ///
    /// Rows are pre-created by the scene generator and simply shown or hidden,
    /// so updating the list never instantiates or destroys UI objects - that
    /// keeps the HUD off the garbage collector during play, which matters for
    /// the 60 FPS target.
    ///
    /// In Phase 1 the rows are pushed by LevelController. From Phase 2 the
    /// ObjectiveManager becomes the only writer.
    /// </summary>
    public class ObjectiveBoardUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleLabel;
        [Tooltip("Pre-created rows, in display order. Extra rows are hidden automatically.")]
        [SerializeField] private List<TMP_Text> rows = new List<TMP_Text>();

        [Header("Appearance")]
        [SerializeField] private string pendingPrefix = "[ ]  ";
        [SerializeField] private string completedPrefix = "[X]  ";
        [SerializeField] private Color pendingColor = new Color(0.16f, 0.17f, 0.20f);
        [SerializeField] private Color completedColor = new Color(0.35f, 0.55f, 0.35f);

        public int Capacity => rows.Count;

        public void SetTitle(string title)
        {
            if (titleLabel != null) titleLabel.text = title;
        }

        /// <summary>Replaces every row. Entries beyond <see cref="Capacity"/> are dropped.</summary>
        public void SetObjectives(IReadOnlyList<ObjectiveEntry> entries)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                TMP_Text row = rows[i];
                if (row == null) continue;

                if (entries != null && i < entries.Count)
                {
                    ObjectiveEntry entry = entries[i];
                    row.gameObject.SetActive(true);
                    row.text = (entry.Completed ? completedPrefix : pendingPrefix) + entry.Text;
                    row.color = entry.Completed ? completedColor : pendingColor;
                    row.fontStyle = entry.Completed ? FontStyles.Strikethrough : FontStyles.Normal;
                }
                else
                {
                    row.gameObject.SetActive(false);
                }
            }

            if (entries != null && entries.Count > rows.Count)
            {
                Debug.LogWarning($"[ObjectiveBoardUI] {entries.Count} objectives supplied but only {rows.Count} rows exist.");
            }
        }

        /// <summary>Convenience for a single objective.</summary>
        public void SetSingleObjective(string text, bool completed = false)
        {
            SetObjectives(new[] { new ObjectiveEntry(text, completed) });
        }
    }
}
