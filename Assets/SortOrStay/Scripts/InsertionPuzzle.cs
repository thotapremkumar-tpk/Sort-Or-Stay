using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 3, the Library. Eight books wait on the reading table in a fixed order; the return shelf
    /// starts with the first one. The player carries the next book from the table and slides it into
    /// a glowing gap on the shelf; bigger books shift right to make room. The shelf stays sorted the
    /// whole time: that growing sorted section is insertion sort.
    public class InsertionPuzzle : SortPuzzleBase
    {
        [Header("Deal")]
        public int[] callNumberPool = { 4, 133, 152, 220, 299, 330, 398, 420, 512, 595, 641, 709, 782, 822, 909, 973 };
        public int minOutOfOrderPairs = 11;

        [Header("Room")]
        public Transform[] tableSlots;       // where the books stand on the table, in "table order"
        public Transform[] shelfSlots;       // shelf positions, left to right
        public InsertSlot[] gaps;            // insertion gaps 0..n (gap m sits just left of shelf slot m)
        public TMP_Text board;
        public Terminal terminal;

        static readonly Color NextColor = new Color(1f, 0.72f, 0.2f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);

        readonly List<SortItem> table = new List<SortItem>();
        readonly List<SortItem> shelf = new List<SortItem>();
        SortItem held;
        bool flying;
        int shifts;
        int startInversions;

        public bool IsHolding => held != null && !flying;

        public static string Fmt(int v) => v.ToString("000");

        // ------------------------------------------------------------ setup

        void Start() { Setup(); }

        public override void Setup()
        {
            int n = items.Count;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            int[] vals = Pick(callNumberPool, n);
            int guard = 0;
            while (CountInversions(vals) < want && ++guard < 800) vals = Pick(callNumberPool, n);

            table.Clear();
            shelf.Clear();
            for (int i = 0; i < n; i++)
            {
                var b = items[i];
                b.puzzle = this;
                b.tintModel = false;
                b.Init(vals[i]);
                if (b.label != null) b.label.text = Stacked(vals[i]);
                b.transform.SetPositionAndRotation(tableSlots[i].position, tableSlots[i].rotation);
                table.Add(b);
            }

            // the shelf starts with the first book: a sorted section of one
            var first = table[0];
            table.RemoveAt(0);
            shelf.Add(first);
            first.transform.SetPositionAndRotation(shelfSlots[0].position, shelfSlots[0].rotation);

            held = null;
            flying = false;
            comparisons = swaps = shifts = hintsUsed = mistakes = 0;
            startInversions = CountInversions(vals);
            Solved = false;
            busy = false;
            startTime = Time.time;
            ShowGaps(false);
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

            // keyboard: F fetches the next book, 1-9 slides it into that gap (walks over if needed)
            if (kb != null && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed)
            {
                if (kb.fKey.wasPressedThisFrame)
                {
                    if (held == null && table.Count > 0) UseByKey(table[0]);
                    else if (held != null && GameUI.Instance != null) GameUI.Instance.Info($"You're already holding {Fmt(held.value)}.  Press the number of a gap to shelve it.");
                }
                int d = DigitPressed(kb);
                if (d > 0)
                {
                    if (held == null)
                    {
                        if (GameUI.Instance != null) GameUI.Instance.Info("Pick up the next book first:  press F or click it.");
                    }
                    else if (!flying && gaps != null && d - 1 < gaps.Length && d - 1 <= shelf.Count && gaps[d - 1] != null)
                        UseByKey(gaps[d - 1]);
                }
            }
#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct step
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !blocked)
            {
                if (held == null) { if (table.Count > 0) StartCoroutine(PickUp(table[0])); }
                else if (!flying) TryInsert(CorrectGap(held.value));
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

            if (held != null)
            {
                if (ui != null) ui.Info($"You're holding {Fmt(held.value)}.  Aim at a glowing gap on the shelf to slide it in.");
                return;
            }
            if (shelf.Contains(book))
            {
                if (ui != null) ui.Info($"{Fmt(book.value)} is already on the return shelf.");
                return;
            }
            if (table.Count > 0 && book != table[0])
            {
                Gesture(PlayerController.GestureScratch);
                table[0].Flash(SortItem.HintColor, 1.4f);
                if (ui != null) ui.Flash($"Take the books in table order.  Next is {Fmt(table[0].value)}, the left-most one.", Advice, 3.2f);
                return;
            }
            StartCoroutine(PickUp(book));
        }

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
            ShowGaps(true);
            Refresh();
            if (GameUI.Instance != null)
                GameUI.Instance.Info($"Holding {Fmt(book.value)}.  Find where it belongs on the shelf.");
        }

        // ------------------------------------------------------------ inserting

        public void TryInsert(int index)
        {
            if (held == null || flying || !AcceptsInput) return;
            int k = shelf.Count;
            if (index < 0 || index > k) return;

            bool okLeft = index == 0 || shelf[index - 1].value <= held.value;
            bool okRight = index == k || held.value <= shelf[index].value;
            if (!okLeft || !okRight)
            {
                mistakes++;
                if (gaps != null && index < gaps.Length && gaps[index] != null) gaps[index].FlashError();
                ProceduralSfx.Play(ProceduralSfx.Error, shelfSlots[Mathf.Min(index, shelfSlots.Length - 1)].position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                string why = !okLeft
                    ? $"{Fmt(shelf[index - 1].value)} on its left is bigger than {Fmt(held.value)}"
                    : $"{Fmt(shelf[index].value)} on its right is smaller than {Fmt(held.value)}";
                if (GameUI.Instance != null) GameUI.Instance.Flash("That breaks the order:  " + why + ".");
                Refresh();
                return;
            }

            int s = k - index;                  // books that must shift one place right
            shifts += s;
            swaps = shifts;
            comparisons += s + (index > 0 ? 1 : 0);
            var book = held;
            held = null;
            ShowGaps(false);
            StartCoroutine(InsertRoutine(book, index, s));
        }

        IEnumerator InsertRoutine(SortItem book, int index, int shifted)
        {
            busy = true;
            flying = true;
            int k = shelf.Count;

            // shift the bigger books right, starting from the end, like the algorithm does
            for (int j = k - 1; j >= index; j--)
            {
                StartCoroutine(SlideTo(shelf[j], shelfSlots[j + 1], 0.24f));
                ProceduralSfx.Play(ProceduralSfx.Page, shelfSlots[j].position, 0.35f);
                yield return new WaitForSeconds(0.1f);
            }
            if (shifted > 0) yield return new WaitForSeconds(0.2f);

            shelf.Insert(index, book);

            // the book glides from the hands into its gap
            Vector3 p0 = book.transform.position;
            Quaternion r0 = book.transform.rotation;
            Vector3 p1 = shelfSlots[index].position;
            Vector3 front = p1 - shelfSlots[index].forward * 0.25f + Vector3.up * 0.03f;
            float e = 0f, dur = 0.5f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, e / dur);
                book.transform.position = Vector3.Lerp(Vector3.Lerp(p0, front, t), Vector3.Lerp(front, p1, t), t);
                book.transform.rotation = Quaternion.Slerp(r0, shelfSlots[index].rotation, Mathf.Clamp01(t * 1.4f));
                yield return null;
            }
            book.transform.SetPositionAndRotation(p1, shelfSlots[index].rotation);
            ProceduralSfx.Play(ProceduralSfx.Swap, p1, 0.6f);
            SetColliders(book, true);

            flying = false;
            busy = false;
            Refresh();
            book.Flash(SortItem.SortedColor, 0.6f);

            if (table.Count == 0)
            {
                Solved = true;
                OnSolved();
                yield break;
            }
            var ui = GameUI.Instance;
            if (ui != null)
            {
                if (shifted == 0) ui.Info(index == 0 ? "Smallest so far:  it goes first." : "Bigger than everything on the shelf:  it goes on the end.  No shifting.");
                else ui.Info($"{shifted} book{(shifted == 1 ? "" : "s")} shifted right to make room.");
            }
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

        void ShowGaps(bool on)
        {
            if (gaps == null) return;
            for (int m = 0; m < gaps.Length; m++)
                if (gaps[m] != null) gaps[m].gameObject.SetActive(on && m <= shelf.Count);
        }

        int CorrectGap(int v)
        {
            int i = 0;
            while (i < shelf.Count && shelf[i].value <= v) i++;
            return i;
        }

        public string PromptForSlot(int index)
        {
            if (held == null) return "";
            int k = shelf.Count;
            string h = Fmt(held.value);
            string key = $"   [{index + 1}]";
            if (index == 0) return $"Slide {h} in first, before {Fmt(shelf[0].value)}" + key;
            if (index >= k) return $"Put {h} on the end, after {Fmt(shelf[k - 1].value)}" + key;
            return $"Slide {h} between {Fmt(shelf[index - 1].value)} and {Fmt(shelf[index].value)}" + key;
        }

        /// Only books still on the table can be clicked, and only while your hands are empty;
        /// while you hold a book, clicks on the shelf go to the nearest gap instead.
        public override bool CanUse(SortItem book) => AcceptsInput && held == null && table.Contains(book);

        public override string PromptFor(SortItem book)
        {
            if (!AcceptsInput) return "";
            if (held != null) return shelf.Contains(book) ? $"{Fmt(book.value)}  ·  aim at a glowing gap instead" : "";
            if (shelf.Contains(book)) return $"{Fmt(book.value)}  ·  shelved";
            if (table.Count > 0 && book == table[0]) return $"Pick up book {Fmt(book.value)}   [F]";
            return table.Count > 0 ? $"{Fmt(book.value)}  ·  take {Fmt(table[0].value)} first" : "";
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (Solved) return "Every book is shelved.";
            if (held == null)
                return table.Count > 0 ? $"Pick up the next book from the table:  {Fmt(table[0].value)}, the left-most one." : "";
            int g = CorrectGap(held.value);
            if (g == 0) return $"{Fmt(held.value)} is smaller than every shelved book.  It goes first, at the far left.";
            if (g >= shelf.Count) return $"{Fmt(held.value)} is bigger than every shelved book.  It goes on the right end.";
            return $"Walk {Fmt(held.value)} along the shelf from the right:  it stops after {Fmt(shelf[g - 1].value)}, before {Fmt(shelf[g].value)}.";
        }

        public override void RequestHint()
        {
            if (Solved) return;
            hintsUsed++;
            if (held == null) { if (table.Count > 0) table[0].Flash(SortItem.HintColor, 2.2f); }
            else
            {
                int g = CorrectGap(held.value);
                if (gaps != null && g < gaps.Length && gaps[g] != null) gaps[g].Flash(new Color(0.3f, 1f, 0.45f), 2.6f);
            }
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
            for (int i = 0; i < shelf.Count; i++)
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
                "You kept the shelf sorted the whole time and slid each new book into place, shifting the bigger ones right.  " +
                "<b>That's insertion sort</b>, the way most people sort a hand of playing cards.\n\n" +
                $"Shifts  <b>{shifts}</b>        Comparisons  <b>{comparisons}</b>        Slips  <b>{mistakes}</b>        Hints  <b>{hintsUsed}</b>        Time  <b>{GameState.Clock(seconds)}</b>\n\n" +
                $"Your {shifts} shifts equal the {startInversions} out-of-order pairs in the table's order:  every shift fixes exactly one pair.\n" +
                $"If the table had already been in order, every book would just go on the end:  {n - 1} comparisons and zero shifts.  " +
                $"That's why insertion sort is the go-to for lists that are <b>nearly sorted</b>.  In reverse order it needs n(n−1)/2 = {n * (n - 1) / 2} shifts.";
            if (ui != null) ui.ShowPopup("THAT'S INSERTION SORT", body, "Click or press E to close  ·  then walk through the glass doors");
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = items.Count;
            foreach (var b in shelf) b.Highlight(SortItem.SortedColor);
            for (int i = 0; i < table.Count; i++) table[i].Highlight(i == 0 && held == null ? NextColor : SortItem.Idle);
            if (held != null) held.Highlight(SortItem.SelectedColor);

            if (GameUI.Instance != null)
                GameUI.Instance.SetStats($"SHIFTS  {shifts}      COMPARISONS  {comparisons}      HINTS  {hintsUsed}");

            string holding = held != null ? $"IN HAND  {Fmt(held.value)}" : table.Count > 0 ? $"NEXT  {Fmt(table[0].value)}" : "TABLE CLEAR";
            if (board != null)
                board.text = "<b>RETURN SHELF</b>\n" + $"{shelf.Count} / {n} SHELVED     {holding}";
            if (terminal != null)
            {
                string status = Solved ? "<color=#5dff7a>UNLOCKED</color>" : "<color=#ff6a5a>LOCKED</color>";
                terminal.SetText(
                    "<b>CATALOGUE</b>   FLOOR 3\n" +
                    "> Return every book to the\n   shelf, smallest first.\n" +
                    "> One book at a time, in\n   table order.\n\n" +
                    $"DOORS   {status}\n" +
                    $"SHELVED {shelf.Count}/{n}   SHIFTS {shifts}\n" +
                    $"{holding}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }
    }
}
