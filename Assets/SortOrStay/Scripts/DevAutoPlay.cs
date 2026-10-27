#if UNITY_EDITOR
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortOrStay
{
    /// Editor-only play-tester (menu Sort or Stay > Dev: ...). While it is switched on it skips the intros, plays the
    /// puzzle, waits for the debrief popup, closes it and leaves the floor, until the end card shows.
    ///   Auto-play:     the puzzle performs its next correct step itself, every half second.
    ///   Hands-on test: every step goes through PlayerInteractor.Use, the same path as a click or a number key,
    ///                  so the avatar really walks over and reaches; every 4th action is a deliberate wrong move
    ///                  and every 6th a hint, and the log checks that a wrong move changed nothing but the slips.
    /// In the five-floor building, leaving a floor means walking it: the avatar goes out of the door and down the
    /// real stairs to the next floor's entrance (or out to the street), waypoint by waypoint.
    /// Every step, the debrief text, warnings and errors go to Logs/SortOrStay_autoplay.txt, and Game-view
    /// screenshots of each stage go to Screenshots/ in the project folder. Never part of a build.
    public class DevAutoPlay : MonoBehaviour
    {
        public const string PrefKey = "SortOrStay.DevAutoPlay";
        public const string HandsKey = "SortOrStay.DevAutoPlay.Hands";
        public const string FloorKey = "SortOrStay.DevAutoPlay.Floor";     // building: start on this floor (once)
        public static bool On => UnityEditor.SessionState.GetBool(PrefKey, false);
        public static bool Hands => UnityEditor.SessionState.GetBool(HandsKey, false);
        public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/SortOrStay_autoplay.txt"));

        public float stepInterval = 0.55f;

        static DevAutoPlay instance;
        float sceneStart, nextStep, popupSince = -1f, solvedSince = -1f, endSince = -1f;
        bool exitCalled, endLogged, popupDone, shotStart, shotMid, shotSolved, shotEnd;
        string scene;
        int steps, actions, floorIndex = -1;

        // hands-on: the action in progress
        bool pending, pendingWrong;
        float pendingSince;
        Vector3 pendingFrom;
        string pendingBefore, pendingName;
        int pendingSlips;

        // building: walking down to the next floor
        bool walking;
        Vector3[] path;
        int wp, arrived = -1;
        float wpSince, walkStart;
        Vector3 walkFrom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!On || instance != null) return;
            var go = new GameObject("DevAutoPlay");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<DevAutoPlay>();
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            Application.logMessageReceived += OnLog;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.logMessageReceived -= OnLog;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            // building, asked to start lower down: jump there once (reloads the scene)
            int startFloor = UnityEditor.SessionState.GetInt(FloorKey, 1);
            if (startFloor > 1 && BuildingManager.Instance != null)
            {
                UnityEditor.SessionState.SetInt(FloorKey, 1);
                Log($"=== starting the building on floor {startFloor}");
                BuildingManager.Instance.JumpTo(startFloor - 1);
                return;
            }
            scene = s.name;
            floorIndex = -1;
            endSince = -1f;
            endLogged = shotEnd = walking = false;
            ResetFloor();
            Log($"=== scene {scene} loaded" + (Hands ? "  (hands-on: walk + use, with wrong moves and hints)" : ""));
        }

        void ResetFloor()
        {
            exitCalled = popupDone = shotStart = shotMid = shotSolved = pending = false;
            popupSince = solvedSince = -1f;
            steps = actions = 0;
            sceneStart = Time.time;
            nextStep = Time.time + 2.0f;
        }

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Log) return;
            Log($"[{type}] {condition}" + (type == LogType.Exception || type == LogType.Error ? "\n" + stackTrace : ""));
        }

        public static void Log(string line)
        {
            try { File.AppendAllText(LogPath, $"{System.DateTime.Now:HH:mm:ss.f}  {line}\n"); }
            catch (System.Exception) { }
        }

        string Label => BuildingManager.Instance != null ? $"{scene}_F{BuildingManager.Instance.CurrentIndex + 1}" : scene;

        void Shot(string label)
        {
            try
            {
                Directory.CreateDirectory(DevScreenshot.Folder);
                string file = Path.Combine(DevScreenshot.Folder, $"{(Hands ? "hands" : "autoplay")}_{Label}_{label}.png");
                ScreenCapture.CaptureScreenshot(file, 1);
                Log($"screenshot {file}");
            }
            catch (System.Exception e) { Log("screenshot failed: " + e.Message); }
        }

        static string Plain(string rich) => string.IsNullOrEmpty(rich) ? "" : Regex.Replace(rich, "<[^>]+>", "");
        static string NoSlips(string s) => Regex.Replace(s ?? "", @"slips \d+", "");

        static SortPuzzleBase CurrentPuzzle()
        {
            var b = BuildingManager.Instance;
            return b != null ? b.CurrentPuzzle : FindAnyObjectByType<SortPuzzleBase>();
        }

        void Update()
        {
            if (!On) return;
            var ui = GameUI.Instance;
            var b = BuildingManager.Instance;

            var intro = FindAnyObjectByType<LevelIntro>();
            if (intro != null) intro.Skip();
            var wake = FindAnyObjectByType<IntroSequence>();
            if (wake != null && wake.enabled) wake.Skip();

            // a new floor of the building
            if (b != null && b.CurrentIndex != floorIndex)
            {
                if (floorIndex >= 0)
                    Log($"=== floor {b.Current.number} ({b.Current.roomName}) entered, {Time.time - walkStart:0.0} s after leaving floor {floorIndex + 1}");
                else Log($"=== floor {b.Current.number} ({b.Current.roomName})");
                floorIndex = b.CurrentIndex;
                ResetFloor();
            }

            if (walking) { Walk(); return; }

            if (ui != null && ui.EndCardOpen)
            {
                if (endSince < 0f) endSince = Time.time;
                if (!endLogged)
                {
                    endLogged = true;
                    Log("END CARD shown.  Scoreboard:\n" + Plain(LevelManager.Scoreboard()));
                }
                if (!shotEnd && Time.time > endSince + 0.8f) { shotEnd = true; Shot("4_endcard"); }
                return;
            }

            if (ui != null && ui.PopupOpen)
            {
                if (popupSince < 0f)
                {
                    popupSince = Time.unscaledTime;
                    Log($"popup open:  {Plain(ui.PopupTitle)}\n{Plain(ui.PopupBody)}");
                }
                if (!shotSolved && Time.unscaledTime > popupSince + 0.8f) { shotSolved = true; Shot("3_debrief"); }
                if (Time.unscaledTime > popupSince + 2.5f) { ui.ClosePopup(); popupSince = -1f; popupDone = true; Log("popup closed"); }
                return;
            }

            if (!shotStart && Time.time > sceneStart + 1.6f) { shotStart = true; Shot("1_start"); }

            var puzzle = CurrentPuzzle();
            if (puzzle == null) return;

            if (puzzle.Solved)
            {
                if (solvedSince < 0f) solvedSince = Time.time;
                bool open = puzzle.door == null || puzzle.door.IsUnlocked;
                // leave once the debrief has been read (or after 14 s if none came)
                bool ready = popupDone ? Time.time > solvedSince + 1f : Time.time > solvedSince + 14f;
                if (!exitCalled && open && ready)
                {
                    exitCalled = true;
                    int level = b != null ? b.Current.number : (LevelManager.Instance != null ? LevelManager.Instance.levelNumber : 0);
                    Log($"solved after {steps} steps.  Result: " + Describe(GameState.Instance.Get(level)) + ".  Leaving the floor.");
                    if (b != null) StartWalk(b.Current.pathOut);
                    else if (LevelManager.Instance != null) LevelManager.Instance.ExitReached();
                }
                return;
            }

            if (!(puzzle is IAutoPlayable ap))
            {
                if (steps == 0) { steps = -1; Log("this floor has no auto-play; play it by hand"); }
                return;
            }

            if (Hands) { HandsOn(puzzle, ap); return; }

            if (Time.time < nextStep || !puzzle.AcceptsInput) return;
            if (ap.AutoStep())
            {
                steps++;
                nextStep = Time.time + stepInterval;
                Log($"step {steps,3}  {ap.DebugState()}");
                if (!shotMid && steps == 8) { shotMid = true; Invoke(nameof(MidShot), 0.45f); }
            }
        }

        // ------------------------------------------------------------ building: out of the door and down the stairs

        void StartWalk(Vector3[] waypoints)
        {
            var pc = PlayerController.Instance;
            if (waypoints == null || waypoints.Length == 0 || pc == null) { Log("  no path out of this floor  <-- BUG"); return; }
            path = waypoints;
            walking = true;
            wp = 0;
            arrived = -1;
            walkStart = Time.time;
            walkFrom = pc.transform.position;
            GoTo(0);
        }

        void GoTo(int i)
        {
            var pc = PlayerController.Instance;
            wpSince = Time.time;
            int target = i;
            Vector3 d = path[i] - pc.transform.position;
            if (new Vector2(d.x, d.z).sqrMagnitude > 0.04f) pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, d.y < -0.5f ? 24f : 14f);
            // inside the room: around the furniture on the walk map; from the door on: straight down the flights
            pc.WalkTo(path[i], 0.3f, () => arrived = target, i > 0);
        }

        void Walk()
        {
            var pc = PlayerController.Instance;
            var b = BuildingManager.Instance;
            if (pc == null) { walking = false; return; }
            if (!pc.canMove)
            {
                // walked into the street: the building fades out and the end card follows
                walking = false;
                pc.StopWalking();
                Log($"  out of the building after {Time.time - walkStart:0.0} s");
                return;
            }

            if (arrived == wp)
            {
                Vector3 p = pc.transform.position;
                Log($"  walk {wp + 1}/{path.Length}  at ({p.x:0.0}, {p.y:0.0}, {p.z:0.0})  target y {path[wp].y:0.0}  {Time.time - wpSince:0.0} s" +
                    (Mathf.Abs(p.y - path[wp].y) > 0.35f ? "  <-- WRONG HEIGHT" : ""));
                if (wp == 3) Invoke(nameof(StairsShot), 0.1f);
                wp++;
                if (wp >= path.Length)
                {
                    walking = false;
                    Log($"  walked out in {Time.time - walkStart:0.0} s, now on floor {(b != null ? b.CurrentIndex + 1 : 0)}");
                    return;
                }
                GoTo(wp);
                return;
            }

            bool gaveUp = !pc.IsAutoWalking && arrived != wp && Time.time > wpSince + 0.5f;
            if (gaveUp || Time.time > wpSince + 12f)
            {
                Vector3 p = pc.transform.position;
                Log($"  STUCK walking to waypoint {wp + 1}/{path.Length} {path[wp]}:  player at {p}{(gaveUp ? " (gave up)" : "")}");
                walking = false;
                pc.StopWalking();
                // keep testing the rest of the building: put the player at the last waypoint
                if (b != null) b.Teleport(path[path.Length - 1], 180f);
            }
        }

        void StairsShot() => Shot("2b_stairs");

        // ------------------------------------------------------------ hands-on

        /// One action at a time through the real input path: walk over, use, wait until the room is calm again.
        void HandsOn(SortPuzzleBase puzzle, IAutoPlayable ap)
        {
            var pc = PlayerController.Instance;
            var pi = PlayerInteractor.Instance;
            if (pc == null || pi == null) return;
            bool calm = puzzle.AcceptsInput && !pc.IsAutoWalking;

            if (pending)
            {
                if (calm && Time.time > pendingSince + 0.35f)
                {
                    pending = false;
                    float walked = Vector3.Distance(Flat(pendingFrom), Flat(pc.transform.position));
                    string after = ap.DebugState();
                    if (pendingWrong)
                    {
                        bool same = NoSlips(after) == NoSlips(pendingBefore);
                        Log($"  wrong move on {pendingName}:  slips {pendingSlips} → {puzzle.mistakes},  state unchanged: {(same ? "yes" : "NO  <-- BUG")}" +
                            $"   (walked {walked:0.0} m, {Time.time - pendingSince:0.0} s)");
                    }
                    else
                    {
                        bool moved = after != pendingBefore || puzzle.Solved;
                        steps++;
                        Log($"step {steps,3}  {pendingName}  walked {walked:0.0} m  {Time.time - pendingSince:0.0} s" + (moved ? "" : "  <-- NOTHING HAPPENED") + $"\n            {after}");
                        if (!shotMid && steps == 8) { shotMid = true; Invoke(nameof(MidShot), 0.3f); }
                    }
                    nextStep = Time.time + stepInterval;
                }
                else if (Time.time > pendingSince + 12f)
                {
                    pending = false;
                    Log($"  STUCK  using {pendingName} for 12 s.  Player at {pc.transform.position}, walking {pc.IsAutoWalking}, puzzle input {puzzle.AcceptsInput}");
                    pc.StopWalking();
                    nextStep = Time.time + 1f;
                }
                return;
            }

            if (!calm || Time.time < nextStep) return;
            actions++;

            if (actions % 6 == 0)
            {
                puzzle.RequestHint();
                Log($"  hint:  {Plain(puzzle.Hint()).Replace("\n", "  ")}");
                nextStep = Time.time + stepInterval;
                return;
            }

            bool wrong = actions % 4 == 0;
            var target = ap.AutoTarget(wrong);
            if (wrong && target == null) { wrong = false; target = ap.AutoTarget(false); }
            if (target == null) { nextStep = Time.time + 0.3f; return; }

            var it = target.GetComponent<IInteractable>() ?? target.GetComponentInParent<IInteractable>();
            if (it == null || !it.CanInteract)
            {
                if (wrong) Log($"  wrong pick {target.name}: not usable now, the pointer skips it (fine)");
                else Log($"  {target.name} is the next step but cannot be used  <-- BUG");
                nextStep = Time.time + stepInterval;
                return;
            }

            pending = true;
            pendingWrong = wrong;
            pendingSince = Time.time;
            pendingFrom = pc.transform.position;
            pendingBefore = ap.DebugState();
            pendingSlips = puzzle.mistakes;
            pendingName = $"{target.name} \"{it.Prompt}\"";
            pi.Use(target);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        void MidShot() => Shot("2_midway");

        static string Describe(LevelResult r)
        {
            if (r == null) return "(none recorded)";
            return $"floor {r.level} {r.algorithm}: {r.detail}, {r.mistakes} slips, {r.hints} hints, {GameState.Clock(r.seconds)}";
        }
    }
}
#endif
