using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SortOrStay;

namespace SortOrStayEditor
{
    /// Builds Floor 1, "The Locked Room" (bubble sort), from scratch: bright LED-lit room, realistic third-person avatar.
    /// Menu: Sort or Stay > Build Level 1 (Locked Room). Re-running it rebuilds and overwrites the scene.
    public static class BuildLevel1
    {
        const string ROOT = "Assets/SortOrStay/";
        const string TP = "Assets/ThirdParty/SortOrStay/";
        const string DUNGEON = TP + "KayKit_DungeonRemastered/Models/";
        const string SPACE = TP + "Kenney_SpaceStationKit/Models/";
        const string TEX = TP + "Textures/";
        const string SCENE = ROOT + "Scenes/Level1_Bubble.unity";
        const string MATDIR = ROOT + "Materials/";
        const string TMP_SETTINGS = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
        const string TMP_FONT = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // room, in metres
        const float W = 6f, D = 7f, H = 3f, T = 0.2f;        // width (x), depth (z), height, wall thickness
        const float DOOR_W = 1.2f, DOOR_H = 2.3f;
        const float FRONT = D * 0.5f;                         // inner face of the front (door) wall
        const float RACK_X = 1.85f;
        const float TERM_X = -1.9f;
        static readonly Vector3 SPAWN = new Vector3(0f, 0f, -2.4f);
        static readonly int[] PREVIEW_ORDER = { 5, 2, 6, 1, 4, 3 };   // what the editor shows; play mode reshuffles

        static readonly Color InkDark = new Color(0.08f, 0.08f, 0.09f);

        static Material mFloor, mWall, mCeil, mSteel, mDark, mTread, mWood, mTag, mSign, mInk, mScreen, mBulb, mLed, mLamp, mHook, mRed, mExit;
        static TMP_FontAsset font;

        enum Anchor { Bottom, Top, Center }

        // ============================================================ entry

        [MenuItem("Sort or Stay/Build Level 1 (Locked Room)")]
        public static void Build()
        {
            if (!File.Exists(TMP_SETTINGS)) { ImportTmpThenBuild(); return; }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildNow();
        }

        static void ImportTmpThenBuild()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string pkg = info != null ? Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage") : "";
            if (string.IsNullOrEmpty(pkg) || !File.Exists(pkg))
            {
                EditorUtility.DisplayDialog("Sort or Stay",
                    "TextMesh Pro essentials are missing.\n\nWindow > TextMeshPro > Import TMP Essential Resources,\nthen run this menu again.", "OK");
                return;
            }
            Debug.Log("Sort or Stay: importing TextMesh Pro essentials first...");
            AssetDatabase.importPackageCompleted -= OnTmpImported;
            AssetDatabase.importPackageCompleted += OnTmpImported;
#pragma warning disable CS0618   // ImportPackage is marked obsolete in Unity 6.6 but still works; this path only runs once
            AssetDatabase.ImportPackage(pkg, false);
#pragma warning restore CS0618
        }

