using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 3, the Library. Eight books stand on the return shelf (the almirah), out of order, and the player
    /// insertion-sorts them in place. The next book is pulled out; the player compares it with its left neighbour
    /// and calls SHIFT (the neighbour is bigger: it slides one place right into the gap) or PLACE (drop the book
    /// into the gap). The part of the shelf left of the next book is always sorted.
    public class InsertionPuzzle : SortPuzzleBase
    {
        [Header("Deal")]
        public int[] callNumberPool = { 4, 133, 152, 220, 299, 330, 398, 420, 512, 595, 641, 709, 782, 822, 909, 973 };
        public int minOutOfOrderPairs = 11;

        [Header("Room")]
        public Transform[] shelfSlots;       // shelf positions, left to right
        public InsertSlot[] gaps;            // clickable glow markers; the active one is moved onto the empty shelf position
        public TMP_Text board;
        public Terminal terminal;

        [Header("Hands")]
        [Tooltip("While sorting, the avatar walks this close to the shelf and pulls books out by hand.")]
        public float handReach = 0.6f;

        static readonly Color NextColor = new Color(1f, 0.72f, 0.2f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);

        SortItem[] shelf;                    // the book in each shelf position (null = empty)
        int keyIdx;                          // position of the book being inserted (everything left of it is sorted)
        SortItem key;                        // the book pulled out of the shelf
        int gap = -1;                        // the empty shelf position while the key is out
        int shifts;
        int startInversions;

        int N => items.Count;

        /// The glowing gap can be clicked while a book is pulled out.
        public bool IsHolding => key != null;

        public static string Fmt(int v) => v.ToString("000");

        Vector3 Grip => Vector3.up * 0.14f - Vector3.forward * 0.09f;   // the spine, middle height

        // ------------------------------------------------------------ setup

        void Start() { Setup(); }

        public override void Setup()
        {
            int n = N;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            int[] vals = Pick(callNumberPool, n);
            int guard = 0;
            while (CountInversions(vals) < want && ++guard < 800) vals = Pick(callNumberPool, n);

            // the books start on the shelf, out of order
            shelf = new SortItem[n];
            for (int i = 0; i < n; i++)
            {
                var b = items[i];
                b.puzzle = this;
                b.tintModel = false;
                b.reachDistance = handReach;
                b.Init(vals[i]);
                if (b.label != null) b.label.text = Stacked(vals[i]);
                b.transform.SetPositionAndRotation(shelfSlots[i].position, shelfSlots[i].rotation);
                SetColliders(b, true);
                shelf[i] = b;
            }

            keyIdx = 1;                          // spot 1 on its own is already a sorted section
            key = null;
            gap = -1;
            if (Player != null && Player.Hand != null) Player.Hand.Release();
            comparisons = swaps = shifts = hintsUsed = mistakes = 0;
            startInversions = CountInversions(vals);
            Solved = false;
            busy = false;
            startTime = Time.time;
            ShowGaps();
            Refresh();
        }

        /// Library spine labels stack the digits so they are big enough to read from a step away.
        static string Stacked(int v)
        {
            string s = Fmt(v);
            return s[0] + "\n" + s[1] + "\n" + s[2];
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

            // keyboard: F pulls out the next book; 1-8 use that shelf position
            if (kb != null && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed)
            {
                if (kb.fKey.wasPressedThisFrame && key == null && keyIdx < N) UseByKey(shelf[keyIdx]);
                int d = DigitPressed(kb);
                if (d > 0 && d <= N)
                {
                    int m = d - 1;
                    if (key != null && m == gap) UseByKey(gaps[m]);
                    else if (shelf[m] != null) UseByKey(shelf[m]);
                }
            }
#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct step
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !blocked)
            {
                if (key == null) StartCoroutine(PullOut());
                else Decide(gap > 0 && shelf[gap - 1].value > key.value);
            }
#endif
        }

        public override void OnItemClicked(SortItem book)
        {
            if (!AcceptsInput) return;
            if (key == null)
            {
                if (book == shelf[keyIdx]) StartCoroutine(PullOut());
                return;
            }
            if (book == key) Decide(false);                                  // PLACE it in the gap
            else if (gap > 0 && book == shelf[gap - 1]) Decide(true);        // SHIFT the neighbour right
        }

        /// A glowing gap was clicked: place the pulled-out book there.
        public void TryInsert(int index)
        {
            if (AcceptsInput && key != null && index == gap) Decide(false);
        }

        /// The book glides from in front of the shelf into position m.
        IEnumerator GlideIn(SortItem book, int m)
        {
            Vector3 p0 = book.transform.position;
            Quaternion r0 = book.transform.rotation;
            Vector3 p1 = shelfSlots[m].position;
            Vector3 front = Front(m);
            float e = 0f, dur = 0.45f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, e / dur);
                book.transform.position = Vector3.Lerp(Vector3.Lerp(p0, front, t), Vector3.Lerp(front, p1, t), t);
                book.transform.rotation = Quaternion.Slerp(r0, shelfSlots[m].rotation, Mathf.Clamp01(t * 1.4f));
                yield return null;
            }
            book.transform.SetPositionAndRotation(p1, shelfSlots[m].rotation);
            ProceduralSfx.Play(ProceduralSfx.Swap, p1, 0.55f);
        }

        /// Just in front of shelf position m, on the aisle side, where a pulled-out book waits.
        Vector3 Front(int m) => shelfSlots[m].position - shelfSlots[m].forward * 0.22f + Vector3.up * 0.03f;

        // ------------------------------------------------------------ insertion sort on the shelf

        IEnumerator PullOut()
        {
            busy = true;
            key = shelf[keyIdx];
            shelf[keyIdx] = null;
            gap = keyIdx;
            key.Highlight(SortItem.SelectedColor);
            ProceduralSfx.Play(ProceduralSfx.Page, key.transform.position, 0.7f);
            if (Player != null && Player.Hand != null) Player.Hand.Hold(HandReach.Right, key.transform, Grip);
            yield return MoveTo(key, Front(gap), 0.3f);
            busy = false;
            ShowGaps();
            Refresh();
            if (GameUI.Instance != null)
                GameUI.Instance.Info($"{Fmt(key.value)} is out.  Compare it with {Fmt(shelf[gap - 1].value)} on its left.");
        }

        /// The player's call on the pulled-out book: shift its left neighbour right (true) or place it here (false).
        void Decide(bool shift)
        {
            int k = key.value;
            bool shouldShift = gap > 0 && shelf[gap - 1].value > k;
            if (shift != shouldShift)
            {
                mistakes++;
                var left = gap > 0 ? shelf[gap - 1] : null;
                if (left != null) left.Flash(SortItem.ErrorColor, 0.6f);
                key.Flash(SortItem.ErrorColor, 0.6f);
                ProceduralSfx.Play(ProceduralSfx.Error, key.transform.position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                if (GameUI.Instance != null && left != null)
                    GameUI.Instance.Flash(shift
                        ? $"{Fmt(left.value)} is smaller than {Fmt(k)}:  it stays.  PLACE {Fmt(k)} in the gap."
                        : $"{Fmt(left.value)} is bigger than {Fmt(k)}:  SHIFT it right first (click it).",
                        Advice, 3.4f);
                Refresh();
                return;
            }

            if (gap > 0) comparisons++;
            StartCoroutine(shift ? ShiftRoutine() : PlaceRoutine());
        }

        IEnumerator ShiftRoutine()
        {
            busy = true;
            shifts++;
            swaps = shifts;
            var b = shelf[gap - 1];
            int to = gap;
            shelf[to] = b;
            shelf[gap - 1] = null;
            gap--;
            ShowGaps();
            ProceduralSfx.Play(ProceduralSfx.Page, shelfSlots[to].position, 0.45f);
            StartCoroutine(SlideTo(b, shelfSlots[to], 0.3f));
            yield return MoveTo(key, Front(gap), 0.3f);
            busy = false;
            ShowGaps();
            Refresh();
            if (GameUI.Instance != null)
                GameUI.Instance.Info(gap > 0
                    ? $"{Fmt(b.value)} > {Fmt(key.value)}:  shifted right.  Now compare with {Fmt(shelf[gap - 1].value)}."
                    : $"{Fmt(b.value)} > {Fmt(key.value)}:  shifted right.  Nothing left of the gap:  place {Fmt(key.value)}.");
        }

        IEnumerator PlaceRoutine()
        {
            busy = true;
            var book = key;
            int m = gap;
            yield return GlideIn(book, m);
            shelf[m] = book;
            key = null;
            gap = -1;
            if (Player != null && Player.Hand != null) Player.Hand.Release();
            keyIdx++;
            busy = false;
            ShowGaps();
            Refresh();
            book.Flash(SortItem.SortedColor, 0.6f);

            if (keyIdx >= N)
            {
                Solved = true;
                OnSolved();
                yield break;
            }
            if (GameUI.Instance != null)
                GameUI.Instance.Info($"{Fmt(book.value)} placed.  Spots 1-{keyIdx} are sorted.  Next:  pull out {Fmt(shelf[keyIdx].value)}.");
        }

        IEnumerator SlideTo(SortItem b, Transform target, float dur)
        {
            Vector3 p0 = b.transform.position;
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, e / dur);
                b.transform.position = Vector3.Lerp(p0, target.position, t) + Vector3.up * (0.015f * Mathf.Sin(t * Mathf.PI));
                yield return null;
            }
            b.transform.SetPositionAndRotation(target.position, target.rotation);
        }

        static void SetColliders(SortItem b, bool on)
        {
            foreach (var col in b.GetComponentsInChildren<Collider>(true)) col.enabled = on;
        }

        /// One glowing gap at most: the empty spot left by the pulled-out book.
        void ShowGaps()
        {
            if (gaps == null) return;
            int active = key != null && !busy ? gap : -1;
            for (int m = 0; m < gaps.Length; m++)
            {
                if (gaps[m] == null) continue;
                bool on = m == active && m < shelfSlots.Length;
                if (on) gaps[m].transform.position = shelfSlots[m].position;
                gaps[m].gameObject.SetActive(on);
            }
        }

        public string PromptForSlot(int index)
        {
            return key != null ? $"Place {Fmt(key.value)} here   [{index + 1}]" : "";
        }

        /// Only the books that matter right now can be clicked.
        public override bool CanUse(SortItem book)
        {
            if (!AcceptsInput) return false;
            if (key == null) return keyIdx < N && book == shelf[keyIdx];
            return book == key || (gap > 0 && book == shelf[gap - 1]);
        }

        public override string PromptFor(SortItem book)
        {
            if (!CanUse(book)) return "";
            if (key == null) return $"Pull out {Fmt(book.value)}   [F]";
            if (book == key) return $"PLACE {Fmt(key.value)} in the gap";
            return $"SHIFT {Fmt(book.value)} right";
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (Solved) return "Every book is sorted.";
            if (key == null) return $"Pull out {Fmt(shelf[keyIdx].value)} (spot {keyIdx + 1}).  Everything left of it is already sorted.";
            if (gap == 0) return $"Nothing is left of the gap:  PLACE {Fmt(key.value)} first in line.";
            int l = shelf[gap - 1].value;
            return l > key.value
                ? $"{Fmt(l)} > {Fmt(key.value)}:  SHIFT {Fmt(l)} right (click it)."
                : $"{Fmt(l)} < {Fmt(key.value)}:  PLACE {Fmt(key.value)} in the gap (click it).";
        }

        public override void RequestHint()
        {
            if (Solved) return;
            hintsUsed++;
            SortItem show = key == null ? shelf[keyIdx]
                          : gap > 0 && shelf[gap - 1].value > key.value ? shelf[gap - 1] : key;
            if (show != null) show.Flash(SortItem.HintColor, 2.2f);
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4.5f);
            Refresh();
        }

        // ------------------------------------------------------------ win

        protected override void OnSolved() { StartCoroutine(SolvedSequence()); }

        IEnumerator SolvedSequence()
        {
            busy = true;
            Gesture(PlayerController.GestureCheer);
            yield return new WaitForSeconds(0.3f);
            for (int i = 0; i < N; i++)
            {
                shelf[i].Highlight(SortItem.SortedColor);
                ProceduralSfx.Play(ProceduralSfx.Blip(i), shelf[i].transform.position, 0.7f);
                yield return new WaitForSeconds(0.14f);
            }
            yield return new WaitForSeconds(0.3f);

            if (door != null) door.Unlock();
            float seconds = Time.time - startTime;
            GameState.Instance.Record(new LevelResult
            {
                level = 3, algorithm = "Insertion sort",
                comparisons = comparisons, swaps = shifts, minSwaps = startInversions,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{shifts} shifts  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The glass doors are open.  Walk through them.");
            yield return new WaitForSeconds(1.3f);

            int n = items.Count;
            string body =
                "You sorted the shelf where it stood:  each book was pulled out and walked left, " +
                "bigger books shifted one place right, and it dropped into the gap.  <b>That's insertion sort</b>, the way most people sort a hand of cards.\n\n" +
                $"Shifts  <b>{shifts}</b>        Comparisons  <b>{comparisons}</b>        Slips  <b>{mistakes}</b>        Hints  <b>{hintsUsed}</b>        Time  <b>{GameState.Clock(seconds)}</b>\n\n" +
                $"{shifts} shifts = the {startInversions} out-of-order pairs on the shelf:  every shift fixes exactly one pair.\n" +
                $"Already in order, every book would stay put:  {n - 1} comparisons and zero shifts.  " +
                $"That's why insertion sort is the go-to for lists that are <b>nearly sorted</b>.  In reverse order it needs n(n−1)/2 = {n * (n - 1) / 2} shifts.";
            if (ui != null) ui.ShowPopup("THAT'S INSERTION SORT", body, "Click or press E to close  ·  then walk through the glass doors");
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = N;
            {
                for (int m = 0; m < n; m++)
                {
                    var b = shelf[m];
                    if (b == null) continue;
                    Color c = SortItem.Idle;
                    if (Solved || m < keyIdx || (key != null && m <= keyIdx)) c = SortItem.SortedColor;        // the sorted part
                    if (!Solved && key == null && m == keyIdx) c = NextColor;                                 // pull this one out next
                    if (key != null && gap > 0 && m == gap - 1) c = SortItem.CompareColor;                  // compared with the key
                    b.Highlight(c);
                }
                if (key != null) key.Highlight(SortItem.SelectedColor);
            }

            if (GameUI.Instance != null)
                GameUI.Instance.SetStats($"SHIFTS  {shifts}      COMPARISONS  {comparisons}      HINTS  {hintsUsed}");

            string step = Solved ? "ALL SORTED"
                : key != null ? $"KEY  {Fmt(key.value)}" + (gap > 0 ? $"  vs  {Fmt(shelf[gap - 1].value)}" : "")
                : $"NEXT  {Fmt(shelf[keyIdx].value)}";
            string phase = $"SORTED  {Mathf.Min(keyIdx, n)} / {n}";
            if (board != null)
                board.text = "<b>RETURN SHELF  ·  INSERTION SORT</b>\n" + $"{phase}     {step}";
            if (terminal != null)
            {
                string status = Solved ? "<color=#5dff7a>UNLOCKED</color>" : "<color=#ff6a5a>LOCKED</color>";
                terminal.SetText(
                    "<b>CATALOGUE</b>   FLOOR 3\n" +
                    "> Insertion sort the shelf:\n   pull a book out, shift\n   bigger books right, place it.\n\n" +
                    $"DOORS   {status}\n" +
                    $"{phase}   SHIFTS {shifts}\n" +
                    $"{step}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }
    }
}
