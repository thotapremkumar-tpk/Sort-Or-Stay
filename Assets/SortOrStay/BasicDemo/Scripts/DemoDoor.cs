using System.Collections;
using UnityEngine;

namespace SortOrStay.BasicDemo
{
    /// Hinged door with a red (locked) / green (open) lamp. Sits on the hinge; the panel is a child.
    public class DemoDoor : MonoBehaviour
    {
        public Renderer lamp;
        public Light lampLight;
        [Tooltip("Light behind the door, switched on when it opens.")]
        public Light beyondLight;
        public float openAngle = -95f;

        public bool Locked { get; private set; } = true;

        static readonly Color LockedCol = new Color(1f, 0.12f, 0.08f);
        static readonly Color OpenCol = new Color(0.2f, 1f, 0.35f);

        Quaternion closedRot;
        Vector3 closedPos;
        Material lampMat;
        Coroutine co;

        void Awake()
        {
            closedRot = transform.localRotation;
            closedPos = transform.localPosition;
            if (lamp != null) lampMat = lamp.material;
            SetLamp(LockedCol);
            if (beyondLight != null) beyondLight.enabled = false;
        }

        public void ResetLock()
        {
            if (co != null) StopCoroutine(co);
            transform.localRotation = closedRot;
            transform.localPosition = closedPos;
            Locked = true;
            SetLamp(LockedCol);
            if (beyondLight != null) beyondLight.enabled = false;
        }

        public void Unlock()
        {
            if (!Locked) return;
            Locked = false;
            SetLamp(OpenCol);
            if (co != null) StopCoroutine(co);
            co = StartCoroutine(OpenRoutine());
        }

        /// Shake when the player tries the door before the keys are sorted.
        public void Rattle()
        {
            if (!Locked) return;
            if (co != null) StopCoroutine(co);
            co = StartCoroutine(ShakeRoutine());
        }

        IEnumerator OpenRoutine()
        {
            yield return new WaitForSeconds(0.7f);
            if (beyondLight != null) beyondLight.enabled = true;
            Quaternion target = closedRot * Quaternion.Euler(0f, openAngle, 0f);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 1.4f;
                transform.localRotation = Quaternion.Slerp(closedRot, target, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
        }

        IEnumerator ShakeRoutine()
        {
            float t = 0f;
            while (t < 0.4f)
            {
                t += Time.deltaTime;
                transform.localPosition = closedPos + new Vector3(Mathf.Sin(t * 95f) * 0.025f * (1f - t / 0.4f), 0f, 0f);
                yield return null;
            }
            transform.localPosition = closedPos;
        }

        void SetLamp(Color c)
        {
            if (lampMat != null)
            {
                if (lampMat.HasProperty("_BaseColor")) lampMat.SetColor("_BaseColor", c);
                lampMat.EnableKeyword("_EMISSION");
                lampMat.SetColor("_EmissionColor", c * 3f);
            }
            if (lampLight != null) lampLight.color = c;
        }
    }
}
