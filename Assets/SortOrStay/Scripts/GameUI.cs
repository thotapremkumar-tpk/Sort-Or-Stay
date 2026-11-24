using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// The whole HUD: progress indicators, move counter, crosshair prompt, flash messages,
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

        /// A popup or another modal card (the room's questions, a lesson, the guide's menu) is on screen:
        /// the world ignores clicks and keys until it closes.
        public bool PopupOpen => popupShown || ModalOpen;
        /// The "why this works" debrief card itself.
        public bool DebriefOpen => popupShown;
        /// Set by the room questions, the guide's lesson and the guide's menu while they are open.
        public bool ModalOpen { get; set; }
        public bool EndCardOpen { get; private set; }

        [Header("Rooms (five-floor building)")]
        public int room;                      // 1-3, 0 = no rooms (standalone floor scene)
        public SortCase roomCase;
        bool popupShown;
        public int LastConsumeFrame { get; private set; } = -1;

        /// Esc can open the pause menu only during normal play.
        public bool PauseMenuEnabled => showHud && !EndCardOpen && !PopupOpen && fade < 0.5f;
        public bool Paused { get; private set; }
        public bool PauseOpen => Paused && PauseMenuEnabled;

        Rect statsRect, controlsRect;      // HUD areas (GUI space) a click should not go through

        // light theme
        public static readonly Color Accent = new Color(0.93f, 0.45f, 0.05f);
        public static readonly Color Card = new Color(1f, 1f, 1f, 0.86f);
        public static readonly Color CardSolid = new Color(0.985f, 0.98f, 0.97f, 0.97f);
        public static readonly Color Ink = new Color(0.11f, 0.12f, 0.14f);
        public static readonly Color InkSoft = new Color(0.35f, 0.37f, 0.40f);
        public static readonly Color Pill = new Color(0.07f, 0.08f, 0.10f, 0.78f);
        public static readonly Color Done = new Color(0.25f, 0.68f, 0.38f, 0.92f);

        float fade = 1f;
        string flashMsg; Color flashColor; float flashUntil;
        string subtitle; float subtitleUntil;
        string popupTitle, popupBody, popupFooter; float popupOpenedAt;
        string endTitle, endBody, endFooter;
        float controlsUntil;

        GUIStyle sStats, sPrompt, sFlash, sSub, sSmall, sPopTitle, sPopBody, sFooter, sEndTitle, sEndBody, sPauseButton, sChip;
        GUIStyle sControls;

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

        /// A subtitle line is on screen (bottom centre).
        public bool SubtitleShowing => !string.IsNullOrEmpty(subtitle) && Time.time < subtitleUntil;
        public void SetStats(string s) { stats = s; }
        public void ShowControls(float seconds) { controlsUntil = Time.time + seconds; }

        public string PopupTitle => popupTitle;
        public string PopupBody => popupBody;

        public void ShowPopup(string title, string body, string footer)
        {
            if (EndCardOpen) return;            // the floor is already over: the end card stays on top
            popupTitle = title; popupBody = body; popupFooter = footer;
            popupShown = true; popupOpenedAt = Time.unscaledTime;
        }

        /// Something on the HUD (or the guide) used this click or key: the world ignores it this frame.
        public void ConsumeClick() { LastConsumeFrame = Time.frameCount; }

        public void ClosePopup()
        {
            if (!popupShown) return;
            popupShown = false;
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
            return statsRect.Contains(g) || controlsRect.Contains(g);
        }

        public void ShowEndCard(string title, string body, string footer)
        {
            endTitle = title; endBody = body; endFooter = footer;
            EndCardOpen = true; popupShown = false; ModalOpen = false;
        }

        // ------------------------------------------------------------ input

        void Update()
        {
            if (!popupShown) return;
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
                // Floor and room progress stay in the HUD. Instructions are on the room terminal.
                float cx0 = 28f, cy0 = 26f;
                float progressRight = cx0 + 4f * 46f + 38f;
                for (int i = 1; i <= 5; i++)
                {
                    Rect chip = new Rect(cx0 + (i - 1) * 46f, cy0, 38f, 26f);
                    bool cur = i == floor, done = i < floor;
                    DrawProgressChip(chip, i.ToString(), cur, done);
                }

                // the floor's three rooms: best, average, worst case
                if (room > 0)
                {
                    string[] names = { "BEST", "AVERAGE", "WORST" };
                    float[] widths = { 72f, 112f, 88f };
                    float rx = cx0 + 5 * 46f + 16f;
                    if (rx + 288f > W - 28f) { rx = cx0; cy0 += 34f; }
                    for (int k = 1; k <= 3; k++)
                    {
                        float w = widths[k - 1];
                        Rect chip = new Rect(rx, cy0, w, 26f);
                        bool cur = k == room, done = k < room;
                        DrawProgressChip(chip, names[k - 1], cur, done);
                        progressRight = Mathf.Max(progressRight, chip.xMax);
                        rx += w + 8f;
                    }
                }

                // move counter, top right
                statsRect = Rect.zero;
                if (!string.IsNullOrEmpty(stats))
                {
                    Vector2 sz = sStats.CalcSize(new GUIContent(stats));
                    float width = Mathf.Min(sz.x + 54f, W - 56f);
                    float x = W - 28f - width;
                    float y = x >= progressRight + 16f ? 26f : cy0 + 38f;
                    float height = sStats.CalcHeight(new GUIContent(stats), width - 44f);
                    Rect r = new Rect(x, y, width, height + 26f);
                    statsRect = r;
                    GlassPanel(r, 12f);
                    Text(new Rect(r.x + 22f, r.y + 13f, width - 44f, height), stats, sStats, GlassText);
                }

                // no text label at the pointer (it covered the view while looking around); the bouncing marker
                // above the hovered thing shows what a click will use
                bool hot = !string.IsNullOrEmpty(prompt) && !PopupOpen && !PauseOpen;
                if (!promptAtCursor)
                {
                    // crosshair (dark dot in a white ring: readable on bright walls)
                    float cx = W * 0.5f, cy = H * 0.5f;
                    Box(new Rect(cx - 5f, cy - 5f, 10f, 10f), new Color(1f, 1f, 1f, 0.85f));
                    Box(new Rect(cx - 3f, cy - 3f, 6f, 6f), hot ? Accent : new Color(0.1f, 0.1f, 0.12f, 0.9f));
                    if (hot)
                    {
                        Box(new Rect(cx - 17f, cy - 1.5f, 8f, 3f), Accent);
                        Box(new Rect(cx + 9f, cy - 1.5f, 8f, 3f), Accent);
                        Box(new Rect(cx - 1.5f, cy - 17f, 3f, 8f), Accent);
                        Box(new Rect(cx - 1.5f, cy + 9f, 3f, 8f), Accent);
                    }
                }

                // controls strip, bottom left
                controlsRect = Rect.zero;
                if (Time.time < controlsUntil && !(GuideNpc.Instance != null && GuideNpc.Instance.DialogOpen))
                {
                    string c = promptAtCursor
                        ? "Click a thing to use it  ·  Right-drag / arrows turn  ·  Scroll zoom  ·  WASD walk  ·  Space jump  ·  H hint (Ada comes)  ·  T lesson  ·  Tab mouse-look  ·  Esc menu"
                        : "WASD walk  ·  Shift run  ·  Space jump  ·  Mouse / arrows look  ·  Click / E use  ·  H hint  ·  T lesson  ·  V camera  ·  C character  ·  Tab point-and-click  ·  Esc menu";
                    float width = Mathf.Min(sControls.CalcSize(new GUIContent(c)).x + 36f, W - 56f);
                    float height = sControls.CalcHeight(new GUIContent(c), width - 36f);
                    Rect r = new Rect(28f, H - 52f - height, width, height + 24f);
                    controlsRect = r;
                    GlassPanel(r, 12f);
                    Text(new Rect(r.x + 18f, r.y + 12f, width - 36f, height), c, sControls, GlassText);
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
            if (popupShown)
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

        internal static readonly Color GlassText = new Color(0.94f, 0.98f, 1f);
        internal static readonly Color GlassAccent = new Color(0.49f, 0.95f, 0.87f);
        static readonly Color GlassFill = new Color(0.06f, 0.12f, 0.19f, 0.86f);

        void DrawProgressChip(Rect r, string label, bool current, bool complete)
        {
            RoundedBox(r, current ? GlassAccent : complete ? new Color(0.13f, 0.36f, 0.30f, 0.92f) : GlassFill, 7f);
            Text(r, label, sChip, current ? Ink : GlassText);
        }

        internal static void GlassPanel(Rect r, float radius)
        {
            RoundedBox(new Rect(r.x, r.y + 5f, r.width, r.height), new Color(0f, 0f, 0f, 0.24f), radius);
            RoundedBox(r, GlassFill, radius);
            RoundedBox(r, new Color(0.66f, 0.85f, 0.94f, 0.42f), radius, 1f);
        }

        void DrawPause(float W, float H)
        {
            var pc = PlayerController.Instance;
            const float bh = 52f, gap = 10f, inset = 32f;
            int floorCount = GameState.FloorScenes.Length;
            float pw = Mathf.Min(520f, W - 32f), bw = pw - 2f * inset;
            float ph = 134f + 8f * bh + 7f * gap + inset;
            Rect r = new Rect((W - pw) * 0.5f, (H - ph) * 0.5f, pw, ph);

            Box(new Rect(0f, 0f, W, H), new Color(0.02f, 0.05f, 0.09f, 0.27f));
            GlassPanel(r, 24f);

            float x = r.x + inset;
            Text(new Rect(x, r.y + 28f, bw, 52f), "Paused", sPopTitle, GlassText);
            string location = room > 0 ? $"Floor {floor}  /  Room {room}" : $"Floor {floor}";
            Text(new Rect(x, r.y + 83f, bw, 28f), location, sSmall, GlassAccent);

            float y = r.y + 134f;
            if (Button(new Rect(x, y, bw, bh), "Resume", primary: true)) Resume();
            y += bh + gap;
            if (Button(new Rect(x, y, bw, bh), BuildingManager.Instance != null && room > 0 ? "Restart this room" : "Restart this floor")) LevelManager.Reload();
            y += bh + gap;
            // one button per floor; floors whose scene is not built yet are greyed out
            float fw = (bw - (floorCount - 1) * gap) / floorCount;
            bool wasEnabled = GUI.enabled;
            for (int i = 0; i < floorCount; i++)
            {
                GUI.enabled = wasEnabled && LevelManager.FloorExists(i + 1);
                if (Button(new Rect(x + i * (fw + gap), y, fw, bh), "F" + (i + 1), centered: true)) LevelManager.LoadFloor(i + 1);
            }
            GUI.enabled = wasEnabled;
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
            y += bh + gap;
            // Professor Ada's dialogs (hints, lessons, the welcome on each floor) can be switched off
            if (Button(new Rect(x, y, bw, bh), "Guide dialogs:  " + (GuideNpc.DialogsOn ? "on" : "off")))
            {
                GuideNpc.DialogsOn = !GuideNpc.DialogsOn;
                Info(GuideNpc.DialogsOn ? "Guide dialogs on:  Professor Ada comes when you press H" : "Guide dialogs off:  hints appear as text");
            }
            y += bh + gap;
            if (Button(new Rect(x, y, bw, bh), "Ada's voice:  " + (GuideVoice.On ? "on" : "off")))
            {
                GuideVoice.On = !GuideVoice.On;
                Info(GuideVoice.On ? "Professor Ada's voice on" : "Professor Ada's voice off  (her lines stay on screen)");
            }

            // a click outside the card resumes play
            var e = Event.current;
            if (e.type == EventType.MouseDown && !r.Contains(e.mousePosition)) { Resume(); e.Use(); }
        }

        void Resume() { SetPaused(false); }

        bool Button(Rect r, string label, bool primary = false, bool centered = false)
            => GlassButton(r, label, centered ? sChip : sPauseButton, primary);

        internal static bool GlassButton(Rect r, string label, GUIStyle style, bool primary = false)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Color fill = primary
                ? (hover ? new Color(0.72f, 1f, 0.94f) : GlassAccent)
                : new Color(0.67f, 0.85f, 0.94f, hover ? 0.23f : 0.10f);
            RoundedBox(r, fill, 10f);
            if (!primary)
                RoundedBox(r, new Color(0.67f, 0.85f, 0.94f, hover ? 0.62f : 0.22f), 10f, 1f);

            Rect labelRect = new Rect(r.x + 8f, r.y, r.width - 16f, r.height);
            Color ink = !GUI.enabled ? new Color(0.61f, 0.68f, 0.74f) : primary ? Ink : GlassText;
            Text(labelRect, label, style, ink);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        internal static void RoundedBox(Rect r, Color color, float radius, float border = 0f)
        {
            GUI.DrawTexture(r, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Display(color), border, radius);
        }

        /// A flat HUD button (light grey, orange when hovered).
        public static bool Button(Rect r, string label, GUIStyle style)
        {
            bool hover = GUI.enabled && r.Contains(Event.current.mousePosition);
            Box(r, !GUI.enabled ? new Color(0.9f, 0.9f, 0.9f, 1f) : hover ? Accent : new Color(0.93f, 0.93f, 0.92f, 1f));
            float th = style.CalcHeight(new GUIContent(label), r.width - 16f);
            Text(new Rect(r.x + 8f, r.y + (r.height - th) * 0.5f, r.width - 16f, th + 2f), label, style, !GUI.enabled ? new Color(0.6f, 0.6f, 0.6f) : hover ? Color.white : Ink);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        // ------------------------------------------------------------ helpers

        // Colour comes from GUI.color, never from mutating a style: copied GUIStyles can share their
        // state objects, and recolouring a copy every frame turned the whole HUD one colour.
        public static void Text(Rect r, string text, GUIStyle st, Color c)
        {
            Color old = GUI.color;
            GUI.color = Display(c);
            GUI.Label(r, text, st);
            GUI.color = old;
        }

        public static void Box(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = Display(c);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        /// The project renders in linear colour space, where IMGUI treats GUI.color as linear and
        /// everything comes out washed out. The HUD colours are written as normal (sRGB) values,
        /// so convert them here.
        public static Color Display(Color c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return c;
            return new Color(Mathf.GammaToLinearSpace(c.r), Mathf.GammaToLinearSpace(c.g), Mathf.GammaToLinearSpace(c.b), c.a);
        }

        public static void Shadow(Rect r)
        {
            Box(new Rect(r.x + 3f, r.y + 5f, r.width, r.height), new Color(0f, 0f, 0f, 0.12f));
        }

        static Font hudFont;

        /// Use Unity's bundled font so the HUD renders consistently across operating systems.
        public static Font HudFont()
        {
            if (hudFont != null) return hudFont;
            hudFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (hudFont == null) hudFont = GUI.skin.font;
            return hudFont;
        }

        void EnsureStyles()
        {
            if (sStats != null && sStats.font != null) return;
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
            sStats = Make(22, FontStyle.Bold, TextAnchor.UpperLeft, true);
            sPrompt = Make(22, FontStyle.Bold, TextAnchor.UpperCenter, false);
            sFlash = Make(30, FontStyle.Bold, TextAnchor.UpperCenter, true);
            sSub = Make(28, FontStyle.Italic, TextAnchor.MiddleCenter, false);
            sSmall = Make(19, FontStyle.Normal, TextAnchor.UpperLeft, false);
            sControls = Make(19, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sPopTitle = Make(40, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sPopBody = Make(25, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sFooter = Make(22, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sEndTitle = Make(64, FontStyle.Bold, TextAnchor.UpperLeft, false);
            sEndBody = Make(27, FontStyle.Normal, TextAnchor.UpperLeft, true);
            sPauseButton = Make(23, FontStyle.Bold, TextAnchor.MiddleLeft, false);
            sChip = Make(18, FontStyle.Bold, TextAnchor.MiddleCenter, false);
        }
    }
}
