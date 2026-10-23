using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay.BasicDemo
{
    /// One numbered key on the rack. The pivot is at the top (the hook), so the key hangs down.
    public class DemoKey : MonoBehaviour
    {
        public int value;
        [Tooltip("Key length at scale 1, hanging down from the pivot.")]
        public float length = 0.4f;

        readonly List<Material> mats = new List<Material>();
        readonly List<Color> baseCols = new List<Color>();
        Color stateCol = Color.white;
        float stateGlow;
        Coroutine flashCo;

        /// Where the HUD draws this key's number: just under the key.
        public Vector3 LabelAnchor => transform.position + Vector3.down * (length * transform.localScale.y + 0.11f);

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>())
                foreach (var m in r.materials)
                {
                    mats.Add(m);
                    baseCols.Add(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color);
                }
        }

        /// Gives the key its number. Bigger numbers get slightly longer keys.
        public void Init(int v)
        {
            value = v;
            transform.localScale = Vector3.one * (0.8f + 0.06f * v);
            Highlight(Color.white);
        }

        /// Lasting colour: normal (white), selected (yellow) or solved (green).
        public void Highlight(Color c, float glow = 0f)
        {
            stateCol = c;
            stateGlow = glow;
            if (flashCo != null) { StopCoroutine(flashCo); flashCo = null; }
            Apply(c, glow);
        }

        /// Shows a colour briefly (red = wrong move, blue = hint), then goes back.
        public void FlashColor(Color c, float seconds)
        {
            if (flashCo != null) StopCoroutine(flashCo);
            flashCo = StartCoroutine(FlashRoutine(c, seconds));
        }

        IEnumerator FlashRoutine(Color c, float seconds)
        {
            Apply(c, 0.8f);
            yield return new WaitForSeconds(seconds);
            Apply(stateCol, stateGlow);
            flashCo = null;
        }

        void Apply(Color c, float glow)
        {
            for (int i = 0; i < mats.Count; i++)
            {
                var m = mats[i];
                if (m == null) continue;
                Color tinted = baseCols[i] * c;
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tinted); else m.color = tinted;
                if (m.HasProperty("_EmissionColor"))
                {
                    if (glow > 0f) m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", glow > 0f ? c * glow : Color.black);
                }
            }
        }
    }
}
