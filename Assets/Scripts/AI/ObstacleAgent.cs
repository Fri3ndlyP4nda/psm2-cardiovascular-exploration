using System.Collections.Generic;
using Cardio.Player;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>The two mobile biological obstacles from PSM1 section 15.</summary>
    public enum ObstacleKind
    {
        /// <summary>Small, fast, aggressive. Hunts the player down.</summary>
        Neutrophil = 0,

        /// <summary>Large, slow, bulky. Occupies space and obstructs routes.</summary>
        Monocyte = 1
    }

    /// <summary>How an obstacle chooses where to go.</summary>
    public enum ObstacleBehaviour
    {
        /// <summary>Path straight to the player whenever they are within detection range.</summary>
        Chase = 0,

        /// <summary>Cycle between fixed points, blocking whatever lies between them.</summary>
        Patrol = 1
    }

    /// <summary>
    /// Behaviour layer on top of <see cref="PathfindingAgent"/>: decides where to
    /// go and hurts the player on contact.
    ///
    /// Neutrophils and monocytes share this one component rather than having a
    /// class each, because they differ only in data - speed, size, damage and
    /// whether they hunt or patrol. Two near-identical subclasses would be
    /// duplicate code with nothing to justify it (PSM1 rule 14); the distinct
    /// prefabs carry the difference instead.
    ///
    /// Movement speed is not set here: PathfindingAgent applies the DDA
    /// multiplier, so a monocyte on Hard is genuinely faster than one on Easy.
    /// </summary>
    [RequireComponent(typeof(PathfindingAgent))]
    [DisallowMultipleComponent]
    public class ObstacleAgent : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private ObstacleKind kind = ObstacleKind.Neutrophil;
        [SerializeField] private ObstacleBehaviour behaviour = ObstacleBehaviour.Chase;

        [Header("Chase")]
        [Tooltip("The player is only hunted inside this radius. Outside it the agent returns to its post.")]
        [SerializeField, Range(3f, 60f)] private float detectionRadius = 18f;

        [Tooltip("Once hunting, the agent keeps going until the player is this far away.")]
        [SerializeField, Range(4f, 80f)] private float loseInterestRadius = 26f;

        [Header("Patrol")]
        [Tooltip("Points cycled through in order. Empty means the agent holds its spawn position.")]
        [SerializeField] private List<Transform> patrolPoints = new List<Transform>();

        [SerializeField, Range(0f, 10f)] private float patrolWaitSeconds = 1.5f;

        [Header("Contact damage")]
        [SerializeField, Range(0, 50)] private int contactDamage = 10;
        [SerializeField, Range(0.2f, 5f)] private float damageInterval = 1f;
        [SerializeField, Range(0.5f, 5f)] private float contactRadius = 1.1f;

        private PathfindingAgent _agent;
        private Transform _player;
        private Transform _homePoint;
        private Vector3 _homePosition;

        private int _patrolIndex;
        private float _patrolResumeTime;
        private float _nextDamageTime;
        private bool _hunting;

        public ObstacleKind Kind => kind;
        public bool IsHunting => _hunting;

        private void Awake()
        {
            _agent = GetComponent<PathfindingAgent>();
            _homePosition = transform.position;

            // Chasers pick their own target; patrollers are steered by this script.
            var home = new GameObject($"{name}_Home");
            home.transform.position = _homePosition;
            _homePoint = home.transform;
        }

        private void Start()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player != null) _player = player.transform;

            if (behaviour == ObstacleBehaviour.Patrol) AdvancePatrol();
        }

        private void OnDestroy()
        {
            if (_homePoint != null) Destroy(_homePoint.gameObject);
        }

        private void Update()
        {
            if (_player == null)
            {
                var player = FindAnyObjectByType<PlayerController>();
                if (player != null) _player = player.transform;
                else return;
            }

            switch (behaviour)
            {
                case ObstacleBehaviour.Chase: UpdateChase(); break;
                case ObstacleBehaviour.Patrol: UpdatePatrol(); break;
            }

            ApplyContactDamage();
        }

        // ------------------------------------------------------------------
        // Behaviours
        // ------------------------------------------------------------------

        /// <summary>
        /// Hysteresis on the detection range: an agent that starts hunting keeps
        /// going until the player is clearly away. Using one radius for both
        /// would make agents flicker in and out of pursuit at the boundary.
        /// </summary>
        private void UpdateChase()
        {
            float distance = Vector3.Distance(transform.position, _player.position);

            if (!_hunting && distance <= detectionRadius) _hunting = true;
            else if (_hunting && distance > loseInterestRadius) _hunting = false;

            Transform desired = _hunting ? _player : _homePoint;
            if (_agent.Target != desired) _agent.Target = desired;
        }

        private void UpdatePatrol()
        {
            if (Time.time < _patrolResumeTime) return;
            if (_agent.HasPath) return;

            _patrolResumeTime = Time.time + patrolWaitSeconds;
            AdvancePatrol();
        }

        private void AdvancePatrol()
        {
            if (patrolPoints == null || patrolPoints.Count == 0)
            {
                _agent.Target = _homePoint;
                return;
            }

            _patrolIndex = (_patrolIndex + 1) % patrolPoints.Count;

            Transform next = patrolPoints[_patrolIndex];
            if (next != null) _agent.Target = next;
        }

        // ------------------------------------------------------------------
        // Damage
        // ------------------------------------------------------------------

        /// <summary>
        /// A proximity check rather than a trigger collider: agents already carry
        /// a CharacterController for movement, and adding a second overlapping
        /// trigger to each one produces noisy enter/exit pairs as they jostle.
        /// </summary>
        private void ApplyContactDamage()
        {
            if (contactDamage <= 0 || Time.time < _nextDamageTime) return;

            float distance = Vector3.Distance(transform.position, _player.position);
            if (distance > contactRadius + 0.5f) return;

            PlayerHealth health = _player.GetComponentInParent<PlayerHealth>();
            if (health == null) return;

            health.TakeDamage(contactDamage);
            _nextDamageTime = Time.time + damageInterval;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, detectionRadius);

            Gizmos.color = new Color(0.9f, 0.2f, 0.2f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, contactRadius);

            if (patrolPoints == null) return;

            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.8f);
            foreach (Transform point in patrolPoints)
            {
                if (point != null) Gizmos.DrawWireCube(point.position, Vector3.one * 0.6f);
            }
        }
    }
}
