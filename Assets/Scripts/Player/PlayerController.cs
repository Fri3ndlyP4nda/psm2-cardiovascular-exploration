using Cardio.Core;
using UnityEngine;

namespace Cardio.Player
{
    /// <summary>
    /// Movement for "Bloo.D. Clot".
    ///
    /// Built on CharacterController rather than Rigidbody physics because the
    /// PSM1 priority is reliable navigation around voxel heart walls, not
    /// physical simulation: CharacterController cannot tunnel through colliders
    /// at high speed and never accumulates unwanted angular velocity.
    ///
    /// Movement is camera-relative (W is always "away from the camera"), which
    /// is the expected feel for a third person game and keeps the player from
    /// getting disoriented inside a round chamber.
    ///
    /// Every tuning value is exposed in the Inspector per PSM1 rule 12.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement")]
        [SerializeField, Range(1f, 15f)] private float moveSpeed = 5f;
        [Tooltip("How quickly the character reaches full speed. Higher = snappier.")]
        [SerializeField, Range(1f, 30f)] private float acceleration = 12f;
        [Tooltip("Degrees per second the body turns to face the movement direction.")]
        [SerializeField, Range(90f, 1440f)] private float turnSpeed = 720f;

        [Header("Jump / Gravity")]
        [SerializeField] private bool jumpEnabled = true;
        [SerializeField, Range(0.5f, 5f)] private float jumpHeight = 1.4f;
        [SerializeField, Range(-40f, -1f)] private float gravity = -18f;
        [Tooltip("Grace period after leaving the ground during which a jump still registers.")]
        [SerializeField, Range(0f, 0.4f)] private float coyoteTime = 0.12f;
        [Tooltip("A jump pressed this long before landing is remembered and fires on touchdown.")]
        [SerializeField, Range(0f, 0.4f)] private float jumpBufferTime = 0.12f;

        [Header("Ground check")]
        [SerializeField, Range(0.01f, 0.5f)] private float groundCheckDistance = 0.15f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("References")]
        [Tooltip("Leave empty to use Camera.main. Movement is relative to this transform.")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("Optional visual child that gets squashed slightly while moving. Purely cosmetic.")]
        [SerializeField] private Transform visual;

        private CharacterController _controller;
        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private float _lastGroundedTime;
        private float _lastJumpPressedTime = -999f;

        /// <summary>Current planar speed in units/second. Read by the HUD and, later, by the PerformanceTracker.</summary>
        public float CurrentSpeed => _horizontalVelocity.magnitude;

        public bool IsGrounded { get; private set; }

        /// <summary>Set false to freeze the player (used during puzzles and cutscenes from Phase 2).</summary>
        public bool InputEnabled { get; set; } = true;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            if (cameraTransform == null && Camera.main != null) cameraTransform = Camera.main.transform;
        }

        private void Update()
        {
            // Gameplay input is ignored unless the game is actually being played,
            // so the pause menu and puzzle UI cannot be "walked through".
            bool canMove = InputEnabled && (GameManager.Instance == null || GameManager.Instance.State == GameState.Playing);

            Vector2 input = canMove ? Vector2.ClampMagnitude(PlayerInputReader.Move, 1f) : Vector2.zero;
            if (canMove && jumpEnabled && PlayerInputReader.JumpPressed) _lastJumpPressedTime = Time.time;

            UpdateGrounded();
            ApplyHorizontalMovement(input);
            ApplyVerticalMovement();

            Vector3 motion = _horizontalVelocity + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            UpdateVisual(input.sqrMagnitude > 0.01f);
        }

        private void UpdateGrounded()
        {
            // CharacterController.isGrounded is the primary source of truth. It is
            // unreliable only on the frame after a Move that lands exactly on a
            // surface, so a short spherecast is used as a second opinion.
            IsGrounded = _controller.isGrounded;

            if (!IsGrounded)
            {
                // Cast from the centre of the capsule's bottom sphere. The player's
                // own layer is masked out and any hit belonging to this hierarchy is
                // discarded - without both guards the cast starts inside the
                // CharacterController and reports "grounded" in mid-air, which would
                // hand the player an infinite jump.
                Vector3 origin = transform.position + _controller.center
                                 - Vector3.up * (_controller.height * 0.5f - _controller.radius);

                int mask = groundMask & ~(1 << gameObject.layer);

                if (Physics.SphereCast(origin, _controller.radius * 0.9f, Vector3.down, out RaycastHit hit,
                                       groundCheckDistance + _controller.skinWidth, mask, QueryTriggerInteraction.Ignore))
                {
                    IsGrounded = hit.collider != null && !hit.collider.transform.IsChildOf(transform);
                }
            }

            if (IsGrounded) _lastGroundedTime = Time.time;
        }

        private void ApplyHorizontalMovement(Vector2 input)
        {
            // Project the camera basis onto the ground plane so looking up or
            // down never slows the character or pushes it into the floor.
            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward; // camera looking straight down
            }

            Vector3 desired = (forward * input.y + right * input.x) * moveSpeed;
            _horizontalVelocity = Vector3.MoveTowards(_horizontalVelocity, desired, acceleration * Time.deltaTime);

            if (desired.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(desired.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
            }
        }

        private void ApplyVerticalMovement()
        {
            bool withinCoyote = Time.time - _lastGroundedTime <= coyoteTime;
            bool jumpBuffered = Time.time - _lastJumpPressedTime <= jumpBufferTime;

            if (jumpEnabled && withinCoyote && jumpBuffered)
            {
                // v = sqrt(2 * g * h) gives the exact launch speed for the requested height.
                _verticalVelocity = Mathf.Sqrt(2f * Mathf.Abs(gravity) * jumpHeight);
                _lastJumpPressedTime = -999f;
                _lastGroundedTime = -999f;
            }
            else if (IsGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f; // keep the controller pressed against the floor
            }
            else
            {
                _verticalVelocity += gravity * Time.deltaTime;
            }
        }

        private void UpdateVisual(bool moving)
        {
            if (visual == null) return;

            // A cheap "wobble" so the blood cell reads as alive without animation clips.
            float bob = moving ? Mathf.Sin(Time.time * 10f) * 0.05f : 0f;
            visual.localScale = Vector3.Lerp(visual.localScale, new Vector3(1f + bob, 1f - bob, 1f + bob), Time.deltaTime * 10f);
        }

        /// <summary>Teleports the player (used on spawn and on respawn after a failed attempt).</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            _controller.enabled = false;      // CharacterController overwrites transform changes while enabled
            transform.SetPositionAndRotation(position, rotation);
            _controller.enabled = true;

            _horizontalVelocity = Vector3.zero;
            _verticalVelocity = 0f;
        }
    }
}
