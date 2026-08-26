using Cardio.AI;
using Cardio.Core;
using UnityEngine;

namespace Cardio.Player
{
    /// <summary>
    /// The player's oxygen burst - a short-range melee swing that damages
    /// leukemic blast cells.
    ///
    /// INPUT: reuses the primary click. That button is already read by PuzzleUI
    /// for picking structures, but only in GameState.Puzzle, while attacking
    /// only happens in GameState.Playing. The two states are mutually exclusive,
    /// so no new binding is needed and the control stays where a player expects
    /// it. The cursor is locked during play, so a click reads as a swing rather
    /// than a point.
    ///
    /// Only <see cref="NpcHealth"/> can be hurt, and only blast cells carry it -
    /// so neutrophils and monocytes are immune by construction, not by a tag
    /// check that could drift.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerAttack : MonoBehaviour
    {
        [Header("Swing")]
        [SerializeField, Range(1, 200)] private int damage = 34;
        [SerializeField, Range(0.5f, 6f)] private float range = 2.2f;
        [SerializeField, Range(0.2f, 3f)] private float radius = 0.9f;
        [SerializeField, Range(0.1f, 3f)] private float cooldown = 0.5f;

        [Header("Feedback")]
        [Tooltip("Optional visual that pulses on each swing. Purely cosmetic.")]
        [SerializeField] private Transform swingVisual;

        private readonly Collider[] _hits = new Collider[16];
        private float _nextSwingTime;

        /// <summary>Seconds until the next swing is allowed. Zero when ready.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _nextSwingTime - Time.time);

        /// <summary>True when a swing would be accepted right now.</summary>
        public bool CanSwing => Time.time >= _nextSwingTime;

        /// <summary>Total blast cells this player has destroyed. Read by the HUD and tests.</summary>
        public int Kills { get; private set; }

        private void Update()
        {
            if (GameManager.Instance != null && GameManager.Instance.State != GameState.Playing) return;
            if (!PlayerInputReader.PrimaryClickPressed) return;

            TrySwing();
        }

        /// <summary>Performs one swing if the cooldown allows. Returns how many blasts were hit.</summary>
        public int TrySwing()
        {
            if (!CanSwing) return 0;

            _nextSwingTime = Time.time + cooldown;

            // Overlap a sphere just in front of the body. More forgiving than a
            // cast, which matters because the target is actively circling.
            Vector3 centre = transform.position + Vector3.up * 0.8f + transform.forward * (range * 0.6f);

            int count = Physics.OverlapSphereNonAlloc(centre, radius, _hits, ~0, QueryTriggerInteraction.Ignore);
            int landed = 0;

            for (int i = 0; i < count; i++)
            {
                if (_hits[i] == null) continue;

                var health = _hits[i].GetComponentInParent<NpcHealth>();
                if (health == null || !health.IsAlive) continue;

                if (health.TakeDamage(damage)) landed++;
            }

            if (swingVisual != null) swingVisual.localScale = Vector3.one * 1.25f;

            return landed;
        }

        /// <summary>Called by the spawn director when one of this player's swings finishes a blast.</summary>
        public void NotifyKill() => Kills++;

        private void LateUpdate()
        {
            // Ease the cosmetic pulse back down.
            if (swingVisual == null) return;

            swingVisual.localScale = Vector3.Lerp(swingVisual.localScale, Vector3.one, Time.deltaTime * 8f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.8f + transform.forward * (range * 0.6f), radius);
        }
    }
}
