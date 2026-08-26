using Cardio.Core;
using UnityEngine;

namespace Cardio.Player
{
    /// <summary>
    /// Third-person orbit camera.
    ///
    /// Written by hand instead of using Cinemachine so the project has one
    /// fewer package dependency and so the collision behaviour (which matters a
    /// lot inside a closed heart chamber) stays explicit and easy to explain in
    /// the report.
    ///
    /// Runs in LateUpdate so it always reads the player's final position for
    /// the frame - doing this in Update produces visible camera jitter.
    /// </summary>
    [DisallowMultipleComponent]
    public class OrbitCameraRig : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [Tooltip("Offset from the target pivot, in world units. Roughly chest height.")]
        [SerializeField] private Vector3 targetOffset = new Vector3(0f, 1.2f, 0f);

        [Header("Orbit")]
        [SerializeField, Range(0.5f, 20f)] private float distance = 7f;
        [SerializeField, Range(2f, 30f)] private float minDistance = 3f;
        [SerializeField, Range(2f, 30f)] private float maxDistance = 12f;
        [SerializeField, Range(20f, 600f)] private float sensitivity = 220f;
        [SerializeField, Range(-89f, 0f)] private float minPitch = -25f;
        [SerializeField, Range(0f, 89f)] private float maxPitch = 70f;
        [SerializeField] private bool invertY;

        [Header("Smoothing")]
        [Tooltip("Seconds for the camera to catch up to the target. 0 = rigid.")]
        [SerializeField, Range(0f, 0.5f)] private float positionSmoothTime = 0.06f;

        [Header("Collision")]
        [Tooltip("Pulls the camera in when a heart wall would come between it and the player.")]
        [SerializeField] private bool avoidGeometry = true;
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField, Range(0.05f, 1f)] private float collisionRadius = 0.3f;

        private float _yaw;
        private float _pitch = 20f;
        private float _currentDistance;
        private Vector3 _smoothVelocity;

        /// <summary>Reused by the collision cast so the camera allocates nothing per frame.</summary>
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        private void Awake()
        {
            _currentDistance = distance;
            LoadPreferences();
        }

        private void Start()
        {
            if (target == null) AcquireTarget();
            SnapToTarget();
        }

        /// <summary>Re-reads sensitivity / invert-Y from PlayerPrefs. Called by the settings panel.</summary>
        public void LoadPreferences()
        {
            sensitivity = PlayerPrefs.GetFloat(GameConstants.PrefMouseSensitivity, sensitivity);
            invertY = PlayerPrefs.GetInt(GameConstants.PrefInvertY, invertY ? 1 : 0) == 1;
        }

        private void AcquireTarget()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null) target = player.transform;
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                AcquireTarget();
                if (target == null) return;
            }

            // Steering is allowed while playing, and during a puzzle only while
            // the right mouse button is held.
            //
            // Puzzle mode releases the cursor so structures can be clicked, so
            // free mouse-look would spin the camera every time the player moved
            // the pointer. Requiring a held button keeps both usable - and
            // without it a structure that happens to sit behind the puzzle panel
            // is simply unreachable, because the player cannot move either.
            GameState state = GameManager.Instance != null ? GameManager.Instance.State : GameState.Playing;

            bool playing = state == GameState.Playing;
            bool puzzleOrbit = state == GameState.Puzzle && PlayerInputReader.SecondaryHeld;

            if (playing || puzzleOrbit) ApplyLook();
            if (playing || state == GameState.Puzzle) ApplyZoom();

            Vector3 pivot = target.position + targetOffset;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 wanted = pivot - rotation * Vector3.forward * distance;

            float desiredDistance = avoidGeometry ? ResolveCollision(pivot, wanted) : distance;

            // Snap in immediately when blocked, ease back out when clear -
            // easing inwards would let the wall clip through for a few frames.
            _currentDistance = desiredDistance < _currentDistance
                ? desiredDistance
                : Mathf.Lerp(_currentDistance, desiredDistance, Time.deltaTime * 4f);

            Vector3 finalPosition = pivot - rotation * Vector3.forward * _currentDistance;

            transform.position = positionSmoothTime > 0f
                ? Vector3.SmoothDamp(transform.position, finalPosition, ref _smoothVelocity, positionSmoothTime)
                : finalPosition;

            // If the camera has been pushed all the way onto the pivot, the look
            // vector is zero and LookRotation would spam the console; keep the
            // previous rotation for that frame instead.
            Vector3 toPivot = pivot - transform.position;
            if (toPivot.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toPivot, Vector3.up);
        }

        /// <summary>
        /// Returns how far the camera may sit from the pivot without a wall
        /// coming between it and the player.
        ///
        /// The cast starts inside the player's own collider, so a plain
        /// SphereCast would return the player itself at distance 0 and slam the
        /// camera into its head. SphereCastNonAlloc lets every hit be inspected
        /// and the player's own hierarchy skipped; NonAlloc rather than
        /// SphereCastAll because this runs every frame.
        /// </summary>
        private float ResolveCollision(Vector3 pivot, Vector3 wantedPosition)
        {
            Vector3 direction = wantedPosition - pivot;
            if (direction.sqrMagnitude < 0.0001f) return distance;
            direction.Normalize();

            int mask = collisionMask;
            if (target != null) mask &= ~(1 << target.gameObject.layer);

            int count = Physics.SphereCastNonAlloc(pivot, collisionRadius, direction, _hitBuffer,
                                                   distance, mask, QueryTriggerInteraction.Ignore);

            float nearest = distance;
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = _hitBuffer[i].collider;
                if (hitCollider == null) continue;
                if (target != null && hitCollider.transform.IsChildOf(target)) continue;

                if (_hitBuffer[i].distance < nearest) nearest = _hitBuffer[i].distance;
            }

            return Mathf.Max(minDistance * 0.5f, nearest - collisionRadius * 0.5f);
        }

        private void ApplyLook()
        {
            Vector2 look = PlayerInputReader.Look;

            // Time.timeScale is 0 while paused, so unscaled delta keeps the feel
            // identical regardless of any slow-motion effects added later.
            float dt = Time.unscaledDeltaTime;
            _yaw += look.x * sensitivity * dt;
            _pitch += (invertY ? look.y : -look.y) * sensitivity * dt;
            _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
        }

        private void ApplyZoom()
        {
            float zoom = PlayerInputReader.ZoomDelta;
            if (Mathf.Abs(zoom) <= 0.0001f) return;

            distance = Mathf.Clamp(distance - zoom * 10f, minDistance, maxDistance);
        }

        /// <summary>Current orbit angles, exposed so tests can assert camera control.</summary>
        public float Yaw => _yaw;
        public float Pitch => _pitch;

        /// <summary>Places the camera behind the target instantly, with no interpolation.</summary>
        public void SnapToTarget()
        {
            if (target == null) return;

            _yaw = target.eulerAngles.y;
            _currentDistance = distance;

            Vector3 pivot = target.position + targetOffset;
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = pivot - rotation * Vector3.forward * distance;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
            _smoothVelocity = Vector3.zero;
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
            SnapToTarget();
        }
    }
}
