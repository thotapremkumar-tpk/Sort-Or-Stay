using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 3, the Library. Eight books wait on the reading table.
    /// Phase 1: the player shelves them all on the return shelf (the almirah), left to right in table order, unsorted.
    /// Phase 2: the shelf is insertion-sorted in place. The next book is pulled out; the player compares it with its
    /// left neighbour and calls SHIFT (the neighbour is bigger: it slides one place right into the gap) or PLACE
    /// (drop the book into the gap). The part of the shelf left of the next book is always sorted.
    public class InsertionPuzzle : SortPuzzleBase
    {
        [Header("Deal")]
        public int[] callNumberPool = { 4, 133, 152, 220, 299, 330, 398, 420, 512, 595, 641, 709, 782, 822, 909, 973 };
        public int minOutOfOrderPairs = 11;

        [Header("Room")]
        public Transform[] tableSlots;       // where the books stand on the table, in "table order"
        public Transform[] shelfSlots;       // shelf positions, left to right
        public InsertSlot[] gaps;            // clickable glow markers; the active one is moved onto the empty shelf position
        public TMP_Text board;
        public Terminal terminal;

        [Header("Hands")]
        [Tooltip("While sorting, the avatar walks this close to the shelf and pulls books out by hand.")]
        public float handReach = 0.6f;

        static readonly Color NextColor = new Color(1f, 0.72f, 0.2f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);

        readonly List<SortItem> table = new List<SortItem>();
        SortItem[] shelf;                    // the book in each shelf position (null = empty)
        int shelved;                         // phase 1: books on the shelf so far
        bool sorting;                        // phase 2 has begun
        int keyIdx;                          // phase 2: position of the book being inserted (everything left of it is sorted)
        SortItem key;                        // phase 2: the book pulled out of the shelf
        int gap = -1;                        // phase 2: the empty shelf position while the key is out
        SortItem held;                       // phase 1: the book carried from the table
        bool flying;
        int shifts;
        int startInversions;

        int N => items.Count;

        /// A gap on the shelf can be clicked: a table book is in hand (phase 1) or a book is pulled out (phase 2).
        public bool IsHolding => sorting ? key != null : held != null && !flying;

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

            table.Clear();
            shelf = new SortItem[n];
            for (int i = 0; i < n; i++)
            {
                var b = items[i];
                b.puzzle = this;
                b.tintModel = false;
                b.reachDistance = 0f;
                b.Init(vals[i]);
                if (b.label != null) b.label.text = Stacked(vals[i]);
                b.transform.SetPositionAndRotation(tableSlots[i].position, tableSlots[i].rotation);
                SetColliders(b, true);
                table.Add(b);
            }

            shelved = 0;
            sorting = false;
            keyIdx = 1;
            key = null;
            gap = -1;
            held = null;
            flying = false;
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

            // keyboard: F takes the next book (from the table, or out of the shelf); 1-8 use that shelf position
            if (kb != null && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed)
            {
                if (kb.fKey.wasPressedThisFrame)
                {
                    if (!sorting) { if (held == null && table.Count > 0) UseByKey(table[0]); }
                    else if (key == null && keyIdx < N) UseByKey(shelf[keyIdx]);
                }
                int d = DigitPressed(kb);
                if (d > 0 && d <= N)
                {
                    int m = d - 1;
                    if (!sorting)
                    {
                        if (held == null) { if (ui != null) ui.Info("Pick up the next book first:  press F or click it."); }
                        else if (!flying && m == shelved) UseByKey(gaps[m]);
                    }
                    else if (key != null && m == gap) UseByKey(gaps[m]);
                    else if (shelf[m] != null) UseByKey(shelf[m]);
                }
            }
#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct step
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !blocked)
            {
                if (!sorting)
                {
                    if (held == null) { if (table.Count > 0) StartCoroutine(PickUp(table[0])); }
                    else if (!flying) TryInsert(shelved);
                }
                else if (key == null) StartCoroutine(PullOut());
                else Decide(gap > 0 && shelf[gap - 1].value > key.value);
            }
