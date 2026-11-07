using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// A sliding door in the hallway of a floor: the entrance of a room, or the way out to the stairs.
    /// It stays locked (red lamp) until the building opens it, then slides into the wall (green lamp).
    /// A room entrance slides shut again behind the player once they are well inside, so each room is
    /// left through its own exit door, after its sort and its questions.
    public class RoomGate : MonoBehaviour, IInteractable
    {
        public Transform panel;
        public Vector3 openOffset = new Vector3(1.45f, 0f, 0f);   // local: where the panel waits, inside the wall
        public Vector3 inward = Vector3.back;                       // world direction from the doorway into the room
        public bool closeBehind = true;                             // shut once the player is inside
        public float closeWhenInside = 1.7f;                        // metres past the doorway
        public float moveTime = 0.9f;
        public Renderer lamp;
        public Light lampLight;
        [TextArea] public string lockedMessage = "Locked.  Finish the room before this one first.";

        public bool IsOpen { get; private set; }
        public bool Unlocked { get; private set; }

        Vector3 closedPos;
        Material lampMat;
        bool closedBehind;
        float playerInSince = -1f;
        Coroutine move;

        static readonly Color Red = new Color(1f, 0.12f, 0.08f);
        static readonly Color Green = new Color(0.2f, 1f, 0.35f);

        void Awake()
        {
            if (panel != null) closedPos = panel.localPosition;
            if (lamp != null) { lampMat = lamp.material; lampMat.EnableKeyword("_EMISSION"); }
            SetLamp(Unlocked && !closedBehind);
        }

        /// Open now and stay open (until the player is inside, if closeBehind).
        public void Open(bool instant = false)
        {
            Unlocked = true;
            closedBehind = false;
            SetLamp(true);
            if (IsOpen) return;
            IsOpen = true;
            if (move != null) StopCoroutine(move);
            if (instant || panel == null) { if (panel != null) panel.localPosition = closedPos + openOffset; Rebuild(); return; }
            move = StartCoroutine(Slide(closedPos + openOffset, true));
        }

        /// Shut at once and lock (the player starts inside the room).
        public void CloseNow()
        {
            if (move != null) StopCoroutine(move);
            IsOpen = false;
            closedBehind = true;
            if (panel != null) panel.localPosition = closedPos;
            SetLamp(false);
        }

        void Update()
        {
            if (!closeBehind || !IsOpen || closedBehind || panel == null) return;
            var pc = PlayerController.Instance;
            if (pc == null) return;
            Vector3 d = pc.transform.position - transform.position;
            if (Mathf.Abs(d.y) > 1.6f) return;
            Vector3 side = Vector3.Cross(Vector3.up, inward);
            if (Vector3.Dot(d, inward) > closeWhenInside && Mathf.Abs(Vector3.Dot(d, side)) < 12f)
            {
                // wait for the guide to come in too (but not for ever)
                if (playerInSince < 0f) playerInSince = Time.time;
                var g = GuideNpc.Instance;
                bool guideIn = g == null || !g.isActiveAndEnabled || g.IsPast(transform.position, inward, 0.9f);
                if (!guideIn && Time.time < playerInSince + 6f) return;
                closedBehind = true;
                IsOpen = false;
                SetLamp(false);
                if (move != null) StopCoroutine(move);
                move = StartCoroutine(Slide(closedPos, false));
            }
            else playerInSince = -1f;
        }

        IEnumerator Slide(Vector3 to, bool opening)
        {
            ProceduralSfx.Play(ProceduralSfx.Slide, transform.position + Vector3.up * 1.2f, 0.7f);
            Vector3 from = panel.localPosition;
            float e = 0f;
            while (e < moveTime)
            {
                e += Time.deltaTime;
                panel.localPosition = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, e / moveTime));
                yield return null;
            }
            panel.localPosition = to;
            if (!opening) ProceduralSfx.Play(ProceduralSfx.Thud, transform.position + Vector3.up * 1.2f, 0.5f);
            Rebuild();
            move = null;
        }

        static void Rebuild()
        {
            if (PlayerController.Instance != null) PlayerController.Instance.RebuildNavMesh();
        }

        void SetLamp(bool green)
        {
            Color c = green ? Green : Red;
            if (lampMat != null)
            {
                if (lampMat.HasProperty("_BaseColor")) lampMat.SetColor("_BaseColor", c);
                if (lampMat.HasProperty("_EmissionColor")) lampMat.SetColor("_EmissionColor", c * 4f);
            }
            if (lampLight != null) lampLight.color = c;
        }

        // IInteractable: a locked gate rattles and says why
        public string Prompt => IsOpen ? "" : "Try the door";
        public bool CanInteract => !IsOpen && panel != null;

        public void Interact()
        {
            if (IsOpen) return;
            ProceduralSfx.Play(ProceduralSfx.Rattle, transform.position + Vector3.up * 1.1f, 0.8f);
            if (PlayerController.Instance != null) PlayerController.Instance.Gesture(PlayerController.GestureTryDoor);
            var g = GuideNpc.Instance;
            string msg = closedBehind && Unlocked ? "This room is done.  Its own exit door is the way out." : lockedMessage;
            if (g != null && g.isActiveAndEnabled && g.Visible) g.Say(msg, GuideNpc.Mood.Deny);
            else if (GameUI.Instance != null) GameUI.Instance.Flash(msg);
        }
    }
}
