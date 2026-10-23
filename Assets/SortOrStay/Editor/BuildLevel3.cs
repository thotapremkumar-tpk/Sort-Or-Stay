using System.Collections.Generic;
using SortOrStay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static SortOrStayEditor.Kit;

namespace SortOrStayEditor
{
    /// Builds Floor 3, "The Library" (insertion sort): a sunlit reading room with wall bookcases,
    /// a reading table holding eight numbered books, the return shelf, a catalogue kiosk and glass doors.
    /// Menu: Sort or Stay > Build Level 3 (Library). Re-running it rebuilds and overwrites the scene.
    public static class BuildLevel3
    {
        const string SCENE = ROOT + "Scenes/Level3_Insertion.unity";

        const float W = 10f, D = 9f, H = 3.8f, T = 0.2f;
        const float HW = W * 0.5f, HD = D * 0.5f;
        const float DOOR_W = 1.8f, DOOR_H = 2.5f;
        const int BOOKS = 8;
        const float TABLE_Z = -1.25f, TABLE_TOP = 0.787f, TABLE_BOOK_Z = -0.95f, TABLE_PITCH = 0.3f;
        const float SHELF_Z = 1.25f, SHELF_FRONT = 1.05f, SHELF_Y = 1.035f, SHELF_PITCH = 0.14f;
        static readonly Vector3 SPAWN = new Vector3(-3.5f, 0f, 0.1f);
        static readonly int[] PREVIEW = { 512, 133, 709, 4, 398, 641, 220, 909 };

        static Material mFloor, mWall, mWainscot, mCeil, mBeam, mWood, mWoodDark, mPaper, mGold, mBrass, mGlass, mGap, mScreen, mDark,
                        mRug, mFabric, mShadeGreen, mBulb, mExit, mLamp, mFrame, mWhite, mGlobe, mSticker;
        static Material[] mCovers;

        static float ShelfX(int j) => -SHELF_PITCH * (BOOKS - 1) * 0.5f + SHELF_PITCH * j;
        static float TableX(int i) => TABLE_PITCH * (BOOKS - 1) * 0.5f - TABLE_PITCH * i;    // left to right as seen from the aisle

