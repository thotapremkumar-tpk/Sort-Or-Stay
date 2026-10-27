using System;
using System.Collections.Generic;
using System.Linq;
using SortOrStay;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using static SortOrStayEditor.Kit;

namespace SortOrStayEditor
{
    /// Builds Scenes/Building.unity: the whole game as one five-floor building. Floor 1 (the Locked Room) is the top
    /// floor, Floor 5 (the Final Vault) the ground floor. Every floor is the same room as its own scene, with one extra
    /// doorway in the front wall (Kit.BuildingMode). A stair tower on the front of the building links them: each floor's
    /// exit door opens onto a landing, two flights with a half-landing go down one storey, and the lower flight ends at
    /// the next floor's entrance. A wall between the two flights keeps the next floor's exit landing separate, so the
    /// only way down is through every room. On the ground floor the vault door opens into a corridor under the stairs
    /// that leads out to the street. Menu: Sort or Stay > Build Building (all 5 floors).
    public static class BuildBuilding
    {
        const string SCENE = ROOT + "Scenes/Building.unity";

        // storeys: floor 1 at the top, floor 5 on the ground
        static readonly float[] LevelY = { 20f, 15f, 10f, 5f, 0f };
        const float STOREY = 5f;
        const float T = 0.2f;                 // wall / slab thickness (same as the rooms)
        const float ZF = 5.2f;                // world z of the outer face of every floor's front wall

        // stair tower, x across, z' = outward from the front wall (z' = z - ZF)
        const float LA0 = -1.1f, LA1 = 1.3f;  // lane A: from a floor's exit door down to the half-landing
        const float LB0 = 1.6f, LB1 = 4.0f;   // lane B: from the half-landing down to the next floor's entrance
        const float XA = (LA0 + LA1) * 0.5f, XB = (LB0 + LB1) * 0.5f, LANE = LA1 - LA0;
        const float LAND = 1.5f;              // landing depth in front of the doors
        const int STEPS = 14;                 // per flight (half a storey)
        const float GOING = 0.28f;
        const float RUN = STEPS * GOING;      // 3.92
        const float FAR = LAND + RUN + 1.6f;  // inner face of the far wall (half-landing 1.6 deep)
        const float TOP = 20f + 3f + T;       // tower roof = top of floor 1's ceiling

        static Material mWall, mFloorTile, mStep, mNosing, mRail, mDark, mSign, mExit, mDoor, mFrame, mGlass, mLight,
                        mPavement, mRoad, mKerb, mFacade, mLamp, mBench;

        static float Riser => STOREY * 0.5f / STEPS;

        [MenuItem("Sort or Stay/Build Building (all 5 floors)")]
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

            var building = new GameObject("Building").transform;
            var parts = new FloorParts[5];
            Func<FloorParts>[] builders = { BuildLevel1.BuildFloor, BuildLevel2.BuildFloor, BuildLevel3.BuildFloor, BuildLevel4.BuildFloor, BuildLevel5.BuildFloor };

            BuildingMode = true;
            try
            {
                for (int k = 0; k < 5; k++)
                {
                    var before = new HashSet<GameObject>(scene.GetRootGameObjects());
                    var p = builders[k]();
                    MakeMaterials();                                  // the floor builders share the Kit statics
                    if (k > 0) Entrance(p);
                    foreach (var go in scene.GetRootGameObjects())    // anything a builder left at the scene root
                        if (!before.Contains(go) && go != p.root.gameObject) go.transform.SetParent(p.root, true);
                    p.root.SetParent(building, true);
                    p.root.position = new Vector3(0f, LevelY[k], RoomZ(p));
                    parts[k] = p;
                }
            }
            finally { BuildingMode = false; }

            var tower = Group("StairTower", building);
            tower.position = new Vector3(0f, 0f, ZF);
            TowerShell(tower, parts);
            for (int k = 0; k < 4; k++) Shaft(tower, k, parts[k + 1]);
            ExitCorridor(tower);
            Street(building);

