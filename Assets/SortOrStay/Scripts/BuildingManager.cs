using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortOrStay
{
    /// Runs the five-floor building (Scenes/Building.unity). The player starts on the top floor, sorts it, walks out
    /// through its door and down the stair tower into the next floor, all the way to the ground floor and out to the
    /// street. No scene loads and no fades between floors: walking into the next room makes it the current floor
    /// (HUD title, objective, move counter, keyboard shortcuts), and the entrance door slides shut behind you.
    [DefaultExecutionOrder(-50)]
    public class BuildingManager : MonoBehaviour
    {
        public static BuildingManager Instance { get; private set; }

        /// Floor to start on after a restart or a jump from the pause menu (0 = the top floor, with the wake-up intro).
        public static int StartFloor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { StartFloor = 0; Instance = null; }

        [Serializable]
        public class Floor
        {
            public int number;
            public string title;
            public string roomName;
            [TextArea] public string objective;
            public string[] lines;
            public SortPuzzleBase puzzle;
            public Door exitDoor;
            public EntranceDoor entrance;          // null on the top floor
            public Transform root;
            public Vector3 spawn;                  // where a restart puts the player (world)
            public float spawnYaw;
            public Vector3 roomMin, roomMax;       // the room's inside (world box)
            public Vector3[] pathOut;              // waypoints from inside this floor, down the stairs, into the next floor (or the street)
            [NonSerialized] public List<Light> lights;
        }

        public Floor[] floors = new Floor[0];
        public PlayerController player;
        public IntroSequence intro;

        public int CurrentIndex { get; private set; }
        public int FloorCount => floors.Length;
        public Floor Current => floors[CurrentIndex];
        public SortPuzzleBase CurrentPuzzle => floors.Length > 0 ? floors[CurrentIndex].puzzle : null;

        bool sawDebrief, toldStairs;
        float solvedAt = -1f, nextLightCheck;

        void Awake()
        {
            Instance = this;
            CurrentIndex = Mathf.Clamp(StartFloor, 0, Mathf.Max(0, floors.Length - 1));
            for (int i = 0; i < floors.Length; i++)
            {
                var f = floors[i];
                if (f.puzzle != null) f.puzzle.inputEnabled = i == CurrentIndex;
                f.lights = new List<Light>();
                if (f.root != null)
                    foreach (var l in f.root.GetComponentsInChildren<Light>(true))
                        if (l.enabled) f.lights.Add(l);
            }
            // starting lower down (restart / jump): no wake-up scene on the top floor
            if (intro != null && CurrentIndex > 0) intro.enabled = false;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            var f = Current;
            if (CurrentIndex > 0)
            {
                Teleport(f.spawn, f.spawnYaw);
                if (f.entrance != null) f.entrance.CloseNow();
                var ui = GameUI.Instance;
                if (ui != null) { ui.SetFade(0f); ui.showHud = true; ui.ShowControls(8f); }
                ApplyHud(CurrentIndex, true);
                StartCoroutine(Lines(f.lines));
            }
            else ApplyHud(0, false);                   // the wake-up intro sets the first objective
            if (f.puzzle != null) f.puzzle.OnBecameCurrent();
            UpdateLights(true);
        }

        void Update()
        {
            if (player == null || floors.Length == 0) return;
            Vector3 p = player.transform.position;

            // walked into a lower floor: it becomes the current one
            for (int i = CurrentIndex + 1; i < floors.Length; i++)
                if (Inside(floors[i], p, 0.7f)) { Enter(i); break; }

            // after the debrief: point the way down
            var cur = Current;
            var ui = GameUI.Instance;
            if (cur.puzzle != null && cur.puzzle.Solved && !toldStairs && ui != null)
            {
                if (solvedAt < 0f) solvedAt = Time.time;
                if (ui.PopupOpen) sawDebrief = true;
                if ((sawDebrief && !ui.PopupOpen) || Time.time > solvedAt + 12f)
                {
                    toldStairs = true;
                    if (CurrentIndex + 1 < floors.Length)
                    {
                        var next = floors[CurrentIndex + 1];
                        ui.SetObjective($"The door is open.  Take the stairs down to <b>Floor {next.number}  ·  {next.roomName}</b>.");
                        ui.Subtitle($"Down the stairs:  Floor {next.number}.", 3.5f);
                    }
                    else ui.SetObjective("The vault is open.  Walk out through the vault door to the street:  you're free.");
                }
            }

            if (Time.time >= nextLightCheck) UpdateLights(false);
        }

        static bool Inside(Floor f, Vector3 p, float margin)
        {
            return p.x > f.roomMin.x + margin && p.x < f.roomMax.x - margin &&
                   p.z > f.roomMin.z + margin && p.z < f.roomMax.z - margin &&
                   p.y > f.roomMin.y - 0.6f && p.y < f.roomMax.y;
        }

        /// Is this world point inside a floor's room (index), or -1 (stairs, street)?
        public int FloorAt(Vector3 p)
        {
            for (int i = 0; i < floors.Length; i++) if (Inside(floors[i], p, 0f)) return i;
            return -1;
        }

        void Enter(int i)
        {
            var prev = Current;
            if (prev.puzzle != null) prev.puzzle.inputEnabled = false;
            CurrentIndex = i;
            sawDebrief = toldStairs = false;
            solvedAt = -1f;

            var f = Current;
            if (f.puzzle != null) f.puzzle.inputEnabled = true;
            ApplyHud(i, true);
            if (f.puzzle != null) f.puzzle.OnBecameCurrent();
            if (player != null) player.RebuildNavMesh();
            ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.45f);
            StartCoroutine(Lines(f.lines));
            UpdateLights(true);
        }

        void ApplyHud(int i, bool objective)
        {
            var ui = GameUI.Instance;
            if (ui == null) return;
            var f = floors[i];
            ui.levelTitle = f.title;
            ui.floor = f.number;
            if (objective) ui.SetObjective(f.objective);
        }

        IEnumerator Lines(string[] lines)
        {
            if (lines == null) yield break;
            var ui = GameUI.Instance;
            foreach (var l in lines)
            {
                if (ui != null) ui.Subtitle(l, 3.6f);
                yield return new WaitForSeconds(3.8f);
            }
        }

        /// Lights of floors far above or below are switched off: they would only shine through floors and ceilings.
        void UpdateLights(bool force)
        {
            nextLightCheck = Time.time + 0.3f;
            if (player == null) return;
            float y = player.transform.position.y;
            foreach (var f in floors)
            {
                if (f.lights == null) continue;
                bool on = Mathf.Abs(y - f.roomMin.y) < 6.5f;
                foreach (var l in f.lights)
                    if (l != null && l.enabled != on) l.enabled = on;
            }
        }

        public void Teleport(Vector3 pos, float yaw)
        {
            if (player == null) return;
            player.StopWalking();
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.transform.position = pos;
            if (cc != null) cc.enabled = true;
            player.SetLook(yaw, 12f);
            player.FaceTowards(pos + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            player.SnapCamera();
        }

        // ------------------------------------------------------------ restart / jump (pause menu, end card)

        /// Restart the floor the player is on; from the end card, the whole building from the top.
        public void Restart()
        {
            var ui = GameUI.Instance;
            if (ui != null && ui.EndCardOpen)
            {
                GameState.Instance.results.Clear();
                StartFloor = 0;
            }
            else StartFloor = CurrentIndex;
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// Start again on another floor (index 0 = the top floor).
        public void JumpTo(int index)
        {
            StartFloor = Mathf.Clamp(index, 0, floors.Length - 1);
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
