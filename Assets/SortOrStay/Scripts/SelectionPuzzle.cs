using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Floor 2, the Storage Room. Seven crates with hidden weights stand on seven scale bays.
    /// Each pass the player weighs every crate that is still unsorted, then picks the lightest;
    /// the overhead crane swaps it into the next bay. The scale keeps no memory between passes,
    /// exactly like selection sort: n(n-1)/2 comparisons, at most n-1 swaps.
    public class SelectionPuzzle : SortPuzzleBase
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

        int pass;
        bool[] weighed;
        int weighings;
        int startInversions;
        Material[] ledMats;
        Material nextMat;
        Vector3[] padRest;
        float cableLen = 0.35f;

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
            pass = 0;
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

        int MinIndexFrom(int from)
        {
            int m = from;
            for (int i = from + 1; i < N; i++) if (items[i].value < items[m].value) m = i;
            return m;
        }

        int Unweighed()
        {
            int c = 0;
            for (int i = pass; i < N; i++) if (!weighed[i]) c++;
            return c;
        }

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
                    if (Unweighed() > 0) OnButton("scan");
                    else StartCoroutine(PlaceRoutine(MinIndexFrom(pass)));
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
            if (Unweighed() == 0)
            {
                if (GameUI.Instance != null) GameUI.Instance.Info("Every crate left already has a reading.  Pick the lightest.");
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

            if (!weighed[idx])
            {
                StartCoroutine(WeighRoutine(idx, true, 0.34f));
                return;
            }

            int left = Unweighed();
            if (left > 0)
            {
                item.Flash(SortItem.ErrorColor, 0.5f);
                Gesture(PlayerController.GestureScratch);
                if (ui != null)
                    ui.Flash($"Weigh every crate first:  {left} still {(left == 1 ? "has" : "have")} no reading.\nSelection sort checks them all before it knows which is lightest.", Advice, 4f);
                return;
            }

            int m = MinIndexFrom(pass);
            if (idx != m)
            {
                mistakes++;
                item.Flash(SortItem.ErrorColor, 0.8f);
                ProceduralSfx.Play(ProceduralSfx.Error, item.transform.position, 0.7f);
                Gesture(PlayerController.GestureScratch);
                if (ui != null) ui.Flash($"{item.value} kg is not the lightest crate left.  Compare the readings again.");
                Refresh();
                return;
            }

            StartCoroutine(PlaceRoutine(idx));
        }

        // ------------------------------------------------------------ weighing

        IEnumerator ScanAll()
        {
            busy = true;
            for (int i = pass; i < N; i++)
                if (!weighed[i]) yield return WeighRoutine(i, false, 0.2f);
            busy = false;
            Refresh();
            if (GameUI.Instance != null) GameUI.Instance.Info($"All {N - pass} crates weighed.  Click the lightest to send it to bay {pass + 1}.");
        }

        IEnumerator WeighRoutine(int i, bool ownBusy, float dur)
        {
            if (ownBusy) busy = true;
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
            if (ownBusy) busy = false;
            Refresh();

            if (ownBusy && Unweighed() == 0 && GameUI.Instance != null)
                GameUI.Instance.Info($"Every crate left is weighed.  Click the lightest to send it to bay {pass + 1}.");
        }

        // ------------------------------------------------------------ the swap (overhead crane)

        IEnumerator PlaceRoutine(int m)
        {
            busy = true;
            int p = pass;
            int n = N;
            comparisons += n - p - 1;          // what the algorithm compared this pass

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
                    GameUI.Instance.Flash($"Pass 2.  The scale keeps no memory:  weigh the {n - pass} crates that are left again.\nPress Q (or the green SCAN button) to weigh them all at once.", Advice, 5.5f);
                else
                    GameUI.Instance.Info($"Pass {pass + 1}:  {n - pass} crates left to weigh.");
            }
        }

        IEnumerator CraneSwap(int m, int p)
        {
            SortItem a = items[m];             // the lightest: lifted and carried to bay p
            SortItem b = items[p];             // the crate in bay p: rolled round behind the row to bay m
            Vector3 aStart = slots[m].position, aEnd = slots[p].position;
            Vector3 bStart = slots[p].position, bEnd = slots[m].position;
            float ha = Height(a);
            float liftTo = MaxCrateHeight() + 0.35f;
            Vector3 back = Vector3.forward * 1.15f;       // the row's far side, away from the player
            float railY = trolley != null ? trolley.position.y : 3.7f;

            // 1. trolley over crate a, cable down to its lid
            float x0 = trolley != null ? trolley.position.x : aStart.x;
            float c0 = cableLen, c1 = Mathf.Max(0.3f, railY - (aStart.y + ha) - 0.06f);
            ProceduralSfx.Play(ProceduralSfx.Slide, aStart + Vector3.up * 3f, 0.4f);
            yield return Phase(0.65f, k =>
            {
                SetTrolleyX(Mathf.Lerp(x0, aStart.x, k));
                SetCable(Mathf.Lerp(c0, c1, Mathf.Clamp01(k * 1.6f - 0.6f)));
            });

            // 2. lift a; b rolls out behind the row
            ProceduralSfx.Play(ProceduralSfx.Swap, aStart + Vector3.up, 0.6f);
            yield return Phase(0.55f, k =>
            {
                a.transform.position = aStart + Vector3.up * (liftTo * k);
                SetCable(c1 - liftTo * k);
                b.transform.position = bStart + back * k;
            });

            // 3. carry a over to bay p; b travels along the back to bay m
            yield return Phase(0.95f, k =>
            {
                SetTrolleyX(Mathf.Lerp(aStart.x, aEnd.x, k));
                a.transform.position = new Vector3(Mathf.Lerp(aStart.x, aEnd.x, k), aStart.y + liftTo, Mathf.Lerp(aStart.z, aEnd.z, k));
                b.transform.position = Vector3.Lerp(bStart, bEnd, k) + back;
            });

            // 4. lower a into bay p; b rolls into bay m
            yield return Phase(0.55f, k =>
            {
                a.transform.position = aEnd + Vector3.up * (liftTo * (1f - k));
                SetCable(c1 - liftTo * (1f - k));
                b.transform.position = bEnd + back * (1f - k);
            });
            a.transform.position = aEnd;
            b.transform.position = bEnd;
            ProceduralSfx.Play(ProceduralSfx.Thud, aEnd, 0.9f);
            ProceduralSfx.Play(ProceduralSfx.Thud, bEnd, 0.6f);

            // 5. reel the cable back in
            float c2 = cableLen;
            yield return Phase(0.35f, k => SetCable(Mathf.Lerp(c1, c2, k)));
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
                Vector3 s = cable.localScale; s.y = currentCable; cable.localScale = s;
            }
            if (hook != null && cable != null) hook.position = cable.position + Vector3.down * currentCable;
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
            int left = Unweighed();
            if (left > 0)
                return $"Weigh the {left} crate{(left == 1 ? "" : "s")} without a reading first.\nClick them one by one, or press Q / the green SCAN button.";
            int m = MinIndexFrom(pass);
            return $"The lightest crate left is {items[m].value} kg on bay {m + 1}.\nClick it and the crane swaps it into bay {pass + 1}.";
        }

        public override void RequestHint()
        {
            if (pass >= N) return;
            hintsUsed++;
            if (Unweighed() > 0)
            {
                for (int i = pass; i < N; i++) if (!weighed[i]) items[i].Flash(SortItem.HintColor, 2.2f);
            }
            else items[MinIndexFrom(pass)].Flash(SortItem.HintColor, 2.4f);
            if (GameUI.Instance != null) GameUI.Instance.Flash(Hint(), SortItem.HintColor, 4.5f);
            Refresh();
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
            if (!weighed[idx]) return "Weigh this crate" + key;
            return $"Send the {item.value} kg crate to bay {pass + 1}" + key;
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
                if (!sorted && i < items.Count) items[i].Highlight(SortItem.Idle);
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
                GameUI.Instance.SetStats($"WEIGHINGS  {weighings}      SWAPS  {swaps}      HINTS  {hintsUsed}");

            string status = Solved ? "<color=#1f9d47>DOORS OPEN</color>" : "<color=#d8432f>LOCKED</color>";
            if (board != null)
            {
                board.text = Solved
                    ? "<b>STOCK SCALE</b>  -  SELECTION SORT\n\nALL 7 BAYS SORTED\nLIGHT TO HEAVY\n" + status
                    : "<b>STOCK SCALE</b>  -  SELECTION SORT\n\n" +
                      $"PASS  {Mathf.Min(pass + 1, n - 1)} / {n - 1}        NEXT BAY  {pass + 1}\n" +
                      $"WEIGHED THIS PASS  {weighedNow} / {n - pass}\n" +
                      $"SWAPS  {swaps}        WEIGHINGS  {weighings}\n" + status;
            }
            if (terminal != null)
            {
                terminal.SetText(
                    "<b>SCALE CONTROL</b>   FLOOR 2\n" +
                    "> Crates go LIGHT to HEAVY.\n" +
                    "> Each pass: weigh all that\n   are left, move the lightest.\n\n" +
                    $"PASS {Mathf.Min(pass + 1, n - 1)}/{n - 1}   NEXT BAY {Mathf.Min(pass + 1, n)}\n" +
                    $"WEIGHED {weighedNow}/{n - pass}   SWAPS {swaps}\n\n" +
                    "<size=80%>[ SCAN = Q ]   [ HINT = H ]</size>");
            }
        }
    }
}
