using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortOrStay.BasicDemo.EditorTools
{
    /// Builds the basic, playable Level 1 demo: floor, one wall with a doorway,
    /// a key rack with 6 hooks, 6 keys, a door with a status lamp, lights and a fixed camera.
    /// Flat colours only, no textures or decoration. Re-running it rebuilds the scene.
    public static class BuildBasicDemo
    {
        const string ROOT = "Assets/SortOrStay/BasicDemo";
        const string MATDIR = ROOT + "/Materials";
        const string SCENE = ROOT + "/Level1_BasicDemo.unity";
        const string KEY_FBX = "Assets/ThirdParty/SortOrStay/KayKit_DungeonRemastered/Models/key.fbx";

        const int KEYS = 6;
        const float SPACING = 0.5f;      // distance between hooks
        const float HOOK_Y = 1.9f;
        const float WALL_Z = 2.6f;       // wall centre; front face at WALL_Z - 0.1
        const float KEY_LEN = 0.4f;
        const float DOOR_X = 2.4f, DOOR_W = 1.1f, DOOR_H = 2.3f;

        [MenuItem("Sort or Stay/Build Basic Demo (Level 1 Bubble Sort)")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EnsureFolder(MATDIR);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var mFloor = Mat("Demo_Floor", new Color(0.34f, 0.34f, 0.36f), 0.15f);
            var mWall = Mat("Demo_Wall", new Color(0.46f, 0.47f, 0.50f), 0.1f);
            var mRack = Mat("Demo_Rack", new Color(0.45f, 0.30f, 0.17f), 0.25f);
            var mHook = Mat("Demo_Hook", new Color(0.22f, 0.22f, 0.24f), 0.6f);
            var mDoor = Mat("Demo_Door", new Color(0.52f, 0.55f, 0.60f), 0.5f);
            var mLamp = Mat("Demo_Lamp", new Color(1f, 0.12f, 0.08f), 0.3f, new Color(3f, 0.36f, 0.24f));

            // Lighting: dim room, one warm light over the rack.
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.24f, 0.24f, 0.27f);
            var sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 0.45f;
            sun.color = new Color(1f, 0.94f, 0.85f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(45f, -30f, 0f);
            var ceiling = new GameObject("Ceiling Light").AddComponent<Light>();
            ceiling.type = LightType.Point;
            ceiling.range = 8f;
            ceiling.intensity = 2.2f;
            ceiling.color = new Color(1f, 0.86f, 0.66f);
            ceiling.shadows = LightShadows.Soft;
            ceiling.transform.position = new Vector3(0.6f, 3.2f, 0.9f);

            // Room: a floor and one wall with a doorway. Nothing else.
            var room = new GameObject("Room").transform;
            Cube("Floor", new Vector3(0.5f, -0.05f, 0.5f), new Vector3(9f, 0.1f, 8f), mFloor, room, true);
            float wallLeftW = DOOR_X - (-4f);
            Cube("Wall_Left", new Vector3(-4f + wallLeftW * 0.5f, 1.75f, WALL_Z), new Vector3(wallLeftW, 3.5f, 0.2f), mWall, room, true);
            float rightStart = DOOR_X + DOOR_W, wallRightW = 5f - rightStart;
            Cube("Wall_Right", new Vector3(rightStart + wallRightW * 0.5f, 1.75f, WALL_Z), new Vector3(wallRightW, 3.5f, 0.2f), mWall, room, true);
            Cube("Wall_AboveDoor", new Vector3(DOOR_X + DOOR_W * 0.5f, (DOOR_H + 3.5f) * 0.5f, WALL_Z), new Vector3(DOOR_W, 3.5f - DOOR_H, 0.2f), mWall, room, true);

            // Key rack: a board with 6 hooks. Slots are the hook points.
            var rack = new GameObject("KeyRack").transform;
            float face = WALL_Z - 0.1f;
            Cube("Rack_Board", new Vector3(0f, HOOK_Y + 0.12f, face - 0.03f), new Vector3(KEYS * SPACING + 0.3f, 0.24f, 0.06f), mRack, rack, false);
            var slots = new Transform[KEYS];
            for (int i = 0; i < KEYS; i++)
            {
                float x = (i - (KEYS - 1) * 0.5f) * SPACING;
                var hook = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                hook.name = "Hook_" + (i + 1);
                Object.DestroyImmediate(hook.GetComponent<Collider>());
                hook.GetComponent<Renderer>().sharedMaterial = mHook;
                hook.transform.SetParent(rack, false);
                hook.transform.position = new Vector3(x, HOOK_Y + 0.02f, face - 0.12f);
                hook.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                hook.transform.localScale = new Vector3(0.03f, 0.07f, 0.03f);

                var slot = new GameObject("Slot_" + (i + 1)).transform;
                slot.SetParent(rack, false);
                slot.position = new Vector3(x, HOOK_Y, face - 0.17f);
                slots[i] = slot;
            }

            // Keys.
            var keyFbx = AssetDatabase.LoadAssetAtPath<GameObject>(KEY_FBX);
            if (keyFbx == null) Debug.LogWarning("[Sort or Stay] key.fbx not found at " + KEY_FBX + " - using cubes instead.");
            var keysParent = new GameObject("Keys").transform;
            var items = new List<DemoKey>();
            for (int i = 0; i < KEYS; i++)
            {
                var root = new GameObject("Key_" + (i + 1));
                root.transform.SetParent(keysParent, false);
                root.transform.position = slots[i].position;
                KeyModel(keyFbx, root.transform);

                var col = root.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, -KEY_LEN * 0.5f, 0f);
                col.size = new Vector3(0.3f, KEY_LEN + 0.06f, 0.24f);

                var key = root.AddComponent<DemoKey>();
                key.length = KEY_LEN;
                items.Add(key);
            }

            // Door: hinge on the left edge of the doorway, panel as a child.
            var hinge = new GameObject("Door");
            hinge.transform.position = new Vector3(DOOR_X, 0f, WALL_Z);
            var panel = Cube("Door_Panel", Vector3.zero, new Vector3(DOOR_W - 0.02f, DOOR_H - 0.02f, 0.1f), mDoor, hinge.transform, true);
            panel.transform.localPosition = new Vector3(DOOR_W * 0.5f, DOOR_H * 0.5f, 0f);
            var handle = Cube("Door_Handle", Vector3.zero, new Vector3(0.05f, 0.2f, 0.06f), mHook, hinge.transform, false);
            handle.transform.localPosition = new Vector3(DOOR_W - 0.15f, 1.05f, -0.09f);
            var door = hinge.AddComponent<DemoDoor>();

            var lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            lamp.name = "Door_Lamp";
            Object.DestroyImmediate(lamp.GetComponent<Collider>());
            lamp.GetComponent<Renderer>().sharedMaterial = mLamp;
            lamp.transform.position = new Vector3(DOOR_X + DOOR_W * 0.5f, DOOR_H + 0.22f, face - 0.03f);
            lamp.transform.localScale = Vector3.one * 0.1f;
            var lampLight = new GameObject("Lamp_Light").AddComponent<Light>();
            lampLight.type = LightType.Point;
            lampLight.range = 1.6f;
            lampLight.intensity = 1.5f;
            lampLight.color = new Color(1f, 0.12f, 0.08f);
            lampLight.transform.SetParent(lamp.transform, false);
            lampLight.transform.localPosition = new Vector3(0f, 0f, -1.5f);

            var beyond = new GameObject("Beyond_Door_Light").AddComponent<Light>();
            beyond.type = LightType.Point;
            beyond.range = 4f;
            beyond.intensity = 2f;
            beyond.color = new Color(0.8f, 1f, 0.85f);
            beyond.transform.position = new Vector3(DOOR_X + DOOR_W * 0.5f, 1.6f, WALL_Z + 1.4f);

            door.lamp = lamp.GetComponent<Renderer>();
            door.lampLight = lampLight;
            door.beyondLight = beyond;

            // Fixed camera that sees the rack and the door. Cursor stays free.
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 55f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.02f, 0.02f, 0.03f);
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(1.0f, 1.55f, -2.0f);
            camGo.transform.LookAt(new Vector3(1.0f, 1.45f, WALL_Z - 0.1f));

            // Game logic.
            var mgr = new GameObject("Level1_BasicDemo");
            var puzzle = mgr.AddComponent<DemoBubblePuzzle>();
            puzzle.slots = slots;
            puzzle.items = items;
            puzzle.door = door;
            var hud = mgr.AddComponent<DemoHUD>();
            hud.puzzle = puzzle;
            hud.cam = cam;
            var pointer = camGo.AddComponent<DemoPointer>();
            pointer.puzzle = puzzle;
            pointer.hud = hud;

            EditorSceneManager.SaveScene(scene, SCENE);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = mgr;
            Debug.Log("[Sort or Stay] Basic demo built: " + SCENE + "  (press Play; click a key, then its neighbour)");
        }

        /// The KayKit key lies along X. Stand it up with the bow on top, scale it to KEY_LEN,
        /// and put its top centre on the root (the hook).
        static void KeyModel(GameObject fbx, Transform root)
        {
            GameObject inst;
            if (fbx != null)
            {
                inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
                inst.transform.SetParent(root, false);
                inst.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
                if (WiderAtBottom(inst)) inst.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            else
            {
                inst = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(inst.GetComponent<Collider>());
                inst.transform.SetParent(root, false);
                inst.transform.localScale = new Vector3(0.08f, 1f, 0.03f);
            }
            inst.name = "Model";

            Bounds b = WorldBounds(inst);
            if (b.size.y > 1e-5f) inst.transform.localScale *= KEY_LEN / b.size.y;
            b = WorldBounds(inst);
            Vector3 topCentre = new Vector3(b.center.x, b.max.y, b.center.z);
            inst.transform.position += root.position - topCentre;
        }

        /// True if the lower half of the model is wider than the upper half (bow pointing down).
        static bool WiderAtBottom(GameObject go)
        {
            var pts = new List<Vector3>();
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(mf.transform.TransformPoint(v));
            }
            if (pts.Count == 0) return false;
            float minY = float.MaxValue, maxY = float.MinValue, cx = 0f;
            foreach (var p in pts) { minY = Mathf.Min(minY, p.y); maxY = Mathf.Max(maxY, p.y); cx += p.x; }
            cx /= pts.Count;
            float mid = (minY + maxY) * 0.5f, top = 0f, bottom = 0f;
            foreach (var p in pts)
            {
                float w = Mathf.Abs(p.x - cx);
                if (p.y >= mid) top = Mathf.Max(top, w); else bottom = Mathf.Max(bottom, w);
            }
            return bottom > top;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        static GameObject Cube(string name, Vector3 pos, Vector3 size, Material m, Transform parent, bool keepCollider)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = name;
            if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.GetComponent<Renderer>().sharedMaterial = m;
            g.transform.SetParent(parent, false);
            g.transform.position = pos;
            g.transform.localScale = size;
            return g;
        }

        static Material Mat(string name, Color c, float smoothness, Color? emission = null)
        {
            string path = MATDIR + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smoothness);
            if (emission.HasValue)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission.Value);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }
    }
}