        [MenuItem("Sort or Stay/Build Level 3 (Library)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildNow();
        }

        public static void BuildNow()
        {
            Begin();
            EnsureCharacters();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MakeMaterials();

            var root = new GameObject("Level3_Library").transform;
            BuildRoom(root);
            var door = DoubleDoor(root, HD, T, DOOR_W, DOOR_H, mBrass, mWoodDark, mLamp, mGlass, true);
            door.lockedMessage = "Locked.  A brass plate reads:  EVERY BOOK IN ITS PLACE.";

            BuildTable(root, out Transform[] tableSlots);
            BuildReturnShelf(root, out Transform[] shelfSlots, out TMP_Text board);
            var books = BuildBooks(root, tableSlots);
            var gaps = BuildGaps(root, shelfSlots);
            var terminal = BuildKiosk(root);
            BuildWallBookcases(root);
            BuildProps(root);
            BuildLights(root);

            var player = BuildPlayer(SPAWN, 90f);

            var sys = new GameObject("Systems").transform;
            BuildUI(sys, "FLOOR 3  ·  THE LIBRARY", 3);

            var puzzle = sys.gameObject.AddComponent<InsertionPuzzle>();
            puzzle.items = books;
            puzzle.slots = shelfSlots;
            puzzle.tableSlots = tableSlots;
            puzzle.shelfSlots = shelfSlots;
            puzzle.gaps = gaps;
            puzzle.board = board;
            puzzle.terminal = terminal;
            puzzle.door = door;
            terminal.puzzle = puzzle;
            foreach (var g in gaps) g.puzzle = puzzle;

            var lm = sys.gameObject.AddComponent<LevelManager>();
            lm.levelNumber = 3;
            lm.nextScene = "Level4_Merge";
            lm.nextFloorLabel = "Floor 4  ·  the Control Room  (Merge Sort)";
            lm.door = door;
            lm.player = player;

            var overview = new GameObject("IntroCamera").transform;
            overview.SetParent(sys, false);
            overview.position = new Vector3(3.9f, 2.85f, -3.7f);
            overview.rotation = Quaternion.LookRotation(new Vector3(-0.4f, 0.9f, 0.5f) - overview.position);
            var intro = sys.gameObject.AddComponent<LevelIntro>();
            intro.player = player;
            intro.puzzle = puzzle;
            intro.overview = overview;
            intro.objective = "Return the books to the shelf, <b>smallest number first</b>.\n" +
                              "Take them <b>in table order</b>, one at a time, and slide each into its gap.\n" +
                              "<size=20>Click the glowing book or press F  ·  click a blue gap or press its number  ·  H hint</size>";
            intro.lines = new[]
            {
                "Floor 3.  A library, warm and quiet.",
                "Eight books on the reading table.  One is already back on the shelf.",
                "One book at a time.  Keep the shelf in order."
            };

            BuildStairwell(root, HD + T, H, DOOR_W, lm, "FLOOR 4", mFloor, mWall, mCeil, mWoodDark, mExit);
            BrightPostFx(sys, "Level3_PostFX", 0.2f);

            BrightEnvironment(new Color(0.84f, 0.84f, 0.86f), new Color(0.70f, 0.66f, 0.60f), new Color(0.44f, 0.38f, 0.32f));
            var sun = Sun(root, new Vector3(30f, -112f, 0f), new Color(1f, 0.93f, 0.80f), 1.7f);
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE, 2);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
            Debug.Log("Sort or Stay: Level 3 'The Library' built and saved to " + SCENE + ". Press Play.");
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mFloor = Pbr("L3_Floor", "WoodFloor064", new Color(0.98f, 0.88f, 0.74f), 3f, 0.42f, 0f);
            mWall = Pbr("L3_Wall", "PaintedPlaster017", new Color(0.98f, 0.95f, 0.88f), 3f, 0.1f, 0f);
            mWainscot = Pbr("L3_Wainscot", "WoodFloor064", new Color(0.66f, 0.47f, 0.32f), 1.2f, 0.35f, 0f);
            mCeil = Flat("L3_Ceiling", new Color(0.97f, 0.96f, 0.93f), 0.1f, 0f);
            mBeam = Pbr("L3_Beam", "WoodFloor064", new Color(0.58f, 0.42f, 0.28f), 0.8f, 0.25f, 0f);
            mWood = Pbr("L3_Wood", "WoodFloor064", new Color(0.78f, 0.58f, 0.40f), 0.7f, 0.4f, 0f);
            mWoodDark = Pbr("L3_WoodDark", "WoodFloor064", new Color(0.46f, 0.31f, 0.20f), 0.7f, 0.4f, 0f);
            mPaper = Flat("L3_Pages", new Color(0.95f, 0.92f, 0.82f), 0.1f, 0f);
            mSticker = Flat("L3_Sticker", new Color(0.98f, 0.97f, 0.93f), 0.2f, 0f);
            mGold = Flat("L3_Gold", new Color(0.86f, 0.68f, 0.28f), 0.7f, 0.8f);
            mBrass = Flat("L3_Brass", new Color(0.80f, 0.64f, 0.32f), 0.65f, 0.7f);
            mDark = Flat("L3_Dark", new Color(0.12f, 0.12f, 0.13f), 0.4f, 0.2f);
            mWhite = Flat("L3_White", new Color(0.96f, 0.95f, 0.92f), 0.3f, 0f);
            mFrame = Flat("L3_WindowFrame", new Color(0.95f, 0.94f, 0.9f), 0.4f, 0f);
            mGlass = Glass("L3_Glass", new Color(0.85f, 0.93f, 1f), 0.14f);
            mRug = Pbr("L3_Rug", "Carpet015", new Color(0.78f, 0.36f, 0.30f), 2f, 0.05f, 0f);
            mFabric = Flat("L3_Fabric", new Color(0.24f, 0.42f, 0.38f), 0.15f, 0f);
            mGlobe = Flat("L3_Globe", new Color(0.36f, 0.58f, 0.72f), 0.55f, 0f);
            mShadeGreen = Emissive("L3_BankerShade", new Color(0.10f, 0.42f, 0.24f), new Color(0.2f, 0.5f, 0.3f), 0.25f);
            mBulb = Emissive("L3_Bulb", new Color(1f, 0.95f, 0.85f), new Color(1f, 0.9f, 0.72f), 2.4f);
            mGap = Emissive("L3_Gap", new Color(0.2f, 0.75f, 1f), new Color(0.2f, 0.75f, 1f), 2f);
            mScreen = Emissive("L3_Screen", new Color(0.02f, 0.05f, 0.06f), new Color(0.02f, 0.14f, 0.12f), 1f);
            mExit = Emissive("L3_Exit", new Color(0.05f, 0.5f, 0.2f), new Color(0.1f, 0.9f, 0.35f), 1.6f);
            mLamp = Emissive("L3_DoorLamp", new Color(1f, 0.12f, 0.08f), new Color(1f, 0.12f, 0.08f), 4f);

            Color[] covers =
            {
                new Color(0.55f, 0.12f, 0.10f), new Color(0.10f, 0.28f, 0.50f), new Color(0.14f, 0.40f, 0.24f), new Color(0.52f, 0.36f, 0.16f),
                new Color(0.32f, 0.16f, 0.36f), new Color(0.80f, 0.70f, 0.48f), new Color(0.15f, 0.15f, 0.16f), new Color(0.70f, 0.30f, 0.12f),
                new Color(0.20f, 0.45f, 0.52f), new Color(0.60f, 0.52f, 0.40f)
            };
            mCovers = new Material[covers.Length];
            for (int i = 0; i < covers.Length; i++) mCovers[i] = Flat("L3_Cover_" + i, covers[i], 0.3f, 0f);
        }

        // ============================================================ room

