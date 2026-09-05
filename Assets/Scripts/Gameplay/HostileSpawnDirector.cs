using System.Collections.Generic;
using Cardio.AI;
using Cardio.Core;
using Cardio.Data;
using Cardio.DDA;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Turns wrong answers into leukemic blast cells, and killing them back
    /// into hints.
    ///
    ///     wrong answer  ->  spawn a blast tagged with that PuzzleId
    ///     kill it       ->  that question's hint is revealed and banked
    ///     all dead      ->  every blast returns after 30 seconds
    ///
    /// ONE BLAST PER QUESTION. Repeated wrong answers on a question that already
    /// has a blast alive spawn nothing. Stacking would concentrate hostiles on
    /// exactly the player who is already struggling, which is the opposite of
    /// what the DDA spends its time doing - and three blasts carrying the same
    /// hint would have nothing to give after the first.
    ///
    /// This is scene-level rather than persistent: it instantiates objects into
    /// the level, and its registry dies with the level, so it belongs to it.
    /// </summary>
    public class HostileSpawnDirector : MonoBehaviour
    {
        public static HostileSpawnDirector Instance { get; private set; }

        [Header("Prefab")]
        [SerializeField] private GameObject blastPrefab;

        [Header("Spawn placement")]
        [Tooltip("How far from the station the blast appears.")]
        [SerializeField, Range(2f, 15f)] private float spawnDistance = 5f;

        [Tooltip("Fallback distance from the player when the station cannot be found.")]
        [SerializeField, Range(2f, 15f)] private float fallbackDistance = 6f;

        [Header("Respawn")]
        [Tooltip("Seconds after the last blast dies before they all return.")]
        [SerializeField, Range(1f, 300f)] private float respawnDelay = 30f;

        [Header("Live state (read-only)")]
        [SerializeField] private int aliveCount;
        [SerializeField] private int spawnedThisLevel;
        [SerializeField] private int killedThisLevel;
        [SerializeField] private float respawnCountdown = -1f;

        /// <summary>One blast per question id. The whole anti-stacking rule lives here.</summary>
        private readonly Dictionary<string, LeukemicBlastAgent> _blasts = new Dictionary<string, LeukemicBlastAgent>();

        private PuzzleManager _puzzleManager;

        public int AliveCount => aliveCount;
        public int SpawnedThisLevel => spawnedThisLevel;
        public int KilledThisLevel => killedThisLevel;

        /// <summary>Seconds until respawn, or -1 when no respawn is pending.</summary>
        public float RespawnCountdown => respawnCountdown;

        /// <summary>The blast currently carrying a question, or null.</summary>
        public LeukemicBlastAgent BlastFor(string puzzleId)
        {
            return _blasts.TryGetValue(puzzleId, out LeukemicBlastAgent blast) ? blast : null;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Detach();
        }

        private void Start()
        {
            Attach();
        }

        private void Update()
        {
            if (respawnCountdown < 0f) return;

            respawnCountdown -= Time.deltaTime;
            if (respawnCountdown > 0f) return;

            respawnCountdown = -1f;
            RespawnAll();
        }

        // ------------------------------------------------------------------
        // Wiring
        // ------------------------------------------------------------------

        private void Attach()
        {
            _puzzleManager = PuzzleManager.Instance;
            if (_puzzleManager == null)
            {
                Debug.LogWarning("[HostileSpawnDirector] No PuzzleManager - wrong answers cannot spawn hostiles.");
                return;
            }

            _puzzleManager.AttemptSubmitted += OnAttemptSubmitted;
        }

        private void Detach()
        {
            if (_puzzleManager == null) return;

            _puzzleManager.AttemptSubmitted -= OnAttemptSubmitted;
            _puzzleManager = null;
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        private void OnAttemptSubmitted(bool correct, int attemptNumber)
        {
            if (correct) return;
            if (_puzzleManager.Current == null) return;

            TrySpawnFor(_puzzleManager.Current.PuzzleId);
        }

        /// <summary>
        /// Spawns a blast for a question, unless one is already alive for it or
        /// the difficulty tier has switched spawning off.
        /// </summary>
        public LeukemicBlastAgent TrySpawnFor(string puzzleId)
        {
            if (string.IsNullOrEmpty(puzzleId) || blastPrefab == null) return null;

            if (_puzzleManager != null && !_puzzleManager.HostileSpawningEnabled)
            {
                // Hard tier: no automatic help, and no combat route to it either.
                return null;
            }

            if (_blasts.TryGetValue(puzzleId, out LeukemicBlastAgent existing) && existing != null)
            {
                if (existing.IsAlive)
                {
                    Debug.Log($"[HostileSpawnDirector] '{puzzleId}' already has a blast alive - not stacking another.");
                    return existing;
                }

                // This question's blast was killed and is sitting inactive. Revive it
                // rather than leaving it behind and instantiating a replacement.
                //
                // The old code only returned early for a *live* blast, so a second
                // wrong answer after a kill overwrote the dictionary entry and
                // orphaned the previous object - still parented, still subscribed to
                // its own Died event, never destroyed and never revivable, because
                // RespawnAll only walks the dictionary. That is a real sequence:
                // answer wrong, kill the blast for its hint, answer wrong again.
                //
                // Respawn() is exactly this path - it is what the delayed respawn
                // uses - so reusing it also keeps one blast per question true of the
                // objects, not just of the dictionary.
                Vector3 returnTo = ChooseSpawnPosition(puzzleId);
                existing.Initialise(puzzleId, returnTo);
                existing.Respawn();

                spawnedThisLevel++;
                aliveCount++;
                PerformanceTracker.Instance?.RecordHostileSpawned();

                Debug.Log($"[HostileSpawnDirector] Revived the blast for '{puzzleId}' at {returnTo}.");
                return existing;
            }

            Vector3 position = ChooseSpawnPosition(puzzleId);

            var instance = Instantiate(blastPrefab, position, Quaternion.identity, transform);
            instance.name = $"LeukemicBlast_{puzzleId}";

            var blast = instance.GetComponent<LeukemicBlastAgent>();
            if (blast == null)
            {
                Debug.LogError("[HostileSpawnDirector] Blast prefab has no LeukemicBlastAgent.");
                Destroy(instance);
                return null;
            }

            blast.Initialise(puzzleId, position);
            blast.Health.Died += OnBlastDied;

            _blasts[puzzleId] = blast;

            spawnedThisLevel++;
            aliveCount++;
            PerformanceTracker.Instance?.RecordHostileSpawned();

            Debug.Log($"[HostileSpawnDirector] Spawned a blast for '{puzzleId}' at {position}.");
            return blast;
        }

        /// <summary>
        /// Places the blast near the station that asked the question, offset away
        /// from the player and snapped onto the navigation grid so it can never
        /// appear inside geometry or somewhere it cannot path out of.
        /// </summary>
        private Vector3 ChooseSpawnPosition(string puzzleId)
        {
            Transform player = FindPlayer();
            Vector3 anchor = player != null ? player.position : transform.position;
            float distance = fallbackDistance;

            foreach (PuzzleStation station in FindObjectsByType<PuzzleStation>(FindObjectsInactive.Exclude))
            {
                if (station.PuzzleId != puzzleId) continue;

                anchor = station.transform.position;
                distance = spawnDistance;
                break;
            }

            // Push away from the player so the blast never materialises on top
            // of them, but stay near the station so it is findable.
            Vector3 away = player != null ? (anchor - player.position) : Vector3.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = Vector3.forward;

            Vector3 candidate = anchor + away.normalized * distance;

            AStarPathfindingManager grid = AStarPathfindingManager.Instance;
            if (grid == null || !grid.IsBuilt) return candidate + Vector3.up;

            PathNode node = grid.NearestWalkableNode(candidate);
            return node != null ? node.WorldPosition + Vector3.up : candidate + Vector3.up;
        }

        // ------------------------------------------------------------------
        // Death and reward
        // ------------------------------------------------------------------

        private void OnBlastDied(NpcHealth health)
        {
            var blast = health.GetComponent<LeukemicBlastAgent>();
            if (blast == null) return;

            killedThisLevel++;
            aliveCount = Mathf.Max(0, aliveCount - 1);

            bool everythingAnswered = ObjectiveManager.Instance != null
                                      && ObjectiveManager.Instance.AllNonExitObjectivesComplete();

            PerformanceTracker.Instance?.RecordHostileKilled(everythingAnswered);

            if (everythingAnswered)
            {
                // Nothing meaningful left to hint about, so the kill is worth
                // points instead.
                GameplayHUD.Instance?.ShowHint("Malignancy cleared - bonus awarded.");
            }
            else if (_puzzleManager != null)
            {
                _puzzleManager.DeliverEarnedHint(blast.PuzzleId);
            }

            FindPlayerAttack()?.NotifyKill();

            blast.gameObject.SetActive(false);

            if (aliveCount <= 0 && spawnedThisLevel > 0) respawnCountdown = respawnDelay;
        }

        // ------------------------------------------------------------------
        // Respawn
        // ------------------------------------------------------------------

        /// <summary>Brings every blast back at its original spawn point.</summary>
        public void RespawnAll()
        {
            int revived = 0;

            foreach (KeyValuePair<string, LeukemicBlastAgent> pair in _blasts)
            {
                LeukemicBlastAgent blast = pair.Value;
                if (blast == null || blast.IsAlive) continue;

                blast.Respawn();
                revived++;
            }

            aliveCount += revived;
            respawnCountdown = -1f;

            if (revived <= 0) return;

            Debug.Log($"[HostileSpawnDirector] {revived} blast(s) returned after {respawnDelay:0}s.");
            GameplayHUD.Instance?.ShowHint("The malignancy is spreading again.");
        }

        /// <summary>Forces the pending respawn immediately. Used by tests.</summary>
        public void ForceRespawnNow()
        {
            respawnCountdown = -1f;
            RespawnAll();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Transform FindPlayer()
        {
            var player = FindAnyObjectByType<Cardio.Player.PlayerController>();
            return player != null ? player.transform : null;
        }

        private static Cardio.Player.PlayerAttack FindPlayerAttack()
        {
            return FindAnyObjectByType<Cardio.Player.PlayerAttack>();
        }
    }
}
