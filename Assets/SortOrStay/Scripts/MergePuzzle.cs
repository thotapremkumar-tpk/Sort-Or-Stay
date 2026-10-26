using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 4, the Control Room. Eight data blocks stand in the rack row of the merge console, scrambled.
    /// Divide: the SPLIT button halves every run until each block is a run of its own (one block is always sorted).
    /// Conquer: neighbouring runs are merged two at a time. The player keeps taking the smaller of the two FRONT
    /// blocks down into the merge row; when one run is empty the rest of the other follows without a comparison,
    /// and the merged run goes back up into the rack as one longer sorted run. Runs of 1 → 2 → 4 → 8.
    /// The merge row is the extra memory merge sort needs.
    public class MergePuzzle : SortPuzzleBase, IAutoPlayable
    {
        [Header("Deal")]
        public int[] valuePool = { 3, 8, 12, 17, 21, 26, 30, 34, 39, 45, 52, 58, 63, 71, 77, 84, 90, 96 };
        public int minOutOfOrderPairs = 12;

        [Header("Console")]
        [Tooltip("Upper row. Local +X runs left to right; the blocks face local -Z.")]
        public Transform rackRow;
        [Tooltip("Lower row in front of the rack, where merges are written.")]
        public Transform mergeRow;
        public float pitch = 0.36f;          // distance between neighbouring blocks of one run
        public float runGap = 0.22f;         // extra space between two runs
        public Renderer[] runStrips;         // glowing strip under each run (one per possible run)
        public Renderer mergeStrip;          // glowing strip under the merge span
        public TMP_Text board;               // display wall behind the console
        public Terminal terminal;
        public Transform splitButton;        // pulses while splitting is the next step

        [Header("Hands")]
        [Tooltip("The avatar walks this close to a block and moves it by hand. The rack row stands ~0.8 m behind the console's front edge.")]
        public float handReach = 1.1f;
        [Tooltip("First person: the eye stays at least this far from the console, so every block is on screen.")]
        public float firstPersonStandOff = 1.15f;

        struct Run
        {
            public int start, len;
            public Run(int start, int len) { this.start = start; this.len = len; }
            public int End => start + len;
        }

        static readonly Color LeftColor = new Color(0.30f, 0.62f, 1f);
        static readonly Color RightColor = new Color(1f, 0.55f, 0.20f);
        static readonly Color MergedColor = new Color(0.50f, 0.92f, 0.58f);
        static readonly Color StripIdle = new Color(0.62f, 0.66f, 0.72f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);

        List<Run> runs = new List<Run>();
        bool splitting;                      // still dividing
        int pair = -1;                       // index (in runs) of the left run of the merge in progress
        int li, ri;                          // blocks already taken from the left / right run
        readonly List<SortItem> output = new List<SortItem>();   // the merge row, in merged order
        int moves, splits, startInversions;
        Material[] stripMats;
        Material mergeStripMat;
        Vector3 splitButtonRest;

        int N => items.Count;
        static HandReach Hand => PlayerController.Instance != null ? PlayerController.Instance.Hand : null;

        Run LeftRun => runs[pair];
        Run RightRun => runs[pair + 1];
        SortItem LeftFront => pair >= 0 && li < LeftRun.len ? items[LeftRun.start + li] : null;
        SortItem RightFront => pair >= 0 && ri < RightRun.len ? items[RightRun.start + ri] : null;

        /// The front block that should go next (the smaller one, or the only one left).
        SortItem SmallerFront
        {
            get
            {
                var l = LeftFront; var r = RightFront;
                if (l == null) return r;
                if (r == null) return l;
                return l.value <= r.value ? l : r;
            }
        }

        // ------------------------------------------------------------ setup

        void Start()
        {
            if (runStrips != null)
            {
                stripMats = new Material[runStrips.Length];
                for (int i = 0; i < runStrips.Length; i++)
                {
                    if (runStrips[i] == null) continue;
                    stripMats[i] = runStrips[i].material;
                    stripMats[i].EnableKeyword("_EMISSION");
                }
            }
            if (mergeStrip != null) { mergeStripMat = mergeStrip.material; mergeStripMat.EnableKeyword("_EMISSION"); }
            if (splitButton != null) splitButtonRest = splitButton.localScale;

            // first person: stand the eye off the console far enough that the whole rack fits on screen
            if (Player != null && rackRow != null)
            {
                float half = ((N - 1) * pitch + (N - 1) * runGap) * 0.5f;
                Player.SetViewStandOff(rackRow.position, -rackRow.forward, rackRow.right, half, firstPersonStandOff);
            }
            Setup();
        }

        public override void Setup()
        {
            int n = N;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            int[] vals = Pick(valuePool, n);
            int guard = 0;
            while (CountInversions(vals) < want && ++guard < 800) vals = Pick(valuePool, n);

            for (int i = 0; i < n; i++)
            {
                items[i].puzzle = this;
                items[i].reachDistance = handReach;
                items[i].Init(vals[i]);
            }
            runs = new List<Run> { new Run(0, n) };
            splitting = true;
            pair = -1;
            li = ri = 0;
            output.Clear();
            comparisons = swaps = moves = splits = hintsUsed = mistakes = 0;
            startInversions = CountInversions(vals);
            Solved = false;
            busy = false;
            startTime = Time.time;
            if (Hand != null) Hand.Release();
            for (int i = 0; i < n; i++) items[i].transform.position = RackPos(runs, i);
            Refresh();
        }

        // ------------------------------------------------------------ layout

        /// Local x of array position i in a row laid out as these runs: blocks 'pitch' apart, runs 'runGap' apart, centred.
        float LayoutX(List<Run> rs, int i)
        {
            float total = (N - 1) * pitch + (rs.Count - 1) * runGap;
            return -total * 0.5f + i * pitch + RunIndexOf(rs, i) * runGap;
        }

        static int RunIndexOf(List<Run> rs, int i)
        {
            for (int k = 0; k < rs.Count; k++)
                if (i >= rs[k].start && i < rs[k].End) return k;
            return rs.Count - 1;
        }

        Vector3 RackPos(List<Run> rs, int i) => rackRow.TransformPoint(new Vector3(LayoutX(rs, i), 0f, 0f));

        /// The runs as they will be once the merge in progress is finished.
        List<Run> MergedRuns()
        {
            var rs = new List<Run>(runs);
            if (pair >= 0 && pair + 1 < rs.Count)
            {
                rs[pair] = new Run(rs[pair].start, rs[pair].len + rs[pair + 1].len);
                rs.RemoveAt(pair + 1);
            }
            return rs;
        }

        /// Spot in the merge row for array position i, under where the merged run will stand.
        Vector3 MergePos(int i) => mergeRow.TransformPoint(new Vector3(LayoutX(MergedRuns(), i), 0f, 0f));

        bool AllSingles()
        {
            foreach (var r in runs) if (r.len > 1) return false;
            return true;
        }

        int MaxRunLength()
        {
            int m = 0;
            foreach (var r in runs) m = Mathf.Max(m, r.len);
            return m;
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

            // keyboard: Q splits; 1 takes the left front block, 2 the right one (walking over first)
            if (kb != null && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed)
            {
                if (kb.qKey.wasPressedThisFrame) OnButton("split");
                int d = DigitPressed(kb);
                if (d == 1 && LeftFront != null) UseByKey(LeftFront);
                if (d == 2 && RightFront != null) UseByKey(RightFront);
            }
#if UNITY_EDITOR
            // play-testing shortcut (editor only): Ctrl+N performs the next correct step
            if (kb != null && kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame && AcceptsInput && !blocked) AutoStep();
#endif

            // the SPLIT button breathes while it is the thing to press
            if (splitButton != null)
            {
                float k = splitting && AcceptsInput ? 1f + 0.06f * Mathf.Sin(Time.time * 5f) : 1f;
                splitButton.localScale = splitButtonRest * k;
            }
        }

        public override void OnButton(string id)
        {
            if (!AcceptsInput) return;
            if (id == "hint") { RequestHint(); return; }
            if (id != "split") return;
            if (!splitting)
            {
                if (GameUI.Instance != null) GameUI.Instance.Info("Every block already stands alone.  Merge the glowing runs:  take the smaller front block.");
                return;
            }
            StartCoroutine(SplitRoutine());
        }

        public override void OnItemClicked(SortItem item)
        {
            if (!AcceptsInput) return;
            var ui = GameUI.Instance;
            if (splitting)
            {
                if (ui != null) ui.Info("Press SPLIT first (Q):  keep halving until every block stands alone.");
                return;
            }
            if (pair < 0) return;

            var lf = LeftFront; var rf = RightFront;
            if (item == lf || item == rf)
            {
                bool left = item == lf;
                var other = left ? rf : lf;
                if (other != null && other.value < item.value)
                {
                    mistakes++;
                    item.Flash(SortItem.ErrorColor, 0.8f);
                    other.Flash(SortItem.HintColor, 1.4f);
                    ProceduralSfx.Play(ProceduralSfx.Error, item.transform.position, 0.7f);
                    Gesture(PlayerController.GestureScratch);
                    if (ui != null) ui.Flash($"{item.value} > {other.value}:  take the smaller front block,  {other.value}.");
                    Refresh();
                    return;
                }
                StartCoroutine(TakeRoutine(item, left, other != null));
                return;
            }

            if (ui == null) return;
            if (output.Contains(item)) ui.Info("Already merged:  it waits in the merge row.");
            else
            {
                int idx = items.IndexOf(item);
                bool inMerge = idx >= LeftRun.start && idx < RightRun.End;
                ui.Info(inMerge ? "Inside a run the blocks are already in order:  only the two front blocks compete."
                                : "That block waits for a later merge.  Merge the two glowing runs first.");
            }
        }

        // ------------------------------------------------------------ divide

        IEnumerator SplitRoutine()
        {
            busy = true;
            var next = new List<Run>();
            foreach (var r in runs)
            {
                if (r.len <= 1) { next.Add(r); continue; }
                int h = r.len / 2;
                next.Add(new Run(r.start, h));
                next.Add(new Run(r.start + h, r.len - h));
            }
            int before = runs.Count;
            runs = next;
            splits++;
            ProceduralSfx.Play(ProceduralSfx.Slide, rackRow.position, 0.6f);
            yield return Relayout(0.45f);
            ProceduralSfx.Play(ProceduralSfx.Thud, rackRow.position, 0.45f);

            var ui = GameUI.Instance;
            if (AllSingles())
            {
                splitting = false;
                BeginNextMerge();
                busy = false;
                Refresh();
                if (ui != null)
                    ui.Flash("Every block stands alone, and one block is always sorted.\nNow merge them back two runs at a time:  take the <b>smaller front block</b> each time.", Advice, 6.5f);
                yield break;
            }
            busy = false;
            Refresh();
            if (ui != null) ui.Info($"Split:  {before} run{(before == 1 ? "" : "s")} → {runs.Count} runs of {MaxRunLength()}.  Still too long:  split again.");
        }

        // ------------------------------------------------------------ conquer

        /// The next two neighbouring runs to merge: the leftmost run of the shortest length and its right neighbour,
        /// so a whole level is merged (runs of 1 → 2, then 2 → 4...) before the next level starts.
        void BeginNextMerge()
        {
            pair = -1;
            li = ri = 0;
            output.Clear();
            if (runs.Count < 2) return;
            int minLen = int.MaxValue;
            foreach (var r in runs) minLen = Mathf.Min(minLen, r.len);
            int k = 0;
            while (k < runs.Count && runs[k].len != minLen) k++;
            if (k >= runs.Count - 1) k = runs.Count - 2;
            pair = k;
        }

        IEnumerator TakeRoutine(SortItem b, bool fromLeft, bool compared)
        {
            busy = true;
            if (compared) comparisons++;
            yield return MoveDown(b, fromLeft, true);

            // one run is empty: the rest of the other follows, no comparisons needed
            bool leftDone = li >= LeftRun.len, rightDone = ri >= RightRun.len;
            if ((leftDone || rightDone) && !(leftDone && rightDone))
            {
                Refresh();
                if (GameUI.Instance != null)
                    GameUI.Instance.Info($"The {(leftDone ? "left" : "right")} run is empty:  the rest of the {(leftDone ? "right" : "left")} run follows, no comparing needed.");
                yield return new WaitForSeconds(0.35f);
                while (li < LeftRun.len) { yield return MoveDown(items[LeftRun.start + li], true, false); yield return new WaitForSeconds(0.06f); }
                while (ri < RightRun.len) { yield return MoveDown(items[RightRun.start + ri], false, false); yield return new WaitForSeconds(0.06f); }
            }

            if (li >= LeftRun.len && ri >= RightRun.len) yield return FinishMerge();
            if (!Solved)
            {
                busy = false;
                Refresh();
            }
        }

        /// One block goes from the front of its run down into the next free spot of the merge row.
        IEnumerator MoveDown(SortItem b, bool fromLeft, bool byHand)
        {
            int dst = LeftRun.start + li + ri;
            Vector3 to = MergePos(dst);
            moves++;
            if (fromLeft) li++; else ri++;
            output.Add(b);
            Refresh();

            var hand = Hand;
            bool useHand = byHand && hand != null && Player != null && Flat(Player.transform.position, b.transform.position) < 1.2f;
            if (useHand) hand.Hold(hand.SideFor(b.transform.position), b.transform, Grip);
            ProceduralSfx.Play(ProceduralSfx.Swap, b.transform.position, 0.5f);
            yield return ArcTo(b, to, Vector3.up * 0.09f, 0.38f);
            ProceduralSfx.Play(ProceduralSfx.Blip(Mathf.Min(7, output.Count - 1)), to, 0.45f);
            if (useHand) hand.Release();
        }

        Vector3 Grip => Vector3.up * 0.14f - (rackRow != null ? rackRow.forward : Vector3.forward) * 0.1f;

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        /// The merged run replaces the two runs in the rack: it rises back up and every run closes ranks.
        IEnumerator FinishMerge()
        {
            yield return new WaitForSeconds(0.25f);
            int start = LeftRun.start, len = LeftRun.len + RightRun.len;
            for (int k = 0; k < len; k++) items[start + k] = output[k];
            runs[pair] = new Run(start, len);
            runs.RemoveAt(pair + 1);
            pair = -1;
            output.Clear();
            Refresh();
            ProceduralSfx.Play(ProceduralSfx.Slide, rackRow.position, 0.5f);
            yield return Relayout(0.5f);

            if (runs.Count == 1)
            {
                busy = false;
                Solved = true;
                OnSolved();
                yield break;
            }

            int minLen = int.MaxValue;
            foreach (var r in runs) minLen = Mathf.Min(minLen, r.len);
            bool levelDone = minLen >= len;
            BeginNextMerge();
            var ui = GameUI.Instance;
            if (ui == null) yield break;
            if (levelDone)
                ui.Flash($"Every run now holds {len} blocks, each run sorted.\nNext level:  merge runs of {len} into runs of {len * 2}.", Advice, 4.5f);
            else
                ui.Info($"Merged into a sorted run of {len}.  Next:  the two glowing runs.");
        }

        /// Every block glides to its rack spot for the current runs.
        IEnumerator Relayout(float dur)
        {
            int n = N;
            var from = new Vector3[n];
            var to = new Vector3[n];
            for (int i = 0; i < n; i++) { from[i] = items[i].transform.position; to[i] = RackPos(runs, i); }
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur));
                for (int i = 0; i < n; i++)
                    items[i].transform.position = Vector3.Lerp(from[i], to[i], k) + Vector3.up * (0.05f * Mathf.Sin(k * Mathf.PI) * (from[i] - to[i]).magnitude);
                yield return null;
            }
            for (int i = 0; i < n; i++) items[i].transform.position = to[i];
            PlaceStrips();
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (Solved) return "Every block is sorted.";
            if (splitting)
                return runs.Count == 1
                    ? "Press SPLIT (Q):  cut the row in half.  Merge sort divides first, then merges."
                    : $"Press SPLIT again (Q):  runs of {MaxRunLength()} are still too long.  Keep halving until every block stands alone.";
            var lf = LeftFront; var rf = RightFront;
            if (lf != null && rf != null)
            {
                bool leftSmaller = lf.value < rf.value;
                return $"Compare the two front blocks:  {lf.value} (left, blue) and {rf.value} (right, orange).\n" +
                       $"Take the smaller one, {(leftSmaller ? lf.value : rf.value)}:  click it or press {(leftSmaller ? 1 : 2)}.";
            }
            return "One run is empty:  the rest of the other run follows on its own.";
        }

        public override void RequestHint()
        {
            if (Solved) return;
            hintsUsed++;
            var s = splitting ? null : SmallerFront;
            if (s != null) s.Flash(SortItem.HintColor, 2.2f);
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4.5f);
            Refresh();
        }

        /// Only the two front blocks of the merge in progress can be taken.
        public override bool CanUse(SortItem item) => AcceptsInput && !splitting && item != null && (item == LeftFront || item == RightFront);

        public override string PromptFor(SortItem item)
        {
            if (!CanUse(item)) return "";
            return $"Take {item.value} into the merge row   [{(item == LeftFront ? 1 : 2)}]";
        }

        // ------------------------------------------------------------ auto-play (editor play-testing)

        public bool AutoStep()
        {
            if (!AcceptsInput) return false;
            if (splitting) { OnButton("split"); return true; }
            var s = SmallerFront;
            if (s == null) return false;
            OnItemClicked(s);
            return true;
        }

        public Component AutoTarget(bool wrong)
        {
            if (Solved) return null;
            if (splitting) return wrong ? (Component)items[0] : splitButton != null ? splitButton.GetComponentInParent<PushButton>() : null;
            var s = SmallerFront;
            if (!wrong) return s;
            var l = LeftFront; var r = RightFront;
            if (l == null || r == null || l.value == r.value) return null;
            return s == l ? r : l;              // the bigger front block
        }

        public string DebugState()
        {
            var sb = new StringBuilder();
            sb.Append(splitting ? "split" : Solved ? "solved" : "merge").Append("  runs ");
            for (int k = 0; k < runs.Count; k++)
            {
                sb.Append('[');
                for (int i = runs[k].start; i < runs[k].End; i++) sb.Append(i > runs[k].start ? " " : "").Append(items[i].value);
                sb.Append(']');
            }
            sb.Append("  out [");
            for (int i = 0; i < output.Count; i++) sb.Append(i > 0 ? " " : "").Append(output[i].value);
            sb.Append($"]  cmp {comparisons}  moves {moves}  slips {mistakes}");
            return sb.ToString();
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
                items[i].Highlight(SortItem.SortedColor);
                ProceduralSfx.Play(ProceduralSfx.Blip(i), items[i].transform.position, 0.7f);
                yield return new WaitForSeconds(0.12f);
            }
            yield return new WaitForSeconds(0.3f);

            if (door != null) door.Unlock();
            float seconds = Time.time - startTime;
            GameState.Instance.Record(new LevelResult
            {
                level = 4, algorithm = "Merge sort",
                comparisons = comparisons, swaps = moves, minSwaps = 0,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{moves} moves  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The doors are open.  Walk through them.");
            yield return new WaitForSeconds(1.3f);

            int n = N;
            int levels = Mathf.RoundToInt(Mathf.Log(n, 2f));
            int maxCmp = n * levels - n + 1;
            string compare = "";
            var f3 = GameState.Instance.Get(3);
            if (f3 != null) compare = $"\n\nOn Floor 3, insertion sort needed {f3.comparisons} comparisons for 8 books.";
            string body =
                "You split the row until every block stood alone (one block is always sorted), then merged sorted runs two at a time, " +
                "always taking the <b>smaller front block</b>.  <b>That's merge sort:</b>  divide, then conquer.\n\n" +
                $"Comparisons  <b>{comparisons}</b>        Moves  <b>{moves}</b>        Slips  <b>{mistakes}</b>        Hints  <b>{hintsUsed}</b>        Time  <b>{GameState.Clock(seconds)}</b>\n\n" +
                $"Every level moved all {n} blocks once:  log₂{n} = {levels} levels × {n} = <b>{n * levels} moves</b> = n·log₂n, however scrambled the blocks were.  " +
                $"A merge never needs more comparisons than blocks minus one, so merge sort never goes past n·log₂n − n + 1 = <b>{maxCmp}</b> for {n} blocks.  " +
                $"Bubble or selection sort can need n(n−1)/2 = <b>{n * (n - 1) / 2}</b>, and the gap grows fast:  for 1,000 items about 10,000 against 500,000.\n" +
                "The price is the merge row:  merge sort needs a second row (extra memory) to merge into." +
                compare;
            if (ui != null) ui.ShowPopup("THAT'S MERGE SORT", body, "Click or press E to close  ·  then walk through the doors");
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = N;
            var lf = LeftFront; var rf = RightFront;
            for (int i = 0; i < n; i++)
            {
                var it = items[i];
                Color c = SortItem.Idle;
                if (Solved) c = SortItem.SortedColor;
                else if (output.Contains(it)) c = MergedColor;
                else if (it == lf) c = LeftColor;
                else if (it == rf) c = RightColor;
                it.Highlight(c);
            }
            if (!busy) PlaceStrips();

            if (GameUI.Instance != null)
                GameUI.Instance.SetStats($"COMPARISONS  {comparisons}      MOVES  {moves}      HINTS  {hintsUsed}");

            string step = Solved ? "ALL SORTED"
                : splitting ? $"STEP 1   DIVIDE   ·   runs of {MaxRunLength()}   ·   press SPLIT"
                : pair >= 0 ? $"STEP 2   MERGE   ·   runs of {LeftRun.len} + {RightRun.len}  →  {LeftRun.len + RightRun.len}"
                : "STEP 2   MERGE";
            string status = Solved ? "<color=#1f9d47>DOORS OPEN</color>" : "<color=#d8432f>LOCKED</color>";

            if (board != null)
            {
                var sb = new StringBuilder();
                sb.Append("<b>MERGE CONSOLE  ·  MERGE SORT</b>\n\n");
                sb.Append("RACK    ").Append(RackLine()).Append('\n');
                sb.Append("MERGE   ").Append(MergeLine()).Append("\n\n");
                sb.Append(step).Append('\n');
                sb.Append($"COMPARISONS  {comparisons}      MOVES  {moves}      ").Append(status);
                board.text = sb.ToString();
            }
            if (terminal != null)
            {
                terminal.SetText(
                    "<b>MERGE CONTROL</b>   FLOOR 4\n" +
                    "> Split until every block\n   stands alone.  Then merge\n   two runs:  smaller front first.\n\n" +
                    $"DOORS   {status}\n" +
                    $"RUNS {runs.Count}   MOVES {moves}\n" +
                    (splitting ? "NEXT:  SPLIT" : Solved ? "SORTED" : "NEXT:  SMALLER FRONT") + "\n\n" +
                    "<size=80%>[ press HINT or H ]</size>");
            }
        }

        string RackLine()
        {
            var sb = new StringBuilder();
            for (int k = 0; k < runs.Count; k++)
            {
                string col = !Solved && pair >= 0 && k == pair ? "#4f9eff" : !Solved && pair >= 0 && k == pair + 1 ? "#ff8c33" : null;
                if (col != null) sb.Append("<color=").Append(col).Append('>');
                sb.Append("[ ");
                for (int i = runs[k].start; i < runs[k].End; i++)
                {
                    bool gone = output.Contains(items[i]);
                    sb.Append(gone ? "__" : items[i].value.ToString()).Append(' ');
                }
                sb.Append(']');
                if (col != null) sb.Append("</color>");
                sb.Append("  ");
            }
            return sb.ToString();
        }

        string MergeLine()
        {
            if (pair < 0) return "<color=#8a93a0>-</color>";
            var sb = new StringBuilder("<color=#3fbf5f>[ ");
            int total = LeftRun.len + RightRun.len;
            for (int k = 0; k < total; k++) sb.Append(k < output.Count ? output[k].value.ToString() : "__").Append(' ');
            sb.Append("]</color>");
            return sb.ToString();
        }

        /// A glowing strip under every run (blue and orange for the two being merged) and one under the merge span.
        void PlaceStrips()
        {
            if (runStrips != null)
            {
                for (int k = 0; k < runStrips.Length; k++)
                {
                    var s = runStrips[k];
                    if (s == null) continue;
                    bool on = k < runs.Count;
                    if (s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
                    if (!on) continue;
                    var r = runs[k];
                    float x0 = LayoutX(runs, r.start), x1 = LayoutX(runs, r.End - 1);
                    s.transform.localPosition = new Vector3((x0 + x1) * 0.5f, s.transform.localPosition.y, s.transform.localPosition.z);
                    Vector3 sc = s.transform.localScale; sc.x = x1 - x0 + pitch * 0.85f; s.transform.localScale = sc;
                    Color c = Solved ? SortItem.SortedColor : pair >= 0 && k == pair ? LeftColor : pair >= 0 && k == pair + 1 ? RightColor : StripIdle;
                    SetGlow(stripMats != null && k < stripMats.Length ? stripMats[k] : null, c, c == StripIdle ? 0.3f : 1.6f);
                }
            }
            if (mergeStrip != null)
            {
                bool on = pair >= 0 && !Solved;
                if (mergeStrip.gameObject.activeSelf != on) mergeStrip.gameObject.SetActive(on);
                if (on)
                {
                    var rs = MergedRuns();
                    float x0 = LayoutX(rs, LeftRun.start), x1 = LayoutX(rs, RightRun.End - 1);
                    var t = mergeStrip.transform;
                    t.localPosition = new Vector3((x0 + x1) * 0.5f, t.localPosition.y, t.localPosition.z);
                    Vector3 sc = t.localScale; sc.x = x1 - x0 + pitch * 0.85f; t.localScale = sc;
                    SetGlow(mergeStripMat, MergedColor, 1.4f);
                }
            }
        }

        static void SetGlow(Material m, Color c, float strength)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * strength);
        }
    }
}
