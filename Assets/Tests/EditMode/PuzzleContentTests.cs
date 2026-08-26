using System.Collections.Generic;
using Cardio.Core;
using Cardio.Data;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Cardio.Tests
{
    /// <summary>
    /// TC-12 / TC-13 (validation half) and TC-16 (content integrity).
    ///
    /// Answer validation lives on PuzzleData as pure methods, so correctness can
    /// be checked exhaustively without a scene: every shipped puzzle is asked
    /// its own question and given both a right and a wrong answer.
    ///
    /// This does NOT cover the UI half of TC-12/TC-13 - dragging a chip or
    /// clicking an option. Those are asserted at the manager level in PlayMode
    /// and by hand in the Game window.
    /// </summary>
    public class PuzzleContentTests
    {
        private static readonly LevelId[] Levels =
        {
            LevelId.Level1_LeftVentricle,
            LevelId.Level2_Brain,
            LevelId.Level3_RightVentricle
        };

        private static QuestionBank LoadBank(LevelId level)
        {
            string path = $"Assets/Data/QuestionBank_{level}.asset";
            var bank = AssetDatabase.LoadAssetAtPath<QuestionBank>(path);
            Assert.IsNotNull(bank, $"missing question bank at {path}");
            return bank;
        }

        // ------------------------------------------------------------------
        // TC-16 content integrity
        // ------------------------------------------------------------------

        [Test]
        public void EveryBank_PassesItsOwnValidator([ValueSource(nameof(Levels))] LevelId level)
        {
            List<string> problems = LoadBank(level).Validate();
            CollectionAssert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void EveryBank_MeetsThePsm1QuestionCount([ValueSource(nameof(Levels))] LevelId level)
        {
            // PSM1 section 25 asks for 10-15 questions per level.
            int count = LoadBank(level).Count;
            Assert.GreaterOrEqual(count, 10, $"{level} has too few puzzles");
            Assert.LessOrEqual(count, 15, $"{level} has more puzzles than PSM1 specifies");
        }

        [Test]
        public void EveryBank_CoversAllThreeComplexities([ValueSource(nameof(Levels))] LevelId level)
        {
            // Without a spread of complexities the DDA has nothing to select between.
            var seen = new HashSet<int>();
            foreach (PuzzleData puzzle in LoadBank(level).Puzzles) seen.Add(puzzle.Complexity);

            CollectionAssert.Contains(seen, 1, $"{level} has no complexity-1 puzzle");
            CollectionAssert.Contains(seen, 2, $"{level} has no complexity-2 puzzle");
            CollectionAssert.Contains(seen, 3, $"{level} has no complexity-3 puzzle");
        }

        [Test]
        public void EveryPuzzle_HasTeachingFeedback([ValueSource(nameof(Levels))] LevelId level)
        {
            // The explanation is where the learning happens; a puzzle without one
            // is just a quiz question.
            foreach (PuzzleData puzzle in LoadBank(level).Puzzles)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(puzzle.Explanation),
                               $"{puzzle.PuzzleId} has no explanation");
                Assert.IsFalse(string.IsNullOrWhiteSpace(puzzle.Hint),
                               $"{puzzle.PuzzleId} has no hint");
            }
        }

        [Test]
        public void Level1_StructureTargets_ExistInTheScene()
        {
            // A typo'd TargetStructureId produces a puzzle that can never be
            // answered. The scene YAML is searched rather than opened so this
            // stays an EditMode test.
            string scenePath = $"Assets/Scenes/{GameConstants.SceneLevel1}.unity";
            Assert.IsTrue(System.IO.File.Exists(scenePath), $"missing {scenePath}");

            string yaml = System.IO.File.ReadAllText(scenePath);

            foreach (PuzzleData puzzle in LoadBank(LevelId.Level1_LeftVentricle).Puzzles)
            {
                if (!puzzle.Type.UsesWorldPicking()) continue;

                StringAssert.Contains(puzzle.TargetStructureId, yaml,
                    $"{puzzle.PuzzleId} targets '{puzzle.TargetStructureId}', absent from Level 1");
            }
        }

        // ------------------------------------------------------------------
        // TC-12 / TC-13 answer validation
        // ------------------------------------------------------------------

        [Test]
        public void EveryPuzzle_AcceptsItsCorrectAnswer([ValueSource(nameof(Levels))] LevelId level)
        {
            foreach (PuzzleData puzzle in LoadBank(level).Puzzles)
            {
                switch (puzzle.Type)
                {
                    case PuzzleType.MultipleChoice:
                        Assert.IsTrue(puzzle.IsCorrectOption(puzzle.CorrectOptionIndex), puzzle.PuzzleId);
                        break;

                    case PuzzleType.BloodFlowSequence:
                        Assert.IsTrue(puzzle.IsCorrectSequence(puzzle.SequenceSteps), puzzle.PuzzleId);
                        break;

                    default:
                        Assert.IsTrue(puzzle.IsCorrectStructure(puzzle.TargetStructureId), puzzle.PuzzleId);
                        break;
                }
            }
        }

        [Test]
        public void EveryPuzzle_RejectsAWrongAnswer([ValueSource(nameof(Levels))] LevelId level)
        {
            foreach (PuzzleData puzzle in LoadBank(level).Puzzles)
            {
                switch (puzzle.Type)
                {
                    case PuzzleType.MultipleChoice:
                    {
                        int wrong = (puzzle.CorrectOptionIndex + 1) % puzzle.Options.Length;
                        Assert.IsFalse(puzzle.IsCorrectOption(wrong), puzzle.PuzzleId);
                        Assert.IsFalse(puzzle.IsCorrectOption(-1), $"{puzzle.PuzzleId} accepted index -1");
                        Assert.IsFalse(puzzle.IsCorrectOption(puzzle.Options.Length),
                                       $"{puzzle.PuzzleId} accepted an out-of-range index");
                        break;
                    }

                    case PuzzleType.BloodFlowSequence:
                    {
                        var reversed = new List<string>(puzzle.SequenceSteps);
                        reversed.Reverse();
                        Assert.IsFalse(puzzle.IsCorrectSequence(reversed), $"{puzzle.PuzzleId} accepted reversed order");

                        var truncated = new List<string>(puzzle.SequenceSteps);
                        truncated.RemoveAt(0);
                        Assert.IsFalse(puzzle.IsCorrectSequence(truncated), $"{puzzle.PuzzleId} accepted a short sequence");
                        Assert.IsFalse(puzzle.IsCorrectSequence(null), $"{puzzle.PuzzleId} accepted null");
                        break;
                    }

                    default:
                        Assert.IsFalse(puzzle.IsCorrectStructure("definitely_not_a_structure"), puzzle.PuzzleId);
                        Assert.IsFalse(puzzle.IsCorrectStructure(""), $"{puzzle.PuzzleId} accepted an empty id");
                        Assert.IsFalse(puzzle.IsCorrectStructure(null), $"{puzzle.PuzzleId} accepted null");
                        break;
                }
            }
        }

        [Test]
        public void StructureMatching_IgnoresCaseAndWhitespace()
        {
            PuzzleData puzzle = LoadBank(LevelId.Level1_LeftVentricle).Find("lv1_id_left_ventricle");
            Assert.IsNotNull(puzzle);

            Assert.IsTrue(puzzle.IsCorrectStructure("  LEFT_VENTRICLE  "),
                          "structure ids should match case-insensitively and ignore padding");
        }

        [Test]
        public void PuzzleIds_AreUniqueAcrossAllBanks()
        {
            // Objectives and stations reference puzzles by id, so a collision
            // between levels would silently complete the wrong objective.
            var seen = new HashSet<string>();

            foreach (LevelId level in Levels)
            {
                foreach (PuzzleData puzzle in LoadBank(level).Puzzles)
                {
                    Assert.IsTrue(seen.Add(puzzle.PuzzleId), $"duplicate PuzzleId '{puzzle.PuzzleId}'");
                }
            }
        }
    }
}
