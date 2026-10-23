using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// A big physical button on a console. Pressing it calls puzzle.OnButton(id) and the cap dips in.
    public class PushButton : MonoBehaviour, IInteractable
    {
        public SortPuzzleBase puzzle;
        public string id = "scan";
        public string label = "Press the button";
        public Transform cap;

        Vector3 capRest;
        bool pressing;

        void Awake() { if (cap != null) capRest = cap.localPosition; }

        public string Prompt => label;
        public bool CanInteract => puzzle != null && puzzle.AcceptsInput && !pressing;

        public void Interact()
        {
            if (!CanInteract) return;
            ProceduralSfx.Play(ProceduralSfx.Select, transform.position, 0.8f);
            StartCoroutine(Press());
            puzzle.OnButton(id);
        }

        IEnumerator Press()
        {
            pressing = true;
            if (cap != null)
            {
                Vector3 inward = cap.localRotation * Vector3.up * -0.012f;
                float e = 0f;
                while (e < 0.18f)
                {
                    e += Time.deltaTime;
                    cap.localPosition = capRest + inward * Mathf.Sin(Mathf.Clamp01(e / 0.18f) * Mathf.PI);
                    yield return null;
                }
                cap.localPosition = capRest;
            }
            pressing = false;
        }
    }
}
