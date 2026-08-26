using Cardio.Player;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Trigger volume that finishes the level. Placed past the aortic valve in
    /// Level 1.
    ///
    /// From Phase 2 the exit is gated: <see cref="LevelController.CanCompleteLevel"/>
    /// decides whether the outstanding objectives allow it. Reaching a closed
    /// exit explains what is missing rather than silently doing nothing.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class LevelExitTrigger : MonoBehaviour
    {
        [SerializeField] private LevelController levelController;
        [Tooltip("Optional visual that spins to draw the eye. Purely cosmetic.")]
        [SerializeField] private Transform spinner;
        [SerializeField] private float spinSpeed = 45f;

        private bool _fired;

        private void Reset()
        {
            // Make the collider a trigger automatically when the component is added.
            GetComponent<Collider>().isTrigger = true;
        }

        private void Awake()
        {
            if (levelController == null) levelController = FindAnyObjectByType<LevelController>();
        }

        private void Update()
        {
            if (spinner != null) spinner.Rotate(Vector3.up, spinSpeed * Time.deltaTime, Space.World);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_fired) return;
            if (other.GetComponentInParent<PlayerController>() == null) return;
            if (levelController == null) return;

            if (!levelController.CanCompleteLevel())
            {
                // Not latched: the player can finish the objectives and come back.
                GameplayHUD.Instance?.ShowHint(levelController.BlockedExitMessage());
                return;
            }

            _fired = true;
            levelController.CompleteLevel();
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<PlayerController>() == null) return;
            GameplayHUD.Instance?.ClearHint();
        }
    }
}
