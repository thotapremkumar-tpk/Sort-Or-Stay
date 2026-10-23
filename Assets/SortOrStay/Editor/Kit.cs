using System.Collections.Generic;
using System.IO;
using System.Linq;
using SortOrStay;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SortOrStayEditor
{
    /// Shared building blocks for the level builders: materials, primitives, walls with openings,
    /// bright daytime lighting, the realistic player avatar rig, HUD/systems and the stairwell exit.
    public static class Kit
    {
        public const string ROOT = "Assets/SortOrStay/";
        public const string TP = "Assets/ThirdParty/SortOrStay/";
        public const string DUNGEON = TP + "KayKit_DungeonRemastered/Models/";
        public const string FURN = TP + "KayKit_FurnitureBits/Models/";
        public const string SPACE = TP + "Kenney_SpaceStationKit/Models/";
        public const string CONVEYOR = TP + "Kenney_ConveyorKit/Models/";
        public const string TEX = TP + "Textures/";
        public const string MATDIR = ROOT + "Materials/";
        public const string PLAYER_DIR = ROOT + "Player/";
        public const string RB = RocketboxImport.ROOT;
        const string TMP_FONT = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        public static readonly Color SkyColor = new Color(0.74f, 0.85f, 0.97f);
        public static readonly Color InkDark = new Color(0.08f, 0.08f, 0.09f);

        public static TMP_FontAsset font;

        public enum Anchor { Bottom, Top, Center }

        public struct Hole
        {
            public float center, width, bottom, top;
            public Hole(float center, float width, float bottom, float top) { this.center = center; this.width = width; this.bottom = bottom; this.top = top; }
        }

        public static void Begin()
        {
            Directory.CreateDirectory(ROOT + "Scenes");
            Directory.CreateDirectory(MATDIR);
            Directory.CreateDirectory(ROOT + "Settings");
            Directory.CreateDirectory(PLAYER_DIR + "Materials");
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(TMP_FONT);
        }

        // ============================================================ materials

        public static Shader URPLit => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");

        public static Material Flat(string name, Color c, float smooth, float metallic)
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

        public static Material Emissive(string name, Color baseColor, Color emission, float strength)
        {
            var m = Flat(name, baseColor, 0.5f, 0f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", emission * strength);
            EditorUtility.SetDirty(m);
            return m;
        }

        /// ambientCG material: Color + NormalGL, tiled.
        public static Material Pbr(string name, string id, Color tint, float tiling, float smooth, float metallic)
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

        /// Clear glass (alpha-blended, no shadows).
        public static Material Glass(string name, Color tint, float alpha)
        {
            var m = Flat(name, new Color(tint.r, tint.g, tint.b, alpha), 0.95f, 0f);
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_SrcBlendAlpha")) m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (m.HasProperty("_DstBlendAlpha")) m.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)RenderQueue.Transparent;
            m.SetShaderPassEnabled("DepthOnly", false);
            m.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(m);
            return m;
        }

        // ============================================================ primitives

        public static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static GameObject Cube(string name, Vector3 localPos, Vector3 scale, Material mat, Transform parent, bool collider = true)
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

        public static GameObject Sphere(string name, Vector3 localPos, float diameter, Material mat, Transform parent)
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

        /// Cylinder standing upright (local Y).
        public static GameObject CylY(string name, Vector3 localPos, float diameter, float height, Material mat, Transform parent, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter);
            go.GetComponent<Renderer>().sharedMaterial = mat;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        /// Cylinder lying along local Z.
        public static GameObject CylZ(string name, Vector3 localPos, float diameter, float length, Material mat, Transform parent)
        {
            var go = CylY(name, localPos, diameter, length, mat, parent);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return go;
        }

        /// Cylinder lying along local X.
        public static GameObject CylX(string name, Vector3 localPos, float diameter, float length, Material mat, Transform parent)
        {
            var go = CylY(name, localPos, diameter, length, mat, parent);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            return go;
        }

        /// TextMeshPro facing -Z of its parent (readable from the -Z side).
        public static TextMeshPro Text(string name, Transform parent, Vector3 localPos, Vector2 size, string text, Color color,
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

        /// Text painted on the floor, readable by someone standing on its -Z side and looking +Z.
        public static TextMeshPro FloorText(string name, Transform parent, Vector3 localPos, Vector2 size, string text, Color color, bool bold = true)
        {
            var t = Text(name, parent, localPos, size, text, color, TextAlignmentOptions.Center, bold);
            t.rectTransform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return t;
        }

        public static Light PointLight(string name, Transform parent, Vector3 worldPos, Color c, float intensity, float range, bool shadows)
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
                var so = new SerializedObject(l.GetUniversalAdditionalLightData());
                var tier = so.FindProperty("m_AdditionalLightsShadowResolutionTier");
                if (tier != null) { tier.intValue = 1; so.ApplyModifiedPropertiesWithoutUndo(); }
            }
            return l;
        }

        public static Light Spot(string name, Transform parent, Vector3 worldPos, Vector3 target, Color c, float intensity, float range, float angle, bool shadows)
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

        public static Light Sun(Transform parent, Vector3 euler, Color c, float intensity)
        {
            var go = new GameObject("Sun");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(euler);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = c;
            l.intensity = intensity;
            l.shadows = LightShadows.Soft;
            l.shadowStrength = 0.9f;
            l.shadowBias = 0.04f;
            l.shadowNormalBias = 0.3f;
            return l;
        }

        /// Instantiates a model inside a wrapper, scales it so one dimension equals 'size'
        /// (axis 0/1/2 = x/y/z, -1 = largest) and moves it so its anchor sits at worldPos.
        public static GameObject Model(string path, string name, Transform parent, Vector3 worldPos, Vector3 worldEuler,
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

        public static GameObject Prop(string path, string name, Transform parent, Vector3 pos, float yaw, float height, bool solid = true)
        {
            var w = Model(path, name, parent, pos, new Vector3(0f, yaw, 0f), height, 1, Anchor.Bottom, null);
            if (solid) Solid(w);
            return w;
        }

        public static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.one * 0.1f);
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            return b;
        }

        /// Box collider around a prop so the player cannot walk through it.
        public static void Solid(GameObject w)
        {
            Bounds b = WorldBounds(w);
            var bc = w.AddComponent<BoxCollider>();
            bc.center = w.transform.InverseTransformPoint(b.center);
            Vector3 lx = w.transform.InverseTransformVector(new Vector3(b.size.x, 0f, 0f));
            Vector3 ly = w.transform.InverseTransformVector(new Vector3(0f, b.size.y, 0f));
            Vector3 lz = w.transform.InverseTransformVector(new Vector3(0f, 0f, b.size.z));
            Vector3 size = new Vector3(Mathf.Abs(lx.x) + Mathf.Abs(ly.x) + Mathf.Abs(lz.x),
                               Mathf.Abs(lx.y) + Mathf.Abs(ly.y) + Mathf.Abs(lz.y),
                               Mathf.Abs(lx.z) + Mathf.Abs(ly.z) + Mathf.Abs(lz.z));
            bc.size = size * 0.92f;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        public static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayer(c.gameObject, layer);
        }

        public static void AddToBuildSettings(string scenePath, int index)
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == scenePath);
            list.Insert(Mathf.Clamp(index, 0, list.Count), new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ============================================================ walls

        /// Wall running along X at depth z (centre of its thickness), from x0 to x1, with rectangular holes.
        public static void WallAlongX(Transform p, string name, float z, float x0, float x1, float height, float thick, Material m, params Hole[] holes)
        {
            var g = Group(name, p);
            float cursor = x0;
            int k = 0;
            foreach (var h in holes.OrderBy(h => h.center))
            {
                float a = h.center - h.width * 0.5f, b = h.center + h.width * 0.5f;
                if (a > cursor + 0.001f) Cube("Seg" + k++, new Vector3((cursor + a) * 0.5f, height * 0.5f, z), new Vector3(a - cursor, height, thick), m, g);
                if (h.bottom > 0.001f) Cube("Below" + k++, new Vector3(h.center, h.bottom * 0.5f, z), new Vector3(h.width, h.bottom, thick), m, g);
                if (h.top < height - 0.001f) Cube("Above" + k++, new Vector3(h.center, (h.top + height) * 0.5f, z), new Vector3(h.width, height - h.top, thick), m, g);
                cursor = b;
            }
            if (x1 > cursor + 0.001f) Cube("Seg" + k, new Vector3((cursor + x1) * 0.5f, height * 0.5f, z), new Vector3(x1 - cursor, height, thick), m, g);
        }

        /// Wall running along Z at x (centre of its thickness), from z0 to z1, with rectangular holes.
        public static void WallAlongZ(Transform p, string name, float x, float z0, float z1, float height, float thick, Material m, params Hole[] holes)
        {
            var g = Group(name, p);
            float cursor = z0;
            int k = 0;
            foreach (var h in holes.OrderBy(h => h.center))
            {
                float a = h.center - h.width * 0.5f, b = h.center + h.width * 0.5f;
                if (a > cursor + 0.001f) Cube("Seg" + k++, new Vector3(x, height * 0.5f, (cursor + a) * 0.5f), new Vector3(thick, height, a - cursor), m, g);
                if (h.bottom > 0.001f) Cube("Below" + k++, new Vector3(x, h.bottom * 0.5f, h.center), new Vector3(thick, h.bottom, h.width), m, g);
                if (h.top < height - 0.001f) Cube("Above" + k++, new Vector3(x, (h.top + height) * 0.5f, h.center), new Vector3(thick, height - h.top, h.width), m, g);
                cursor = b;
            }
            if (z1 > cursor + 0.001f) Cube("Seg" + k, new Vector3(x, height * 0.5f, (cursor + z1) * 0.5f), new Vector3(thick, height, z1 - cursor), m, g);
        }

        /// A window in a wall that runs along Z: frame, mullions and a glass pane. 'inward' = +1 if the room is at larger x.
        public static void WindowZ(Transform p, string name, float x, float zc, float width, float bottom, float top, float thick,
                                   Material frame, Material glass, Material sill)
        {
            var g = Group(name, p);
            float h = top - bottom, yc = (top + bottom) * 0.5f;
            float f = 0.07f;
            Cube("Frame_Top", new Vector3(x, top - f * 0.5f, zc), new Vector3(thick + 0.02f, f, width), frame, g, false);
            Cube("Frame_Bottom", new Vector3(x, bottom + f * 0.5f, zc), new Vector3(thick + 0.02f, f, width), frame, g, false);
            Cube("Frame_A", new Vector3(x, yc, zc - width * 0.5f + f * 0.5f), new Vector3(thick + 0.02f, h, f), frame, g, false);
            Cube("Frame_B", new Vector3(x, yc, zc + width * 0.5f - f * 0.5f), new Vector3(thick + 0.02f, h, f), frame, g, false);
            Cube("Mullion_V", new Vector3(x, yc, zc), new Vector3(thick * 0.6f, h, 0.04f), frame, g, false);
            Cube("Mullion_H", new Vector3(x, bottom + h * 0.62f, zc), new Vector3(thick * 0.6f, 0.04f, width), frame, g, false);
            var pane = Cube("Glass", new Vector3(x, yc, zc), new Vector3(0.012f, h, width), glass, g);
            pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (sill != null) Cube("Sill", new Vector3(x, bottom - 0.03f, zc), new Vector3(thick + 0.16f, 0.05f, width + 0.1f), sill, g, false);
        }

        /// Same as WindowZ for a wall that runs along X.
        public static void WindowX(Transform p, string name, float z, float xc, float width, float bottom, float top, float thick,
                                   Material frame, Material glass, Material sill)
        {
            var g = Group(name, p);
            float h = top - bottom, yc = (top + bottom) * 0.5f;
            float f = 0.07f;
            Cube("Frame_Top", new Vector3(xc, top - f * 0.5f, z), new Vector3(width, f, thick + 0.02f), frame, g, false);
            Cube("Frame_Bottom", new Vector3(xc, bottom + f * 0.5f, z), new Vector3(width, f, thick + 0.02f), frame, g, false);
            Cube("Frame_A", new Vector3(xc - width * 0.5f + f * 0.5f, yc, z), new Vector3(f, h, thick + 0.02f), frame, g, false);
            Cube("Frame_B", new Vector3(xc + width * 0.5f - f * 0.5f, yc, z), new Vector3(f, h, thick + 0.02f), frame, g, false);
            Cube("Mullion_V", new Vector3(xc, yc, z), new Vector3(0.04f, h, thick * 0.6f), frame, g, false);
            var pane = Cube("Glass", new Vector3(xc, yc, z), new Vector3(width, h, 0.012f), glass, g);
            pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (sill != null) Cube("Sill", new Vector3(xc, bottom - 0.03f, z), new Vector3(width + 0.1f, 0.05f, thick + 0.16f), sill, g, false);
        }

        // ============================================================ bright daytime look

        public static void BrightEnvironment(Color sky, Color equator, Color ground)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = sky;
            RenderSettings.ambientEquatorColor = equator;
            RenderSettings.ambientGroundColor = ground;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = false;
            RenderSettings.reflectionIntensity = 0.6f;
            RenderSettings.sun = null;
        }

        public static void BrightPostFx(Transform parent, string assetName, float exposure)
        {
            string path = ROOT + "Settings/" + assetName + ".asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(path) != null) AssetDatabase.DeleteAsset(path);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);

            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.Neutral);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(exposure);
            color.contrast.Override(6f);
            color.saturation.Override(8f);
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.15f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.6f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.5f);

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

        /// A bright LED ceiling panel with its light.
        public static void CeilingPanel(Transform p, string name, Vector3 pos, Vector2 size, Material housing, Material diffuser,
                                        Color c, float intensity, float range, bool shadows)
        {
            var g = Group(name, p);
            g.position = pos;
            Cube("Housing", new Vector3(0f, -0.02f, 0f), new Vector3(size.x + 0.06f, 0.04f, size.y + 0.06f), housing, g, false);
            Cube("Diffuser", new Vector3(0f, -0.042f, 0f), new Vector3(size.x, 0.008f, size.y), diffuser, g, false);
            PointLight("Light", g, pos + Vector3.down * 0.35f, c, intensity, range, shadows);
        }

        // ============================================================ player characters

        static readonly string[] CharNames = { "Male_Adult_10", "Female_Adult_04" };
        static readonly string[] CharCodes = { "m024", "f004" };
        static readonly string[] DisplayNames = { "Alex", "Maya" };
        static readonly string[] AnimStems = { "idle_neutral_01", "walk_neutral_01", "run_neutral_01", "cheer_01", "idle_scratch_head_01", "try_door_outwards", "idle_look_around_01" };
        const string MALE_CTRL = PLAYER_DIR + "Player_Male.controller";
        const string FEMALE_CTRL = PLAYER_DIR + "Player_Female.overrideController";

        static string CharFbx(int i) => RB + "Characters/" + CharNames[i] + "/" + CharNames[i] + ".fbx";
        static string AnimFbx(string stem) => RB + "Animations/" + stem + ".max.fbx";

        [MenuItem("Sort or Stay/Setup Player Characters (re-import)")]
        public static void SetupCharactersMenu()
        {
            Begin();
            EnsureCharacters(true);
            Debug.Log("Sort or Stay: player characters set up. Rebuild the levels to use them.");
        }

        public static bool EnsureCharacters(bool force = false)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx(0)) == null)
            {
                Debug.LogError("Sort or Stay: Rocketbox characters are missing from " + RB + ". The player falls back to a capsule.");
                return false;
            }

            // make sure the import settings from RocketboxImport are applied
            var paths = new List<string>();
            for (int i = 0; i < CharNames.Length; i++) paths.Add(CharFbx(i));
            foreach (var g in new[] { "m_", "f_" }) foreach (var s in AnimStems) paths.Add(AnimFbx(g + s));
            foreach (var path in paths)
            {
                var mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) { Debug.LogWarning("Sort or Stay: missing " + path); continue; }
                bool isAnim = path.Contains("/Animations/");
                bool configured = mi.animationType == ModelImporterAnimationType.Generic && mi.motionNodeName == "Bip01"
                                  && (!isAnim || (mi.clipAnimations.Length == 1 && mi.clipAnimations[0].name != "Take 001"));
                if (force || !configured) AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { RB.TrimEnd('/') }))
            {
                string tp = AssetDatabase.GUIDToAssetPath(guid);
                var ti = AssetImporter.GetAtPath(tp) as TextureImporter;
                if (ti != null && tp.ToLowerInvariant().Contains("_normal") && ti.textureType != TextureImporterType.NormalMap)
                    AssetDatabase.ImportAsset(tp, ImportAssetOptions.ForceUpdate);
            }

            for (int i = 0; i < CharNames.Length; i++) CharacterMaterials(i);

            if (force)
            {
                AssetDatabase.DeleteAsset(FEMALE_CTRL);
                AssetDatabase.DeleteAsset(MALE_CTRL);
            }
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(MALE_CTRL) == null) BuildControllers();
            AssetDatabase.SaveAssets();
            return true;
        }

        static Material[] CharacterMaterials(int i)
        {
            string tex = RB + "Characters/" + CharNames[i] + "/Textures/" + CharCodes[i];
            string pre = PLAYER_DIR + "Materials/" + CharNames[i];
            var body = CharMat(pre + "_Body.mat", tex + "_body_color.jpg", tex + "_body_normal.png", false, 0.32f);
            var head = CharMat(pre + "_Head.mat", tex + "_head_color.jpg", tex + "_head_normal.png", false, 0.38f);
            var hair = CharMat(pre + "_Hair.mat", tex + "_opacity_color.png", null, true, 0.25f);
            return new[] { body, head, hair };
        }

        static Material CharMat(string path, string colorTex, string normalTex, bool cutout, float smooth)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(URPLit); AssetDatabase.CreateAsset(m, path); }
            m.shader = URPLit;
            var col = AssetDatabase.LoadAssetAtPath<Texture2D>(colorTex);
            if (col != null) m.SetTexture("_BaseMap", col);
            else Debug.LogWarning("Sort or Stay: missing " + colorTex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", smooth);
            m.SetFloat("_Metallic", 0f);
            if (normalTex != null)
            {
                var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normalTex);
                if (n != null) { m.SetTexture("_BumpMap", n); m.EnableKeyword("_NORMALMAP"); m.SetFloat("_BumpScale", 1f); }
            }
            if (cutout)
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.42f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetFloat("_Cull", 0f);                         // hair cards are seen from both sides
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            EditorUtility.SetDirty(m);
            return m;
        }

        static AnimationClip Clip(string stem)
        {
            return AssetDatabase.LoadAllAssetsAtPath(AnimFbx(stem)).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        }

        static void BuildControllers()
        {
            var ctrl = AnimatorController.CreateAnimatorControllerAtPath(MALE_CTRL);
            ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
            string[] gestures = { PlayerController.GestureCheer, PlayerController.GestureScratch, PlayerController.GestureTryDoor, PlayerController.GestureLookAround };
            string[] gestureClips = { "m_cheer_01", "m_idle_scratch_head_01", "m_try_door_outwards", "m_idle_look_around_01" };
            foreach (var g in gestures) ctrl.AddParameter(g, AnimatorControllerParameterType.Trigger);

            var sm = ctrl.layers[0].stateMachine;
            var loco = ctrl.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(Clip("m_idle_neutral_01"), 0f);
            tree.AddChild(Clip("m_walk_neutral_01"), 1.45f);
            tree.AddChild(Clip("m_run_neutral_01"), 3.3f);
            var ch = tree.children;
            ch[1].timeScale = 1.45f;     // the walk clip covers ~1.0 m/s; the player walks at 1.45 m/s
            ch[2].timeScale = 1.14f;     // the run clip covers ~2.9 m/s; the player runs at 3.3 m/s
            tree.children = ch;
            EditorUtility.SetDirty(tree);
            sm.defaultState = loco;

            for (int i = 0; i < gestures.Length; i++)
            {
                var st = sm.AddState(gestures[i], new Vector3(300f, 120f * i, 0f));
                st.motion = Clip(gestureClips[i]);
                var t = sm.AddAnyStateTransition(st);
                t.AddCondition(AnimatorConditionMode.If, 0f, gestures[i]);
                t.hasExitTime = false;
                t.duration = 0.2f;
                t.canTransitionToSelf = false;

                var back = st.AddTransition(loco);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.25f;

                var interrupt = st.AddTransition(loco);
                interrupt.AddCondition(AnimatorConditionMode.Greater, 0.4f, "Speed");
                interrupt.hasExitTime = false;
                interrupt.duration = 0.2f;
            }
            EditorUtility.SetDirty(ctrl);

            var oc = new AnimatorOverrideController(ctrl);
            var pairs = new List<KeyValuePair<AnimationClip, AnimationClip>>();
            oc.GetOverrides(pairs);
            for (int i = 0; i < pairs.Count; i++)
            {
                var orig = pairs[i].Key;
                if (orig == null || !orig.name.StartsWith("m_")) continue;
                var f = Clip("f_" + orig.name.Substring(2));
                pairs[i] = new KeyValuePair<AnimationClip, AnimationClip>(orig, f);
            }
            oc.ApplyOverrides(pairs);
            AssetDatabase.CreateAsset(oc, FEMALE_CTRL);
            AssetDatabase.SaveAssets();
        }

        /// The player: character controller, over-the-shoulder camera and both avatars (one active).
        public static PlayerController BuildPlayer(Vector3 pos, float yaw)
        {
            bool haveChars = EnsureCharacters();
            var male = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(MALE_CTRL);
            var female = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(FEMALE_CTRL);

            var player = new GameObject("Player");
            player.transform.position = pos;
            var cc = player.AddComponent<CharacterController>();
            cc.height = 1.8f; cc.radius = 0.28f; cc.center = new Vector3(0f, 0.9f, 0f);
            cc.stepOffset = 0.3f; cc.skinWidth = 0.03f; cc.slopeLimit = 50f;

            var pivot = Group("CameraPivot", player.transform);
            pivot.localPosition = new Vector3(0f, 1.55f, 0f);
            pivot.localRotation = Quaternion.Euler(14f, yaw, 0f);
            var camGo = new GameObject("PlayerCamera");
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(pivot, false);
            camGo.transform.localPosition = new Vector3(0.6f, 0.12f, -1.9f);
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 62f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = SkyColor;
            camGo.AddComponent<AudioListener>();
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            int n = CharNames.Length;
            var avatars = new Transform[n];
            var anims = new Animator[n];
            var holds = new Transform[n];
            var roots = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var wrap = Group("Avatar_" + DisplayNames[i], player.transform);
                wrap.localPosition = Vector3.zero;
                avatars[i] = wrap;
                var fbx = haveChars ? AssetDatabase.LoadAssetAtPath<GameObject>(CharFbx(i)) : null;
                GameObject inst;
                if (fbx != null)
                {
                    inst = (GameObject)PrefabUtility.InstantiatePrefab(fbx, wrap);
                    inst.transform.localPosition = Vector3.zero;
                    inst.transform.localRotation = Quaternion.identity;

                    // face +Z: the character's left thigh minus its right thigh points to its left
                    var lt = FindDeep(inst.transform, "Bip01 L Thigh");
                    var rt = FindDeep(inst.transform, "Bip01 R Thigh");
                    if (lt != null && rt != null)
                    {
                        Vector3 left = lt.position - rt.position; left.y = 0f;
                        if (left.sqrMagnitude > 1e-6f)
                        {
                            Vector3 fwd = Vector3.Cross(Vector3.up, left.normalized);
                            inst.transform.Rotate(0f, Vector3.SignedAngle(fwd, Vector3.forward, Vector3.up), 0f, Space.World);
                        }
                    }

                    Bounds b = WorldBounds(inst);
                    if (b.size.y < 1.4f || b.size.y > 2.2f) inst.transform.localScale *= (i == 0 ? 1.78f : 1.68f) / Mathf.Max(0.01f, b.size.y);
                    b = WorldBounds(inst);
                    inst.transform.position += Vector3.up * (wrap.position.y - b.min.y);
                    var pelvis = FindDeep(inst.transform, "Bip01 Pelvis");
                    if (pelvis != null)
                    {
                        Vector3 d = pelvis.position - wrap.position; d.y = 0f;
                        inst.transform.position -= d;
                    }

                    var mats = CharacterMaterials(i);
                    foreach (var smr in inst.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        var sm = smr.sharedMaterials;
                        for (int k = 0; k < sm.Length; k++)
                        {
                            string nm = sm[k] != null ? sm[k].name.ToLowerInvariant() : "";
                            sm[k] = nm.Contains("opac") ? mats[2] : nm.Contains("head") ? mats[1] : mats[0];
                        }
                        smr.sharedMaterials = sm;
                        smr.updateWhenOffscreen = true;
                        smr.shadowCastingMode = ShadowCastingMode.On;
                    }

                    var anim = inst.GetComponent<Animator>();
                    if (anim == null) anim = inst.AddComponent<Animator>();
                    if (anim.avatar == null) anim.avatar = AssetDatabase.LoadAllAssetsAtPath(CharFbx(i)).OfType<Avatar>().FirstOrDefault();
                    anim.runtimeAnimatorController = i == 0 ? male : (female != null ? female : male);
                    anim.applyRootMotion = false;
                    anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    anims[i] = anim;
                    roots[i] = FindDeep(inst.transform, "Bip01");
                }
                else
                {
                    inst = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    inst.name = "PlaceholderBody";
                    inst.transform.SetParent(wrap, false);
                    inst.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                    Object.DestroyImmediate(inst.GetComponent<Collider>());
                }

                var hold = Group("HoldPoint", wrap);
                hold.localPosition = new Vector3(0f, 1.08f, 0.36f);
                holds[i] = hold;
                wrap.rotation = Quaternion.Euler(0f, yaw, 0f);
                wrap.gameObject.SetActive(i == 0);
            }

            SetLayer(player, 2);   // Ignore Raycast: the aim ray and the camera collision pass through the player

            var pc = player.AddComponent<PlayerController>();
            pc.cameraPivot = pivot;
            pc.cam = cam;
            pc.avatars = avatars;
            pc.animators = anims;
            pc.holdPoints = holds;
            pc.rootBones = roots;
            pc.avatarNames = (string[])DisplayNames.Clone();
            pc.startYaw = yaw;
            pc.camDistance = 1.9f;
            pc.shoulder = new Vector2(0.6f, 0.12f);
            pc.minPitch = -45f;
            pc.maxPitch = 60f;

            var interactor = player.AddComponent<PlayerInteractor>();
            interactor.cam = cam;
            interactor.controller = pc;
            interactor.marker = BuildHoverMarker();
            return pc;
        }

        /// A bouncing yellow chevron that floats above whatever the pointer is on.
        static Transform BuildHoverMarker()
        {
            var mat = Emissive("Hover_Marker", new Color(1f, 0.78f, 0.15f), new Color(1f, 0.7f, 0.1f), 2.2f);
            var g = new GameObject("HoverMarker").transform;
            var a = Cube("Arm_L", new Vector3(-0.055f, 0.06f, 0f), new Vector3(0.035f, 0.17f, 0.035f), mat, g, false);
            a.transform.localRotation = Quaternion.Euler(0f, 0f, 40f);
            var b = Cube("Arm_R", new Vector3(0.055f, 0.06f, 0f), new Vector3(0.035f, 0.17f, 0.035f), mat, g, false);
            b.transform.localRotation = Quaternion.Euler(0f, 0f, -40f);
            foreach (var r in g.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
            SetLayer(g.gameObject, 2);
            g.gameObject.SetActive(false);
            return g;
        }

        // ============================================================ HUD + systems

        public static GameUI BuildUI(Transform sys, string title, int floor)
        {
            var ui = sys.gameObject.AddComponent<GameUI>();
            ui.levelTitle = title;
            ui.floor = floor;
            return ui;
        }

        /// Short corridor beyond a door in the front wall (+Z), with stairs up, an exit sign and the exit trigger.
        public static void BuildStairwell(Transform p, float zWallOuter, float roomHeight, float width, LevelManager lm, string sign,
                                          Material floor, Material wall, Material ceil, Material tread, Material exitMat)
        {
            var g = Group("Stairwell", p);
            float z0 = zWallOuter;
            float len = 4.2f;
            float zc = z0 + len * 0.5f;
            float T = 0.2f;

            Cube("Floor", new Vector3(0f, -T * 0.5f, zc), new Vector3(width + 2f * T, T, len), floor, g);
            Cube("Wall_L", new Vector3(-(width * 0.5f + T * 0.5f), 3.2f, zc), new Vector3(T, 6.4f, len), wall, g);
            Cube("Wall_R", new Vector3(width * 0.5f + T * 0.5f, 3.2f, zc), new Vector3(T, 6.4f, len), wall, g);
            Cube("Ceiling", new Vector3(0f, roomHeight + T * 0.5f, z0 + 0.9f), new Vector3(width + 2f * T, T, 1.8f), ceil, g);
            Cube("End", new Vector3(0f, 3.2f, z0 + len + T * 0.5f), new Vector3(width + 2f * T, 6.4f, T), wall, g);
            Cube("Skylight", new Vector3(0f, 6.4f + T * 0.5f, zc), new Vector3(width + 2f * T, T, len), ceil, g);

            for (int k = 0; k < 7; k++)
            {
                float zk = z0 + 1.9f + 0.26f * k + 0.13f;
                float hk = 0.18f * (k + 1);
                Cube("Step_" + k, new Vector3(0f, hk * 0.5f, zk), new Vector3(width, hk, 0.26f), tread, g);
            }
            Cube("Landing", new Vector3(0f, 0.63f, z0 + len - 0.14f), new Vector3(width, 1.26f, 0.28f), tread, g);

            Cube("ExitSign", new Vector3(0f, 2.6f, z0 + len - 0.015f), new Vector3(1.0f, 0.3f, 0.03f), exitMat, g, false);
            var t = Text("ExitText", g, new Vector3(0f, 2.6f, z0 + len - 0.035f), new Vector2(0.9f, 0.22f), sign,
                         Color.white, TextAlignmentOptions.Center, true);
            t.rectTransform.localRotation = Quaternion.identity;
            PointLight("StairLight", g, new Vector3(0f, 3.4f, zc), new Color(1f, 0.97f, 0.92f), 2.2f, 7f, false);

            var trig = new GameObject("ExitTrigger");
            trig.transform.SetParent(g, false);
            trig.transform.position = new Vector3(0f, 1.1f, z0 + 1.4f);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(width, 2.2f, 0.6f);
            var et = trig.AddComponent<ExitTrigger>();
            et.manager = lm;
        }

        /// Double sliding door centred at x=0 in a front wall whose room-side face is at zFace.
        /// Leaves slide sideways into the wall. Returns the Door component.
        public static Door DoubleDoor(Transform p, float zFace, float wallT, float width, float height, Material leaf, Material frame,
                                      Material lamp, Material glass, bool glazed)
        {
            var g = Group("Door", p);
            g.localPosition = new Vector3(0f, 0f, zFace + wallT * 0.5f);
            float face = -wallT * 0.5f;

            Cube("Frame_L", new Vector3(-width * 0.5f - 0.06f, (height + 0.12f) * 0.5f, face - 0.045f), new Vector3(0.12f, height + 0.12f, 0.09f), frame, g);
            Cube("Frame_R", new Vector3(width * 0.5f + 0.06f, (height + 0.12f) * 0.5f, face - 0.045f), new Vector3(0.12f, height + 0.12f, 0.09f), frame, g);
            Cube("Frame_Top", new Vector3(0f, height + 0.06f, face - 0.045f), new Vector3(width + 0.24f, 0.12f, 0.09f), frame, g);
            Cube("Threshold", new Vector3(0f, 0.008f, 0f), new Vector3(width, 0.016f, wallT + 0.1f), frame, g, false);

            Transform Leaf(string name, float sx)
            {
                var lf = Group(name, g);
                lf.localPosition = new Vector3(sx * width * 0.25f, height * 0.5f, 0f);
                float lw = width * 0.5f - 0.01f;
                if (glazed)
                {
                    Cube("Stile_Out", new Vector3(sx * (lw * 0.5f - 0.04f), 0f, 0f), new Vector3(0.08f, height - 0.02f, 0.06f), leaf, lf);
                    Cube("Stile_In", new Vector3(-sx * (lw * 0.5f - 0.03f), 0f, 0f), new Vector3(0.06f, height - 0.02f, 0.06f), leaf, lf);
                    Cube("Rail_Top", new Vector3(0f, height * 0.5f - 0.06f, 0f), new Vector3(lw, 0.12f, 0.06f), leaf, lf);
                    Cube("Rail_Bottom", new Vector3(0f, -height * 0.5f + 0.1f, 0f), new Vector3(lw, 0.2f, 0.06f), leaf, lf);
                    var pane = Cube("Glass", new Vector3(0f, 0.04f, 0f), new Vector3(lw - 0.12f, height - 0.3f, 0.015f), glass, lf);
                    pane.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    Cube("Handle", new Vector3(-sx * (lw * 0.5f - 0.12f), 0f, -0.06f), new Vector3(0.03f, 0.5f, 0.03f), frame, lf, false);
                }
                else
                {
                    Cube("Slab", Vector3.zero, new Vector3(lw, height - 0.02f, 0.07f), leaf, lf);
                    Cube("Window", new Vector3(0f, height * 0.18f, -0.036f), new Vector3(lw * 0.45f, height * 0.22f, 0.01f), glass, lf, false);
                    Cube("Kick", new Vector3(0f, -height * 0.5f + 0.15f, -0.04f), new Vector3(lw - 0.06f, 0.26f, 0.01f), frame, lf, false);
                    Cube("Handle", new Vector3(-sx * (lw * 0.5f - 0.1f), 0f, -0.06f), new Vector3(0.03f, 0.35f, 0.03f), frame, lf, false);
                }
                return lf;
            }
            var left = Leaf("Leaf_L", -1f);
            var right = Leaf("Leaf_R", 1f);

            var lampGo = Sphere("StatusLamp", new Vector3(width * 0.5f + 0.3f, height + 0.05f, face - 0.05f), 0.09f, lamp, g);
            Cube("Lamp_Base", new Vector3(width * 0.5f + 0.3f, height + 0.05f, face - 0.01f), new Vector3(0.14f, 0.14f, 0.02f), frame, g, false);
            var lampLight = PointLight("StatusLight", g, g.TransformPoint(new Vector3(width * 0.5f + 0.3f, height + 0.05f, face - 0.3f)),
                                       new Color(1f, 0.12f, 0.08f), 0.6f, 1.6f, false);

            var door = g.gameObject.AddComponent<Door>();
            door.panel = left;
            door.openOffset = new Vector3(-width * 0.5f - 0.02f, 0f, 0f);
            door.panelB = right;
            door.openOffsetB = new Vector3(width * 0.5f + 0.02f, 0f, 0f);
            door.statusLamp = lampGo.GetComponent<Renderer>();
            door.statusLight = lampLight;
            return door;
        }
    }
}
