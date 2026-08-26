using System;
using System.Collections.Generic;
using Cardio.Data;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>One row on the objective clipboard, authored per level.</summary>
    [Serializable]
    public class LevelObjective
    {
        [Tooltip("Text shown on the clipboard.")]
        public string Description = "";

        public ObjectiveKind Kind = ObjectiveKind.Puzzle;

        [Tooltip("For Kind == Puzzle: the PuzzleId that satisfies this objective.")]
        public string PuzzleId = "";

        /// <summary>Runtime only - always starts false, never authored.</summary>
        [NonSerialized] public bool Completed;
    }

    /// <summary>
    /// Owns the level's objective list and is the only writer of
    /// <see cref="ObjectiveBoardUI"/> from Phase 2 onwards.
    ///
    /// It listens to PuzzleManager rather than being called by it, so puzzles
    /// know nothing about objectives - a puzzle can be answered from anywhere
    /// (a station, a hint system, a test) and the board still updates.
    /// </summary>
    public class ObjectiveManager : MonoBehaviour
    {
        public static ObjectiveManager Instance { get; private set; }

        [SerializeField] private List<LevelObjective> objectives = new List<LevelObjective>();

        [Header("Board")]
        [SerializeField] private string boardTitle = "CURRENT OBJECTIVE";

        /// <summary>Raised when one objective flips to complete.</summary>
        public event Action<LevelObjective> ObjectiveCompleted;

        /// <summary>Raised once, when every objective is complete.</summary>
        public event Action AllObjectivesCompleted;

        public IReadOnlyList<LevelObjective> Objectives => objectives;

        public int CompletedCount
        {
            get
            {
                int n = 0;
                foreach (LevelObjective o in objectives) if (o.Completed) n++;
                return n;
            }
        }

        public bool AllComplete => objectives.Count > 0 && CompletedCount >= objectives.Count;

        private bool _allCompleteRaised;

        private void Awake()
        {
            Instance = this;

            // Completion is runtime state; make sure a re-loaded scene starts clean.
            foreach (LevelObjective o in objectives) o.Completed = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (PuzzleManager.Instance != null) PuzzleManager.Instance.PuzzleAnswered -= OnPuzzleAnswered;
        }

        private void Start()
        {
            if (PuzzleManager.Instance != null) PuzzleManager.Instance.PuzzleAnswered += OnPuzzleAnswered;
            else Debug.LogWarning("[ObjectiveManager] No PuzzleManager in the scene - puzzle objectives cannot complete.");

            Refresh();
        }

        // ------------------------------------------------------------------
        // Completion
        // ------------------------------------------------------------------

        private void OnPuzzleAnswered(PuzzleResult result)
        {
            // Only a correct answer advances an objective. An incorrect result is
            // still recorded for the DDA, but the clipboard row stays open.
            if (!result.Correct) return;

            for (int i = 0; i < objectives.Count; i++)
            {
                LevelObjective objective = objectives[i];
                if (objective.Kind != ObjectiveKind.Puzzle) continue;
                if (objective.PuzzleId != result.PuzzleId) continue;

                Complete(objective);
            }
        }

        /// <summary>Completes the ReachExit objective. Called by LevelController.</summary>
        public void CompleteExitObjective()
        {
            foreach (LevelObjective objective in objectives)
            {
                if (objective.Kind == ObjectiveKind.ReachExit) Complete(objective);
            }
        }

        /// <summary>Completes an objective by list index. Used for Custom objectives.</summary>
        public void CompleteObjective(int index)
        {
            if (index < 0 || index >= objectives.Count) return;
            Complete(objectives[index]);
        }

        /// <summary>True when every non-exit objective is done - the gate for opening the level exit.</summary>
        public bool AllNonExitObjectivesComplete()
        {
            foreach (LevelObjective objective in objectives)
            {
                if (objective.Kind == ObjectiveKind.ReachExit) continue;
                if (!objective.Completed) return false;
            }
            return true;
        }

        private void Complete(LevelObjective objective)
        {
            if (objective == null || objective.Completed) return;

            objective.Completed = true;
            ObjectiveCompleted?.Invoke(objective);
            Refresh();

            if (AllComplete && !_allCompleteRaised)
            {
                _allCompleteRaised = true;
                AllObjectivesCompleted?.Invoke();
            }
        }

        // ------------------------------------------------------------------
        // Board
        // ------------------------------------------------------------------

        /// <summary>Redraws the clipboard from the current objective state.</summary>
        public void Refresh()
        {
            ObjectiveBoardUI board = GameplayHUD.Instance != null ? GameplayHUD.Instance.ObjectiveBoard : null;
            if (board == null) return;

            board.SetTitle(boardTitle);

            var entries = new List<ObjectiveEntry>(objectives.Count);
            foreach (LevelObjective objective in objectives)
            {
                entries.Add(new ObjectiveEntry(objective.Description, objective.Completed));
            }

            board.SetObjectives(entries);
        }
    }
}
