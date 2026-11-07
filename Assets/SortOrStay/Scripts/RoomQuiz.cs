using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// The room check: after the sort and its debrief, Professor Ada asks three multiple-choice questions about
    /// this algorithm in this room's case (best, average or worst). A wrong pick is greyed out, counted and answered
    /// with a nudge; the right pick shows why it's right. When all three are answered the door opens.
    /// Keys 1-4 (or a click) pick an answer, Enter / Space / E (or Next) goes on.
    [DefaultExecutionOrder(120)]
    public class RoomQuiz : MonoBehaviour
    {
        public SortPuzzleBase puzzle;

        public bool Passed { get; private set; }
        public bool Open { get; private set; }
        public int WrongAnswers { get; private set; }

        /// Question on screen (for the play-test tool): index, the question, whether it is answered right.
        public int Index => qi;
        public QuizQuestion Current => qs != null && qi < qs.Count ? qs[qi] : null;
        public bool AnsweredRight => answered;
        public bool IsWrongPick(int k) => wrongPicks.Contains(k);

        List<QuizQuestion> qs;
        int qi;
        bool answered;
        readonly HashSet<int> wrongPicks = new HashSet<int>();
        string feedback;
        bool feedbackGood;
        bool armed, sawDebrief;
        float armedAt, shownAt;

        GUIStyle sHead, sQ, sOpt, sFeed, sFoot, sNum;
        static readonly Color Good = new Color(0.20f, 0.62f, 0.34f);
        static readonly Color Bad = new Color(0.86f, 0.25f, 0.20f);
        static readonly Color Blue = new Color(0.16f, 0.42f, 0.78f);

        /// Called by the puzzle when it is solved: the questions start once its debrief card has been closed.
        public void Arm(SortPuzzleBase p)
        {
            if (Passed || Open) return;
            puzzle = p;
            armed = true;
            sawDebrief = false;
            armedAt = Time.time;
        }

        void Update()
        {
            var ui = GameUI.Instance;
            if (armed && !Open && !Passed)
            {
                if (ui != null && ui.DebriefOpen) sawDebrief = true;
                bool debriefUp = ui != null && ui.DebriefOpen;
                bool ready = !debriefUp && (sawDebrief || Time.time > armedAt + 9f);
                if (ready && !(ui != null && ui.EndCardOpen)) Begin();
            }
            if (!Open) return;

            var kb = Keyboard.current;
            if (kb == null || Time.unscaledTime < shownAt + 0.35f) return;
            if (!answered)
            {
                for (int k = 0; k < 4; k++)
                    if (kb[Key.Digit1 + k].wasPressedThisFrame || kb[Key.Numpad1 + k].wasPressedThisFrame) Pick(k);
            }
            else if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame)
                Next();
        }

        public void Begin()
        {
            if (Open || Passed || puzzle == null) return;
            armed = false;
            qs = QuizBank.For(puzzle);
            qi = 0;
            ResetQuestion();
            Open = true;
            var ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ModalOpen = true;
                ui.SetObjective($"Answer Professor Ada's 3 questions about <b>{puzzle.Algorithm.ToLowerInvariant()}</b> in its <b>{puzzle.CaseName}</b>.  All three right opens the door.");
            }
            PlayerController.FreeCursor();
            if (PlayerController.Instance != null) PlayerController.Instance.StopWalking();
            var g = GuideNpc.Instance;
            if (g != null && g.isActiveAndEnabled)
                g.Say($"Well sorted!  Three quick questions about the {puzzle.CaseName} before I open the door.", GuideNpc.Mood.Present, 4f);
            ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.5f);
        }

        void ResetQuestion()
        {
            answered = false;
            wrongPicks.Clear();
            feedback = null;
            shownAt = Time.unscaledTime;
            var q = Current;
            if (q != null) Voice(q.text);
        }

        /// Ada reads the questions and her answers aloud (when her dialogs and voice are on).
        static void Voice(string text)
        {
            if (GuideNpc.DialogsOn) GuideVoice.Speak(text);
        }

        /// Answer option k (0-3, in the order shown).
        public void Pick(int k)
        {
            var q = Current;
            if (!Open || answered || q == null || k < 0 || k >= q.options.Length || wrongPicks.Contains(k)) return;
            var g = GuideNpc.Instance;
            bool guide = g != null && g.isActiveAndEnabled;
            if (k == q.correct)
            {
                answered = true;
                feedback = q.explain;
                feedbackGood = true;
                Voice("Right.  " + q.explain);
                ProceduralSfx.PlayUI(ProceduralSfx.Unlock, 0.45f);
                if (guide) g.React(GuideNpc.Mood.Happy, "Exactly right.");
            }
            else
            {
                wrongPicks.Add(k);
                WrongAnswers++;
                feedback = "Not quite.  " + q.hint;
                feedbackGood = false;
                Voice(feedback);
                ProceduralSfx.PlayUI(ProceduralSfx.Error, 0.5f);
                if (guide) g.React(GuideNpc.Mood.Deny, "Not quite. Try again.");
            }
        }

        public void Next()
        {
            if (!Open || !answered) return;
            qi++;
            if (qs == null || qi >= qs.Count) { Finish(); return; }
            ResetQuestion();
        }

        void Finish()
        {
            Open = false;
            Passed = true;
            Voice(WrongAnswers == 0 ? "All three right, first time. The door is open." : "All three answered. The door is open.");
            var ui = GameUI.Instance;
            if (ui != null)
            {
                ui.ModalOpen = false;
                ui.SetObjective(puzzle.OpenObjective);
            }
            PlayerController.ApplyCursorForMode();
            PlayerController.LockedThisFrame = Time.frameCount;
            var r = GameState.Instance.Get(puzzle.floorNumber, puzzle.roomNumber);
            if (r != null) r.quizSlips = WrongAnswers;
            if (puzzle.door != null) puzzle.door.Unlock();
            var g = GuideNpc.Instance;
            if (g != null && g.isActiveAndEnabled)
                g.Say(WrongAnswers == 0 ? "All three right, first time!  The door is open." : "All three answered.  The door is open.", GuideNpc.Mood.Clap, 3.5f);
        }

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (!Open || Screen.height <= 0) return;
            var ui = GameUI.Instance;
            if (ui != null && (ui.EndCardOpen || ui.Fade > 0.5f)) return;
            GUI.depth = -5;
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 1080f;
            Styles();
            var q = Current;
            if (q == null) return;

            float pw = Mathf.Min(1120f, W - 80f);
            float inner = pw - 96f;
            float qh = sQ.CalcHeight(new GUIContent(q.text), inner - 150f);
            float optH = 0f;
            var optHeights = new float[q.options.Length];
            for (int i = 0; i < q.options.Length; i++)
            {
                optHeights[i] = Mathf.Max(58f, sOpt.CalcHeight(new GUIContent(q.options[i]), inner - 90f) + 26f);
                optH += optHeights[i] + 10f;
            }
            float fh = string.IsNullOrEmpty(feedback) ? 0f : sFeed.CalcHeight(new GUIContent(feedback), inner - 40f) + 34f;
            float ph = 36f + 40f + 18f + Mathf.Max(qh, 120f) + 24f + optH + (fh > 0 ? fh + 14f : 0f) + 64f;
            Rect r = new Rect((W - pw) * 0.5f, Mathf.Max(20f, (H - ph) * 0.5f), pw, ph);

            GameUI.Box(new Rect(0f, 0f, W, H), new Color(0.95f, 0.95f, 0.95f, 0.30f));
            GameUI.Shadow(r);
            GameUI.Box(r, GameUI.CardSolid);
            GameUI.Box(new Rect(r.x, r.y, pw, 7f), Blue);

            float x = r.x + 48f, y = r.y + 30f;
            string head = $"ROOM CHECK  ·  {puzzle.Algorithm.ToUpperInvariant()}  ·  {puzzle.CaseName.ToUpperInvariant()}";
            GameUI.Text(new Rect(x, y, inner - 200f, 30f), head, sHead, Blue);
            GameUI.Text(new Rect(r.xMax - 48f - 220f, y, 220f, 30f), $"QUESTION {qi + 1} OF {qs.Count}", sHead, GameUI.InkSoft);
            // progress dots
            for (int i = 0; i < qs.Count; i++)
                GameUI.Box(new Rect(r.xMax - 48f - 220f + i * 26f, y + 34f, 18f, 6f), i < qi ? Good : i == qi ? Blue : new Color(0.85f, 0.85f, 0.85f));
            y += 58f;

            // Ada's face, then the question
            var g = GuideNpc.Instance;
            Texture face = g != null ? g.Portrait : null;
            Rect pr = new Rect(x, y, 120f, 120f);
            GameUI.Box(new Rect(pr.x - 3f, pr.y - 3f, pr.width + 6f, pr.height + 6f), Blue);
            if (face != null) GUI.DrawTexture(pr, face, ScaleMode.ScaleAndCrop);
            else { GameUI.Box(pr, new Color(0.9f, 0.92f, 0.96f)); GameUI.Text(pr, "ADA", sNum, Blue); }
            GameUI.Text(new Rect(x + 150f, y, inner - 150f, qh + 6f), q.text, sQ, GameUI.Ink);
            y += Mathf.Max(qh, 120f) + 24f;

            // options
            for (int i = 0; i < q.options.Length; i++)
            {
                Rect o = new Rect(x, y, inner, optHeights[i]);
                bool wrong = wrongPicks.Contains(i);
                bool right = answered && i == q.correct;
                bool hover = !answered && !wrong && o.Contains(Event.current.mousePosition);
                Color bg = right ? Good : wrong ? new Color(0.97f, 0.88f, 0.86f) : hover ? GameUI.Accent : new Color(0.93f, 0.93f, 0.92f);
                GameUI.Box(o, bg);
                Color ink = right || hover ? Color.white : wrong ? new Color(0.62f, 0.40f, 0.38f) : GameUI.Ink;
                Rect num = new Rect(o.x + 14f, o.y + (o.height - 36f) * 0.5f, 36f, 36f);
                GameUI.Box(num, right || hover ? new Color(1f, 1f, 1f, 0.25f) : wrong ? new Color(0.9f, 0.7f, 0.68f) : Color.white);
                GameUI.Text(num, right ? "✓" : wrong ? "✗" : (i + 1).ToString(), sNum, ink);
                float th = sOpt.CalcHeight(new GUIContent(q.options[i]), inner - 90f);
                GameUI.Text(new Rect(o.x + 68f, o.y + (o.height - th) * 0.5f, inner - 90f, th + 4f), q.options[i], sOpt, ink);
                if (!answered && !wrong && GUI.Button(o, GUIContent.none, GUIStyle.none)) Pick(i);
                y += optHeights[i] + 10f;
            }

            // feedback
            if (fh > 0f)
            {
                y += 4f;
                Rect fr = new Rect(x, y, inner, fh);
                GameUI.Box(fr, feedbackGood ? new Color(0.88f, 0.96f, 0.90f) : new Color(1f, 0.95f, 0.88f));
                GameUI.Box(new Rect(fr.x, fr.y, 5f, fr.height), feedbackGood ? Good : GameUI.Accent);
                GameUI.Text(new Rect(fr.x + 22f, fr.y + 16f, inner - 40f, fh - 20f), feedback, sFeed, GameUI.Ink);
                y += fh + 14f;
            }

            // footer / next
            if (answered)
            {
                Rect nb = new Rect(r.xMax - 48f - 260f, r.yMax - 64f, 260f, 48f);
                if (GameUI.Button(nb, qi + 1 < qs.Count ? "Next question   (Enter)" : "Open the door   (Enter)", sFoot)) Next();
                GameUI.Text(new Rect(x, r.yMax - 52f, inner - 300f, 30f), "Right!", sHead, Good);
            }
            else
                GameUI.Text(new Rect(x, r.yMax - 52f, inner, 30f),
                            wrongPicks.Count > 0 ? "Pick another answer:  1 - 4 or click" : "Press 1 - 4 or click an answer", sFoot, GameUI.InkSoft);
        }

        void Styles()
        {
            if (sHead != null && sHead.font != null) return;
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
            sHead = Make(20, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sQ = Make(29, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sOpt = Make(24, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sFeed = Make(23, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sFoot = Make(21, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sNum = Make(22, FontStyle.Bold, TextAnchor.MiddleCenter, false);
        }
    }
}
