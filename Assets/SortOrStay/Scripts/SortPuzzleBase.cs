using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay
{
    /// Shared logic for every room. items[i] always sits in slots[i]:
    /// the list is the source of truth and the physical objects follow it.
    public abstract class SortPuzzleBase : MonoBehaviour
    {
        public Transform[] slots;
        public List<SortItem> items = new List<SortItem>();
        public Door door;
        public int comparisons, swaps, hintsUsed, mistakes;
        public bool inputEnabled = true;     // off during the intro

        public bool Solved { get; protected set; }
        public virtual bool AcceptsInput => inputEnabled && !Solved && !busy;

        /// In the five-floor building only the floor the player is on drives the HUD and the first-person view.
        public bool IsCurrent => BuildingManager.Instance == null || BuildingManager.Instance.CurrentPuzzle == this;

        /// The move counter, top right of the HUD (only for the floor the player is on).
        protected void SetHudStats(string s)
        {
            if (IsCurrent && GameUI.Instance != null) GameUI.Instance.SetStats(s);
        }

        /// First person: keep the eye far enough from this floor's display that all of it fits on screen.
        protected virtual void ApplyViewStandOff() { }

        /// Redraw boards, terminal and HUD counter.
        protected virtual void RefreshDisplays() { }

        /// The player has just walked onto this floor (building) or the floor's scene has started.
        public void OnBecameCurrent()
        {
            startTime = Time.time;              // the floor's clock starts when the player walks in
            ApplyViewStandOff();
            RefreshDisplays();
        }

        protected bool busy;                 // true while an animation runs
        protected float startTime;

        protected static PlayerController Player => PlayerController.Instance;

        protected static void Gesture(string g)
        {
            if (PlayerController.Instance != null) PlayerController.Instance.Gesture(g);
        }

        protected bool IsSorted()
        {
            for (int i = 1; i < items.Count; i++)
                if (items[i - 1].value > items[i].value) return false;
            return true;
        }

        protected IEnumerator MoveTo(SortItem item, Vector3 pos, float t = 0.4f)
        {
            Vector3 start = item.transform.position;
            float e = 0f;
            while (e < t)
            {
                e += Time.deltaTime;
                item.transform.position = Vector3.Lerp(start, pos, Mathf.SmoothStep(0f, 1f, e / t));
                yield return null;
            }
            item.transform.position = pos;
        }

        /// Curved move (quadratic Bezier) so two items swapping pass each other instead of overlapping.
        protected IEnumerator ArcTo(SortItem item, Vector3 pos, Vector3 bulge, float t = 0.45f)
        {
            Vector3 a = item.transform.position;
            Vector3 c = (a + pos) * 0.5f + bulge * 2f;
            float e = 0f;
            while (e < t)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / t);
                Vector3 p = Vector3.Lerp(Vector3.Lerp(a, c, k), Vector3.Lerp(c, pos, k), k);
                item.transform.position = p;
                yield return null;
            }
            item.transform.position = pos;
        }

        protected void CheckWin()
        {
            if (!Solved && IsSorted())
            {
                Solved = true;
                OnSolved();
            }
        }

        protected virtual void OnSolved()
        {
            if (door != null) door.Unlock();
        }

        public abstract void Setup();     // spawn + shuffle items
        public abstract string Hint();    // text for the hint button

        public virtual void OnItemClicked(SortItem item) { }

        /// Whether clicking this item does anything right now (the pointer only highlights usable things).
        public virtual bool CanUse(SortItem item) => AcceptsInput;

        /// Number-key shortcuts are allowed (normal play, no popup or pause menu).
        protected bool ShortcutsAllowed()
        {
            var ui = GameUI.Instance;
            if (ui != null && (ui.PopupOpen || ui.PauseOpen || ui.EndCardOpen)) return false;
            var pc = PlayerController.Instance;
            if (pc != null && (pc.cinematic || !pc.canMove)) return false;
            return AcceptsInput;
        }

        /// 1-9 on the number row or keypad this frame, else 0.
        protected static int DigitPressed(UnityEngine.InputSystem.Keyboard kb)
        {
            if (kb == null) return 0;
            for (int d = 1; d <= 9; d++)
            {
                if (kb[UnityEngine.InputSystem.Key.Digit1 + (d - 1)].wasPressedThisFrame) return d;
                if (kb[UnityEngine.InputSystem.Key.Numpad1 + (d - 1)].wasPressedThisFrame) return d;
            }
            return 0;
        }

        /// Walk over to an object and use it, exactly as if it had been clicked.
        protected static void UseByKey(Component target)
        {
            if (PlayerInteractor.Instance != null) PlayerInteractor.Instance.Use(target);
        }

        /// Wall buttons (PushButton) call this with their id.
        public virtual void OnButton(string id) { }

        public virtual string PromptFor(SortItem item)
        {
            if (!AcceptsInput) return "";
            return "Select " + item.value;
        }

        public virtual void RequestHint()
        {
            hintsUsed++;
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4f);
        }

        /// Fisher-Yates shuffle; reshuffles if the result is already sorted.
        public static void Shuffle(int[] arr)
        {
            if (arr.Length < 2) return;
            do
            {
                for (int i = arr.Length - 1; i > 0; i--)
                {
                    int j = Random.Range(0, i + 1);
                    (arr[i], arr[j]) = (arr[j], arr[i]);
                }
            } while (IsAscending(arr));
        }

        public static bool IsAscending(IList<int> a)
        {
            for (int i = 1; i < a.Count; i++)
                if (a[i - 1] > a[i]) return false;
            return true;
        }

        /// Out-of-order pairs = the exact number of swaps bubble sort needs.
        public static int CountInversions(IList<int> a)
        {
            int n = 0;
            for (int i = 0; i < a.Count; i++)
                for (int j = i + 1; j < a.Count; j++)
                    if (a[i] > a[j]) n++;
            return n;
        }

        /// Picks 'count' distinct values from 'pool' (random order).
        public static int[] Pick(int[] pool, int count)
        {
            var list = new List<int>(pool);
            var res = new int[count];
            for (int i = 0; i < count; i++)
            {
                int k = Random.Range(0, list.Count);
                res[i] = list[k];
                list.RemoveAt(k);
            }
            return res;
        }

        protected int[] CurrentValues()
        {
            var v = new int[items.Count];
            for (int i = 0; i < items.Count; i++) v[i] = items[i].value;
            return v;
        }
    }
}
