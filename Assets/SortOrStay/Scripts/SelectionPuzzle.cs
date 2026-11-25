using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 2, the Storage Room. Seven crates with hidden weights stand on seven scale bays.
    /// Each pass the player weighs the unsorted crates strictly left to right; every new reading is compared
    /// with the lightest crate so far, which stays marked. Then the player picks that lightest crate and the
    /// overhead crane swaps it into the next bay through a temporary spot behind the row.
    /// The scale keeps no memory between passes, exactly like selection sort: n(n-1)/2 comparisons, at most n-1 swaps.
    public class SelectionPuzzle : SortPuzzleBase, IAutoPlayable
    {
        public override string Algorithm => "Selection sort";

        /// Best case for selection sort: already in order. It still weighs every crate on every pass; it only saves the swaps.
        protected override int[] BestDeal(int[] sorted) => (int[])sorted.Clone();

        /// Worst case: the heaviest crate first, the rest in order. Every pass has to swap: n-1 swaps, the most possible.
        protected override int[] WorstDeal(int[] sorted)
        {
            int n = sorted.Length;
            var v = new int[n];
            v[0] = sorted[n - 1];
            for (int i = 1; i < n; i++) v[i] = sorted[i - 1];
            return v;
        }

        /// The first pass should have a swap to do.
        protected override bool AverageOk(int[] v) => MinIndex(v, 0) != 0;

        [Header("Deal")]
        public int[] weightPool = { 3, 4, 6, 7, 9, 11, 12, 14, 17, 19, 22, 25, 28, 31 };
        public int minOutOfOrderPairs = 9;

        [Header("Room")]
        public Renderer[] padLeds;
        public Transform[] padTops;          // scale plates, dip when a crate is weighed
        public TMP_Text[] padReadouts;       // little digital readout per bay
        public Transform trolley;            // moves along the fixed ceiling rail
        public Transform cable;              // unit-length cable hanging from the trolley, scaled in y
        public Transform hook;
        public Terminal terminal;
        public Renderer nextMarker;          // glowing frame on the next bay to fill

        static readonly Color LedOff = new Color(0.2f, 0.2f, 0.22f);
        static readonly Color LedWeighed = new Color(0.2f, 0.75f, 1f);
        static readonly Color LedSorted = new Color(0.25f, 1f, 0.4f);
        static readonly Color LedNext = new Color(1f, 0.62f, 0.1f);
        static readonly Color Advice = new Color(1f, 0.82f, 0.45f);
        static readonly Color MinColor = new Color(1f, 0.55f, 0.1f);   // the lightest crate found so far this pass

        int pass;
        int scanAt;                          // next bay to weigh this pass (left to right)
        int minIdx = -1;                     // lightest crate found so far this pass
        bool[] weighed;
        int weighings;
        int startInversions;
        Material[] ledMats;
        Material nextMat;
        Vector3[] padRest;
        const float RestCableLength = 0.35f;
        const float HookContactOffset = 0.035f;
        const float ParkingDepth = 1.15f;
        float currentCable = RestCableLength;
        Vector3 cableMount;
        Transform reachArm;

        int N => items.Count;

        // ------------------------------------------------------------ setup

        void Start()
        {
            if (padLeds != null)
            {
                ledMats = new Material[padLeds.Length];
                for (int i = 0; i < padLeds.Length; i++)
                {
                    if (padLeds[i] == null) continue;
                    ledMats[i] = padLeds[i].material;
                    ledMats[i].EnableKeyword("_EMISSION");
                }
            }
            if (padTops != null)
            {
                padRest = new Vector3[padTops.Length];
                for (int i = 0; i < padTops.Length; i++) if (padTops[i] != null) padRest[i] = padTops[i].position;
            }
            if (nextMarker != null) { nextMat = nextMarker.material; nextMat.EnableKeyword("_EMISSION"); }
            PrepareCrane();
            Setup();
        }

        public override void Setup()
        {
            int n = N;
            int[] vals = Pick(weightPool, n);
            int guard = 0;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            while ((CountInversions(vals) < want || MinIndex(vals, 0) == 0) && ++guard < 800) vals = Pick(weightPool, n);

            vals = DealFor(vals);
            int minWeight = vals[0], maxWeight = vals[0];
            foreach (int weight in weightPool)
            {
                minWeight = Mathf.Min(minWeight, weight);
                maxWeight = Mathf.Max(maxWeight, weight);
            }
            for (int i = 0; i < n; i++)
            {
                items[i].puzzle = this;
                ResizeCrate(items[i], Mathf.InverseLerp(minWeight, maxWeight, vals[i]));
                items[i].Init(vals[i]);
                items[i].transform.position = slots[i].position;
                if (items[i].label != null) items[i].label.text = "?";
            }
            weighed = new bool[n];
            pass = scanAt = 0;
            minIdx = -1;
            comparisons = swaps = hintsUsed = mistakes = weighings = 0;
            startInversions = CountInversions(vals);
            Solved = false;
            busy = false;
            startTime = Time.time;
            SetCable(RestCableLength);
            Refresh();
        }

        static void ResizeCrate(SortItem crate, float weight)
        {
            if (!crate.TryGetComponent(out BoxCollider box)) return;

            // Bounds in metres. Keep even the largest crate within its 0.84 m scale plate.
            Vector3 size = Vector3.Lerp(new Vector3(0.46f, 0.40f, 0.47f), new Vector3(0.74f, 0.87f, 0.70f), weight);
            crate.transform.localScale = new Vector3(size.x / box.size.x, size.y / box.size.y, size.z / box.size.z);
        }

        static int MinIndex(int[] v, int from)
        {
            int m = from;
            for (int i = from + 1; i < v.Length; i++) if (v[i] < v[m]) m = i;
            return m;
        }

        bool Scanning => scanAt < N;

        // ------------------------------------------------------------ input

        void Update()
        {
            var kb = Keyboard.current;
            var ui = GameUI.Instance;
            bool blocked = ui != null && (ui.PopupOpen || ui.PauseOpen);
            if (kb != null && AcceptsInput && !blocked)
            {
                if (kb.hKey.wasPressedThisFrame) { ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.7f); RequestHint(); }
                if (kb.qKey.wasPressedThisFrame) OnButton("scan");

                // number keys pick the crate on that bay (walks over if needed)
                int d = DigitPressed(kb);
                if (d >= 1 && d <= items.Count && ShortcutsAllowed() && !kb.leftCtrlKey.isPressed) UseByKey(items[d - 1]);
#if UNITY_EDITOR
                // play-testing shortcut (editor only): Ctrl+N performs the next correct step
                if (kb.leftCtrlKey.isPressed && kb.nKey.wasPressedThisFrame)
                {
                    if (Scanning) OnButton("scan");
                    else StartCoroutine(PlaceRoutine(minIdx));
                }
#endif
            }

            // the next bay's frame breathes so the player knows where the lightest crate goes
            if (nextMat != null)
            {
                float k = 1.2f + 0.9f * Mathf.Sin(Time.time * 4f);
                if (nextMat.HasProperty("_EmissionColor")) nextMat.SetColor("_EmissionColor", LedNext * (Solved ? 0f : k));
            }
        }

        public override void OnButton(string id)
        {
            if (!AcceptsInput) return;
            if (id == "hint") { RequestHint(); return; }
            if (id != "scan") return;
            if (!Scanning)
            {
                if (GameUI.Instance != null) GameUI.Instance.Info($"All weighed.  Click the lightest ({items[minIdx].value} kg) to swap it into bay {pass + 1}.");
                return;
            }
            StartCoroutine(ScanAll());
        }

        public override void OnItemClicked(SortItem item)
        {
            if (!AcceptsInput) return;
            int idx = items.IndexOf(item);
            if (idx < 0) return;
            var ui = GameUI.Instance;

            if (idx < pass)
            {
                if (ui != null) ui.Info($"Bay {idx + 1} is done:  that crate is in its final place.");
                return;
            }

            // weighing goes strictly left to right, like the algorithm's loop
            if (Scanning)
            {
                if (idx == scanAt) StartCoroutine(ScanOne(idx, true, 0.34f));
                else if (idx > scanAt)
                {
                    item.Flash(SortItem.ErrorColor, 0.5f);
                    items[scanAt].Flash(SortItem.HintColor, 1.2f);
                    if (ui != null) ui.Flash($"Weigh left to right:  bay {scanAt + 1} is next.", Advice, 3f);
                }
                else if (ui != null) ui.Info($"Already weighed.  Next:  bay {scanAt + 1}.");
                return;
            }

            if (idx != minIdx)
            {
                mistakes++;
                item.Flash(SortItem.ErrorColor, 0.8f);
                items[minIdx].Flash(SortItem.HintColor, 1.2f);
                ProceduralSfx.Play(ProceduralSfx.Error, item.transform.position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                if (ui != null) ui.Flash($"{item.value} kg is not the lightest.  The lightest is {items[minIdx].value} kg on bay {minIdx + 1}.");
                Refresh();
                return;
            }

            StartCoroutine(PlaceRoutine(idx));
        }

        // ------------------------------------------------------------ weighing

        IEnumerator ScanAll()
        {
            busy = true;
            while (Scanning) yield return ScanOne(scanAt, false, 0.2f);
            busy = false;
            Refresh();
        }

        /// Weigh one crate, then compare it with the lightest found so far this pass.
        IEnumerator ScanOne(int i, bool ownBusy, float dur)
        {
            if (ownBusy) busy = true;
            yield return WeighRoutine(i, dur);
            var ui = GameUI.Instance;
            if (minIdx < 0)
            {
                minIdx = i;
                if (ui != null) ui.Info($"{items[i].value} kg:  lightest so far.");
            }
            else
            {
                comparisons++;
                int a = items[i].value, b = items[minIdx].value;
                if (a < b) minIdx = i;
                if (ui != null) ui.Info(a < b ? $"{a} < {b}:  new lightest." : $"{a} > {b}:  {b} kg stays the lightest.");
            }
            scanAt = i + 1;
            if (ownBusy) busy = false;
            Refresh();
            if (!Scanning && ui != null)
                ui.Flash($"Lightest:  {items[minIdx].value} kg on bay {minIdx + 1}.  Click it to swap into bay {pass + 1}.", MinColor, 4f);
        }

        IEnumerator WeighRoutine(int i, float dur)
        {
            weighings++;
            var item = items[i];
            Vector3 basePos = slots[i].position;
            ProceduralSfx.Play(ProceduralSfx.Scan, basePos + Vector3.up * 0.5f, 0.7f);

            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                float d = -0.03f * Mathf.Sin(Mathf.Clamp01(e / dur) * Mathf.PI);
                item.transform.position = basePos + Vector3.up * d;
                if (padTops != null && i < padTops.Length && padTops[i] != null) padTops[i].position = padRest[i] + Vector3.up * d;
                yield return null;
            }
            item.transform.position = basePos;
            if (padTops != null && i < padTops.Length && padTops[i] != null) padTops[i].position = padRest[i];

            weighed[i] = true;
            if (item.label != null) item.label.text = item.value + " kg";
            item.Flash(SortItem.HintColor, 0.35f);
        }

        // ------------------------------------------------------------ the swap (overhead crane)

        IEnumerator PlaceRoutine(int m)
        {
            busy = true;
            int p = pass;
            int n = N;

            if (m != p)
            {
                swaps++;
                yield return CraneSwap(m, p);
                (items[m], items[p]) = (items[p], items[m]);
            }
            else
            {
                if (GameUI.Instance != null) GameUI.Instance.Info($"The lightest is already on bay {p + 1}.  No swap needed.");
                ProceduralSfx.Play(ProceduralSfx.Thud, slots[p].position, 0.6f);
                yield return new WaitForSeconds(0.3f);
            }

            items[p].Highlight(SortItem.SortedColor);
            ProceduralSfx.Play(ProceduralSfx.Blip(p), items[p].transform.position, 0.6f);
            pass++;
            scanAt = pass;
            minIdx = -1;

            // the scale keeps no memory: every crate still unsorted loses its reading
            for (int i = pass; i < n; i++)
            {
                weighed[i] = false;
                if (items[i].label != null) items[i].label.text = "?";
            }

            if (pass == n - 1)
            {
                // one crate left: it must be the heaviest
                int last = n - 1;
                weighed[last] = true;
                if (items[last].label != null) items[last].label.text = items[last].value + " kg";
                yield return new WaitForSeconds(0.35f);
                items[last].Highlight(SortItem.SortedColor);
                pass = n;
            }

            busy = false;
            Refresh();

            if (pass >= n)
            {
                Solved = true;
                OnSolved();
            }
            else if (GameUI.Instance != null)
            {
                if (pass == 1)
                    GameUI.Instance.Flash($"Pass 2.  Weigh the {n - pass} remaining crates again, from left to right.\nPress Q to weigh them all.", Advice, 5.5f);
                else
                    GameUI.Instance.Info($"Pass {pass + 1}:  {n - pass} crates left to weigh.");
            }
        }

        /// Swap through a temporary spot on the floor behind the selected bay, so both crates ride the crane:
        /// tmp = lightest;  bay m = crate from bay p;  bay p = tmp.
        IEnumerator CraneSwap(int m, int p)
        {
            SortItem a = items[m];             // the lightest
            SortItem b = items[p];             // the crate in the bay it belongs in
            var ui = GameUI.Instance;
            Vector3 temp = slots[m].position + Vector3.forward * ParkingDepth;
            temp.y -= 0.08f;                                               // on the floor, not on a scale plate

            if (ui != null) ui.Info($"tmp = {a.value} kg");
            yield return CraneMove(a, slots[m].position, temp);
            if (ui != null) ui.Info($"bay {m + 1} = {b.value} kg");
            yield return CraneMove(b, slots[p].position, slots[m].position);
            if (ui != null) ui.Info($"bay {p + 1} = tmp ({a.value} kg)");
            yield return CraneMove(a, temp, slots[p].position);

            float cableAtEnd = currentCable;
            yield return Phase(0.3f, k => SetCable(Mathf.Lerp(cableAtEnd, RestCableLength, k)));
        }

        /// Align the trolley and its sliding arm above the crate before lowering the hook.
        IEnumerator CraneMove(SortItem crate, Vector3 from, Vector3 to)
        {
            float h = Height(crate);
            float carryY = slots[0].position.y + MaxCrateHeight() + 0.35f;      // clears every crate in the row
            float gripHeight = h + HookContactOffset;

            Vector3 start = cable != null ? cable.position : from;
            ProceduralSfx.Play(ProceduralSfx.Slide, from + Vector3.up * 3f, 0.4f);
            yield return Phase(0.33f, k => AlignCrane(Vector3.Lerp(start, from, k)));
            float hookStartY = hook != null ? hook.position.y : carryY + gripHeight;
            yield return Phase(0.22f, k => SetHookHeight(Mathf.Lerp(hookStartY, from.y + gripHeight, k)));

            // lift
            ProceduralSfx.Play(ProceduralSfx.Swap, from + Vector3.up, 0.55f);
            yield return Phase(0.45f, k =>
            {
                Vector3 pos = new Vector3(from.x, Mathf.Lerp(from.y, carryY, k), from.z);
                crate.transform.position = pos;
                SetHookHeight(pos.y + gripHeight);
            });

            Vector3 d = to - from;
            float flat = new Vector2(d.x, d.z).magnitude;
            yield return Phase(0.45f + 0.16f * flat, k =>
            {
                Vector3 pos = new Vector3(Mathf.Lerp(from.x, to.x, k), carryY, Mathf.Lerp(from.z, to.z, k));
                AlignCrane(pos);
                crate.transform.position = pos;
                SetHookHeight(pos.y + gripHeight);
            });

            // lower and release the crate before raising the empty hook
            yield return Phase(0.45f, k =>
            {
                Vector3 pos = new Vector3(to.x, Mathf.Lerp(carryY, to.y, k), to.z);
                crate.transform.position = pos;
                SetHookHeight(pos.y + gripHeight);
            });
            ProceduralSfx.Play(ProceduralSfx.Thud, to, 0.85f);

            yield return Phase(0.3f, k => SetHookHeight(Mathf.Lerp(to.y, carryY, k) + gripHeight));
        }

        IEnumerator Phase(float dur, System.Action<float> step)
        {
            float e = 0f;
            while (e < dur)
            {
                e += Time.deltaTime;
                step(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(e / dur)));
                yield return null;
            }
            step(1f);
        }

        void PrepareCrane()
        {
            if (trolley == null || cable == null) return;
            cableMount = cable.localPosition;
            var body = trolley.Find("Body");
            if (body == null || !body.TryGetComponent(out MeshFilter mesh) ||
                !body.TryGetComponent(out MeshRenderer renderer)) return;

            var arm = new GameObject("Telescoping Arm", typeof(MeshFilter), typeof(MeshRenderer));
            reachArm = arm.transform;
            reachArm.SetParent(trolley, false);
            arm.GetComponent<MeshFilter>().sharedMesh = mesh.sharedMesh;
            arm.GetComponent<MeshRenderer>().sharedMaterial = renderer.sharedMaterial;
            arm.SetActive(false);
        }

        void AlignCrane(Vector3 target)
        {
            if (trolley == null) return;
            Vector3 position = trolley.position;
            position.x = target.x;
            trolley.position = position;
            if (cable == null) return;

            float extension = trolley.InverseTransformPoint(target).z - cableMount.z;
            cable.localPosition = cableMount + Vector3.forward * extension;
            if (reachArm != null)
            {
                const float thickness = 0.08f;
                reachArm.localPosition = cableMount + new Vector3(0f, thickness * 0.5f, extension * 0.5f);
                reachArm.localScale = new Vector3(0.18f, thickness, Mathf.Abs(extension) + 0.14f);
                reachArm.gameObject.SetActive(Mathf.Abs(extension) > 0.01f);
            }
            SetCable(currentCable);
        }

        void SetCable(float len)
        {
            currentCable = Mathf.Max(0.05f, len);
            if (cable != null)
            {
                Vector3 s = cable.localScale; s.y = currentCable; cable.localScale = s;
            }
            if (hook != null && cable != null) hook.position = cable.position + Vector3.down * currentCable;
        }

        void SetHookHeight(float y)
        {
            if (cable != null) SetCable(cable.position.y - y);
            else if (hook != null) hook.position = new Vector3(hook.position.x, y, hook.position.z);
        }

        static float Height(SortItem it)
        {
            var bc = it.GetComponent<BoxCollider>();
            return bc != null ? bc.size.y * it.transform.lossyScale.y : 0.6f;
        }

        float MaxCrateHeight()
        {
            float h = 0f;
            foreach (var it in items) h = Mathf.Max(h, Height(it));
            return h;
        }

        // ------------------------------------------------------------ hints

        public override string Hint()
        {
            if (pass >= N) return "All crates are sorted.";
            if (Scanning)
                return minIdx < 0
                    ? $"Weigh bay {scanAt + 1} first:  it starts as the lightest so far."
                    : $"Weigh bay {scanAt + 1} next and compare it with the lightest so far, {items[minIdx].value} kg on bay {minIdx + 1}.";
            return $"The lightest crate left is {items[minIdx].value} kg on bay {minIdx + 1}.\nClick it and the crane swaps it into bay {pass + 1}.";
        }

        public override void RequestHint()
        {
            if (pass >= N) return;
            hintsUsed++;
            items[Scanning ? scanAt : minIdx].Flash(SortItem.HintColor, 2.4f);
            ShowHint(Hint(), 4.5f);
            Refresh();
        }

        protected override void RefreshDisplays() => Refresh();

        // ------------------------------------------------------------ auto-play (editor play-testing)

        public bool AutoStep()
        {
            if (!AcceptsInput || pass >= N) return false;
            if (Scanning) StartCoroutine(ScanOne(scanAt, true, 0.3f));
            else StartCoroutine(PlaceRoutine(minIdx));
            return true;
        }

        public Component AutoTarget(bool wrong)
        {
            if (Solved || pass >= N) return null;
            if (Scanning) return wrong ? null : items[scanAt];                      // weigh the next crate
            if (!wrong) return items[minIdx];                                          // send the lightest
            for (int i = pass; i < N; i++) if (i != minIdx) return items[i];           // a crate that is not the lightest
            return null;
        }

        public string DebugState()
        {
            var sb = new System.Text.StringBuilder($"pass {pass + 1}  weigh {(Scanning ? (scanAt + 1).ToString() : "-")}  lightest {(minIdx >= 0 ? items[minIdx].value.ToString() : "-")}  bays [");
            for (int i = 0; i < N; i++) sb.Append(i > 0 ? " " : "").Append(i < pass || weighed[i] ? items[i].value.ToString() : "?").Append(i < pass ? "*" : "");
            sb.Append($"]  swaps {swaps}  weighings {weighings}  slips {mistakes}");
            return sb.ToString();
        }

        /// Crates already in their final bay are done: the pointer skips them.
        public override bool CanUse(SortItem item) => AcceptsInput && items.IndexOf(item) >= pass;

        public override string PromptFor(SortItem item)
        {
            if (!AcceptsInput) return "";
            int idx = items.IndexOf(item);
            if (idx < 0) return "";
            string key = $"   [{idx + 1}]";
            if (idx < pass) return $"Bay {idx + 1}  ·  sorted";
            if (Scanning)
            {
                if (idx == scanAt) return "Weigh this crate" + key;
                return (idx > scanAt ? $"Bay {idx + 1}  ·  weigh left to right" : $"{item.value} kg  ·  weighed") + key;
            }
            if (idx == minIdx) return $"Swap the {item.value} kg crate into bay {pass + 1}" + key;
            return $"{item.value} kg  ·  not the lightest" + key;
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
                yield return new WaitForSeconds(0.15f);
            }
            yield return new WaitForSeconds(0.3f);

            UnlockDoor();
            float seconds = Time.time - startTime;
            Record(new LevelResult
            {
                level = 2, algorithm = "Selection sort",
                comparisons = comparisons, swaps = swaps, minSwaps = 0,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{swaps} swaps  ·  {weighings} weighings  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective(AfterSolveObjective("The loading doors are open.  Walk through them."));
            yield return new WaitForSeconds(1.3f);

            int n = N;
            var floor1 = GameState.Instance.Get(1);
            string compare = floor1 != null
                ? $"\n\nOn Floor 1, bubble sort needed <b>{floor1.swaps}</b> swaps for 6 keys.  Selection sort trades moving for looking:  few swaps, many comparisons.  That's the right trade when moving things is expensive, like heavy crates."
                : "\n\nBubble sort would have shuffled these crates around many more times.  Selection sort trades moving for looking:  few swaps, many comparisons.";
            string body =
                "Every pass you checked <b>every</b> crate that was left, found the lightest, and made <b>one</b> swap to put it in the next bay.  <b>That's selection sort.</b>\n\n" +
                $"Your swaps  <b>{swaps}</b>   (never more than n−1 = {n - 1})        Weighings  <b>{weighings}</b>        Hints  <b>{hintsUsed}</b>        Time  <b>{GameState.Clock(seconds)}</b>\n\n" +
                $"The algorithm's comparisons:  {n - 1}+{n - 2}+…+1 = <b>{comparisons}</b> = n(n−1)/2.  It always does exactly that many, even if the crates start in order, because it never remembers what it has seen." +
                compare;
            if (ui != null) ui.ShowPopup("THAT'S SELECTION SORT", body, DebriefFooter("Click or press E to close  ·  then walk through the loading doors"));
            busy = false;
        }

        // ------------------------------------------------------------ displays

        void Refresh()
        {
            int n = N;
            for (int i = 0; i < n; i++)
            {
                bool sorted = i < pass;
                Color c = sorted ? LedSorted : weighed[i] ? LedWeighed : LedOff;
                if (ledMats != null && i < ledMats.Length && ledMats[i] != null)
                {
                    if (ledMats[i].HasProperty("_BaseColor")) ledMats[i].SetColor("_BaseColor", c);
                    if (ledMats[i].HasProperty("_EmissionColor")) ledMats[i].SetColor("_EmissionColor", (sorted || weighed[i]) ? c * 2.5f : Color.black);
                }
                if (padReadouts != null && i < padReadouts.Length && padReadouts[i] != null)
                    padReadouts[i].text = sorted || weighed[i] ? $"{items[i].value:00.0}" : "--.-";
                if (!sorted && i < items.Count) items[i].Highlight(i == minIdx ? MinColor : SortItem.Idle);
            }

            if (nextMarker != null)
            {
                nextMarker.gameObject.SetActive(pass < n && !Solved);
                if (pass < n)
                {
                    Vector3 p = slots[pass].position;
                    nextMarker.transform.position = new Vector3(p.x, nextMarker.transform.position.y, p.z);
                }
            }

            int weighedNow = 0;
            for (int i = pass; i < n; i++) if (weighed[i]) weighedNow++;

            if (GameUI.Instance != null)
                SetHudStats($"WEIGHINGS  {weighings}      SWAPS  {swaps}      HINTS  {hintsUsed}");

            if (terminal != null)
            {
                terminal.SetText(
                    "<b>SELECTION SORT</b>   FLOOR 2\n" +
                    "Sort the crates from lightest to heaviest.\n" +
                    "Weigh the remaining crates left to right.\nMove the lightest to the next bay.\n\n" +
                    (Solved ? $"ALL {n} BAYS SORTED   SWAPS {swaps}\n\n" :
                        $"PASS {pass + 1}/{n - 1}   NEXT BAY {pass + 1}\nWEIGHED {weighedNow}/{n - pass}   SWAPS {swaps}\n\n") +
                    "<size=80%>[ SCAN = Q ]   [ HINT = H ]</size>");
            }
        }
    }
}