        static void BuildRoom(Transform p)
        {
            var room = Group("Room", p);
            Cube("Floor", new Vector3(0f, -T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mFloor, room);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mCeil, room);
            for (int i = 0; i < 4; i++)
                Cube("Beam_" + i, new Vector3(0f, H - 0.11f, -3.3f + i * 2.2f), new Vector3(W, 0.22f, 0.24f), mBeam, room, false);

            float[] wz = { -2.6f, 0f, 2.6f };
            var holes = new List<Hole>();
            foreach (float z in wz) holes.Add(new Hole(z, 1.5f, 0.85f, 3.1f));
            WallAlongZ(room, "Wall_Right", HW + T * 0.5f, -HD - T, HD + T, H, T, mWall, holes.ToArray());
            for (int i = 0; i < wz.Length; i++)
                WindowZ(room, "Window_" + i, HW + T * 0.5f, wz[i], 1.5f, 0.85f, 3.1f, T, mFrame, mGlass, mWood);
            WallAlongZ(room, "Wall_Left", -HW - T * 0.5f, -HD - T, HD + T, H, T, mWall);
            WallAlongX(room, "Wall_Back", -HD - T * 0.5f, -HW, HW, H, T, mWall);
            WallAlongX(room, "Wall_Front", HD + T * 0.5f, -HW, HW, H, T, mWall, new Hole(0f, DOOR_W, 0f, DOOR_H));

            // wood wainscot on the walls that are not covered by bookcases
            float wh = 0.95f;
            foreach (float z in new[] { -3.85f, -1.3f, 1.3f, 3.85f })
            {
                float len = (z == -1.3f || z == 1.3f) ? 1.1f : 1.1f;
                Cube("Wainscot_R", new Vector3(HW - 0.012f, wh * 0.5f, z), new Vector3(0.024f, wh, len), mWainscot, room, false);
            }
            Cube("Rail_R", new Vector3(HW - 0.02f, wh, 0f), new Vector3(0.04f, 0.04f, D), mWoodDark, room, false);

            // entrance (left wall), closed behind us
            var ent = Group("EntranceDoor", room);
            ent.localPosition = new Vector3(-HW, 0f, 0f);
            ent.localRotation = Quaternion.Euler(0f, 90f, 0f);      // local +Z points into the room (+X)
            Cube("Frame", new Vector3(0f, 1.2f, 0.03f), new Vector3(1.3f, 2.4f, 0.06f), mWoodDark, ent, false);
            Cube("Slab", new Vector3(0f, 1.15f, 0.07f), new Vector3(1.1f, 2.28f, 0.04f), mWood, ent, false);
            Cube("Panel_Top", new Vector3(0f, 1.62f, 0.095f), new Vector3(0.8f, 0.8f, 0.01f), mWoodDark, ent, false);
            Cube("Panel_Bottom", new Vector3(0f, 0.6f, 0.095f), new Vector3(0.8f, 0.7f, 0.01f), mWoodDark, ent, false);
            Sphere("Knob", new Vector3(0.42f, 1.05f, 0.11f), 0.07f, mBrass, ent);

            // "quiet please" plaque
            var q = Group("QuietSign", room);
            q.localPosition = new Vector3(-HW + 0.01f, 2.75f, 0f);
            q.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Cube("Plate", new Vector3(0f, 0f, -0.01f), new Vector3(1.1f, 0.24f, 0.02f), mBrass, q, false);
            Text("Text", q, new Vector3(0f, 0f, -0.022f), new Vector2(1.0f, 0.16f), "QUIET PLEASE", new Color(0.22f, 0.15f, 0.08f), TextAlignmentOptions.Center, true);

            // exit sign above the glass doors
            Cube("ExitSign", new Vector3(0f, DOOR_H + 0.28f, HD - 0.02f), new Vector3(0.7f, 0.22f, 0.04f), mExit, room, false);
            Text("ExitText", room, new Vector3(0f, DOOR_H + 0.28f, HD - 0.045f), new Vector2(0.62f, 0.16f), "EXIT", Color.white, TextAlignmentOptions.Center, true);

            // big rug under the reading area
            Cube("Rug", new Vector3(0f, 0.004f, 0f), new Vector3(6.2f, 0.008f, 4.6f), mRug, room, false);
            Cube("RugBorder", new Vector3(0f, 0.003f, 0f), new Vector3(6.4f, 0.006f, 4.8f), mWoodDark, room, false);
        }

        // ============================================================ reading table + books

