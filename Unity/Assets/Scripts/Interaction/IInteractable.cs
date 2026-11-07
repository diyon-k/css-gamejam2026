namespace Blink.Interaction
{
    /// <summary>
    /// Something the <see cref="Interactor"/> can look at and use with Interact (E / gamepad North).
    /// Put it on the object or any parent of its collider.
    /// </summary>
    public interface IInteractable
    {
        /// <summary>Text shown under the crosshair, e.g. "Open wardrobe". Null or empty shows nothing.</summary>
        string Prompt(Interactor who);
        /// <summary>False shows the prompt without the key hint (e.g. "The cupboard is locked").</summary>
        bool CanInteract(Interactor who);
        void Interact(Interactor who);
    }
}
