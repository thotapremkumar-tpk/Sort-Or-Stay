#if UNITY_EDITOR
using System.Collections.Generic;
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
    [DefaultExecutionOrder(10000)]          // LateUpdate after the player's camera (the recording's director moves it)
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

        // building: rooms, the guide's lessons and the room questions
        string stage, walkedFor, lastDialogLine;
        float dialogLineSince;
        bool dialogShot, resumeWalk;
        float lessonSince = -1f, quizNext;
        bool lessonShot, quizShot, quizWrongDone;

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
            quizShot = quizWrongDone = false;
            hasTargetPos = false;
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

        // recording the gameplay video: slower, readable pacing on the video's own clock
        static bool Rec => DevRecorder.On;
        static float Now => Rec ? DevRecorder.Clock : Time.unscaledTime;
        float dialogQuietSince = -1f, summaryQuietSince = -1f;
        string lingerKey;
        float lingerUntil;
        bool hintShown;

        // recording: the camera director (turns to the sorting; a front camera shows the labelled side)
        float frontK;
        bool frontInit;
        Vector3 frontPos, frontVel;
        Quaternion frontRot;
        float frontFov = 50f;
        int frontSide = 1;
        string frontRoom;
        float camYaw, camPitch = 12f, yawVel, pitchVel;
        bool camInit;
        Vector3 lastTargetPos;
        bool hasTargetPos;
        SortPuzzleBase focusFor;
        Vector3 focusCenter;

        string Label => BuildingManager.Instance != null ? $"{scene}_F{BuildingManager.Instance.CurrentIndex + 1}R{BuildingManager.Instance.RoomIndex + 1}" : scene;

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
            if (Rec) Director(b);

            if (!Rec)                                // the video keeps the wake-up scene and the floor titles
            {
                var intro = FindAnyObjectByType<LevelIntro>();
                if (intro != null) intro.Skip();
                var wake = FindAnyObjectByType<IntroSequence>();
                if (wake != null && wake.enabled) wake.Skip();
            }

            // a new floor or room of the building
            if (b != null)
            {
                string st = $"{b.CurrentIndex}:{b.RoomIndex}:{b.InRoom}";
                if (st != stage)
                {
                    if (b.CurrentIndex != floorIndex)
                    {
                        Log(floorIndex >= 0 ? $"=== floor {b.Current.number} ({b.Current.roomName}) reached, {Time.time - walkStart:0.0} s after leaving floor {floorIndex + 1}"
                                            : $"=== floor {b.Current.number} ({b.Current.roomName})");
                        floorIndex = b.CurrentIndex;
                    }
                    if (b.InRoom && !b.FloorDone)
                    {
                        var r = b.CurrentRoom;
                        Log($"--- room {r.number} ({SortPuzzleBase.CaseLabel(r.sortCase)}) entered.  Deal: {(r.puzzle is IAutoPlayable a0 ? a0.DebugState() : "")}");
                        ResetFloor();
                    }
                    stage = st;
                }
            }

            // the guide's lesson: read it for a moment, then close it
            var guide = GuideNpc.Instance;
            if (guide != null && guide.LessonOpen)
            {
                if (lessonSince < 0f) { lessonSince = Now; summaryQuietSince = -1f; Log("guide lesson open"); }
                if (!lessonShot && Now > lessonSince + (Rec ? 6f : 1.2f)) { lessonShot = true; Shot("1b_lesson"); }
                if (Rec) RecLesson(guide);
                else if (Now > lessonSince + 2.4f)
                {
                    guide.CloseLesson();
                    lessonSince = -1f;
                    Log("guide lesson closed");
                }
                if (walking) resumeWalk = true;     // the lesson stopped the walk: carry on afterwards
                return;
            }
            if (guide != null && guide.MenuOpen) return;

            // her dialogs: read each line for a moment, then move on
            if (guide != null && guide.DialogOpen)
            {
                string line = Plain(guide.DialogLine);
                if (line != lastDialogLine) { lastDialogLine = line; dialogLineSince = Now; dialogQuietSince = -1f; Log($"  ADA:  {line}"); }
                if (!dialogShot && Now > dialogLineSince + (Rec ? 2f : 0.6f)) { dialogShot = true; Shot("1a_dialog"); }
                if (Rec)
                {
                    // the video: each line stays until she has said it, then a short breath
                    if (GuideVoice.Speaking) dialogQuietSince = -1f;
                    else if (dialogQuietSince < 0f) dialogQuietSince = Now;
                    float least = Mathf.Max(1.6f, line.Length / 20f);
                    bool wasVoiced = dialogQuietSince > dialogLineSince + 0.5f;      // (no voice: give it reading time instead)
                    if (dialogQuietSince >= 0f && Now > dialogQuietSince + 0.7f && Now > dialogLineSince + 1.2f && (wasVoiced || Now > dialogLineSince + least))
                        guide.DialogNext();
                }
                else if (Now > dialogLineSince + 0.9f) guide.DialogNext();     // the first call finishes the typing, the next moves on
                if (walking) resumeWalk = true;
                return;
            }

            if (walking)
            {
                if (resumeWalk) { resumeWalk = false; GoTo(wp); }
                Walk();
                return;
            }

            if (ui != null && ui.EndCardOpen)
            {
                if (endSince < 0f) endSince = Time.time;
                if (!endLogged)
                {
                    endLogged = true;
                    Log("END CARD shown.  Scoreboard:\n" + Plain(LevelManager.Scoreboard()));
                }
                if (!shotEnd && Time.time > endSince + (Rec ? 2.5f : 0.8f)) { shotEnd = true; Shot("4_endcard"); }
                if (Rec && Time.time > endSince + 8f) DevRecorder.Finish();       // the video ends on the end card
                return;
            }

            if (ui != null && ui.DebriefOpen)
            {
                if (popupSince < 0f)
                {
                    popupSince = Now;
                    Log($"popup open:  {Plain(ui.PopupTitle)}\n{Plain(ui.PopupBody)}");
                }
                if (!shotSolved && Now > popupSince + (Rec ? 1.5f : 0.8f)) { shotSolved = true; Shot("3_debrief"); }
                if (Now > popupSince + (Rec ? 5f : 2.5f)) { ui.ClosePopup(); popupSince = -1f; popupDone = true; Log("popup closed"); }
                return;
            }

            if (!shotStart && Time.time > sceneStart + 1.6f) { shotStart = true; Shot("1_start"); }

            var puzzle = CurrentPuzzle();

            // the room's questions
            var quiz = puzzle != null ? puzzle.quiz : null;
            if (quiz != null && quiz.Open)
            {
                Quiz(quiz);
                return;
            }

            // building: a room finished (or the floor): walk to the next room, down the stairs, or out to the street
            if (b != null)
            {
                string key = $"{b.CurrentIndex}:{b.RoomIndex}:{b.FloorDone}";
                bool roomOver = (!b.InRoom && b.RoomIndex > 0) || b.FloorDone;
                if (roomOver && walkedFor != key)
                {
                    // hands-on: stay in the finished room a while, like a player reading the debrief (the next room's
                    // door must still be open afterwards; it used to shut on a player still in the room next door)
                    if (Hands && !Rec)
                    {
                        if (lingerKey != key) { lingerKey = key; lingerUntil = Time.time + 8f; Log("  staying 8 s in the finished room"); }
                        if (Time.time < lingerUntil) return;
                    }
                    walkedFor = key;
                    if (!b.FloorDone)
                    {
                        var nextRoom = b.CurrentRoom;
                        bool open = nextRoom.entrance == null || nextRoom.entrance.IsOpen;
                        Log($"  Room {nextRoom.number}'s door: " + (open ? "open" : "SHUT  <- BUG"));
                    }
                    else if (b.Current.stairsGate != null)
                        Log("  stairs door: " + (b.Current.stairsGate.IsOpen ? "open" : "SHUT  <- BUG"));
                    var done = b.FloorDone ? b.CurrentRoom : b.Current.rooms[b.RoomIndex - 1];
                    var res = GameState.Instance.Get(b.Current.number, done.number);
                    Log($"room {done.number} done after {steps} steps.  Result: " + Describe(res) + (b.FloorDone ? ".  Floor done, to the stairs." : ".  On to the next room."));
                    StartWalk(b.PathToNext());
                    return;
                }
                if (!b.InRoom || b.FloorDone) return;
            }

            if (puzzle == null) return;

            if (puzzle.Solved)
            {
                if (b != null) return;               // the building moves on once the questions are answered
                if (solvedSince < 0f) solvedSince = Time.time;
                bool open = puzzle.door == null || puzzle.door.IsUnlocked;
                // leave once the debrief has been read (or after 14 s if none came)
                bool ready = popupDone ? Time.time > solvedSince + 1f : Time.time > solvedSince + 14f;
                if (!exitCalled && open && ready)
                {
                    exitCalled = true;
                    int level = LevelManager.Instance != null ? LevelManager.Instance.levelNumber : 0;
                    Log($"solved after {steps} steps.  Result: " + Describe(GameState.Instance.Get(level)) + ".  Leaving the floor.");
                    if (LevelManager.Instance != null) LevelManager.Instance.ExitReached();
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
            // the video: on the first room, ask Ada for a hint once (H), so the hint dialog is in it
            if (Rec && !hintShown && steps == 2 && b != null && b.CurrentIndex == 0 && b.RoomIndex == 0)
            {
                hintShown = true;
                puzzle.RequestHint();
                Log("  hint asked (for the video)");
                nextStep = Time.time + 1f;
                return;
            }
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
            if (!Rec && new Vector2(d.x, d.z).sqrMagnitude > 0.04f) pc.SetLook(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, d.y < -0.5f ? 24f : 14f);
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
                    Log($"  walked in {Time.time - walkStart:0.0} s, now on floor {(b != null ? b.CurrentIndex + 1 : 0)}, room {(b != null ? b.RoomIndex + 1 : 0)}, inside: {(b != null && b.InRoom ? "yes" : "no")}");
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
                if (calm && Time.time > pendingSince + (Rec ? 0.55f : 0.35f))
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
                    nextStep = Time.time + (Rec ? 0.45f : stepInterval);
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

            bool askHint = Rec ? (!hintShown && actions == 3 && BuildingManager.Instance != null && BuildingManager.Instance.CurrentIndex == 0 && BuildingManager.Instance.RoomIndex == 0)
                               : actions % 6 == 0;
            if (askHint)
            {
                hintShown = true;
                puzzle.RequestHint();
                Log($"  hint:  {Plain(puzzle.Hint()).Replace("\n", "  ")}");
                nextStep = Time.time + stepInterval;
                return;
            }

            bool wrong = !Rec && actions % 4 == 0;
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
            lastTargetPos = target.transform.position;
            hasTargetPos = true;
            pi.Use(target);
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// Answers the room's questions: the right answer, except one deliberate wrong pick per room in hands-on mode.
        void Quiz(RoomQuiz quiz)
        {
            if (Now < quizNext) return;
            quizNext = Now + (Rec ? 2.3f : 1.1f);
            var q = quiz.Current;
            if (q == null) return;
            if (quiz.AnsweredRight)
            {
                if (!quizShot) { quizShot = true; Shot("3b_quiz"); }
                quiz.Next();
                if (!quiz.Open) Log($"  questions done:  {quiz.WrongAnswers} wrong {(quiz.WrongAnswers == 1 ? "answer" : "answers")}.  Door open: {(quiz.puzzle != null && quiz.puzzle.door != null && quiz.puzzle.door.IsUnlocked ? "yes" : "NO  <-- BUG")}");
                return;
            }
            if (Hands && !Rec && !quizWrongDone)
            {
                quizWrongDone = true;
                int wrong = (q.correct + 1) % q.options.Length;
                int before = quiz.WrongAnswers;
                quiz.Pick(wrong);
                Log($"  Q{quiz.Index + 1}  {Plain(q.text)}\n        wrong pick \"{Plain(q.options[wrong])}\":  wrong answers {before} → {quiz.WrongAnswers}, still open: {(!quiz.AnsweredRight ? "yes" : "NO  <-- BUG")}");
                return;
            }
            Log($"  Q{quiz.Index + 1}  {Plain(q.text)}\n        answer \"{Plain(q.options[q.correct])}\"");
            quiz.Pick(q.correct);
        }

        void MidShot() => Shot("2_midway");

        // ------------------------------------------------------------ recording: the camera director

        /// Where the room's sorting happens: the middle of its slots and items.
        Vector3 Focus(SortPuzzleBase p)
        {
            if (p != focusFor)
            {
                focusFor = p;
                var bnd = new Bounds(p.transform.position, Vector3.zero);
                bool any = false;
                if (p.slots != null)
                    foreach (var t in p.slots)
                        if (t != null) { if (!any) bnd = new Bounds(t.position, Vector3.zero); else bnd.Encapsulate(t.position); any = true; }
                foreach (var it in p.items)
                    if (it != null) { if (!any) bnd = new Bounds(it.transform.position, Vector3.zero); else bnd.Encapsulate(it.transform.position); any = true; }
                focusCenter = bnd.center;
            }
            return focusCenter;
        }

        /// While recording: in a room the view turns smoothly to the things being sorted (third person over the
        /// shoulder while the player walks up and starts; first person close up after the first few moves, for the
        /// small things on a rack or shelf); walking between rooms it looks where the player is going.
        void LateUpdate()
        {
            if (On && Rec) FrontCamera();
        }

        /// The labels on the things being sorted (weights, numbers, call numbers) face this way.
        static bool FrontOf(SortPuzzleBase p, out Vector3 center, out Vector3 normal, out Vector3 along, out float halfW, out float halfH)
        {
            center = Vector3.zero; normal = Vector3.forward; along = Vector3.right; halfW = halfH = 0.5f;
            Vector3 n = Vector3.zero;
            int count = 0;
            var pts = new List<Vector3>();
            foreach (var it in p.items)
            {
                if (it == null || !it.gameObject.activeInHierarchy) continue;
                pts.Add(it.label != null ? it.label.transform.position : it.transform.position + Vector3.up * 0.2f);
                if (it.label != null) { n += -it.label.transform.forward; count++; }
            }
            if (p.slots != null) foreach (var t in p.slots) if (t != null) pts.Add(t.position);
            if (pts.Count == 0 || count == 0) return false;
            n.y = 0f;
            if (n.sqrMagnitude < 0.0001f) return false;
            normal = n.normalized;
            along = Vector3.Cross(Vector3.up, normal).normalized;
            var bnd = new Bounds(pts[0], Vector3.zero);
            foreach (var q in pts) bnd.Encapsulate(q);
            center = bnd.center;
            float w = 0f, h = 0f;
            foreach (var q in pts)
            {
                w = Mathf.Max(w, Mathf.Abs(Vector3.Dot(q - center, along)));
                h = Mathf.Max(h, Mathf.Abs(q.y - center.y));
            }
            halfW = w + 0.35f;
            halfH = h + 0.3f;
            return true;
        }

        /// While a room is being sorted, the video's camera stands in front of the labelled side of the things being
        /// sorted (a little to the side the player is not on, a little above), so every number can be read and the
        /// player is seen reaching in; it glides there from the player's camera and back once the room is sorted.
        void FrontCamera()
        {
            var pc = PlayerController.Instance;
            var b = BuildingManager.Instance;
            if (pc == null || pc.cam == null || pc.cinematic) { frontK = 0f; frontInit = false; return; }
            var guide = GuideNpc.Instance;
            bool talk = guide != null && (guide.DialogOpen || guide.LessonOpen || guide.Visible);
            var puzzle = CurrentPuzzle();
            bool sorting = b != null && b.InRoom && !b.FloorDone && puzzle != null && !puzzle.Solved && !walking && !talk
                           && actions > 0 && (puzzle.quiz == null || !puzzle.quiz.Open);
            float dt = Mathf.Max(0.001f, Time.deltaTime);
            string room = b != null ? $"{b.CurrentIndex}:{b.RoomIndex}" : "";
            if (room != frontRoom) { frontRoom = room; frontInit = false; }

            Vector3 goalPos = frontPos;
            Quaternion goalRot = frontRot;
            float goalFov = frontFov;
            Vector3 c = Vector3.zero, n = Vector3.forward, a = Vector3.right;
            float hw = 1f, hh = 0.5f;
            bool have = sorting && FrontOf(puzzle, out c, out n, out a, out hw, out hh);
            if (have)
            {
                const float vfov = 50f;
                float tv = Mathf.Tan(vfov * 0.5f * Mathf.Deg2Rad), th = tv * 16f / 9f;
                float d = Mathf.Max(hw / th, hh / tv) * 1.15f + 0.4f;
                d = Mathf.Max(d, 1.7f);
                // stay on the side the player is not on, decided once per room (no swinging back and forth)
                float s = Vector3.Dot(pc.transform.position - c, a);
                if (!frontInit) frontSide = s > 0f ? -1 : 1;
                Vector3 dir = (n * d + a * (frontSide * 0.28f * hw) + Vector3.up * (0.45f + 0.12f * d)).normalized;
                float want = (n * d + a * (frontSide * 0.28f * hw) + Vector3.up * (0.45f + 0.12f * d)).magnitude;
                Vector3 origin = c + Vector3.up * 0.15f;
                float dist = want;
                if (Physics.SphereCast(origin, 0.18f, dir, out RaycastHit hit, want, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    dist = Mathf.Max(0.9f, hit.distance - 0.15f);
                Vector3 pos = origin + dir * dist;
                if (dist < want * 0.85f) pos += Vector3.up * Mathf.Min(0.9f, (want - dist) * 0.35f);   // short of room: higher, looking down more
                // the field of view widens if the wall kept the camera close
                float flat = Mathf.Max(0.5f, Vector3.Dot(pos - c, n));
                float need = 2f * Mathf.Atan(Mathf.Max(hw / (16f / 9f), hh) / flat) * Mathf.Rad2Deg * 1.1f;
                goalFov = Mathf.Clamp(Mathf.Max(vfov, need), vfov, 75f);
                Vector3 look = c;
                if (hasTargetPos) look = Vector3.Lerp(c, lastTargetPos, 0.15f);
                goalPos = pos;
                goalRot = Quaternion.LookRotation(look - pos, Vector3.up);
                if (!frontInit)
                {
                    frontInit = true;
                    frontPos = goalPos; frontRot = goalRot; frontFov = goalFov; frontVel = Vector3.zero;
                }
            }

            frontPos = Vector3.SmoothDamp(frontPos, goalPos, ref frontVel, 0.6f, 6f, dt);
            frontRot = Quaternion.Slerp(frontRot, goalRot, 1f - Mathf.Exp(-dt * 3f));
            frontFov = Mathf.Lerp(frontFov, goalFov, 1f - Mathf.Exp(-dt * 3f));

            frontK = Mathf.MoveTowards(frontK, have ? 1f : 0f, dt / 1.1f);
            if (frontK <= 0f) return;
            float k = Mathf.SmoothStep(0f, 1f, frontK);
            var cam = pc.cam.transform;
            cam.position = Vector3.Lerp(cam.position, frontPos, k);
            cam.rotation = Quaternion.Slerp(cam.rotation, frontRot, k);
            pc.cam.fieldOfView = Mathf.Lerp(pc.cam.fieldOfView, frontFov, k);
        }

        void Director(BuildingManager b)
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.cinematic) { camInit = false; return; }
            if (!camInit) { camInit = true; camYaw = pc.Yaw; yawVel = pitchVel = 0f; if (pc.autoWalkSpeed < 2.5f) pc.autoWalkSpeed = 2.5f; pc.firstPersonFov = 78f; }
            var guide = GuideNpc.Instance;
            bool talk = guide != null && (guide.DialogOpen || guide.LessonOpen || guide.Visible);
            var puzzle = CurrentPuzzle();
            bool sorting = b != null && b.InRoom && !b.FloorDone && puzzle != null && !puzzle.Solved && !walking;

            bool wantFirst = false;
            float targetYaw = camYaw, targetPitch = camPitch;
            bool aim = false;

            if (sorting && !talk)
            {
                Vector3 f = Focus(puzzle);
                if (hasTargetPos) f = Vector3.Lerp(f, lastTargetPos, 0.35f);
                Vector3 from = wantFirst ? pc.transform.position + Vector3.up * pc.EyeHeight : pc.transform.position + Vector3.up * pc.pivotHeight;
                Vector3 d = f - from;
                float flat = new Vector2(d.x, d.z).magnitude;
                if (flat > 0.8f)
                {
                    targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    float back = wantFirst ? 0f : pc.camDistance * 0.9f;
                    targetPitch = Mathf.Atan2(from.y + (wantFirst ? 0f : 0.35f) - f.y, flat + back) * Mathf.Rad2Deg + (wantFirst ? 2f : 4f);
                    aim = true;
                }
            }
            else if (walking && path != null && wp < path.Length)
            {
                Vector3 v = pc.transform.position;
                Vector3 d = path[wp] - v;
                if (new Vector2(d.x, d.z).sqrMagnitude > 0.09f)
                {
                    targetYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
                    targetPitch = d.y < -0.5f ? 22f : 12f;
                    aim = true;
                }
            }

            if (pc.view != PlayerController.View.Third) pc.view = PlayerController.View.Third;

            if (aim)
            {
                float dt = Mathf.Max(0.001f, Time.deltaTime);
                camYaw = Mathf.SmoothDampAngle(camYaw, targetYaw, ref yawVel, wantFirst ? 0.35f : 0.55f, 220f, dt);
                camPitch = Mathf.SmoothDamp(camPitch, Mathf.Clamp(targetPitch, -20f, 40f), ref pitchVel, 0.5f, 120f, dt);
                pc.SetLook(camYaw, camPitch);
            }
            else camYaw = pc.Yaw;
        }

        /// The video: let her lesson play with her voice for a few steps (more on the first floor), then the
        /// best / average / worst summary, then close it once she has finished.
        void RecLesson(GuideNpc guide)
        {
            var b = BuildingManager.Instance;
            int floor = b != null ? b.Current.number : 1;
            int maxSteps = floor == 1 ? 6 : 2;
            if (!guide.LessonOnSummary)
            {
                if (guide.LessonStepIndex >= maxSteps || Now > lessonSince + 120f) guide.LessonToSummary();
                return;
            }
            if (GuideVoice.Speaking) { summaryQuietSince = -1f; return; }
            if (summaryQuietSince < 0f) summaryQuietSince = Now;
            if (Now > summaryQuietSince + 2f)
            {
                guide.CloseLesson();
                lessonSince = -1f;
                Log("guide lesson closed");
            }
        }

        static string Describe(LevelResult r)
        {
            if (r == null) return "(none recorded)";
            return $"floor {r.level} room {r.room} {r.algorithm} ({SortPuzzleBase.CaseLabel(r.sortCase)}): {r.comparisons} comparisons, {r.detail}, {r.mistakes} slips, {r.hints} hints, {GameState.Clock(r.seconds)}";
        }
    }
}
#endif
