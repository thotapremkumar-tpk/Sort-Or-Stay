using UnityEngine;

namespace SortOrStay.BasicDemo
{
    /// IMGUI HUD for the basic demo: objective, counters, buttons, a number under
    /// each key, feedback messages and the "door unlocked" card.
    /// IMGUI keeps the demo free of fonts, canvases and TextMeshPro setup.
    public class DemoHUD : MonoBehaviour
    {
        public static DemoHUD Instance { get; private set; }
        public DemoBubblePuzzle puzzle;
        public Camera cam;

        const float VW = 1600f, VH = 900f;      // virtual canvas, scaled to the Game view
        float s = 1f, W = VW, H = VH;
        readonly Rect panelRect = new Rect(20f, 20f, 470f, 250f);
        Rect cardRect;

        string flashText;
        Color flashCol;
        float flashEnd;
        float sceneStart;

        GUIStyle sKicker, sTitle, sBody, sStat, sBtn, sKey, sFlash, sTip, sCardTitle, sCardBody;
        Texture2D tPanel, tDim, tKey, tKeySel, tKeyDone, tBlack;
        bool styled;

        static readonly string[] Intro =
        {
            "You wake up on the floor of a small, windowless room.",
            "The steel door is locked. Six numbered keys hang on the wall, in the wrong order.",
            "A terminal reads:  ORDER UNLOCKS.  Arrange the keys from smallest to largest.",
        };

        void Awake()
        {
            Instance = this;
            sceneStart = Time.time;
            if (cam == null) cam = Camera.main;
        }

        public void Flash(string msg) => Flash(msg, new Color(1f, 0.42f, 0.36f));

        public void Flash(string msg, Color col, float seconds = 2.4f)
        {
            flashText = msg;
            flashCol = col;
            flashEnd = Time.time + seconds;
        }

        bool CardVisible => puzzle != null && puzzle.Solved && Time.time > puzzle.SolvedAt + 1.4f;

        /// True when a screen point (Input System coordinates, origin bottom-left) is over the HUD.
        public bool PointerOverUI(Vector2 p)
        {
            var g = new Vector2(p.x / s, (Screen.height - p.y) / s);
            return panelRect.Contains(g) || (CardVisible && cardRect.Contains(g));
        }

        void OnGUI()
        {
            if (puzzle == null) return;
            Styles();
            s = Mathf.Min(Screen.width / VW, Screen.height / VH);
            W = Screen.width / s;
            H = Screen.height / s;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));

