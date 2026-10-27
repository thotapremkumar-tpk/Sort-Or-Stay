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
    /// Builds Floor 4, "The Control Room" (merge sort): a bright operations room with a two-tier merge console
    /// (rack row above, merge row in front), eight numbered data blocks, a display wall, server racks, operator
    /// desks and sliding steel doors. Menu: Sort or Stay > Build Level 4 (Control Room).
    /// Re-running it rebuilds and overwrites the scene.
    public static class BuildLevel4
    {
        const string SCENE = ROOT + "Scenes/Level4_Merge.unity";

        const float W = 11f, D = 10f, H = 4.0f, T = 0.2f;
        const float HW = W * 0.5f, HD = D * 0.5f;
        const float DOOR_W = 2.0f, DOOR_H = 2.6f;
        const int BLOCKS = 8;

        // merge console: the merge row is the lower front surface, the rack row stands on a raised back step
        const float CONSOLE_LEN = 5.6f, CONSOLE_Z0 = 0.25f, CONSOLE_Z1 = 1.05f;
        const float DESK_Y = 0.86f, RISER_Y = 1.0f, RISER_Z0 = 0.64f;
        const float MERGE_Z = 0.44f, RACK_Z = 0.84f;
        const float PITCH = 0.36f, GAP = 0.22f;
        const float BW = 0.28f, BH = 0.24f, BD = 0.2f;     // data block size

        static readonly Vector3 SPAWN = new Vector3(0f, 0f, -2.6f);
        static readonly int[] PREVIEW = { 45, 12, 77, 3, 58, 26, 90, 34 };

        static Material mFloor, mWall, mBand, mCeil, mConsole, mConsoleTop, mDark, mSteel, mTread, mBlock, mBlockStripe, mLabel,
                        mStrip, mScreen, mRack, mLedGreen, mLedBlue, mLedAmber, mGlass, mGlassDark, mFrame, mWhite, mExit, mLamp,
                        mPanel, mHousing, mRed, mBlue, mPlant, mPot;

        [MenuItem("Sort or Stay/Build Level 4 (Control Room)")]
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

            var parts = BuildFloor();
            var root = parts.root;
            var door = parts.exitDoor;
            var puzzle = parts.puzzle;

            var player = BuildPlayer(SPAWN, 0f);

            var sys = parts.systems;
            BuildUI(sys, parts.title, 4);

            var lm = sys.gameObject.AddComponent<LevelManager>();
            lm.levelNumber = 4;
            lm.nextScene = "Level5_Quick";
            lm.nextFloorLabel = "Floor 5  ·  the Final Vault  (Quick Sort)";
            lm.door = door;
            lm.player = player;

            var overview = new GameObject("IntroCamera").transform;
            overview.SetParent(sys, false);
            overview.position = new Vector3(4.3f, 3.1f, -3.7f);
            overview.rotation = Quaternion.LookRotation(new Vector3(-0.3f, 1.1f, 0.9f) - overview.position);
            var intro = sys.gameObject.AddComponent<LevelIntro>();
            intro.player = player;
            intro.puzzle = puzzle;
            intro.overview = overview;
            intro.objective = parts.objective;
            intro.lines = parts.lines;

            BuildStairwell(root, HD + T, H, DOOR_W, lm, "FLOOR 5", mTread, mWall, mCeil, mTread, mExit);
            BrightPostFx(sys, "Level4_PostFX", 0.15f);

            BrightEnvironment(new Color(0.80f, 0.86f, 0.94f), new Color(0.66f, 0.68f, 0.70f), new Color(0.40f, 0.41f, 0.43f));
            var sun = Sun(root, new Vector3(34f, -110f, 0f), new Color(1f, 0.96f, 0.88f), 1.55f);
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE, 3);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
            Debug.Log("Sort or Stay: Level 4 'The Control Room' built and saved to " + SCENE + ". Press Play.");
        }

        /// The room, its doors, the console, the blocks and the puzzle, built at the origin into the open scene.
        /// Used by the standalone scene above and by the five-floor building (Kit.BuildingMode).
        public static FloorParts BuildFloor()
        {
            MakeMaterials();

            var root = new GameObject("Level4_ControlRoom").transform;
            BuildRoom(root);
            var door = DoubleDoor(root, HD, T, DOOR_W, DOOR_H, mSteel, mDark, mLamp, mGlass, false);
            door.lockedMessage = "The doors stay shut.  The display wall reads:  ONE SORTED RUN.";

            BuildConsole(root, out Transform rackRow, out Transform mergeRow, out Renderer[] strips, out Renderer mergeStrip,
                         out PushButton splitBtn, out PushButton hintBtn);
            var blocks = BuildBlocks(root, rackRow);
            var board = BuildDisplayWall(root);
            var terminal = BuildKiosk(root);
            BuildPoster(root);
            BuildRacks(root);
            BuildOperatorDesks(root);
            BuildLights(root);

            var sys = FloorSystems(root, 4);
            var puzzle = sys.gameObject.AddComponent<MergePuzzle>();
            puzzle.items = blocks;
            puzzle.slots = new Transform[0];
            puzzle.rackRow = rackRow;
            puzzle.mergeRow = mergeRow;
            puzzle.pitch = PITCH;
            puzzle.handReach = 1.1f;          // the rack row stands ~0.8 m behind the console's front edge
            puzzle.runGap = GAP;
            puzzle.runStrips = strips;
            puzzle.mergeStrip = mergeStrip;
            puzzle.board = board;
            puzzle.terminal = terminal;
            puzzle.splitButton = splitBtn.cap;
            puzzle.door = door;
            terminal.puzzle = puzzle;
            splitBtn.puzzle = puzzle;
            hintBtn.puzzle = puzzle;

            return new FloorParts
            {
                root = root, systems = sys, puzzle = puzzle, exitDoor = door, number = 4,
                title = "FLOOR 4  ·  THE CONTROL ROOM", roomName = "The Control Room",
                objective = "<b>Merge sort</b> the data blocks, smallest first.\n" +
                            "Press <b>SPLIT</b> until every block stands alone.  Then merge two runs at a time:  always take the <b>smaller front block</b>.\n" +
                            "<size=20>Q split  ·  click a glowing front block, or press 1 (left) / 2 (right)  ·  H hint</size>",
                lines = new[]
                {
                    "Floor 4.  A control room, humming quietly.",
                    "Eight data blocks, scrambled.  One console to merge them.",
                    "Split them all apart, then merge them back, two runs at a time."
                },
                width = W, depth = D, height = H, spawn = SPAWN
            };
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mFloor = Pbr("L4_Floor", "Tiles107", new Color(0.95f, 0.96f, 0.98f), 4f, 0.45f, 0f);
            mWall = Pbr("L4_Wall", "PaintedPlaster017", new Color(0.95f, 0.96f, 0.98f), 3f, 0.12f, 0f);
            mBand = Flat("L4_Band", new Color(0.16f, 0.42f, 0.70f), 0.35f, 0f);
            mCeil = Flat("L4_Ceiling", new Color(0.96f, 0.96f, 0.97f), 0.1f, 0f);
            mConsole = Flat("L4_Console", new Color(0.92f, 0.93f, 0.95f), 0.45f, 0.1f);
            mConsoleTop = Pbr("L4_ConsoleTop", "MetalPlates017A", new Color(0.78f, 0.81f, 0.86f), 1.5f, 0.5f, 0.35f);
            mDark = Pbr("L4_DarkMetal", "Metal063", new Color(0.30f, 0.32f, 0.36f), 1f, 0.5f, 0.45f);
            mSteel = Pbr("L4_Steel", "MetalPlates017A", new Color(0.82f, 0.84f, 0.87f), 1f, 0.45f, 0.3f);
            mTread = Pbr("L4_Tread", "DiamondPlate009", new Color(0.74f, 0.75f, 0.77f), 2f, 0.4f, 0.4f);
            mBlock = Flat("L4_Block", new Color(0.24f, 0.26f, 0.30f), 0.55f, 0.35f);
            mBlockStripe = Flat("L4_BlockStripe", new Color(0.93f, 0.45f, 0.05f), 0.4f, 0f);
            mLabel = Flat("L4_Label", new Color(0.97f, 0.96f, 0.92f), 0.2f, 0f);
            mStrip = Emissive("L4_RunStrip", new Color(0.62f, 0.66f, 0.72f), new Color(0.62f, 0.66f, 0.72f), 0.3f);
            mScreen = Emissive("L4_Screen", new Color(0.02f, 0.05f, 0.08f), new Color(0.02f, 0.12f, 0.2f), 1f);
            mRack = Flat("L4_ServerRack", new Color(0.17f, 0.18f, 0.21f), 0.5f, 0.3f);
            mLedGreen = Emissive("L4_LedGreen", new Color(0.2f, 1f, 0.4f), new Color(0.2f, 1f, 0.4f), 3f);
            mLedBlue = Emissive("L4_LedBlue", new Color(0.25f, 0.6f, 1f), new Color(0.25f, 0.6f, 1f), 3f);
            mLedAmber = Emissive("L4_LedAmber", new Color(1f, 0.65f, 0.15f), new Color(1f, 0.65f, 0.15f), 3f);
            mGlass = Glass("L4_Glass", new Color(0.82f, 0.92f, 1f), 0.16f);
            mGlassDark = Glass("L4_RackGlass", new Color(0.2f, 0.25f, 0.3f), 0.35f);
            mFrame = Flat("L4_WindowFrame", new Color(0.24f, 0.26f, 0.29f), 0.5f, 0.4f);
            mWhite = Flat("L4_White", new Color(0.96f, 0.96f, 0.95f), 0.3f, 0f);
            mExit = Emissive("L4_Exit", new Color(0.05f, 0.5f, 0.2f), new Color(0.1f, 0.9f, 0.35f), 1.6f);
            mLamp = Emissive("L4_DoorLamp", new Color(1f, 0.12f, 0.08f), new Color(1f, 0.12f, 0.08f), 4f);
            mPanel = Emissive("L4_LedPanel", new Color(0.98f, 0.98f, 1f), new Color(0.95f, 0.97f, 1f), 1.6f);
            mHousing = Flat("L4_PanelHousing", new Color(0.9f, 0.9f, 0.92f), 0.3f, 0.2f);
            mRed = Emissive("L4_ButtonSplit", new Color(0.95f, 0.38f, 0.06f), new Color(1f, 0.42f, 0.05f), 0.9f);
            mBlue = Emissive("L4_ButtonHint", new Color(0.15f, 0.45f, 0.9f), new Color(0.2f, 0.5f, 1f), 0.8f);
            mPlant = Flat("L4_Plant", new Color(0.22f, 0.52f, 0.26f), 0.3f, 0f);
            mPot = Flat("L4_Pot", new Color(0.92f, 0.92f, 0.9f), 0.4f, 0f);
        }

        // ============================================================ room

        static void BuildRoom(Transform p)
        {
            var room = Group("Room", p);
            Cube("Floor", new Vector3(0f, -T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mFloor, room);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mCeil, room);

            WallAlongZ(room, "Wall_Left", -HW - T * 0.5f, -HD - T, HD + T, H, T, mWall);
            float[] wz = { -3.2f, -1.1f, 1.0f, 3.1f };
            var windows = new List<Hole>();
            foreach (float z in wz) windows.Add(new Hole(z, 1.6f, 2.25f, 3.6f));
            WallAlongZ(room, "Wall_Right", HW + T * 0.5f, -HD - T, HD + T, H, T, mWall, windows.ToArray());
            for (int i = 0; i < wz.Length; i++)
                WindowZ(room, "Window_" + i, HW + T * 0.5f, wz[i], 1.6f, 2.25f, 3.6f, T, mFrame, mGlass, mWhite);
            WallAlongX(room, "Wall_Back", -HD - T * 0.5f, -HW, HW, H, T, mWall);
            WallAlongX(room, "Wall_Front", HD + T * 0.5f, -HW, HW, H, T, mWall, FrontHoles(new Hole(0f, DOOR_W, 0f, DOOR_H)));

            // blue band along the lower walls, with a white trim
            float bh = 0.9f;
            Cube("Band_L", new Vector3(-HW + 0.006f, bh * 0.5f, 0f), new Vector3(0.012f, bh, D), mBand, room, false);
            Cube("Band_R", new Vector3(HW - 0.006f, bh * 0.5f, 0f), new Vector3(0.012f, bh, D), mBand, room, false);
            Cube("Band_B", new Vector3(0f, bh * 0.5f, -HD + 0.006f), new Vector3(W, bh, 0.012f), mBand, room, false);
            float segW = (W - DOOR_W) * 0.5f;
            Cube("Band_FL", new Vector3(-(DOOR_W * 0.5f + segW * 0.5f), bh * 0.5f, HD - 0.006f), new Vector3(segW, bh, 0.012f), mBand, room, false);
            FrontStrip(room, "Band_FR", DOOR_W * 0.5f, HW, bh * 0.5f, bh, HD - 0.006f, 0.012f, mBand);
            Cube("Trim_L", new Vector3(-HW + 0.012f, bh, 0f), new Vector3(0.024f, 0.04f, D), mWhite, room, false);
            Cube("Trim_R", new Vector3(HW - 0.012f, bh, 0f), new Vector3(0.024f, 0.04f, D), mWhite, room, false);
            Cube("Trim_B", new Vector3(0f, bh, -HD + 0.012f), new Vector3(W, 0.04f, 0.024f), mWhite, room, false);

            // the door we came in by (back wall), closed. In the building you come in from the stair tower instead.
            if (!BuildingMode)
            {
                var back = Group("EntranceDoor", room);
                back.localPosition = new Vector3(0f, 0f, -HD);
                Cube("Frame", new Vector3(0f, 1.18f, 0.025f), new Vector3(1.32f, 2.36f, 0.05f), mDark, back, false);
                Cube("Slab", new Vector3(0f, 1.13f, 0.055f), new Vector3(1.12f, 2.24f, 0.04f), mSteel, back, false);
                Cube("Bar", new Vector3(0f, 1.05f, 0.09f), new Vector3(0.9f, 0.06f, 0.05f), mDark, back, false);
                Cube("SignPlate", new Vector3(0f, 2.55f, 0.02f), new Vector3(0.8f, 0.2f, 0.02f), mWhite, back, false);
                var st = Text("Sign", back, new Vector3(0f, 2.55f, 0.032f), new Vector2(0.74f, 0.15f), "STAIRS  DOWN", InkDark, TextAlignmentOptions.Center, true);
                st.rectTransform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            }

            // exit sign above the doors
            Cube("ExitSign", new Vector3(0f, DOOR_H + 0.28f, HD - 0.02f), new Vector3(0.7f, 0.22f, 0.04f), mExit, room, false);
            Text("ExitText", room, new Vector3(0f, DOOR_H + 0.28f, HD - 0.045f), new Vector2(0.62f, 0.16f), "EXIT", Color.white, TextAlignmentOptions.Center, true);

            // a big room sign on the left wall
            var sign = Group("RoomSign", room);
            sign.localPosition = new Vector3(-HW + 0.01f, 2.95f, 2.4f);
            sign.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Cube("Plate", new Vector3(0f, 0f, -0.01f), new Vector3(2.4f, 0.42f, 0.02f), mBand, sign, false);
            Text("Text", sign, new Vector3(0f, 0f, -0.022f), new Vector2(2.2f, 0.3f), "CONTROL ROOM  ·  MERGE SORT", Color.white, TextAlignmentOptions.Center, true);

            // painted walkway lines from the entrance to the console
            Cube("Walk_L", new Vector3(-1.1f, 0.002f, -3.2f), new Vector3(0.06f, 0.004f, 3.2f), mBand, room, false);
            Cube("Walk_R", new Vector3(1.1f, 0.002f, -3.2f), new Vector3(0.06f, 0.004f, 3.2f), mBand, room, false);
        }

        // ============================================================ merge console

        static void BuildConsole(Transform p, out Transform rackRow, out Transform mergeRow, out Renderer[] strips, out Renderer mergeStrip,
                                 out PushButton splitBtn, out PushButton hintBtn)
        {
            var g = Group("MergeConsole", p);
            float zc = (CONSOLE_Z0 + CONSOLE_Z1) * 0.5f, dep = CONSOLE_Z1 - CONSOLE_Z0;

            // body up to the merge surface, then a raised step at the back for the rack row
            Cube("Body", new Vector3(0f, (DESK_Y - 0.04f) * 0.5f, zc), new Vector3(CONSOLE_LEN, DESK_Y - 0.04f, dep), mConsole, g);
            Cube("Top", new Vector3(0f, DESK_Y - 0.02f, zc), new Vector3(CONSOLE_LEN + 0.06f, 0.04f, dep + 0.06f), mConsoleTop, g, false);
            float rdep = CONSOLE_Z1 - RISER_Z0;
            Cube("Riser", new Vector3(0f, (DESK_Y + RISER_Y - 0.02f) * 0.5f, RISER_Z0 + rdep * 0.5f), new Vector3(CONSOLE_LEN, RISER_Y - DESK_Y - 0.02f, rdep), mConsole, g);
            Cube("RiserTop", new Vector3(0f, RISER_Y - 0.01f, RISER_Z0 + rdep * 0.5f), new Vector3(CONSOLE_LEN + 0.04f, 0.02f, rdep + 0.03f), mConsoleTop, g, false);
            Cube("Kick", new Vector3(0f, 0.05f, CONSOLE_Z0 - 0.005f), new Vector3(CONSOLE_LEN, 0.1f, 0.01f), mDark, g, false);
            Cube("Accent", new Vector3(0f, DESK_Y - 0.12f, CONSOLE_Z0 - 0.006f), new Vector3(CONSOLE_LEN, 0.05f, 0.012f), mBand, g, false);

            // labels on the two front faces
            Text("RackLabel", g, new Vector3(-CONSOLE_LEN * 0.5f + 0.75f, (DESK_Y + RISER_Y) * 0.5f, RISER_Z0 - 0.004f), new Vector2(1.3f, 0.08f),
                 "RACK  -  RUNS WAIT HERE", new Color(0.25f, 0.28f, 0.32f), TextAlignmentOptions.Left, true);
            Text("MergeLabel", g, new Vector3(-CONSOLE_LEN * 0.5f + 0.75f, DESK_Y - 0.24f, CONSOLE_Z0 - 0.004f), new Vector2(1.3f, 0.1f),
                 "MERGE ROW", new Color(0.25f, 0.28f, 0.32f), TextAlignmentOptions.Left, true);
            Text("Rule", g, new Vector3(0.4f, DESK_Y - 0.24f, CONSOLE_Z0 - 0.004f), new Vector2(3.6f, 0.09f),
                 "TAKE THE SMALLER OF THE TWO FRONT BLOCKS", new Color(0.25f, 0.28f, 0.32f), TextAlignmentOptions.Center, true);

            rackRow = Group("RackRow", g);
            rackRow.localPosition = new Vector3(0f, RISER_Y, RACK_Z);
            mergeRow = Group("MergeRow", g);
            mergeRow.localPosition = new Vector3(0f, DESK_Y, MERGE_Z);

            // one glowing strip per run (at most eight runs), and one under the merge span
            strips = new Renderer[BLOCKS];
            for (int k = 0; k < BLOCKS; k++)
            {
                var s = Cube("RunStrip_" + k, new Vector3(0f, 0.003f, 0f), new Vector3(0.3f, 0.006f, 0.27f), mStrip, rackRow, false);
                var r = s.GetComponent<Renderer>();
                r.shadowCastingMode = ShadowCastingMode.Off;
                strips[k] = r;
            }
            var ms = Cube("MergeStrip", new Vector3(0f, 0.003f, 0f), new Vector3(0.3f, 0.006f, 0.27f), mStrip, mergeRow, false);
            mergeStrip = ms.GetComponent<Renderer>();
            mergeStrip.shadowCastingMode = ShadowCastingMode.Off;
            ms.SetActive(false);

            // the two console buttons at the ends of the merge surface
            splitBtn = Button(g, "SplitButton", new Vector3(-CONSOLE_LEN * 0.5f + 0.3f, DESK_Y, MERGE_Z - 0.04f), mRed, "SPLIT  (Q)", "split", "Split every run in half  (Q)");
            hintBtn = Button(g, "HintButton", new Vector3(CONSOLE_LEN * 0.5f - 0.3f, DESK_Y, MERGE_Z - 0.04f), mBlue, "HINT  (H)", "hint", "Ask for a hint  (H)");
        }

        static PushButton Button(Transform g, string name, Vector3 pos, Material capMat, string label, string id, string prompt)
        {
            var b = Group(name, g);
            b.localPosition = pos;
            CylY("Collar", new Vector3(0f, 0.014f, 0f), 0.22f, 0.028f, mDark, b);
            var cap = CylY("Cap", new Vector3(0f, 0.04f, 0f), 0.16f, 0.045f, capMat, b).transform;
            var t = Text("Label", b, Vector3.zero, new Vector2(0.32f, 0.05f), label, InkDark, TextAlignmentOptions.Center, true);
            t.rectTransform.localPosition = new Vector3(0f, 0.002f, -0.15f);
            t.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var bc = b.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.05f, 0f);
            bc.size = new Vector3(0.26f, 0.12f, 0.26f);
            var pb = b.gameObject.AddComponent<PushButton>();
            pb.id = id;
            pb.label = prompt;
            pb.cap = cap;
            return pb;
        }

        // ============================================================ data blocks

        static List<SortItem> BuildBlocks(Transform p, Transform rackRow)
        {
            var parent = Group("DataBlocks", p);
            var list = new List<SortItem>();
            for (int i = 0; i < BLOCKS; i++)
            {
                var b = new GameObject("Block_" + (i + 1));
                b.transform.SetParent(parent, false);
                float x = -(BLOCKS - 1) * 0.5f * (PITCH + GAP) + i * (PITCH + GAP);
                b.transform.position = rackRow.TransformPoint(new Vector3(x, 0f, 0f));

                var body = Cube("Body", new Vector3(0f, BH * 0.5f, 0f), new Vector3(BW, BH, BD), mBlock, b.transform, false);
                Cube("Stripe", new Vector3(0f, BH - 0.022f, -BD * 0.5f - 0.002f), new Vector3(BW, 0.018f, 0.004f), mBlockStripe, b.transform, false);
                Cube("Vent", new Vector3(0f, 0.028f, -BD * 0.5f - 0.002f), new Vector3(BW - 0.08f, 0.012f, 0.004f), mDark, b.transform, false);
                Cube("Handle", new Vector3(0f, BH + 0.012f, 0f), new Vector3(BW * 0.5f, 0.024f, 0.035f), mDark, b.transform, false);
                var plate = Cube("Label", new Vector3(0f, BH * 0.47f, -BD * 0.5f - 0.004f), new Vector3(BW - 0.05f, 0.13f, 0.006f), mLabel, b.transform, false);
                var num = Text("Number", b.transform, new Vector3(0f, BH * 0.47f, -BD * 0.5f - 0.0085f), new Vector2(BW - 0.08f, 0.11f),
                               PREVIEW[i].ToString(), InkDark, TextAlignmentOptions.Center, true);

                var bc = b.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, BH * 0.5f, 0f);
                bc.size = new Vector3(BW, BH + 0.02f, BD);

                var item = b.AddComponent<SortItem>();
                item.value = PREVIEW[i];
                item.label = num;
                item.rend = body.GetComponent<Renderer>();
                item.tagRenderer = plate.GetComponent<Renderer>();
                item.tintModel = true;
                list.Add(item);
            }
            return list;
        }

        // ============================================================ display wall + kiosk + poster

        static TMP_Text BuildDisplayWall(Transform p)
        {
            var g = Group("DisplayWall", p);
            g.localPosition = new Vector3(0f, 0f, 1.6f);
            foreach (float x in new[] { -1.9f, 1.9f })
            {
                Cube("Leg", new Vector3(x, 0.66f, 0.05f), new Vector3(0.08f, 1.32f, 0.08f), mDark, g);
                Cube("Foot", new Vector3(x, 0.02f, 0.05f), new Vector3(0.36f, 0.04f, 0.5f), mDark, g, false);
            }
            Cube("Bezel", new Vector3(0f, 1.98f, 0f), new Vector3(4.3f, 1.34f, 0.08f), mDark, g);
            Cube("Screen", new Vector3(0f, 1.98f, -0.042f), new Vector3(4.16f, 1.2f, 0.004f), mScreen, g, false);
            Cube("TopLight", new Vector3(0f, 2.66f, -0.01f), new Vector3(4.3f, 0.03f, 0.06f), mBand, g, false);
            return Text("BoardText", g, new Vector3(0f, 1.98f, -0.046f), new Vector2(3.95f, 1.08f),
                        "<b>MERGE CONSOLE  ·  MERGE SORT</b>", new Color(0.78f, 0.95f, 1f), TextAlignmentOptions.Center, false);
        }

        static Terminal BuildKiosk(Transform p)
        {
            var g = Group("MergeControlKiosk", p);
            g.localPosition = new Vector3(-3.75f, 0f, 0.55f);
            g.localRotation = Quaternion.Euler(0f, -28f, 0f);       // local -Z faces the middle of the room
            Cube("Foot", new Vector3(0f, 0.03f, 0f), new Vector3(0.55f, 0.06f, 0.45f), mDark, g);
            Cube("Post", new Vector3(0f, 0.55f, 0.03f), new Vector3(0.16f, 1.0f, 0.12f), mConsole, g);
            var head = Group("Head", g);
            head.localPosition = new Vector3(0f, 1.25f, 0f);
            head.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Cube("Bezel", Vector3.zero, new Vector3(0.74f, 0.52f, 0.06f), mDark, head);
            Cube("Screen", new Vector3(0f, 0.01f, -0.031f), new Vector3(0.68f, 0.44f, 0.004f), mScreen, head, false);
            var txt = Text("ScreenText", head, new Vector3(0f, 0.01f, -0.034f), new Vector2(0.64f, 0.4f), "<b>MERGE CONTROL</b>   FLOOR 4",
                           new Color(0.6f, 1f, 0.8f), TextAlignmentOptions.TopLeft, false);
            Text("Brand", g, new Vector3(0f, 0.85f, -0.032f), new Vector2(0.14f, 0.04f), "HINTS", Color.white, TextAlignmentOptions.Center, true);

            var term = g.gameObject.AddComponent<Terminal>();
            term.screen = txt;
            term.prompt = "Merge control  ·  ask for a hint";
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.78f, 0f);
            bc.size = new Vector3(0.74f, 1.56f, 0.45f);
            return term;
        }

        static void BuildPoster(Transform p)
        {
            var poster = Group("RulesPoster", p);
            poster.localPosition = new Vector3(-HW + 0.01f, 0f, -1.4f);
            poster.localRotation = Quaternion.Euler(0f, -90f, 0f);
            Cube("Board", new Vector3(0f, 1.7f, -0.01f), new Vector3(1.5f, 1.05f, 0.02f), mWhite, poster, false);
            Cube("Stripe", new Vector3(0f, 2.15f, -0.022f), new Vector3(1.5f, 0.13f, 0.004f), mBand, poster, false);
            Text("Title", poster, new Vector3(0f, 2.15f, -0.026f), new Vector2(1.4f, 0.09f), "HOW THE MERGE CONSOLE WORKS", Color.white, TextAlignmentOptions.Center, true);
            Text("Body", poster, new Vector3(0f, 1.62f, -0.026f), new Vector2(1.36f, 0.8f),
                 "1.  SPLIT halves every run, until each block stands alone.\n" +
                 "2.  Merge two neighbouring runs:  compare their two FRONT blocks and move the smaller one down into the merge row.\n" +
                 "3.  When one run is empty, the rest of the other follows.\n" +
                 "4.  Runs of 1 → 2 → 4 → 8.\n\n<i>One block on its own is always sorted.</i>",
                 InkDark, TextAlignmentOptions.TopLeft, false);
        }

        // ============================================================ server racks + operator desks

        static void BuildRacks(Transform p)
        {
            var g = Group("ServerRacks", p);
            var rng = new System.Random(4);
            for (int i = 0; i < 4; i++) ServerRack(g, new Vector3(-HW + 0.5f, 0f, 1.2f + i * 0.72f), -90f, rng);
            for (int i = 0; i < 3; i++) ServerRack(g, new Vector3(HW - 0.5f, 0f, 2.1f + i * 0.72f), 90f, rng);
        }

        /// A tall server cabinet whose front faces local -Z after 'yaw'.
        static void ServerRack(Transform p, Vector3 pos, float yaw, System.Random rng)
        {
            var r = Group("ServerRack", p);
            r.localPosition = pos;
            r.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Cube("Cabinet", new Vector3(0f, 1.0f, 0f), new Vector3(0.66f, 2.0f, 0.9f), mRack, r);
            Cube("Top", new Vector3(0f, 2.01f, 0f), new Vector3(0.68f, 0.02f, 0.92f), mDark, r, false);
            Material[] leds = { mLedGreen, mLedBlue, mLedAmber, mLedGreen };
            for (int u = 0; u < 8; u++)
            {
                float y = 0.28f + u * 0.21f;
                Cube("Unit", new Vector3(0f, y, -0.452f), new Vector3(0.56f, 0.16f, 0.006f), mDark, r, false);
                int n = 2 + rng.Next(3);
                for (int k = 0; k < n; k++)
                {
                    var led = Cube("Led", new Vector3(-0.22f + k * 0.045f, y + 0.04f, -0.457f), new Vector3(0.02f, 0.02f, 0.004f), leds[rng.Next(leds.Length)], r, false);
                    led.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            var glass = Cube("Door", new Vector3(0f, 1.0f, -0.462f), new Vector3(0.62f, 1.92f, 0.008f), mGlassDark, r, false);
            glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        static void BuildOperatorDesks(Transform p)
        {
            var g = Group("OperatorDesks", p);
            foreach (float sx in new[] { -1f, 1f })
            {
                var d = Group(sx < 0 ? "Desk_Left" : "Desk_Right", g);
                d.localPosition = new Vector3(sx * 3.3f, 0f, -3.9f);
                // the operator faces the back wall, where two wall screens show the network
                Cube("Top", new Vector3(0f, 0.74f, 0f), new Vector3(1.8f, 0.04f, 0.75f), mConsoleTop, d);
                Cube("Side_L", new Vector3(-0.87f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.7f), mConsole, d);
                Cube("Side_R", new Vector3(0.87f, 0.36f, 0f), new Vector3(0.05f, 0.72f, 0.7f), mConsole, d);
                Cube("Modesty", new Vector3(0f, 0.45f, -0.33f), new Vector3(1.7f, 0.5f, 0.03f), mConsole, d, false);
                foreach (float mx in new[] { -0.45f, 0.45f })
                {
                    Cube("MonitorStand", new Vector3(mx, 0.84f, -0.18f), new Vector3(0.05f, 0.18f, 0.05f), mDark, d, false);
                    var mon = Cube("Monitor", new Vector3(mx, 1.06f, -0.2f), new Vector3(0.62f, 0.38f, 0.03f), mDark, d, false);
                    mon.transform.localRotation = Quaternion.Euler(0f, mx < 0 ? 12f : -12f, 0f);
                    var scr = Cube("MonitorScreen", Vector3.zero, Vector3.one, mScreen, mon.transform, false);
                    scr.transform.localPosition = new Vector3(0f, 0f, 0.6f);
                    scr.transform.localScale = new Vector3(0.92f, 0.86f, 0.1f);
                }
                Cube("Keyboard", new Vector3(0f, 0.765f, 0.12f), new Vector3(0.45f, 0.012f, 0.14f), mDark, d, false);
                Prop(SPACE + "chair.fbx", "Chair", d, d.TransformPoint(new Vector3(0.1f, 0f, 0.7f)), 180f + sx * 15f, 0.95f);
            }

            // two big wall screens on the back wall, either side of the entrance
            foreach (float sx in new[] { -1f, 1f })
            {
                var s = Group(sx < 0 ? "WallScreen_L" : "WallScreen_R", g);
                s.localPosition = new Vector3(sx * 3.3f, 1.85f, -HD + 0.02f);
                s.localRotation = Quaternion.Euler(0f, 180f, 0f);     // readable from inside the room
                Cube("Bezel", new Vector3(0f, 0f, 0.03f), new Vector3(2.0f, 1.1f, 0.05f), mDark, s, false);
                Cube("Screen", new Vector3(0f, 0f, 0.0f), new Vector3(1.9f, 1.0f, 0.004f), mScreen, s, false);
                Text("Text", s, new Vector3(0f, 0f, -0.004f), new Vector2(1.8f, 0.9f),
                     sx < 0 ? "<b>NETWORK</b>\n\nNODES ONLINE   64 / 64\nLATENCY   3 ms\nSTATUS   <color=#5dff7a>OK</color>"
                            : "<b>STORAGE</b>\n\nBLOCKS   8\nORDER   <color=#ff6a5a>SCRAMBLED</color>\nACTION   MERGE SORT",
                     new Color(0.7f, 0.95f, 1f), TextAlignmentOptions.Center, false);
            }

            // potted plants by the windows
            foreach (float z in new[] { -2.1f, 0.0f })
            {
                var pl = Group("Plant", g);
                pl.localPosition = new Vector3(HW - 0.45f, 0f, z);
                CylY("Pot", new Vector3(0f, 0.22f, 0f), 0.4f, 0.44f, mPot, pl, true);
                Sphere("Leaves", new Vector3(0f, 0.75f, 0f), 0.7f, mPlant, pl);
                Sphere("Leaves2", new Vector3(0.12f, 1.05f, 0.05f), 0.45f, mPlant, pl);
            }
        }

        // ============================================================ lights

        static void BuildLights(Transform p)
        {
            var g = Group("Lights", p);
            int k = 0;
            foreach (float x in new[] { -3.0f, 0f, 3.0f })
                foreach (float z in new[] { -3.0f, 0.4f, 3.2f })
                {
                    CeilingPanel(g, "LedPanel_" + k, new Vector3(x, H, z), new Vector2(1.2f, 0.6f), mHousing, mPanel,
                                 new Color(0.97f, 0.98f, 1f), x == 0f && z == 0.4f ? 2.2f : 1.6f, 7.5f, x == 0f && z == 0.4f);
                    k++;
                }
            PointLight("Fill_Console", g, new Vector3(0f, 2.3f, -0.8f), new Color(1f, 0.98f, 0.94f), 0.9f, 4.5f, false);
        }
    }
}
