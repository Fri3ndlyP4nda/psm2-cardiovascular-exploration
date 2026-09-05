using Cardio.Player;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>
    /// A malignant white blood cell - a leukemic blast.
    ///
    /// STORY AND ACCURACY: the body is under attack by a white blood cancer.
    /// These are the cancerous cells, and they are the only thing the player
    /// fights. Neutrophils and monocytes remain the body's legitimate immune
    /// defenders and stay untouched as ordinary hazards, which keeps the game
    /// from teaching that immune cells are the enemy.
    ///
    /// Movement and chasing are reused wholesale from PathfindingAgent and
    /// ObstacleAgent. What is layered on top is everything those two lack:
    /// health, death, a link back to the question that spawned it, and the
    /// ability to be revived when the level respawns its hostiles.
    /// </summary>
    [RequireComponent(typeof(NpcHealth))]
    [RequireComponent(typeof(PathfindingAgent))]
    [DisallowMultipleComponent]
    public class LeukemicBlastAgent : MonoBehaviour
    {
        [Header("Visual state")]
        [Tooltip("Body renderers, flashed on each hit so the player can see a swing land.")]
        [SerializeField] private Renderer[] bodyRenderers;

        [Tooltip("How long the hit flash lasts.")]
        [SerializeField, Range(0.05f, 1f)] private float hitFlashSeconds = 0.18f;

        /// <summary>
        /// Set on the renderers rather than the material, so flashing does not
        /// instantiate a per-instance material copy for every blast in the level.
        /// </summary>
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private MaterialPropertyBlock _flashBlock;
        private float _hitFlashUntil = -1f;
        private int _lastKnownHealth;

        private NpcHealth _health;
        private PathfindingAgent _pathfinding;
        private CharacterController _controller;

        /// <summary>The question this blast was spawned for. Killing it reveals that question's hint.</summary>
        public string PuzzleId { get; private set; }

        /// <summary>Where it first appeared. Respawning returns it here.</summary>
        public Vector3 SpawnPosition { get; private set; }

        public NpcHealth Health => _health;
        public bool IsAlive => _health != null && _health.IsAlive;

        /// <summary>True while the body is flashing from a hit.</summary>
        public bool HitFlashActive => _hitFlashUntil >= 0f;

        private void Awake()
        {
            _health = GetComponent<NpcHealth>();
            _pathfinding = GetComponent<PathfindingAgent>();
            _controller = GetComponent<CharacterController>();

            SpawnPosition = transform.position;

            _flashBlock = new MaterialPropertyBlock();
            _lastKnownHealth = _health != null ? _health.CurrentHealth : 0;
        }

        private void OnEnable()
        {
            if (_health != null) _health.HealthChanged += OnHealthChanged;
        }

        private void OnDisable()
        {
            if (_health != null) _health.HealthChanged -= OnHealthChanged;

            // Leaving a half-finished flash on the renderers would make a revived
            // blast come back mid-glow.
            _hitFlashUntil = -1f;
            ApplyFlash(0f);
        }

        /// <summary>
        /// Flashes the body when a swing lands.
        ///
        /// NpcHealth has raised HealthChanged since Phase 6 and nothing listened, so
        /// a blast took three hits to kill while showing no reaction to the first
        /// two. That is not only unsatisfying - it is unfair, because the attack is
        /// a sphere in front of the player and there was no way to learn its reach
        /// except by dying to a blast you thought you were hitting.
        /// </summary>
        private void OnHealthChanged(int current, int max)
        {
            bool tookDamage = current < _lastKnownHealth;
            _lastKnownHealth = current;

            if (!tookDamage || hitFlashSeconds <= 0f) return;

            _hitFlashUntil = Time.time + hitFlashSeconds;
        }

        private void Update()
        {
            if (_hitFlashUntil < 0f) return;

            float remaining = (_hitFlashUntil - Time.time) / Mathf.Max(0.0001f, hitFlashSeconds);

            if (remaining <= 0f)
            {
                _hitFlashUntil = -1f;
                ApplyFlash(0f);
                return;
            }

            ApplyFlash(Mathf.Clamp01(remaining));
        }

        /// <summary>Drives the emission tint on the body renderers. 0 restores normal.</summary>
        private void ApplyFlash(float strength)
        {
            if (bodyRenderers == null || _flashBlock == null) return;

            Color emission = Color.white * (strength * 1.6f);

            foreach (Renderer r in bodyRenderers)
            {
                if (r == null) continue;

                r.GetPropertyBlock(_flashBlock);
                _flashBlock.SetColor(EmissionId, emission);
                r.SetPropertyBlock(_flashBlock);
            }
        }

        /// <summary>Binds this blast to the question that produced it.</summary>
        public void Initialise(string puzzleId, Vector3 spawnPosition)
        {
            PuzzleId = puzzleId;
            SpawnPosition = spawnPosition;

            Teleport(spawnPosition);
        }

        /// <summary>Returns the blast to its spawn point at full health.</summary>
        public void Respawn()
        {
            _hitFlashUntil = -1f;
            ApplyFlash(0f);

            _health.Revive();
            _lastKnownHealth = _health.CurrentHealth;
            Teleport(SpawnPosition);

            gameObject.SetActive(true);
            _pathfinding.RequestPath();
        }

        /// <summary>
        /// Repositions the blast safely.
        ///
        /// Writing transform.position directly does not work while a
        /// CharacterController is enabled - it keeps its own idea of where it is
        /// and overwrites the change on the next move. Anything relocating an
        /// agent must go through here.
        /// </summary>
        public void MoveTo(Vector3 position) => Teleport(position);

        private void Teleport(Vector3 position)
        {
            if (_controller != null) _controller.enabled = false;
            transform.position = position;
            if (_controller != null) _controller.enabled = true;
        }

        private void OnDrawGizmosSelected()
        {
            if (string.IsNullOrEmpty(PuzzleId)) return;

            Gizmos.color = new Color(0.8f, 0.3f, 0.9f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, 1.5f);
        }
    }
}
