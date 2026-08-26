using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Marks a piece of geometry as belonging to a named anatomical structure.
    ///
    /// Why this exists separately from <see cref="AnatomyMarker"/>: the marker
    /// is a single labelled point floating in the chamber, while the structure
    /// it names is made of many blocks that are siblings of it, not children.
    /// A raycast hits a block, so the block itself has to carry the id. The
    /// scene generator attaches this to exactly the renderers that a structure
    /// owns, which is also the set a hint highlights.
    /// </summary>
    public class AnatomyStructureTag : MonoBehaviour
    {
        [Tooltip("Must match the PuzzleData.TargetStructureId used by puzzles about this structure.")]
        [SerializeField] private string structureId = "";

        [Tooltip("The label/marker this geometry belongs to. Used for highlighting.")]
        [SerializeField] private AnatomyMarker marker;

        public string StructureId => structureId;
        public AnatomyMarker Marker => marker;

        /// <summary>Display name, falling back to the id when no marker is linked.</summary>
        public string DisplayName => marker != null ? marker.DisplayName : structureId.Replace('_', ' ');

        /// <summary>Forwards a highlight request to the owning marker.</summary>
        public void SetHighlighted(bool highlighted)
        {
            if (marker != null) marker.SetHighlighted(highlighted);
        }
    }
}
