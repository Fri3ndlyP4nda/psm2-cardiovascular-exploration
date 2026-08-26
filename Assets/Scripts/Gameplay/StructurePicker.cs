using UnityEngine;
using UnityEngine.EventSystems;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Turns a screen position into the anatomical structure under it.
    ///
    /// This is what makes "drag the label onto the correct 3D structure" work
    /// as PSM1 specifies, rather than degrading into a list of buttons: the
    /// answer is given by pointing at the actual geometry in the chamber.
    /// </summary>
    public static class StructurePicker
    {
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[16];

        /// <summary>
        /// Returns the structure under <paramref name="screenPosition"/>, or null.
        ///
        /// Uses RaycastNonAlloc and walks every hit rather than taking the first,
        /// because the nearest collider may be an untagged prop (a plaque mound,
        /// the exit marker) sitting in front of the structure being asked about.
        /// </summary>
        public static AnatomyStructureTag Pick(Vector2 screenPosition, Camera camera, float maxDistance = 250f)
        {
            if (camera == null) camera = Camera.main;
            if (camera == null) return null;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            int count = Physics.RaycastNonAlloc(ray, HitBuffer, maxDistance, ~0, QueryTriggerInteraction.Ignore);
            if (count <= 0) return null;

            AnatomyStructureTag nearest = null;
            float nearestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.collider == null) continue;

                var tag = hit.collider.GetComponentInParent<AnatomyStructureTag>();
                if (tag == null) continue;

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    nearest = tag;
                }
            }

            return nearest;
        }

        /// <summary>
        /// True when the pointer is over an interactive UI element, so a click
        /// meant for a button is not also read as a structure pick.
        /// </summary>
        public static bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }
    }
}
