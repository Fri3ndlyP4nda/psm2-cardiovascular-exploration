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
        [Tooltip("Optional burst shown for each swing. Purely cosmetic; the attack works without it.")]
        [SerializeField] private Transform swingVisual;

        [Tooltip("How long the swing burst stays on screen.")]
        [SerializeField, Range(0.05f, 1f)] private float burstSeconds = 0.28f;

        private readonly Collider[] _hits = new Collider[16];
        private float _nextSwingTime;

        // Both names are set on the property block: the Built-in pipeline's
        // Standard shader calls it _Color, URP and HDRP call it _BaseColor.
        // Setting a property the shader does not have is harmless.
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Renderer _burstRenderer;
        private MaterialPropertyBlock _burstBlock;
        private Vector3 _burstBaseScale = Vector3.one;
        private Color _burstBaseColor = Color.white;

        /// <summary>When the current burst began, or -1 when none is playing.</summary>
        private float _burstStartedAt = -1f;

        private void Awake()
        {
            if (swingVisual == null) return;

            _burstBaseScale = swingVisual.localScale;
            _burstRenderer = swingVisual.GetComponent<Renderer>();
            _burstBlock = new MaterialPropertyBlock();

            if (_burstRenderer == null) return;

            if (_burstRenderer.sharedMaterial != null) _burstBaseColor = _burstRenderer.sharedMaterial.color;

            // Hidden until a swing. The burst is an event, not scenery - leaving it
            // on would park a translucent ball in front of the player forever.
            _burstRenderer.enabled = false;
        }

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

            StartBurst();

            return landed;
        }

        /// <summary>Called by the spawn director when one of this player's swings finishes a blast.</summary>
        public void NotifyKill() => Kills++;

        /// <summary>Shows the burst and restarts its timeline.</summary>
        private void StartBurst()
        {
            if (swingVisual == null) return;

            _burstStartedAt = Time.time;
            if (_burstRenderer != null) _burstRenderer.enabled = true;
        }

        /// <summary>
        /// Drives the burst: expand and fade, then hide.
        ///
        /// This replaced a scale pulse that eased back to 1 and left the object
        /// visible in between, which only works if the visual is meant to be part
        /// of the character. Expanding while fading reads as energy leaving the
        /// cell, and hiding at the end keeps the player's silhouette clean.
        ///
        /// The alpha goes through a MaterialPropertyBlock rather than the material,
        /// because touching renderer.material would instantiate a per-instance copy
        /// every time and leak one material per player.
        /// </summary>
        private void LateUpdate()
        {
            if (swingVisual == null || _burstStartedAt < 0f) return;

            float t = burstSeconds <= 0f
                ? 1f
                : Mathf.Clamp01((Time.time - _burstStartedAt) / burstSeconds);

            swingVisual.localScale = _burstBaseScale * Mathf.Lerp(0.5f, 1.35f, t);

            if (_burstRenderer != null)
            {
                Color c = _burstBaseColor;
                c.a *= 1f - t;

                _burstRenderer.GetPropertyBlock(_burstBlock);
                _burstBlock.SetColor(ColorId, c);
                _burstBlock.SetColor(BaseColorId, c);
                _burstRenderer.SetPropertyBlock(_burstBlock);
            }

            if (t < 1f) return;

            _burstStartedAt = -1f;
            swingVisual.localScale = _burstBaseScale;
            if (_burstRenderer != null) _burstRenderer.enabled = false;
        }

        /// <summary>True while the swing burst is on screen. Used by tests.</summary>
        public bool BurstIsPlaying => _burstStartedAt >= 0f;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.4f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.8f + transform.forward * (range * 0.6f), radius);
        }
    }
}