        static void OnTmpImported(string packageName)
        {
            AssetDatabase.importPackageCompleted -= OnTmpImported;
            AssetDatabase.Refresh();
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(TMP_SETTINGS)) Build();
                else Debug.LogWarning("Sort or Stay: TMP essentials did not finish importing. Run the menu again.");
            };
        }

        static void BuildNow()
        {
            Directory.CreateDirectory(ROOT + "Scenes");
            Directory.CreateDirectory(MATDIR);
            Directory.CreateDirectory(ROOT + "Settings");
            AssetDatabase.Refresh();

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TMP_FONT);
            Kit.Begin();
            Kit.EnsureCharacters();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            MakeMaterials();

            var root = new GameObject("Level1_LockedRoom").transform;
            BuildRoom(root);
            Door door = BuildDoor(root, out Renderer[] leds);
            Transform[] slots = BuildRack(root);
            List<SortItem> keys = BuildKeys(root, slots);
            Terminal terminal = BuildTerminal(root);
            BuildSign(root);
            BuildProps(root);
            BuildLights(root);

            // ---------- player: realistic human avatar, third-person camera ----------
            var pc = Kit.BuildPlayer(SPAWN, 0f);

            // ---------- systems ----------
            var sys = new GameObject("Systems");
            var ui = sys.AddComponent<GameUI>();
            ui.levelTitle = "FLOOR 1  ·  THE LOCKED ROOM";
            ui.floor = 1;

            var puzzle = sys.AddComponent<BubblePuzzle>();
            puzzle.slots = slots;
            puzzle.items = keys;
            puzzle.door = door;
            puzzle.terminal = terminal;
            puzzle.slotLeds = leds;
            terminal.puzzle = puzzle;

            var lm = sys.AddComponent<LevelManager>();
            lm.levelNumber = 1;
            lm.nextScene = "Level2_Selection";
            lm.door = door;
            lm.player = pc;

            var intro = sys.AddComponent<IntroSequence>();
            intro.player = pc;
            intro.puzzle = puzzle;

            BuildCorridor(root, lm);
            Kit.BrightPostFx(sys.transform, "Level1_PostFX", 0.2f);

            // ---------- lighting: bright and steady ----------
            Kit.BrightEnvironment(new Color(0.80f, 0.82f, 0.86f), new Color(0.64f, 0.64f, 0.62f), new Color(0.42f, 0.40f, 0.38f));

            // ---------- save ----------
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = root.gameObject;

            Debug.Log("Sort or Stay: Level 1 'The Locked Room' built and saved to " + SCENE + ". Press Play.");
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mFloor = Pbr("Floor_Concrete", "Concrete034", new Color(0.86f, 0.86f, 0.84f), 3f, 0.3f, 0f);
            mWall = Pbr("Wall_Plaster", "PaintedPlaster017", new Color(0.97f, 0.96f, 0.93f), 2.5f, 0.10f, 0f);
            mCeil = Pbr("Ceiling_Plaster", "PaintedPlaster017", new Color(0.96f, 0.96f, 0.96f), 3f, 0.05f, 0f);
            mSteel = Pbr("Door_Steel", "MetalPlates017A", new Color(0.78f, 0.80f, 0.84f), 1f, 0.45f, 0.35f);
            mDark = Pbr("Metal_Dark", "Metal063", new Color(0.42f, 0.43f, 0.46f), 1f, 0.40f, 0.35f);
            mTread = Pbr("Metal_Tread", "DiamondPlate009", new Color(0.70f, 0.71f, 0.73f), 2f, 0.40f, 0.4f);
            mWood = Pbr("Rack_Wood", "WoodFloor064", new Color(0.86f, 0.66f, 0.46f), 0.6f, 0.3f, 0f);

            mTag = Flat("Key_Tag", new Color(0.93f, 0.90f, 0.82f), 0.2f, 0f);
            mSign = Flat("Sign_Yellow", new Color(0.95f, 0.78f, 0.15f), 0.35f, 0f);
            mInk = Flat("Ink_Dark", InkDark, 0.3f, 0f);
            mHook = Flat("Hook_Metal", new Color(0.30f, 0.30f, 0.32f), 0.7f, 0.5f);

            mRed = Emissive("Button_Red", new Color(0.75f, 0.08f, 0.06f), new Color(0.9f, 0.1f, 0.06f), 0.6f);
            mScreen = Emissive("Terminal_Screen", new Color(0.01f, 0.05f, 0.04f), new Color(0.02f, 0.22f, 0.14f), 1f);
            mBulb = Emissive("Ceiling_Diffuser", new Color(1f, 0.99f, 0.96f), new Color(1f, 0.97f, 0.92f), 2.6f);
            mLed = Emissive("Led", new Color(0.05f, 0.05f, 0.05f), Color.black, 0f);
            mLamp = Emissive("Door_Lamp", new Color(1f, 0.12f, 0.08f), new Color(1f, 0.12f, 0.08f), 4f);
            mExit = Emissive("Exit_Green", new Color(0.05f, 0.45f, 0.18f), new Color(0.1f, 0.9f, 0.35f), 1.8f);
        }

        static Shader URPLit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        static Material Flat(string name, Color c, float smooth, float metallic)
        {
            string path = MATDIR + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(URPLit); AssetDatabase.CreateAsset(m, path); }
            m.shader = URPLit;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            m.DisableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material Emissive(string name, Color baseColor, Color emission, float strength)
        {
            var m = Flat(name, baseColor, 0.5f, 0f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission * strength);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// ambientCG material: Color + NormalGL, tiled.
        static Material Pbr(string name, string id, Color tint, float tiling, float smooth, float metallic)
        {
            var m = Flat(name, tint, smooth, metallic);
            string folder = TEX + id + "/";
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + id + "_1K-JPG_Color.jpg");
            var nrm = AssetDatabase.LoadAssetAtPath<Texture2D>(folder + id + "_1K-JPG_NormalGL.jpg");
            if (col != null)
            {
                m.SetTexture("_BaseMap", col);
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", col);
            }
            else Debug.LogWarning("Sort or Stay: texture not found for " + id);
            if (nrm != null)
            {
                var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(nrm)) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.SaveAndReimport();
                }
                m.SetTexture("_BumpMap", nrm);
                m.EnableKeyword("_NORMALMAP");
            }
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            EditorUtility.SetDirty(m);
            return m;
        }

        // ============================================================ room

        static void BuildRoom(Transform p)
        {
            var room = Group("Room", p);
            float hw = W * 0.5f, hd = D * 0.5f;

            Cube("Floor", new Vector3(0f, -T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mFloor, room);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, 0f), new Vector3(W + 2f * T, T, D + 2f * T), mCeil, room);
            Cube("Wall_Left", new Vector3(-hw - T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D + 2f * T), mWall, room);
            Cube("Wall_Right", new Vector3(hw + T * 0.5f, H * 0.5f, 0f), new Vector3(T, H, D + 2f * T), mWall, room);
            Cube("Wall_Back", new Vector3(0f, H * 0.5f, -hd - T * 0.5f), new Vector3(W + 2f * T, H, T), mWall, room);

            // front wall, split around the door opening
            float segW = (W + 2f * T - DOOR_W) * 0.5f;
            float segX = DOOR_W * 0.5f + segW * 0.5f;
            float zf = hd + T * 0.5f;
            Cube("Wall_Front_L", new Vector3(-segX, H * 0.5f, zf), new Vector3(segW, H, T), mWall, room);
            Cube("Wall_Front_R", new Vector3(segX, H * 0.5f, zf), new Vector3(segW, H, T), mWall, room);
            Cube("Wall_Front_Top", new Vector3(0f, (DOOR_H + H) * 0.5f, zf), new Vector3(DOOR_W, H - DOOR_H, T), mWall, room);

            // dark skirting along the walls
            Cube("Skirt_L", new Vector3(-hw + 0.01f, 0.06f, 0f), new Vector3(0.02f, 0.12f, D), mDark, room, false);
            Cube("Skirt_R", new Vector3(hw - 0.01f, 0.06f, 0f), new Vector3(0.02f, 0.12f, D), mDark, room, false);
            Cube("Skirt_B", new Vector3(0f, 0.06f, -hd + 0.01f), new Vector3(W, 0.12f, 0.02f), mDark, room, false);
            Cube("Skirt_FL", new Vector3(-segX, 0.06f, hd - 0.01f), new Vector3(segW - 2f * T, 0.12f, 0.02f), mDark, room, false);
            Cube("Skirt_FR", new Vector3(segX, 0.06f, hd - 0.01f), new Vector3(segW - 2f * T, 0.12f, 0.02f), mDark, room, false);

            // pipes along the left wall, a vent in the ceiling
            CylZ("Pipe_A", new Vector3(-hw + 0.14f, H - 0.22f, 0f), 0.10f, D, mDark, room);
            CylZ("Pipe_B", new Vector3(-hw + 0.12f, H - 0.40f, 0f), 0.06f, D, mHook, room);
            for (int i = 0; i < 4; i++)
                Cube("PipeBracket_" + i, new Vector3(-hw + 0.08f, H - 0.30f, -2.6f + i * 1.75f), new Vector3(0.16f, 0.30f, 0.04f), mHook, room, false);
            Cube("Vent", new Vector3(1.6f, H - 0.006f, -1.8f), new Vector3(0.7f, 0.012f, 0.5f), mTread, room, false);
        }

        // ============================================================ door

        static Door BuildDoor(Transform p, out Renderer[] leds)
        {
            var g = Group("Door", p);
            g.localPosition = new Vector3(0f, 0f, FRONT + T * 0.5f);   // centred in the wall's thickness
            float face = -T * 0.5f;                                  // the room-side face of the wall

            // frame, proud of the wall
            Cube("Frame_L", new Vector3(-DOOR_W * 0.5f - 0.06f, (DOOR_H + 0.12f) * 0.5f, face - 0.045f), new Vector3(0.12f, DOOR_H + 0.12f, 0.09f), mDark, g);
            Cube("Frame_R", new Vector3(DOOR_W * 0.5f + 0.06f, (DOOR_H + 0.12f) * 0.5f, face - 0.045f), new Vector3(0.12f, DOOR_H + 0.12f, 0.09f), mDark, g);
            Cube("Frame_Top", new Vector3(0f, DOOR_H + 0.06f, face - 0.045f), new Vector3(DOOR_W + 0.24f, 0.12f, 0.09f), mDark, g);
            Cube("Threshold", new Vector3(0f, 0.008f, 0f), new Vector3(DOOR_W, 0.016f, T + 0.1f), mTread, g, false);

            // the sliding panel, kept inside the wall's thickness so it can disappear into it
            var panel = Group("DoorPanel", g);
            panel.localPosition = new Vector3(0f, DOOR_H * 0.5f, 0f);
            Cube("Slab", Vector3.zero, new Vector3(DOOR_W - 0.02f, DOOR_H - 0.02f, 0.08f), mSteel, panel);
            Cube("Bar_Top", new Vector3(0f, 0.62f, -0.05f), new Vector3(DOOR_W - 0.16f, 0.14f, 0.025f), mTread, panel, false);
            Cube("Bar_Bottom", new Vector3(0f, -0.62f, -0.05f), new Vector3(DOOR_W - 0.16f, 0.14f, 0.025f), mTread, panel, false);
            Cube("Handle_Plate", new Vector3(0.43f, 0f, -0.05f), new Vector3(0.10f, 0.34f, 0.02f), mDark, panel, false);
            Cube("Handle", new Vector3(0.43f, 0f, -0.075f), new Vector3(0.035f, 0.26f, 0.03f), mHook, panel, false);

            // red / green status lamp
            Cube("Lamp_Base", new Vector3(DOOR_W * 0.5f + 0.26f, DOOR_H + 0.02f, face - 0.01f), new Vector3(0.14f, 0.14f, 0.02f), mDark, g, false);
            var lamp = Sphere("StatusLamp", new Vector3(DOOR_W * 0.5f + 0.26f, DOOR_H + 0.02f, face - 0.05f), 0.085f, mLamp, g);
            var lampLight = PointLight("StatusLight", g, g.TransformPoint(new Vector3(DOOR_W * 0.5f + 0.26f, DOOR_H + 0.02f, face - 0.30f)),
                                       new Color(1f, 0.12f, 0.08f), 0.9f, 1.8f, false);

            // slot panel above the door: one LED per hook, lit when that hook holds its final key
            float py = DOOR_H + 0.38f;
            Cube("SlotPanel", new Vector3(0f, py, face - 0.015f), new Vector3(0.92f, 0.26f, 0.03f), mDark, g, false);
            leds = new Renderer[6];
            for (int i = 0; i < 6; i++)
            {
                float x = -0.30f + 0.12f * i;
                leds[i] = Cube("Led_" + i, new Vector3(x, py - 0.035f, face - 0.035f), new Vector3(0.07f, 0.07f, 0.012f), mLed, g, false).GetComponent<Renderer>();
            }
            Text("SlotLabel", g, new Vector3(0f, py + 0.075f, face - 0.032f), new Vector2(0.8f, 0.055f), "KEY ORDER",
                 new Color(0.85f, 0.85f, 0.82f), TextAlignmentOptions.Center, true);

            var door = g.gameObject.AddComponent<Door>();
            door.panel = panel;
            door.swing = true;                                       // hinged on the left, swings out into the corridor
            door.hinge = new Vector3(-DOOR_W * 0.5f, DOOR_H * 0.5f, 0f);
            door.swingAngle = -100f;
            door.openTime = 2.2f;
            door.statusLamp = lamp.GetComponent<Renderer>();
            door.statusLight = lampLight;
            return door;
        }

        // ============================================================ key rack + keys

        static Transform[] BuildRack(Transform p)
        {
            var rack = Group("KeyRack", p);
            rack.localPosition = new Vector3(RACK_X, 0f, FRONT);

            Cube("Board", new Vector3(0f, 1.70f, -0.025f), new Vector3(1.95f, 0.30f, 0.05f), mWood, rack);
            Cube("Trim_Top", new Vector3(0f, 1.855f, -0.03f), new Vector3(1.99f, 0.025f, 0.06f), mDark, rack, false);
            Cube("Trim_Bottom", new Vector3(0f, 1.545f, -0.03f), new Vector3(1.99f, 0.025f, 0.06f), mDark, rack, false);

            var slots = new Transform[6];
            for (int i = 0; i < 6; i++)
            {
                float x = -0.75f + 0.3f * i;
                CylZ("Hook_" + i, new Vector3(x, 1.625f, -0.095f), 0.022f, 0.09f, mHook, rack);
                Cube("HookTip_" + i, new Vector3(x, 1.643f, -0.135f), new Vector3(0.02f, 0.045f, 0.02f), mHook, rack, false);

                var s = new GameObject("Slot_" + i).transform;
                s.SetParent(rack, false);
                s.localPosition = new Vector3(x, 1.625f, -0.125f);
                s.localRotation = Quaternion.Euler(0f, 180f, 0f);   // forward points into the room
                slots[i] = s;
            }

            // hook numbers: press 1-6 on the keyboard to pick the key on that hook
            for (int i = 0; i < 6; i++)
                Text("HookNumber_" + (i + 1), rack, new Vector3(-0.75f + 0.3f * i, 1.79f, -0.052f), new Vector2(0.12f, 0.08f),
                     (i + 1).ToString(), InkDark, TextAlignmentOptions.Center, true);
            Text("Label_Small", rack, new Vector3(-0.75f, 1.945f, -0.01f), new Vector2(0.42f, 0.065f), "SMALLEST", InkDark, TextAlignmentOptions.Left, true);
            Text("Label_Large", rack, new Vector3(0.75f, 1.945f, -0.01f), new Vector2(0.42f, 0.065f), "LARGEST", InkDark, TextAlignmentOptions.Right, true);
            Cube("Arrow_Line", new Vector3(0.02f, 1.945f, -0.008f), new Vector3(0.56f, 0.012f, 0.006f), mInk, rack, false);
            var h1 = Cube("Arrow_Head_A", new Vector3(0.285f, 1.952f, -0.008f), new Vector3(0.05f, 0.012f, 0.006f), mInk, rack, false);
            h1.transform.localRotation = Quaternion.Euler(0f, 0f, -35f);
            var h2 = Cube("Arrow_Head_B", new Vector3(0.285f, 1.938f, -0.008f), new Vector3(0.05f, 0.012f, 0.006f), mInk, rack, false);
            h2.transform.localRotation = Quaternion.Euler(0f, 0f, 35f);
            return slots;
        }

        static List<SortItem> BuildKeys(Transform p, Transform[] slots)
        {
            var parent = Group("Keys", p);
            var list = new List<SortItem>();
            for (int i = 0; i < slots.Length; i++)
            {
                var keyRoot = new GameObject("Key_" + i);
                keyRoot.transform.SetParent(parent, false);
                keyRoot.transform.position = slots[i].position;

                var swing = Group("Swing", keyRoot.transform);
                var model = KeyModel(swing);
                Cube("String", new Vector3(0f, -0.262f, 0f), new Vector3(0.004f, 0.03f, 0.004f), mInk, swing, false);
                var tag = Cube("Tag", new Vector3(0f, -0.31f, 0f), new Vector3(0.115f, 0.085f, 0.012f), mTag, swing, false);
                var num = Text("Number", swing, new Vector3(0f, -0.31f, -0.0075f), new Vector2(0.10f, 0.072f),
                               PREVIEW_ORDER[i].ToString(), InkDark, TextAlignmentOptions.Center, true);

                var bc = keyRoot.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, -0.17f, 0f);
                bc.size = new Vector3(0.22f, 0.42f, 0.14f);

                var item = keyRoot.AddComponent<SortItem>();
                item.value = PREVIEW_ORDER[i];
                item.label = num;
                item.rend = model.GetComponentInChildren<Renderer>();
                item.tagRenderer = tag.GetComponent<Renderer>();
                item.swing = swing;
                list.Add(item);
            }
            return list;
        }

        /// The KayKit key lies along its X axis; turn it so the bow is up and the bit hangs down.
        static GameObject KeyModel(Transform swing)
        {
            Vector3 top = swing.position + Vector3.down * 0.004f;
            var w = Model(DUNGEON + "key.fbx", "Model", swing, top, new Vector3(0f, 0f, -90f), 0.24f, 1, Anchor.Top, mHook);
            if (WiderAtBottom(w))
            {
                Object.DestroyImmediate(w);
                w = Model(DUNGEON + "key.fbx", "Model", swing, top, new Vector3(0f, 0f, 90f), 0.24f, 1, Anchor.Top, mHook);
            }
            return w;
        }

        // ============================================================ terminal + sign

        static Terminal BuildTerminal(Transform p)
        {
            var g = Group("Terminal", p);
            g.localPosition = new Vector3(TERM_X, 0f, FRONT);

            Cube("Bezel", new Vector3(0f, 1.52f, -0.035f), new Vector3(1.06f, 0.74f, 0.07f), mDark, g);
            Cube("Screen", new Vector3(0f, 1.555f, -0.0715f), new Vector3(0.94f, 0.58f, 0.004f), mScreen, g, false);
            Cube("Screen_Accent", new Vector3(0f, 1.255f, -0.0715f), new Vector3(0.94f, 0.008f, 0.004f), mExit, g, false);
            var txt = Text("ScreenText", g, new Vector3(0f, 1.555f, -0.0745f), new Vector2(0.86f, 0.53f),
                           "<b>SECURITY TERMINAL</b>   FLOOR 1\n> ORDER UNLOCKS.\n> Arrange the keys from\n   smallest to largest.\n\nSTATUS   LOCKED",
                           new Color(0.55f, 1f, 0.72f), TextAlignmentOptions.TopLeft, false);

            Cube("Button_Plate", new Vector3(0.36f, 1.05f, -0.01f), new Vector3(0.24f, 0.2f, 0.02f), mDark, g, false);
            CylZ("Hint_Button", new Vector3(0.36f, 1.065f, -0.035f), 0.085f, 0.035f, mRed, g);
            Text("Hint_Label", g, new Vector3(0.36f, 0.98f, -0.0205f), new Vector2(0.18f, 0.035f), "HINT",
                 new Color(0.9f, 0.9f, 0.88f), TextAlignmentOptions.Center, true);

            var term = g.gameObject.AddComponent<Terminal>();
            term.screen = txt;
            var bc = g.gameObject.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.36f, -0.06f);
            bc.size = new Vector3(1.08f, 1.02f, 0.14f);

            PointLight("TerminalGlow", g, new Vector3(TERM_X, 1.55f, FRONT - 0.45f), new Color(0.35f, 1f, 0.72f), 0.7f, 2.4f, false);
            return term;
        }

        static void BuildSign(Transform p)
        {
            var g = Group("RuleSign", p);
            g.localPosition = new Vector3(RACK_X, 0f, FRONT);
            Cube("Edge", new Vector3(0f, 1.0f, -0.008f), new Vector3(1.56f, 0.34f, 0.016f), mInk, g, false);
            Cube("Plate", new Vector3(0f, 1.0f, -0.018f), new Vector3(1.5f, 0.28f, 0.01f), mSign, g, false);
            Text("Rule", g, new Vector3(0f, 1.045f, -0.0245f), new Vector2(1.40f, 0.085f), "ONLY NEIGHBOURING KEYS MAY SWAP",
                 InkDark, TextAlignmentOptions.Center, true);
            Text("How", g, new Vector3(0f, 0.957f, -0.0245f), new Vector2(1.40f, 0.06f), "Compare the glowing pair: swap if the left key is bigger, else stay.",
                 InkDark, TextAlignmentOptions.Center, false);
        }

        // ============================================================ props + lights

        static void BuildProps(Transform p)
        {
            var g = Group("Props", p);
            Prop(DUNGEON + "box_stacked.fbx", "Crates", g, new Vector3(2.3f, 0f, -2.75f), 15f, 1.15f);
            Prop(DUNGEON + "box_small.fbx", "Crate_Small", g, new Vector3(1.4f, 0f, -3.0f), -10f, 0.5f);
            Prop(DUNGEON + "barrel_small.fbx", "Barrel", g, new Vector3(-2.5f, 0f, -2.95f), 0f, 0.8f);

            // the chair the player fell off
            var chair = Model(DUNGEON + "chair.fbx", "Chair_Tipped", g, new Vector3(0.95f, 0f, -1.55f), new Vector3(0f, 35f, 88f), 1.0f, -1, Anchor.Bottom, mWood);
            Solid(chair);

            // desk with an old computer along the left wall
            var desk = Prop(SPACE + "table.fbx", "Desk", g, new Vector3(-2.5f, 0f, -0.6f), 90f, 0.78f);
            float top = WorldBounds(desk).max.y;
            Model(SPACE + "computer.fbx", "Computer", g, new Vector3(-2.55f, top, -0.6f), new Vector3(0f, 90f, 0f), 0.45f, 1, Anchor.Bottom, mDark);
        }

        static GameObject Prop(string path, string name, Transform parent, Vector3 pos, float yaw, float height)
        {
            var w = Model(path, name, parent, pos, new Vector3(0f, yaw, 0f), height, 1, Anchor.Bottom, mWood);
            Solid(w);
            return w;
        }

        static void BuildLights(Transform p)
        {
            var g = Group("Lights", p);
            Kit.CeilingPanel(g, "Panel_Front", new Vector3(0f, H, 1.1f), new Vector2(1.2f, 0.6f), mDark, mBulb, new Color(1f, 0.97f, 0.93f), 2.6f, 9f, true);
            Kit.CeilingPanel(g, "Panel_Back", new Vector3(0f, H, -1.7f), new Vector2(1.2f, 0.6f), mDark, mBulb, new Color(1f, 0.97f, 0.93f), 2.2f, 8f, false);
            Spot("RackSpot", g, new Vector3(RACK_X, H - 0.12f, FRONT - 1.0f), new Vector3(RACK_X, 1.45f, FRONT),
                 new Color(1f, 0.96f, 0.9f), 3.2f, 4.5f, 60f, true);
            PointLight("Fill_Left", g, new Vector3(-2.2f, 2.2f, 0f), new Color(0.96f, 0.97f, 1f), 0.9f, 6f, false);
            PointLight("Fill_Right", g, new Vector3(2.2f, 2.2f, -1f), new Color(0.96f, 0.97f, 1f), 0.9f, 6f, false);
        }

        // ============================================================ corridor + exit

        static void BuildCorridor(Transform p, LevelManager lm)
        {
            var g = Group("Corridor", p);
            float z0 = FRONT + T;     // far face of the front wall
            float len = 4f;
            float zc = z0 + len * 0.5f;
            float cw = 1.8f;

            Cube("Floor", new Vector3(0f, -T * 0.5f, zc), new Vector3(cw + 2f * T, T, len), mTread, g);
            Cube("Wall_L", new Vector3(-(cw * 0.5f + T * 0.5f), 3f, zc), new Vector3(T, 6f, len), mWall, g);
            Cube("Wall_R", new Vector3(cw * 0.5f + T * 0.5f, 3f, zc), new Vector3(T, 6f, len), mWall, g);
            Cube("Ceiling", new Vector3(0f, H + T * 0.5f, z0 + 0.9f), new Vector3(cw + 2f * T, T, 1.8f), mCeil, g);  // the stairwell above is open
            Cube("End", new Vector3(0f, 3f, z0 + len + T * 0.5f), new Vector3(cw + 2f * T, 6f, T), mWall, g);

            // stairs up to the next floor
            for (int k = 0; k < 7; k++)
            {
                float zk = z0 + 1.9f + 0.26f * k + 0.13f;
                float hk = 0.18f * (k + 1);
                Cube("Step_" + k, new Vector3(0f, hk * 0.5f, zk), new Vector3(cw, hk, 0.26f), mTread, g);
            }
            Cube("Landing", new Vector3(0f, 0.63f, z0 + len - 0.14f), new Vector3(cw, 1.26f, 0.28f), mTread, g);

            Cube("ExitSign", new Vector3(0f, 2.55f, z0 + len - 0.015f), new Vector3(0.9f, 0.28f, 0.03f), mExit, g, false);
            Text("ExitText", g, new Vector3(0f, 2.55f, z0 + len - 0.035f), new Vector2(0.8f, 0.2f), "FLOOR 2  ^",
                 Color.white, TextAlignmentOptions.Center, true);
            PointLight("ExitGlow", g, new Vector3(0f, 2.1f, z0 + len - 0.6f), new Color(0.45f, 1f, 0.6f), 1.1f, 5f, false);
            PointLight("CorridorLight", g, new Vector3(0f, 2.7f, z0 + 0.8f), new Color(1f, 0.97f, 0.92f), 2.0f, 6f, false);

            var trig = new GameObject("ExitTrigger");
            trig.transform.SetParent(g, false);
            trig.transform.position = new Vector3(0f, 1.1f, z0 + 1.4f);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(cw, 2.2f, 0.6f);
            var et = trig.AddComponent<ExitTrigger>();
            et.manager = lm;
        }

        // ============================================================ post-processing

        static void PostFx(Transform parent)
        {
            string path = ROOT + "Settings/Level1_PostFX.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(1.1f);
            bloom.scatter.Override(0.65f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.45f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.35f);
            color.contrast.Override(10f);
            color.saturation.Override(-8f);

            foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            var go = new GameObject("PostFX");
            go.transform.SetParent(parent, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.priority = 1f;
            vol.sharedProfile = profile;
        }

        // ============================================================ helpers

        static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static GameObject Cube(string name, Vector3 localPos, Vector3 scale, Material mat, Transform parent, bool collider = true)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static GameObject Sphere(string name, Vector3 localPos, float diameter, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// Cylinder lying along the local Z axis.
        static GameObject CylZ(string name, Vector3 localPos, float diameter, float length, Material mat, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        static TextMeshPro Text(string name, Transform parent, Vector3 localPos, Vector2 size, string text, Color color,
                                TextAlignmentOptions align, bool bold, float maxSize = 10f)
        {
            var go = new GameObject(name);
            var t = go.AddComponent<TextMeshPro>();
            t.rectTransform.SetParent(parent, false);
            if (font != null) t.font = font;
            t.text = text;
            t.color = color;
            t.alignment = align;
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.01f;
            t.fontSizeMax = maxSize;
            t.richText = true;
            if (bold) t.fontStyle = FontStyles.Bold;
            t.rectTransform.sizeDelta = size;
            t.rectTransform.localPosition = localPos;
            t.rectTransform.localRotation = Quaternion.identity;
            var mr = go.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
            return t;
        }

        static Light PointLight(string name, Transform parent, Vector3 worldPos, Color c, float intensity, float range, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            if (shadows)
            {
                // A point light needs 6 shadow maps. At the default High tier they plus the rack spot overflow
                // the 2048 atlas and URP logs a resolution warning every frame it happens. Medium fits.
                var so = new SerializedObject(l.GetUniversalAdditionalLightData());
                var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                if (tier != null) { tier.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            return l;
        }

        static Light Spot(string name, Transform parent, Vector3 worldPos, Vector3 target, Color c, float intensity, float range, float angle, bool shadows)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.rotation = Quaternion.LookRotation(target - worldPos);
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = c;
            l.intensity = intensity;
            l.range = range;
            l.spotAngle = angle;
            l.innerSpotAngle = angle * 0.55f;
            l.shadows = shadows ? LightShadows.Soft : LightShadows.None;
            return l;
        }

        /// Instantiates a model inside a wrapper, scales it so one dimension equals 'size'
        /// (axis 0/1/2 = x/y/z, -1 = largest) and moves it so its anchor sits at worldPos.
        static GameObject Model(string path, string name, Transform parent, Vector3 worldPos, Vector3 worldEuler,
                                float size, int axis, Anchor anchor, Material fallback)
        {
            var w = new GameObject(name);
            w.transform.SetParent(parent, false);
            w.transform.position = worldPos;
            w.transform.rotation = Quaternion.Euler(worldEuler);

            GameObject inst;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, w.transform);
                inst.transform.localPosition = Vector3.zero;
            }
            else
            {
                Debug.LogWarning("Sort or Stay: model not found, using a box instead: " + path);
                inst = GameObject.CreatePrimitive(PrimitiveType.Cube);
                inst.name = "MISSING_" + Path.GetFileNameWithoutExtension(path);
                inst.transform.SetParent(w.transform, false);
                Object.DestroyImmediate(inst.GetComponent<Collider>());
                if (fallback != null) inst.GetComponent<Renderer>().sharedMaterial = fallback;
            }

            Bounds b = WorldBounds(inst);
            float dim = axis < 0 ? Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)) : b.size[axis];
            if (dim > 1e-5f) inst.transform.localScale *= size / dim;

            b = WorldBounds(inst);
            Vector3 a = anchor == Anchor.Bottom ? new Vector3(b.center.x, b.min.y, b.center.z)
                      : anchor == Anchor.Top ? new Vector3(b.center.x, b.max.y, b.center.z)
                      : b.center;
            inst.transform.position += worldPos - a;

            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return w;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// Box collider around a prop so the player cannot walk through it.
        static void Solid(GameObject w)
        {
            Bounds b = WorldBounds(w);
            var bc = w.AddComponent<BoxCollider>();
            bc.center = w.transform.InverseTransformPoint(b.center);
            // the wrapper may be rotated; express the world AABB size in its local axes
            Vector3 lx = w.transform.InverseTransformVector(new Vector3(b.size.x, 0f, 0f));
            Vector3 ly = w.transform.InverseTransformVector(new Vector3(0f, b.size.y, 0f));
            Vector3 lz = w.transform.InverseTransformVector(new Vector3(0f, 0f, b.size.z));
            Vector3 size = new Vector3(Mathf.Abs(lx.x) + Mathf.Abs(ly.x) + Mathf.Abs(lz.x),
                               Mathf.Abs(lx.y) + Mathf.Abs(ly.y) + Mathf.Abs(lz.y),
                               Mathf.Abs(lx.z) + Mathf.Abs(ly.z) + Mathf.Abs(lz.z));
            bc.size = size * 0.9f;
        }

        /// True if the mesh is wider in its bottom third than its top third (the key's bow is the wide end).
        static bool WiderAtBottom(GameObject w)
        {
            var pts = new List<Vector3>();
            foreach (var mf in w.GetComponentsInChildren<MeshFilter>())
            {
                var mesh = mf.sharedMesh;
                if (mesh == null) continue;
                Vector3[] v;
                try { v = mesh.vertices; } catch { continue; }
                if (v == null) continue;
                foreach (var x in v) pts.Add(mf.transform.TransformPoint(x));
            }
            if (pts.Count < 8) return false;

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (var q in pts) { minY = Mathf.Min(minY, q.y); maxY = Mathf.Max(maxY, q.y); }
            float h = maxY - minY;
            if (h <= 1e-5f) return false;

            float tMin = float.MaxValue, tMax = float.MinValue, bMin = float.MaxValue, bMax = float.MinValue;
            foreach (var q in pts)
            {
                if (q.y > maxY - 0.3f * h) { tMin = Mathf.Min(tMin, q.x); tMax = Mathf.Max(tMax, q.x); }
                else if (q.y < minY + 0.3f * h) { bMin = Mathf.Min(bMin, q.x); bMax = Mathf.Max(bMax, q.x); }
            }
            if (tMax < tMin || bMax < bMin) return false;
            return (bMax - bMin) > (tMax - tMin) * 1.1f;
        }

        static void AddToBuildSettings(string scenePath)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == scenePath);
            list.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }
    }
}
