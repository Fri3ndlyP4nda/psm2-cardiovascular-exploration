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
    /// The two fixes for world-picking puzzles being unanswerable in practice:
    ///
    ///   B - the structure under the pointer is highlighted, so the chamber
    ///       visibly reads as clickable.
    ///   C - the camera can be orbited while a puzzle is open (right mouse
    ///       held), so a structure behind the panel is still reachable.
    /// </summary>
    public class PuzzleAffordanceTests
    {
        private TestInputSource _input;
        private OrbitCameraRig _rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return TestLevel.Load();

            _input = new TestInputSource();
            PlayerInputReader.SetSource(_input);

            _rig = Object.FindAnyObjectByType<OrbitCameraRig>();
            Assert.IsNotNull(_rig, "the gameplay camera should carry an OrbitCameraRig");
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
        // C - camera control during a puzzle
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator DuringAPuzzle_MovingTheMouseAloneDoesNotSpinTheCamera()
        {
            yield return OpenWorldPickingPuzzle();

            float yawBefore = _rig.Yaw;

            // Pointer moves, but no button is held - the camera must hold still,
            // otherwise aiming at a structure would drag the view with it.
            _input.LookAxis = new Vector2(4f, 0f);
            yield return new WaitForSeconds(0.4f);
            _input.Reset();

            Assert.AreEqual(yawBefore, _rig.Yaw, 0.01f,
                            "free mouse movement must not orbit while the cursor is being used to aim");
        }

        [UnityTest]
        public IEnumerator DuringAPuzzle_HoldingRightMouse_OrbitsTheCamera()
        {
            yield return OpenWorldPickingPuzzle();

            float yawBefore = _rig.Yaw;

            _input.HoldSecondary = true;
            _input.LookAxis = new Vector2(4f, 0f);
            yield return new WaitForSeconds(0.4f);
            _input.Reset();

            Assert.AreNotEqual(yawBefore, _rig.Yaw,
                               "holding right mouse should let the player look around during a puzzle");
            Assert.Greater(Mathf.Abs(_rig.Yaw - yawBefore), 1f, "the camera should turn a usable amount");
        }

        [UnityTest]
        public IEnumerator WhilePaused_TheCameraStaysLockedEvenWithRightMouseHeld()
        {
            GameManager.Instance.PauseGame();
            yield return null;

            float yawBefore = _rig.Yaw;

            _input.HoldSecondary = true;
            _input.LookAxis = new Vector2(4f, 0f);
            yield return new WaitForSecondsRealtime(0.4f);
            _input.Reset();

            Assert.AreEqual(yawBefore, _rig.Yaw, 0.01f, "pausing should still freeze the camera completely");

            GameManager.Instance.ResumeGame();
            yield return null;
        }

        // ------------------------------------------------------------------
        // B - hover highlighting
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator PointingAtAStructure_HighlightsIt_AndPointingAwayClearsIt()
        {
            yield return OpenWorldPickingPuzzle();

            // Aim the gameplay camera straight down at the chamber floor, which
            // is tagged as the left ventricle.
            _rig.enabled = false;
            Camera camera = Camera.main;
            camera.transform.position = new Vector3(0f, 12f, -4f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            yield return null;

            AnatomyMarker floorMarker = FindMarker("left_ventricle");

            // Start from a known-empty aim. The pointer defaults to (0,0), which
            // is the bottom-left of the screen and a perfectly real direction -
            // so "nothing hovered" has to be established, not assumed.
            _input.Pointer = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 4f);
            yield return TestLevel.Frames(3);
            Assert.IsFalse(floorMarker.IsHovered, "aiming at empty space should highlight nothing");

            _input.Pointer = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f);
            yield return TestLevel.Frames(3);
            Assert.IsTrue(floorMarker.IsHovered, "the structure under the pointer should be highlighted");

            _input.Pointer = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 4f);
            yield return TestLevel.Frames(3);
            Assert.IsFalse(floorMarker.IsHovered, "moving the pointer away should clear the highlight");
        }

        [UnityTest]
        public IEnumerator ClosingThePuzzle_ClearsAnyHover()
        {
            yield return OpenWorldPickingPuzzle();

            _rig.enabled = false;
            Camera camera = Camera.main;
            camera.transform.position = new Vector3(0f, 12f, -4f);
            camera.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            yield return null;

            _input.Pointer = new Vector2(camera.pixelWidth * 0.5f, camera.pixelHeight * 0.5f);
            yield return TestLevel.Frames(3);

            AnatomyMarker floorMarker = FindMarker("left_ventricle");
            Assert.IsTrue(floorMarker.IsHovered);

            PuzzleManager.Instance.AbandonPuzzle();
            yield return TestLevel.Frames(2);

            Assert.IsFalse(floorMarker.IsHovered, "closing the panel must not strand a highlight in the world");
        }

        [UnityTest]
        public IEnumerator AHintHighlight_SurvivesThePointerMovingAway()
        {
            // Hover and hint are separate channels; the pointer leaving must not
            // erase the hint that is showing the answer.
            yield return OpenWorldPickingPuzzle();

            AnatomyMarker marker = FindMarker("left_ventricle");
            marker.SetHighlighted(true);
            marker.SetHovered(true);
            yield return null;

            marker.SetHovered(false);

            Assert.IsTrue(marker.IsHighlighted, "the hint highlight must outlive the hover");

            marker.SetHighlighted(false);
            Assert.IsFalse(marker.IsHighlighted);
        }

        // ------------------------------------------------------------------
        // The instruction itself
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator TheInstruction_ExplainsBothAnsweringAndLookingAround()
        {
            yield return OpenWorldPickingPuzzle();
            yield return TestLevel.Frames(2);

            var instruction = TestLevel.Find("Instruction").GetComponent<TMP_Text>();
            StringAssert.Contains("lick", instruction.text, "the player must be told how to answer");
            StringAssert.Contains("right mouse", instruction.text,
                                  "the player must be told how to look around, since they cannot walk");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static IEnumerator OpenWorldPickingPuzzle()
        {
            foreach (PuzzleData puzzle in PuzzleManager.Instance.Bank.Puzzles)
            {
                if (puzzle == null || !puzzle.Type.UsesWorldPicking()) continue;
                if (puzzle.Complexity > PuzzleManager.Instance.MaxComplexity) continue;

                Assert.IsTrue(PuzzleManager.Instance.BeginPuzzle(puzzle));
                yield return null;
                yield break;
            }

            Assert.Fail("no world-picking puzzle available at the current tier");
        }

        private static AnatomyMarker FindMarker(string structureId)
        {
            foreach (AnatomyStructureTag tag in Object.FindObjectsByType<AnatomyStructureTag>(FindObjectsInactive.Exclude))
            {
                if (tag.StructureId == structureId && tag.Marker != null) return tag.Marker;
            }

            Assert.Fail($"no marker found for '{structureId}'");
            return null;
        }
    }
}
