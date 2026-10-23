using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SortOrStay.BasicDemo
{
    /// Floor 1, basic demo: bubble sort with six keys.
    /// Click a key, then click its neighbour to swap them. When the keys read
    /// smallest to largest, the door unlocks.
    /// items[i] always hangs on slots[i]: the list is the truth, the keys follow it.
    public class DemoBubblePuzzle : MonoBehaviour
    {
        public Transform[] slots;
        public List<DemoKey> items = new List<DemoKey>();
        public DemoDoor door;
        [Tooltip("Reshuffle until the start has at least this many out-of-order pairs.")]
        public int minInversions = 6;
        public float swapTime = 0.45f;

        public int comparisons, swaps, hintsUsed;
        public bool Busy { get; private set; }
        public bool Solved { get; private set; }
        public float StartedAt { get; private set; }
        public float SolvedAt { get; private set; }
        public int StartInversions { get; private set; }
        public DemoKey Selected => selected;

        public static readonly Color Yellow = new Color(1f, 0.82f, 0.15f);
        public static readonly Color Red = new Color(1f, 0.25f, 0.2f);
        public static readonly Color Blue = new Color(0.25f, 0.85f, 1f);
        public static readonly Color Green = new Color(0.3f, 1f, 0.45f);

        DemoKey selected;
        DemoHUD HUD => DemoHUD.Instance;

        void Start() { Setup(); }

        /// Shuffle the keys onto the hooks and reset the counters.
        public void Setup()
        {
            StopAllCoroutines();
            Busy = false; Solved = false; selected = null;
            comparisons = swaps = hintsUsed = 0;

            int n = items.Count;
            var v = new int[n];
            int want = Mathf.Min(minInversions, n * (n - 1) / 2);
            int guard = 0;
            do
            {
                for (int i = 0; i < n; i++) v[i] = i + 1;
                Shuffle(v);
            } while (Inversions(v) < want && ++guard < 200);

            for (int i = 0; i < n; i++)
            {
                items[i].Init(v[i]);
                items[i].transform.position = slots[i].position;
            }
            StartInversions = Inversions(v);
            if (door != null) door.ResetLock();
            StartedAt = Time.time;
        }

        public void OnKeyClicked(DemoKey clicked)
        {
            if (Busy || Solved || clicked == null) return;

            if (selected == null)
            {
                selected = clicked;
                clicked.Highlight(Yellow, 0.6f);
                return;
            }
            if (clicked == selected)                          // click again to put it back
            {
                selected.Highlight(Color.white);
                selected = null;
                return;
            }

            int i = items.IndexOf(selected), j = items.IndexOf(clicked);
            comparisons++;
            var first = selected;
            selected = null;
            first.Highlight(Color.white);

            if (Mathf.Abs(i - j) != 1)
            {
                first.FlashColor(Red, 0.45f);
                clicked.FlashColor(Red, 0.45f);
                if (HUD != null) HUD.Flash("Only neighbours can swap.");
                return;
            }
            StartCoroutine(SwapRoutine(Mathf.Min(i, j), Mathf.Max(i, j)));
        }

        IEnumerator SwapRoutine(int i, int j)
        {
            Busy = true;
            swaps++;
            DemoKey left = items[i], right = items[j];
            bool wasInOrder = left.value < right.value;

            (items[i], items[j]) = (items[j], items[i]);
            StartCoroutine(MoveArc(left, slots[j].position, 0.30f, 0.06f));   // passes in front
            yield return MoveArc(right, slots[i].position, 0.0f, -0.03f);

            if (wasInOrder && HUD != null)
                HUD.Flash($"{left.value} and {right.value} were already in order. Bubble sort only swaps when the left key is bigger.",
                          new Color(1f, 0.72f, 0.3f), 3f);
            Busy = false;
            CheckWin();
        }

        IEnumerator MoveArc(DemoKey key, Vector3 target, float forward, float lift)
        {
            Vector3 start = key.transform.position;
            float e = 0f;
            while (e < 1f)
            {
                e = Mathf.Min(1f, e + Time.deltaTime / swapTime);
                float k = Mathf.SmoothStep(0f, 1f, e);
                float arc = Mathf.Sin(e * Mathf.PI);
                key.transform.position = Vector3.Lerp(start, target, k) + Vector3.back * forward * arc + Vector3.up * lift * arc;
                yield return null;
            }
            key.transform.position = target;
        }

        void CheckWin()
        {
            for (int k = 1; k < items.Count; k++)
                if (items[k - 1].value > items[k].value) return;
            Solved = true;
            SolvedAt = Time.time;
            if (door != null) door.Unlock();
            StartCoroutine(GlowLeftToRight());
        }

        IEnumerator GlowLeftToRight()
        {
            foreach (var key in items)
            {
                key.Highlight(Green, 0.8f);
                yield return new WaitForSeconds(0.12f);
            }
        }

        /// First neighbouring pair that is out of order, or -1.
        int FirstInversion()
        {
            for (int k = 0; k < items.Count - 1; k++)
                if (items[k].value > items[k + 1].value) return k;
            return -1;
        }

        public string Hint()
        {
            int k = FirstInversion();
            return k < 0 ? "Already sorted!"
                         : $"Look at {items[k].value} and {items[k + 1].value}: the left one is bigger, so swap them.";
        }

        public void ShowHint()
        {
            if (Busy || Solved) return;
            int k = FirstInversion();
            if (k < 0) return;
            hintsUsed++;
            if (selected != null) { selected.Highlight(Color.white); selected = null; }
            items[k].FlashColor(Blue, 1.6f);
            items[k + 1].FlashColor(Blue, 1.6f);
            if (HUD != null) HUD.Flash(Hint(), Blue, 3f);
        }

        public void OnDoorClicked()
        {
            if (door == null || HUD == null) return;
            if (door.Locked) { door.Rattle(); HUD.Flash("The door won't budge. Sort the keys first."); }
            else HUD.Flash("The way up is open. Floor 2 (Selection Sort) comes next.", Green, 2.5f);
        }

        /// Fisher-Yates shuffle.
        public static void Shuffle(int[] a)
        {
            for (int i = a.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (a[i], a[j]) = (a[j], a[i]);
            }
        }

        /// Out-of-order pairs = exactly the number of swaps bubble sort needs.
        public static int Inversions(IList<int> a)
        {
            int c = 0;
            for (int i = 0; i < a.Count; i++)
                for (int j = i + 1; j < a.Count; j++)
                    if (a[i] > a[j]) c++;
            return c;
        }
    }
}