            DrawKeyNumbers();
            DrawPanel();
            DrawBottomLine();
            DrawFlash();
            if (CardVisible) DrawCard();
            DrawFade();
        }

        void DrawKeyNumbers()
        {
            if (cam == null) return;
            foreach (var k in puzzle.items)
            {
                if (k == null) continue;
                Vector3 sp = cam.WorldToScreenPoint(k.LabelAnchor);
                if (sp.z <= 0f) continue;
                float x = sp.x / s, y = (Screen.height - sp.y) / s;
                var r = new Rect(x - 26f, y - 22f, 52f, 44f);
                bool sel = puzzle.Selected == k;
                GUI.DrawTexture(r, puzzle.Solved ? tKeyDone : sel ? tKeySel : tKey);
                sKey.normal.textColor = (sel || puzzle.Solved) ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
                GUI.Label(r, k.value.ToString(), sKey);
            }
        }

        void DrawPanel()
        {
            GUI.DrawTexture(panelRect, tPanel);
            float x = panelRect.x + 18f, y = panelRect.y + 14f, w = panelRect.width - 36f;
            GUI.Label(new Rect(x, y, w, 20f), "FLOOR 1  ·  THE LOCKED ROOM  ·  BASIC DEMO", sKicker);
            GUI.Label(new Rect(x, y + 20f, w, 38f), "Bubble Sort", sTitle);
            GUI.Label(new Rect(x, y + 62f, w, 24f), "Arrange the keys from smallest to largest.", sBody);
            GUI.Label(new Rect(x, y + 86f, w, 24f), "You may only swap two neighbouring keys.", sBody);
            GUI.Label(new Rect(x, y + 122f, 210f, 28f), "Comparisons  " + puzzle.comparisons, sStat);
            GUI.Label(new Rect(x + 214f, y + 122f, 200f, 28f), "Swaps  " + puzzle.swaps, sStat);

            GUI.enabled = !puzzle.Solved;
            if (GUI.Button(new Rect(x, y + 168f, 200f, 42f), "Hint  (H)", sBtn)) puzzle.ShowHint();
            GUI.enabled = true;
            if (GUI.Button(new Rect(x + 214f, y + 168f, 200f, 42f), "Restart  (R)", sBtn)) puzzle.Setup();
        }

        void DrawBottomLine()
        {
            float t = Time.time - sceneStart;
            string line = null;
            if (t < 10.2f)
            {
                if (t > 0.6f) line = Intro[Mathf.Min(Intro.Length - 1, (int)((t - 0.6f) / 3.2f))];
            }
            else if (!puzzle.Solved)
            {
                line = puzzle.Selected != null
                    ? "Now click a key right next to it to swap them.     (Click it again to put it back.)"
                    : "Click a key, then click its neighbour to swap them.       H = hint      R = restart";
            }
            if (line == null) return;
            var r = new Rect(W * 0.5f - 540f, H - 92f, 1080f, 56f);
            GUI.DrawTexture(r, tDim);
            GUI.Label(r, line, sTip);
        }

        void DrawFlash()
        {
            if (string.IsNullOrEmpty(flashText) || Time.time > flashEnd) return;
            float a = Mathf.Clamp01((flashEnd - Time.time) / 0.4f);
            var r = new Rect(510f, 26f, Mathf.Max(400f, W - 530f), 60f);
            GUI.color = new Color(1f, 1f, 1f, a);
            GUI.DrawTexture(r, tDim);
            sFlash.normal.textColor = flashCol;
            GUI.Label(r, flashText, sFlash);
            GUI.color = Color.white;
        }

        void DrawCard()
        {
            cardRect = new Rect(W * 0.5f - 370f, H * 0.5f - 205f, 740f, 390f);
            GUI.DrawTexture(cardRect, tPanel);
            float x = cardRect.x + 32f, w = cardRect.width - 64f, y = cardRect.y + 26f;
            int min = puzzle.StartInversions;
            int secs = Mathf.RoundToInt(puzzle.SolvedAt - puzzle.StartedAt);
            string verdict = puzzle.swaps == min
                ? "You matched it exactly, just like bubble sort."
                : $"{puzzle.swaps - min} extra swaps. Swapping a pair that was already in order costs two: the mistake and the fix.";

            GUI.Label(new Rect(x, y, w, 46f), "DOOR UNLOCKED", sCardTitle);
            GUI.Label(new Rect(x, y + 58f, w, 60f),
                "You compared neighbours and pushed the biggest key to the end each pass.  That's bubble sort.", sCardBody);
            GUI.Label(new Rect(x, y + 134f, w, 150f),
                $"Your swaps  {puzzle.swaps}        Fewest possible  {min}   (one per out-of-order pair)\n" +
                $"Comparisons  {puzzle.comparisons}        Hints  {puzzle.hintsUsed}        Time  {secs} s\n\n" + verdict, sCardBody);
            if (GUI.Button(new Rect(x, cardRect.yMax - 72f, 240f, 46f), "Play again  (R)", sBtn)) puzzle.Setup();
        }

        void DrawFade()
        {
            float t = Time.time - sceneStart;
            if (t >= 2f) return;
            GUI.color = new Color(1f, 1f, 1f, 1f - t / 2f);
            GUI.DrawTexture(new Rect(0f, 0f, W, H), tBlack);
            GUI.color = Color.white;
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        static GUIStyle Make(int size, FontStyle fs, TextAnchor a, Color col, bool wrap = false)
        {
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap };
            st.normal.textColor = col;
            return st;
        }

        void Styles()
        {
            if (styled) return;
            styled = true;
            tPanel = Tex(new Color(0.06f, 0.07f, 0.10f, 0.9f));
            tDim = Tex(new Color(0f, 0f, 0f, 0.62f));
            tKey = Tex(new Color(0.08f, 0.09f, 0.12f, 0.88f));
            tKeySel = Tex(new Color(1f, 0.8f, 0.12f, 0.96f));
            tKeyDone = Tex(new Color(0.3f, 1f, 0.45f, 0.96f));
            tBlack = Tex(Color.black);

            sKicker = Make(13, FontStyle.Bold, TextAnchor.UpperLeft, new Color(1f, 0.78f, 0.3f));
            sTitle = Make(30, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            sBody = Make(17, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.88f, 0.9f, 0.95f), true);
            sStat = Make(19, FontStyle.Bold, TextAnchor.UpperLeft, Color.white);
            sBtn = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            sKey = Make(26, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white);
            sFlash = Make(21, FontStyle.Bold, TextAnchor.MiddleCenter, Color.white, true);
            sTip = Make(19, FontStyle.Normal, TextAnchor.MiddleCenter, Color.white, true);
            sCardTitle = Make(36, FontStyle.Bold, TextAnchor.UpperLeft, new Color(0.3f, 1f, 0.45f));
            sCardBody = Make(19, FontStyle.Normal, TextAnchor.UpperLeft, new Color(0.9f, 0.92f, 0.96f), true);
        }
    }
}
