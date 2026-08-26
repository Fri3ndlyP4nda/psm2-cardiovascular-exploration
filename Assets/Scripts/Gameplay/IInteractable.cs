using UnityEngine;

namespace Cardio.Gameplay
{
    /// <summary>
    /// Anything the player can activate with the interact key.
    ///
    /// Kept as an interface so <see cref="Cardio.Player.PlayerInteraction"/>
    /// never has to know about puzzles specifically - Phase 8 can add doors,
    /// collectables or valve controls without touching the player code.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Line shown on the HUD prompt, e.g. "Examine the mitral valve".</summary>
        string InteractionPrompt { get; }

        /// <summary>False when the interaction is unavailable (already solved, busy, locked).</summary>
        bool CanInteract { get; }

        /// <summary>World position the player must be near. Usually the transform position.</summary>
        Vector3 InteractionPoint { get; }

        void Interact(GameObject interactor);
    }
}
