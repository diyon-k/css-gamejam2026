using Blink.Interaction;
using UnityEngine;

namespace Blink.World
{
    /// <summary>
    /// On a piece of a <see cref="FrontDoor"/> the player can look at (a leaf, a lock plate): passes the look and the
    /// press on to the door, so the whole door answers wherever you aim at it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrontDoorPart : MonoBehaviour, IInteractable
    {
        public FrontDoor door;

        public string Prompt(Interactor who) => door != null ? door.Prompt(who) : null;
        public bool CanInteract(Interactor who) => door != null && door.CanInteract(who);
        public void Interact(Interactor who) { if (door != null) door.Interact(who); }
    }
}
