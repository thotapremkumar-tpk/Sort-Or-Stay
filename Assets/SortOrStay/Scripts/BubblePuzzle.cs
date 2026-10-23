using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 1. Six keys on hooks; the player may only swap two neighbours.
    /// Escaping means doing bubble sort by hand.
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

        SortItem selected;
        int startInversions;
        int wrongWaySwaps;
        Material[] ledMats;

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
                items[i].Init(vals[i]);
                items[i].transform.position = slots[i].position;
            }

            startInversions = CountInversions(vals);
            comparisons = swaps = hintsUsed = wrongWaySwaps = mistakes = 0;
            selected = null;
            Solved = false;
            busy = false;
            startTime = Time.time;
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
            // number keys pick the key on that hook (1 = left-most): press two neighbours to swap them
            int d = DigitPressed(kb);
            if (d >= 1 && d <= items.Count && ShortcutsAllowed() && !(kb.leftCtrlKey.isPressed)) UseByKey(items[d - 1]);

#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct swap
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !popup)
            {
                int k = FirstOutOfOrderPair();
                if (k >= 0) { comparisons++; StartCoroutine(SwapRoutine(k, k + 1)); Refresh(); }
            }
#endif
        }

        // ------------------------------------------------------------ the mechanic (as in the guide)

        public override void OnItemClicked(SortItem clicked)
        {
            if (!AcceptsInput) return;

            if (selected == null)
            {
                selected = clicked;
                clicked.Highlight(SortItem.SelectedColor);
                clicked.Kick(25f);
                ProceduralSfx.Play(ProceduralSfx.Select, clicked.transform.position, 0.6f);
                return;
            }

            if (clicked == selected)                       // click the same key again to put it down
            {
                selected.Highlight(SortItem.Idle);
                selected = null;
                return;
            }

            int i = items.IndexOf(selected), j = items.IndexOf(clicked);
            comparisons++;

            if (Mathf.Abs(i - j) != 1)
            {
                clicked.Flash(SortItem.ErrorColor, 0.6f);
                selected.Flash(SortItem.ErrorColor, 0.6f);
                ProceduralSfx.Play(ProceduralSfx.Error, clicked.transform.position, 0.7f);
                mistakes++;
                Gesture(PlayerController.GestureScratch);
                if (GameUI.Instance != null) GameUI.Instance.Flash("Only neighbours can swap.");
            }
            else
            {
                int lo = Mathf.Min(i, j), hi = Mathf.Max(i, j);
                if (items[lo].value < items[hi].value)
                {
                    wrongWaySwaps++;
                    mistakes++;
                    if (GameUI.Instance != null)
                        GameUI.Instance.Flash($"{items[lo].value} and {items[hi].value} were already in order.  Bubble sort only swaps when the left key is bigger.",
                                              new Color(1f, 0.8f, 0.4f), 3.2f);
                }
                StartCoroutine(SwapRoutine(lo, hi));
            }

            selected.Highlight(SortItem.Idle);
            selected = null;
            Refresh();
        }

        IEnumerator SwapRoutine(int i, int j)
        {
            busy = true;
            swaps++;
            (items[i], items[j]) = (items[j], items[i]);

            ProceduralSfx.Play(ProceduralSfx.Swap, slots[i].position, 0.8f);
            Vector3 outward = slots[i].forward;          // into the room, toward the player
            StartCoroutine(ArcTo(items[i], slots[i].position, outward * 0.10f + Vector3.up * 0.04f));
            yield return ArcTo(items[j], slots[j].position, outward * 0.05f - Vector3.up * 0.03f);

            items[i].Kick(-60f);
            items[j].Kick(60f);
            busy = false;
            Refresh();
            CheckWin();
        }

        // ------------------------------------------------------------ hints

        int FirstOutOfOrderPair()
        {
            for (int k = 0; k < items.Count - 1; k++)
                if (items[k].value > items[k + 1].value) return k;
            return -1;
        }

        public override string Hint()
        {
            int k = FirstOutOfOrderPair();
            if (k < 0) return "The keys are already in order.";
            int a = items[k].value, b = items[k + 1].value;
            return $"Look at {a} and {b}:  {a} is bigger but sits on the left.  Swap them.";
        }

        public override void RequestHint()
        {
            int k = FirstOutOfOrderPair();
            if (k < 0) return;
            hintsUsed++;
            items[k].Flash(SortItem.HintColor, 2.2f);
            items[k + 1].Flash(SortItem.HintColor, 2.2f);
            items[k].Kick(30f);
            items[k + 1].Kick(-30f);
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4f);
            Refresh();
        }

        public override string PromptFor(SortItem item)
        {
            if (!AcceptsInput) return "";
            string key = $"   [{items.IndexOf(item) + 1}]";
            if (selected == null) return $"Pick up key {item.value}" + key;
            if (item == selected) return $"Put key {item.value} back" + key;
            int i = items.IndexOf(selected), j = items.IndexOf(item);
            return (Mathf.Abs(i - j) == 1 ? $"Swap {selected.value} with {item.value}" : $"Key {item.value} is not next to {selected.value}") + key;
        }

        // ------------------------------------------------------------ win

        protected override void OnSolved() { StartCoroutine(SolvedSequence()); }

        IEnumerator SolvedSequence()
        {
            busy = true;
            if (selected != null) { selected.Highlight(SortItem.Idle); selected = null; }
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
            GameState.Instance.Record(new LevelResult
            {
                level = 1, algorithm = "Bubble sort",
                comparisons = comparisons, swaps = swaps, minSwaps = startInversions,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{swaps} swaps (fewest {startInversions})  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The door is open.  Walk through it.");
            yield return new WaitForSeconds(1.4f);

            int n = items.Count;
            string extra = wrongWaySwaps > 0
                ? $"\n\n{wrongWaySwaps} of your swaps broke a pair that was already in order.  Each one added two swaps: the mistake and the fix.  Bubble sort never makes that move."
                : swaps == startInversions ? "\n\nYou matched it exactly, just like bubble sort would." : "";
            string body =
                "You compared neighbours and pushed the biggest key to the end each pass.  <b>That's bubble sort.</b>\n\n" +
                $"Your swaps  <b>{swaps}</b>        Fewest possible  <b>{startInversions}</b>\n" +
                $"Comparisons  <b>{comparisons}</b>        Hints  <b>{hintsUsed}</b>        Time  <b>{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}</b>\n\n" +
                $"Why {startInversions}?  The keys started with {startInversions} out-of-order pairs.  Every neighbour swap fixes exactly one, " +
                "so that is the floor, and bubble sort always lands on it." + extra +
                $"\n\nA computer running bubble sort on {n} keys checks up to {n * (n - 1) / 2} neighbour pairs, n(n-1)/2.  Simple, but slow as the list grows.";
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
                GameUI.Instance.SetStats($"SWAPS  {swaps}      COMPARISONS  {comparisons}      HINTS  {hintsUsed}");

            if (terminal != null)
            {
                string status = Solved ? "<color=#5dff7a>UNLOCKED</color>" : "<color=#ff5a4e>LOCKED</color>";
                terminal.SetText(
                    "<b>SECURITY TERMINAL</b>   FLOOR 1\n" +
                    "> ORDER UNLOCKS.\n" +
                    "> Arrange the keys from\n   smallest to largest.\n\n" +
                    $"STATUS   {status}\n" +
                    $"KEYS IN PLACE   {correct}/{n}\n" +
                    $"SWAPS   {swaps}\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }
    }
}
