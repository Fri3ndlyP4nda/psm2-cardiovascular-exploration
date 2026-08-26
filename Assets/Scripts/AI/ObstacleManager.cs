using System.Collections.Generic;
using Cardio.DDA;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>
    /// Scene-level registry for the mobile obstacles.
    ///
    /// The agents themselves are placed in the scene by the generator rather
    /// than spawned at runtime, so their positions are visible and tunable in
    /// the editor. This class therefore does not spawn: it collects them, gives
    /// the rest of the game one place to ask about them, and can switch them off
    /// wholesale - which is what the fixed-difficulty control condition in the
    /// PSM2 evaluation needs, and what makes it possible to test the puzzle flow
    /// without being chased.
    ///
    /// It deliberately does NOT push the difficulty multiplier onto agents.
    /// Each PathfindingAgent reads <see cref="DDAManager.ObstacleSpeedMultiplier"/>
    /// live, so a tier change takes effect on the same frame with no broadcast
    /// and no chance of an agent being missed.
    /// </summary>
    [DisallowMultipleComponent]
    public class ObstacleManager : MonoBehaviour
    {
        public static ObstacleManager Instance { get; private set; }

        [Header("Control")]
        [Tooltip("Turn off to disable every obstacle in the level.")]
        [SerializeField] private bool obstaclesEnabled = true;

        [Header("Live state (read-only)")]
        [SerializeField] private int neutrophilCount;
        [SerializeField] private int monocyteCount;
        [SerializeField] private float currentSpeedMultiplier = 1f;

        private readonly List<ObstacleAgent> _agents = new List<ObstacleAgent>();

        public IReadOnlyList<ObstacleAgent> Agents => _agents;
        public int AgentCount => _agents.Count;

        /// <summary>Multiplier the agents are currently moving at. Mirrored here for the Inspector.</summary>
        public float CurrentSpeedMultiplier => currentSpeedMultiplier;

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            Rescan();
            SetObstaclesEnabled(obstaclesEnabled);
        }

        private void Update()
        {
            currentSpeedMultiplier = DDAManager.Instance != null
                ? DDAManager.Instance.ObstacleSpeedMultiplier
                : 1f;
        }

        /// <summary>
        /// Rebuilds the registry from whatever is in the scene.
        ///
        /// Leukemic blasts are deliberately excluded. They reuse ObstacleAgent
        /// for their chase behaviour, so a naive search would count them as
        /// neutrophils and let SetObstaclesEnabled switch off the hostiles the
        /// HostileSpawnDirector owns. "Obstacle" here means the body's healthy
        /// immune cells only.
        /// </summary>
        public void Rescan()
        {
            _agents.Clear();

            foreach (ObstacleAgent agent in FindObjectsByType<ObstacleAgent>(FindObjectsInactive.Include))
            {
                if (agent.GetComponent<LeukemicBlastAgent>() != null) continue;
                _agents.Add(agent);
            }

            neutrophilCount = 0;
            monocyteCount = 0;

            foreach (ObstacleAgent agent in _agents)
            {
                if (agent.Kind == ObstacleKind.Neutrophil) neutrophilCount++;
                else monocyteCount++;
            }
        }

        /// <summary>Enables or disables every registered obstacle.</summary>
        public void SetObstaclesEnabled(bool enabled)
        {
            obstaclesEnabled = enabled;

            foreach (ObstacleAgent agent in _agents)
            {
                if (agent != null) agent.gameObject.SetActive(enabled);
            }
        }

        /// <summary>Forces every agent to discard its route and path again.</summary>
        public void RepathAll()
        {
            foreach (ObstacleAgent agent in _agents)
            {
                if (agent == null) continue;

                var pathfinder = agent.GetComponent<PathfindingAgent>();
                if (pathfinder != null) pathfinder.RequestPath();
            }
        }

        /// <summary>Total stuck recoveries across all agents. A rising number means the grid needs tuning.</summary>
        public int TotalStuckRecoveries()
        {
            int total = 0;

            foreach (ObstacleAgent agent in _agents)
            {
                if (agent == null) continue;

                var pathfinder = agent.GetComponent<PathfindingAgent>();
                if (pathfinder != null) total += pathfinder.StuckRecoveries;
            }

            return total;
        }
    }
}