        static void BuildTable(Transform p, out Transform[] slots)
        {
            var g = Group("ReadingTable", p);
            g.localPosition = new Vector3(0f, 0f, TABLE_Z);
            float len = 3.4f, dep = 0.95f;
            Cube("Top", new Vector3(0f, TABLE_TOP - 0.025f, 0f), new Vector3(len, 0.05f, dep), mWood, g);
            Cube("Apron_F", new Vector3(0f, TABLE_TOP - 0.11f, dep * 0.5f - 0.06f), new Vector3(len - 0.2f, 0.12f, 0.03f), mWoodDark, g, false);
            Cube("Apron_B", new Vector3(0f, TABLE_TOP - 0.11f, -dep * 0.5f + 0.06f), new Vector3(len - 0.2f, 0.12f, 0.03f), mWoodDark, g, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cube("Leg", new Vector3(sx * (len * 0.5f - 0.12f), (TABLE_TOP - 0.05f) * 0.5f, sz * (dep * 0.5f - 0.1f)), new Vector3(0.08f, TABLE_TOP - 0.05f, 0.08f), mWoodDark, g);

            // brass plate on the aisle edge: which way the table order runs
            var plate = Group("OrderPlate", g);
            plate.localPosition = new Vector3(0f, TABLE_TOP + 0.002f, dep * 0.5f - 0.06f);
            Cube("Plate", Vector3.zero, new Vector3(1.6f, 0.004f, 0.07f), mBrass, plate, false);
            var t = Text("Text", plate, new Vector3(0f, 0.003f, 0f), new Vector2(1.55f, 0.06f), "TABLE ORDER:  LEFT  TO  RIGHT",
                         new Color(0.22f, 0.15f, 0.08f), TextAlignmentOptions.Center, true);
            t.rectTransform.localRotation = Quaternion.Euler(90f, 180f, 0f);   // lies flat, read from the aisle

            slots = new Transform[BOOKS];
            for (int i = 0; i < BOOKS; i++)
            {
                var s = new GameObject("TableSlot_" + i).transform;
                s.SetParent(p, false);
                s.position = new Vector3(TableX(i), TABLE_TOP, TABLE_BOOK_Z);
                s.rotation = Quaternion.Euler(0f, 180f, 0f);        // spines face the aisle (+Z)
                slots[i] = s;
            }

            // two banker's lamps at the ends of the table
            foreach (float x in new[] { -1.45f, 1.45f })
            {
                var l = Group("BankerLamp", g);
                l.localPosition = new Vector3(x, TABLE_TOP, -0.2f);
                CylY("Base", new Vector3(0f, 0.015f, 0f), 0.16f, 0.03f, mBrass, l);
                CylY("Stem", new Vector3(0f, 0.2f, 0f), 0.025f, 0.38f, mBrass, l);
                var shade = CylX("Shade", new Vector3(0f, 0.4f, 0.04f), 0.12f, 0.36f, mShadeGreen, l);
                shade.transform.localScale = new Vector3(0.12f, 0.18f, 0.16f);
                Cube("BulbStrip", new Vector3(0f, 0.36f, 0.04f), new Vector3(0.28f, 0.02f, 0.06f), mBulb, l, false);
                PointLight("Light", l, l.TransformPoint(new Vector3(0f, 0.3f, 0.1f)), new Color(1f, 0.88f, 0.66f), 0.8f, 2.4f, false);
            }
        }

        static List<SortItem> BuildBooks(Transform p, Transform[] tableSlots)
        {
            var parent = Group("Books", p);
            var list = new List<SortItem>();
            var rng = new System.Random(20261021);
            int[] coverOrder = { 0, 1, 2, 3, 4, 7, 8, 6 };
            for (int i = 0; i < BOOKS; i++)
            {
                float t = 0.078f + (float)rng.NextDouble() * 0.022f;
                float h = 0.25f + (float)rng.NextDouble() * 0.05f;
                float d = 0.2f;
                var cover = mCovers[coverOrder[i % coverOrder.Length]];

                var b = new GameObject("Book_" + (i + 1));
                b.transform.SetParent(parent, false);
                b.transform.SetPositionAndRotation(tableSlots[i].position, tableSlots[i].rotation);

                var board = Cube("Board_L", new Vector3(-t * 0.5f + 0.003f, h * 0.5f, 0f), new Vector3(0.006f, h, d), cover, b.transform, false);
                Cube("Board_R", new Vector3(t * 0.5f - 0.003f, h * 0.5f, 0f), new Vector3(0.006f, h, d), cover, b.transform, false);
                Cube("Spine", new Vector3(0f, h * 0.5f, -d * 0.5f + 0.005f), new Vector3(t, h, 0.01f), cover, b.transform, false);
                Cube("Pages", new Vector3(0f, h * 0.5f, 0.004f), new Vector3(t - 0.012f, h - 0.014f, d - 0.016f), mPaper, b.transform, false);
                Cube("Band_Top", new Vector3(0f, h - 0.028f, -d * 0.5f - 0.001f), new Vector3(t, 0.008f, 0.003f), mGold, b.transform, false);
                Cube("Band_Bottom", new Vector3(0f, 0.028f, -d * 0.5f - 0.001f), new Vector3(t, 0.008f, 0.003f), mGold, b.transform, false);

                float sy = h * 0.5f + 0.005f;
                var sticker = Cube("Sticker", new Vector3(0f, sy, -d * 0.5f - 0.0015f), new Vector3(t - 0.014f, 0.155f, 0.003f), mSticker, b.transform, false);
                string v = PREVIEW[i].ToString("000");
                var num = Text("CallNumber", b.transform, new Vector3(0f, sy, -d * 0.5f - 0.0035f), new Vector2(t - 0.02f, 0.15f),
                               v[0] + "\n" + v[1] + "\n" + v[2], InkDark, TextAlignmentOptions.Center, true);
                num.lineSpacing = -18f;

                var bc = b.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, h * 0.5f, 0f);
                bc.size = new Vector3(t + 0.01f, h, d);

                var item = b.AddComponent<SortItem>();
                item.value = PREVIEW[i];
                item.label = num;
                item.rend = board.GetComponent<Renderer>();
                item.tagRenderer = sticker.GetComponent<Renderer>();
                item.tintModel = false;
                list.Add(item);
            }
            return list;
        }

