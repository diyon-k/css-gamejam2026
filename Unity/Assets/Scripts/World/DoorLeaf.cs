using Blink.Interaction;
using UnityEngine;

namespace Blink.World
{
    /// <summary>On a door leaf in the shell model: lets the Interactor's look ray (which hits the leaf's colliders) reach the <see cref="Door"/>.</summary>
    public sealed class DoorLeaf : MonoBehaviour, IInteractable
    {
        public Door door;
        public string Prompt(Interactor who) => door ? door.Prompt(who) : null;
        public bool CanInteract(Interactor who) => door != null; // a locked one rattles, so the hands still reach for it
        public void Interact(Interactor who) { if (door) door.Interact(who); }
    }
}
