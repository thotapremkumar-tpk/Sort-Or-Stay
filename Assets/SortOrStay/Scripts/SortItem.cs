using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SortOrStay
{
    /// Any object that has a value to sort (here: a numbered key on the rack).
    /// Clicking it is forwarded to the puzzle that owns it.
    public class SortItem : MonoBehaviour, IInteractable
    {
        public int value;
        public TMP_Text label;            // number on the key tag
        public Renderer rend;
        public Renderer tagRenderer;      // the paper tag; tinted and glows with the key state
        public Transform swing;           // pivot the key swings around on its hook
        public bool tintModel = true;     // false: only the tag changes colour (books keep their cover colour)
        [HideInInspector] public SortPuzzleBase puzzle;

        public static readonly Color Idle = Color.white;
        public static readonly Color SelectedColor = new Color(1f, 0.8f, 0.12f);
        public static readonly Color SortedColor = new Color(0.25f, 1f, 0.4f);
        public static readonly Color ErrorColor = new Color(1f, 0.22f, 0.18f);
        public static readonly Color HintColor = new Color(0.25f, 0.85f, 1f);
        static readonly Color TagBase = new Color(0.93f, 0.9f, 0.82f);

        readonly List<Material> modelMats = new List<Material>();
        readonly List<Color> modelBase = new List<Color>();
        Material tagMat;
        Color persistent = Idle;
        float flashUntil = -1f;
        float swingAngle;
        float swingVel;
        Quaternion swingBase = Quaternion.identity;

        public bool IsLocked { get; private set; }

        void Awake()
        {
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r == tagRenderer || r.GetComponent<TMP_Text>() != null) continue;
                foreach (var m in r.materials)
                {
                    modelMats.Add(m);
                    modelBase.Add(m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.HasProperty("_Color") ? m.GetColor("_Color") : Color.white);
                }
            }
            if (tagRenderer != null)
            {
                tagMat = tagRenderer.material;
                tagMat.EnableKeyword("_EMISSION");
            }
            if (swing != null) swingBase = swing.localRotation;
            Apply(Idle);
        }

        public void Init(int v)
        {
            value = v;
            if (label != null) label.text = v.ToString();
            IsLocked = false;
            Highlight(Idle);
        }

        /// Lasting colour (idle, selected, sorted). Waits if a flash is showing.
        public void Highlight(Color c)
        {
            persistent = c;
            if (Time.time >= flashUntil) Apply(c);
        }

        /// Temporary colour for a few seconds, then back to the lasting one.
        public void Flash(Color c, float seconds)
        {
            flashUntil = Time.time + seconds;
            Apply(c);
        }

        public void Lock()
        {
            IsLocked = true;
            Highlight(new Color(1f, 0.78f, 0.2f));
        }

        /// Push the key so it swings on its hook.
        public void Kick(float degreesPerSecond)
        {
            swingVel += degreesPerSecond;
        }

        void Update()
        {
            if (flashUntil > 0f && Time.time >= flashUntil)
            {
                flashUntil = -1f;
                Apply(persistent);
            }

            if (swing == null) return;

            // Damped spring; a selected key sways gently.
            float target = persistent == SelectedColor ? Mathf.Sin(Time.time * 5.5f) * 4f : 0f;
            float acc = -38f * (swingAngle - target) - 4.5f * swingVel;
            swingVel += acc * Time.deltaTime;
            swingAngle += swingVel * Time.deltaTime;
            swingAngle = Mathf.Clamp(swingAngle, -25f, 25f);
            swing.localRotation = Quaternion.Euler(0f, 0f, swingAngle) * swingBase;
        }

        void Apply(Color c)
        {
            bool idle = c == Idle;
            for (int i = 0; i < modelMats.Count && tintModel; i++)
            {
                var m = modelMats[i];
                if (m == null) continue;
                Color b = modelBase[i];
                Color model = idle ? b : Color.Lerp(b, c, 0.55f);
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", model);
                else if (m.HasProperty("_Color")) m.SetColor("_Color", model);
            }

            if (tagMat != null)
            {
                Color tag = idle ? TagBase : Color.Lerp(TagBase, c, 0.75f);
                if (tagMat.HasProperty("_BaseColor")) tagMat.SetColor("_BaseColor", tag);
                if (tagMat.HasProperty("_EmissionColor")) tagMat.SetColor("_EmissionColor", idle ? Color.black : c * 1.4f);
            }
        }

        // IInteractable
        public string Prompt => puzzle != null ? puzzle.PromptFor(this) : "";
        public bool CanInteract => puzzle != null && puzzle.CanUse(this);
        public void Interact()
        {
            if (puzzle != null) puzzle.OnItemClicked(this);
        }
    }
}
