using System;
using Cardio.Core;
using UnityEngine;

namespace Cardio.Player
{
    /// <summary>
    /// The "Blood Count" health system from the PSM1 design.
    ///
    /// Intentionally simple: it exists to give environmental mistakes a
    /// consequence, not to be a combat system. Damage sources call
    /// <see cref="TakeDamage"/>; everything else reacts to the events.
    ///
    /// Note on responsibility: this class does not show a failure screen or
    /// reload a scene. It raises <see cref="Died"/> and lets GameManager /
    /// LevelController decide, which keeps the failure flow in one place.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerHealth : MonoBehaviour
    {
        [Header("Blood Count")]
        [SerializeField, Range(10, 500)] private int maxBloodCount = 100;
        [Tooltip("Seconds of immunity after taking a hit. Prevents a single obstacle from draining the bar instantly.")]
        [SerializeField, Range(0f, 3f)] private float invulnerabilityDuration = 1.0f;

        [Header("Regeneration (optional)")]
        [Tooltip("Blood Count restored per second while undamaged. Set to 0 to disable.")]
        [SerializeField, Range(0f, 20f)] private float regenPerSecond = 0f;
        [SerializeField, Range(0f, 10f)] private float regenDelayAfterDamage = 4f;

        private float _regenBuffer;
        private float _lastDamageTime = -999f;

        public int MaxBloodCount => maxBloodCount;
        public int CurrentBloodCount { get; private set; }
        public bool IsAlive => CurrentBloodCount > 0;
        public bool IsInvulnerable => Time.time - _lastDamageTime < invulnerabilityDuration;

        /// <summary>Normalised health, 0..1. Drives the HUD bar fill.</summary>
        public float Normalised => maxBloodCount <= 0 ? 0f : (float)CurrentBloodCount / maxBloodCount;

        /// <summary>(current, max) whenever the value changes.</summary>
        public event Action<int, int> BloodCountChanged;

        /// <summary>Amount of damage actually applied. Used by the HUD for the hit flash.</summary>
        public event Action<int> Damaged;

        public event Action Died;

        private void Awake()
        {
            CurrentBloodCount = maxBloodCount;
        }

        private void Start()
        {
            // Fired in Start (not Awake) so UI that subscribes in its own Awake
            // still receives the initial value.
            BloodCountChanged?.Invoke(CurrentBloodCount, maxBloodCount);
        }

        private void Update()
        {
            if (regenPerSecond <= 0f || !IsAlive) return;
            if (Time.time - _lastDamageTime < regenDelayAfterDamage) return;
            if (CurrentBloodCount >= maxBloodCount) return;

            // Accumulate fractional regen so a slow rate still works with int health.
            _regenBuffer += regenPerSecond * Time.deltaTime;
            int whole = Mathf.FloorToInt(_regenBuffer);
            if (whole <= 0) return;

            _regenBuffer -= whole;
            Heal(whole);
        }

        /// <summary>Applies damage. Ignored while invulnerable or already dead.</summary>
        public void TakeDamage(int amount)
        {
            if (amount <= 0 || !IsAlive || IsInvulnerable) return;

            _lastDamageTime = Time.time;
            int applied = Mathf.Min(amount, CurrentBloodCount);
            CurrentBloodCount -= applied;

            Damaged?.Invoke(applied);
            BloodCountChanged?.Invoke(CurrentBloodCount, maxBloodCount);

            if (CurrentBloodCount <= 0)
            {
                Died?.Invoke();
                GameManager.Instance?.NotifyPlayerDied();
            }
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || !IsAlive) return;

            CurrentBloodCount = Mathf.Min(maxBloodCount, CurrentBloodCount + amount);
            BloodCountChanged?.Invoke(CurrentBloodCount, maxBloodCount);
        }

        /// <summary>Restores full Blood Count. Called when a level starts or restarts.</summary>
        public void ResetHealth()
        {
            CurrentBloodCount = maxBloodCount;
            _lastDamageTime = -999f;
            _regenBuffer = 0f;
            BloodCountChanged?.Invoke(CurrentBloodCount, maxBloodCount);
        }
    }
}
