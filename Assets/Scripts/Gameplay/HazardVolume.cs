using Cardio.Core;
using Cardio.DDA;
using Cardio.Player;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// A static damaging region - the Phase 1 stand-in for the biological
    /// hazards that arrive in Phase 5 (neutrophils, monocytes, fatty plaque).
    ///
    /// It exists now purely so the Blood Count system has a real, testable
    /// damage source: walk in, watch the bar drop, reach zero, see the failure
    /// screen. That verifies the whole health -> GameManager -> UI chain before
    /// any AI code is written.
    ///
    /// Damage per tick is a serialized field because Phase 4's DifficultySettings
    /// will scale it.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class HazardVolume : MonoBehaviour
    {
        [Header("Damage")]
        [SerializeField, Range(1, 50)] private int damagePerTick = 10;
        [Tooltip("Seconds between damage ticks while the player stays inside.")]
        [SerializeField, Range(0.1f, 5f)] private float tickInterval = 1f;
        [Tooltip("Apply one tick the moment the player enters, before the interval starts.")]
        [SerializeField] private bool damageOnEnter = true;

        private float _nextTickTime;

        private void Reset()
        {
            GetComponent<Collider>().isTrigger = true;
        }

        /// <summary>
        /// Damage after the current difficulty tier's multiplier.
        ///
        /// Read live rather than cached, so a tier change mid-level takes effect
        /// immediately. This is the Phase 4 "reduce environmental pressure"
        /// effect from PSM1 section 12, and until the A* obstacles arrive in
        /// Phase 5 it is the DDA's only lever on the environment.
        /// </summary>
        private int EffectiveDamage()
        {
            float multiplier = DDAManager.Instance != null ? DDAManager.Instance.HazardDamageMultiplier : 1f;
            return Mathf.Max(1, Mathf.RoundToInt(damagePerTick * multiplier));
        }

        /// <summary>
        /// Hazards only bite while the player can actually do something about it.
        ///
        /// Found in the first playtest: standing on a plaque and opening a puzzle
        /// kept the ticks coming at 14 a second on Hard, while the character was
        /// frozen and the panel covered the view. Roughly seven seconds of answering
        /// a question emptied a full Blood Count with no way to react.
        /// </summary>
        private static bool GameplayActive =>
            GameManager.Instance == null || GameManager.Instance.IsGameplayActive;

        private void OnTriggerEnter(Collider other)
        {
            if (!damageOnEnter || !GameplayActive) return;

            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null) return;

            health.TakeDamage(EffectiveDamage());
            _nextTickTime = Time.time + tickInterval;
        }

        private void OnTriggerStay(Collider other)
        {
            if (!GameplayActive) return;
            if (Time.time < _nextTickTime) return;

            PlayerHealth health = other.GetComponentInParent<PlayerHealth>();
            if (health == null) return;

            health.TakeDamage(EffectiveDamage());
            _nextTickTime = Time.time + tickInterval;
        }

        private void OnDrawGizmos()
        {
            // Visible in the Scene view so hazard placement is obvious while editing.
            var col = GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = new Color(1f, 0.4f, 0.1f, 0.25f);
            Bounds b = col.bounds;
            Gizmos.DrawCube(b.center, b.size);
        }
    }
}
