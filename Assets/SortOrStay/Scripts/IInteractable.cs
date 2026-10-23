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

    /// Things the avatar handles with its hand: it walks this close (flat metres) before using them.
    /// Zero or less keeps the interactor's normal distances.
    public interface IHandReach
    {
        float ReachDistance { get; }
    }
}