        // ============================================================ return shelf

        static void BuildReturnShelf(Transform p, out Transform[] slots, out TMP_Text board)
        {
            var g = Group("ReturnShelf", p);
            g.localPosition = new Vector3(0f, 0f, SHELF_Z);
            float wid = 2.4f, hgt = 1.6f, dep = 0.4f;    // waist-high, so the camera can see over it
            float front = SHELF_FRONT - SHELF_Z;          // local z of the front face (-0.2)
            Cube("Side_L", new Vector3(-wid * 0.5f + 0.025f, hgt * 0.5f, 0f), new Vector3(0.05f, hgt, dep), mWoodDark, g);
            Cube("Side_R", new Vector3(wid * 0.5f - 0.025f, hgt * 0.5f, 0f), new Vector3(0.05f, hgt, dep), mWoodDark, g);
            Cube("Back", new Vector3(0f, hgt * 0.5f, dep * 0.5f - 0.01f), new Vector3(wid, hgt, 0.02f), mWood, g);
            Cube("Top", new Vector3(0f, hgt - 0.02f, 0f), new Vector3(wid + 0.06f, 0.04f, dep + 0.04f), mWoodDark, g, false);
            Cube("Plinth", new Vector3(0f, 0.05f, 0f), new Vector3(wid, 0.1f, dep), mWoodDark, g);

            float[] boards = { 0.55f, SHELF_Y - 0.015f };
            foreach (float y in boards)
                Cube("Board", new Vector3(0f, y, 0f), new Vector3(wid - 0.1f, 0.03f, dep - 0.02f), mWood, g);

            // the return board: a brass lip and a strip of soft light along the front
            Cube("ReturnLip", new Vector3(0f, SHELF_Y - 0.04f, front - 0.012f), new Vector3(wid - 0.1f, 0.06f, 0.02f), mBrass, g, false);
            var lab = Text("ReturnLabel", g, new Vector3(0f, SHELF_Y - 0.04f, front - 0.024f), new Vector2(wid - 0.2f, 0.045f),
                           "RETURN SHELF    -    SMALLEST  TO  LARGEST,  LEFT  TO  RIGHT", new Color(0.22f, 0.15f, 0.08f), TextAlignmentOptions.Center, true);
            Cube("ShelfLight", new Vector3(0f, hgt - 0.05f, -0.08f), new Vector3(wid - 0.2f, 0.01f, 0.02f), mBulb, g, false);
            PointLight("ShelfGlow", g, g.TransformPoint(new Vector3(0f, hgt - 0.15f, -0.4f)), new Color(1f, 0.92f, 0.78f), 0.7f, 1.8f, false);

            // decorative books on the other boards (one merged mesh)
            var rng = new System.Random(7);
            var batch = new BookBatch();
            batch.Row(g, new Vector3(-wid * 0.5f + 0.07f, 0.1f, 0f), wid - 0.14f, 0.32f, dep - 0.05f, rng, 0.0f);
            batch.Row(g, new Vector3(-wid * 0.5f + 0.07f, 0.565f, 0f), wid - 0.14f, 0.3f, dep - 0.05f, rng, 0.0f);
            batch.Row(g, new Vector3(-wid * 0.5f + 0.07f, SHELF_Y, 0f), 0.15f, 0.29f, dep - 0.05f, rng, 0f);              // bookends of old books
            batch.Row(g, new Vector3(wid * 0.5f - 0.22f, SHELF_Y, 0f), 0.15f, 0.29f, dep - 0.05f, rng, 0f);
            batch.Build(g, "DecorBooks", mCovers, mPaper);

            // the live sign on top
            var sign = Group("Sign", g);
            sign.localPosition = new Vector3(0f, hgt + 0.2f, front + 0.05f);
            Cube("Plaque", new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0.36f, 0.04f), mWoodDark, sign, false);
            Cube("Face", new Vector3(0f, 0f, -0.022f), new Vector3(1.4f, 0.28f, 0.004f), mWhite, sign, false);
            board = Text("Text", sign, new Vector3(0f, 0f, -0.026f), new Vector2(1.34f, 0.25f), "<b>RETURN SHELF</b>",
                         new Color(0.2f, 0.14f, 0.08f), TextAlignmentOptions.Center, false);

            slots = new Transform[BOOKS];
            for (int j = 0; j < BOOKS; j++)
            {
                var s = new GameObject("ShelfSlot_" + j).transform;
                s.SetParent(p, false);
                s.position = new Vector3(ShelfX(j), SHELF_Y, SHELF_FRONT + 0.1f);
                s.rotation = Quaternion.identity;                 // spines face the aisle (-Z)
                slots[j] = s;
            }
        }