            // ---------- player: wakes up on the top floor ----------
            Vector3 spawn0 = parts[0].root.TransformPoint(parts[0].spawn);
            var player = BuildPlayer(spawn0, 0f);
            player.navBoundsHeight = 11f;          // walk map: this floor and the stairs down to the next one
            player.navCenterOffset = -1f;

            // ---------- systems ----------
            var sys = new GameObject("Systems").transform;
            var ui = BuildUI(sys, parts[0].title, 1);

            var intro = sys.gameObject.AddComponent<IntroSequence>();
            intro.player = player;
            intro.puzzle = parts[0].puzzle;

            var lm = sys.gameObject.AddComponent<LevelManager>();
            lm.levelNumber = 5;
            lm.nextScene = "";
            lm.nextFloorLabel = "";
            lm.door = parts[4].exitDoor;
            lm.player = player;
            StreetExitTrigger(tower, lm);

            var bm = sys.gameObject.AddComponent<BuildingManager>();
            bm.player = player;
            bm.intro = intro;
            bm.floors = new BuildingManager.Floor[5];
            for (int k = 0; k < 5; k++) bm.floors[k] = FloorData(k, parts);

            BrightPostFx(sys, "Building_PostFX", 0.18f);
            BrightEnvironment(new Color(0.82f, 0.85f, 0.90f), new Color(0.68f, 0.67f, 0.64f), new Color(0.42f, 0.40f, 0.38f));
            var sun = Sun(building, new Vector3(35f, -110f, 0f), new Color(1f, 0.95f, 0.86f), 1.55f);
            RenderSettings.sun = sun;

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, SCENE);
            AddToBuildSettings(SCENE, 0);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = building.gameObject;
            Debug.Log("Sort or Stay: the five-floor building is built and saved to " + SCENE + ". Press Play: you wake up on the top floor.");
        }

        /// Floors line up on their front walls: a room's centre sits half its depth (plus the wall) behind ZF.
        static float RoomZ(FloorParts p) => ZF - (p.depth * 0.5f + T);

        static BuildingManager.Floor FloorData(int k, FloorParts[] parts)
        {
            var p = parts[k];
            float zc = RoomZ(p), y = LevelY[k];
            var f = new BuildingManager.Floor
            {
                number = p.number,
                title = p.title,
                roomName = p.roomName,
                objective = p.objective,
                lines = p.lines,
                puzzle = p.puzzle,
                exitDoor = p.exitDoor,
                entrance = p.root.GetComponentInChildren<EntranceDoor>(true),
                root = p.root,
                spawn = k == 0 ? p.root.TransformPoint(p.spawn) : new Vector3(EntranceX, y, ZF - T - 1.4f),
                spawnYaw = k == 0 ? 0f : 180f,
                roomMin = new Vector3(-p.width * 0.5f, y, zc - p.depth * 0.5f),
                roomMax = new Vector3(p.width * 0.5f, y + p.height, zc + p.depth * 0.5f),
            };

            float mid = y - STOREY * 0.5f;
            if (k < 4)
            {
                float yb = LevelY[k + 1];
                f.pathOut = new[]
                {
                    new Vector3(0f, y, ZF - T - 1.1f),                 // in front of the exit door, inside
                    new Vector3(XA, y, ZF + 0.8f),                    // landing
                    new Vector3(XA, mid, ZF + LAND + RUN + 0.6f),     // down flight A to the half-landing
                    new Vector3(XB, mid, ZF + LAND + RUN + 0.6f),     // across
                    new Vector3(XB, yb, ZF + LAND - 0.4f),            // down flight B
                    new Vector3(EntranceX, yb, ZF - T - 0.7f),        // through the entrance
                    new Vector3(EntranceX, yb, ZF - T - 1.9f),        // into the next floor
                };
            }
            else
            {
                f.pathOut = new[]
                {
                    new Vector3(0f, 0f, ZF - T - 1.1f),               // in front of the vault door
                    new Vector3(XA, 0f, ZF + 0.9f),                   // the corridor under the stairs
                    new Vector3(XA, 0f, ZF + FAR - 0.5f),
                    new Vector3(XA, 0f, ZF + FAR + T + 1.0f),         // out through the street door
                    new Vector3(XA, 0f, ZF + FAR + T + 2.6f),         // on the pavement
                };
            }
            return f;
        }

        // ============================================================ materials

        static void MakeMaterials()
        {
            mWall = Pbr("B_StairWall", "PaintedPlaster017", new Color(0.95f, 0.95f, 0.93f), 2.5f, 0.1f, 0f);
            mFloorTile = Pbr("B_StairFloor", "Tiles107", new Color(0.88f, 0.88f, 0.86f), 2f, 0.35f, 0f);
            mStep = Pbr("B_Step", "Concrete034", new Color(0.86f, 0.86f, 0.84f), 1.2f, 0.3f, 0f);
            mNosing = Flat("B_Nosing", new Color(0.93f, 0.62f, 0.10f), 0.4f, 0f);
            mRail = Flat("B_Rail", new Color(0.22f, 0.23f, 0.25f), 0.6f, 0.6f);
            mDark = Flat("B_Dark", new Color(0.18f, 0.18f, 0.20f), 0.4f, 0.3f);
            mSign = Flat("B_SignPlate", new Color(0.12f, 0.36f, 0.66f), 0.35f, 0f);
            mExit = Emissive("B_Exit", new Color(0.05f, 0.5f, 0.2f), new Color(0.1f, 0.9f, 0.35f), 1.6f);
            mDoor = Pbr("B_EntranceDoor", "MetalPlates017A", new Color(0.80f, 0.82f, 0.85f), 1f, 0.45f, 0.3f);
            mFrame = Flat("B_Frame", new Color(0.30f, 0.31f, 0.34f), 0.5f, 0.4f);
            mGlass = Glass("B_Glass", new Color(0.82f, 0.92f, 1f), 0.16f);
            mLight = Emissive("B_LightPanel", new Color(1f, 0.98f, 0.94f), new Color(1f, 0.96f, 0.9f), 2.2f);
            mPavement = Pbr("B_Pavement", "Tiles107", new Color(0.80f, 0.79f, 0.76f), 6f, 0.25f, 0f);
            mRoad = Pbr("B_Road", "Concrete034", new Color(0.30f, 0.30f, 0.32f), 6f, 0.2f, 0f);
            mKerb = Flat("B_Kerb", new Color(0.70f, 0.70f, 0.68f), 0.2f, 0f);
            mFacade = Pbr("B_Facade", "PaintedPlaster017", new Color(0.86f, 0.80f, 0.70f), 4f, 0.1f, 0f);
            mLamp = Emissive("B_StreetLamp", new Color(1f, 0.95f, 0.8f), new Color(1f, 0.9f, 0.7f), 1.2f);
            mBench = Pbr("B_Bench", "WoodFloor064", new Color(0.70f, 0.52f, 0.34f), 0.6f, 0.3f, 0f);
        }

        // ============================================================ entrance doorway (floor-local, before the floor moves)

        static void Entrance(FloorParts p)
        {
            float hd = p.depth * 0.5f;
            var g = Group("EntranceDoor", p.root);
            g.localPosition = new Vector3(EntranceX, 0f, hd + T * 0.5f);     // centred in the front wall's thickness
            float w = EntranceW, h = EntranceH, d = T + 0.12f;

            Cube("Frame_L", new Vector3(-w * 0.5f - 0.05f, (h + 0.1f) * 0.5f, 0f), new Vector3(0.1f, h + 0.1f, d), mFrame, g, false);
            Cube("Frame_R", new Vector3(w * 0.5f + 0.05f, (h + 0.1f) * 0.5f, 0f), new Vector3(0.1f, h + 0.1f, d), mFrame, g, false);
            Cube("Frame_Top", new Vector3(0f, h + 0.05f, 0f), new Vector3(w + 0.2f, 0.1f, d), mFrame, g, false);
            Cube("Threshold", new Vector3(0f, 0.008f, 0f), new Vector3(w, 0.016f, T + 0.1f), mFrame, g, false);

            // sliding panel: waits inside the wall (towards +x), closes once the player is in the room
            var panel = Group("Panel", g);
            panel.localPosition = new Vector3(0f, h * 0.5f, 0f);
            Cube("Slab", Vector3.zero, new Vector3(w - 0.02f, h - 0.02f, 0.06f), mDoor, panel);
            Cube("Window", new Vector3(0f, h * 0.2f, -0.031f), new Vector3(w * 0.4f, h * 0.22f, 0.006f), mGlass, panel, false);
            Cube("Window_Out", new Vector3(0f, h * 0.2f, 0.031f), new Vector3(w * 0.4f, h * 0.22f, 0.006f), mGlass, panel, false);
            Cube("Handle", new Vector3(-w * 0.5f + 0.12f, 0f, -0.045f), new Vector3(0.03f, 0.3f, 0.03f), mFrame, panel, false);
            Cube("Kick", new Vector3(0f, -h * 0.5f + 0.13f, 0.032f), new Vector3(w - 0.08f, 0.22f, 0.006f), mFrame, panel, false);

            // floor sign above the doorway, on the stair side (faces +Z, read from the stairs)
            var sign = Group("FloorSign", g);
            sign.localPosition = new Vector3(0f, h + 0.42f, T * 0.5f + 0.02f);
            sign.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Cube("Plate", Vector3.zero, new Vector3(w + 0.6f, 0.42f, 0.03f), mSign, sign, false);
            Text("Text", sign, new Vector3(0f, 0.03f, -0.018f), new Vector2(w + 0.5f, 0.22f), $"FLOOR {p.number}",
                 Color.white, TextAlignmentOptions.Center, true);
            Text("Name", sign, new Vector3(0f, -0.12f, -0.018f), new Vector2(w + 0.5f, 0.11f), p.roomName.ToUpperInvariant(),
                 new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center, true);

            var ed = g.gameObject.AddComponent<EntranceDoor>();
            ed.panel = panel;
            ed.openOffset = new Vector3(w + 0.06f, 0f, 0f);
            ed.inward = Vector3.back;
        }

        // ============================================================ stair tower

        /// Walls, roof, ground, the wall between the two flights, the far wall with its windows, and fillers where
        /// a floor's front wall does not reach (floor 1 is narrower than the tower; small gaps between storeys).
        static void TowerShell(Transform t, FloorParts[] parts)
        {
            var g = Group("Shell", t);
            float x0 = LA0 - T, x1 = LB1 + T, w = x1 - x0, xc = (x0 + x1) * 0.5f;
            float depth = FAR + T, hTot = TOP + T;

            Cube("Ground", new Vector3(xc, -T * 0.5f, FAR * 0.5f), new Vector3(w, T, FAR), mFloorTile, g);
            Cube("Roof", new Vector3(xc, TOP + T * 0.5f, depth * 0.5f), new Vector3(w, T, depth), mWall, g);
            Cube("Wall_Left", new Vector3(LA0 - T * 0.5f, hTot * 0.5f - T, depth * 0.5f), new Vector3(T, hTot, depth), mWall, g);
            Cube("Wall_Right", new Vector3(LB1 + T * 0.5f, hTot * 0.5f - T, depth * 0.5f), new Vector3(T, hTot, depth), mWall, g);
            Cube("Wall_Middle", new Vector3((LA1 + LB0) * 0.5f, hTot * 0.5f - T, (LAND + RUN) * 0.5f), new Vector3(LB0 - LA1, hTot, LAND + RUN), mWall, g);

            // far wall: the street door at the bottom of lane A, a window at every half-landing and at the top
            var holes = new List<(float x0, float x1, float y0, float y1, bool glass)>
            {
                (XA - 0.8f, XA + 0.8f, 0f, 2.2f, false)
            };
            for (int k = 0; k < 4; k++)
            {
                float mid = LevelY[k] - STOREY * 0.5f;
                holes.Add((XA - 0.6f, XA + 1.1f, mid + 0.9f, mid + 2.4f, true));      // over lane A; the sign hangs on lane B's wall
            }
            holes.Add((XA - 0.6f, XA + 1.1f, LevelY[0] + 0.9f, LevelY[0] + 2.4f, true));
            FarWall(g, holes, x0, x1);

            // fillers in the plane of the floors' front walls (z' from -T to 0)
            var fill = Group("Fillers", g);
            float zf = -T * 0.5f;
            for (int k = 0; k < 5; k++)
            {
                var p = parts[k];
                float y0 = LevelY[k] - T, y1 = LevelY[k] + p.height + T;
                float rx = p.width * 0.5f + T;
                if (x1 > rx) Cube("Side_" + k, new Vector3((rx + x1) * 0.5f, (y0 + y1) * 0.5f, zf), new Vector3(x1 - rx, y1 - y0, T), mWall, fill);
                if (x0 < -rx) Cube("SideL_" + k, new Vector3((x0 - rx) * 0.5f, (y0 + y1) * 0.5f, zf), new Vector3(-rx - x0, y1 - y0, T), mWall, fill);
                if (k > 0)
                {
                    float gapTop = LevelY[k - 1] - T;                 // underside of the floor above
                    if (gapTop - y1 > 0.005f)
                        Cube("Gap_" + k, new Vector3(xc, (y1 + gapTop) * 0.5f, zf), new Vector3(w, gapTop - y1, T), mWall, fill);
                }
            }
        }

        static void FarWall(Transform g, List<(float x0, float x1, float y0, float y1, bool glass)> holes, float x0, float x1)
        {
            var wall = Group("Wall_Far", g);
            float z = FAR + T * 0.5f, w = x1 - x0, xc = (x0 + x1) * 0.5f;
            var sorted = holes.OrderBy(h => h.y0).ToList();
            float y = -T;
            int n = 0;
            foreach (var h in sorted)
            {
                if (h.y0 > y + 0.001f) Cube("Band_" + n, new Vector3(xc, (y + h.y0) * 0.5f, z), new Vector3(w, h.y0 - y, T), mWall, wall);
                if (h.x0 > x0) Cube("Left_" + n, new Vector3((x0 + h.x0) * 0.5f, (h.y0 + h.y1) * 0.5f, z), new Vector3(h.x0 - x0, h.y1 - h.y0, T), mWall, wall);
                if (h.x1 < x1) Cube("Right_" + n, new Vector3((h.x1 + x1) * 0.5f, (h.y0 + h.y1) * 0.5f, z), new Vector3(x1 - h.x1, h.y1 - h.y0, T), mWall, wall);
                if (h.glass) WindowX(wall, "Window_" + n, z, (h.x0 + h.x1) * 0.5f, h.x1 - h.x0, h.y0, h.y1, T, mFrame, mGlass, mDark);
                y = h.y1;
                n++;
            }
            if (TOP + T > y) Cube("Band_Top", new Vector3(xc, (y + TOP + T) * 0.5f, z), new Vector3(w, TOP + T - y, T), mWall, wall);
        }

        /// One storey of stairs, from floor k's exit landing down to floor k+1's entrance landing.
        static void Shaft(Transform t, int k, FloorParts below)
        {
            float yTop = LevelY[k], yBot = LevelY[k + 1], mid = yTop - STOREY * 0.5f, r = Riser;
            var g = Group($"Stairs_F{k + 1}_to_F{k + 2}", t);

            // landing outside floor k's exit door (lane A)
            Cube("Landing_Top", new Vector3(XA, yTop - 0.125f, LAND * 0.5f), new Vector3(LANE, 0.25f, LAND), mFloorTile, g);

            // flight A: outward, down to the half-landing
            for (int i = 0; i < STEPS; i++)
            {
                float z0 = LAND + i * GOING, top = yTop - (i + 1) * r;
                Cube("StepA_" + i, new Vector3(XA, top - 0.225f, z0 + GOING * 0.5f), new Vector3(LANE, 0.45f, GOING), mStep, g);
                if (i < STEPS - 1) Cube("NoseA_" + i, new Vector3(XA, top + 0.003f, z0 + GOING - 0.03f), new Vector3(LANE - 0.1f, 0.006f, 0.05f), mNosing, g, false);
            }

            // half-landing across both lanes
            float zm0 = LAND + RUN, zm1 = FAR;
            Cube("Landing_Half", new Vector3((LA0 + LB1) * 0.5f, mid - 0.125f, (zm0 + zm1) * 0.5f), new Vector3(LB1 - LA0, 0.25f, zm1 - zm0), mFloorTile, g);

            // flight B: back towards the building, down to the next floor's entrance
            for (int i = 0; i < STEPS; i++)
            {
                float z1 = zm0 - i * GOING, top = mid - (i + 1) * r;
                Cube("StepB_" + i, new Vector3(XB, top - 0.225f, z1 - GOING * 0.5f), new Vector3(LANE, 0.45f, GOING), mStep, g);
                if (i < STEPS - 1) Cube("NoseB_" + i, new Vector3(XB, top + 0.003f, z1 - GOING + 0.03f), new Vector3(LANE - 0.1f, 0.006f, 0.05f), mNosing, g, false);
            }

            // landing outside the next floor's entrance (lane B); the ground floor stands on the tower's ground slab
            if (yBot > 0.01f) Cube("Landing_Bottom", new Vector3(XB, yBot - 0.125f, LAND * 0.5f), new Vector3(LANE, 0.25f, LAND), mFloorTile, g);

            // handrails on both sides of each flight
            float hr = 0.9f;
            Rod(g, "Rail_A_Out", new Vector3(LA0 + 0.06f, yTop + hr, LAND), new Vector3(LA0 + 0.06f, mid + hr, zm0), mRail);
            Rod(g, "Rail_A_In", new Vector3(LA1 - 0.06f, yTop + hr, LAND), new Vector3(LA1 - 0.06f, mid + hr, zm0), mRail);
            Rod(g, "Rail_B_In", new Vector3(LB0 + 0.06f, mid + hr, zm0), new Vector3(LB0 + 0.06f, yBot + hr, LAND), mRail);
            Rod(g, "Rail_B_Out", new Vector3(LB1 - 0.06f, mid + hr, zm0), new Vector3(LB1 - 0.06f, yBot + hr, LAND), mRail);

            // lights: wall lamps at both landings and above the half-landing window
            Sconce(g, "Light_Top", new Vector3(LA0 + 0.05f, yTop + 2.4f, LAND * 0.5f), 90f, 1.3f, 5f);
            Sconce(g, "Light_Half", new Vector3((LA0 + LB1) * 0.5f, mid + 2.75f, FAR - 0.05f), 180f, 1.6f, 6.5f);
            Sconce(g, "Light_Bottom", new Vector3(LB1 - 0.05f, yBot + 2.4f, LAND * 0.5f), -90f, 1.3f, 5f);

            // signs: hanging over the top of flight A (read from the door), on the far wall at the half-landing,
            // and big storey numbers on the side walls
            string next = $"FLOOR {below.number}  ·  {below.roomName.ToUpperInvariant()}";
            var s1 = Group("Sign_Down", g);
            float signY = yTop + 2.45f;
            s1.localPosition = new Vector3(XA, signY, LAND - 0.1f);
            Cube("Plate", Vector3.zero, new Vector3(LANE - 0.3f, 0.36f, 0.03f), mSign, s1, false);
            Text("Text", s1, new Vector3(0f, 0f, -0.018f), new Vector2(LANE - 0.4f, 0.24f), "DOWN  ↓   " + next, Color.white, TextAlignmentOptions.Center, true);
            float ceiling = k == 0 ? TOP : yTop + STOREY - 0.25f;        // the roof, or the landing slab above
            float hang = ceiling - (signY + 0.18f);
            CylY("Hanger_L", new Vector3(XA - 0.8f, signY + 0.18f + hang * 0.5f, LAND - 0.1f), 0.015f, hang, mRail, g);
            CylY("Hanger_R", new Vector3(XA + 0.8f, signY + 0.18f + hang * 0.5f, LAND - 0.1f), 0.015f, hang, mRail, g);

            var s2 = Group("Sign_Half", g);
            s2.localPosition = new Vector3(XB, mid + 1.9f, FAR - 0.02f);
            Cube("Plate", Vector3.zero, new Vector3(LANE - 0.2f, 0.46f, 0.03f), mSign, s2, false);
            Text("Text", s2, new Vector3(0f, 0.07f, -0.018f), new Vector2(LANE - 0.3f, 0.22f), $"→   FLOOR {below.number}", Color.white, TextAlignmentOptions.Center, true);
            Text("Name", s2, new Vector3(0f, -0.13f, -0.018f), new Vector2(LANE - 0.3f, 0.12f), below.roomName.ToUpperInvariant(),
                 new Color(0.85f, 0.92f, 1f), TextAlignmentOptions.Center, true);

            StoreyNumber(g, $"F{k + 1}", new Vector3(LA0 + 0.01f, yTop + 1.7f, LAND * 0.5f), -90f);   // read from lane A
            StoreyNumber(g, $"F{k + 2}", new Vector3(LB1 - 0.01f, yBot + 1.7f, LAND * 0.5f), 90f);    // read from lane B
        }

        /// Ground floor: the vault door opens into lane A under the last stairs; the corridor ends at the street door.
        static void ExitCorridor(Transform t)
        {
            var g = Group("ExitCorridor", t);
            Sconce(g, "Light_Corridor", new Vector3(LA0 + 0.05f, 2.0f, 2.6f), 90f, 1.2f, 5f);
            Sconce(g, "Light_Door", new Vector3(LA1 - 0.05f, 1.9f, FAR - 1.0f), -90f, 1.0f, 4f);

            // street door frame and the exit sign above it (inside, faces the corridor)
            Cube("DoorFrame_L", new Vector3(XA - 0.85f, 1.1f, FAR - 0.06f), new Vector3(0.1f, 2.2f, 0.12f), mFrame, g, false);
            Cube("DoorFrame_R", new Vector3(XA + 0.85f, 1.1f, FAR - 0.06f), new Vector3(0.1f, 2.2f, 0.12f), mFrame, g, false);
            Cube("DoorFrame_Top", new Vector3(XA, 2.15f, FAR - 0.06f), new Vector3(1.8f, 0.1f, 0.12f), mFrame, g, false);
            Cube("ExitSign", new Vector3(XA, 2.0f, FAR - 0.6f), new Vector3(0.7f, 0.2f, 0.04f), mExit, g, false);
            Text("ExitText", g, new Vector3(XA, 2.0f, FAR - 0.625f), new Vector2(0.62f, 0.15f), "EXIT  ·  STREET", Color.white, TextAlignmentOptions.Center, true);

            // a "you came out of the vault" plaque by the vault door, and a ground-floor number
            StoreyNumber(g, "G", new Vector3(LA0 + 0.01f, 1.6f, LAND * 0.5f), -90f);
        }

        static void StreetExitTrigger(Transform t, LevelManager lm)
        {
            var trig = new GameObject("ExitTrigger_Street");
            trig.transform.SetParent(t, false);
            trig.transform.localPosition = new Vector3(XA, 1.1f, FAR + T + 1.6f);
            var bc = trig.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            bc.size = new Vector3(3.2f, 2.2f, 0.8f);
            trig.AddComponent<ExitTrigger>().manager = lm;
        }

        /// Outside the street door: pavement, kerb, road, two street lamps, a bench and the building's front.
        static void Street(Transform building)
        {
            var g = Group("Street", building);
            float zf = ZF + FAR + T;                  // outer face of the far wall
            Cube("Pavement", new Vector3(1.5f, -0.06f, zf + 3.5f), new Vector3(24f, 0.12f, 7f), mPavement, g);
            Cube("Kerb", new Vector3(1.5f, -0.02f, zf + 7.1f), new Vector3(24f, 0.16f, 0.25f), mKerb, g);
            Cube("Road", new Vector3(1.5f, -0.16f, zf + 12f), new Vector3(30f, 0.12f, 9.5f), mRoad, g);
            for (int i = 0; i < 6; i++)
                Cube("RoadLine_" + i, new Vector3(-9f + i * 4.4f, -0.095f, zf + 12f), new Vector3(2.2f, 0.01f, 0.14f), mKerb, g, false);

            // the building's front either side of the stair tower
            float hb = TOP + T;
            Cube("Facade_L", new Vector3(-6.3f, hb * 0.5f - 0.2f, zf - T * 0.5f), new Vector3(10f, hb, T), mFacade, g);
            Cube("Facade_R", new Vector3(9.2f, hb * 0.5f - 0.2f, zf - T * 0.5f), new Vector3(10f, hb, T), mFacade, g);
            Cube("Facade_Base", new Vector3(1.5f, 0.3f, zf + 0.04f), new Vector3(30f, 0.6f, 0.06f), mDark, g, false);
            var name = Group("BuildingName", g);
            name.localPosition = new Vector3(-6.3f, 3.2f, zf + 0.02f);
            name.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Text("Text", name, Vector3.zero, new Vector2(6f, 0.8f), "SORT  OR  STAY", new Color(0.20f, 0.20f, 0.22f), TextAlignmentOptions.Center, true);

            foreach (float x in new[] { -3.5f, 6.5f })
            {
                var l = Group("StreetLamp", g);
                l.localPosition = new Vector3(x, 0f, zf + 6.5f);
                CylY("Post", new Vector3(0f, 2.0f, 0f), 0.12f, 4f, mRail, l, true);
                Cube("Arm", new Vector3(0f, 3.95f, -0.35f), new Vector3(0.08f, 0.08f, 0.7f), mRail, l, false);
                Cube("Head", new Vector3(0f, 3.88f, -0.7f), new Vector3(0.3f, 0.12f, 0.45f), mLamp, l, false);
            }
            var b = Group("Bench", g);
            b.localPosition = new Vector3(4.2f, 0f, zf + 4.8f);
            Cube("Seat", new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.06f, 0.42f), mBench, b);
            Cube("Back", new Vector3(0f, 0.75f, 0.19f), new Vector3(1.6f, 0.4f, 0.05f), mBench, b, false);
            Cube("Leg_L", new Vector3(-0.7f, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), mRail, b, false);
            Cube("Leg_R", new Vector3(0.7f, 0.22f, 0f), new Vector3(0.06f, 0.44f, 0.4f), mRail, b, false);
        }

        // ============================================================ helpers

        /// A round bar from a to b (handrails).
        static void Rod(Transform p, string name, Vector3 a, Vector3 b, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(p, false);
            Vector3 d = b - a;
            go.transform.localPosition = (a + b) * 0.5f;
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d.normalized);
            go.transform.localScale = new Vector3(0.045f, d.magnitude * 0.5f, 0.045f);
            go.GetComponent<Renderer>().sharedMaterial = m;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
        }

        /// A wall lamp (local position = on the wall face). yaw turns its front (+Z) away from the wall:
        /// 90 = on a left wall facing +x, -90 = on a right wall facing -x, 180 = on the far wall facing -z.
        static void Sconce(Transform p, string name, Vector3 local, float yaw, float intensity, float range)
        {
            var g = Group(name, p);
            g.localPosition = local;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Cube("Back", new Vector3(0f, 0f, 0.02f), new Vector3(0.36f, 0.2f, 0.04f), mFrame, g, false);
            Cube("Glow", new Vector3(0f, 0f, 0.06f), new Vector3(0.3f, 0.14f, 0.05f), mLight, g, false);
            PointLight("Light", g, g.TransformPoint(new Vector3(0f, -0.1f, 0.45f)), new Color(1f, 0.97f, 0.92f), intensity, range, false);
        }

        /// Big painted storey number on a side wall. yaw -90: on the left wall, read from +x; 90: right wall, read from -x.
        static void StoreyNumber(Transform p, string text, Vector3 local, float yaw)
        {
            var g = Group("Storey_" + text, p);
            g.localPosition = local;
            g.localRotation = Quaternion.Euler(0f, yaw, 0f);
            Text("Number", g, new Vector3(0f, 0f, -0.005f), new Vector2(1.2f, 0.9f), text, new Color(0.12f, 0.36f, 0.66f), TextAlignmentOptions.Center, true, 30f);
        }
    }
}
