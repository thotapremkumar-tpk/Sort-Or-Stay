namespace SortOrStay
{
    /// Anything the player can point at and click (or press E on).
    public interface IInteractable
    {
        /// Text shown under the crosshair, e.g. "Select key 4". Empty = no prompt.
        string Prompt { get; }

        /// False while the object should ignore clicks (animation running, puzzle solved...).
        bool CanInteract { get; }

        void Interact();
    }
}
