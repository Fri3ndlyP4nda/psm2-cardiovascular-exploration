using Cardio.Core;
using Cardio.Gameplay;
using Cardio.UI;
using UnityEngine;

namespace Cardio.Player
{
    /// <summary>
    /// Finds the nearest usable <see cref="IInteractable"/> around the player,
    /// shows its prompt on the HUD, and activates it on the interact key.
    ///
    /// Scanning is throttled rather than run every frame: the search is only
    /// meaningful at walking speed, and this keeps a physics query off the
    /// per-frame budget for the 60 FPS target.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerInteraction : MonoBehaviour
    {
        [Header("Detection")]
        [SerializeField, Range(1f, 10f)] private float interactionRadius = 3.5f;
        [SerializeField, Range(0.02f, 0.5f)] private float scanInterval = 0.1f;
        [SerializeField] private LayerMask interactableMask = ~0;

        [Header("Prompt")]
        [SerializeField] private string keyName = "E";

        private readonly Collider[] _buffer = new Collider[24];
        private float _nextScanTime;
        private IInteractable _nearest;

        /// <summary>The interactable currently in range, or null.</summary>
        public IInteractable Nearest => _nearest;

        private void Update()
        {
            // Interaction is a gameplay action; suppress it in menus, while a
            // puzzle panel is open, and on the failure screen.
            bool canInteract = GameManager.Instance == null || GameManager.Instance.State == GameState.Playing;

            if (!canInteract)
            {
                if (_nearest != null) ClearNearest();
                return;
            }

            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + scanInterval;
                Scan();
            }

            if (_nearest != null && _nearest.CanInteract && PlayerInputReader.InteractPressed)
            {
                _nearest.Interact(gameObject);
            }
        }

        private void OnDisable()
        {
            ClearNearest();
        }

        private void Scan()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, interactionRadius, _buffer,
                                                      interactableMask, QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestSqr = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider col = _buffer[i];
                if (col == null) continue;

                var candidate = col.GetComponentInParent<IInteractable>();
                if (candidate == null || !candidate.CanInteract) continue;

                float sqr = (candidate.InteractionPoint - transform.position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            if (ReferenceEquals(best, _nearest)) return;

            _nearest = best;
            UpdatePrompt();
        }

        private void UpdatePrompt()
        {
            if (GameplayHUD.Instance == null) return;

            if (_nearest == null) GameplayHUD.Instance.ClearInteractionPrompt();
            else GameplayHUD.Instance.ShowInteractionPrompt($"[{keyName}]  {_nearest.InteractionPrompt}");
        }

        private void ClearNearest()
        {
            _nearest = null;
            GameplayHUD.Instance?.ClearInteractionPrompt();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.35f);
            Gizmos.DrawWireSphere(transform.position, interactionRadius);
        }
    }
}
