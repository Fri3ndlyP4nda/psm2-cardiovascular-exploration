using Cardio.Player;
using UnityEngine;

namespace Cardio.Tests
{
    /// <summary>
    /// Scripted stand-in for the keyboard and mouse.
    ///
    /// Installed via <see cref="PlayerInputReader.SetSource"/>, so PlayerController,
    /// PlayerInteraction, PauseMenuUI and PuzzleUI all read from it without any
    /// of them knowing a test is running. That is what makes "hold W", "press
    /// Space" and "press Esc" objectively testable rather than manual steps.
    ///
    /// The *Pressed flags are one-shot: they clear themselves after being read,
    /// mirroring Unity's GetKeyDown semantics so a single call to
    /// <see cref="PressJump"/> cannot fire twice.
    /// </summary>
    public class TestInputSource : IPlayerInputSource
    {
        public Vector2 MoveAxis;
        public Vector2 LookAxis;
        public float Zoom;
        public bool HoldJump;
        public bool HoldSecondary;
        public Vector2 Pointer;

        private bool _jumpPressed;
        private bool _interactPressed;
        private bool _pausePressed;
        private bool _submitPressed;
        private bool _clickPressed;

        public Vector2 Move => MoveAxis;
        public Vector2 Look => LookAxis;
        public float ZoomDelta => Zoom;
        public bool JumpHeld => HoldJump;
        public bool SecondaryHeld => HoldSecondary;
        public Vector2 PointerPosition => Pointer;

        public bool JumpPressed => Consume(ref _jumpPressed);
        public bool InteractPressed => Consume(ref _interactPressed);
        public bool PausePressed => Consume(ref _pausePressed);
        public bool SubmitPressed => Consume(ref _submitPressed);
        public bool PrimaryClickPressed => Consume(ref _clickPressed);

        public void PressJump() => _jumpPressed = true;
        public void PressInteract() => _interactPressed = true;
        public void PressPause() => _pausePressed = true;
        public void PressSubmit() => _submitPressed = true;
        public void PressPrimaryClick() => _clickPressed = true;

        /// <summary>Releases every axis and clears any pending presses.</summary>
        public void Reset()
        {
            MoveAxis = Vector2.zero;
            LookAxis = Vector2.zero;
            Zoom = 0f;
            HoldJump = false;
            HoldSecondary = false;

            _jumpPressed = _interactPressed = _pausePressed = _submitPressed = _clickPressed = false;
        }

        private static bool Consume(ref bool flag)
        {
            if (!flag) return false;

            flag = false;
            return true;
        }
    }
}
