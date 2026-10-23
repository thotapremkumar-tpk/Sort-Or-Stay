using UnityEngine;

namespace SortOrStay
{
    /// A glowing gap on the library shelf. While the player holds a book, clicking a gap inserts it there.
    public class InsertSlot : MonoBehaviour, IInteractable
    {
        public InsertionPuzzle puzzle;
        public int index;
        public Renderer glow;

        static readonly Color BaseEmission = new Color(0.2f, 0.75f, 1f);
        Material mat;
        Color flashColor;
        float flashUntil;

        void Awake()
        {
            if (glow != null)
            {
                mat = glow.material;
                mat.EnableKeyword("_EMISSION");
            }
        }

        void Update()
        {
            if (mat == null) return;
            bool flashing = Time.time < flashUntil;
            float pulse = 1.4f + 0.8f * Mathf.Sin(Time.time * 5f + index * 0.6f);
            Color c = flashing ? flashColor : BaseEmission;
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", c * (flashing ? 3f : pulse));
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        }

        public void Flash(Color c, float seconds)
        {
            flashColor = c;
            flashUntil = Time.time + seconds;
        }

        public void FlashError() { Flash(new Color(1f, 0.2f, 0.15f), 0.7f); }

        public string Prompt => puzzle != null ? puzzle.PromptForSlot(index) : "";
        public bool CanInteract => puzzle != null && puzzle.AcceptsInput && puzzle.IsHolding;

        public void Interact()
        {
            if (puzzle != null) puzzle.TryInsert(index);
        }
    }
}
