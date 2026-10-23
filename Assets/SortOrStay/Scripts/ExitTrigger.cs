using UnityEngine;

namespace SortOrStay
{
    /// Invisible box past the door. Walking into it ends the floor.
    [RequireComponent(typeof(Collider))]
    public class ExitTrigger : MonoBehaviour
    {
        public LevelManager manager;

        void OnTriggerEnter(Collider other)
        {
            if (manager == null) return;
            if (other.GetComponent<CharacterController>() != null) manager.ExitReached();
        }
    }
}
