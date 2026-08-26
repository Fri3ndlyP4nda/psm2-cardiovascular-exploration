using Cardio.Player;
using TMPro;
using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// A labelled anatomical landmark (chamber, valve or vessel).
    ///
    /// Phase 1 role: passive signage. Walking near a structure shows its name
    /// and a one line description, which is the "learn anatomy by exploring"
    /// part of the PSM1 concept working at its simplest.
    ///
    /// Phase 2 role: the same component becomes the drop target for the
    /// drag-and-drop labelling puzzle - <see cref="StructureId"/> is already the
    /// key the PuzzleManager will match answers against, and
    /// <see cref="SetHighlighted"/> is already the hook HintManager will call.
    /// </summary>
    public class AnatomyMarker : MonoBehaviour
    {
        [Header("Identity")]
        [Tooltip("Stable key used by puzzles and hints, e.g. left_ventricle, mitral_valve.")]
        [SerializeField] private string structureId = "left_ventricle";
        [SerializeField] private string displayName = "Left Ventricle";
        [TextArea(2, 4)]
        [SerializeField] private string description = "Pumps oxygenated blood into the aorta and around the body.";

        [Header("Proximity label")]
        [SerializeField] private TMP_Text worldLabel;
        [SerializeField, Range(1f, 30f)] private float revealRadius = 8f;
        [Tooltip("Keeps the label facing the camera.")]
        [SerializeField] private bool billboard = true;

        [Header("Highlight")]
        [Tooltip("Renderers tinted when this structure is highlighted by a hint.")]
        [SerializeField] private Renderer[] highlightRenderers;

        [Tooltip("Colour used when a hint points at this structure.")]
        [SerializeField] private Color highlightColor = new Color(1f, 0.92f, 0.35f);

        [Tooltip("Colour used when the pointer is simply over this structure during a puzzle.")]
        [SerializeField] private Color hoverColor = new Color(0.45f, 0.75f, 0.95f);

        private Transform _player;
        private Camera _camera;
        private MaterialPropertyBlock _block;
        private bool _highlighted;
        private bool _hovered;

        public string StructureId => structureId;
        public string DisplayName => displayName;
        public string Description => description;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (worldLabel != null) worldLabel.text = displayName;
        }

        private void Update()
        {
            if (worldLabel == null) return;

            if (_player == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc == null) return;
                _player = pc.transform;
            }

            float sqrDistance = (_player.position - transform.position).sqrMagnitude;
            bool near = sqrDistance <= revealRadius * revealRadius;

            if (worldLabel.gameObject.activeSelf != near) worldLabel.gameObject.SetActive(near);
            if (!near) return;

            worldLabel.text = $"{displayName}\n<size=60%>{description}</size>";

            if (billboard)
            {
                if (_camera == null) _camera = Camera.main;
                if (_camera != null)
                {
                    // Face the camera without inheriting its roll or pitch.
                    Vector3 toCamera = worldLabel.transform.position - _camera.transform.position;
                    toCamera.y = 0f;
                    if (toCamera.sqrMagnitude > 0.001f) worldLabel.transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
                }
            }
        }

        /// <summary>
        /// Tints the structure as the answer to a hint. Called by HintManager
        /// when the DDA decides the player needs visual assistance.
        /// </summary>
        public void SetHighlighted(bool highlighted)
        {
            if (_highlighted == highlighted) return;

            _highlighted = highlighted;
            RefreshTint();
        }

        /// <summary>
        /// Tints the structure because the pointer is over it during a puzzle.
        ///
        /// Kept on its own channel rather than reusing SetHighlighted: a hint
        /// and a hover can be active at once, and a single boolean would let the
        /// pointer moving away silently erase the hint that is showing the
        /// answer. A hint always wins the colour.
        /// </summary>
        public void SetHovered(bool hovered)
        {
            if (_hovered == hovered) return;

            _hovered = hovered;
            RefreshTint();
        }

        /// <summary>True while a hint is pointing at this structure.</summary>
        public bool IsHighlighted => _highlighted;

        /// <summary>True while the pointer is over this structure.</summary>
        public bool IsHovered => _hovered;

        /// <summary>
        /// Applies whichever tint currently wins. Uses a MaterialPropertyBlock
        /// so no material instances are created at runtime.
        /// </summary>
        private void RefreshTint()
        {
            if (highlightRenderers == null) return;

            Color tint = _highlighted ? highlightColor
                       : _hovered ? hoverColor
                       : Color.black;

            foreach (Renderer r in highlightRenderers)
            {
                if (r == null) continue;

                r.GetPropertyBlock(_block);
                _block.SetColor("_EmissionColor", tint);
                r.SetPropertyBlock(_block);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.9f, 0.3f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, revealRadius);
        }
    }
}
