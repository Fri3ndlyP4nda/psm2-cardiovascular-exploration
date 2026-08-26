using System.Collections.Generic;
using Cardio.Core;
using Cardio.DDA;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>
    /// Requests a path, follows it, and re-requests when the world moves under
    /// it.
    ///
    /// Movement goes through a CharacterController rather than writing to
    /// transform directly. That gives a second, physical guarantee against the
    /// PSM1 requirement that entities never walk through walls: even if a path
    /// were imperfect, the controller would slide along the geometry instead of
    /// passing through it.
    ///
    /// Speed is multiplied by <see cref="DDAManager.ObstacleSpeedMultiplier"/>,
    /// read live every frame. This is the whole of the PSM1 section 14
    /// integration: the DDA changes the speed parameter, and A* carries on
    /// finding valid routes exactly as before. The search is never touched.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [DisallowMultipleComponent]
    public class PathfindingAgent : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;

        [Tooltip("Find the player automatically when no target is assigned.")]
        [SerializeField] private bool autoTargetPlayer = true;

        [Header("Movement")]
        [Tooltip("Base speed before the difficulty multiplier is applied.")]
        [SerializeField, Range(0.5f, 12f)] private float baseSpeed = 2.5f;

        [SerializeField, Range(45f, 1080f)] private float turnSpeed = 360f;
        [SerializeField, Range(-40f, -1f)] private float gravity = -18f;

        [Tooltip("How close counts as having reached a waypoint.")]
        [SerializeField, Range(0.2f, 3f)] private float waypointTolerance = 0.8f;

        [Tooltip("Stop moving when this close to the target.")]
        [SerializeField, Range(0f, 6f)] private float stoppingDistance = 1.2f;

        [Header("Repathing")]
        [SerializeField, Range(0.1f, 5f)] private float repathInterval = 0.6f;

        [Tooltip("Repath immediately if the target has moved further than this since the last path.")]
        [SerializeField, Range(0.5f, 10f)] private float targetMoveThreshold = 2f;

        [Header("Anti-stuck")]
        [Tooltip("Seconds of near-zero progress before the agent forces a fresh path.")]
        [SerializeField, Range(0.5f, 10f)] private float stuckTimeout = 1.5f;

        [SerializeField, Range(0.01f, 1f)] private float stuckDistanceThreshold = 0.15f;

        [Header("Debug")]
        [SerializeField] private bool drawPathGizmo = true;

        private CharacterController _controller;
        private readonly List<Vector3> _path = new List<Vector3>();
        private int _waypointIndex;

        private float _nextRepathTime;
        private Vector3 _lastTargetPosition;
        private Vector3 _lastPosition;
        private float _stuckTimer;
        private float _verticalVelocity;

        /// <summary>Speed after the current difficulty tier's multiplier.</summary>
        public float CurrentSpeed => baseSpeed * (DDAManager.Instance != null ? DDAManager.Instance.ObstacleSpeedMultiplier : 1f);

        /// <summary>True when the agent holds a route it has not finished walking.</summary>
        public bool HasPath => _path.Count > 0 && _waypointIndex < _path.Count;

        /// <summary>Times this agent has had to recover from being stuck. Useful when tuning.</summary>
        public int StuckRecoveries { get; private set; }

        public Transform Target
        {
            get => target;
            set { target = value; _nextRepathTime = 0f; }
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _lastPosition = transform.position;
        }

        private void Update()
        {
            // Agents freeze outside normal play, so they cannot creep up on the
            // player while a puzzle panel is open or the game is paused.
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;

            AcquireTarget();
            UpdatePathIfNeeded();
            FollowPath();
            DetectStuck();
        }

        // ------------------------------------------------------------------
        // Pathing
        // ------------------------------------------------------------------

        private void AcquireTarget()
        {
            if (target != null || !autoTargetPlayer) return;

            var player = FindAnyObjectByType<Cardio.Player.PlayerController>();
            if (player != null) target = player.transform;
        }

        private void UpdatePathIfNeeded()
        {
            if (target == null || AStarPathfindingManager.Instance == null) return;

            bool intervalElapsed = Time.time >= _nextRepathTime;
            bool targetMoved = (target.position - _lastTargetPosition).sqrMagnitude > targetMoveThreshold * targetMoveThreshold;

            if (!intervalElapsed && !targetMoved) return;

            RequestPath();
        }

        /// <summary>Asks the manager for a fresh route to the current target.</summary>
        public void RequestPath()
        {
            if (target == null || AStarPathfindingManager.Instance == null) return;

            _nextRepathTime = Time.time + repathInterval;
            _lastTargetPosition = target.position;

            if (AStarPathfindingManager.Instance.FindPath(transform.position, target.position, _path))
            {
                _waypointIndex = 0;
            }
            else
            {
                // No route: stand still rather than drifting blindly towards the
                // target and grinding against a wall.
                _path.Clear();
                _waypointIndex = 0;
            }
        }

        private void FollowPath()
        {
            Vector3 horizontal = Vector3.zero;

            if (HasPath && !IsWithinStoppingDistance())
            {
                Vector3 waypoint = _path[_waypointIndex];
                Vector3 toWaypoint = waypoint - transform.position;
                toWaypoint.y = 0f;

                if (toWaypoint.magnitude <= waypointTolerance)
                {
                    _waypointIndex++;
                }
                else
                {
                    Vector3 direction = toWaypoint.normalized;
                    horizontal = direction * CurrentSpeed;

                    Quaternion look = Quaternion.LookRotation(direction, Vector3.up);
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * Time.deltaTime);
                }
            }

            // Gravity keeps agents on the floor as it changes height between the
            // chamber and the corridors.
            if (_controller.isGrounded && _verticalVelocity < 0f) _verticalVelocity = -2f;
            else _verticalVelocity += gravity * Time.deltaTime;

            _controller.Move((horizontal + Vector3.up * _verticalVelocity) * Time.deltaTime);
        }

        private bool IsWithinStoppingDistance()
        {
            if (target == null) return true;

            Vector3 flat = target.position - transform.position;
            flat.y = 0f;

            return flat.magnitude <= stoppingDistance;
        }

        // ------------------------------------------------------------------
        // Anti-stuck
        // ------------------------------------------------------------------

        /// <summary>
        /// PSM1 requires that agents never become permanently stuck. An agent
        /// that holds a path but has stopped making progress - wedged on a
        /// corner, or shoved by another agent - throws its route away and asks
        /// for a new one from wherever it now is.
        /// </summary>
        private void DetectStuck()
        {
            if (!HasPath || IsWithinStoppingDistance())
            {
                _stuckTimer = 0f;
                _lastPosition = transform.position;
                return;
            }

            float moved = (transform.position - _lastPosition).magnitude;
            _lastPosition = transform.position;

            if (moved > stuckDistanceThreshold * Time.deltaTime * 60f)
            {
                _stuckTimer = 0f;
                return;
            }

            _stuckTimer += Time.deltaTime;
            if (_stuckTimer < stuckTimeout) return;

            _stuckTimer = 0f;
            StuckRecoveries++;
            _path.Clear();
            _waypointIndex = 0;
            _nextRepathTime = 0f;

            RequestPath();
        }

        private void OnDrawGizmosSelected()
        {
            if (!drawPathGizmo || _path.Count == 0) return;

            Gizmos.color = new Color(1f, 0.5f, 0.2f);
            Vector3 previous = transform.position;

            for (int i = _waypointIndex; i < _path.Count; i++)
            {
                Gizmos.DrawLine(previous + Vector3.up * 0.3f, _path[i] + Vector3.up * 0.3f);
                Gizmos.DrawWireSphere(_path[i] + Vector3.up * 0.3f, 0.2f);
                previous = _path[i];
            }
        }
    }
}
