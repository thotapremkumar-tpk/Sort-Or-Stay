using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// Sliding door (single or double), or a hinged door that swings open, with a red/green status lamp.
    /// Locked until the puzzle unlocks it.
    public class Door : MonoBehaviour, IInteractable
    {
        public Transform panel;
        public Vector3 openOffset = new Vector3(1.3f, 0f, 0f);
        public Transform panelB;                       // optional second leaf (double door)
        public Vector3 openOffsetB = new Vector3(-1.3f, 0f, 0f);
        public float openTime = 1.6f;
        [Header("Hinged (instead of sliding)")]
        public bool swing;
        public Vector3 hinge = new Vector3(-0.6f, 0f, 0f);  // in the door's local space, on the hinge-side edge
        public float swingAngle = -100f;                     // degrees about the vertical; negative opens away from the room
        public Renderer statusLamp;
        public Light statusLight;
        public Color lockedColor = new Color(1f, 0.12f, 0.08f);
        public Color unlockedColor = new Color(0.2f, 1f, 0.35f);
        public string lockedMessage = "Locked tight.  The terminal says: ORDER UNLOCKS.";

        public bool IsUnlocked { get; private set; }

        Vector3 closedPos, closedPosB;
        Quaternion closedRot;
        bool shaking;
        Material lampMat;

        void Awake()
        {
            if (panel != null) { closedPos = panel.localPosition; closedRot = panel.localRotation; }
            if (panelB != null) closedPosB = panelB.localPosition;
            if (statusLamp != null) lampMat = statusLamp.material;
            SetLamp(lockedColor);
        }

        public void Unlock()
        {
            if (IsUnlocked) return;
            IsUnlocked = true;
            SetLamp(unlockedColor);
            ProceduralSfx.Play(ProceduralSfx.Unlock, transform.position + Vector3.up * 1.5f, 1f);
            StartCoroutine(OpenRoutine());
        }

        IEnumerator OpenRoutine()
        {
            yield return new WaitForSeconds(0.55f);
            while (shaking) yield return null;
            ProceduralSfx.Play(swing ? ProceduralSfx.Rattle : ProceduralSfx.Slide, transform.position + Vector3.up * 1.2f, swing ? 0.5f : 0.9f);
            if (swing) { yield return SwingOpen(); yield break; }
            float e = 0f;
            while (e < openTime)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / openTime);
                if (panel != null) panel.localPosition = closedPos + openOffset * k;
                if (panelB != null) panelB.localPosition = closedPosB + openOffsetB * k;
                yield return null;
            }
            if (panel != null) panel.localPosition = closedPos + openOffset;
            if (panelB != null) panelB.localPosition = closedPosB + openOffsetB;
            // the doorway is free now: let clicks walk through it
            if (PlayerController.Instance != null) PlayerController.Instance.RebuildNavMesh();
        }

        /// A heavy door: a small push to unlatch it, then it swings wide and settles with a little bounce.
        IEnumerator SwingOpen()
        {
            float e = 0f;
            while (e < openTime)
            {
                e += Time.deltaTime;
                float t = Mathf.Clamp01(e / openTime);
                float k = t < 0.12f
                    ? Mathf.SmoothStep(0f, 0.06f, t / 0.12f)                            // unlatch
                    : Mathf.Lerp(0.06f, 1f, 1f - Mathf.Pow(1f - (t - 0.12f) / 0.88f, 3f));   // swing, slowing as it opens
                k += 0.025f * Mathf.Sin(Mathf.Clamp01((t - 0.8f) / 0.2f) * Mathf.PI);     // settles against its stop
                SetSwing(k);
                yield return null;
            }
            SetSwing(1f);
            ProceduralSfx.Play(ProceduralSfx.Thud, transform.position + Vector3.up * 1.2f, 0.4f);
            if (PlayerController.Instance != null) PlayerController.Instance.RebuildNavMesh();
        }

        void SetSwing(float k)
        {
            if (panel == null) return;
            Quaternion r = Quaternion.Euler(0f, swingAngle * k, 0f);
            panel.localPosition = hinge + r * (closedPos - hinge);
            panel.localRotation = r * closedRot;
        }

        IEnumerator Shake()
        {
            shaking = true;
            float e = 0f;
            while (e < 0.5f)
            {
                e += Time.deltaTime;
                float x = Mathf.Sin(e * 75f) * 0.012f * (1f - e / 0.5f);
                if (panel != null) panel.localPosition = closedPos + new Vector3(x, 0f, x * 0.6f);
                if (panelB != null) panelB.localPosition = closedPosB + new Vector3(-x, 0f, x * 0.6f);
                yield return null;
            }
            if (panel != null) panel.localPosition = closedPos;
            if (panelB != null) panelB.localPosition = closedPosB;
            shaking = false;
        }

        void SetLamp(Color c)
        {
            if (lampMat != null)
            {
                lampMat.EnableKeyword("_EMISSION");
                if (lampMat.HasProperty("_BaseColor")) lampMat.SetColor("_BaseColor", c);
                if (lampMat.HasProperty("_EmissionColor")) lampMat.SetColor("_EmissionColor", c * 4f);
            }
            if (statusLight != null) statusLight.color = c;
        }

        // IInteractable
        public string Prompt => IsUnlocked ? "" : "Try the door";
        public bool CanInteract => !IsUnlocked && !shaking;

        public void Interact()
        {
            if (IsUnlocked) return;
            StartCoroutine(Shake());
            ProceduralSfx.Play(ProceduralSfx.Rattle, transform.position + Vector3.up * 1.1f, 1f);
            if (PlayerController.Instance != null) PlayerController.Instance.Gesture(PlayerController.GestureTryDoor);
            if (GameUI.Instance != null) GameUI.Instance.Flash(lockedMessage);
        }
    }
}
