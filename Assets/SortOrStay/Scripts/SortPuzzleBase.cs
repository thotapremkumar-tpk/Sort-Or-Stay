using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay
{
    /// Which input a room deals. Classic = the original single-room deal (standalone floor scenes).
    /// In the building every floor has three rooms: best case, average case, worst case.
    public enum SortCase { Classic, Best, Average, Worst }

    /// Shared logic for every room. items[i] always sits in slots[i]:
    /// the list is the source of truth and the physical objects follow it.
    public abstract class SortPuzzleBase : MonoBehaviour
    {
        public Transform[] slots;
        public List<SortItem> items = new List<SortItem>();
        public Door door;
        public int comparisons, swaps, hintsUsed, mistakes;
        public bool inputEnabled = true;     // off during the intro

        [Header("Room (five-floor building)")]
        public SortCase sortCase = SortCase.Classic;
        public int floorNumber = 1;
        public int roomNumber = 0;           // 1-3 in the building, 0 in a standalone floor scene
        [Tooltip("Questions asked after the sort; the door opens only when all are answered right. Optional.")]
        public RoomQuiz quiz;

        public bool Solved { get; protected set; }
        public virtual bool AcceptsInput => inputEnabled && !Solved && !busy;

        /// "Bubble sort", "Selection sort"...
        public abstract string Algorithm { get; }

        public string CaseName => CaseLabel(sortCase);

        public static string CaseLabel(SortCase c)
        {
            switch (c)
            {
                case SortCase.Best: return "best case";
                case SortCase.Average: return "average case";
                case SortCase.Worst: return "worst case";
                default: return "";
            }
        }

        /// Count of out-of-order pairs at the start of this room's deal.
        public int StartInversions { get; protected set; }

        // ------------------------------------------------------------ deals for the three cases

        /// Rearranges a deal for this room's case. Classic keeps the puzzle's own random deal.
        protected int[] DealFor(int[] vals)
        {
            if (sortCase == SortCase.Classic || vals == null || vals.Length < 2) return vals;
            var sorted = (int[])vals.Clone();
            System.Array.Sort(sorted);
            int[] res;
            switch (sortCase)
            {
                case SortCase.Best: res = BestDeal(sorted); break;
                case SortCase.Worst: res = WorstDeal(sorted); break;
                default: res = AverageDeal(sorted); break;
            }
            StartInversions = CountInversions(res);
            return res;
        }

        /// Best case input for this algorithm, from the values in ascending order.
        protected virtual int[] BestDeal(int[] sorted) => NearlySorted(sorted);

        /// Worst case input for this algorithm, from the values in ascending order.
        protected virtual int[] WorstDeal(int[] sorted) => Reversed(sorted);

        /// Extra rule an average deal must meet (e.g. the first pass must have work to do).
        protected virtual bool AverageOk(int[] v) => true;

        /// A random order with a middling number of out-of-order pairs: about n(n-1)/4, the expected count.
        int[] AverageDeal(int[] sorted)
        {
            int n = sorted.Length, max = n * (n - 1) / 2;
            int lo = Mathf.Max(1, Mathf.RoundToInt(max * 0.38f)), hi = Mathf.Max(lo, Mathf.RoundToInt(max * 0.62f));
            var v = (int[])sorted.Clone();
            for (int guard = 0; guard < 4000; guard++)
            {
                Shuffle(v);
                int inv = CountInversions(v);
                if (inv >= lo && inv <= hi && AverageOk(v)) return v;
            }
            return v;
        }

        /// Sorted except two separate neighbour pairs: one quick pass fixes it.
        public static int[] NearlySorted(int[] sorted)
        {
            var v = (int[])sorted.Clone();
            int n = v.Length;
            if (n < 4) { if (n >= 2) (v[0], v[1]) = (v[1], v[0]); return v; }
            int i = Random.Range(0, n - 1);
            int j;
            int guard = 0;
            do { j = Random.Range(0, n - 1); } while (Mathf.Abs(i - j) < 2 && ++guard < 100);
            (v[i], v[i + 1]) = (v[i + 1], v[i]);
            (v[j], v[j + 1]) = (v[j + 1], v[j]);
            return v;
        }

        public static int[] Reversed(int[] sorted)
        {
            var v = (int[])sorted.Clone();
            System.Array.Reverse(v);
            return v;
        }

        // ------------------------------------------------------------ after the sort: questions, then the door

        /// True while the room's questions still stand between the player and the door.
        public bool QuizPending => quiz != null && quiz.isActiveAndEnabled && !quiz.Passed;

        /// What the objective says once the door is open (kept while the questions are asked).
        public string OpenObjective { get; private set; } = "The door is open.  Walk through it.";

        /// The door opens now, or after the room's questions if it has any.
        protected void UnlockDoor()
        {
            if (QuizPending) { quiz.Arm(this); return; }
            if (door != null) door.Unlock();
        }

        protected string AfterSolveObjective(string open)
        {
            OpenObjective = open;
            return QuizPending
                ? "Sorted!  Now answer <b>Professor Ada's 3 questions</b> about this " + CaseName + " to unlock the door."
                : open;
        }

        protected string DebriefFooter(string normal)
        {
            return QuizPending ? "Click or press E to close  ·  then answer 3 questions to open the door" : normal;
        }

        /// Keeps this room's score (one line per floor and room on the scoreboard).
        protected void Record(LevelResult r)
        {
            r.room = roomNumber;
            r.sortCase = sortCase;
            if (r.startInversions == 0) r.startInversions = StartInversions;
            GameState.Instance.Record(r);
        }

        /// A hint: spoken by the guide when there is one, otherwise flashed on screen.
        protected void ShowHint(string text, float seconds = 4f)
        {
            var g = GuideNpc.Instance;
            if (g != null && g.isActiveAndEnabled && IsCurrent) { g.SayHint(text); return; }
            if (GameUI.Instance != null) GameUI.Instance.Flash(text, SortItem.HintColor, seconds);
        }

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
            ShowHint(Hint());
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
