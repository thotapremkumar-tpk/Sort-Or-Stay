using System.Collections;
using UnityEngine;

namespace SortOrStay
{
    /// Sliding door (single or double) with a red/green status lamp. Locked until the puzzle unlocks it.
    public class Door : MonoBehaviour, IInteractable
    {
        public Transform panel;
        public Vector3 openOffset = new Vector3(1.3f, 0f, 0f);
        public Transform panelB;                       // optional second leaf (double door)
        public Vector3 openOffsetB = new Vector3(-1.3f, 0f, 0f);
        public float openTime = 1.6f;
        public Renderer statusLamp;
        public Light statusLight;
        public Color lockedColor = new Color(1f, 0.12f, 0.08f);
        public Color unlockedColor = new Color(0.2f, 1f, 0.35f);
        public string lockedMessage = "Locked tight.  The terminal says: ORDER UNLOCKS.";

        public bool IsUnlocked { get; private set; }

        Vector3 closedPos, closedPosB;
        bool shaking;
        Material lampMat;

        void Awake()
        {
            if (panel != null) closedPos = panel.localPosition;
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
            ProceduralSfx.Play(ProceduralSfx.Slide, transform.position + Vector3.up * 1.2f, 0.9f);
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
