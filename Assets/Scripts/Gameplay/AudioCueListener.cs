using Cardio.Core;
using Cardio.Data;
using Cardio.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Turns gameplay events into sound cues.
    ///
    /// This exists as a separate listener rather than as calls scattered through
    /// PuzzleManager and PlayerHealth for two reasons. It keeps the one-way
    /// dependency rule - gameplay classes emit events and never learn that audio
    /// exists - and it keeps <see cref="AudioManager"/> in Core, which is not
    /// allowed to know about the Gameplay or Player layers. This class is in
    /// Gameplay, which may know both, so it is the correct place to join them.
    ///
    /// It is the same pattern <see cref="Cardio.DDA.PerformanceTracker"/> uses,
    /// including re-attaching on sceneLoaded because the objects it listens to
    /// are rebuilt with each level.
    /// </summary>
    [DisallowMultipleComponent]
    public class AudioCueListener : MonoBehaviour
    {
        private PuzzleManager _puzzleManager;
        private PlayerHealth _playerHealth;

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;

            var gm = GameManager.Instance;
            if (gm != null) gm.StateChanged += OnStateChanged;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;

            var gm = GameManager.Instance;
            if (gm != null) gm.StateChanged -= OnStateChanged;

            Detach();
        }

        private void Start() => Attach();

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Detach();
            Attach();
        }

        private void Attach()
        {
            _puzzleManager = PuzzleManager.Instance;
            if (_puzzleManager != null)
            {
                _puzzleManager.PuzzleAnswered += OnPuzzleAnswered;
                _puzzleManager.HintRequested += OnHintRequested;
            }

            _playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (_playerHealth != null) _playerHealth.Damaged += OnDamaged;
        }

        private void Detach()
        {
            if (_puzzleManager != null)
            {
                _puzzleManager.PuzzleAnswered -= OnPuzzleAnswered;
                _puzzleManager.HintRequested -= OnHintRequested;
                _puzzleManager = null;
            }

            if (_playerHealth != null)
            {
                _playerHealth.Damaged -= OnDamaged;
                _playerHealth = null;
            }
        }

        private static void OnPuzzleAnswered(PuzzleResult result)
            => AudioManager.PlayCue(result.Correct ? AudioCue.Correct : AudioCue.Wrong);

        private static void OnHintRequested(PuzzleData puzzle, HintSource source)
            => AudioManager.PlayCue(AudioCue.Hint);

        private static void OnDamaged(int amount) => AudioManager.PlayCue(AudioCue.Damage);

        private static void OnStateChanged(GameState state)
        {
            if (state == GameState.LevelComplete) AudioManager.PlayCue(AudioCue.LevelComplete);
        }
    }
}
