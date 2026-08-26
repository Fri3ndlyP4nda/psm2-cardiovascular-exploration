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
        [Tooltip("Renderers dimmed while dead, restored on respawn.")]
        [SerializeField] private Renderer[] bodyRenderers;

        private NpcHealth _health;
        private PathfindingAgent _pathfinding;
        private CharacterController _controller;

        /// <summary>The question this blast was spawned for. Killing it reveals that question's hint.</summary>
        public string PuzzleId { get; private set; }

        /// <summary>Where it first appeared. Respawning returns it here.</summary>
        public Vector3 SpawnPosition { get; private set; }

        public NpcHealth Health => _health;
        public bool IsAlive => _health != null && _health.IsAlive;

        private void Awake()
        {
            _health = GetComponent<NpcHealth>();
            _pathfinding = GetComponent<PathfindingAgent>();
            _controller = GetComponent<CharacterController>();

            SpawnPosition = transform.position;
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
            _health.Revive();
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
