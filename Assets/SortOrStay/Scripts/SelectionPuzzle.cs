using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 2, the Storage Room. Seven crates with hidden weights stand on seven scale bays.
    /// Each pass the player weighs the unsorted crates strictly left to right; every new reading is compared
    /// with the lightest crate so far, which stays marked. Then the player picks that lightest crate and the
    /// overhead crane swaps it into the next bay through a temporary spot behind the row (tmp = a; a = b; b = tmp).
    /// The scale keeps no memory between passes, exactly like selection sort: n(n-1)/2 comparisons, at most n-1 swaps.
    public class SelectionPuzzle : SortPuzzleBase, IAutoPlayable
    {
        [Header("Deal")]
        public int[] weightPool = { 3, 4, 6, 7, 9, 11, 12, 14, 17, 19, 22, 25, 28, 31 };
        public int minOutOfOrderPairs = 9;

        [Header("Room")]
        public Renderer[] padLeds;
        public Transform[] padTops;          // scale plates, dip when a crate is weighed
        public TMP_Text[] padReadouts;       // little digital readout per bay
        public Transform trolley;            // crane trolley on the ceiling rail (moves along x)
        public Transform cable;              // unit-length cable hanging from the trolley, scaled in y
        public Transform hook;
        public TMP_Text board;               // big status screen above the exit
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
        float cableLen = 0.35f;
        Quaternion cableRest = Quaternion.identity;

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
            if (cable != null) cableRest = cable.rotation;
            Setup();
        }

        public override void Setup()
        {
            int n = N;
            int[] vals = Pick(weightPool, n);
            int guard = 0;
            int want = Mathf.Min(minOutOfOrderPairs, n * (n - 1) / 2 - 1);
            while ((CountInversions(vals) < want || MinIndex(vals, 0) == 0) && ++guard < 800) vals = Pick(weightPool, n);

            for (int i = 0; i < n; i++)
            {
                items[i].puzzle = this;
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
            SetCable(cableLen);
            Refresh();
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
                    GameUI.Instance.Flash($"Pass 2.  The scale keeps no memory:  weigh the {n - pass} crates that are left again, left to right.\nPress Q (or the green SCAN button) to weigh them all.", Advice, 5.5f);
                else
                    GameUI.Instance.Info($"Pass {pass + 1}:  {n - pass} crates left to weigh.");
            }
        }

        /// Swap through a temporary spot on the floor behind the lightest crate's bay, so both crates ride the crane:
        /// tmp = lightest;  bay m = crate from bay p;  bay p = tmp.
        IEnumerator CraneSwap(int m, int p)
        {
            SortItem a = items[m];             // the lightest
            SortItem b = items[p];             // the crate in the bay it belongs in
            var ui = GameUI.Instance;
            Vector3 temp = slots[m].position + Vector3.forward * 1.15f;   // the row's far side, away from the player
            temp.y -= 0.08f;                                               // on the floor, not on a scale plate

            if (ui != null) ui.Info($"tmp = {a.value} kg");
            yield return CraneMove(a, slots[m].position, temp);
            if (ui != null) ui.Info($"bay {m + 1} = {b.value} kg");
            yield return CraneMove(b, slots[p].position, slots[m].position);
            if (ui != null) ui.Info($"bay {p + 1} = tmp ({a.value} kg)");
            yield return CraneMove(a, temp, slots[p].position);

            yield return Phase(0.3f, k => SetCable(Mathf.Lerp(currentCable, cableLen, k)));
        }

        /// The crane picks one crate up, carries it (swaying a little) and sets it down. The trolley runs along the
        /// rail in x; for a spot behind the row the cable angles back to reach it.
        IEnumerator CraneMove(SortItem crate, Vector3 from, Vector3 to)
        {
            float h = Height(crate);
            float carryY = slots[0].position.y + MaxCrateHeight() + 0.35f;      // clears every crate in the row
            System.Func<Vector3, Vector3> lid = bottom => bottom + Vector3.up * (h - 0.04f);   // where the hook grips
            Quaternion rest = crate.transform.rotation;

            // trolley over the crate, then the hook down to its lid
            float x0 = trolley != null ? trolley.position.x : from.x;
            float c0 = currentCable;
            Vector3 hook0 = Vector3.zero;
            ProceduralSfx.Play(ProceduralSfx.Slide, from + Vector3.up * 3f, 0.4f);
            yield return Phase(0.55f, k =>
            {
                float move = Mathf.Clamp01(k / 0.6f), drop = Mathf.Clamp01((k - 0.6f) / 0.4f);
                SetTrolleyX(Mathf.Lerp(x0, from.x, move));
                if (drop <= 0f) { SetCable(c0); hook0 = HookPos(); }
                else SetHook(Vector3.Lerp(hook0, lid(from), drop));
            });

            // lift
            ProceduralSfx.Play(ProceduralSfx.Swap, from + Vector3.up, 0.55f);
            yield return Phase(0.45f, k =>
            {
                Vector3 pos = new Vector3(from.x, Mathf.Lerp(from.y, carryY, k), from.z);
                crate.transform.position = pos;
                SetHook(lid(pos));
            });

            // carry: the load lags behind the movement, then swings back
            Vector3 d = to - from;
            float tiltZ = Mathf.Clamp(d.x * 2.5f, -8f, 8f), tiltX = Mathf.Clamp(-d.z * 6f, -8f, 8f);
            float flat = new Vector2(d.x, d.z).magnitude;
            yield return Phase(0.45f + 0.16f * flat, k =>
            {
                Vector3 pos = new Vector3(Mathf.Lerp(from.x, to.x, k), carryY, Mathf.Lerp(from.z, to.z, k));
                SetTrolleyX(pos.x);
                crate.transform.position = pos;
                float sway = Mathf.Sin(k * Mathf.PI);
                crate.transform.rotation = Quaternion.Euler(tiltX * sway, 0f, tiltZ * sway) * rest;
                SetHook(lid(pos));
            });

            // set it down, settling the last of the swing
            yield return Phase(0.45f, k =>
            {
                Vector3 pos = new Vector3(to.x, Mathf.Lerp(carryY, to.y, k), to.z);
                crate.transform.position = pos;
                float sway = -0.35f * Mathf.Sin(k * Mathf.PI);
                crate.transform.rotation = Quaternion.Euler(tiltX * sway, 0f, tiltZ * sway) * rest;
                SetHook(lid(pos));
            });
            crate.transform.SetPositionAndRotation(to, rest);
            ProceduralSfx.Play(ProceduralSfx.Thud, to, 0.85f);

            // hook back up under the trolley, at travel height
            Vector3 hookDown = HookPos();
            float travel = Mathf.Max(0.3f, (cable != null ? cable.position.y : carryY + 2f) - lid(new Vector3(0f, carryY, 0f)).y);
            yield return Phase(0.3f, k =>
            {
                Vector3 up = (cable != null ? cable.position : hookDown) + Vector3.down * travel;
                SetHook(Vector3.Lerp(hookDown, up, k));
            });
            SetCable(travel);
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

        void SetTrolleyX(float x)
        {
            if (trolley == null) return;
            Vector3 p = trolley.position; p.x = x; trolley.position = p;
            SetCable(currentCable);
        }

        float currentCable = 0.35f;

        void SetCable(float len)
        {
            currentCable = Mathf.Max(0.05f, len);
            if (cable != null)
            {
                cable.rotation = cableRest;
                Vector3 s = cable.localScale; s.y = currentCable; cable.localScale = s;
            }
            if (hook != null && cable != null) hook.position = cable.position + Vector3.down * currentCable;
        }

        /// Put the hook at a point; the cable stretches (and angles) from the trolley down to it.
        void SetHook(Vector3 p)
        {
            if (cable == null) { if (hook != null) hook.position = p; return; }
            Vector3 d = p - cable.position;
            currentCable = Mathf.Max(0.05f, d.magnitude);
            cable.rotation = Quaternion.FromToRotation(Vector3.down, d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.down) * cableRest;
            Vector3 s = cable.localScale; s.y = currentCable; cable.localScale = s;
            if (hook != null) hook.position = p;
        }

        Vector3 HookPos() => hook != null ? hook.position : (cable != null ? cable.position + Vector3.down * currentCable : Vector3.zero);

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
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4.5f);
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

            if (door != null) door.Unlock();
            float seconds = Time.time - startTime;
            GameState.Instance.Record(new LevelResult
            {
                level = 2, algorithm = "Selection sort",
                comparisons = comparisons, swaps = swaps, minSwaps = 0,
                hints = hintsUsed, mistakes = mistakes, seconds = seconds,
                detail = $"{swaps} swaps  ·  {weighings} weighings  ·  {comparisons} comparisons"
            });
            Refresh();

            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective("The loading doors are open.  Walk through them.");
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
            if (ui != null) ui.ShowPopup("THAT'S SELECTION SORT", body, "Click or press E to close  ·  then walk through the loading doors");
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

            string status = Solved ? "<color=#1f9d47>DOORS OPEN</color>" : "<color=#d8432f>LOCKED</color>";
            if (board != null)
            {
                board.text = Solved
                    ? "<b>STOCK SCALE</b>  -  SELECTION SORT\n\nALL 7 BAYS SORTED\nLIGHT TO HEAVY\n" + status
                    : "<b>STOCK SCALE</b>  -  SELECTION SORT\n\n" +
                      $"PASS  {Mathf.Min(pass + 1, n - 1)} / {n - 1}        NEXT BAY  {pass + 1}\n" +
                      $"WEIGHED THIS PASS  {weighedNow} / {n - pass}" + (minIdx >= 0 ? $"      LIGHTEST  {items[minIdx].value} KG\n" : "\n") +
                      $"SWAPS  {swaps}        WEIGHINGS  {weighings}\n" + status;
            }
            if (terminal != null)
            {
                terminal.SetText(
                    "<b>SCALE CONTROL</b>   FLOOR 2\n" +
                    "> Crates go LIGHT to HEAVY.\n" +
                    "> Each pass: weigh left to right,\n   swap the lightest forward.\n\n" +
                    $"PASS {Mathf.Min(pass + 1, n - 1)}/{n - 1}   NEXT BAY {Mathf.Min(pass + 1, n)}\n" +
                    $"WEIGHED {weighedNow}/{n - pass}   SWAPS {swaps}\n\n" +
                    "<size=80%>[ SCAN = Q ]   [ HINT = H ]</size>");
            }
        }
    }
}
