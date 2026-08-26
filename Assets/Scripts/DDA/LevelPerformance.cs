using System;
using Cardio.Core;
using UnityEngine;

namespace Cardio.DDA
{
    /// <summary>
    /// Everything measured for one level during this session.
    ///
    /// The field list is deliberately the shape of a Firestore SESSION_LOGS
    /// document (PSM1 section 20), so Phase 7 can serialise one of these per
    /// level without reshaping anything. It is also what the Phase 9 dashboard
    /// reads.
    ///
    /// One record per level, not per attempt: retrying after a failure keeps
    /// accumulating into the same record and increments <see cref="LevelFailures"/>,
    /// which gives a truer picture of how much work a level actually cost.
    /// </summary>
    [Serializable]
    public class LevelPerformance
    {
        public LevelId Level = LevelId.None;

        [Header("Puzzles")]
        public int PuzzlesAttempted;
        public int PuzzlesCorrect;

        /// <summary>Puzzles that used up every attempt and resolved as failed.</summary>
        public int PuzzlesFailed;

        /// <summary>Every wrong submission, including repeats on the same puzzle.</summary>
        public int IncorrectAnswers;

        /// <summary>Hints the player asked for.</summary>
        public int HintsUsed;

        /// <summary>Hints the DDA offered unprompted. Assistance received, not requested.</summary>
        public int AutoHintsGiven;

        /// <summary>Hints earned by destroying the blast cell a wrong answer spawned.</summary>
        public int EarnedHints;

        [Header("Combat")]
        /// <summary>Leukemic blasts spawned by wrong answers in this level.</summary>
        public int TotalHostilesSpawned;

        /// <summary>Leukemic blasts destroyed in this level.</summary>
        public int TotalHostilesKilled;

        public float TotalResponseSeconds;

        [Header("Survival")]
        /// <summary>Times Blood Count reached zero in this level.</summary>
        public int LevelFailures;

        public int DamageTaken;
        public int LowestBloodCount = int.MaxValue;

        [Header("Outcome")]
        public int Score;
        public DifficultyTier FinalDifficulty = DifficultyTier.Easy;
        public float DurationSeconds;
        public bool Completed;

        /// <summary>Longest run of consecutive wrong answers seen in this level.</summary>
        public int MaxConsecutiveFailures;

        /// <summary>Ratio of puzzles answered correctly, 0..1. Zero while nothing has been attempted.</summary>
        public float Accuracy01 => PuzzlesAttempted <= 0 ? 0f : (float)PuzzlesCorrect / PuzzlesAttempted;

        /// <summary>Mean seconds spent per resolved puzzle.</summary>
        public float AverageResponseSeconds => PuzzlesAttempted <= 0 ? 0f : TotalResponseSeconds / PuzzlesAttempted;

        /// <summary>Mean wrong submissions per puzzle - a finer signal than accuracy alone.</summary>
        public float AverageIncorrectPerPuzzle => PuzzlesAttempted <= 0 ? 0f : (float)IncorrectAnswers / PuzzlesAttempted;

        /// <summary>Blood Count at its worst, or 0 if the level was never entered.</summary>
        public int LowestBloodCountOrZero => LowestBloodCount == int.MaxValue ? 0 : LowestBloodCount;

        public override string ToString()
        {
            return $"{Level}: {PuzzlesCorrect}/{PuzzlesAttempted} correct ({Accuracy01 * 100f:0.#}%), " +
                   $"avg {AverageResponseSeconds:0.00}s, {IncorrectAnswers} wrong, " +
                   $"{HintsUsed} hints (+{AutoHintsGiven} auto), " +
                   $"score {Score}, difficulty {FinalDifficulty}";
        }
    }
}
