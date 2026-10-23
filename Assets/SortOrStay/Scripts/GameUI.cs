using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// The whole HUD in a light theme: objective card, move counter, crosshair prompt, flash messages,
    /// subtitles, the "why this works" popup, the pause menu, the fade and the end card.
    /// Drawn with IMGUI so it needs no prefabs.
    public class GameUI : MonoBehaviour
    {
        public static GameUI Instance { get; private set; }

        [Header("Text")]
        public string levelTitle = "FLOOR 1  ·  THE LOCKED ROOM";
        public int floor = 1;
        [TextArea] public string objective = "";
        public string stats = "";
        [HideInInspector] public string prompt = "";
        [HideInInspector] public bool promptAtCursor;      // point-and-click: label follows the mouse
        public bool showHud = true;
        public Color fadeColor = new Color(0.02f, 0.02f, 0.03f);

        public bool PopupOpen { get; private set; }
        public bool EndCardOpen { get; private set; }
        public int LastConsumeFrame { get; private set; } = -1;

        /// Esc can open the pause menu only during normal play.
        public bool PauseMenuEnabled => showHud && !EndCardOpen && !PopupOpen && fade < 0.5f;
        public bool Paused { get; private set; }
        public bool PauseOpen => Paused && PauseMenuEnabled;

        Rect cardRect, statsRect, controlsRect;      // HUD areas (GUI space) a click should not go through

        // light theme
        static readonly Color Accent = new Color(0.93f, 0.45f, 0.05f);
        static readonly Color Card = new Color(1f, 1f, 1f, 0.86f);
        static readonly Color CardSolid = new Color(0.985f, 0.98f, 0.97f, 0.97f);
        static readonly Color Ink = new Color(0.11f, 0.12f, 0.14f);
        static readonly Color InkSoft = new Color(0.35f, 0.37f, 0.40f);
        static readonly Color Pill = new Color(0.07f, 0.08f, 0.10f, 0.78f);

        float fade = 1f;
        string flashMsg; Color flashColor; float flashUntil;
        string subtitle; float subtitleUntil;
        string popupTitle, popupBody, popupFooter; float popupOpenedAt;
        string endTitle, endBody, endFooter;
        float controlsUntil;

        GUIStyle sTitle, sBody, sStats, sPrompt, sFlash, sSub, sSmall, sPopTitle, sPopBody, sFooter, sEndTitle, sEndBody, sButton, sChip;

        void Awake() { Instance = this; }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ------------------------------------------------------------ API

        public void SetFade(float a) { fade = Mathf.Clamp01(a); }
        public float Fade => fade;

        public IEnumerator FadeTo(float target, float seconds)
        {
            float start = fade, e = 0f;
            while (e < seconds)
            {
                e += Time.deltaTime;
                fade = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, e / seconds));
                yield return null;
            }
            fade = target;
        }

        public void Flash(string msg) { Flash(msg, new Color(1f, 0.45f, 0.38f)); }
        public void Info(string msg) { Flash(msg, new Color(0.96f, 0.96f, 0.94f)); }

        public void Flash(string msg, Color c, float seconds = 2.6f)
        {
            flashMsg = msg; flashColor = c; flashUntil = Time.time + seconds;
        }

        public void Subtitle(string line, float seconds)
        {
            subtitle = line; subtitleUntil = Time.time + seconds;
        }

        public void SetObjective(string s) { objective = s; }
        public void SetStats(string s) { stats = s; }
        public void ShowControls(float seconds) { controlsUntil = Time.time + seconds; }

        public void ShowPopup(string title, string body, string footer)
        {
            popupTitle = title; popupBody = body; popupFooter = footer;
            PopupOpen = true; popupOpenedAt = Time.unscaledTime;
        }

        public void ClosePopup()
        {
            if (!PopupOpen) return;
            PopupOpen = false;
            LastConsumeFrame = Time.frameCount;
        }

        public void SetPaused(bool p)
        {
            Paused = p;
            if (p) PlayerController.FreeCursor();
            else
            {
                PlayerController.ApplyCursorForMode();
                PlayerController.LockedThisFrame = Time.frameCount;
                LastConsumeFrame = Time.frameCount;
            }
        }

        /// True if a screen point (pixels, origin bottom-left) is over a HUD card.
        public bool IsOverHud(Vector2 screen)
        {
            if (Screen.height <= 0) return false;
            float s = Screen.height / 1080f;
            Vector2 g = new Vector2(screen.x / s, (Screen.height - screen.y) / s);
            return cardRect.Contains(g) || statsRect.Contains(g) || controlsRect.Contains(g);
        }

        public void ShowEndCard(string title, string body, string footer)
        {
            endTitle = title; endBody = body; endFooter = footer;
            EndCardOpen = true; PopupOpen = false;
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            if (!PopupOpen) return;
            var kb = Keyboard.current; var mouse = Mouse.current;
            bool close = (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
                      || (mouse != null && mouse.leftButton.wasPressedThisFrame);
            if (close && Time.unscaledTime > popupOpenedAt + 0.6f) ClosePopup();
        }

        // ------------------------------------------------------------ drawing

        void OnGUI()
        {
            if (Screen.height <= 0) return;
            float s = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
            float W = Screen.width / s, H = 1080f;
            EnsureStyles();

            if (showHud && !EndCardOpen)
            {
                // level card, top left
                bool hasObj = !string.IsNullOrEmpty(objective);
                float cardH = hasObj ? 26f + 34f + sBody.CalcHeight(new GUIContent(objective), 584f) + 18f : 60f;
                Rect card = new Rect(28f, 26f, 640f, cardH);
                cardRect = card;
                Shadow(card);
                Box(card, Card);
                Box(new Rect(card.x, card.y, 5f, card.height), Accent);
                Text(new Rect(card.x + 22f, card.y + 14f, 600f, 30f), levelTitle, sTitle, Accent);
                if (hasObj) Text(new Rect(card.x + 22f, card.y + 50f, 600f, cardH - 56f), objective, sBody, Ink);

                // floor progress chips under the card
                float cx0 = 28f, cy0 = card.yMax + 10f;
                for (int i = 1; i <= 5; i++)
                {
                    Rect chip = new Rect(cx0 + (i - 1) * 46f, cy0, 38f, 26f);
                    bool cur = i == floor, done = i < floor;
                    Box(chip, cur ? Accent : done ? new Color(0.25f, 0.68f, 0.38f, 0.92f) : new Color(1f, 1f, 1f, 0.7f));
                    Text(chip, i.ToString(), sChip, cur || done ? Color.white : InkSoft);
                }

                // move counter, top right
                if (!string.IsNullOrEmpty(stats))
                {
                    Vector2 sz = sStats.CalcSize(new GUIContent(stats));
                    Rect r = new Rect(W - 28f - sz.x - 44f, 26f, sz.x + 44f, 54f);
                    statsRect = r;
                    Shadow(r);
                    Box(r, Card);
                    Text(new Rect(r.x + 22f, r.y + 13f, sz.x + 4f, 30f), stats, sStats, Ink);
                }

                bool hot = !string.IsNullOrEmpty(prompt) && !PopupOpen && !PauseOpen;
                if (promptAtCursor)
                {
                    // point and click: the label sits next to the mouse
                    if (hot)
                    {
                        Vector2 m = Event.current.mousePosition;
                        string p = "Click   ·   " + prompt;
                        Vector2 psz = sPrompt.CalcSize(new GUIContent(p));
                        float px = Mathf.Min(m.x + 26f, W - psz.x - 50f);
                        float py = Mathf.Min(m.y + 22f, H - 60f);
                        Rect pr = new Rect(px, py, psz.x + 36f, 40f);
                        Box(pr, Pill);
                        Text(new Rect(pr.x, pr.y + 8f, pr.width, 30f), p, sPrompt, new Color(1f, 0.82f, 0.45f));
                    }
                }
                else
                {
                    // crosshair (dark dot in a white ring: readable on bright walls) + prompt
                    float cx = W * 0.5f, cy = H * 0.5f;
                    Box(new Rect(cx - 5f, cy - 5f, 10f, 10f), new Color(1f, 1f, 1f, 0.85f));
                    Box(new Rect(cx - 3f, cy - 3f, 6f, 6f), hot ? Accent : new Color(0.1f, 0.1f, 0.12f, 0.9f));
                    if (hot)
                    {
                        Box(new Rect(cx - 17f, cy - 1.5f, 8f, 3f), Accent);
                        Box(new Rect(cx + 9f, cy - 1.5f, 8f, 3f), Accent);
                        Box(new Rect(cx - 1.5f, cy - 17f, 3f, 8f), Accent);
                        Box(new Rect(cx - 1.5f, cy + 9f, 3f, 8f), Accent);
                        string p = "Click / E   ·   " + prompt;
                        Vector2 psz = sPrompt.CalcSize(new GUIContent(p));
                        Rect pr = new Rect(cx - psz.x * 0.5f - 18f, cy + 28f, psz.x + 36f, 40f);
                        Box(pr, Pill);
                        Text(new Rect(pr.x, pr.y + 8f, pr.width, 30f), p, sPrompt, new Color(1f, 0.82f, 0.45f));
                    }
                }

                // controls strip, bottom left
                controlsRect = Rect.zero;
                if (Time.time < controlsUntil)
                {
                    string c = promptAtCursor
                        ? "Click a thing to walk over and use it  ·  Click the floor to walk  ·  Right-drag / arrows turn  ·  Scroll zoom  ·  WASD walk  ·  H hint  ·  Tab mouse-look  ·  Esc menu"
                        : "WASD walk  ·  Shift run  ·  Mouse / arrows look  ·  Click / E use  ·  H hint  ·  V camera  ·  C character  ·  Tab point-and-click  ·  Esc menu";
                    Vector2 sz = sSmall.CalcSize(new GUIContent(c));
                    Rect r = new Rect(28f, H - 28f - 46f, Mathf.Min(sz.x + 36f, W - 56f), 46f);
                    controlsRect = r;
                    Shadow(r);
                    Box(r, Card);
                    Text(new Rect(r.x + 18f, r.y + 12f, sz.x + 4f, 26f), c, sSmall, Ink);
                }
            }

            // flash message, in a dark pill so it reads on any background
            if (!string.IsNullOrEmpty(flashMsg) && Time.time < flashUntil && !EndCardOpen)
            {
                float a = Mathf.Clamp01((flashUntil - Time.time) / 0.4f);
                float fw = Mathf.Min(W - 160f, Mathf.Max(420f, sFlash.CalcSize(new GUIContent(flashMsg)).x + 70f));
                float fh = sFlash.CalcHeight(new GUIContent(flashMsg), fw - 60f) + 30f;
                Rect r = new Rect((W - fw) * 0.5f, H * 0.27f, fw, fh);
                Box(r, new Color(Pill.r, Pill.g, Pill.b, Pill.a * a));
                Color fc = flashColor; fc.a *= a;
                Text(new Rect(r.x + 30f, r.y + 15f, fw - 60f, fh - 20f), flashMsg, sFlash, fc);
            }

            // subtitle
            if (!string.IsNullOrEmpty(subtitle) && Time.time < subtitleUntil && !EndCardOpen)
            {
                float a = Mathf.Clamp01((subtitleUntil - Time.time) / 0.5f);
                Vector2 sz = sSub.CalcSize(new GUIContent(subtitle));
                float w = Mathf.Min(sz.x + 70f, W - 80f);
                Rect r = new Rect((W - w) * 0.5f, H - 168f, w, 60f);
                Box(r, new Color(0f, 0f, 0f, 0.6f * a));
                Text(r, subtitle, sSub, new Color(1f, 0.97f, 0.9f, a));
            }

            // popup ("why this works")
            if (PopupOpen)
            {
                float pw = Mathf.Min(1060f, W - 80f);
                float bodyH = sPopBody.CalcHeight(new GUIContent(popupBody), pw - 96f);
                float ph = 40f + 56f + 18f + bodyH + 30f + 40f + 30f;
                Rect r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);
                Box(new Rect(0f, 0f, W, H), new Color(0.95f, 0.95f, 0.95f, 0.25f));
                Shadow(r);
                Box(r, CardSolid);
                Box(new Rect(r.x, r.y, pw, 7f), Accent);
                Text(new Rect(r.x + 48f, r.y + 36f, pw - 96f, 56f), popupTitle, sPopTitle, Accent);
                Text(new Rect(r.x + 48f, r.y + 110f, pw - 96f, bodyH + 8f), popupBody, sPopBody, Ink);
                Text(new Rect(r.x + 48f, r.y + ph - 62f, pw - 96f, 34f), popupFooter, sFooter, InkSoft);
            }

            if (PauseOpen) DrawPause(W, H);

            // fade
            if (fade > 0.001f) Box(new Rect(0f, 0f, W, H), new Color(fadeColor.r, fadeColor.g, fadeColor.b, fade));

            // end card, drawn over the fade
            if (EndCardOpen)
            {
                Box(new Rect(0f, 0f, W, H), new Color(0.97f, 0.96f, 0.94f, 1f));
                float cw = Mathf.Min(1180f, W - 80f);
                float x = (W - cw) * 0.5f;
                float bodyH = sEndBody.CalcHeight(new GUIContent(endBody), cw);
                float top = Mathf.Max(80f, (H - (120f + bodyH + 80f)) * 0.5f);
                Box(new Rect(x, top, 120f, 7f), Accent);
                Text(new Rect(x, top + 24f, cw, 80f), endTitle, sEndTitle, Accent);
                Text(new Rect(x, top + 120f, cw, bodyH + 10f), endBody, sEndBody, Ink);
                Text(new Rect(x, top + 140f + bodyH, cw, 40f), endFooter, sFooter, InkSoft);
            }
        }

        // ------------------------------------------------------------ pause menu

        void DrawPause(float W, float H)
        {
            var pc = PlayerController.Instance;
            const float bw = 420f, bh = 54f, gap = 12f;
            int rows = 6;
            bool[] floors = { LevelManager.FloorExists(1), LevelManager.FloorExists(2), LevelManager.FloorExists(3) };
            float ph = 120f + rows * (bh + gap) + 40f;
            Rect r = new Rect((W - 520f) * 0.5f, (H - ph) * 0.5f, 520f, ph);

            Box(new Rect(0f, 0f, W, H), new Color(0.96f, 0.96f, 0.96f, 0.35f));
            Shadow(r);
            Box(r, CardSolid);
            Box(new Rect(r.x, r.y, r.width, 7f), Accent);
            Text(new Rect(r.x + 50f, r.y + 32f, 420f, 50f), "PAUSED", sPopTitle, Accent);

            float y = r.y + 112f;
            float x = r.x + 50f;
            if (Button(new Rect(x, y, bw, bh), "Resume")) Resume();
            y += bh + gap;
            if (Button(new Rect(x, y, bw, bh), "Restart this floor")) LevelManager.Reload();
            y += bh + gap;
            float fw = (bw - 2f * gap) / 3f;
            for (int i = 0; i < 3; i++)
            {
                GUI.enabled = floors[i];
                if (Button(new Rect(x + i * (fw + gap), y, fw, bh), "Floor " + (i + 1))) LevelManager.LoadFloor(i + 1);
            }
            GUI.enabled = true;
            y += bh + gap;
            if (pc != null && Button(new Rect(x, y, bw, bh), "Character:  " + pc.AvatarName + "   (C)")) pc.CycleAvatar();
            y += bh + gap;
            if (pc != null && Button(new Rect(x, y, bw, bh), "Camera:  " + pc.ViewName.ToLowerInvariant() + "   (V)")) pc.ToggleView();
            y += bh + gap;
            if (pc != null && Button(new Rect(x, y, bw, bh), "Controls:  " + (PlayerController.PointerMode ? "point and click" : "mouse look") + "   (Tab)"))
            {
                PlayerController.PointerMode = !PlayerController.PointerMode;
                Info(PlayerController.PointerMode ? "Point and click" : "Mouse look");
            }

            // a click outside the card resumes play
            var e = Event.current;
            if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { Resume(); e.Use(); }
        }

        void Resume() { SetPaused(false); }

        bool Button(Rect r, string label)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Box(r, !GUI.enabled ? new Color(0.9f, 0.9f, 0.9f, 1f) : hover ? Accent : new Color(0.93f, 0.93f, 0.92f, 1f));
            Text(new Rect(r.x, r.y + 14f, r.width, 30f), label, sButton, !GUI.enabled ? new Color(0.6f, 0.6f, 0.6f) : hover ? Color.white : Ink);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ------------------------------------------------------------ helpers

        // Colour comes from GUI.color, never from mutating a style: copied GUIStyles can share their
        // state objects, and recolouring a copy every frame turned the whole HUD one colour.
        static void Text(Rect r, string text, GUIStyle st, Color c)
        {
            Color old = GUI.color;
            GUI.color = Display(c);
            GUI.Label(r, text, st);
            GUI.color = old;
        }

        static void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = Display(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// The project renders in linear colour space, where IMGUI treats GUI.color as linear and
        /// everything comes out washed out. The HUD colours are written as normal (sRGB) values,
        /// so convert them here.
        static Color Display(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            return new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
        }

        static void Shadow(Rect r)
        {
            Box(new Rect(r.x + 3f, r.y + 5f, r.width, r.height), new Color(0f, 0f, 0f, 0.12f));
        }

        static Font hudFont;

        /// A dynamic OS font, so every style's size and weight are honoured on any machine.
        static Font HudFont()
        {
            if (hudFont != null) return hudFont;
            try
            {
                hudFont = Font.CreateDynamicFontFromOSFont(new[] { "Helvetica Neue", "Helvetica", "Arial", "Segoe UI", "Liberation Sans", "DejaVu Sans" }, 24);
            }
            catch (System.Exception) { hudFont = null; }
            if (hudFont == null) hudFont = GUI.skin.font;
            return hudFont;
        }

        void EnsureStyles()
        {
            if (sTitle != null && sTitle.font != null) return;
            Font f = HudFont();
            GUIStyle Make(int size, FontStyle fs, TextAnchor a, bool wrap)
            {
                var st = new GUIStyle
                {
                    font = f, fontSize = size, fontStyle = fs, alignment = a, wordWrap = wrap, richText = true,
                    clipping = TextClipping.Overflow,
                    padding = new RectOffset(0, 0, 0, 0), margin = new RectOffset(0, 0, 0, 0)
                };
                st.normal.textColor = Color.white;     // tinted per draw by GUI.color
                return st;
            }
            sTitle = Make(20, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sBody = Make(24, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sStats = Make(22, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sPrompt = Make(22, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sFlash = Make(30, FontStyle.Bold, TextAnchor.UpperCenter, true);
            sSub = Make(28, FontStyle.Italic, TextAnchor.MiddleCenter, false);
            sSmall = Make(19, FontStyle.Normal, TextAnchor.UpperLeft, false);
            sPopTitle = Make(40, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sPopBody = Make(25, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sFooter = Make(22, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sEndTitle = Make(64, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sEndBody = Make(27, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sButton = Make(23, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sChip = Make(18, FontStyle.Bold, TextAnchor.MiddleCenter, false);
        }
    }
}
