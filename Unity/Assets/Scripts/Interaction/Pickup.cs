using UnityEngine;

namespace Blink.Interaction
{
    /// <summary>A small object the player can take into their <see cref="Inventory"/>.</summary>
    public sealed class Pickup : MonoBehaviour, IInteractable
    {
        public string itemId = "candle";
        public string displayName = "Candle";

        public string Prompt(Interactor who) => "Pick up " + displayName;
        public bool CanInteract(Interactor who) => who.Inventory != null;

        public void Interact(Interactor who)
        {
            who.Inventory.Add(itemId, displayName);
            gameObject.SetActive(false); // not destroyed: dying puts it back (WakeUpSequence)
        }
    }
}
