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
    /// Builds Floor 2, "The Storage Room" (selection sort): a bright warehouse with seven scale bays,
    /// seven crates of hidden weight, an overhead crane, a scale-control desk and loading doors.
    /// Menu: Sort or Stay > Build Level 2 (Storage Room). Re-running it rebuilds and overwrites the scene.
    public static class BuildLevel2
    {
        const string SCENE = ROOT + "Scenes/Level2_Selection.unity";

        const float W = 10f, D = 10f, H = 4.4f, T = 0.2f;
        const float HW = W * 0.5f, HD = D * 0.5f;
        const float DOOR_W = 2.0f, DOOR_H = 2.6f;
        const int BAYS = 7;
        const float PITCH = 1.05f, ROW_Z = 0.6f;
        static readonly Vector3 SPAWN = new Vector3(0f, 0f, -3.4f);
        static readonly int[] PREVIEW = { 14, 6, 22, 3, 17, 9, 28 };

        static Material mFloor, mWall, mBand, mCeil, mSteel, mDark, mTread, mYellow, mCrate, mCrateFrame, mPaper, mPad, mLed, mNext,
                        mFrame, mGlass, mScreen, mRed, mGreen, mShade, mBulb, mCardboard, mPallet, mExit, mLamp, mBlue, mDrumRed, mDrumBlue, mWhite;

        static float BayX(int i) => -PITCH * (BAYS - 1) * 0.5f + PITCH * i;

        [MenuItem("Sort or Stay/Build Level 2 (Storage Room)")]
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

            var root = new GameObject("Level2_StorageRoom").transform;
            BuildRoom(root);
            var door = DoubleDoor(root, HD, T, DOOR_W, DOOR_H, mSteel, mDark, mLamp, mGlass, false);
            door.lockedMessage = "The loading doors won't budge.  The board says: LIGHT TO HEAVY.";

            Transform[] slots;
            Renderer[] leds; Transform[] padTops; TMP_Text[] readouts; Renderer next;
            BuildBays(root, out slots, out leds, out padTops, out readouts, out next);
            var crates = BuildCrates(root, slots);
            BuildCrane(root, out Transform trolley, out Transform cable, out Transform hook);
            var board = BuildBoard(root);
            var terminal = BuildConsole(root, out PushButton scanBtn, out PushButton hintBtn);
            BuildFloorMarkings(root);
            BuildProps(root);
            BuildLights(root);

            // ---------- player ----------
            var player = BuildPlayer(SPAWN, 0f);

            // ---------- systems ----------
            var sys = new GameObject("Systems").transform;
            BuildUI(sys, "FLOOR 2  ·  THE STORAGE ROOM", 2);

            var puzzle = sys.gameObject.AddComponent<SelectionPuzzle>();
            puzzle.slots = slots;
            puzzle.items = crates;
            puzzle.door = door;
            puzzle.padLeds = leds;
            puzzle.padTops = padTops;
            puzzle.padReadouts = readouts;
            puzzle.trolley = trolley;
            puzzle.cable = cable;
            puzzle.hook = hook;
            puzzle.board = board;
            puzzle.terminal = terminal;
            puzzle.nextMarker = next;
            terminal.puzzle = puzzle;
            scanBtn.puzzle = puzzle;
            hintBtn.puzzle = puzzle;

            var lm = sys.gameObject.AddComponent<LevelManager>();
            lm.levelNumber = 2;
            lm.nextScene = "Level3_Insertion";
            lm.nextFloorLabel = "Floor 3  ·  the Library  (Insertion Sort)";
            lm.door = door;
            lm.player = player;

            var overview = new GameObject("IntroCamera").transform;
            overview.SetParent(sys, false);
            overview.position = new Vector3(4.1f, 3.55f, -4.5f);
            overview.rotation = Quaternion.LookRotation(new Vector3(-0.6f, 0.5f, 1.4f) - overview.position);
            var intro = sys.gameObject.AddComponent<LevelIntro>();
            intro.player = player;
            intro.puzzle = puzzle;
            intro.overview = overview;
            intro.objective = "Sort the crates <b>light to heavy</b> into bays 1-7.\n" +
                              "Each pass: <b>weigh every crate that's left</b>, then send the <b>lightest</b> to the next bay.\n" +
                              "<size=20>Click a crate or press its bay number (1-7)  ·  Q weighs them all  ·  H hint</size>";
            intro.lines = new[]
            {
                "Floor 2.  A storage room, full of daylight.",
                "Seven crates on seven scales... and every reading is blank.",
                "Light to heavy.  Find the lightest, every single pass."
            };

            BuildStairwell(root, HD + T, H, DOOR_W, lm, "FLOOR 3", mTread, mWall, mCeil, mTread, mExit);
            BrightPostFx(sys, "Level2_PostFX", 0.15f);

            // ---------- lighting ----------
            BrightEnvironment(new Color(0.80f, 0.84f, 0.90f), new Color(0.66f, 0.66f, 0.64f), new Color(0.42f, 0.40f, 0.38f));
            var sun = Sun(root, new Vector3(36f, -108f, 0f), new Color(1f, 0.95f, 0.86f), 1.6f);
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE, 1);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
            Debug.Log("Sort or Stay: Level 2 'The Storage Room' built and saved to " + SCENE + ". Press Play.");
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mFloor = Pbr("L2_Floor", "Concrete034", new Color(0.88f, 0.88f, 0.86f), 4f, 0.32f, 0f);
            mWall = Pbr("L2_Wall", "PaintedPlaster017", new Color(0.97f, 0.96f, 0.93f), 3f, 0.12f, 0f);
            mBand = Flat("L2_WallBand", new Color(0.33f, 0.52f, 0.70f), 0.3f, 0f);
            mCeil = Flat("L2_Ceiling", new Color(0.95f, 0.95f, 0.95f), 0.1f, 0f);
            mSteel = Pbr("L2_Steel", "MetalPlates017A", new Color(0.80f, 0.82f, 0.85f), 1f, 0.45f, 0.3f);
            mDark = Pbr("L2_DarkMetal", "Metal063", new Color(0.36f, 0.37f, 0.40f), 1f, 0.45f, 0.4f);
            mTread = Pbr("L2_Tread", "DiamondPlate009", new Color(0.74f, 0.75f, 0.77f), 2f, 0.4f, 0.4f);
            mPad = Pbr("L2_ScalePlate", "DiamondPlate009", new Color(0.86f, 0.87f, 0.89f), 1f, 0.5f, 0.45f);
            mYellow = Flat("L2_SafetyYellow", new Color(0.98f, 0.78f, 0.08f), 0.4f, 0f);
            mBlue = Flat("L2_RackBlue", new Color(0.12f, 0.36f, 0.72f), 0.45f, 0.2f);
            mCrate = Pbr("L2_CrateWood", "WoodFloor064", new Color(0.95f, 0.82f, 0.64f), 0.5f, 0.2f, 0f);
            mCrateFrame = Pbr("L2_CrateFrame", "WoodFloor064", new Color(0.62f, 0.46f, 0.30f), 0.4f, 0.2f, 0f);
            mPaper = Flat("L2_Label", new Color(0.97f, 0.95f, 0.88f), 0.15f, 0f);
            mCardboard = Flat("L2_Cardboard", new Color(0.74f, 0.57f, 0.38f), 0.15f, 0f);
            mPallet = Pbr("L2_Pallet", "WoodFloor064", new Color(0.85f, 0.70f, 0.50f), 0.6f, 0.15f, 0f);
            mFrame = Flat("L2_WindowFrame", new Color(0.24f, 0.26f, 0.29f), 0.5f, 0.4f);
            mGlass = Glass("L2_Glass", new Color(0.82f, 0.92f, 1f), 0.16f);
            mWhite = Flat("L2_White", new Color(0.96f, 0.96f, 0.95f), 0.3f, 0f);
            mDrumRed = Flat("L2_DrumRed", new Color(0.72f, 0.12f, 0.10f), 0.45f, 0.3f);
            mDrumBlue = Flat("L2_DrumBlue", new Color(0.10f, 0.30f, 0.62f), 0.45f, 0.3f);
            mShade = Flat("L2_LampShade", new Color(0.16f, 0.36f, 0.30f), 0.55f, 0.3f);

            mLed = Emissive("L2_Led", new Color(0.2f, 0.2f, 0.22f), Color.black, 0f);
            mNext = Emissive("L2_NextBay", new Color(1f, 0.62f, 0.1f), new Color(1f, 0.62f, 0.1f), 1.5f);
            mScreen = Emissive("L2_Screen", new Color(0.02f, 0.05f, 0.07f), new Color(0.02f, 0.12f, 0.18f), 1f);
            mRed = Emissive("L2_ButtonRed", new Color(0.8f, 0.1f, 0.07f), new Color(0.9f, 0.12f, 0.06f), 0.8f);
            mGreen = Emissive("L2_ButtonGreen", new Color(0.12f, 0.75f, 0.25f), new Color(0.15f, 0.9f, 0.3f), 0.9f);
            mBulb = Emissive("L2_Bulb", new Color(1f, 0.97f, 0.9f), new Color(1f, 0.93f, 0.8f), 2.5f);
            mExit = Emissive("L2_Exit", new Color(0.05f, 0.5f, 0.2f), new Color(0.1f, 0.9f, 0.35f), 1.6f);
            mLamp = Emissive("L2_DoorLamp", new Color(1f, 0.12f, 0.08f), new Color(1f, 0.12f, 0.08f), 4f);
        }

        // ============================================================ room

        static void BuildRoom(Transform p)
        {
            var room = Group("Room", p);
            Cube("Floor", new Vector3(0f, -T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mFloor, room);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mCeil, room);

            WallAlongZ(room, "Wall_Left", -HW - T * 0.5f, -HD - T, HD + T, H, T, mWall);
            var windows = new List<Hole>();
            float[] wz = { -3.3f, -1.1f, 1.1f, 3.3f };
            foreach (float z in wz) windows.Add(new Hole(z, 1.6f, 2.35f, 3.85f));
            WallAlongZ(room, "Wall_Right", HW + T * 0.5f, -HD - T, HD + T, H, T, mWall, windows.ToArray());
            WallAlongX(room, "Wall_Back", -HD - T * 0.5f, -HW, HW, H, T, mWall);
            WallAlongX(room, "Wall_Front", HD + T * 0.5f, -HW, HW, H, T, mWall, new Hole(0f, DOOR_W, 0f, DOOR_H));
            for (int i = 0; i < wz.Length; i++)
                WindowZ(room, "Window_" + i, HW + T * 0.5f, wz[i], 1.6f, 2.35f, 3.85f, T, mFrame, mGlass, mWhite);

            // painted band along the lower walls, and a dark skirting
            float bh = 1.1f;
            Cube("Band_L", new Vector3(-HW + 0.006f, bh * 0.5f, 0f), new Vector3(0.012f, bh, D), mBand, room, false);
            Cube("Band_R", new Vector3(HW - 0.006f, bh * 0.5f, 0f), new Vector3(0.012f, bh, D), mBand, room, false);
            Cube("Band_B", new Vector3(0f, bh * 0.5f, -HD + 0.006f), new Vector3(W, bh, 0.012f), mBand, room, false);
            float segW = (W - DOOR_W) * 0.5f;
            Cube("Band_FL", new Vector3(-(DOOR_W * 0.5f + segW * 0.5f), bh * 0.5f, HD - 0.006f), new Vector3(segW, bh, 0.012f), mBand, room, false);
            Cube("Band_FR", new Vector3(DOOR_W * 0.5f + segW * 0.5f, bh * 0.5f, HD - 0.006f), new Vector3(segW, bh, 0.012f), mBand, room, false);
            Cube("BandTrim_L", new Vector3(-HW + 0.012f, bh, 0f), new Vector3(0.024f, 0.04f, D), mWhite, room, false);
            Cube("BandTrim_R", new Vector3(HW - 0.012f, bh, 0f), new Vector3(0.024f, 0.04f, D), mWhite, room, false);
            Cube("BandTrim_B", new Vector3(0f, bh, -HD + 0.012f), new Vector3(W, 0.04f, 0.024f), mWhite, room, false);

            // the door we came in by (back wall), closed
            var back = Group("EntranceDoor", room);
            back.localPosition = new Vector3(0f, 0f, -HD);
            Cube("Frame", new Vector3(0f, 1.18f, 0.025f), new Vector3(1.32f, 2.36f, 0.05f), mDark, back, false);
            Cube("Slab", new Vector3(0f, 1.13f, 0.055f), new Vector3(1.12f, 2.24f, 0.04f), mSteel, back, false);
            Cube("Bar", new Vector3(0f, 1.05f, 0.09f), new Vector3(0.9f, 0.06f, 0.05f), mDark, back, false);
            Cube("SignPlate", new Vector3(0f, 2.55f, 0.02f), new Vector3(0.8f, 0.2f, 0.02f), mWhite, back, false);
            var st = Text("Sign", back, new Vector3(0f, 2.55f, 0.032f), new Vector2(0.74f, 0.15f), "STAIRS  DOWN", InkDark, TextAlignmentOptions.Center, true);
            st.rectTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            // exit sign above the loading doors
            Cube("ExitSign", new Vector3(0f, DOOR_H + 0.28f, HD - 0.02f), new Vector3(0.7f, 0.22f, 0.04f), mExit, room, false);
            Text("ExitText", room, new Vector3(0f, DOOR_H + 0.28f, HD - 0.045f), new Vector2(0.62f, 0.16f), "EXIT", Color.white, TextAlignmentOptions.Center, true);
        }

        // ============================================================ scale bays + crates

        static void BuildBays(Transform p, out Transform[] slots, out Renderer[] leds, out Transform[] padTops, out TMP_Text[] readouts, out Renderer next)
        {
            var g = Group("ScaleBays", p);
            slots = new Transform[BAYS];
            leds = new Renderer[BAYS];
            padTops = new Transform[BAYS];
            readouts = new TMP_Text[BAYS];
            for (int i = 0; i < BAYS; i++)
            {
                float x = BayX(i);
                var bay = Group("Bay_" + (i + 1), g);
                bay.localPosition = new Vector3(x, 0f, ROW_Z);
                Cube("Base", new Vector3(0f, 0.025f, 0f), new Vector3(0.92f, 0.05f, 0.92f), mDark, bay);
                padTops[i] = Cube("Plate", new Vector3(0f, 0.065f, 0f), new Vector3(0.84f, 0.03f, 0.84f), mPad, bay, false).transform;
                leds[i] = Cube("Led", new Vector3(-0.1f, 0.06f, -0.475f), new Vector3(0.56f, 0.025f, 0.03f), mLed, bay, false).GetComponent<Renderer>();
                Cube("Readout_Box", new Vector3(0.3f, 0.075f, -0.475f), new Vector3(0.22f, 0.11f, 0.04f), mDark, bay, false);
                Cube("Readout_Glass", new Vector3(0.3f, 0.078f, -0.496f), new Vector3(0.19f, 0.08f, 0.004f), mScreen, bay, false);
                var ro = Text("Readout", bay, new Vector3(0.3f, 0.078f, -0.5f), new Vector2(0.18f, 0.07f), "--.-",
                              new Color(0.45f, 1f, 0.7f), TextAlignmentOptions.Center, true);
                readouts[i] = ro;

                var s = new GameObject("Slot").transform;
                s.SetParent(bay, false);
                s.localPosition = new Vector3(0f, 0.08f, 0f);
                slots[i] = s;
            }
            next = Cube("NextBayGlow", new Vector3(BayX(0), 0.003f, ROW_Z), new Vector3(1.0f, 0.006f, 1.0f), mNext, g, false).GetComponent<Renderer>();
            next.shadowCastingMode = ShadowCastingMode.Off;
        }

        static List<SortItem> BuildCrates(Transform p, Transform[] slots)
        {
            var parent = Group("Crates", p);
            var list = new List<SortItem>();
            var rng = new System.Random(20261021);
            for (int i = 0; i < slots.Length; i++)
            {
                float w = 0.52f + (float)rng.NextDouble() * 0.22f;
                float h = 0.48f + (float)rng.NextDouble() * 0.26f;
                float d = 0.50f + (float)rng.NextDouble() * 0.2f;

                var c = new GameObject("Crate_" + (i + 1));
                c.transform.SetParent(parent, false);
                c.transform.position = slots[i].position;
                c.transform.rotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 6.0 - 3.0), 0f);

                var body = Cube("Body", new Vector3(0f, h * 0.5f, 0f), new Vector3(w - 0.03f, h - 0.03f, d - 0.03f), mCrate, c.transform, false);
                float b = 0.055f;
                // vertical corner posts
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        Cube("Post", new Vector3(sx * (w * 0.5f - b * 0.5f), h * 0.5f, sz * (d * 0.5f - b * 0.5f)), new Vector3(b, h, b), mCrateFrame, c.transform, false);
                // top and bottom rims
                foreach (float y in new[] { b * 0.5f, h - b * 0.5f })
                {
                    Cube("RimX", new Vector3(0f, y, -(d * 0.5f - b * 0.5f)), new Vector3(w, b, b), mCrateFrame, c.transform, false);
                    Cube("RimX", new Vector3(0f, y, d * 0.5f - b * 0.5f), new Vector3(w, b, b), mCrateFrame, c.transform, false);
                    Cube("RimZ", new Vector3(-(w * 0.5f - b * 0.5f), y, 0f), new Vector3(b, b, d), mCrateFrame, c.transform, false);
                    Cube("RimZ", new Vector3(w * 0.5f - b * 0.5f, y, 0f), new Vector3(b, b, d), mCrateFrame, c.transform, false);
                }
                // diagonal brace on the back face
                float diag = Mathf.Sqrt(w * w + h * h) - 0.12f;
                var brace = Cube("Brace", new Vector3(0f, h * 0.5f, d * 0.5f - 0.01f), new Vector3(diag, 0.05f, 0.03f), mCrateFrame, c.transform, false);
                brace.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(h, w) * Mathf.Rad2Deg);

                // paper label on the front, with the weight once it's known
                float ly = Mathf.Min(h * 0.58f, h - 0.16f);
                var tag = Cube("Label", new Vector3(0f, ly, -d * 0.5f - 0.004f), new Vector3(0.3f, 0.19f, 0.006f), mPaper, c.transform, false);
                var num = Text("Weight", c.transform, new Vector3(0f, ly - 0.01f, -d * 0.5f - 0.009f), new Vector2(0.27f, 0.13f),
                               PREVIEW[i] + " kg", InkDark, TextAlignmentOptions.Center, true);
                Text("Stencil", c.transform, new Vector3(0f, ly + 0.075f, -d * 0.5f - 0.009f), new Vector2(0.26f, 0.03f),
                     "WEIGHT", new Color(0.35f, 0.35f, 0.38f), TextAlignmentOptions.Center, true);
                Text("Fragile", c.transform, new Vector3(0f, h - 0.1f, -d * 0.5f - 0.004f), new Vector2(w - 0.16f, 0.05f),
                     "FRAGILE", new Color(0.62f, 0.16f, 0.12f), TextAlignmentOptions.Center, true);

                var bc = c.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, h * 0.5f, 0f);
                bc.size = new Vector3(w, h, d);

                var item = c.AddComponent<SortItem>();
                item.value = PREVIEW[i];
                item.label = num;
                item.rend = body.GetComponent<Renderer>();
                item.tagRenderer = tag.GetComponent<Renderer>();
                item.tintModel = true;
                list.Add(item);
            }
            return list;
        }

        // ============================================================ crane

        static void BuildCrane(Transform p, out Transform trolley, out Transform cable, out Transform hook)
        {
            var g = Group("Crane", p);
            float railY = 3.98f;
            Cube("Rail", new Vector3(0f, railY, ROW_Z), new Vector3(8.6f, 0.16f, 0.14f), mYellow, g, false);
            Cube("RailFlange", new Vector3(0f, railY - 0.09f, ROW_Z), new Vector3(8.6f, 0.02f, 0.26f), mDark, g, false);
            foreach (float x in new[] { -3.9f, 0f, 3.9f })
                Cube("Hanger", new Vector3(x, (railY + H) * 0.5f, ROW_Z), new Vector3(0.06f, H - railY, 0.06f), mDark, g, false);

            trolley = Group("Trolley", g);
            trolley.localPosition = new Vector3(0f, railY - 0.2f, ROW_Z);
            Cube("Body", Vector3.zero, new Vector3(0.42f, 0.2f, 0.34f), mYellow, trolley, false);
            Cube("Motor", new Vector3(0.12f, 0.02f, 0.2f), new Vector3(0.16f, 0.14f, 0.1f), mDark, trolley, false);
            foreach (float x in new[] { -0.14f, 0.14f })
                CylZ("Wheel", new Vector3(x, 0.14f, 0f), 0.09f, 0.3f, mDark, trolley);
            Text("Brand", trolley, new Vector3(0f, 0f, -0.172f), new Vector2(0.36f, 0.08f), "2 T", InkDark, TextAlignmentOptions.Center, true);

            cable = Group("Cable", trolley);
            cable.localPosition = new Vector3(0f, -0.1f, 0f);
            var strand = CylY("Strand", new Vector3(0f, -0.5f, 0f), 0.02f, 1f, mDark, cable);
            strand.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.On;
            cable.localScale = new Vector3(1f, 0.35f, 1f);

            hook = Group("Hook", trolley);
            hook.localPosition = new Vector3(0f, -0.45f, 0f);
            Cube("Block", new Vector3(0f, 0.05f, 0f), new Vector3(0.12f, 0.1f, 0.08f), mYellow, hook, false);
            Cube("Bar", new Vector3(0f, -0.02f, 0f), new Vector3(0.3f, 0.03f, 0.05f), mDark, hook, false);
        }

        // ============================================================ status board + control desk

        static TMP_Text BuildBoard(Transform p)
        {
            var g = Group("StatusBoard", p);
            g.localPosition = new Vector3(0f, 3.62f, HD);
            Cube("Bezel", new Vector3(0f, 0f, -0.04f), new Vector3(2.6f, 0.9f, 0.08f), mDark, g, false);
            Cube("Screen", new Vector3(0f, 0f, -0.082f), new Vector3(2.48f, 0.78f, 0.004f), mScreen, g, false);
            return Text("BoardText", g, new Vector3(0f, 0f, -0.086f), new Vector2(2.36f, 0.7f),
                        "<b>STOCK SCALE</b>  -  SELECTION SORT", new Color(0.75f, 0.95f, 1f), TextAlignmentOptions.Center, false);
        }

        static Terminal BuildConsole(Transform p, out PushButton scan, out PushButton hint)
        {
            // stands against the left wall and faces into the room (+X)
            var g = Group("ScaleControl", p);
            g.localPosition = new Vector3(-HW, 0f, -1.35f);
            g.localRotation = Quaternion.Euler(0f, -90f, 0f);     // local -Z faces +X (into the room)

            Cube("Desk", new Vector3(0f, 0.47f, -0.3f), new Vector3(1.2f, 0.94f, 0.56f), mWhite, g);
            Cube("DeskTop", new Vector3(0f, 0.955f, -0.31f), new Vector3(1.26f, 0.03f, 0.62f), mDark, g, false);
            Cube("Kick", new Vector3(0f, 0.05f, -0.585f), new Vector3(1.2f, 0.1f, 0.01f), mDark, g, false);

            Cube("Bezel", new Vector3(0f, 1.62f, -0.035f), new Vector3(1.06f, 0.74f, 0.07f), mDark, g);
            Cube("Screen", new Vector3(0f, 1.655f, -0.0715f), new Vector3(0.94f, 0.58f, 0.004f), mScreen, g, false);
            var txt = Text("ScreenText", g, new Vector3(0f, 1.655f, -0.0745f), new Vector2(0.86f, 0.53f),
                           "<b>SCALE CONTROL</b>   FLOOR 2", new Color(0.6f, 1f, 0.8f), TextAlignmentOptions.TopLeft, false);
            var term = g.gameObject.AddComponent<Terminal>();
            term.screen = txt;
            term.prompt = "Scale control  ·  ask for a hint";
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.62f, -0.06f);
            bc.size = new Vector3(1.08f, 0.76f, 0.14f);

            scan = Button(g, "ScanButton", new Vector3(-0.26f, 0.97f, -0.36f), mGreen, "SCAN ALL", "scan", "Weigh every crate that's left  (Q)");
            hint = Button(g, "HintButton", new Vector3(0.28f, 0.97f, -0.36f), mRed, "HINT", "hint", "Ask for a hint  (H)");
            return term;
        }

        static PushButton Button(Transform g, string name, Vector3 pos, Material capMat, string label, string id, string prompt)
        {
            var b = Group(name, g);
            b.localPosition = pos;
            CylY("Collar", new Vector3(0f, 0.012f, 0f), 0.2f, 0.024f, mDark, b);
            var cap = CylY("Cap", new Vector3(0f, 0.035f, 0f), 0.15f, 0.04f, capMat, b).transform;
            var t = Text("Label", b, new Vector3(0f, 0.002f, -0.14f), new Vector2(0.3f, 0.05f), label, InkDark, TextAlignmentOptions.Center, true);
            t.rectTransform.localPosition = new Vector3(0f, 0.001f, -0.15f);
            t.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var bc = b.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.05f, 0f);
            bc.size = new Vector3(0.24f, 0.12f, 0.24f);
            var pb = b.gameObject.AddComponent<PushButton>();
            pb.id = id;
            pb.label = prompt;
            pb.cap = cap;
            return pb;
        }

        // ============================================================ floor markings

        static void BuildFloorMarkings(Transform p)
        {
            var g = Group("FloorMarkings", p);
            float x0 = BayX(0) - 0.65f, x1 = BayX(BAYS - 1) + 0.65f;
            float z0 = ROW_Z - 0.62f, z1 = ROW_Z + 0.62f;
            float y = 0.002f, t = 0.07f;
            Cube("Line_Front", new Vector3((x0 + x1) * 0.5f, y, z0), new Vector3(x1 - x0, 0.004f, t), mYellow, g, false);
            Cube("Line_Back", new Vector3((x0 + x1) * 0.5f, y, z1), new Vector3(x1 - x0, 0.004f, t), mYellow, g, false);
            Cube("Line_L", new Vector3(x0, y, ROW_Z), new Vector3(t, 0.004f, z1 - z0), mYellow, g, false);
            Cube("Line_R", new Vector3(x1, y, ROW_Z), new Vector3(t, 0.004f, z1 - z0), mYellow, g, false);

            for (int i = 0; i < BAYS; i++)
                FloorText("BayNumber_" + (i + 1), g, new Vector3(BayX(i), 0.004f, ROW_Z - 0.86f), new Vector2(0.5f, 0.3f), (i + 1).ToString(), new Color(0.15f, 0.15f, 0.17f));
            FloorText("Lightest", g, new Vector3(BayX(0), 0.004f, ROW_Z - 1.28f), new Vector2(1.4f, 0.2f), "LIGHTEST", new Color(0.15f, 0.15f, 0.17f));
            FloorText("Heaviest", g, new Vector3(BayX(BAYS - 1), 0.004f, ROW_Z - 1.28f), new Vector2(1.4f, 0.2f), "HEAVIEST", new Color(0.15f, 0.15f, 0.17f));
            Cube("Arrow", new Vector3(0f, y, ROW_Z - 1.28f), new Vector3(4.2f, 0.004f, 0.04f), mYellow, g, false);
            var a1 = Cube("ArrowHead_A", new Vector3(2.04f, y, ROW_Z - 1.25f), new Vector3(0.22f, 0.004f, 0.04f), mYellow, g, false);
            a1.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);
            var a2 = Cube("ArrowHead_B", new Vector3(2.04f, y, ROW_Z - 1.31f), new Vector3(0.22f, 0.004f, 0.04f), mYellow, g, false);
            a2.transform.localRotation = Quaternion.Euler(0f, -30f, 0f);

            // walkway to the loading doors
            Cube("Walk_L", new Vector3(-1.2f, y, 3.1f), new Vector3(t, 0.004f, 3.8f), mYellow, g, false);
            Cube("Walk_R", new Vector3(1.2f, y, 3.1f), new Vector3(t, 0.004f, 3.8f), mYellow, g, false);

            // rules poster on the left wall, beside the desk
            var poster = Group("RulesPoster", p);
            poster.localPosition = new Vector3(-HW + 0.01f, 0f, 0.7f);
            poster.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Cube("Board", new Vector3(0f, 1.75f, -0.01f), new Vector3(1.3f, 0.95f, 0.02f), mWhite, poster, false);
            Cube("Stripe", new Vector3(0f, 2.16f, -0.022f), new Vector3(1.3f, 0.12f, 0.004f), mYellow, poster, false);
            Text("Title", poster, new Vector3(0f, 2.16f, -0.026f), new Vector2(1.2f, 0.09f), "HOW THIS ROOM WORKS", InkDark, TextAlignmentOptions.Center, true);
            Text("Body", poster, new Vector3(0f, 1.68f, -0.026f), new Vector2(1.16f, 0.72f),
                 "1.  Weigh every crate that is still unsorted.\n2.  Find the lightest one.\n3.  The crane swaps it into the next bay.\n4.  Repeat with the crates that are left.\n\n<i>The scale forgets after every pass.</i>",
                 InkDark, TextAlignmentOptions.TopLeft, false);
        }

        // ============================================================ props

        static void BuildProps(Transform p)
        {
            var g = Group("Props", p);
            var rng = new System.Random(42);

            // pallet racks: one along the left wall at the back, one along the back wall, one along the right wall at the front
            Rack(g, "Rack_LeftBack", new Vector3(-HW + 0.45f, 0f, -3.2f), 90f, 2.6f, rng);
            Rack(g, "Rack_Back", new Vector3(3.0f, 0f, -HD + 0.45f), 0f, 3.0f, rng);
            Rack(g, "Rack_RightFront", new Vector3(HW - 0.45f, 0f, 3.4f), -90f, 2.0f, rng);

            // pallets with stacked boxes
            Pallet(g, "Pallet_A", new Vector3(-3.4f, 0f, 3.5f), 8f, rng);
            Pallet(g, "Pallet_B", new Vector3(-2.2f, 0f, 3.9f), -4f, rng);
            Pallet(g, "Pallet_C", new Vector3(3.5f, 0f, -2.1f), 15f, rng);

            // drums
            Drum(g, new Vector3(-4.4f, 0f, 2.0f), mDrumBlue);
            Drum(g, new Vector3(-4.35f, 0f, 2.65f), mDrumRed);
            Drum(g, new Vector3(-3.8f, 0f, 2.3f), mDrumBlue);

            // fire extinguisher by the doors
            var fe = Group("Extinguisher", g);
            fe.localPosition = new Vector3(1.6f, 0f, HD - 0.12f);
            CylY("Tank", new Vector3(0f, 0.95f, 0f), 0.16f, 0.5f, mDrumRed, fe);
            Sphere("Top", new Vector3(0f, 1.2f, 0f), 0.16f, mDrumRed, fe);
            Cube("Valve", new Vector3(0f, 1.3f, 0f), new Vector3(0.05f, 0.08f, 0.05f), mDark, fe, false);
            Cube("Sign", new Vector3(0f, 1.7f, 0.1f), new Vector3(0.24f, 0.24f, 0.01f), mDrumRed, fe, false);
        }

        static void Rack(Transform p, string name, Vector3 pos, float yaw, float length, System.Random rng)
        {
            var r = Group(name, p);
            r.localPosition = pos;
            r.localRotation = Quaternion.Euler(0f, yaw, 0f);
            float depth = 0.8f, hgt = 2.4f;
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    Cube("Upright", new Vector3(sx * length * 0.5f, hgt * 0.5f, sz * depth * 0.5f), new Vector3(0.07f, hgt, 0.07f), mBlue, r, false);
            float[] levels = { 0.12f, 0.92f, 1.72f };
            foreach (float y in levels)
            {
                Cube("Beam_F", new Vector3(0f, y, -depth * 0.5f), new Vector3(length, 0.1f, 0.05f), mYellow, r, false);
                Cube("Beam_B", new Vector3(0f, y, depth * 0.5f), new Vector3(length, 0.1f, 0.05f), mYellow, r, false);
                Cube("Deck", new Vector3(0f, y + 0.06f, 0f), new Vector3(length, 0.02f, depth), mDark, r, false);
                float x = -length * 0.5f + 0.1f;
                while (x < length * 0.5f - 0.35f)
                {
                    float bw = 0.3f + (float)rng.NextDouble() * 0.35f;
                    float bh = 0.25f + (float)rng.NextDouble() * 0.4f;
                    float bd = 0.4f + (float)rng.NextDouble() * 0.3f;
                    if (x + bw > length * 0.5f - 0.05f) break;
                    if (rng.NextDouble() > 0.15)
                    {
                        var box = Cube("Box", new Vector3(x + bw * 0.5f, y + 0.07f + bh * 0.5f, (float)(rng.NextDouble() * 0.1 - 0.05)), new Vector3(bw, bh, bd), mCardboard, r, false);
                        box.transform.localRotation = Quaternion.Euler(0f, (float)(rng.NextDouble() * 8.0 - 4.0), 0f);
                        Cube("Tape", new Vector3(x + bw * 0.5f, y + 0.07f + bh + 0.002f, box.transform.localPosition.z), new Vector3(0.06f, 0.004f, bd), mPaper, r, false);
                    }
                    x += bw + 0.04f;
                }
            }
            var col = r.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, hgt * 0.5f, 0f);
            col.size = new Vector3(length + 0.1f, hgt, depth + 0.1f);
        }

        static void Pallet(Transform p, string name, Vector3 pos, float yaw, System.Random rng)
        {
            var g = Group(name, p);
            g.localPosition = pos;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            for (int i = 0; i < 3; i++) Cube("Runner", new Vector3(-0.5f + i * 0.5f, 0.05f, 0f), new Vector3(0.1f, 0.1f, 1.0f), mPallet, g, false);
            for (int i = 0; i < 6; i++) Cube("Slat", new Vector3(0f, 0.115f, -0.45f + i * 0.18f), new Vector3(1.2f, 0.025f, 0.12f), mPallet, g, false);
            float y = 0.13f;
            int layers = 2 + rng.Next(2);
            for (int l = 0; l < layers; l++)
            {
                float bh = 0.32f + (float)rng.NextDouble() * 0.1f;
                for (int bx = 0; bx < 2; bx++)
                    for (int bz = 0; bz < 2; bz++)
                    {
                        if (l == layers - 1 && rng.NextDouble() < 0.3) continue;
                        Cube("Box", new Vector3(-0.29f + bx * 0.58f, y + bh * 0.5f, -0.24f + bz * 0.48f), new Vector3(0.54f, bh, 0.44f), mCardboard, g, false);
                    }
                y += bh + 0.005f;
            }
            Cube("Wrap", new Vector3(0f, (0.13f + y) * 0.5f, 0f), new Vector3(1.17f, y - 0.13f, 0.97f), mGlass, g, false).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var col = g.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, y * 0.5f, 0f);
            col.size = new Vector3(1.2f, y, 1.0f);
        }

        static void Drum(Transform p, Vector3 pos, Material m)
        {
            var d = CylY("Drum", pos + new Vector3(0f, 0.44f, 0f), 0.58f, 0.88f, m, p, true);
            CylY("Rib", pos + new Vector3(0f, 0.3f, 0f), 0.6f, 0.03f, mDark, p);
            CylY("Rib", pos + new Vector3(0f, 0.6f, 0f), 0.6f, 0.03f, mDark, p);
            CylY("Lid", pos + new Vector3(0f, 0.885f, 0f), 0.56f, 0.01f, mDark, p);
        }

        // ============================================================ lights

        static void BuildLights(Transform p)
        {
            var g = Group("Lights", p);
            int k = 0;
            foreach (float x in new[] { -2.4f, 2.4f })
                foreach (float z in new[] { -2.2f, 2.6f })
                {
                    var l = Group("Pendant_" + k, g);
                    l.localPosition = new Vector3(x, H, z);
                    CylY("Cord", new Vector3(0f, -0.35f, 0f), 0.015f, 0.7f, mDark, l);
                    CylY("Shade", new Vector3(0f, -0.78f, 0f), 0.56f, 0.18f, mShade, l);
                    CylY("ShadeTop", new Vector3(0f, -0.7f, 0f), 0.3f, 0.06f, mShade, l);
                    Sphere("Bulb", new Vector3(0f, -0.86f, 0f), 0.16f, mBulb, l);
                    PointLight("Light", l, new Vector3(x, H - 1.05f, z), new Color(1f, 0.96f, 0.9f), 2.4f, 9f, k == 1);
                    k++;
                }
            PointLight("Fill_Front", g, new Vector3(0f, 2.6f, -3.6f), new Color(0.95f, 0.97f, 1f), 0.9f, 7f, false);
            PointLight("Fill_Doors", g, new Vector3(0f, 2.6f, 3.6f), new Color(0.95f, 0.97f, 1f), 0.8f, 6f, false);
        }
    }
}
