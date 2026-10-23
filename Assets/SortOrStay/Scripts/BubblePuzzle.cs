using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 1. Six keys on hooks, sorted the way bubble sort does it: pass after pass, the room points at
    /// one pair of neighbours at a time, left to right. The player picks up a key of that pair and decides:
    /// click its neighbour to swap them (the left one is bigger) or click the same key again to let them stay.
    /// At the end of each pass the biggest unsorted key has bubbled to the right and is locked in place.
    public class BubblePuzzle : SortPuzzleBase
    {
        [Header("Deal")]
        public bool useFixedOrder = false;
        public int[] fixedOrder = { 5, 2, 6, 1, 4, 3 };
        [Tooltip("A random deal is redealt until it has at least this many out-of-order pairs.")]
        public int minOutOfOrderPairs = 7;

        [Header("Room")]
        public Terminal terminal;
        public Renderer[] slotLeds;          // panel above the door: lights when a hook holds its final key
        public Color ledOn = new Color(0.25f, 1f, 0.4f);
        public Color ledOff = new Color(0.05f, 0.05f, 0.05f);

        [Header("Hand and camera")]
        [Tooltip("The avatar walks this close to a key and takes it by hand.")]
        public float handReach = 0.55f;
        [Tooltip("Within this distance of the rack the camera keeps all keys in view.")]
        public float frameRange = 2.8f;

        SortItem selected;
        int startInversions;
        int pass, cmp;                       // bubble sort cursor: pass number and left index of the pair being compared
        int passSwaps;
        Material[] ledMats;
        Bounds keyBox;
        bool framing;
        Coroutine lift;                      // the picked-up key sliding off / back onto its hook

        int N => items.Count;
        static HandReach Hand => PlayerController.Instance != null ? PlayerController.Instance.Hand : null;

        void Start()
        {
            if (slotLeds != null)
            {
                ledMats = new Material[slotLeds.Length];
                for (int i = 0; i < slotLeds.Length; i++)
                {
                    if (slotLeds[i] == null) continue;
                    ledMats[i] = slotLeds[i].material;
                    ledMats[i].EnableKeyword("_EMISSION");
                }
            }

            // every key, its tag and the SMALLEST / LARGEST labels: what the camera keeps in view at the rack
            keyBox = new Bounds(slots[0].position, Vector3.zero);
            foreach (var s in slots)
            {
                keyBox.Encapsulate(s.position + Vector3.up * 0.35f);
                keyBox.Encapsulate(s.position + Vector3.down * 0.42f);
            }
            keyBox.Expand(new Vector3(0.3f, 0.06f, 0.2f));
            Setup();
        }

        public override void Setup()
        {
            int n = items.Count;
            int[] vals = new int[n];
            for (int i = 0; i < n; i++) vals[i] = i + 1;

            if (useFixedOrder && fixedOrder != null && fixedOrder.Length == n)
            {
                vals = (int[])fixedOrder.Clone();
            }
            else
            {
                int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
                int guard = 0;
                do { Shuffle(vals); } while (CountInversions(vals) < want && ++guard < 500);
            }

            for (int i = 0; i < n; i++)
            {
                items[i].puzzle = this;
                items[i].reachDistance = handReach;
                items[i].Init(vals[i]);
                items[i].transform.position = slots[i].position;
            }

            startInversions = CountInversions(vals);
            comparisons = swaps = hintsUsed = mistakes = 0;
            pass = cmp = passSwaps = 0;
            selected = null;
            if (Hand != null) Hand.Release();
            Solved = false;
            busy = false;
            startTime = Time.time;
            ShowCursor();
            Refresh();
        }

        void Update()
        {
            var kb = Keyboard.current;
            bool popup = GameUI.Instance != null && GameUI.Instance.PopupOpen;
            if (kb != null && kb.hKey.wasPressedThisFrame && AcceptsInput && !popup)
            {
                ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.7f);
                RequestHint();
            }
            // number keys pick the key on that hook (1 = left-most): the same number again = stay, its neighbour = swap
            int d = DigitPressed(kb);
            if (d >= 1 && d <= items.Count && ShortcutsAllowed() && !(kb.leftCtrlKey.isPressed)) UseByKey(items[d - 1]);

            var pc = Player;
            if (pc != null)
            {
                // keep every key on screen while the player works at the rack (first and third person)
                Vector3 off = pc.transform.position - keyBox.center;
                off.y = 0f;
                bool want = !Solved && inputEnabled && off.magnitude < frameRange;
                if (want) { pc.FrameBounds(keyBox); framing = true; }
                else if (framing) { pc.ClearFrame(); framing = false; }

                // walked away holding a key: hang it back up
                if (selected != null && !busy && Vector3.Distance(pc.Chest, selected.transform.position) > 1.6f) PutDown();
            }

#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N makes the next correct bubble sort move
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !popup)
            {
                if (selected != items[cmp] && selected != items[cmp + 1]) { PutDown(); Select(items[cmp]); }
                Decide(items[cmp].value > items[cmp + 1].value);
            }
