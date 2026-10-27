using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 5, the Final Vault. Eight gold bars lie on the marble counter, out of order, and the vault door opens
    /// only for a sorted row. Quick sort, partitioned in place (Lomuto):
    ///   1. raise the pivot, the last bar of the range;
    ///   2. compare every other bar of the range with it, left to right. SMALLER bars are swapped behind the laser
    ///      gate (the gate then moves one place right); BIGGER bars stay where they are;
    ///   3. drop the pivot into the slot just right of the gate: it is in its final place and never moves again.
    /// Then the same is done to the part left of the pivot, then to the part right of it. One bar on its own is
    /// already in place. The display shows the ranges still to do (the recursion stack).
    public class QuickPuzzle : SortPuzzleBase, IAutoPlayable
    {
        [Header("Deal")]
        public int[] valuePool = { 11, 14, 19, 23, 27, 31, 36, 42, 47, 53, 58, 64, 69, 75, 81, 88, 93, 97 };
        public int minOutOfOrderPairs = 11;

        [Header("Vault counter")]
        public float pivotLift = 0.42f;      // the pivot hovers this high above its range while the others are compared
        public Transform gate;               // laser gate: every bar left of it is smaller than the pivot
        public Transform rangeStrip;         // glowing strip under the range being partitioned
        public Transform pads;               // the two choice buttons; they follow the bar being compared
        public PushButton smallerPad, biggerPad;
        public float padsForward = -0.22f;   // the pads sit this far in front of the bar (toward the player)
        public Light pivotLight;
        public TMP_Text board;               // big screen above the vault door
        public Terminal terminal;
        public Transform vaultWheel;         // the door's hand wheel spins before the door swings open

        [Header("Hands")]
        [Tooltip("The avatar walks this close to the pivot bar and lifts it by hand.")]
        public float handReach = 0.7f;
        [Tooltip("First person: the eye stays at least this far from the counter, so every bar is on screen.")]
        public float firstPersonStandOff = 1.15f;

        enum Phase { Pivot, Scan, Drop, Done }

        static readonly Color SmallerColor = new Color(0.30f, 0.62f, 1f);
        static readonly Color BiggerColor = new Color(1f, 0.50f, 0.38f);
        static readonly Color PivotColor = new Color(0.78f, 0.45f, 1f);
        static readonly Color NextColor = new Color(1f, 0.72f, 0.2f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);

        Phase phase;
        readonly Stack<Vector2Int> todo = new Stack<Vector2Int>();   // ranges still to sort (x = lo, y = hi)
        int lo, hi;                          // the range being partitioned
        int wall;                            // the gate: bars lo..wall-1 are smaller than the pivot
        int cur;                             // the bar being compared
        SortItem pivot;
        int homeValue;                       // the pivot just dropped into its final place
        bool[] placed;
        int partitions, startInversions;
        readonly List<string> pivotLog = new List<string>();
        Material stripMat;

        int N => items.Count;
        float Pitch => slots.Length > 1 ? Vector3.Distance(slots[0].position, slots[1].position) : 0.46f;
        static HandReach Hand => PlayerController.Instance != null ? PlayerController.Instance.Hand : null;

        // ------------------------------------------------------------ setup

        void Start()
        {
            if (rangeStrip != null)
            {
                var r = rangeStrip.GetComponent<Renderer>();
                if (r != null) { stripMat = r.material; stripMat.EnableKeyword("_EMISSION"); }
            }
            if (IsCurrent) ApplyViewStandOff();
            Setup();
        }

        /// First person: stand the eye off the counter far enough that every bar fits on screen.
        protected override void ApplyViewStandOff()
        {
            if (Player == null || slots.Length < 2) return;
            Vector3 a = slots[0].position, b = slots[slots.Length - 1].position;
            Player.SetViewStandOff((a + b) * 0.5f, -slots[0].forward, b - a, Vector3.Distance(a, b) * 0.5f, firstPersonStandOff);
        }

        protected override void RefreshDisplays() => Refresh();

        public override void Setup()
        {
            int n = N;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            int[] vals = Pick(valuePool, n);
            int guard = 0;
            while ((CountInversions(vals) < want || !MiddlingPivot(vals)) && ++guard < 800) vals = Pick(valuePool, n);

            for (int i = 0; i < n; i++)
            {
                var b = items[i];
                b.puzzle = this;
                b.reachDistance = handReach;
                b.Init(vals[i]);
                b.transform.SetPositionAndRotation(slots[i].position, slots[i].rotation);
            }
            placed = new bool[n];
            todo.Clear();
            pivotLog.Clear();
            pivot = null;
            lo = 0; hi = n - 1;
            wall = cur = lo;
            phase = Phase.Pivot;
            comparisons = swaps = hintsUsed = mistakes = partitions = 0;
            startInversions = CountInversions(vals);
            Solved = false;
            busy = false;
            startTime = Time.time;
            if (Hand != null) Hand.Release();
            Refresh();
        }

        /// The first pivot (the last bar) should be neither among the two smallest nor the two biggest, so the
        /// first split is easy to see.
        static bool MiddlingPivot(int[] v)
        {
            int p = v[v.Length - 1], rank = 0;
            foreach (int x in v) if (x < p) rank++;
            return rank >= 2 && rank <= v.Length - 3;
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            var kb = Keyboard.current;
            var ui = GameUI.Instance;
            bool blocked = ui != null && (ui.PopupOpen || ui.PauseOpen);
            if (kb != null && kb.hKey.wasPressedThisFrame && AcceptsInput && !blocked)
            {
                ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.7f);
                RequestHint();
            }

            // keyboard: P raises / drops the pivot; 1 = smaller, 2 = bigger (walking over first if needed)
            if (kb != null && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed)
            {
                if (kb.pKey.wasPressedThisFrame)
                {
                    if (phase == Phase.Pivot) UseByKey(items[hi]);
                    else if (phase == Phase.Drop && pivot != null) UseByKey(pivot);
                }
                int d = DigitPressed(kb);
                if (phase == Phase.Scan)
                {
                    if (d == 1 && smallerPad != null) UseByKey(smallerPad);
                    if (d == 2 && biggerPad != null) UseByKey(biggerPad);
                }
            }
#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct step
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !blocked) AutoStep();
#endif

            // the pivot glows; its light follows it
            if (pivotLight != null)
            {
                bool on = pivot != null && !Solved;
                if (pivotLight.enabled != on) pivotLight.enabled = on;
                if (on) pivotLight.transform.position = pivot.transform.position + Vector3.up * 0.32f - slots[0].forward * 0.15f;
            }
        }

        public override void OnButton(string id)
        {
            if (!AcceptsInput) return;
            if (id == "hint") { RequestHint(); return; }
            if (id == "smaller") Decide(true);
            else if (id == "bigger") Decide(false);
        }

        public override void OnItemClicked(SortItem item)
        {
            if (!AcceptsInput) return;
            var ui = GameUI.Instance;
            switch (phase)
            {
                case Phase.Pivot:
                    if (item == items[hi]) StartCoroutine(RaisePivot());
                    else if (ui != null) ui.Info($"The vault's rule:  the pivot is the last bar of the range,  {items[hi].value}.");
                    break;
                case Phase.Drop:
                    if (item == pivot) StartCoroutine(DropPivot());
                    break;
                case Phase.Scan:
                    if (ui != null && item == items[cur])
                        ui.Info($"Is {item.value} smaller or bigger than the pivot, {pivot.value}?  Press 1 or 2, or use the two buttons in front of it.");
                    break;
            }
        }

        // ------------------------------------------------------------ quick sort

        Vector3 PivotSpot() => Vector3.Lerp(slots[lo].position, slots[hi].position, 0.5f) + Vector3.up * pivotLift;

        IEnumerator RaisePivot()
        {
            busy = true;
            pivot = items[hi];
            partitions++;
            wall = cur = lo;
            var hand = Hand;
            bool useHand = hand != null && Player != null && Flat(Player.transform.position, pivot.transform.position) < 1.2f;
            if (useHand) hand.Hold(hand.SideFor(pivot.transform.position), pivot.transform, Vector3.up * 0.08f);
            ProceduralSfx.Play(ProceduralSfx.Select, pivot.transform.position, 0.7f);
            Vector3 up = pivot.transform.position + Vector3.up * pivotLift;
            yield return MoveTo(pivot, up, 0.35f);
            if (useHand) hand.Release();
            yield return MoveTo(pivot, PivotSpot(), 0.35f);
            ProceduralSfx.Play(ProceduralSfx.Hum, pivot.transform.position, 0.35f);
            phase = Phase.Scan;
            busy = false;
            Refresh();
            if (GameUI.Instance != null)
                GameUI.Instance.Info($"Pivot {pivot.value}.  Compare every other bar with it, left to right:  smaller goes behind the gate.");
        }

        /// The player's call on the bar being compared: smaller than the pivot (true) or bigger (false).
        void Decide(bool smaller)
        {
            if (!AcceptsInput || phase != Phase.Scan || pivot == null) return;
            var b = items[cur];
            bool isSmaller = b.value < pivot.value;
            if (smaller != isSmaller)
            {
                mistakes++;
                b.Flash(SortItem.ErrorColor, 0.8f);
                pivot.Flash(SortItem.HintColor, 1.2f);
                ProceduralSfx.Play(ProceduralSfx.Error, b.transform.position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                if (GameUI.Instance != null)
                    GameUI.Instance.Flash(isSmaller
                        ? $"{b.value} < {pivot.value}:  it's SMALLER than the pivot.  It goes behind the gate (1)."
                        : $"{b.value} > {pivot.value}:  it's BIGGER than the pivot.  Leave it where it is (2).", Advice, 3.4f);
                Refresh();
                return;
            }
            comparisons++;
            StartCoroutine(smaller ? SmallerRoutine() : BiggerRoutine());
        }

        IEnumerator SmallerRoutine()
        {
            busy = true;
            var b = items[cur];
            var ui = GameUI.Instance;
            if (wall != cur)
            {
                // swap with the first bigger bar, the one just right of the gate
                var c = items[wall];
                swaps++;
                Vector3 back = -slots[0].forward;          // toward the player
                ProceduralSfx.Play(ProceduralSfx.Swap, b.transform.position, 0.6f);
                StartCoroutine(ArcTo(c, slots[cur].position, -back * 0.14f + Vector3.up * 0.05f, 0.45f));
                yield return ArcTo(b, slots[wall].position, back * 0.14f + Vector3.up * 0.08f, 0.45f);
                items[wall] = b;
                items[cur] = c;
                ProceduralSfx.Play(ProceduralSfx.Thud, b.transform.position, 0.5f);
                if (ui != null) ui.Info($"{b.value} < {pivot.value}:  swapped behind the gate with {c.value}.");
            }
            else
            {
                yield return Bob(b);
                if (ui != null) ui.Info($"{b.value} < {pivot.value}:  it's already next to the gate.  The gate moves past it.");
            }
            wall++;
            cur++;
            ProceduralSfx.Play(ProceduralSfx.Slide, gate != null ? gate.position : b.transform.position, 0.3f);
            AfterCompare();
        }

        IEnumerator BiggerRoutine()
        {
            busy = true;
            var b = items[cur];
            yield return Bob(b);
            if (GameUI.Instance != null) GameUI.Instance.Info($"{b.value} > {pivot.value}:  it stays right of the gate.");
            cur++;
            AfterCompare();
        }

        void AfterCompare()
        {
            if (cur >= hi)
            {
                phase = Phase.Drop;
                if (GameUI.Instance != null)
                    GameUI.Instance.Flash($"Every bar is compared.  Drop the pivot into slot {wall + 1}, right of the gate:  click it or press P.", Advice, 4.5f);
            }
            busy = false;
            Refresh();
        }

        /// A little hop, so the player sees which bar was judged.
        IEnumerator Bob(SortItem b)
        {
            Vector3 p = b.transform.position;
            float e = 0f, dur = 0.22f;
            while (e < dur)
            {
                e += Time.deltaTime;
                b.transform.position = p + Vector3.up * (0.035f * Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI));
                yield return null;
            }
            b.transform.position = p;
        }

        IEnumerator DropPivot()
        {
            busy = true;
            int p = wall;
            var ui = GameUI.Instance;
            if (p != hi)
            {
                // the first bar bigger than the pivot moves into the pivot's old slot, behind the others
                var c = items[p];
                swaps++;
                ProceduralSfx.Play(ProceduralSfx.Slide, c.transform.position, 0.5f);
                yield return ArcTo(c, slots[hi].position, slots[0].forward * 0.2f + Vector3.up * 0.06f, 0.5f);
                items[hi] = c;
            }

            // the pivot glides over its slot, then drops in
            var hand = Hand;
            bool useHand = hand != null && Player != null && Flat(Player.transform.position, slots[p].position) < 1.2f;
            Vector3 above = slots[p].position + Vector3.up * pivotLift;
            yield return MoveTo(pivot, above, 0.35f);
            if (useHand) hand.Hold(hand.SideFor(pivot.transform.position), pivot.transform, Vector3.up * 0.08f);
            yield return MoveTo(pivot, slots[p].position, 0.3f);
            if (useHand) hand.Release();
            items[p] = pivot;
            placed[p] = true;
            ProceduralSfx.Play(ProceduralSfx.Thud, slots[p].position, 0.9f);
            ProceduralSfx.Play(ProceduralSfx.Blip(Mathf.Min(7, p)), slots[p].position, 0.6f);

            int left = p - lo, right = hi - p;
            homeValue = pivot.value;
            pivotLog.Add($"{pivot.value} → {left} | {right}");
            if (ui != null)
                ui.Flash($"{pivot.value} is home:  {left} smaller bar{(left == 1 ? "" : "s")} left of it,  {right} bigger right of it.  It never moves again.", SortItem.SortedColor, 4f);

            // the left part is sorted first, so it goes on the stack last
            todo.Push(new Vector2Int(p + 1, hi));
            todo.Push(new Vector2Int(lo, p - 1));
            pivot = null;
            Refresh();
            yield return new WaitForSeconds(0.9f);
            yield return NextRange();
        }

        /// Takes the next range that still needs a pivot. A range of one bar is already in place.
        IEnumerator NextRange()
        {
            busy = true;
            phase = Phase.Pivot;
            var ui = GameUI.Instance;
            while (todo.Count > 0)
            {
                var r = todo.Pop();
                if (r.y < r.x) continue;                 // an empty side
                if (r.x == r.y)
                {
                    placed[r.x] = true;
                    lo = hi = r.x;
                    Refresh();
                    ProceduralSfx.Play(ProceduralSfx.Blip(Mathf.Min(7, r.x)), slots[r.x].position, 0.6f);
                    if (ui != null) ui.Info($"{items[r.x].value} is on its own:  one bar is always in its place.");
                    yield return new WaitForSeconds(0.6f);
                    continue;
                }
                lo = r.x;
                hi = r.y;
                wall = cur = lo;
                phase = Phase.Pivot;
                busy = false;
                Refresh();
                if (ui != null)
                    ui.Info($"Next:  slots {lo + 1}-{hi + 1}.  Raise their pivot, the last bar:  {items[hi].value}.");
                yield break;
            }

            phase = Phase.Done;
            busy = false;
            Solved = true;
            Refresh();
            OnSolved();
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            switch (phase)
            {
                case Phase.Pivot:
                    return $"The pivot is the last bar of the range:  {items[hi].value} (slot {hi + 1}).  Click it or press P to raise it.";
                case Phase.Scan:
                {
                    int v = items[cur].value;
                    return v < pivot.value
                        ? $"{v} < {pivot.value}:  smaller than the pivot.  Press 1:  it goes behind the gate."
                        : $"{v} > {pivot.value}:  bigger than the pivot.  Press 2:  it stays where it is.";
                }
                case Phase.Drop:
                    return $"All compared.  Drop the pivot into slot {wall + 1}, just right of the gate:  click it or press P.";
                default:
                    return "Every bar is in its place.";
            }
        }

        public override void RequestHint()
        {
            if (Solved) return;
            hintsUsed++;
            SortItem show = phase == Phase.Pivot ? items[hi] : phase == Phase.Scan ? items[cur] : pivot;
            if (show != null) show.Flash(SortItem.HintColor, 2.2f);
            if (phase == Phase.Scan && pivot != null)
            {
                var pad = items[cur].value < pivot.value ? smallerPad : biggerPad;
                if (pad != null) StartCoroutine(PulsePad(pad.transform));
            }
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4.5f);
            Refresh();
        }

        IEnumerator PulsePad(Transform t)
        {
            Vector3 s = t.localScale;
            float e = 0f;
            while (e < 1.2f)
            {
                e += Time.deltaTime;
                t.localScale = s * (1f + 0.25f * Mathf.Abs(Mathf.Sin(e * 8f)));
                yield return null;
            }
            t.localScale = s;
        }

        /// Only the bar that matters right now can be clicked: the pivot to raise, or the pivot to drop.
        public override bool CanUse(SortItem item)
        {
            if (!AcceptsInput || item == null) return false;
            if (phase == Phase.Pivot) return item == items[hi];
            if (phase == Phase.Drop) return item == pivot;
            return false;
        }

        public override string PromptFor(SortItem item)
        {
            if (!CanUse(item)) return "";
            return phase == Phase.Pivot ? $"Raise {item.value} as the pivot   [P]" : $"Drop the pivot into slot {wall + 1}   [P]";
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        // ------------------------------------------------------------ auto-play (editor play-testing)

        public bool AutoStep()
        {
            if (!AcceptsInput) return false;
            switch (phase)
            {
                case Phase.Pivot: OnItemClicked(items[hi]); return true;
                case Phase.Scan: Decide(items[cur].value < pivot.value); return true;
                case Phase.Drop: OnItemClicked(pivot); return true;
            }
            return false;
        }

        public Component AutoTarget(bool wrong)
        {
            switch (phase)
            {
                case Phase.Pivot:
                    return wrong ? (hi > lo ? items[lo] : null) : items[hi];
                case Phase.Scan:
                {
                    bool smaller = items[cur].value < pivot.value;
                    return smaller != wrong ? (Component)smallerPad : biggerPad;
                }
                case Phase.Drop:
                    return wrong ? null : pivot;
            }
            return null;
        }

        public string DebugState()
        {
            var sb = new StringBuilder();
            sb.Append(phase).Append($"  range {lo + 1}-{hi + 1}  gate {wall + 1}  cur {cur + 1}  pivot {(pivot != null ? pivot.value.ToString() : "-")}  row [");
            for (int i = 0; i < N; i++)
            {
                if (i > 0) sb.Append(' ');
                if (items[i] == pivot) sb.Append("^^");
                else sb.Append(items[i].value).Append(placed[i] ? "*" : "");
            }
            sb.Append($"]  todo {todo.Count}  cmp {comparisons}  swaps {swaps}  slips {mistakes}");
            return sb.ToString();
        }

        // ------------------------------------------------------------ win

        protected override void OnSolved() { StartCoroutine(SolvedSequence()); }

        IEnumerator SolvedSequence()
        {
            busy = true;
            if (!IsSorted()) Debug.LogWarning("Sort or Stay: quick sort finished but the row is not sorted. " + DebugState());
            Gesture(PlayerController.GestureCheer);
            yield return new WaitForSeconds(0.3f);
            for (int i = 0; i < N; i++)
            {
                items[i].Highlight(SortItem.SortedColor);
                ProceduralSfx.Play(ProceduralSfx.Blip(i), items[i].transform.position, 0.7f);
                yield return new WaitForSeconds(0.12f);
            }

            // the hand wheel spins, then the vault door swings open
            if (vaultWheel != null)
            {
                ProceduralSfx.Play(ProceduralSfx.Rattle, vaultWheel.position, 0.8f);
                Quaternion r0 = vaultWheel.localRotation;
                float e = 0f, dur = 1.6f;
                while (e < dur)
                {
                    e += Time.deltaTime;
                    float k = Mathf.SmoothStep(0f, 1f, e / dur);
                    vaultWheel.localRotation = r0 * Quaternion.Euler(0f, 0f, -540f * k);
                    yield return null;
                }
            }
            yield return new WaitForSeconds(0.2f);
            if (door != null) door.Unlock();

            float seconds = Time.time - startTime;
            GameState.Instance.Record(new LevelResult
            {
                level = 5, algorithm = "Quick sort",
                comparisons = comparisons, swaps = swaps, minSwaps = 0,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{swaps} swaps  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The vault is open.  Walk out:  you're free.");
            yield return new WaitForSeconds(2.2f);

            int n = N;
            int levels = Mathf.RoundToInt(Mathf.Log(n, 2f));
            string compare = "";
            var f4 = GameState.Instance.Get(4);
            if (f4 != null) compare = $"\n\nMerge sort on Floor 4 needed {f4.comparisons} comparisons, but it needed the merge row.  Quick sort sorted the bars where they lay.";
            string body =
                "Raise a pivot, swap every smaller bar behind the gate, drop the pivot in:  it lands in its final place and never moves again.  " +
                "Then do the same to each side.  <b>That's quick sort</b>, divide and conquer without a second row.\n\n" +
                $"Comparisons  <b>{comparisons}</b>      Swaps  <b>{swaps}</b>      Pivots  <b>{partitions}</b>      Slips  <b>{mistakes}</b>      Hints  <b>{hintsUsed}</b>      Time  <b>{GameState.Clock(seconds)}</b>\n\n" +
                $"Your pivots  (smaller | bigger):   {string.Join("     ", pivotLog)}\n\n" +
                $"A pivot near the middle halves the work, so quick sort needs about n·log₂n comparisons ({n * levels} for {n} bars), and it is usually the fastest sort in practice.  " +
                $"A pivot that is the smallest or the biggest leaves one side empty:  on bars that are already in order it falls to n(n−1)/2 = <b>{n * (n - 1) / 2}</b>.  " +
                "That's why real libraries choose their pivot with care." +
                compare;
            if (ui != null) ui.ShowPopup("THAT'S QUICK SORT", body, "Click or press E to close  ·  then walk out through the vault door");
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = N;
            bool partitioning = (phase == Phase.Scan || phase == Phase.Drop) && pivot != null;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                Color c = SortItem.Idle;
                if (Solved) c = SortItem.SortedColor;
                else if (it == pivot) c = PivotColor;
                else if (placed[i]) c = SortItem.SortedColor;
                else if (partitioning && i >= lo && i < hi)
                {
                    if (i < wall) c = SmallerColor;
                    else if (i < cur) c = BiggerColor;
                    else if (i == cur && phase == Phase.Scan) c = SortItem.SelectedColor;
                }
                else if (phase == Phase.Pivot && i == hi) c = NextColor;
                it.Highlight(c);
            }

            // laser gate, range strip and the two choice buttons
            float pitch = Pitch;
            Vector3 right = slots.Length > 1 ? (slots[1].position - slots[0].position).normalized : Vector3.right;
            if (gate != null)
            {
                bool on = partitioning && !Solved;
                if (gate.gameObject.activeSelf != on) gate.gameObject.SetActive(on);
                if (on) gate.position = new Vector3(slots[wall].position.x, gate.position.y, slots[wall].position.z) - right * (pitch * 0.5f);
            }
            if (rangeStrip != null)
            {
                bool on = !Solved && phase != Phase.Done && hi > lo;
                if (rangeStrip.gameObject.activeSelf != on) rangeStrip.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 mid = (slots[lo].position + slots[hi].position) * 0.5f;
                    rangeStrip.position = new Vector3(mid.x, rangeStrip.position.y, mid.z);
                    Vector3 sc = rangeStrip.localScale; sc.x = (hi - lo) * pitch + pitch * 0.9f; rangeStrip.localScale = sc;
                    if (stripMat != null)
                    {
                        Color g = new Color(1f, 0.75f, 0.25f);
                        if (stripMat.HasProperty("_BaseColor")) stripMat.SetColor("_BaseColor", g);
                        if (stripMat.HasProperty("_EmissionColor")) stripMat.SetColor("_EmissionColor", g * 1.3f);
                    }
                }
            }
            if (pads != null)
            {
                bool on = phase == Phase.Scan && !Solved && cur < hi;
                if (pads.gameObject.activeSelf != on) pads.gameObject.SetActive(on);
                if (on)
                {
                    Vector3 s = slots[cur].position;
                    pads.position = new Vector3(s.x, pads.position.y, s.z) + slots[0].forward * padsForward;
                }
            }

            if (GameUI.Instance != null)
                SetHudStats($"COMPARISONS  {comparisons}      SWAPS  {swaps}      HINTS  {hintsUsed}");

            string step = Solved || phase == Phase.Done ? "ALL BARS IN ORDER"
                : phase == Phase.Pivot && hi <= lo ? $"SLOT {lo + 1}   ·   ONE BAR ON ITS OWN:  ALREADY IN PLACE"
                : phase == Phase.Pivot ? $"SLOTS {lo + 1}-{hi + 1}   ·   RAISE THE PIVOT  ({items[hi].value})"
                : pivot == null ? $"{homeValue} IS HOME   ·   NEXT RANGE..."
                : phase == Phase.Scan ? $"PIVOT {pivot.value}   ·   IS {items[cur].value} SMALLER OR BIGGER?"
                : $"PIVOT {pivot.value}   ·   DROP IT INTO SLOT {wall + 1}";
            string status = Solved ? "<color=#1f9d47>VAULT OPEN</color>" : "<color=#d8432f>LOCKED</color>";

            if (board != null)
            {
                var sb = new StringBuilder();
                sb.Append("<b>VAULT LOCK  ·  QUICK SORT</b>\n\n");
                sb.Append(RowLine()).Append("\n\n");
                sb.Append(step).Append('\n');
                sb.Append("TO DO  ").Append(TodoLine()).Append("      ");
                sb.Append($"COMPARISONS  {comparisons}    SWAPS  {swaps}    ").Append(status);
                board.text = sb.ToString();
            }
            if (terminal != null)
            {
                terminal.SetText(
                    "<b>VAULT CONTROL</b>   FLOOR 5\n" +
                    "> Raise the pivot (last bar).\n> Smaller:  behind the gate.\n> Bigger:  leave it.\n> Drop the pivot.  Repeat.\n\n" +
                    $"VAULT   {status}\n" +
                    $"PIVOTS {partitions}   SWAPS {swaps}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }

        string RowLine()
        {
            var sb = new StringBuilder();
            bool partitioning = (phase == Phase.Scan || phase == Phase.Drop) && pivot != null;
            for (int i = 0; i < N; i++)
            {
                if (partitioning && i == wall) sb.Append("<color=#ff4a3a>|</color> ");
                var it = items[i];
                string v = it.value.ToString();
                string col = null;
                if (it == pivot) { v = "__"; col = "#b07cff"; }
                else if (Solved || placed[i]) col = "#3fbf5f";
                else if (partitioning && i >= lo && i < hi)
                    col = i < wall ? "#4f9eff" : i < cur ? "#ff8a66" : i == cur && phase == Phase.Scan ? "#ffc61f" : null;
                else if (i < lo || i > hi) col = "#8a93a0";
                sb.Append(col != null ? $"<color={col}>{v}</color>" : v).Append("  ");
            }
            if (pivot != null) sb.Append($"     pivot <color=#b07cff><b>{pivot.value}</b></color>");
            return sb.ToString();
        }

        string TodoLine()
        {
            if (todo.Count == 0) return "-";
            var sb = new StringBuilder();
            foreach (var r in todo)            // top of the stack first: the next range to sort
            {
                if (r.y < r.x) continue;
                sb.Append(r.x == r.y ? $"[{r.x + 1}] " : $"[{r.x + 1}-{r.y + 1}] ");
            }
            return sb.Length > 0 ? sb.ToString() : "-";
        }
    }
}