        static InsertSlot[] BuildGaps(Transform p, Transform[] shelfSlots)
        {
            var g = Group("ShelfGaps", p);
            var gaps = new InsertSlot[BOOKS + 1];
            for (int m = 0; m <= BOOKS; m++)
            {
                float x = ShelfX(0) + SHELF_PITCH * m - SHELF_PITCH * 0.5f;
                var go = new GameObject("Gap_" + m);
                go.transform.SetParent(g, false);
                go.transform.position = new Vector3(x, SHELF_Y, SHELF_FRONT + 0.1f);
                var glow = Cube("Glow", new Vector3(0f, 0.15f, -0.02f), new Vector3(0.014f, 0.28f, 0.17f), mGap, go.transform, false);
                glow.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.15f, -0.08f);
                bc.size = new Vector3(0.075f, 0.32f, 0.3f);
                var slot = go.AddComponent<InsertSlot>();
                slot.index = m;
                slot.glow = glow.GetComponent<Renderer>();
                // gap number above the glow: press it on the keyboard to slide the book in there
                Text("Number", go.transform, new Vector3(0f, 0.36f, -0.12f), new Vector2(0.09f, 0.07f), (m + 1).ToString(),
                     new Color(0.1f, 0.45f, 0.75f), TextAlignmentOptions.Center, true);
                gaps[m] = slot;
            }
            return gaps;
        }

        // ============================================================ catalogue kiosk

        static Terminal BuildKiosk(Transform p)
        {
            var g = Group("CatalogueKiosk", p);
            g.localPosition = new Vector3(2.75f, 0f, 0.15f);
            g.localRotation = Quaternion.Euler(0f, 90f, 0f);     // local -Z faces -X (toward the aisle)
            Cube("Foot", new Vector3(0f, 0.03f, 0f), new Vector3(0.5f, 0.06f, 0.4f), mWoodDark, g);
            Cube("Post", new Vector3(0f, 0.55f, 0.02f), new Vector3(0.16f, 1.0f, 0.12f), mWood, g);
            var head = Group("Head", g);
            head.localPosition = new Vector3(0f, 1.2f, 0f);
            head.localRotation = Quaternion.Euler(15f, 0f, 0f);       // tilted back so the screen looks up a little
            Cube("Bezel", new Vector3(0f, 0f, 0f), new Vector3(0.7f, 0.5f, 0.06f), mDark, head);
            Cube("Screen", new Vector3(0f, 0.01f, -0.031f), new Vector3(0.64f, 0.42f, 0.004f), mScreen, head, false);
            var txt = Text("ScreenText", head, new Vector3(0f, 0.01f, -0.034f), new Vector2(0.6f, 0.38f), "<b>CATALOGUE</b>   FLOOR 3",
                           new Color(0.6f, 1f, 0.8f), TextAlignmentOptions.TopLeft, false);
            Text("Brand", g, new Vector3(0f, 0.85f, -0.042f), new Vector2(0.14f, 0.04f), "ASK ME", Color.white, TextAlignmentOptions.Center, true);

            var term = g.gameObject.AddComponent<Terminal>();
            term.screen = txt;
            term.prompt = "Catalogue  ·  ask for a hint";
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.75f, 0f);
            bc.size = new Vector3(0.7f, 1.5f, 0.45f);
            return term;
        }

        // ============================================================ wall bookcases

        static void BuildWallBookcases(Transform p)
        {
            var g = Group("Bookcases", p);
            var rng = new System.Random(1234);
            // back wall
            for (int i = 0; i < 4; i++) Bookcase(g, "Back_" + i, new Vector3(-3.45f + i * 2.3f, 0f, -HD + 0.22f), 0f, 2.2f, rng);
            // front wall, either side of the doors
            Bookcase(g, "Front_L", new Vector3(-3.0f, 0f, HD - 0.22f), 180f, 3.2f, rng);
            Bookcase(g, "Front_R", new Vector3(3.0f, 0f, HD - 0.22f), 180f, 3.2f, rng);
            // left wall, either side of the entrance
            Bookcase(g, "Left_A", new Vector3(-HW + 0.22f, 0f, -2.5f), 90f, 2.8f, rng);
            Bookcase(g, "Left_B", new Vector3(-HW + 0.22f, 0f, 2.5f), 90f, 2.8f, rng);
        }

        /// A tall bookcase whose front faces local -Z after 'yaw' (yaw 0 = faces +Z... see below).
        static void Bookcase(Transform p, string name, Vector3 pos, float yaw, float wid, System.Random rng)
        {
            var g = Group("Bookcase_" + name, p);
            g.localPosition = pos;
            // the builders below put the open front on local +Z
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float hgt = 2.7f, dep = 0.4f;
            Cube("Side_L", new Vector3(-wid * 0.5f + 0.025f, hgt * 0.5f, 0f), new Vector3(0.05f, hgt, dep), mWoodDark, g, false);
            Cube("Side_R", new Vector3(wid * 0.5f - 0.025f, hgt * 0.5f, 0f), new Vector3(0.05f, hgt, dep), mWoodDark, g, false);
            Cube("Back", new Vector3(0f, hgt * 0.5f, -dep * 0.5f + 0.01f), new Vector3(wid, hgt, 0.02f), mWood, g, false);
            Cube("Crown", new Vector3(0f, hgt + 0.03f, 0.02f), new Vector3(wid + 0.08f, 0.06f, dep + 0.06f), mWoodDark, g, false);
            Cube("Plinth", new Vector3(0f, 0.05f, 0f), new Vector3(wid, 0.1f, dep), mWoodDark, g, false);
            var batch = new BookBatch();
            float y = 0.1f;
            for (int s = 0; s < 5; s++)
            {
                if (s > 0) Cube("Shelf", new Vector3(0f, y - 0.015f, 0f), new Vector3(wid - 0.1f, 0.03f, dep - 0.02f), mWood, g, false);
                batch.Row(g, new Vector3(-wid * 0.5f + 0.06f, y, 0f), wid - 0.12f, 0.36f, dep - 0.05f, rng, 0.06f);
                y += 0.52f;
            }
            batch.Build(g, "Books", mCovers, mPaper);
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, hgt * 0.5f, 0f);
            bc.size = new Vector3(wid, hgt, dep);
        }

        /// Collects many little book boxes and merges them into one mesh (one submesh per material).
        class BookBatch
        {
            readonly List<CombineInstance>[] perMat = new List<CombineInstance>[11];
            static Mesh cube;
            Transform owner;

            public BookBatch()
            {
                for (int i = 0; i < perMat.Length; i++) perMat[i] = new List<CombineInstance>();
                if (cube == null) cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            }

            /// A row of books standing along local +X from 'start' (bottom-left), spines toward local +Z.
            public void Row(Transform space, Vector3 start, float length, float maxH, float depth, System.Random rng, float gapChance)
            {
                owner = space;
                float x = 0f;
                while (x < length - 0.03f)
                {
                    float t = 0.025f + (float)rng.NextDouble() * 0.04f;
                    if (x + t > length) break;
                    if (rng.NextDouble() < gapChance) { x += 0.05f + (float)rng.NextDouble() * 0.1f; continue; }
                    float h = maxH * (0.62f + (float)rng.NextDouble() * 0.36f);
                    float d = depth * (0.75f + (float)rng.NextDouble() * 0.25f);
                    int mat = rng.Next(10);
                    bool lean = rng.NextDouble() < 0.06 && x > 0.1f;
                    Quaternion rot = lean ? Quaternion.Euler(0f, 0f, -12f) : Quaternion.identity;
                    Vector3 c = start + new Vector3(x + t * 0.5f, h * 0.5f, depth * 0.5f - d * 0.5f);
                    Add(mat, Matrix4x4.TRS(c, rot, new Vector3(t, h, d)));
                    // pages peek out at the top
                    Add(10, Matrix4x4.TRS(c + new Vector3(0f, h * 0.5f - 0.004f, -0.004f), rot, new Vector3(t * 0.8f, 0.01f, d * 0.9f)));
                    x += t + 0.002f;
                }
            }

            void Add(int mat, Matrix4x4 m)
            {
                perMat[mat].Add(new CombineInstance { mesh = cube, transform = m });
            }

            public void Build(Transform parent, string name, Material[] covers, Material pages)
            {
                var mats = new List<Material>();
                var subs = new List<CombineInstance>();
                for (int i = 0; i < perMat.Length; i++)
                {
                    if (perMat[i].Count == 0) continue;
                    var part = new Mesh { indexFormat = IndexFormat.UInt32 };
                    part.CombineMeshes(perMat[i].ToArray(), true, true);
                    subs.Add(new CombineInstance { mesh = part, transform = Matrix4x4.identity });
                    mats.Add(i < 10 ? covers[i % covers.Length] : pages);
                }
                if (subs.Count == 0) return;
                var mesh = new Mesh { name = name + "_Mesh", indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(subs.ToArray(), false, true);
                mesh.RecalculateBounds();
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats.ToArray();
                mr.shadowCastingMode = ShadowCastingMode.On;
                foreach (var s in subs) Object.DestroyImmediate(s.mesh);
            }
        }

        // ============================================================ props

        static void BuildProps(Transform p)
        {
            var g = Group("Props", p);

            // armchairs by the windows, each with a side table
            Armchair(g, new Vector3(3.95f, 0f, -1.35f), -110f);
            Armchair(g, new Vector3(3.95f, 0f, 1.35f), -70f);
            var st = Group("SideTable", g);
            st.localPosition = new Vector3(4.25f, 0f, 0f);
            CylY("Top", new Vector3(0f, 0.55f, 0f), 0.5f, 0.03f, mWood, st, true);
            CylY("Leg", new Vector3(0f, 0.27f, 0f), 0.06f, 0.54f, mWoodDark, st);
            CylY("Foot", new Vector3(0f, 0.02f, 0f), 0.34f, 0.03f, mWoodDark, st);
            Cube("OpenBook", new Vector3(0.04f, 0.575f, 0f), new Vector3(0.3f, 0.02f, 0.21f), mPaper, st, false);

            // a globe on a stand
            var gl = Group("Globe", g);
            gl.localPosition = new Vector3(-3.9f, 0f, -0.95f);
            CylY("Foot", new Vector3(0f, 0.03f, 0f), 0.4f, 0.06f, mWoodDark, gl, true);
            CylY("Stand", new Vector3(0f, 0.45f, 0f), 0.05f, 0.8f, mWoodDark, gl);
            var ring = CylY("Meridian", new Vector3(0f, 1.02f, 0f), 0.48f, 0.01f, mBrass, gl);
            ring.transform.localRotation = Quaternion.Euler(90f, 0f, 23f);
            Sphere("Earth", new Vector3(0f, 1.02f, 0f), 0.42f, mGlobe, gl);

            // a glass display case with the "golden book" by the doors
            var dc = Group("DisplayCase", g);
            dc.localPosition = new Vector3(-1.55f, 0f, HD - 0.75f);
            Cube("Base", new Vector3(0f, 0.45f, 0f), new Vector3(0.7f, 0.9f, 0.5f), mWoodDark, dc);
            var glass = Cube("Glass", new Vector3(0f, 1.15f, 0f), new Vector3(0.66f, 0.5f, 0.46f), mGlass, dc, false);
            glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Cube("Lid", new Vector3(0f, 1.41f, 0f), new Vector3(0.7f, 0.03f, 0.5f), mWoodDark, dc, false);
            var gb = Cube("GoldenBook", new Vector3(0f, 1.0f, 0f), new Vector3(0.26f, 0.05f, 0.2f), mGold, dc, false);
            gb.transform.localRotation = Quaternion.Euler(0f, 20f, 0f);
            Text("Card", dc, new Vector3(0f, 0.86f, -0.252f), new Vector2(0.6f, 0.06f), "FIRST EDITION  -  SORTED", InkDark, TextAlignmentOptions.Center, true);

            // a book trolley at the left end of the table
            var tr = Group("BookTrolley", g);
            tr.localPosition = new Vector3(-2.4f, 0f, -2.35f);
            tr.localRotation = Quaternion.Euler(0f, 12f, 0f);
            Cube("Frame", new Vector3(0f, 0.5f, 0f), new Vector3(0.9f, 0.04f, 0.45f), mWoodDark, tr);
            Cube("Lower", new Vector3(0f, 0.18f, 0f), new Vector3(0.9f, 0.04f, 0.45f), mWoodDark, tr, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Cube("Post", new Vector3(sx * 0.42f, 0.42f, sz * 0.2f), new Vector3(0.03f, 0.7f, 0.03f), mWoodDark, tr, false);
                    Sphere("Wheel", new Vector3(sx * 0.42f, 0.04f, sz * 0.2f), 0.08f, mDark, tr);
                }
            var rng = new System.Random(99);
            var batch = new BookBatch();
            batch.Row(tr, new Vector3(-0.42f, 0.52f, -0.2f), 0.84f, 0.26f, 0.2f, rng, 0.05f);
            batch.Row(tr, new Vector3(-0.42f, 0.2f, -0.2f), 0.84f, 0.26f, 0.2f, rng, 0.1f);
            batch.Build(tr, "Books", mCovers, mPaper);
        }

        static void Armchair(Transform p, Vector3 pos, float yaw)
        {
            var a = Group("Armchair", p);
            a.localPosition = pos;
            a.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Cube("Seat", new Vector3(0f, 0.32f, 0f), new Vector3(0.7f, 0.2f, 0.62f), mFabric, a);
            Cube("Cushion", new Vector3(0f, 0.45f, 0.02f), new Vector3(0.58f, 0.08f, 0.56f), mFabric, a, false);
            Cube("Back", new Vector3(0f, 0.72f, -0.27f), new Vector3(0.7f, 0.62f, 0.14f), mFabric, a, false);
            Cube("Arm_L", new Vector3(-0.31f, 0.52f, 0.02f), new Vector3(0.1f, 0.24f, 0.62f), mFabric, a, false);
            Cube("Arm_R", new Vector3(0.31f, 0.52f, 0.02f), new Vector3(0.1f, 0.24f, 0.62f), mFabric, a, false);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cube("Leg", new Vector3(sx * 0.3f, 0.11f, sz * 0.25f), new Vector3(0.05f, 0.22f, 0.05f), mWoodDark, a, false);
        }

        // ============================================================ lights

        static void BuildLights(Transform p)
        {
            var g = Group("Lights", p);
            float[] xs = { -1.6f, 0f, 1.6f };
            for (int i = 0; i < xs.Length; i++)
            {
                var l = Group("Pendant_" + i, g);
                l.localPosition = new Vector3(xs[i], H, -0.1f);
                CylY("Rod", new Vector3(0f, -0.4f, 0f), 0.02f, 0.8f, mBrass, l);
                CylY("Cap", new Vector3(0f, -0.82f, 0f), 0.12f, 0.05f, mBrass, l);
                Sphere("Globe", new Vector3(0f, -1.0f, 0f), 0.3f, mBulb, l);
                PointLight("Light", l, new Vector3(xs[i], H - 1.1f, -0.1f), new Color(1f, 0.92f, 0.8f), 1.9f, 7.5f, i == 1);
            }
            PointLight("Fill_Back", g, new Vector3(0f, 2.8f, -3.4f), new Color(1f, 0.95f, 0.88f), 1.0f, 7f, false);
            PointLight("Fill_Front", g, new Vector3(0f, 2.8f, 3.4f), new Color(1f, 0.95f, 0.88f), 1.0f, 7f, false);
            PointLight("Fill_Left", g, new Vector3(-3.6f, 2.6f, 0f), new Color(1f, 0.95f, 0.88f), 0.8f, 6f, false);
        }
    }
}