#endif

            // the next book on the table hovers a little so it is easy to spot
            for (int i = 0; i < table.Count; i++)
            {
                int slot = items.IndexOf(table[i]);
                if (slot < 0 || slot >= tableSlots.Length) continue;
                float lift = (i == 0 && held == null && !Solved) ? 0.03f + 0.02f * Mathf.Sin(Time.time * 3.2f) : 0f;
                table[i].transform.position = tableSlots[slot].position + Vector3.up * lift;
            }
        }

        void LateUpdate()
        {
            if (held == null || flying) return;
            var pc = Player;
            if (pc == null) return;
            Transform hp = pc.HoldPoint;
            float bob = Mathf.Sin(Time.time * (pc.Speed > 0.1f ? 9f : 2f)) * (pc.Speed > 0.1f ? 0.012f : 0.004f);
            held.transform.SetPositionAndRotation(hp.position + Vector3.up * bob, hp.rotation);
        }

        public override void OnItemClicked(SortItem book)
        {
            if (!AcceptsInput) return;
            var ui = GameUI.Instance;

            if (!sorting)
            {
                if (held != null) { if (ui != null) ui.Info($"You're holding {Fmt(held.value)}.  Put it in spot {shelved + 1} on the shelf."); return; }
                if (!table.Contains(book)) return;
                if (book != table[0])
                {
                    Gesture(PlayerController.GestureScratch);
                    table[0].Flash(SortItem.HintColor, 1.4f);
                    if (ui != null) ui.Flash($"Take the books in table order.  Next is {Fmt(table[0].value)}, the left-most one.", Advice, 3.2f);
                    return;
                }
                StartCoroutine(PickUp(book));
                return;
            }

            if (key == null)
            {
                if (book == shelf[keyIdx]) StartCoroutine(PullOut());
                return;
            }
            if (book == key) Decide(false);                                  // PLACE it in the gap
            else if (gap > 0 && book == shelf[gap - 1]) Decide(true);        // SHIFT the neighbour right
        }

        // ------------------------------------------------------------ phase 1: table to shelf, in table order

        IEnumerator PickUp(SortItem book)
        {
            busy = true;
            flying = true;
            held = book;
            table.Remove(book);
            SetColliders(book, false);              // a carried book must not block the player or the aim ray
            book.Highlight(SortItem.SelectedColor);
            ProceduralSfx.Play(ProceduralSfx.Page, book.transform.position, 0.8f);

            Vector3 p0 = book.transform.position;
            Quaternion r0 = book.transform.rotation;
            float e = 0f, dur = 0.42f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, e / dur);
                Transform hp = Player != null ? Player.HoldPoint : transform;
                Vector3 mid = Vector3.Lerp(p0, hp.position, 0.5f) + Vector3.up * 0.18f;
                book.transform.position = Vector3.Lerp(Vector3.Lerp(p0, mid, k), Vector3.Lerp(mid, hp.position, k), k);
                book.transform.rotation = Quaternion.Slerp(r0, hp.rotation, k);
                yield return null;
            }
            flying = false;
            busy = false;
            ShowGaps();
            Refresh();
            if (GameUI.Instance != null) GameUI.Instance.Info($"Holding {Fmt(book.value)}.  Put it in spot {shelved + 1}.");
        }

        /// A glowing gap was clicked.
        public void TryInsert(int index)
        {
            if (!AcceptsInput) return;
            if (sorting) { if (key != null && index == gap) Decide(false); return; }
            if (held == null || flying || index != shelved) return;
            var book = held;
            held = null;
            StartCoroutine(ShelveRoutine(book, index));
        }

        IEnumerator ShelveRoutine(SortItem book, int index)
        {
            busy = true;
            flying = true;
            ShowGaps();
            yield return GlideIn(book, index);
            shelf[index] = book;
            shelved++;
            SetColliders(book, true);
            flying = false;
            busy = false;
            ShowGaps();
            Refresh();

            var ui = GameUI.Instance;
            if (shelved < N)
            {
                if (ui != null) ui.Info($"{shelved} / {N} on the shelf.  Next:  {Fmt(table[0].value)}.");
                yield break;
            }

            // every book is on the shelf, in table order: now sort them where they stand
            sorting = true;
            keyIdx = 1;
            foreach (var b in items) b.reachDistance = handReach;
            Refresh();
            if (ui != null)
            {
                ui.SetObjective("<b>Insertion sort</b> the shelf.\nPull out the glowing book, then compare it with its left neighbour:\n" +
                                "<b>SHIFT</b> the neighbour right if it is bigger (click it),  otherwise <b>PLACE</b> the book (click it or the gap).");
                ui.Flash($"All {N} books are shelved, unsorted.  Now insertion sort:  pull out {Fmt(shelf[1].value)} (spot 2).", Advice, 5f);
            }
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

        // ------------------------------------------------------------ phase 2: insertion sort on the shelf

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

        /// One glowing gap at most: the next free spot (phase 1) or the empty spot left by the pulled-out book (phase 2).
        void ShowGaps()
        {
            if (gaps == null) return;
            int active = sorting ? (key != null && !busy ? gap : -1) : (held != null && !flying ? shelved : -1);
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
            if (sorting) return key != null ? $"Place {Fmt(key.value)} here   [{index + 1}]" : "";
            return held != null ? $"Put {Fmt(held.value)} in spot {index + 1}   [{index + 1}]" : "";
        }

        /// Only the books that matter right now can be clicked.
        public override bool CanUse(SortItem book)
        {
            if (!AcceptsInput) return false;
            if (!sorting) return held == null && table.Contains(book);
            if (key == null) return keyIdx < N && book == shelf[keyIdx];
            return book == key || (gap > 0 && book == shelf[gap - 1]);
        }

        public override string PromptFor(SortItem book)
        {
            if (!CanUse(book)) return "";
            if (!sorting) return book == table[0] ? $"Pick up {Fmt(book.value)}   [F]" : $"{Fmt(book.value)}  ·  take {Fmt(table[0].value)} first";
            if (key == null) return $"Pull out {Fmt(book.value)}   [F]";
            if (book == key) return $"PLACE {Fmt(key.value)} in the gap";
            return $"SHIFT {Fmt(book.value)} right";
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (Solved) return "Every book is sorted.";
            if (!sorting)
                return held == null
                    ? (table.Count > 0 ? $"Pick up the next book from the table:  {Fmt(table[0].value)}, the left-most one." : "")
                    : $"Put {Fmt(held.value)} in the glowing spot {shelved + 1}.  Sorting comes after every book is shelved.";
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
            SortItem show = !sorting ? (held == null && table.Count > 0 ? table[0] : null)
                          : key == null ? shelf[keyIdx]
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
                "You shelved the books in table order, then sorted the shelf where it stood:  each book was pulled out and walked left, " +
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
            if (!sorting)
            {
                for (int i = 0; i < table.Count; i++) table[i].Highlight(i == 0 && held == null ? NextColor : SortItem.Idle);
                for (int m = 0; m < n; m++) if (shelf[m] != null) shelf[m].Highlight(SortItem.Idle);   // shelved, not yet sorted
                if (held != null) held.Highlight(SortItem.SelectedColor);
            }
            else
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
                : !sorting ? (held != null ? $"IN HAND  {Fmt(held.value)}" : table.Count > 0 ? $"NEXT  {Fmt(table[0].value)}" : "")
                : key != null ? $"KEY  {Fmt(key.value)}" + (gap > 0 ? $"  vs  {Fmt(shelf[gap - 1].value)}" : "")
                : $"NEXT  {Fmt(shelf[keyIdx].value)}";
            string phase = !sorting ? $"SHELVED  {shelved} / {n}" : $"SORTED  {Mathf.Min(keyIdx, n)} / {n}";
            if (board != null)
                board.text = (!sorting ? "<b>RETURN SHELF</b>\n" : "<b>INSERTION SORT</b>\n") + $"{phase}     {step}";
            if (terminal != null)
            {
                string status = Solved ? "<color=#5dff7a>UNLOCKED</color>" : "<color=#ff6a5a>LOCKED</color>";
                terminal.SetText(
                    "<b>CATALOGUE</b>   FLOOR 3\n" +
                    "> 1. Shelve every book in\n   table order.\n" +
                    "> 2. Insertion sort the shelf:\n   shift bigger books right.\n\n" +
                    $"DOORS   {status}\n" +
                    $"{phase}   SHIFTS {shifts}\n" +
                    $"{step}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }
    }
}
