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
    /// Builds Floor 5, "The Final Vault" (quick sort): a bright marble vault with a long counter holding eight
    /// numbered gold bars, a laser gate, two choice buttons, a round vault door with a hand wheel, safe-deposit
    /// walls, pillars and the gold chest. The vault door opens onto the exit stairs and the end scoreboard.
    /// Menu: Sort or Stay > Build Level 5 (Vault). Re-running it rebuilds and overwrites the scene.
    public static class BuildLevel5
    {
        const string SCENE = ROOT + "Scenes/Level5_Quick.unity";

        const float W = 11f, D = 10f, H = 4.6f, T = 0.2f;
        const float HW = W * 0.5f, HD = D * 0.5f;
        const float DOOR_W = 1.5f, DOOR_H = 2.5f, SILL = 0.2f;
        const float DOOR_CY = (SILL + DOOR_H) * 0.5f, VAULT_R = 1.4f;
        const int BARS = 8;

        const float COUNTER_LEN = 4.6f, COUNTER_DEP = 0.6f, COUNTER_Z = 0.7f, COUNTER_Y = 0.92f, PITCH = 0.46f;
        const float BW = 0.34f, BH = 0.13f, BD = 0.18f;     // gold bar size
        const float PADS_FORWARD = -0.19f;

        static readonly Vector3 SPAWN = new Vector3(0f, 0f, -2.6f);
        static readonly int[] PREVIEW = { 47, 19, 81, 31, 64, 11, 93, 36 };

        static Material mFloor, mMarble, mMarbleDark, mWall, mCeil, mGold, mBrass, mVaultSteel, mVaultDark, mDark, mVelvet, mPlate,
                        mCarpet, mStrip, mLaser, mPadBlue, mPadRed, mScreen, mExit, mLamp, mPanel, mHousing, mDeposit, mTread;

        static float SlotX(int j) => -PITCH * (BARS - 1) * 0.5f + PITCH * j;

        [MenuItem("Sort or Stay/Build Level 5 (Vault)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildNow();
        }

        [MenuItem("Sort or Stay/Build Levels 4 + 5")]
        public static void BuildBoth()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildLevel4.BuildNow();
            BuildNow();
        }

        public static void BuildNow()
        {
            Begin();
            EnsureCharacters();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            MakeMaterials();

            var root = new GameObject("Level5_FinalVault").transform;
            BuildRoom(root);
            var door = BuildVaultDoor(root, out Transform wheel);

            BuildCounter(root, out Transform[] slots);
            var bars = BuildBars(root, slots);
            var gate = BuildGate(root);
            var strip = Cube("RangeStrip", new Vector3(0f, COUNTER_Y + 0.003f, COUNTER_Z), new Vector3(3.7f, 0.006f, 0.34f), mStrip, root, false);
            strip.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var pads = BuildPads(root, out PushButton smaller, out PushButton bigger);
            var pivotLight = PointLight("PivotLight", root, new Vector3(0f, 1.7f, COUNTER_Z), new Color(1f, 0.82f, 0.45f), 1.4f, 1.9f, false);
            pivotLight.enabled = false;
            var board = BuildBoard(root);
            var terminal = BuildKiosk(root);
            BuildDepositWalls(root);
            BuildProps(root);
            BuildLights(root);

            var player = BuildPlayer(SPAWN, 0f);

            var sys = new GameObject("Systems").transform;
            BuildUI(sys, "FLOOR 5  ·  THE FINAL VAULT", 5);

            var puzzle = sys.gameObject.AddComponent<QuickPuzzle>();
            puzzle.items = bars;
            puzzle.slots = slots;
            puzzle.gate = gate;
            puzzle.rangeStrip = strip.transform;
            puzzle.pads = pads;
            puzzle.padsForward = PADS_FORWARD;
            puzzle.smallerPad = smaller;
            puzzle.biggerPad = bigger;
            puzzle.pivotLight = pivotLight;
            puzzle.board = board;
            puzzle.terminal = terminal;
            puzzle.vaultWheel = wheel;
            puzzle.door = door;
            terminal.puzzle = puzzle;
            smaller.puzzle = puzzle;
            bigger.puzzle = puzzle;

            var lm = sys.gameObject.AddComponent<LevelManager>();
            lm.levelNumber = 5;
            lm.nextScene = "";                 // the last floor: walking out shows the final scoreboard
            lm.nextFloorLabel = "";
            lm.door = door;
            lm.player = player;

            var overview = new GameObject("IntroCamera").transform;
            overview.SetParent(sys, false);
            overview.position = new Vector3(4.2f, 3.2f, -3.8f);
            overview.rotation = Quaternion.LookRotation(new Vector3(0f, 1.5f, 2.6f) - overview.position);
            var intro = sys.gameObject.AddComponent<LevelIntro>();
            intro.player = player;
            intro.puzzle = puzzle;
            intro.overview = overview;
            intro.objective = "<b>Quick sort</b> the gold bars, smallest first.\n" +
                              "Raise the <b>pivot</b> (the last bar).  Every other bar:  <b>smaller</b> goes behind the gate,  <b>bigger</b> stays.  Then drop the pivot into its place.\n" +
                              "<size=20>P pivot  ·  1 smaller  ·  2 bigger  ·  H hint</size>";
            intro.lines = new[]
            {
                "Floor 5.  The vault:  the last door between you and the street.",
                "Eight gold bars, out of order.  The vault opens only for a sorted row.",
                "Pick a pivot, split the rest around it, and do it again."
            };

            BuildStairwell(root, HD + T, H, DOOR_W, lm, "EXIT", mTread, mWall, mCeil, mTread, mExit);
            BrightPostFx(sys, "Level5_PostFX", 0.18f);

            BrightEnvironment(new Color(0.86f, 0.84f, 0.82f), new Color(0.72f, 0.66f, 0.58f), new Color(0.46f, 0.40f, 0.34f));
            var sun = Sun(root, new Vector3(40f, -120f, 0f), new Color(1f, 0.93f, 0.80f), 1.2f);
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE, 4);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;
            Debug.Log("Sort or Stay: Level 5 'The Final Vault' built and saved to " + SCENE + ". Press Play.");
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mFloor = Pbr("L5_Floor", "Marble012", new Color(0.98f, 0.96f, 0.93f), 3f, 0.7f, 0f);
            mMarble = Pbr("L5_Marble", "Marble012", new Color(0.96f, 0.94f, 0.91f), 1.2f, 0.75f, 0f);
            mMarbleDark = Pbr("L5_MarbleDark", "Marble012", new Color(0.40f, 0.38f, 0.38f), 1.2f, 0.7f, 0f);
            mWall = Pbr("L5_Wall", "PaintedPlaster017", new Color(0.98f, 0.94f, 0.86f), 3f, 0.12f, 0f);
            mCeil = Flat("L5_Ceiling", new Color(0.97f, 0.95f, 0.91f), 0.1f, 0f);
            mGold = Flat("L5_Gold", new Color(1.0f, 0.76f, 0.26f), 0.8f, 0.95f);
            mBrass = Flat("L5_Brass", new Color(0.82f, 0.64f, 0.30f), 0.65f, 0.75f);
            mVaultSteel = Pbr("L5_VaultSteel", "MetalPlates017A", new Color(0.80f, 0.82f, 0.85f), 1f, 0.55f, 0.6f);
            mVaultDark = Pbr("L5_VaultDark", "Metal063", new Color(0.48f, 0.50f, 0.53f), 1f, 0.5f, 0.6f);
            mDark = Flat("L5_Dark", new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.3f);
            mVelvet = Flat("L5_Velvet", new Color(0.45f, 0.06f, 0.10f), 0.15f, 0f);
            mPlate = Flat("L5_BarPlate", new Color(0.98f, 0.96f, 0.90f), 0.3f, 0f);
            mCarpet = Pbr("L5_Carpet", "Carpet015", new Color(0.62f, 0.12f, 0.14f), 2f, 0.05f, 0f);
            mStrip = Emissive("L5_RangeStrip", new Color(1f, 0.75f, 0.25f), new Color(1f, 0.75f, 0.25f), 1.3f);
            mLaser = Emissive("L5_Laser", new Color(1f, 0.15f, 0.1f), new Color(1f, 0.15f, 0.1f), 4f);
            mPadBlue = Emissive("L5_PadSmaller", new Color(0.25f, 0.55f, 1f), new Color(0.3f, 0.6f, 1f), 1.2f);
            mPadRed = Emissive("L5_PadBigger", new Color(1f, 0.40f, 0.25f), new Color(1f, 0.42f, 0.28f), 1.2f);
            mScreen = Emissive("L5_Screen", new Color(0.05f, 0.04f, 0.02f), new Color(0.12f, 0.08f, 0.02f), 1f);
            mExit = Emissive("L5_Exit", new Color(0.05f, 0.5f, 0.2f), new Color(0.1f, 0.9f, 0.35f), 1.6f);
            mLamp = Emissive("L5_DoorLamp", new Color(1f, 0.12f, 0.08f), new Color(1f, 0.12f, 0.08f), 4f);
            mPanel = Emissive("L5_LightPanel", new Color(1f, 0.97f, 0.9f), new Color(1f, 0.95f, 0.85f), 1.7f);
            mHousing = Flat("L5_PanelHousing", new Color(0.86f, 0.70f, 0.40f), 0.6f, 0.6f);
            mDeposit = Pbr("L5_DepositDoor", "MetalPlates017A", new Color(0.88f, 0.80f, 0.62f), 1f, 0.6f, 0.65f);
            mTread = Pbr("L5_Tread", "Marble012", new Color(0.86f, 0.84f, 0.80f), 1f, 0.6f, 0f);
        }

        // ============================================================ room

        static void BuildRoom(Transform p)
        {
            var room = Group("Room", p);
            Cube("Floor", new Vector3(0f, -T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mFloor, room);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mCeil, room);
            for (int i = 0; i < 4; i++)
                Cube("Beam_" + i, new Vector3(0f, H - 0.12f, -3.6f + i * 2.4f), new Vector3(W, 0.24f, 0.3f), mBrass, room, false);

            WallAlongZ(room, "Wall_Left", -HW - T * 0.5f, -HD - T, HD + T, H, T, mWall);
            WallAlongZ(room, "Wall_Right", HW + T * 0.5f, -HD - T, HD + T, H, T, mWall);
            WallAlongX(room, "Wall_Back", -HD - T * 0.5f, -HW, HW, H, T, mWall);
            WallAlongX(room, "Wall_Front", HD + T * 0.5f, -HW, HW, H, T, mWall, new Hole(0f, DOOR_W, SILL, DOOR_H));

            // dark marble skirting and a brass rail all round
            float sh = 0.7f;
            Cube("Skirt_L", new Vector3(-HW + 0.01f, sh * 0.5f, 0f), new Vector3(0.02f, sh, D), mMarbleDark, room, false);
            Cube("Skirt_R", new Vector3(HW - 0.01f, sh * 0.5f, 0f), new Vector3(0.02f, sh, D), mMarbleDark, room, false);
            Cube("Skirt_B", new Vector3(0f, sh * 0.5f, -HD + 0.01f), new Vector3(W, sh, 0.02f), mMarbleDark, room, false);
            float segW = (W - 3.4f) * 0.5f;
            Cube("Skirt_FL", new Vector3(-(1.7f + segW * 0.5f), sh * 0.5f, HD - 0.01f), new Vector3(segW, sh, 0.02f), mMarbleDark, room, false);
            Cube("Skirt_FR", new Vector3(1.7f + segW * 0.5f, sh * 0.5f, HD - 0.01f), new Vector3(segW, sh, 0.02f), mMarbleDark, room, false);
            Cube("Rail_L", new Vector3(-HW + 0.02f, sh, 0f), new Vector3(0.04f, 0.04f, D), mBrass, room, false);
            Cube("Rail_R", new Vector3(HW - 0.02f, sh, 0f), new Vector3(0.04f, 0.04f, D), mBrass, room, false);
            Cube("Rail_B", new Vector3(0f, sh, -HD + 0.02f), new Vector3(W, 0.04f, 0.04f), mBrass, room, false);

            // the door we came in by (back wall), closed
            var back = Group("EntranceDoor", room);
            back.localPosition = new Vector3(0f, 0f, -HD);
            Cube("Frame", new Vector3(0f, 1.2f, 0.03f), new Vector3(1.4f, 2.4f, 0.06f), mBrass, back, false);
            Cube("Slab", new Vector3(0f, 1.15f, 0.065f), new Vector3(1.16f, 2.28f, 0.05f), mVaultSteel, back, false);
            Cube("Bar", new Vector3(0f, 1.05f, 0.1f), new Vector3(0.9f, 0.06f, 0.05f), mBrass, back, false);

            // a red carpet from the entrance to the counter
            Cube("CarpetBorder", new Vector3(0f, 0.003f, -2.3f), new Vector3(1.8f, 0.006f, 4.9f), mBrass, room, false);
            Cube("Carpet", new Vector3(0f, 0.006f, -2.3f), new Vector3(1.64f, 0.008f, 4.74f), mCarpet, room, false);

            // exit sign above the vault door
            Cube("ExitSign", new Vector3(-2.3f, 2.55f, HD - 0.02f), new Vector3(0.7f, 0.22f, 0.04f), mExit, room, false);
            Text("ExitText", room, new Vector3(-2.3f, 2.55f, HD - 0.045f), new Vector2(0.62f, 0.16f), "EXIT", Color.white, TextAlignmentOptions.Center, true);
        }

        // ============================================================ vault door

        static Door BuildVaultDoor(Transform p, out Transform wheel)
        {
            var g = Group("VaultDoor", p);
            g.localPosition = new Vector3(0f, 0f, HD);       // the room-side face of the front wall

            // brass ring on the wall around the opening
            const int segs = 28;
            float rr = VAULT_R + 0.17f;
            for (int k = 0; k < segs; k++)
            {
                float a = k * 360f / segs;
                float rad = a * Mathf.Deg2Rad;
                var s = Cube("Ring_" + k, new Vector3(Mathf.Cos(rad) * rr, DOOR_CY + Mathf.Sin(rad) * rr, -0.06f),
                             new Vector3(2f * Mathf.PI * rr / segs + 0.03f, 0.22f, 0.12f), mBrass, g, false);
                s.transform.localRotation = Quaternion.Euler(0f, 0f, a + 90f);
            }

            // the door itself: a thick steel disk that swings into the room on a hinge at its left edge
            var panel = Group("Panel", g);
            panel.localPosition = new Vector3(0f, DOOR_CY, -0.26f);
            CylZ("Disk", Vector3.zero, VAULT_R * 2f, 0.28f, mVaultSteel, panel);
            CylZ("Face", new Vector3(0f, 0f, -0.145f), VAULT_R * 1.62f, 0.02f, mVaultDark, panel);
            CylZ("InnerFace", new Vector3(0f, 0f, -0.152f), VAULT_R * 1.2f, 0.02f, mVaultSteel, panel);
            for (int k = 0; k < 16; k++)
            {
                float rad = k * Mathf.PI * 2f / 16f;
                CylZ("Bolt", new Vector3(Mathf.Cos(rad) * VAULT_R * 0.9f, Mathf.Sin(rad) * VAULT_R * 0.9f, -0.15f), 0.11f, 0.05f, mBrass, panel);
            }
            CylZ("Hub", new Vector3(0f, 0f, -0.18f), 0.46f, 0.08f, mBrass, panel);
            wheel = Group("Wheel", panel);
            wheel.localPosition = new Vector3(0f, 0f, -0.27f);
            for (int s = 0; s < 3; s++)
            {
                var spoke = Cube("Spoke", Vector3.zero, new Vector3(1.24f, 0.06f, 0.05f), mBrass, wheel, false);
                spoke.transform.localRotation = Quaternion.Euler(0f, 0f, s * 60f);
            }
            for (int k = 0; k < 6; k++)
            {
                float rad = k * Mathf.PI / 3f;
                Sphere("Knob", new Vector3(Mathf.Cos(rad) * 0.64f, Mathf.Sin(rad) * 0.64f, 0f), 0.11f, mBrass, wheel);
            }
            CylZ("WheelHub", Vector3.zero, 0.18f, 0.1f, mGold, wheel);
            Text("Plate", panel, new Vector3(0f, -0.92f, -0.164f), new Vector2(1.2f, 0.1f), "ALL BARS IN ORDER", new Color(0.25f, 0.2f, 0.1f), TextAlignmentOptions.Center, true);
            var bc = panel.gameObject.AddComponent<BoxCollider>();
            bc.center = Vector3.zero;
            bc.size = new Vector3(2.1f, 2.5f, 0.3f);

            var lampGo = Sphere("StatusLamp", new Vector3(2.0f, DOOR_CY + 1.25f, -0.07f), 0.12f, mLamp, g);
            Cube("LampBase", new Vector3(2.0f, DOOR_CY + 1.25f, -0.02f), new Vector3(0.18f, 0.18f, 0.03f), mBrass, g, false);
            var lampLight = PointLight("StatusLight", g, g.TransformPoint(new Vector3(2.0f, DOOR_CY + 1.25f, -0.4f)), new Color(1f, 0.12f, 0.08f), 0.6f, 1.8f, false);

            var door = g.gameObject.AddComponent<Door>();
            door.panel = panel;
            door.swing = true;
            door.hinge = new Vector3(-(VAULT_R + 0.05f), 0f, -0.26f);
            door.swingAngle = 105f;                            // positive: swings into the room
            door.openTime = 2.6f;
            door.statusLamp = lampGo.GetComponent<Renderer>();
            door.statusLight = lampLight;
            door.lockedMessage = "The vault door won't move.  A brass plate reads:  ALL BARS IN ORDER.";
            return door;
        }

        // ============================================================ counter, bars, gate, pads

        static void BuildCounter(Transform p, out Transform[] slots)
        {
            var g = Group("Counter", p);
            float front = COUNTER_Z - COUNTER_DEP * 0.5f;
            Cube("Body", new Vector3(0f, (COUNTER_Y - 0.04f) * 0.5f, COUNTER_Z), new Vector3(COUNTER_LEN, COUNTER_Y - 0.04f, COUNTER_DEP), mMarble, g);
            Cube("Top", new Vector3(0f, COUNTER_Y - 0.02f, COUNTER_Z), new Vector3(COUNTER_LEN + 0.08f, 0.04f, COUNTER_DEP + 0.08f), mMarbleDark, g, false);
            Cube("Plinth", new Vector3(0f, 0.05f, COUNTER_Z), new Vector3(COUNTER_LEN + 0.06f, 0.1f, COUNTER_DEP + 0.06f), mMarbleDark, g, false);
            Cube("Trim", new Vector3(0f, COUNTER_Y - 0.07f, front - 0.006f), new Vector3(COUNTER_LEN, 0.025f, 0.012f), mGold, g, false);
            Text("Rule", g, new Vector3(0f, 0.42f, front - 0.006f), new Vector2(3.8f, 0.1f),
                 "SMALLER  BEHIND  THE  GATE   ·   BIGGER  STAYS   ·   THE  PIVOT  FINDS  ITS  PLACE", new Color(0.35f, 0.27f, 0.12f), TextAlignmentOptions.Center, true);

            slots = new Transform[BARS];
            for (int j = 0; j < BARS; j++)
            {
                var pad = Cube("Velvet_" + (j + 1), new Vector3(SlotX(j), COUNTER_Y + 0.004f, COUNTER_Z), new Vector3(0.4f, 0.008f, 0.24f), mVelvet, g, false);
                pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                Text("SlotNumber_" + (j + 1), g, new Vector3(SlotX(j), COUNTER_Y - 0.15f, front - 0.006f), new Vector2(0.2f, 0.09f), (j + 1).ToString(),
                     new Color(0.45f, 0.33f, 0.12f), TextAlignmentOptions.Center, true);

                var s = new GameObject("Slot_" + (j + 1)).transform;
                s.SetParent(p, false);
                s.position = new Vector3(SlotX(j), COUNTER_Y + 0.008f, COUNTER_Z);
                s.rotation = Quaternion.identity;               // bars face the player (-Z)
                slots[j] = s;
            }
        }

        static List<SortItem> BuildBars(Transform p, Transform[] slots)
        {
            var parent = Group("GoldBars", p);
            var list = new List<SortItem>();
            for (int i = 0; i < BARS; i++)
            {
                var b = new GameObject("Bar_" + (i + 1));
                b.transform.SetParent(parent, false);
                b.transform.SetPositionAndRotation(slots[i].position, slots[i].rotation);

                var body = Cube("Body", new Vector3(0f, BH * 0.5f, 0f), new Vector3(BW, BH, BD), mGold, b.transform, false);
                Cube("Crest", new Vector3(0f, BH + 0.012f, 0.01f), new Vector3(BW - 0.07f, 0.024f, BD - 0.06f), mGold, b.transform, false);
                var plate = Cube("Plate", new Vector3(0f, BH * 0.5f, -BD * 0.5f - 0.003f), new Vector3(BW - 0.08f, 0.1f, 0.005f), mPlate, b.transform, false);
                var num = Text("Number", b.transform, new Vector3(0f, BH * 0.5f, -BD * 0.5f - 0.0065f), new Vector2(BW - 0.11f, 0.088f),
                               PREVIEW[i].ToString(), InkDark, TextAlignmentOptions.Center, true);

                var bc = b.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, (BH + 0.024f) * 0.5f, 0f);
                bc.size = new Vector3(BW, BH + 0.024f, BD);

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

        /// The laser gate: a thin red beam standing on the counter between two bars.
        static Transform BuildGate(Transform p)
        {
            var g = Group("LaserGate", p);
            g.position = new Vector3(SlotX(0) - PITCH * 0.5f, COUNTER_Y, COUNTER_Z);
            Cube("Base", new Vector3(0f, 0.012f, 0f), new Vector3(0.05f, 0.024f, 0.3f), mDark, g, false);
            var beam = Cube("Beam", new Vector3(0f, 0.22f, 0f), new Vector3(0.018f, 0.4f, 0.26f), mLaser, g, false);
            beam.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            Cube("Cap", new Vector3(0f, 0.43f, 0f), new Vector3(0.05f, 0.02f, 0.3f), mDark, g, false);
            PointLight("Glow", g, g.TransformPoint(new Vector3(0f, 0.25f, -0.2f)), new Color(1f, 0.2f, 0.12f), 0.5f, 0.9f, false);
            g.gameObject.SetActive(false);
            return g;
        }

        static Transform BuildPads(Transform p, out PushButton smaller, out PushButton bigger)
        {
            var g = Group("ChoicePads", p);
            g.position = new Vector3(SlotX(0), COUNTER_Y, COUNTER_Z + PADS_FORWARD);
            smaller = Pad(g, "SmallerPad", new Vector3(-0.1f, 0f, 0f), mPadBlue, "1  SMALLER", "smaller", "Smaller than the pivot:  swap it behind the gate   [1]");
            bigger = Pad(g, "BiggerPad", new Vector3(0.1f, 0f, 0f), mPadRed, "BIGGER  2", "bigger", "Bigger than the pivot:  leave it   [2]");
            g.gameObject.SetActive(false);
            return g;
        }

        static PushButton Pad(Transform g, string name, Vector3 pos, Material capMat, string label, string id, string prompt)
        {
            var b = Group(name, g);
            b.localPosition = pos;
            CylY("Collar", new Vector3(0f, 0.008f, 0f), 0.13f, 0.016f, mDark, b);
            var cap = CylY("Cap", new Vector3(0f, 0.024f, 0f), 0.1f, 0.026f, capMat, b).transform;
            var t = Text("Label", b, Vector3.zero, new Vector2(0.17f, 0.032f), label, InkDark, TextAlignmentOptions.Center, true);
            t.rectTransform.localPosition = new Vector3(0f, 0.002f, -0.075f);
            t.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var bc = b.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.025f, -0.01f);
            bc.size = new Vector3(0.17f, 0.06f, 0.16f);
            var pb = b.gameObject.AddComponent<PushButton>();
            pb.id = id;
            pb.label = prompt;
            pb.cap = cap;
            return pb;
        }

        // ============================================================ board + kiosk

        static TMP_Text BuildBoard(Transform p)
        {
            var g = Group("VaultBoard", p);
            g.localPosition = new Vector3(0f, 3.6f, HD);
            Cube("Bezel", new Vector3(0f, 0f, -0.03f), new Vector3(3.7f, 1.02f, 0.06f), mBrass, g, false);
            Cube("Screen", new Vector3(0f, 0f, -0.062f), new Vector3(3.56f, 0.9f, 0.004f), mScreen, g, false);
            return Text("BoardText", g, new Vector3(0f, 0f, -0.066f), new Vector2(3.4f, 0.82f), "<b>VAULT LOCK  ·  QUICK SORT</b>",
                        new Color(1f, 0.88f, 0.6f), TextAlignmentOptions.Center, false);
        }

        static Terminal BuildKiosk(Transform p)
        {
            var g = Group("VaultKiosk", p);
            g.localPosition = new Vector3(3.6f, 0f, 0.5f);
            g.localRotation = Quaternion.Euler(0f, 30f, 0f);         // local -Z faces the middle of the room
            Cube("Foot", new Vector3(0f, 0.03f, 0f), new Vector3(0.55f, 0.06f, 0.45f), mMarbleDark, g);
            Cube("Post", new Vector3(0f, 0.55f, 0.03f), new Vector3(0.16f, 1.0f, 0.12f), mBrass, g);
            var head = Group("Head", g);
            head.localPosition = new Vector3(0f, 1.25f, 0f);
            head.localRotation = Quaternion.Euler(12f, 0f, 0f);
            Cube("Bezel", Vector3.zero, new Vector3(0.74f, 0.52f, 0.06f), mBrass, head);
            Cube("Screen", new Vector3(0f, 0.01f, -0.031f), new Vector3(0.68f, 0.44f, 0.004f), mScreen, head, false);
            var txt = Text("ScreenText", head, new Vector3(0f, 0.01f, -0.034f), new Vector2(0.64f, 0.4f), "<b>VAULT CONTROL</b>   FLOOR 5",
                           new Color(1f, 0.88f, 0.6f), TextAlignmentOptions.TopLeft, false);
            Text("Brand", g, new Vector3(0f, 0.85f, -0.032f), new Vector2(0.14f, 0.04f), "HINTS", Color.white, TextAlignmentOptions.Center, true);

            var term = g.gameObject.AddComponent<Terminal>();
            term.screen = txt;
            term.prompt = "Vault control  ·  ask for a hint";
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 0.78f, 0f);
            bc.size = new Vector3(0.74f, 1.56f, 0.45f);
            return term;
        }

        // ============================================================ safe-deposit walls + props

        static void BuildDepositWalls(Transform p)
        {
            var g = Group("DepositBoxes", p);
            DepositWall(g, new Vector3(-HW + 0.05f, 0f, -2.1f), -90f);
            DepositWall(g, new Vector3(HW - 0.05f, 0f, -2.1f), 90f);
        }

        /// A wall of small brass safe-deposit doors; its front faces local -Z after 'yaw'.
        static void DepositWall(Transform p, Vector3 pos, float yaw)
        {
            var w = Group("DepositWall", p);
            w.localPosition = pos;
            w.localRotation = Quaternion.Euler(0f, yaw, 0f);
            const int cols = 8, rows = 6;
            float cw = 0.4f, ch = 0.34f, y0 = 0.85f;
            float width = cols * cw + 0.1f, height = rows * ch + 0.1f;
            Cube("Frame", new Vector3(0f, y0 + height * 0.5f - 0.05f, 0.04f), new Vector3(width, height, 0.1f), mBrass, w);
            var mats = new List<CombineInstance>();
            var keys = new List<CombineInstance>();
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    Vector3 cpos = new Vector3(-width * 0.5f + 0.05f + cw * (c + 0.5f), y0 + ch * (r + 0.5f), -0.015f);
                    mats.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(cpos, Quaternion.identity, new Vector3(cw - 0.03f, ch - 0.03f, 0.03f)) });
                    keys.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(cpos + new Vector3(cw * 0.3f, 0f, -0.018f), Quaternion.identity, new Vector3(0.03f, 0.05f, 0.01f)) });
                }
            Merged(w, "Doors", mats, mDeposit);
            Merged(w, "Keyholes", keys, mDark);
        }

        static void Merged(Transform parent, string name, List<CombineInstance> parts, Material m)
        {
            var mesh = new Mesh { name = name + "_Mesh", indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = m;
        }

        static void BuildProps(Transform p)
        {
            var g = Group("Props", p);

            // four slim marble columns with brass rings
            foreach (float sx in new[] { -1f, 1f })
                foreach (float z in new[] { -3.6f, 2.9f })
                {
                    var c = Group("Column", g);
                    c.localPosition = new Vector3(sx * 3.8f, 0f, z);
                    Cube("Base", new Vector3(0f, 0.11f, 0f), new Vector3(0.68f, 0.22f, 0.68f), mMarbleDark, c);
                    CylY("Shaft", new Vector3(0f, H * 0.5f, 0f), 0.46f, H - 0.4f, mMarble, c, true);
                    CylY("RingLow", new Vector3(0f, 0.3f, 0f), 0.52f, 0.06f, mBrass, c);
                    CylY("RingHigh", new Vector3(0f, H - 0.3f, 0f), 0.52f, 0.06f, mBrass, c);
                    Cube("Capital", new Vector3(0f, H - 0.12f, 0f), new Vector3(0.64f, 0.24f, 0.64f), mMarbleDark, c, false);
                }

            // the gold chest on a marble plinth, and coin stacks on a side table
            var plinth = Cube("ChestPlinth", new Vector3(4.55f, 0.3f, 4.15f), new Vector3(1.0f, 0.6f, 0.8f), mMarbleDark, g);
            plinth.name = "ChestPlinth";
            Prop(DUNGEON + "chest_gold.fbx", "GoldChest", g, new Vector3(4.55f, 0.6f, 4.15f), -150f, 0.62f, false);
            Cube("CoinTable", new Vector3(-4.55f, 0.37f, 4.15f), new Vector3(1.0f, 0.74f, 0.7f), mMarbleDark, g);
            Prop(DUNGEON + "coin_stack_large.fbx", "CoinsLarge", g, new Vector3(-4.75f, 0.74f, 4.05f), 20f, 0.3f, false);
            Prop(DUNGEON + "coin_stack_medium.fbx", "CoinsMedium", g, new Vector3(-4.35f, 0.74f, 4.25f), -10f, 0.22f, false);
            Prop(DUNGEON + "coin_stack_small.fbx", "CoinsSmall", g, new Vector3(-4.45f, 0.74f, 3.9f), 40f, 0.14f, false);

            // velvet rope posts either side of the carpet, near the entrance
            foreach (float sx in new[] { -1f, 1f })
            {
                for (int k = 0; k < 3; k++)
                {
                    float z = -4.4f + k * 1.1f;
                    CylY("RopePost", new Vector3(sx * 1.15f, 0.48f, z), 0.07f, 0.96f, mBrass, g);
                    CylY("RopeBase", new Vector3(sx * 1.15f, 0.02f, z), 0.26f, 0.04f, mBrass, g);
                    Sphere("RopeTop", new Vector3(sx * 1.15f, 0.98f, z), 0.1f, mBrass, g);
                    if (k < 2) Cube("Rope", new Vector3(sx * 1.15f, 0.82f, z + 0.55f), new Vector3(0.04f, 0.04f, 1.0f), mVelvet, g, false);
                }
                var col = new GameObject("RopeBarrier").AddComponent<BoxCollider>();
                col.transform.SetParent(g, false);
                col.center = new Vector3(sx * 1.15f, 0.5f, -3.3f);
                col.size = new Vector3(0.12f, 1.0f, 2.3f);
            }
        }

        // ============================================================ lights

        static void BuildLights(Transform p)
        {
            var g = Group("Lights", p);
            int k = 0;
            // panels sit between the ceiling beams (beams at z = -3.6, -1.2, 1.2, 3.6)
            foreach (float x in new[] { -2.6f, 0f, 2.6f })
                foreach (float z in new[] { -2.4f, 0f, 2.4f })
                {
                    bool key = x == 0f && z == 0f;
                    CeilingPanel(g, "LightPanel_" + k, new Vector3(x, H, z), new Vector2(1.2f, 0.6f), mHousing, mPanel,
                                 new Color(1f, 0.95f, 0.86f), key ? 2.3f : 1.6f, 8f, key);
                    k++;
                }
            PointLight("Fill_Door", g, new Vector3(0f, 2.8f, 3.4f), new Color(1f, 0.93f, 0.82f), 1.2f, 6f, false);
            PointLight("Fill_Counter", g, new Vector3(0f, 2.2f, -0.6f), new Color(1f, 0.96f, 0.9f), 0.9f, 4.5f, false);
            Spot("CounterSpot", g, new Vector3(0f, H - 0.3f, -0.4f), new Vector3(0f, COUNTER_Y, COUNTER_Z), new Color(1f, 0.92f, 0.78f), 2.2f, 7f, 50f, false);
        }
    }
}