#endif
        }

        // ------------------------------------------------------------ the mechanic: one compare at a time

        bool InPair(int idx) => idx == cmp || idx == cmp + 1;
        bool IsFinal(int idx) => idx >= N - pass;          // already bubbled into place by an earlier pass

        Vector3 Grip => Vector3.down * 0.09f + slots[0].forward * 0.02f;

        /// Hook 1 is on the player's left: the left key of a pair goes in the left hand, the right key in the right.
        static int HandFor(int idx, int pairLeft) => idx == pairLeft ? HandReach.Left : HandReach.Right;

        public override void OnItemClicked(SortItem clicked)
        {
            if (!AcceptsInput) return;
            int idx = items.IndexOf(clicked);

            if (selected == null)
            {
                if (InPair(idx)) Select(clicked); else NotThisPair(clicked, idx);
                return;
            }
            if (clicked == selected) { Decide(false); return; }          // same key again: they stay
            if (InPair(idx)) Decide(true);                               // its neighbour in the pair: swap
            else NotThisPair(clicked, idx);
        }

        void Select(SortItem key)
        {
            selected = key;
            key.Highlight(SortItem.SelectedColor);
            key.Kick(25f);
            ProceduralSfx.Play(ProceduralSfx.Select, key.transform.position, 0.6f);
            int i = items.IndexOf(key);
            if (Hand != null) Hand.Hold(HandFor(i, cmp), key.transform, Grip);
            Lift(key, slots[i].position + slots[i].forward * 0.07f + Vector3.up * 0.03f);
        }

        void PutDown()
        {
            if (selected == null) return;
            int i = items.IndexOf(selected);
            Lift(selected, slots[i].position);
            selected = null;
            if (Hand != null) Hand.Release();
            ShowCursor();
        }

        void Lift(SortItem key, Vector3 to)
        {
            if (lift != null) StopCoroutine(lift);
            lift = StartCoroutine(MoveTo(key, to, 0.22f));
        }

        void NotThisPair(SortItem clicked, int idx)
        {
            clicked.Flash(SortItem.ErrorColor, 0.5f);
            items[cmp].Kick(30f);
            items[cmp + 1].Kick(-30f);
            ProceduralSfx.Play(ProceduralSfx.Error, clicked.transform.position, 0.4f);
            if (GameUI.Instance == null) return;
            if (IsFinal(idx))
                GameUI.Instance.Flash($"Key {clicked.value} already bubbled to its final place.  Compare the glowing pair.", SortItem.CompareColor, 3f);
            else
                GameUI.Instance.Flash($"Bubble sort compares one pair at a time, left to right.  Now: hooks {cmp + 1} and {cmp + 2}  (keys {items[cmp].value} and {items[cmp + 1].value}).",
                                      SortItem.CompareColor, 3.2f);
        }

        /// The player's call on the current pair: swap (true) or stay (false).
        void Decide(bool swap)
        {
            int a = items[cmp].value, b = items[cmp + 1].value;
            if (swap != a > b)
            {
                mistakes++;
                items[cmp].Flash(SortItem.ErrorColor, 0.6f);
                items[cmp + 1].Flash(SortItem.ErrorColor, 0.6f);
                ProceduralSfx.Play(ProceduralSfx.Error, items[cmp].transform.position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                if (GameUI.Instance != null)
                    GameUI.Instance.Flash(swap
                        ? $"{a} is smaller than {b}: they are already in order.  Click the key you're holding again to let them stay."
                        : $"{a} is bigger than {b} and sits on the left.  Swap them: click the other glowing key.",
                        new Color(1f, 0.8f, 0.4f), 3.4f);
                Refresh();
                return;                                                  // still holding the key: decide again
            }

            comparisons++;
            if (swap) StartCoroutine(SwapRoutine(cmp, cmp + 1));
            else StartCoroutine(StayRoutine(a, b));
            Refresh();
        }

        IEnumerator StayRoutine(int a, int b)
        {
            busy = true;
            ProceduralSfx.Play(ProceduralSfx.Blip(cmp), items[cmp].transform.position, 0.6f);
            if (GameUI.Instance != null) GameUI.Instance.Info($"{a} < {b}:  they stay.");
            var key = selected;
            int i = items.IndexOf(key);
            selected = null;
            key.Highlight(SortItem.Idle);
            if (lift != null) StopCoroutine(lift);
            yield return MoveTo(key, slots[i].position, 0.22f);
            if (Hand != null) Hand.Release();
            key.Kick(-20f);
            busy = false;
            Advance();
        }

        IEnumerator SwapRoutine(int i, int j)
        {
            busy = true;
            swaps++;
            passSwaps++;
            selected = null;
            if (lift != null) StopCoroutine(lift);
            SortItem leftKey = items[i], rightKey = items[j];

            // one key in each hand: the second hand reaches for its key and lifts it too
            if (Hand != null)
            {
                Hand.Hold(HandReach.Left, leftKey.transform, Grip);
                Hand.Hold(HandReach.Right, rightKey.transform, Grip);
            }
            leftKey.Highlight(SortItem.SelectedColor);
            rightKey.Highlight(SortItem.SelectedColor);
            Vector3 outward = slots[i].forward;          // into the room, toward the player
            StartCoroutine(MoveTo(leftKey, slots[i].position + outward * 0.07f + Vector3.up * 0.03f, 0.25f));
            yield return MoveTo(rightKey, slots[j].position + outward * 0.07f + Vector3.up * 0.03f, 0.25f);

            // cross them over: the left hand passes in front, the right hand behind
            (items[i], items[j]) = (items[j], items[i]);
            ProceduralSfx.Play(ProceduralSfx.Swap, slots[i].position, 0.8f);
            StartCoroutine(ArcTo(rightKey, slots[i].position, outward * 0.03f - Vector3.up * 0.03f, 0.55f));
            yield return ArcTo(leftKey, slots[j].position, outward * 0.12f + Vector3.up * 0.04f, 0.55f);
            if (Hand != null) Hand.Release();
            leftKey.Highlight(SortItem.CompareColor);
            rightKey.Highlight(SortItem.CompareColor);

            items[i].Kick(-60f);
            items[j].Kick(60f);
            busy = false;
            Advance();
            CheckWin();
        }

        /// Move the compare cursor one pair right; at the sorted tail the pass ends and the next one starts.
        void Advance()
        {
            cmp++;
            if (cmp >= N - 1 - pass)
            {
                int fin = N - 1 - pass;
                items[fin].Kick(35f);
                ProceduralSfx.Play(ProceduralSfx.Blip(5), items[fin].transform.position, 0.7f);
                if (GameUI.Instance != null && !IsSorted())
                    GameUI.Instance.Flash($"Pass {pass + 1} done:  {items[fin].value} bubbled to the end and is locked in place.  Pass {pass + 2} starts from the left.",
                                          SortItem.SortedColor, 3.4f);
                pass++;
                cmp = 0;
                passSwaps = 0;
                if (pass >= N - 1) pass = N - 1;
            }
            ShowCursor();
            Refresh();
        }

        /// Colours: the pair being compared glows, keys a pass has fixed in place are green.
        void ShowCursor()
        {
            for (int i = 0; i < N; i++)
            {
                if (items[i] == selected) continue;
                items[i].Highlight(IsFinal(i) ? SortItem.SortedColor : InPair(i) && !Solved ? SortItem.CompareColor : SortItem.Idle);
            }
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (IsSorted()) return "The keys are already in order.";
            int a = items[cmp].value, b = items[cmp + 1].value;
            return a > b
                ? $"Pass {pass + 1}:  compare {a} and {b}.  {a} is bigger and on the left, so swap them."
                : $"Pass {pass + 1}:  compare {a} and {b}.  {a} is smaller, so they stay:  pick one up and click it again.";
        }

        public override void RequestHint()
        {
            if (IsSorted()) return;
            hintsUsed++;
            items[cmp].Flash(SortItem.HintColor, 2.2f);
            items[cmp + 1].Flash(SortItem.HintColor, 2.2f);
            items[cmp].Kick(30f);
            items[cmp + 1].Kick(-30f);
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4f);
            Refresh();
        }

        public override string PromptFor(SortItem item)
        {
            if (!AcceptsInput) return "";
            int idx = items.IndexOf(item);
            string key = $"   [{idx + 1}]";
            if (IsFinal(idx)) return $"Key {item.value} is in its final place" + key;
            if (!InPair(idx)) return $"Key {item.value}: not compared yet  (the glowing pair is next)" + key;
            if (selected == null) return $"Pick up key {item.value} to compare {items[cmp].value} and {items[cmp + 1].value}" + key;
            if (item == selected) return $"Stay:  keep {items[cmp].value} before {items[cmp + 1].value}" + key;
            return $"Swap {selected.value} with {item.value}" + key;
        }

        // ------------------------------------------------------------ win

        protected override void OnSolved() { StartCoroutine(SolvedSequence()); }

        IEnumerator SolvedSequence()
        {
            busy = true;
            if (selected != null) { selected.Highlight(SortItem.Idle); selected = null; }
            if (Hand != null) Hand.Release();
            Gesture(PlayerController.GestureCheer);
            yield return new WaitForSeconds(0.25f);

            // keys glow green, left to right
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Highlight(SortItem.SortedColor);
                items[i].Kick(40f);
                ProceduralSfx.Play(ProceduralSfx.Blip(i), items[i].transform.position, 0.7f);
                yield return new WaitForSeconds(0.17f);
            }
            yield return new WaitForSeconds(0.3f);

            if (door != null) door.Unlock();
            float seconds = Time.time - startTime;
            int passes = pass + (cmp > 0 ? 1 : 0);
            GameState.Instance.Record(new LevelResult
            {
                level = 1, algorithm = "Bubble sort",
                comparisons = comparisons, swaps = swaps, minSwaps = startInversions,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{swaps} swaps (fewest {startInversions})  ·  {comparisons} comparisons in {passes} passes"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The door is open.  Walk through it.");
            yield return new WaitForSeconds(1.4f);

            int n = items.Count;
            string extra = mistakes > 0
                ? $"\n\n{mistakes} of your calls were wrong (a swap of keys already in order, or a stay when the left key was bigger).  Bubble sort never makes those moves."
                : "\n\nEvery call was right, exactly what bubble sort would do.";
            string body =
                "Pass after pass you compared neighbours from left to right: swap when the left key is bigger, otherwise let them stay.  " +
                "Each pass pushed the biggest remaining key to the end.  <b>That's bubble sort.</b>\n\n" +
                $"Passes  <b>{passes}</b>        Comparisons  <b>{comparisons}</b>        Swaps  <b>{swaps}</b>  (fewest possible  <b>{startInversions}</b>)\n" +
                $"Hints  <b>{hintsUsed}</b>        Time  <b>{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}</b>\n\n" +
                $"Why {startInversions} swaps?  The keys started with {startInversions} out-of-order pairs and every neighbour swap fixes exactly one." + extra +
                $"\n\nA computer running bubble sort on {n} keys makes up to {n - 1} passes and {n * (n - 1) / 2} comparisons, n(n-1)/2, " +
                "and one last pass with no swaps to know it is done.  Simple, but slow as the list grows.";
            if (ui != null) ui.ShowPopup("THAT'S BUBBLE SORT", body, "Click or press E to close  ·  then walk through the door");
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = items.Count;
            int[] sortedVals = CurrentValues();
            System.Array.Sort(sortedVals);
            int correct = 0;
            for (int i = 0; i < n; i++)
            {
                bool ok = items[i].value == sortedVals[i];
                if (ok) correct++;
                if (ledMats != null && i < ledMats.Length && ledMats[i] != null)
                {
                    Color c = ok ? ledOn : ledOff;
                    if (ledMats[i].HasProperty("_BaseColor")) ledMats[i].SetColor("_BaseColor", c);
                    if (ledMats[i].HasProperty("_EmissionColor")) ledMats[i].SetColor("_EmissionColor", ok ? ledOn * 3f : Color.black);
                }
            }

            if (GameUI.Instance != null)
                GameUI.Instance.SetStats(Solved
                    ? $"SWAPS  {swaps}      COMPARISONS  {comparisons}      HINTS  {hintsUsed}"
                    : $"PASS  {pass + 1}      COMPARING  {items[cmp].value} · {items[cmp + 1].value}      SWAPS  {swaps}      COMPARISONS  {comparisons}      HINTS  {hintsUsed}");

            if (terminal != null)
            {
                string status = Solved ? "<color=#5dff7a>UNLOCKED</color>" : "<color=#ff5a4e>LOCKED</color>";
                terminal.SetText(
                    "<b>SECURITY TERMINAL</b>   FLOOR 1\n" +
                    "> ORDER UNLOCKS.\n" +
                    "> Arrange the keys from\n   smallest to largest.\n" +
                    $"STATUS   {status}\n" +
                    (Solved ? "" : $"PASS {pass + 1}   COMPARE  {cmp + 1}-{cmp + 2}\n") +
                    $"KEYS IN PLACE   {correct}/{n}\n" +
                    $"SWAPS   {swaps}   COMPARES   {comparisons}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }
    }
}
