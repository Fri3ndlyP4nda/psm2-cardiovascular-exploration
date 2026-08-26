using UnityEngine;
#if !ENABLE_LEGACY_INPUT_MANAGER && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Cardio.Player
{
    /// <summary>
    /// A source of player input. Implemented by the hardware reader in normal
    /// play, and by a scripted stand-in during automated tests.
    /// </summary>
    public interface IPlayerInputSource
    {
        Vector2 Move { get; }
        Vector2 Look { get; }
        float ZoomDelta { get; }
        bool JumpHeld { get; }
        bool JumpPressed { get; }
        bool InteractPressed { get; }
        bool PausePressed { get; }
        bool SubmitPressed { get; }
        bool PrimaryClickPressed { get; }

        /// <summary>Right mouse button held. Used to orbit while the cursor is free.</summary>
        bool SecondaryHeld { get; }

        Vector2 PointerPosition { get; }
    }

    /// <summary>
    /// Thin abstraction over Unity input.
    ///
    /// The project ships configured for the classic Input Manager (zero setup,
    /// which matters for a build that must run on any lab machine), but the
    /// new Input System package is supported too: the conditional compilation
    /// below picks whichever backend the project is set to, so installing the
    /// package later does not require touching PlayerController or the camera.
    ///
    /// TEST SEAM: <see cref="SetSource"/> substitutes a scripted source for the
    /// hardware. This is what allows movement, jumping, pausing and interaction
    /// to be verified automatically rather than by a human holding keys down -
    /// the production code path is unchanged, because PlayerController still
    /// reads exactly these properties.
    /// </summary>
    public static class PlayerInputReader
    {
        private static IPlayerInputSource _source;

        /// <summary>Replaces hardware input with a scripted source. Tests only.</summary>
        public static void SetSource(IPlayerInputSource source) => _source = source;

        /// <summary>Restores hardware input.</summary>
        public static void ClearSource() => _source = null;

        /// <summary>True while a scripted source is installed.</summary>
        public static bool IsOverridden => _source != null;

        // ---- Public API: scripted source when present, hardware otherwise ----
        public static Vector2 Move => _source != null ? _source.Move : HardwareMove;
        public static Vector2 Look => _source != null ? _source.Look : HardwareLook;
        public static float ZoomDelta => _source != null ? _source.ZoomDelta : HardwareZoomDelta;
        public static bool JumpHeld => _source != null ? _source.JumpHeld : HardwareJumpHeld;
        public static bool JumpPressed => _source != null ? _source.JumpPressed : HardwareJumpPressed;
        public static bool InteractPressed => _source != null ? _source.InteractPressed : HardwareInteractPressed;
        public static bool PausePressed => _source != null ? _source.PausePressed : HardwarePausePressed;
        public static bool SubmitPressed => _source != null ? _source.SubmitPressed : HardwareSubmitPressed;
        public static bool PrimaryClickPressed => _source != null ? _source.PrimaryClickPressed : HardwarePrimaryClickPressed;
        public static bool SecondaryHeld => _source != null ? _source.SecondaryHeld : HardwareSecondaryHeld;
        public static Vector2 PointerPosition => _source != null ? _source.PointerPosition : HardwarePointerPosition;

#if ENABLE_LEGACY_INPUT_MANAGER
        // ---------------- Classic Input Manager ----------------
        private static Vector2 HardwareMove => new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        private static Vector2 HardwareLook => new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));
        private static float HardwareZoomDelta => Input.GetAxis("Mouse ScrollWheel");
        private static bool HardwareJumpHeld => Input.GetButton("Jump");
        private static bool HardwareJumpPressed => Input.GetButtonDown("Jump");
        private static bool HardwareInteractPressed => Input.GetKeyDown(KeyCode.E);
        private static bool HardwarePausePressed => Input.GetKeyDown(KeyCode.Escape);
        private static bool HardwareSubmitPressed => Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
        private static bool HardwarePrimaryClickPressed => Input.GetMouseButtonDown(0);
        private static bool HardwareSecondaryHeld => Input.GetMouseButton(1);
        private static Vector2 HardwarePointerPosition => Input.mousePosition;

#elif ENABLE_INPUT_SYSTEM
        // ---------------- New Input System ----------------
        private static Keyboard K => Keyboard.current;
        private static Mouse M => Mouse.current;

        private static Vector2 HardwareMove
        {
            get
            {
                if (K == null) return Vector2.zero;
                float x = (K.dKey.isPressed || K.rightArrowKey.isPressed ? 1f : 0f) -
                          (K.aKey.isPressed || K.leftArrowKey.isPressed ? 1f : 0f);
                float y = (K.wKey.isPressed || K.upArrowKey.isPressed ? 1f : 0f) -
                          (K.sKey.isPressed || K.downArrowKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        // Scaled to roughly match the classic "Mouse X/Y" axis magnitude so the
        // sensitivity values tuned on one backend still feel right on the other.
        private static Vector2 HardwareLook => M == null ? Vector2.zero : M.delta.ReadValue() * 0.05f;
        private static float HardwareZoomDelta => M == null ? 0f : M.scroll.ReadValue().y * 0.01f;
        private static bool HardwareJumpHeld => K != null && K.spaceKey.isPressed;
        private static bool HardwareJumpPressed => K != null && K.spaceKey.wasPressedThisFrame;
        private static bool HardwareInteractPressed => K != null && K.eKey.wasPressedThisFrame;
        private static bool HardwarePausePressed => K != null && K.escapeKey.wasPressedThisFrame;
        private static bool HardwareSubmitPressed => K != null && (K.enterKey.wasPressedThisFrame || K.numpadEnterKey.wasPressedThisFrame);
        private static bool HardwarePrimaryClickPressed => M != null && M.leftButton.wasPressedThisFrame;
        private static bool HardwareSecondaryHeld => M != null && M.rightButton.isPressed;
        private static Vector2 HardwarePointerPosition => M == null ? Vector2.zero : M.position.ReadValue();

#else
        // ---------------- No input backend enabled ----------------
        private static Vector2 HardwareMove => Vector2.zero;
        private static Vector2 HardwareLook => Vector2.zero;
        private static float HardwareZoomDelta => 0f;
        private static bool HardwareJumpHeld => false;
        private static bool HardwareJumpPressed => false;
        private static bool HardwareInteractPressed => false;
        private static bool HardwarePausePressed => false;
        private static bool HardwareSubmitPressed => false;
        private static bool HardwarePrimaryClickPressed => false;
        private static bool HardwareSecondaryHeld => false;
        private static Vector2 HardwarePointerPosition => Vector2.zero;
#endif
    }
}
