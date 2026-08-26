using System;
using UnityEngine;

namespace Cardio.AI
{
    /// <summary>
    /// Health for a hostile NPC.
    ///
    /// Deliberately separate from <see cref="Cardio.Player.PlayerHealth"/>
    /// rather than shared: the player's version drives Blood Count UI and calls
    /// GameManager.NotifyPlayerDied, none of which applies here. What they share
    /// is the shape - integer health, a damage entry point, and events that let
    /// everything else react without this class knowing about it.
    ///
    /// Only leukemic blast cells carry this. Neutrophils and monocytes have no
    /// NpcHealth by design, which is what makes them unkillable hazards rather
    /// than enemies.
    /// </summary>
    [DisallowMultipleComponent]
    public class NpcHealth : MonoBehaviour
    {
        [SerializeField, Range(1, 500)] private int maxHealth = 100;

        [Tooltip("Seconds of immunity after a hit, so one swing cannot register twice.")]
        [SerializeField, Range(0f, 1f)] private float hitCooldown = 0.15f;

        private float _lastHitTime = -999f;

        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        /// <summary>(current, max) whenever health changes.</summary>
        public event Action<int, int> HealthChanged;

        /// <summary>Raised once when health reaches zero.</summary>
        public event Action<NpcHealth> Died;

        private void Awake()
        {
            CurrentHealth = maxHealth;
        }

        /// <summary>Applies damage. Returns true if this blow landed.</summary>
        public bool TakeDamage(int amount)
        {
            if (amount <= 0 || !IsAlive) return false;
            if (Time.time - _lastHitTime < hitCooldown) return false;

            _lastHitTime = Time.time;
            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            HealthChanged?.Invoke(CurrentHealth, maxHealth);

            if (CurrentHealth <= 0) Died?.Invoke(this);

            return true;
        }

        /// <summary>Restores full health. Used when a cleared level respawns its hostiles.</summary>
        public void Revive()
        {
            CurrentHealth = maxHealth;
            _lastHitTime = -999f;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
        }
    }
}
