using Cardio.Core;
using Cardio.Data;
using Cardio.Player;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// One per level scene. Owns everything that is true for the whole level:
    /// where the player starts, which level id this is, and what happens when
    /// the exit is reached.
    ///
    /// Keeping this in the scene (rather than in GameManager) is what allows
    /// each level to differ without adding branches to a shared class, and it
    /// gives Phase 2 a natural place to hook the PuzzleManager and
    /// ObjectiveManager in.
    /// </summary>
    public class LevelController : MonoBehaviour
    {
        [Header("Identity")]
        [SerializeField] private LevelId levelId = LevelId.Level1_LeftVentricle;

        [Header("Player")]
        [Tooltip("Where Bloo.D. Clot starts. Falls back to this object's transform.")]
        [SerializeField] private Transform spawnPoint;
        [Tooltip("Spawned only if no PlayerController is already present in the scene.")]
        [SerializeField] private GameObject playerPrefab;

        [Header("Exit rules")]
        [Tooltip("When true the level exit only completes the level once every puzzle objective is done.")]
        [SerializeField] private bool exitRequiresObjectives = true;

        private PlayerController _player;
        private PlayerHealth _playerHealth;

        public LevelId LevelId => levelId;

        private void Start()
        {
            SettingsPanel.ApplySavedSettings();

            EnsurePlayer();
            PlacePlayerAtSpawn();
            BindHud();

            // Objectives are owned by ObjectiveManager from Phase 2 onwards.
            // It draws the clipboard itself in its own Start().
            if (ObjectiveManager.Instance == null)
            {
                Debug.LogWarning("[LevelController] No ObjectiveManager in the scene - the objective board will stay empty.");
            }

            GameManager.Instance?.NotifyLevelStarted(levelId);
        }

        private void OnDestroy()
        {
            if (_playerHealth != null) _playerHealth.Died -= OnPlayerDied;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsurePlayer()
        {
            _player = FindAnyObjectByType<PlayerController>();

            if (_player == null && playerPrefab != null)
            {
                Vector3 pos = spawnPoint != null ? spawnPoint.position : transform.position;
                Quaternion rot = spawnPoint != null ? spawnPoint.rotation : Quaternion.identity;
                _player = Instantiate(playerPrefab, pos, rot).GetComponent<PlayerController>();
            }

            if (_player == null)
            {
                Debug.LogError("[LevelController] No player in the scene and no player prefab assigned.");
                return;
            }

            _playerHealth = _player.GetComponent<PlayerHealth>();
            if (_playerHealth != null)
            {
                _playerHealth.ResetHealth();
                _playerHealth.Died += OnPlayerDied;
            }
        }

        private void PlacePlayerAtSpawn()
        {
            if (_player == null || spawnPoint == null) return;
            _player.Teleport(spawnPoint.position, spawnPoint.rotation);

            var rig = FindAnyObjectByType<OrbitCameraRig>();
            if (rig != null) rig.SetTarget(_player.transform);
        }

        private void BindHud()
        {
            if (GameplayHUD.Instance != null && _playerHealth != null)
            {
                GameplayHUD.Instance.BindHealth(_playerHealth);
            }
        }

        // ------------------------------------------------------------------
        // Level flow
        // ------------------------------------------------------------------

        /// <summary>
        /// True when the player is allowed to finish the level. With
        /// <see cref="exitRequiresObjectives"/> set, every puzzle objective must
        /// be complete first - that is what stops Level 1 being finished by
        /// walking past the anatomy without learning any of it.
        /// </summary>
        public bool CanCompleteLevel()
        {
            if (!exitRequiresObjectives) return true;
            if (ObjectiveManager.Instance == null) return true;

            return ObjectiveManager.Instance.AllNonExitObjectivesComplete();
        }

        /// <summary>Message shown at the exit when objectives are still outstanding.</summary>
        public string BlockedExitMessage()
        {
            ObjectiveManager objectives = ObjectiveManager.Instance;
            if (objectives == null) return "The exit is closed.";

            int remaining = 0;
            foreach (LevelObjective o in objectives.Objectives)
            {
                if (o.Kind != ObjectiveKind.ReachExit && !o.Completed) remaining++;
            }

            return remaining == 1
                ? "One objective still outstanding - check the clipboard."
                : $"{remaining} objectives still outstanding - check the clipboard.";
        }

        /// <summary>Called by <see cref="LevelExitTrigger"/> when the player reaches the exit.</summary>
        public void CompleteLevel()
        {
            var gm = GameManager.Instance;
            if (gm == null || gm.State != GameState.Playing) return;

            ObjectiveManager.Instance?.CompleteExitObjective();
            gm.NotifyLevelCompleted(levelId);
        }

        private void OnPlayerDied()
        {
            // GameManager already switched to GameOver; just stop the body moving.
            if (_player != null) _player.InputEnabled = false;
        }
    }
}
