using System.Collections;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.Gameplay;
using Cardio.Player;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace Cardio.Tests
{
    /// <summary>
    /// Asserts what is actually *on screen* in the puzzle panel for each of the
    /// five formats.
    ///
    /// The existing suites verified that answers are accepted; none of them
    /// checked that the player is shown anything to answer with. This closes
    /// that gap: sections active/inactive, buttons active, labels populated and
    /// non-degenerate in size.
    /// </summary>
    public class PuzzlePanelContentTests
    {
        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            // Hard lifts the complexity cap so every format can be opened.
            DDAManager.Instance.ForceTier(DifficultyTier.Hard, "panel content test");
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            if (PuzzleManager.Instance != null && PuzzleManager.Instance.IsPuzzleActive)
            {
                PuzzleManager.Instance.AbandonPuzzle();
            }

            PlayerInputReader.ClearSource();
        }

        // ------------------------------------------------------------------
        // Multiple choice
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator MultipleChoice_ShowsOneVisibleButtonPerOption()
        {
            PuzzleData puzzle = Open(PuzzleType.MultipleChoice);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(Section("OptionsSection").activeInHierarchy, "the options section should be visible");
            Assert.IsFalse(Section("SequenceSection").activeInHierarchy, "the sequence section should be hidden");

            for (int i = 0; i < 4; i++)
            {
                GameObject button = TestLevel.Find($"Btn_Option{i}");
                Assert.IsNotNull(button, $"Btn_Option{i} should exist in the panel");

                bool shouldBeUsed = i < puzzle.Options.Length;
                Assert.AreEqual(shouldBeUsed, button.activeInHierarchy,
                                $"Btn_Option{i} active state should match the option count");

                if (!shouldBeUsed) continue;

                var label = button.GetComponentInChildren<TMP_Text>(true);
                Assert.IsNotNull(label, $"Btn_Option{i} should have a text label");
                Assert.AreEqual(puzzle.Options[i], label.text, $"Btn_Option{i} shows the wrong option text");
                Assert.IsNotEmpty(label.text.Trim(), $"Btn_Option{i} label is blank");

                var rect = (RectTransform)button.transform;
                Assert.Greater(rect.rect.width, 50f, $"Btn_Option{i} is too narrow to click");
                Assert.Greater(rect.rect.height, 10f, $"Btn_Option{i} has collapsed to zero height");
            }
        }

        // ------------------------------------------------------------------
        // Blood flow sequence
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator BloodFlowSequence_ShowsOneVisibleButtonPerStep()
        {
            PuzzleData puzzle = Open(PuzzleType.BloodFlowSequence);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(Section("SequenceSection").activeInHierarchy, "the sequence section should be visible");
            Assert.IsFalse(Section("OptionsSection").activeInHierarchy, "the options section should be hidden");

            for (int i = 0; i < 6; i++)
            {
                GameObject button = TestLevel.Find($"Btn_Step{i}");
                Assert.IsNotNull(button, $"Btn_Step{i} should exist in the panel");

                bool shouldBeUsed = i < puzzle.SequenceSteps.Length;
                Assert.AreEqual(shouldBeUsed, button.activeInHierarchy,
                                $"Btn_Step{i} active state should match the step count");

                if (!shouldBeUsed) continue;

                var label = button.GetComponentInChildren<TMP_Text>(true);
                Assert.IsNotNull(label);
                Assert.IsNotEmpty(label.text.Trim(), $"Btn_Step{i} label is blank");
                CollectionAssert.Contains(puzzle.SequenceSteps, label.text,
                                          $"Btn_Step{i} shows text that is not one of the steps");

                var rect = (RectTransform)button.transform;
                Assert.Greater(rect.rect.height, 10f, $"Btn_Step{i} has collapsed to zero height");
            }
        }

        // ------------------------------------------------------------------
        // Structure formats
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator IdentifyStructure_ShowsAnInstruction_AndNoAnswerButtons()
        {
            Open(PuzzleType.IdentifyStructure);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(Section("StructureSection").activeInHierarchy, "the structure section should be visible");
            Assert.IsFalse(Section("OptionsSection").activeInHierarchy,
                           "identify puzzles are answered in the world, so option buttons stay hidden");
            Assert.IsFalse(Section("SequenceSection").activeInHierarchy);

            var instruction = TestLevel.Find("Instruction").GetComponent<TMP_Text>();
            Assert.IsNotNull(instruction);
            Assert.IsNotEmpty(instruction.text.Trim(), "the player must be told how to answer");
            StringAssert.Contains("lick", instruction.text, "an identify puzzle should say to click a structure");

            // The drag chip belongs to drag-and-drop only.
            Assert.IsFalse(TestLevel.Find("DragChip").activeInHierarchy,
                           "the drag chip should be hidden for a click-to-identify puzzle");
        }

        [UnityTest]
        public IEnumerator DragAndDrop_ShowsThePopulatedLabelChip()
        {
            PuzzleData puzzle = Open(PuzzleType.DragAndDropLabel);
            yield return TestLevel.Frames(2);

            Assert.IsTrue(Section("StructureSection").activeInHierarchy);

            GameObject chip = TestLevel.Find("DragChip");
            Assert.IsTrue(chip.activeInHierarchy, "a drag-and-drop puzzle must show its label chip");

            var label = chip.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(label);
            Assert.AreEqual(puzzle.ResolveLabelText(), label.text, "the chip should carry the puzzle's label");
            Assert.IsNotEmpty(label.text.Trim(), "the chip label is blank");
        }

        // ------------------------------------------------------------------
        // The prompt itself
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator EveryFormat_ShowsItsPromptAndHeader()
        {
            foreach (PuzzleType type in new[]
                     {
                         PuzzleType.IdentifyStructure, PuzzleType.DragAndDropLabel,
                         PuzzleType.BloodFlowSequence, PuzzleType.ValveIdentification,
                         PuzzleType.MultipleChoice
                     })
            {
                PuzzleData puzzle = Open(type);
                yield return TestLevel.Frames(2);

                var prompt = TestLevel.Find("Prompt").GetComponent<TMP_Text>();
                var header = TestLevel.Find("Header").GetComponent<TMP_Text>();

                Assert.AreEqual(puzzle.Prompt, prompt.text, $"{type}: prompt text mismatch");
                Assert.IsNotEmpty(header.text.Trim(), $"{type}: header is blank");

                PuzzleManager.Instance.AbandonPuzzle();
                yield return TestLevel.WaitUntil(() => !PuzzleManager.Instance.IsPuzzleActive, 10f, "panel to close");
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static GameObject Section(string name)
        {
            GameObject section = TestLevel.Find(name);
            Assert.IsNotNull(section, $"the puzzle panel should contain {name}");
            return section;
        }

        private static PuzzleData Open(PuzzleType type)
        {
            foreach (PuzzleData puzzle in PuzzleManager.Instance.Bank.Puzzles)
            {
                if (puzzle == null || puzzle.Type != type) continue;
                if (PuzzleManager.Instance.IsSolved(puzzle.PuzzleId)) continue;

                Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle), $"could not open a {type} puzzle");
                return puzzle;
            }

            Assert.Fail($"Level 1 bank has no {type} puzzle");
            return null;
        }
    }
}
