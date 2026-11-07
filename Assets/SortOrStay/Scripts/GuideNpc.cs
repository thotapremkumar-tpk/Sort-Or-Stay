using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Professor Ada: a realistic human guide (Microsoft Rocketbox avatar). She is not with the player all the time:
    ///   - at the start of every floor she walks up, talks (a dialog box: her face, her name, one line at a time),
    ///     teaches the floor's algorithm on screen (an animated bar lesson, every step in her words, ending with its
    ///     best / average / worst case table), then says where to start and walks off;
    ///   - when the player asks for a hint (H) she comes back, gives the hint in a dialog and leaves again;
    ///   - T brings her back for the lesson; while she is around, clicking her opens a small ask menu.
    /// Dialogs can be avoided: Esc (or Skip) ends a dialog, "Turn dialogs off" in the dialog or the pause menu stops
    /// them altogether (hints then appear as plain text and she stays away). The choice is remembered.
    /// She walks on the player's walk map (PlayerController's runtime NavMesh) and has no solid collider.
    [DefaultExecutionOrder(-30)]
    public class GuideNpc : MonoBehaviour
    {
        public static GuideNpc Instance { get; private set; }

        public enum Mood { Neutral, Wave, Present, Happy, Clap, Deny, Think }

        public string displayName = "Professor Ada";
        public Animator animator;
        public Transform head;                       // Bip01 Head
        public CapsuleCollider clickCollider;        // a trigger: only used to tell when she is pointed at
        [Header("Following")]
        public float walkSpeed = 1.35f;
        public float runSpeed = 3.0f;
        public float keepDistance = 1.5f;            // stops this close to the player
        public float followDistance = 2.7f;          // starts walking when the player is further than this
        public float teleportDistance = 15f;

        // ------------------------------------------------------------ dialogs on / off (remembered)

        const string DialogsKey = "SortOrStay.GuideDialogs";

        /// Whether she talks in dialogs (and comes to the player). Off: hints are plain text and she stays away.
        public static bool DialogsOn
        {
            get => PlayerPrefs.GetInt(DialogsKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(DialogsKey, value ? 1 : 0);
                PlayerPrefs.Save();
                if (!value && Instance != null) Instance.DialogsTurnedOff();
            }
        }

        /// Her face (the last picture of it; null until she has been on screen once).
        public Texture Portrait => portraitReady ? portraitRT : null;
        bool portraitReady;
        public bool Visible => visible;
        public bool LessonOpen => lessonOpen;
        public bool MenuOpen => menuOpen;
        public bool DialogOpen => convoOpen;
        public string DialogLine => convoOpen && convoIdx < convo.Count ? convo[convoIdx].text : "";
        public bool Speaking => current != null;

        /// Floors whose welcome (and lesson) she has already given in this game; T replays a lesson.
        static readonly HashSet<int> floorsGreeted = new HashSet<int>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { floorsGreeted.Clear(); Instance = null; }

        // ------------------------------------------------------------ state

        class Line { public string text; public Mood mood; public float seconds; }

        // short line in the corner panel (only while she is around and no dialog is open)
        readonly Queue<Line> queue = new Queue<Line>();
        Line current;
        float lineStart, lineEnd;

        // dialog box
        readonly List<Line> convo = new List<Line>();
        int convoIdx;
        float convoLineAt;
        bool convoOpen;
        Action<bool> convoDone;                      // true = skipped

        // presence
        bool visible = true;
        bool approaching, leaving;
        Vector3 leaveGoal, talkGoal;
        float leaveSince;
        Renderer[] renderers;

        bool moving;
        readonly List<Vector3> corners = new List<Vector3>();
        int corner;
        float nextRepath, curSpeed, failSince = -1f, nextAsideCheck;
        NavMeshPath navPath;
        Vector3 asideGoal;
        bool goingAside;

        bool menuOpen, lessonOpen, lessonTable, lessonPlaying;
        List<LessonStep> lesson;
        int lessonStep, lessonFloor;
        float nextLessonStep, lessonStepAt, voiceQuietSince = -1f;
        Action afterLesson;
        bool hovered;

        SortPuzzleBase watched;
        int watchedMistakes, watchedHints, slipsSinceHint;

        Camera portraitCam;
        RenderTexture portraitRT;

        GUIStyle sName, sText, sSmall, sCap, sBar, sTitle, sCell, sCellB, sMenu, sTag, sDialog, sDialogBtn;

        static readonly Color Blue = new Color(0.16f, 0.42f, 0.78f);
        static readonly int SpeedId = Animator.StringToHash("Speed");
        static readonly int TalkingId = Animator.StringToHash("Talking");

        // ------------------------------------------------------------ setup

        void Awake()
        {
            Instance = this;
            navPath = new NavMeshPath();
            if (clickCollider != null) clickCollider.isTrigger = true;
            portraitRT = new RenderTexture(256, 256, 16) { name = "AdaPortrait" };
            var go = new GameObject("AdaPortraitCamera");
            go.transform.SetParent(transform, false);
            portraitCam = go.AddComponent<Camera>();
            portraitCam.targetTexture = portraitRT;
            portraitCam.fieldOfView = 24f;
            portraitCam.nearClipPlane = 0.05f;
            portraitCam.farClipPlane = 8f;
            portraitCam.clearFlags = CameraClearFlags.SolidColor;
            portraitCam.backgroundColor = new Color(0.80f, 0.86f, 0.94f);
            portraitCam.depth = -20f;
            portraitCam.enabled = false;
            renderers = GetComponentsInChildren<Renderer>(true);
            SetVisible(false);                       // she arrives when the floor starts
        }

        void OnDestroy()
        {
            GuideVoice.Stop();
            if (Instance == this) Instance = null;
            if (portraitRT != null) portraitRT.Release();
        }

        /// Stand here at once.
        public void PlaceAt(Vector3 pos, Vector3 face)
        {
            if (NavMesh.SamplePosition(pos, out var hit, 2f, NavMesh.AllAreas)) pos = hit.position;
            transform.position = pos;
            Vector3 d = face - pos; d.y = 0f;
            if (d.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(d);
            moving = false;
            corners.Clear();
            curSpeed = 0f;
        }

        void SetVisible(bool on)
        {
            visible = on;
            if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = on;
            if (clickCollider != null) clickCollider.enabled = on;
            if (!on) { approaching = leaving = moving = false; corners.Clear(); curSpeed = 0f; current = null; queue.Clear(); }
        }

        /// Come to the player: appear a few steps away, out of the camera's view if possible, and walk up.
        public void Appear()
        {
            var pc = PlayerController.Instance;
            leaving = false;
            if (visible || pc == null) { if (pc != null && visible) { talkGoal = TalkSpot(pc); approaching = true; nextRepath = 0f; } SetVisible(true); return; }
            Vector3 pl = pc.transform.position;
            Vector3 back = pc.cam != null ? -pc.cam.transform.forward : -pc.transform.forward; back.y = 0f; back.Normalize();
            Vector3 left = pc.cam != null ? -pc.cam.transform.right : -pc.transform.right; left.y = 0f; left.Normalize();
            Vector3 spot = pl + left * 1.4f;
            foreach (var off in new[] { back * 2.4f + left * 1.2f, left * 2.8f, back * 2.6f, -left * 2.8f, left * 1.6f, -left * 1.6f })
            {
                if (!NavMesh.SamplePosition(pl + off, out var hit, 0.8f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - pl.y) > 0.6f) continue;
                if (NavMesh.SamplePosition(pl, out var me, 1.2f, NavMesh.AllAreas) && NavMesh.CalculatePath(hit.position, me.position, NavMesh.AllAreas, navPath)
                    && navPath.status == NavMeshPathStatus.PathComplete) { spot = hit.position; break; }
            }
            PlaceAt(spot, pl);
            SetVisible(true);
            talkGoal = TalkSpot(pc);
            approaching = true;
            nextRepath = 0f;
        }

        /// Where she stands to talk: in front of the player and a little to the left, so she is in the picture.
        Vector3 TalkSpot(PlayerController pc)
        {
            Vector3 pl = pc.transform.position;
            Vector3 fwd = pc.cam != null ? pc.cam.transform.forward : pc.transform.forward; fwd.y = 0f; fwd.Normalize();
            Vector3 left = pc.cam != null ? -pc.cam.transform.right : -pc.transform.right; left.y = 0f; left.Normalize();
            NavMesh.SamplePosition(pl, out var me, 1.2f, NavMesh.AllAreas);
            foreach (var off in new[] { fwd * 1.5f + left * 1.0f, fwd * 1.2f + left * 1.3f, fwd * 1.6f - left * 1.0f, left * 1.4f + fwd * 0.4f, -left * 1.4f + fwd * 0.4f })
            {
                if (!NavMesh.SamplePosition(pl + off, out var hit, 0.4f, NavMesh.AllAreas) || Mathf.Abs(hit.position.y - pl.y) > 0.5f) continue;
                if (Flat(hit.position, pl) < 1.0f) continue;
                if (NavMesh.CalculatePath(me.position, hit.position, NavMesh.AllAreas, navPath) && navPath.status == NavMeshPathStatus.PathComplete)
                    return hit.position;
            }
            return GoalNear(pc, transform.position, pl);
        }

        /// Walk away and disappear.
        public void Leave()
        {
            if (!visible || leaving) return;
            var pc = PlayerController.Instance;
            if (pc == null) { SetVisible(false); return; }
            Vector3 pl = pc.transform.position, me = transform.position;
            Vector3 away = me - pl; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) away = -transform.forward;
            away.Normalize();
            leaveGoal = me + away * 5f;
            if (NavMesh.SamplePosition(leaveGoal, out var hit, 2.5f, NavMesh.AllAreas)) leaveGoal = hit.position;
            leaving = true;
            approaching = false;
            leaveSince = Time.time;
            moving = true;
            goingAside = false;
            nextRepath = 0f;
        }

        void DialogsTurnedOff()
        {
            if (convoOpen) EndConvo(true);
            if (lessonOpen) { afterLesson = null; CloseLesson(); }
            if (menuOpen) CloseMenu();
            Leave();
            if (GameUI.Instance != null)
                GameUI.Instance.Flash("Guide dialogs off:  H gives hints as text.  Turn them back on in the pause menu (Esc).", new Color(0.96f, 0.96f, 0.94f), 4.5f);
        }

        // ------------------------------------------------------------ dialog box

        /// Talk to the player in the dialog box, one line at a time (Space / Enter / click: next, Esc: skip).
        /// She comes over first if she is not around. done(skipped) runs at the end. With dialogs off: done(true) at once.
        public void Converse(IList<(string text, Mood mood)> lines, Action<bool> done)
        {
            if (!DialogsOn || lines == null || lines.Count == 0) { done?.Invoke(true); return; }
            if (convoOpen) EndConvo(true);
            Appear();
            convo.Clear();
            foreach (var l in lines) convo.Add(new Line { text = l.text, mood = l.mood });
            convoIdx = 0;
            convoLineAt = Time.unscaledTime;
            convoDone = done;
            convoOpen = true;
            current = null;
            queue.Clear();
            SetModal(true);
            Gesture(convo[0].mood);
            GuideVoice.Speak(convo[0].text);
        }

        bool LineTyped => convoOpen && convoIdx < convo.Count && TypedChars(convo[convoIdx].text, convoLineAt) >= VisibleChars(convo[convoIdx].text);

        /// Next line (or finish the typing of this one first).
        public void DialogNext()
        {
            if (!convoOpen) return;
            if (!LineTyped) { convoLineAt = -1000f; return; }
            convoIdx++;
            if (convoIdx >= convo.Count) { EndConvo(false); return; }
            convoLineAt = Time.unscaledTime;
            Gesture(convo[convoIdx].mood);
            GuideVoice.Speak(convo[convoIdx].text);
        }

        public void SkipDialog() { if (convoOpen) EndConvo(true); }

        void EndConvo(bool skipped)
        {
            GuideVoice.Stop();
            convoOpen = false;
            SetModal(false);
            var d = convoDone;
            convoDone = null;
            d?.Invoke(skipped);
        }

        static int VisibleChars(string rich) => Regex.Replace(rich ?? "", "<[^>]+>", "").Length;
        static int TypedChars(string rich, float since) => Mathf.FloorToInt((Time.unscaledTime - since) * 60f);

        // ------------------------------------------------------------ short lines (corner panel, only while she is here)

        /// Say this now in the corner panel, if she is around (no dialog box, no waiting).
        public void Say(string text, Mood mood = Mood.Neutral, float seconds = 0f)
        {
            if (!visible || leaving || convoOpen) return;
            queue.Clear();
            Begin(new Line { text = text, mood = mood, seconds = seconds });
        }

        public void SayNext(string text, Mood mood = Mood.Neutral, float seconds = 0f)
        {
            if (!visible || leaving || convoOpen) return;
            var l = new Line { text = text, mood = mood, seconds = seconds };
            if (current == null) Begin(l); else queue.Enqueue(l);
        }

        /// A hint: she comes over and says it in a dialog, then leaves (dialogs off: plain text on screen).
        public void SayHint(string text)
        {
            if (!DialogsOn)
            {
                if (GameUI.Instance != null) GameUI.Instance.Flash(text, SortItem.HintColor, 4.5f);
                return;
            }
            string[] openers = { "Need a hand?  Let's look at it together.", "Good idea to ask.  Here's where you are.", "Let me see...", "Stuck?  That's normal.  Look here." };
            string opener = openers[UnityEngine.Random.Range(0, openers.Length)];
            Converse(new List<(string, Mood)>
            {
                (opener, Mood.Think),
                ("<b>Hint:</b>  " + text, Mood.Present),
            }, skipped =>
            {
                if (!skipped) SayNext("Press H again any time.", Mood.Neutral, 1.8f);
                Leave();
            });
        }

        /// A gesture (and, while she is here and no question card is open, a short line).
        public void React(Mood mood, string line)
        {
            Gesture(mood);
            var quiz = CurrentQuiz();
            if (quiz == null || !quiz.Open) Say(line, Mood.Neutral, 2.2f);
        }

        void Begin(Line l)
        {
            current = l;
            lineStart = Time.time;
            lineEnd = Time.time + (l.seconds > 0f ? l.seconds : Duration(l.text));
            Gesture(l.mood);
            if (DialogsOn) GuideVoice.Speak(l.text);
        }

        static float Duration(string text)
        {
            int chars = Regex.Replace(text ?? "", "<[^>]+>", "").Length;
            return Mathf.Clamp(1.6f + chars / 16f, 2.5f, 11f);
        }

        void Gesture(Mood m)
        {
            if (animator == null || !visible) return;
            string t = m == Mood.Wave ? "Wave" : m == Mood.Present ? "Present" : m == Mood.Clap || m == Mood.Happy ? "Clap"
                     : m == Mood.Deny ? "Deny" : m == Mood.Think ? "Think" : null;
            if (t != null) animator.SetTrigger(t);
        }

        void UpdateSpeech()
        {
            if (current != null && Time.time >= lineEnd)
            {
                current = null;
                if (queue.Count > 0) Begin(queue.Dequeue());
            }
            bool talking = (convoOpen && !LineTyped) || lessonOpen || current != null || (visible && GuideVoice.Speaking);
            if (animator != null) animator.SetBool(TalkingId, talking && curSpeed < 0.3f);
        }

        // ------------------------------------------------------------ the building tells her what is happening

        /// The player has reached a floor (the top floor after the wake-up scene, or a hallway at the foot of the
        /// stairs): she comes over, welcomes them, teaches the floor's algorithm, says where to start and leaves.
        public void OnFloorStart(int floor, SortPuzzleBase puzzle)
        {
            watched = puzzle;
            if (floorsGreeted.Contains(floor)) return;
            floorsGreeted.Add(floor);
            if (!DialogsOn)
            {
                if (GameUI.Instance != null) GameUI.Instance.Subtitle($"Floor {floor}:  {AlgorithmName(floor)}.   T  lesson   ·   H  hint", 5f);
                return;
            }
            var lines = new List<(string, Mood)>();
            if (floor == 1)
            {
                lines.Add(($"Hello!  I'm {displayName}, your guide.  Five floors, one sorting algorithm on each.", Mood.Wave));
                lines.Add(("Every floor has three rooms:  Room 1 is the algorithm's <b>best case</b>, Room 2 its <b>average case</b>, Room 3 its <b>worst case</b>.  Watch how the work grows.", Mood.Present));
                lines.Add(("Solve a room, answer my three questions about it, and the next room opens.  After Room 3, the stairs open.", Mood.Neutral));
            }
            else lines.Add(($"Welcome to Floor {floor}:  <b>{AlgorithmName(floor)}</b>.", Mood.Wave));
            lines.Add((Concept(floor), Mood.Present));
            lines.Add(("Let me show you how it works.", Mood.Neutral));
            Converse(lines, skipped =>
            {
                if (skipped) { Leave(); return; }
                OpenLesson(floor, () => Converse(new List<(string, Mood)>
                {
                    ("Your turn.  " + (floor == 1 ? "" : "Room 1 is straight ahead, across the hallway.  ") + RoomIntro(floor, SortCase.Best), Mood.Present),
                    ("I'll leave you to it.  Press <b>H</b> whenever you want a hint, or <b>T</b> to see the lesson again, and I'll come back.", Mood.Wave),
                }, _ => Leave()));
            });
        }

        /// The algorithm in one sentence.
        public static string Concept(int floor)
        {
            switch (floor)
            {
                case 1: return "Bubble sort compares <b>neighbours</b>, left to right, and swaps them when the left one is bigger.  Each pass pushes the biggest value to the end.";
                case 2: return "Selection sort finds the <b>smallest</b> item that is left and moves it to the front, again and again.";
                case 3: return "Insertion sort takes one item at a time and <b>slides it left</b> into the sorted part, like sorting cards in your hand.";
                case 4: return "Merge sort <b>splits</b> the row in halves down to single items, then <b>merges</b> sorted runs back together.";
                default: return "Quick sort picks a <b>pivot</b>, puts smaller items left of it and bigger ones right of it, then does the same on each side.";
            }
        }

        /// The player has walked into a room (she is usually away by now; nothing to say).
        public void OnRoomEntered(int floor, int room, SortPuzzleBase puzzle)
        {
            watched = puzzle;
            watchedMistakes = puzzle != null ? puzzle.mistakes : 0;
            watchedHints = puzzle != null ? puzzle.hintsUsed : 0;
            slipsSinceHint = 0;
        }

        /// The room is done (the objective card says where to go next).
        public void OnRoomDone(int floor, int room, bool lastRoom, bool lastFloor) { }

        static string CaseName(int room) => room == 1 ? "best case" : room == 2 ? "average case" : "worst case";

        public static string AlgorithmName(int floor)
        {
            switch (floor)
            {
                case 1: return "bubble sort";
                case 2: return "selection sort";
                case 3: return "insertion sort";
                case 4: return "merge sort";
                default: return "quick sort";
            }
        }

        /// What this room's input is and what to look out for.
        public static string RoomIntro(int floor, SortCase c)
        {
            switch (floor)
            {
                case 1:
                    return c == SortCase.Best ? "Room 1, the best case:  these keys are almost in order.  Watch how little bubble sort has to do."
                         : c == SortCase.Worst ? "Room 3, the worst case:  the keys are in reverse order.  Every single comparison will be a swap."
                         : "Room 2, the average case:  a random row.  Expect about half of all the pairs to be out of order.";
                case 2:
                    return c == SortCase.Best ? "Room 1, the best case:  these crates are already sorted, but you can't see the weights.  Will selection sort notice?"
                         : c == SortCase.Worst ? "Room 3, the worst case for swaps:  the heaviest crate is first, so the crane must move on every pass."
                         : "Room 2, the average case:  random crates.  Weigh what's left, move the lightest, repeat.";
                case 3:
                    return c == SortCase.Best ? "Room 1, the best case:  the shelf is nearly sorted.  Most books will stay right where they are."
                         : c == SortCase.Worst ? "Room 3, the worst case:  the shelf is in reverse order.  Every book has to travel all the way left."
                         : "Room 2, the average case:  a random shelf.  Each book slides past about half of the sorted part.";
                case 4:
                    return c == SortCase.Best ? "Room 1, the best case:  the blocks are already in order.  Splitting and merging still happen, just with fewer comparisons."
                         : c == SortCase.Worst ? "Room 3, the worst case:  every merge will alternate between both runs right to the end."
                         : "Room 2, the average case:  random blocks.  Split down to single blocks, then merge pairs back together.";
                default:
                    return c == SortCase.Best ? "Room 1, the best case:  every pivot here will land in the middle of its range."
                         : c == SortCase.Worst ? "Room 3, quick sort's worst case:  the bars are already sorted, so every pivot is the biggest.  Count the comparisons!"
                         : "Room 2, the average case:  random bars.  Pivot, partition, then the same on each side.";
            }
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            var pc = PlayerController.Instance;
            var ui = GameUI.Instance;
            UpdateSpeech();
            WatchPuzzle();
            HandleInput(pc, ui);
            if (lessonOpen && lessonPlaying && !lessonTable && Time.unscaledTime >= nextLessonStep)
            {
                // with her voice on, the next step waits until she has finished the line (plus a short breath)
                if (GuideVoice.Speaking) voiceQuietSince = -1f;
                else if (voiceQuietSince < 0f) voiceQuietSince = Time.unscaledTime;
                if (!GuideVoice.Speaking && Time.unscaledTime >= voiceQuietSince + 0.45f) LessonNext();
            }
            // pause menu open: she stops talking
            if (ui != null && ui.PauseOpen && GuideVoice.Speaking) GuideVoice.Stop();
            if (pc != null && visible) Follow(pc);
            if (animator != null) animator.SetFloat(SpeedId, curSpeed);
        }

        void LateUpdate()
        {
            // Esc ends a dialog, the menu or the lesson (here, after the player script has seen the key, so it never pauses)
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (convoOpen) SkipDialog();
                else if (menuOpen) CloseMenu();
                else if (lessonOpen) { afterLesson = null; CloseLesson(); Leave(); }
            }

            // portrait camera: renders only while she is here and her face is on screen (the last picture stays for the questions)
            bool showFace = visible && (convoOpen || lessonOpen || menuOpen || (current != null && PanelVisible()));
            if (portraitCam.enabled != showFace) portraitCam.enabled = showFace;
            if (showFace)
            {
                portraitReady = true;
                Vector3 h = head != null ? head.position : transform.position + Vector3.up * 1.6f;
                Vector3 f = transform.forward; f.y = 0f; f.Normalize();
                portraitCam.transform.position = h + f * 0.75f + Vector3.up * 0.02f;
                portraitCam.transform.LookAt(h);
            }
        }

        bool CanOpenModal(PlayerController pc, GameUI ui)
        {
            if (ui == null || ui.PopupOpen || ui.PauseOpen || ui.EndCardOpen || ui.Fade > 0.3f || !ui.showHud) return false;
            if (pc != null && pc.cinematic) return false;
            return true;
        }

        void HandleInput(PlayerController pc, GameUI ui)
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            hovered = false;
            if (ui == null || pc == null) return;

            if (convoOpen)
            {
                if (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                    && Time.unscaledTime > convoLineAt + 0.15f)
                    DialogNext();
                return;
            }
            if (lessonOpen && kb != null)
            {
                if (kb.rightArrowKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame) { lessonPlaying = false; LessonNext(); }
                if (kb.leftArrowKey.wasPressedThisFrame) { lessonPlaying = false; LessonBack(); }
                if (kb.spaceKey.wasPressedThisFrame) { if (lessonTable) CloseLesson(); else { lessonPlaying = !lessonPlaying; nextLessonStep = Time.unscaledTime + 0.4f; } }
                return;
            }
            if (menuOpen) return;
            if (!CanOpenModal(pc, ui)) return;

            if (kb != null && kb.tKey.wasPressedThisFrame) { TeachMe(); return; }
            if (kb != null && kb.hKey.wasPressedThisFrame)
            {
                // the puzzle answers H itself while it is running; in the hallway she tells you where to go
                var p = CurrentPuzzle();
                if (p == null || !p.AcceptsInput)
                {
                    if (DialogsOn) Converse(new List<(string, Mood)> { ("Where to next?  " + WhatToDo(), Mood.Present) }, _ => Leave());
                    else ui.Flash(WhatToDo(), SortItem.HintColor, 4.5f);
                }
            }

            // pointed at? (only while she is here)
            if (!visible || leaving || clickCollider == null || pc.cam == null) return;
            bool pointer = PlayerController.PointerMode;
            Ray ray;
            if (pointer) { if (mouse == null) return; ray = pc.cam.ScreenPointToRay(mouse.position.ReadValue()); }
            else { if (Cursor.lockState != CursorLockMode.Locked) return; ray = pc.cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f)); }
            if (!clickCollider.Raycast(ray, out var hit, 30f)) return;
            if (Physics.Raycast(ray, out var wall, hit.distance - 0.05f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;
            hovered = true;
            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool use = kb != null && kb.eKey.wasPressedThisFrame;
            if ((click || use) && PlayerController.LockedThisFrame != Time.frameCount)
            {
                ui.ConsumeClick();
                OpenMenu();
            }
        }

        /// T: she comes over and teaches this floor's algorithm again (dialogs off: just the lesson).
        public void TeachMe()
        {
            int floor = CurrentFloor();
            if (!DialogsOn) { OpenLesson(floor, null); return; }
            Converse(new List<(string, Mood)> { ($"Of course.  Here's {AlgorithmName(floor)} again.", Mood.Present) }, skipped =>
            {
                if (skipped) { Leave(); return; }
                OpenLesson(floor, () => Converse(new List<(string, Mood)> { ("Your turn.  Press <b>H</b> if you want a hint.", Mood.Wave) }, _ => Leave()));
            });
        }

        void WatchPuzzle()
        {
            var p = CurrentPuzzle();
            if (p != watched) { watched = p; watchedMistakes = p != null ? p.mistakes : 0; watchedHints = p != null ? p.hintsUsed : 0; slipsSinceHint = 0; }
            if (p == null) return;
            if (p.hintsUsed != watchedHints) { watchedHints = p.hintsUsed; slipsSinceHint = 0; }
            if (p.mistakes > watchedMistakes)
            {
                slipsSinceHint += p.mistakes - watchedMistakes;
                watchedMistakes = p.mistakes;
                Gesture(Mood.Deny);
                if (slipsSinceHint >= 2)
                {
                    slipsSinceHint = 0;
                    if (GameUI.Instance != null)
                        GameUI.Instance.Subtitle(DialogsOn ? "Stuck?  Press H and Professor Ada will come with a hint." : "Stuck?  Press H for a hint, or T for the lesson.", 4f);
                }
            }
        }

        static SortPuzzleBase CurrentPuzzle()
        {
            var b = BuildingManager.Instance;
            return b != null ? b.CurrentPuzzle : FindAnyObjectByType<SortPuzzleBase>();
        }

        static RoomQuiz CurrentQuiz()
        {
            var p = CurrentPuzzle();
            return p != null ? p.quiz : null;
        }

        static int CurrentFloor()
        {
            var b = BuildingManager.Instance;
            if (b != null && b.FloorCount > 0) return b.Current.number;
            var p = CurrentPuzzle();
            return p != null ? p.floorNumber : 1;
        }

        static string WhatToDo()
        {
            var ui = GameUI.Instance;
            string obj = ui != null ? ui.objective : "";
            obj = Regex.Replace(obj ?? "", "<size=[^>]*>.*?</size>", "", RegexOptions.Singleline);
            obj = Regex.Replace(obj, "<[^>]+>", "").Replace("\n", "  ").Trim();
            return string.IsNullOrEmpty(obj) ? "Look around:  the glowing things are the ones you can use." : obj;
        }

        // ------------------------------------------------------------ walking: coming over, staying near, leaving

        void Follow(PlayerController pc)
        {
            var ui = GameUI.Instance;
            Vector3 me = transform.position, pl = pc.transform.position;
            float flat = Flat(me, pl);
            float dt = Time.deltaTime;

            if (leaving)
            {
                // gone once she is out of sight or far enough
                bool outOfView = true;
                if (pc.cam != null)
                {
                    Vector3 vp = pc.cam.WorldToViewportPoint(me + Vector3.up * 1.2f);
                    outOfView = vp.z < 0f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.1f || vp.y > 1.1f;
                }
                if ((outOfView && flat > 2.5f) || flat > 6f || Time.time > leaveSince + 4f || Flat(me, leaveGoal) < 0.3f) { SetVisible(false); return; }
            }

            bool hold = !approaching && !leaving && (lessonOpen || menuOpen || (ui != null && ui.PopupOpen) || pc.cinematic);
            if (!leaving && !hold && (flat > teleportDistance || Mathf.Abs(me.y - pl.y) > 3.6f)) { CatchUp(pc); return; }

            if (!leaving)
            {
                if (approaching && Flat(me, talkGoal) < 0.25f) { approaching = false; moving = false; corners.Clear(); }
                if (approaching && !moving) { moving = true; goingAside = false; nextRepath = 0f; }
                if (!hold && !moving && flat > followDistance) { moving = true; goingAside = false; nextRepath = 0f; }
                if (moving && !goingAside && !approaching && flat < keepDistance) { moving = false; corners.Clear(); }
                if (hold && moving && flat < followDistance) { moving = false; corners.Clear(); }
                if (approaching && !moving) { moving = true; nextRepath = 0f; }

                // never stand between the camera and the player
                if (!moving && !hold && Time.time >= nextAsideCheck)
                {
                    nextAsideCheck = Time.time + 0.5f;
                    if (Blocking(pc, out asideGoal)) { moving = true; goingAside = true; nextRepath = 0f; }
                }
            }

            if (moving && Time.time >= nextRepath)
            {
                nextRepath = Time.time + 0.35f;
                Vector3 goal = leaving ? leaveGoal : approaching ? talkGoal : goingAside ? asideGoal : GoalNear(pc, me, pl);
                if (!Repath(me, goal))
                {
                    if (failSince < 0f) failSince = Time.time;
                    if (leaving) { SetVisible(false); return; }
                    if (Time.time > failSince + 2.0f && flat > 3f) { CatchUp(pc); return; }
                }
                else failSince = -1f;
            }

            Vector3 moveDir = Vector3.zero;
            float target = 0f;
            if (moving && corner < corners.Count)
            {
                Vector3 c = corners[corner];
                Vector3 d = c - me; d.y = 0f;
                if (d.magnitude < 0.12f) { corner++; if (corner >= corners.Count && goingAside) { moving = false; goingAside = false; } }
                else { moveDir = d.normalized; target = leaving ? walkSpeed * 1.2f : flat > 5.5f ? runSpeed : walkSpeed; }
            }
            else if (moving && corner >= corners.Count && goingAside) { moving = false; goingAside = false; }

            curSpeed = Mathf.MoveTowards(curSpeed, target, (target > curSpeed ? 5f : 7f) * dt);
            if (curSpeed > 0.01f && moveDir != Vector3.zero)
            {
                me += moveDir * curSpeed * dt;
                if (NavMesh.SamplePosition(me + Vector3.up * 0.3f, out var hit, 1.2f, NavMesh.AllAreas))
                    me.y = Mathf.Lerp(me.y, hit.position.y, 0.5f);
                transform.position = me;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(moveDir), 10f * dt);
            }
            else if (!leaving && flat < 7f)
            {
                // stand facing the player
                Vector3 d = pl - me; d.y = 0f;
                if (d.sqrMagnitude > 0.04f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 220f * dt);
            }
        }

        /// A point about keepDistance from the player, on her side of the player (left of the camera if she has no side yet).
        Vector3 GoalNear(PlayerController pc, Vector3 me, Vector3 pl)
        {
            Vector3 away = me - pl; away.y = 0f;
            if (away.sqrMagnitude < 0.01f && pc.cam != null) away = -pc.cam.transform.right;
            away.Normalize();
            Vector3 g = pl + away * (keepDistance - 0.1f);
            if (NavMesh.SamplePosition(g, out var hit, 1.2f, NavMesh.AllAreas)) return hit.position;
            if (NavMesh.SamplePosition(pl, out hit, 1.5f, NavMesh.AllAreas)) return hit.position;
            return pl;
        }

        bool Repath(Vector3 from, Vector3 goal)
        {
            if (!NavMesh.SamplePosition(from, out var a, 1.5f, NavMesh.AllAreas)) return false;
            if (!NavMesh.CalculatePath(a.position, goal, NavMesh.AllAreas, navPath) || navPath.status == NavMeshPathStatus.PathInvalid) return false;
            corners.Clear();
            corners.AddRange(navPath.corners);
            corner = corners.Count > 1 ? 1 : 0;
            return navPath.status == NavMeshPathStatus.PathComplete;
        }

        /// Is she in the way of the camera's view of the player? Then where should she step to?
        bool Blocking(PlayerController pc, out Vector3 goal)
        {
            goal = Vector3.zero;
            if (pc.cam == null || pc.firstPerson) return false;
            Vector3 cam = pc.cam.transform.position, chest = pc.Chest;
            Vector3 mine = transform.position + Vector3.up * 1.2f;
            Vector3 seg = chest - cam;
            float t = Vector3.Dot(mine - cam, seg) / Mathf.Max(0.001f, seg.sqrMagnitude);
            if (t < 0f || t > 1.05f) return false;
            if (Vector3.Distance(cam + seg * t, mine) > 0.5f) return false;
            Vector3 left = -pc.cam.transform.right; left.y = 0f; left.Normalize();
            Vector3 g = pc.transform.position + left * 1.4f + pc.transform.forward * 0.3f;
            if (!NavMesh.SamplePosition(g, out var hit, 1.2f, NavMesh.AllAreas)) return false;
            goal = hit.position;
            return true;
        }

        /// Fell far behind (a jump between floors, a door that shut): appear just behind the player.
        void CatchUp(PlayerController pc)
        {
            Vector3 back = pc.cam != null ? -pc.cam.transform.forward : -pc.transform.forward;
            back.y = 0f; back.Normalize();
            Vector3 left = pc.cam != null ? -pc.cam.transform.right : -pc.transform.right;
            left.y = 0f; left.Normalize();
            Vector3 pl = pc.transform.position;
            foreach (var off in new[] { left * 1.2f + back * 0.4f, left * 1.3f, back * 1.3f, -left * 1.2f })
            {
                if (NavMesh.SamplePosition(pl + off, out var hit, 0.8f, NavMesh.AllAreas) && Mathf.Abs(hit.position.y - pl.y) < 0.6f)
                {
                    PlaceAt(hit.position, pl);
                    failSince = -1f;
                    return;
                }
            }
            PlaceAt(pl + left * 1.0f, pl);
            failSince = -1f;
        }

        /// Is she past a doorway (for doors that shut behind the player: they wait for her too)?
        public bool IsPast(Vector3 doorway, Vector3 inward, float metres)
        {
            Vector3 d = transform.position - doorway;
            return Vector3.Dot(d, inward) > metres || Mathf.Abs(d.y) > 2f;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        // ------------------------------------------------------------ menu and lesson

        void OpenMenu()
        {
            menuOpen = true;
            SetModal(true);
            Gesture(Mood.Wave);
        }

        void CloseMenu()
        {
            menuOpen = false;
            SetModal(false);
        }

        /// The on-screen lesson for a floor; then() runs when it is closed normally (not with Esc).
        public void OpenLesson(int floor, Action then)
        {
            if (lessonOpen) return;
            if (menuOpen) menuOpen = false;
            lessonFloor = Mathf.Clamp(floor, 1, 5);
            lesson = LessonScript.Steps(lessonFloor);
            lessonStep = 0;
            lessonStepAt = Time.unscaledTime;
            lessonTable = false;
            lessonPlaying = true;
            nextLessonStep = Time.unscaledTime + 2.6f;
            lessonOpen = true;
            afterLesson = then;
            SetModal(true);
            Gesture(Mood.Present);
            current = null;
            queue.Clear();
            SpeakLessonLine();
        }

        /// Her line for the lesson step on screen (or the summary).
        void SpeakLessonLine()
        {
            voiceQuietSince = -1f;
            if (!DialogsOn) return;
            if (lessonTable)
            {
                LessonScript.Cases(lessonFloor, out string memory, out string takeaway);
                GuideVoice.Speak("These are the three cases you'll meet on this floor. " + takeaway);
            }
            else if (lesson != null && lessonStep < lesson.Count) GuideVoice.Speak(lesson[lessonStep].caption);
        }

        public void CloseLesson()
        {
            if (!lessonOpen) return;
            GuideVoice.Stop();
            lessonOpen = false;
            SetModal(false);
            var then = afterLesson;
            afterLesson = null;
            if (then != null) then();
            else if (visible) Leave();
        }

        void LessonNext()
        {
            if (lesson == null || lessonTable) return;
            if (lessonStep < lesson.Count - 1) { lessonStep++; lessonStepAt = Time.unscaledTime; nextLessonStep = Time.unscaledTime + StepTime(); }
            else { lessonTable = true; lessonPlaying = false; }
            SpeakLessonLine();
        }

        void LessonBack()
        {
            if (lessonTable) { lessonTable = false; SpeakLessonLine(); return; }
            if (lessonStep > 0) { lessonStep--; lessonStepAt = Time.unscaledTime; }
            SpeakLessonLine();
        }

        float StepTime()
        {
            var st = lesson != null && lessonStep < lesson.Count ? lesson[lessonStep] : null;
            return st == null ? 1.8f : Mathf.Clamp(1.4f + VisibleChars(st.caption) / 28f, 2.0f, 4.5f);
        }

        void SetModal(bool on)
        {
            var ui = GameUI.Instance;
            if (ui != null) ui.ModalOpen = on;
            if (on)
            {
                PlayerController.FreeCursor();
                if (PlayerController.Instance != null) PlayerController.Instance.StopWalking();
            }
            else
            {
                PlayerController.ApplyCursorForMode();
                PlayerController.LockedThisFrame = Time.frameCount;
                if (ui != null) ui.ConsumeClick();
            }
        }

        // ------------------------------------------------------------ drawing

        bool PanelVisible()
        {
            var ui = GameUI.Instance;
            if (ui == null || !ui.showHud || ui.EndCardOpen || ui.DebriefOpen || ui.PauseOpen || ui.Fade > 0.5f) return false;
            var quiz = CurrentQuiz();
            return !lessonOpen && !menuOpen && !convoOpen && !(quiz != null && quiz.Open);
        }

        void OnGUI()
        {
            if (Screen.height <= 0) return;
            var ui = GameUI.Instance;
            if (ui == null || ui.EndCardOpen || ui.Fade > 0.6f) return;
            GUI.depth = -4;
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 1080f;
            Styles();

            if (hovered && visible && !menuOpen && !lessonOpen && !convoOpen) NameTag(s);
            if (current != null && visible && PanelVisible()) SpeechPanel(W, H);
            if (menuOpen) Menu(W, H);
            if (lessonOpen) LessonPanel(W, H);
            if (convoOpen && !ui.PauseOpen) DialogBox(W, H);
        }

        /// The dialog box: her face, her name, the line typing out, Next / Skip / Turn dialogs off.
        void DialogBox(float W, float H)
        {
            if (convoIdx >= convo.Count) return;
            var line = convo[convoIdx];
            float pw = Mathf.Min(1200f, W - 80f);
            float textW = pw - 230f - 40f;
            float th = sDialog.CalcHeight(new GUIContent(line.text), textW);
            float ph = Mathf.Max(230f, 70f + th + 80f);
            Rect r = new Rect((W - pw) * 0.5f, H - 36f - ph, pw, ph);
            GameUI.Shadow(r);
            GameUI.Box(r, GameUI.CardSolid);
            GameUI.Box(new Rect(r.x, r.y, pw, 6f), Blue);

            Rect pr = new Rect(r.x + 28f, r.y + 28f, 176f, 176f);
            GameUI.Box(new Rect(pr.x - 3f, pr.y - 3f, pr.width + 6f, pr.height + 6f), Blue);
            if (portraitRT != null) GUI.DrawTexture(pr, portraitRT, ScaleMode.ScaleAndCrop);

            float x = r.x + 230f;
            GameUI.Text(new Rect(x, r.y + 24f, textW, 28f), displayName.ToUpperInvariant(), sName, Blue);
            GameUI.Text(new Rect(r.xMax - 40f - 160f, r.y + 24f, 160f, 28f), $"{convoIdx + 1} / {convo.Count}", sSmall, GameUI.InkSoft);
            int shown = Mathf.Clamp(TypedChars(line.text, convoLineAt), 0, line.text.Length);
            GameUI.Text(new Rect(x, r.y + 60f, textW, th + 6f), RichPrefix(line.text, shown), sDialog, GameUI.Ink);

            // buttons
            float by = r.yMax - 62f;
            bool last = convoIdx >= convo.Count - 1;
            if (GameUI.Button(new Rect(r.xMax - 40f - 220f, by, 220f, 46f), LineTyped ? (last ? "Done   (Space)" : "Next  ▶   (Space)") : "▶▶   (Space)", sDialogBtn)) DialogNext();
            if (GameUI.Button(new Rect(r.xMax - 40f - 220f - 14f - 170f, by, 170f, 46f), "Skip   (Esc)", sDialogBtn)) SkipDialog();
            if (GameUI.Button(new Rect(x, by, 210f, 46f), "Turn dialogs off", sDialogBtn)) DialogsOn = false;
            if (GameUI.Button(new Rect(x + 222f, by, 150f, 46f), GuideVoice.On ? "Voice: on" : "Voice: off", sDialogBtn))
            {
                GuideVoice.On = !GuideVoice.On;
                if (GuideVoice.On) GuideVoice.Speak(line.text);
            }
            // something on the computer keeps her from being heard (e.g. the Mac's sound is muted)
            if (GuideVoice.On && GuideVoice.Problem != null)
                GameUI.Text(new Rect(x, by - 30f, textW, 24f), GuideVoice.Problem, sSmall, new Color(0.82f, 0.30f, 0.18f));
            // a click anywhere else on the box moves on too
            var e = Event.current;
            if (e.type == EventType.MouseDown && r.Contains(e.mousePosition) && e.mousePosition.y < by - 6f) { DialogNext(); e.Use(); }
        }

        void NameTag(float s)
        {
            var pc = PlayerController.Instance;
            if (pc == null || pc.cam == null) return;
            Vector3 top = (head != null ? head.position : transform.position + Vector3.up * 1.6f) + Vector3.up * 0.32f;
            Vector3 sp = pc.cam.WorldToScreenPoint(top);
            if (sp.z < 0f) return;
            Vector2 g = new Vector2(sp.x / s, (Screen.height - sp.y) / s);
            string t = $"<b>{displayName}</b>   ·   click to ask";
            Vector2 sz = sTag.CalcSize(new GUIContent(t));
            Rect r = new Rect(g.x - sz.x * 0.5f - 16f, g.y - 40f, sz.x + 32f, 36f);
            GameUI.Box(r, new Color(0.07f, 0.08f, 0.10f, 0.8f));
            GameUI.Text(new Rect(r.x + 16f, r.y + 7f, sz.x + 4f, 26f), t, sTag, Color.white);
        }

        void SpeechPanel(float W, float H)
        {
            float pw = Mathf.Min(800f, W * 0.46f);
            float textW = pw - 150f - 30f;
            string full = current.text;
            float th = sText.CalcHeight(new GUIContent(full), textW);
            float ph = Mathf.Max(132f, 46f + th + 38f);
            var ui = GameUI.Instance;
            float bottom = ui != null && ui.SubtitleShowing ? H - 186f : H - 96f;     // above a subtitle line
            Rect r = new Rect(W - 28f - pw, bottom - ph, pw, ph);
            GameUI.Shadow(r);
            GameUI.Box(r, GameUI.CardSolid);
            GameUI.Box(new Rect(r.x, r.y, 5f, r.height), Blue);
            Rect pr = new Rect(r.x + 18f, r.y + 16f, 112f, 112f);
            GameUI.Box(new Rect(pr.x - 2f, pr.y - 2f, pr.width + 4f, pr.height + 4f), Blue);
            if (portraitRT != null) GUI.DrawTexture(pr, portraitRT, ScaleMode.ScaleAndCrop);
            GameUI.Text(new Rect(r.x + 150f, r.y + 14f, textW, 26f), displayName.ToUpperInvariant(), sName, Blue);
            int shown = Mathf.Clamp(Mathf.FloorToInt((Time.time - lineStart) * 55f), 0, full.Length);
            GameUI.Text(new Rect(r.x + 150f, r.y + 44f, textW, th + 4f), RichPrefix(full, shown), sText, GameUI.Ink);
            GameUI.Text(new Rect(r.x + 150f, r.yMax - 30f, textW, 22f), "H hint   ·   T lesson   ·   click me to ask", sSmall, GameUI.InkSoft);
        }

        /// The first 'count' visible characters of a rich-text string, with every opened tag still closed.
        static string RichPrefix(string s, int count)
        {
            if (count >= s.Length) return s;
            var sb = new System.Text.StringBuilder();
            var open = new Stack<string>();
            int visible = 0, i = 0;
            while (i < s.Length && visible < count)
            {
                if (s[i] == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) break;
                    string tag = s.Substring(i, end - i + 1);
                    sb.Append(tag);
                    if (tag.StartsWith("</")) { if (open.Count > 0) open.Pop(); }
                    else { string name = tag.Substring(1, tag.Length - 2).Split('=', ' ')[0]; open.Push(name); }
                    i = end + 1;
                    continue;
                }
                sb.Append(s[i]);
                visible++;
                i++;
            }
            while (open.Count > 0) sb.Append("</" + open.Pop() + ">");
            return sb.ToString();
        }

        void Menu(float W, float H)
        {
            float mw = 560f;
            string[] items = { "Give me a hint   (H)", "Teach me this algorithm   (T)", "What do I do here?", "What is this room's case?", "Turn dialogs off", "Close   (Esc)" };
            float mh = 150f + items.Length * 64f + 20f;
            Rect r = new Rect((W - mw) * 0.5f + 240f, (H - mh) * 0.5f, mw, mh);
            GameUI.Box(new Rect(0f, 0f, W, H), new Color(0.95f, 0.95f, 0.95f, 0.22f));
            GameUI.Shadow(r);
            GameUI.Box(r, GameUI.CardSolid);
            GameUI.Box(new Rect(r.x, r.y, mw, 7f), Blue);
            Rect pr = new Rect(r.x + 30f, r.y + 30f, 96f, 96f);
            GameUI.Box(new Rect(pr.x - 2f, pr.y - 2f, pr.width + 4f, pr.height + 4f), Blue);
            if (portraitRT != null) GUI.DrawTexture(pr, portraitRT, ScaleMode.ScaleAndCrop);
            GameUI.Text(new Rect(r.x + 146f, r.y + 40f, mw - 170f, 30f), displayName.ToUpperInvariant(), sName, Blue);
            GameUI.Text(new Rect(r.x + 146f, r.y + 72f, mw - 170f, 60f), "What would you like to know?", sText, GameUI.Ink);
            float y = r.y + 146f;
            for (int i = 0; i < items.Length; i++)
            {
                if (GameUI.Button(new Rect(r.x + 30f, y, mw - 60f, 54f), items[i], sMenu)) MenuPick(i);
                y += 64f;
            }
            var e = Event.current;
            if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { CloseMenu(); e.Use(); }
        }

        void MenuPick(int i)
        {
            CloseMenu();
            var p = CurrentPuzzle();
            switch (i)
            {
                case 0:
                    if (p != null && p.AcceptsInput) p.RequestHint();
                    else Converse(new List<(string, Mood)> { ("Where to next?  " + WhatToDo(), Mood.Present) }, _ => Leave());
                    break;
                case 1: TeachMe(); break;
                case 2: Converse(new List<(string, Mood)> { (WhatToDo(), Mood.Present) }, _ => Leave()); break;
                case 3:
                    if (p != null && p.sortCase != SortCase.Classic)
                        Converse(new List<(string, Mood)> { (RoomIntro(p.floorNumber, p.sortCase), Mood.Present), (CaseWhy(p.floorNumber, p.sortCase), Mood.Think) }, _ => Leave());
                    else Converse(new List<(string, Mood)> { ($"This floor is {AlgorithmName(CurrentFloor())}.  Press T for the lesson.", Mood.Present) }, _ => Leave());
                    break;
                case 4: DialogsOn = false; break;
                default: Leave(); break;
            }
        }

        /// One more sentence on why this input is that case.
        static string CaseWhy(int floor, SortCase c)
        {
            switch (floor)
            {
                case 1: return c == SortCase.Best ? "On a sorted row bubble sort needs one pass:  O(n)." : c == SortCase.Worst ? "Reversed, every pair is wrong:  n(n−1)/2 swaps, O(n²)." : "Random input still costs O(n²) on average.";
                case 2: return "Selection sort makes n(n−1)/2 comparisons in every case:  only the swaps change.";
                case 3: return c == SortCase.Best ? "Nearly sorted, each book barely moves:  close to O(n)." : c == SortCase.Worst ? "Reversed, book k shifts past k−1 books:  O(n²)." : "On average a book moves past half the sorted part:  O(n²).";
                case 4: return "Merge sort always has log₂n levels, so every case is O(n log n);  only the comparisons per merge change.";
                default: return c == SortCase.Best ? "Balanced splits give log₂n levels:  O(n log n)." : c == SortCase.Worst ? "Each split peels off one bar:  n levels, O(n²)." : "Random pivots split well enough on average:  O(n log n).";
            }
        }

        void LessonPanel(float W, float H)
        {
            float pw = Mathf.Min(1240f, W - 80f), ph = 790f;
            Rect r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
            GameUI.Box(new Rect(0f, 0f, W, H), new Color(0.95f, 0.95f, 0.95f, 0.30f));
            GameUI.Shadow(r);
            GameUI.Box(r, GameUI.CardSolid);
            GameUI.Box(new Rect(r.x, r.y, pw, 7f), Blue);

            GameUI.Text(new Rect(r.x + 60f, r.y + 34f, pw - 120f, 50f), LessonScript.Title(lessonFloor), sTitle, Blue);
            GameUI.Text(new Rect(r.xMax - 60f - 300f, r.y + 46f, 300f, 30f),
                        lessonTable ? "summary" : $"step {lessonStep + 1} of {lesson.Count}", sSmall, GameUI.InkSoft);

            if (!lessonTable) DrawBars(new Rect(r.x + 60f, r.y + 128f, pw - 120f, 340f), lesson[lessonStep]);
            else DrawTable(new Rect(r.x + 60f, r.y + 112f, pw - 120f, 420f));

            // her line for this step, as a dialog strip with her face
            string cap = lessonTable ? "These are the three cases you'll meet on this floor:  Room 1, Room 2 and Room 3." : lesson[lessonStep].caption;
            Rect dr = new Rect(r.x + 40f, r.y + 500f, pw - 80f, 150f);
            if (lessonTable) dr = new Rect(r.x + 40f, r.y + 560f, pw - 80f, 110f);
            GameUI.Box(dr, new Color(0.90f, 0.94f, 0.99f));
            GameUI.Box(new Rect(dr.x, dr.y, 5f, dr.height), Blue);
            float face = dr.height - 24f;
            Rect pr = new Rect(dr.x + 18f, dr.y + 12f, face, face);
            GameUI.Box(new Rect(pr.x - 2f, pr.y - 2f, pr.width + 4f, pr.height + 4f), Blue);
            if (portraitRT != null) GUI.DrawTexture(pr, portraitRT, ScaleMode.ScaleAndCrop);
            float tx = pr.xMax + 22f, tw = dr.xMax - tx - 20f;
            GameUI.Text(new Rect(tx, dr.y + 12f, tw, 24f), displayName.ToUpperInvariant(), sName, Blue);
            int shown = Mathf.Clamp(TypedChars(cap, lessonStepAt), 0, cap.Length);
            GameUI.Text(new Rect(tx, dr.y + 42f, tw, dr.height - 50f), RichPrefix(cap, shown), sCap, GameUI.Ink);

            // controls
            float by = r.yMax - 82f, bw = 210f;
            if (!lessonTable)
            {
                GUI.enabled = lessonStep > 0;
                if (GameUI.Button(new Rect(r.x + 60f, by, bw, 52f), "◀  Back   (←)", sMenu)) { lessonPlaying = false; LessonBack(); }
                GUI.enabled = true;
                if (GameUI.Button(new Rect(r.x + 60f + bw + 14f, by, bw, 52f), lessonPlaying ? "Pause   (Space)" : "Play   (Space)", sMenu))
                { lessonPlaying = !lessonPlaying; nextLessonStep = Time.unscaledTime + 0.4f; }
                if (GameUI.Button(new Rect(r.x + 60f + 2 * (bw + 14f), by, bw, 52f), "Next  ▶   (→)", sMenu)) { lessonPlaying = false; LessonNext(); }
                if (GameUI.Button(new Rect(r.xMax - 60f - 250f, by, 250f, 52f), "Skip to the summary", sMenu)) { lessonTable = true; lessonPlaying = false; SpeakLessonLine(); }
            }
            else
            {
                if (GameUI.Button(new Rect(r.x + 60f, by, bw, 52f), "◀  Watch again", sMenu)) { lessonTable = false; lessonStep = 0; lessonStepAt = Time.unscaledTime; lessonPlaying = true; nextLessonStep = Time.unscaledTime + 1.2f; SpeakLessonLine(); }
                if (GameUI.Button(new Rect(r.xMax - 60f - 300f, by, 300f, 52f), "Got it, let's sort   (Space)", sMenu)) CloseLesson();
            }
            GameUI.Text(new Rect(r.x + 60f, r.yMax - 24f, pw - 120f, 20f), "Esc closes the lesson  ·  T opens it again any time", sSmall, GameUI.InkSoft);
        }

        void DrawBars(Rect area, LessonStep st)
        {
            int n = st.values.Length;
            int maxV = 1;
            foreach (int v in st.values) maxV = Mathf.Max(maxV, v);
            float gap = 18f, runGap = 34f;
            int gaps = 0;
            if (st.gapAfter != null) foreach (bool g in st.gapAfter) if (g) gaps++;
            float bw = Mathf.Min(110f, (area.width - (n - 1) * gap - gaps * runGap) / n);
            float total = n * bw + (n - 1) * gap + gaps * runGap;
            float x = area.x + (area.width - total) * 0.5f;
            float baseY = area.yMax - 34f;
            for (int i = 0; i < n; i++)
            {
                if (st.divider == i)
                {
                    GameUI.Box(new Rect(x - gap * 0.5f - 3f, area.y + 10f, 6f, baseY - area.y), new Color(0.86f, 0.25f, 0.20f, 0.9f));
                    GameUI.Text(new Rect(x - gap * 0.5f - 40f, area.y - 20f, 80f, 24f), "gate", sSmall, new Color(0.86f, 0.25f, 0.20f));
                }
                float h = Mathf.Lerp(40f, baseY - area.y - 20f, st.values[i] / (float)maxV);
                Rect b = new Rect(x, baseY - h, bw, h);
                GameUI.Box(b, MarkColor(st.marks[i]));
                GameUI.Text(new Rect(x, baseY + 4f, bw, 30f), st.values[i].ToString(), sBar, st.marks[i] == LessonStep.Dim ? new Color(0.7f, 0.7f, 0.7f) : GameUI.Ink);
                x += bw + gap;
                if (st.gapAfter != null && st.gapAfter[i]) x += runGap;
            }
            if (st.divider >= n)
            {
                float gx = x - gap * 0.5f;
                GameUI.Box(new Rect(gx - 3f, area.y + 10f, 6f, baseY - area.y), new Color(0.86f, 0.25f, 0.20f, 0.9f));
            }
            // legend
            float lx = area.x;
            LegendItem(ref lx, area.y - 28f, LessonStep.Compare, "comparing");
            LegendItem(ref lx, area.y - 28f, LessonStep.Move, "moving");
            LegendItem(ref lx, area.y - 28f, LessonStep.Sorted, "in its final place");
            if (lessonFloor == 5) LegendItem(ref lx, area.y - 28f, LessonStep.Pivot, "pivot");
            if (lessonFloor == 2) LegendItem(ref lx, area.y - 28f, LessonStep.Min, "smallest so far");
            if (lessonFloor == 3) LegendItem(ref lx, area.y - 28f, LessonStep.Min, "value being inserted");
        }

        void LegendItem(ref float x, float y, int mark, string label)
        {
            GameUI.Box(new Rect(x, y + 4f, 16f, 16f), MarkColor(mark));
            float w = sSmall.CalcSize(new GUIContent(label)).x;
            GameUI.Text(new Rect(x + 24f, y + 1f, w + 4f, 22f), label, sSmall, GameUI.InkSoft);
            x += 24f + w + 30f;
        }

        static Color MarkColor(int m)
        {
            switch (m)
            {
                case LessonStep.Compare: return new Color(0.89f, 0.64f, 0.10f);
                case LessonStep.Move: return new Color(0.92f, 0.41f, 0.20f);
                case LessonStep.Sorted: return new Color(0.18f, 0.62f, 0.43f);
                case LessonStep.Pivot: return new Color(0.48f, 0.33f, 0.78f);
                case LessonStep.Min: return new Color(0.16f, 0.47f, 0.84f);
                case LessonStep.Dim: return new Color(0.88f, 0.89f, 0.91f);
                default: return new Color(0.62f, 0.67f, 0.74f);
            }
        }

        void DrawTable(Rect area)
        {
            var rows = LessonScript.Cases(lessonFloor, out string memory, out string takeaway);
            float[] cw = { 0.17f, 0.37f, 0.26f, 0.20f };
            string[] head = { "Case", "Input that causes it", "Comparisons", "Running time" };
            float y = area.y, rh = 64f;
            float x = area.x;
            GameUI.Box(new Rect(area.x, y, area.width, rh - 8f), new Color(0.16f, 0.42f, 0.78f, 0.12f));
            for (int c = 0; c < 4; c++) { GameUI.Text(new Rect(x + 18f, y + 16f, area.width * cw[c] - 24f, 30f), head[c], sCellB, Blue); x += area.width * cw[c]; }
            y += rh;
            string[] roomName = { "Room 1", "Room 2", "Room 3" };
            for (int k = 0; k < rows.Length; k++)
            {
                x = area.x;
                GameUI.Box(new Rect(area.x, y, area.width, rh - 8f), k % 2 == 0 ? new Color(0.95f, 0.95f, 0.94f) : new Color(0.98f, 0.98f, 0.97f));
                string[] cells = { $"<b>{rows[k].name}</b>  <size=17>{roomName[k]}</size>", rows[k].input, rows[k].count, $"<b>{rows[k].bigO}</b>" };
                for (int c = 0; c < 4; c++) { GameUI.Text(new Rect(x + 18f, y + 15f, area.width * cw[c] - 24f, 40f), cells[c], sCell, GameUI.Ink); x += area.width * cw[c]; }
                y += rh;
            }
            y += 14f;
            GameUI.Text(new Rect(area.x, y, area.width, 30f), memory, sCell, GameUI.InkSoft);
            y += 44f;
            GameUI.Text(new Rect(area.x, y, area.width, 70f), takeaway, sCap, GameUI.Ink);
        }

        void Styles()
        {
            if (sName != null && sName.font != null) return;
            Font f = GameUI.HudFont();
            GUIStyle Make(int size, FontStyle fs, TextAnchor a, bool wrap)
            {
                var st = new GUIStyle
                {
                    font = f, fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap, richText = true,
                    clipping = TextClipping.Overflow, padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
                };
                st.normal.textColor = Color.white;
                return st;
            }
            sName = Make(19, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sText = Make(23, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sSmall = Make(17, FontStyle.Normal, TextAnchor.UpperLeft, false);
            sCap = Make(26, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sBar = Make(24, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sTitle = Make(38, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sCell = Make(23, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sCellB = Make(21, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sMenu = Make(21, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sTag = Make(19, FontStyle.Normal, TextAnchor.UpperLeft, false);
            sDialog = Make(29, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sDialogBtn = Make(20, FontStyle.Bold, TextAnchor.UpperCenter, false);
        }
    }
}
