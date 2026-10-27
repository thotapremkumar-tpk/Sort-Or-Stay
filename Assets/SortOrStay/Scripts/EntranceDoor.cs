using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// The doorway from the stair tower into a floor of the building. It stands open while the player comes down the
    /// stairs, and once they are well inside the room it slides shut behind them: locked in until the room is sorted.
    public class EntranceDoor : MonoBehaviour
    {
        public Transform panel;
        public Vector3 openOffset = new Vector3(1.45f, 0f, 0f);   // local: where the panel waits, inside the wall
        public Vector3 inward = Vector3.back;                       // world direction from the doorway into the room
        public float closeWhenInside = 1.6f;                        // metres past the doorway
        public float closeTime = 0.9f;

        public bool IsClosed { get; private set; }

        Vector3 closedPos;

        void Awake()
        {
            if (panel == null) return;
            closedPos = panel.localPosition;
            panel.localPosition = closedPos + openOffset;
        }

        void Update()
        {
            if (IsClosed || panel == null) return;
            var pc = PlayerController.Instance;
            if (pc == null) return;
            Vector3 d = pc.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 1.6f) return;
            Vector3 side = Vector3.Cross(Vector3.up, inward);
            if (Vector3.Dot(d, inward) > closeWhenInside && Mathf.Abs(Vector3.Dot(d, side)) < 8f) StartCoroutine(Close());
        }

        /// Shut at once (the player starts on this floor).
        public void CloseNow()
        {
            IsClosed = true;
            if (panel != null) panel.localPosition = closedPos;
        }

        IEnumerator Close()
        {
            IsClosed = true;
            ProceduralSfx.Play(ProceduralSfx.Slide, transform.position + Vector3.up * 1.2f, 0.7f);
            Vector3 from = panel.localPosition;
            float e = 0f;
            while (e < closeTime)
            {
                e += Time.deltaTime;
                panel.localPosition = Vector3.Lerp(from, closedPos, Mathf.SmoothStep(0f, 1f, e / closeTime));
                yield return null;
            }
            panel.localPosition = closedPos;
            ProceduralSfx.Play(ProceduralSfx.Thud, transform.position + Vector3.up * 1.2f, 0.6f);
        }
    }
}
