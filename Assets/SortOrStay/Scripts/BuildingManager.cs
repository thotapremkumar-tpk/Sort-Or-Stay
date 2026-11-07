using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SortOrStay
{
    /// Runs the five-floor building (Scenes/Building.unity). Every floor is one sorting algorithm in three rooms
    /// side by side along a hallway: Room 1 deals the algorithm's best case, Room 2 its average case, Room 3 its
    /// worst case. The player starts inside Room 1 of the top floor. Each room is sorted, its three questions
    /// answered (RoomQuiz), and its exit door opens onto the hallway; that opens the next room's entrance. After
    /// Room 3 the hallway door to the stairs opens, and the stairs lead down into the next floor's hallway, all
    /// the way to the ground floor and out to the street. No scene loads and no fades between rooms or floors.
    [DefaultExecutionOrder(-50)]
    public class BuildingManager : MonoBehaviour
    {
        public static BuildingManager Instance { get; private set; }

        /// Floor and room to start in after a restart or a jump from the pause menu (0, 0 = top floor, wake-up intro).
        public static int StartFloor, StartRoom;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { StartFloor = 0; StartRoom = 0; Instance = null; }

        [Serializable]
        public class Room
        {
            public int number;                       // 1-3
            public SortCase sortCase;
            [TextArea] public string objective;
            public SortPuzzleBase puzzle;
            public Door exitDoor;                    // in the room's front wall, onto the hallway
            public RoomGate entrance;                // from the hallway
            public Transform root;
            public Vector3 roomMin, roomMax;         // the room's inside (world box)
            public Vector3 spawn;                    // inside, just past the entrance (a restart puts the player here)
            public float spawnYaw;
            public Vector3 entranceOutside, entranceInside;   // hallway side / room side of the entrance
            public Vector3 exitInside, exitOutside;           // room side / hallway side of the exit door
        }

        [Serializable]
        public class Floor
        {
            public int number;
            public string title;
            public string roomName;
            public Room[] rooms = new Room[0];
            public RoomGate stairsGate;              // hallway -> stairs down (or the street on the ground floor)
            public EntranceDoor entrance;            // stairs from above -> hallway (null on the top floor)
            public Transform root;
            public Vector3 hallMin, hallMax;         // the hallway's inside (world box)
            public Vector3[] stairsPath;             // from the hallway at the stairs door, down to the next hallway (or the street)
            [NonSerialized] public List<Light> lights;
        }

        public Floor[] floors = new Floor[0];
        public PlayerController player;
        public IntroSequence intro;
        public GuideNpc guide;

        public int CurrentIndex { get; private set; }
        public int RoomIndex { get; private set; }
        public int FloorCount => floors.Length;
        public Floor Current => floors[CurrentIndex];
        public Room CurrentRoom => Current.rooms.Length > 0 ? Current.rooms[Mathf.Clamp(RoomIndex, 0, Current.rooms.Length - 1)] : null;
        public SortPuzzleBase CurrentPuzzle => floors.Length > 0 && CurrentRoom != null ? CurrentRoom.puzzle : null;

        /// The player is inside the current room (not in the hallway or on the stairs).
        public bool InRoom => inRoom;
        /// Every room of the current floor is done: the stairs are open.
        public bool FloorDone => floorDone;

        bool inRoom, roomDone, floorDone;
        float nextLightCheck;
        Transform shadowRoom;                                               // the room whose lights cast shadows
        readonly Dictionary<Light, LightShadows> shadowTypes = new Dictionary<Light, LightShadows>();

        void Awake()
        {
            Instance = this;
            CurrentIndex = Mathf.Clamp(StartFloor, 0, Mathf.Max(0, floors.Length - 1));
            RoomIndex = floors.Length > 0 ? Mathf.Clamp(StartRoom, 0, Mathf.Max(0, floors[CurrentIndex].rooms.Length - 1)) : 0;
            for (int i = 0; i < floors.Length; i++)
            {
                var f = floors[i];
                foreach (var r in f.rooms)
                    if (r.puzzle != null) r.puzzle.inputEnabled = false;
                f.lights = new List<Light>();
                if (f.root != null)
                    foreach (var l in f.root.GetComponentsInChildren<Light>(true))
                        if (l.enabled) { f.lights.Add(l); shadowTypes[l] = l.shadows; }
            }
            // starting lower down (restart / jump): no wake-up scene on the top floor
            if (intro != null && (CurrentIndex > 0 || RoomIndex > 0)) intro.enabled = false;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (floors.Length == 0) return;
            var f = Current;
            var room = CurrentRoom;

            // rooms before the start room count as done: their doors stand open
            for (int k = 0; k < RoomIndex; k++)
            {
                var r = f.rooms[k];
                if (r.entrance != null) r.entrance.Open(true);
                if (r.exitDoor != null) r.exitDoor.Unlock();
            }
            if (f.entrance != null) f.entrance.CloseNow();
            if (room.entrance != null) room.entrance.CloseNow();

            bool wakeUp = CurrentIndex == 0 && RoomIndex == 0 && intro != null && intro.enabled;
            if (!wakeUp)
            {
                Teleport(room.spawn, room.spawnYaw);
                var ui = GameUI.Instance;
                if (ui != null) { ui.SetFade(0f); ui.showHud = true; ui.ShowControls(8f); }
            }
            ApplyHud(true);
            if (wakeUp && GameUI.Instance != null && intro != null) GameUI.Instance.SetObjective(room.objective);
            inRoom = true;
            if (room.puzzle != null) { room.puzzle.inputEnabled = !wakeUp; room.puzzle.OnBecameCurrent(); }
            if (intro != null) intro.objective = room.objective;
            if (guide != null)
            {
                Vector3 side = Quaternion.Euler(0f, room.spawnYaw, 0f) * new Vector3(-1.2f, 0f, 0.4f);
                guide.PlaceAt(room.spawn + side, room.spawn);
                StartCoroutine(GreetWhenReady());
            }
            UpdateLights(true);
        }

        /// The guide comes over once the wake-up scene is over.
        IEnumerator GreetWhenReady()
        {
            yield return null;
            var ui = GameUI.Instance;
            while ((player != null && player.cinematic) || (ui != null && (!ui.showHud || ui.Fade > 0.2f))) yield return null;
            yield return new WaitForSeconds(0.8f);
            if (guide != null) guide.OnFloorStart(Current.number, CurrentPuzzle);   // welcome + lesson (once per floor)
        }

        void Update()
        {
            if (player == null || floors.Length == 0) return;
            Vector3 p = player.transform.position;

            // walked into a lower floor (its hallway or one of its rooms)
            for (int i = CurrentIndex + 1; i < floors.Length; i++)
                if (InsideFloor(floors[i], p, 0.5f)) { EnterFloor(i); break; }

            var room = CurrentRoom;
            if (room == null) return;

            // walked into the room the floor is waiting on
            if (!inRoom && InsideBox(room.roomMin, room.roomMax, p, 0.6f)) EnterRoom();

            // the room is sorted, its questions are answered and its door is open: the next room (or the stairs) opens
            var ui = GameUI.Instance;
            if (inRoom && !roomDone && room.puzzle != null && room.puzzle.Solved && room.exitDoor != null && room.exitDoor.IsUnlocked
                && !(ui != null && ui.PopupOpen))
                RoomDone();

            if (Time.time >= nextLightCheck) UpdateLights(false);
        }

        static bool InsideBox(Vector3 min, Vector3 max, Vector3 p, float margin)
        {
            return p.x > min.x + margin && p.x < max.x - margin &&
                   p.z > min.z + margin && p.z < max.z - margin &&
                   p.y > min.y - 0.6f && p.y < max.y;
        }

        static bool InsideFloor(Floor f, Vector3 p, float margin)
        {
            if (InsideBox(f.hallMin, f.hallMax, p, margin)) return true;
            foreach (var r in f.rooms) if (InsideBox(r.roomMin, r.roomMax, p, margin)) return true;
            return false;
        }

        /// Is this world point inside a floor (hallway or a room): its index, or -1 (stairs, street)?
        public int FloorAt(Vector3 p)
        {
            for (int i = 0; i < floors.Length; i++) if (InsideFloor(floors[i], p, 0f)) return i;
            return -1;
        }

        void EnterFloor(int i)
        {
            var prev = CurrentPuzzle;
            if (prev != null) prev.inputEnabled = false;
            CurrentIndex = i;
            RoomIndex = 0;
            inRoom = roomDone = floorDone = false;

            var f = Current;
            if (f.rooms.Length > 0 && f.rooms[0].entrance != null) f.rooms[0].entrance.Open();
            ApplyHud(false);
            var ui = GameUI.Instance;
            if (ui != null) ui.SetObjective($"Floor {f.number}:  <b>{GuideNpc.AlgorithmName(f.number)}</b>.  Walk into <b>Room 1  ·  best case</b>, straight ahead across the hallway.");
            if (player != null) player.RebuildNavMesh();
            ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.45f);
            if (guide != null) guide.OnFloorStart(f.number, CurrentPuzzle);         // she comes over: welcome + lesson, then leaves
            UpdateLights(true);
        }

        void EnterRoom()
        {
            inRoom = true;
            roomDone = false;
            var room = CurrentRoom;
            ApplyHud(true);
            if (room.puzzle != null)
            {
                room.puzzle.inputEnabled = true;
                room.puzzle.OnBecameCurrent();
            }
            ProceduralSfx.PlayUI(ProceduralSfx.Hint, 0.4f);
            if (guide != null) guide.OnRoomEntered(Current.number, room.number, room.puzzle);
        }

        void RoomDone()
        {
            roomDone = true;
            var f = Current;
            var room = CurrentRoom;
            if (room.puzzle != null) room.puzzle.inputEnabled = false;
            bool last = RoomIndex >= f.rooms.Length - 1;
            bool lastFloor = CurrentIndex >= floors.Length - 1;
            var ui = GameUI.Instance;
            if (guide != null) guide.OnRoomDone(f.number, room.number, last, lastFloor);

            if (!last)
            {
                var next = f.rooms[RoomIndex + 1];
                if (next.entrance != null) next.entrance.Open();
                RoomIndex++;
                inRoom = false;
                roomDone = false;
                ApplyHud(false);
                if (ui != null)
                    ui.SetObjective($"The door is open.  Go to <b>Room {next.number}  ·  {SortPuzzleBase.CaseLabel(next.sortCase)}</b>:  out into the hallway, the next door on the right.");
            }
            else
            {
                floorDone = true;
                if (f.stairsGate != null) f.stairsGate.Open();
                if (ui != null)
                {
                    if (!lastFloor)
                    {
                        var below = floors[CurrentIndex + 1];
                        ui.SetObjective($"Floor {f.number} done.  Back along the hallway to the <b>stairs</b>, and down to <b>Floor {below.number}  ·  {GuideNpc.AlgorithmName(below.number)}</b>.");
                        ui.Subtitle($"Down the stairs:  Floor {below.number}.", 3.5f);
                    }
                    else ui.SetObjective("All five floors done.  Back along the hallway, through the green door and out to the street:  you're free.");
                }
            }
        }

        void ApplyHud(bool objective)
        {
            var ui = GameUI.Instance;
            if (ui == null) return;
            var f = Current;
            var room = CurrentRoom;
            ui.floor = f.number;
            ui.room = room != null ? room.number : 0;
            ui.roomCase = room != null ? room.sortCase : SortCase.Classic;
            ui.levelTitle = room != null
                ? $"FLOOR {f.number}  ·  {GuideNpc.AlgorithmName(f.number).ToUpperInvariant()}  ·  ROOM {room.number}:  {SortPuzzleBase.CaseLabel(room.sortCase).ToUpperInvariant()}"
                : f.title;
            if (objective && room != null) ui.SetObjective(room.objective);
        }

        /// Lights of floors far above or below are switched off: they would only shine through floors and ceilings.
        /// Only the lights of the room the floor is on cast shadows (three rooms of shadowed lights at once were too many).
        void UpdateLights(bool force)
        {
            nextLightCheck = Time.time + 0.3f;
            if (player == null) return;
            var room = CurrentRoom != null ? CurrentRoom.root : null;
            if (room != shadowRoom || force)
            {
                shadowRoom = room;
                foreach (var kv in shadowTypes)
                {
                    if (kv.Key == null) continue;
                    bool mine = room != null && kv.Key.transform.IsChildOf(room);
                    var want = mine ? kv.Value : LightShadows.None;
                    if (kv.Key.shadows != want) kv.Key.shadows = want;
                }
            }
            float y = player.transform.position.y;
            foreach (var f in floors)
            {
                if (f.lights == null) continue;
                bool on = Mathf.Abs(y - f.hallMin.y) < 4.2f;
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

        // ------------------------------------------------------------ play-testing: the way to the next room

        /// Waypoints from the room just finished to the inside of the next room (or down the stairs into the next
        /// floor's Room 1, or out to the street). Used by the editor-only play-test tool.
        public Vector3[] PathToNext()
        {
            var f = Current;
            var pts = new List<Vector3>();
            int done = floorDone ? f.rooms.Length - 1 : RoomIndex - 1;      // the room just finished
            if (done < 0) done = 0;
            var from = f.rooms[done];
            pts.Add(from.exitInside);
            pts.Add(from.exitOutside);
            if (!floorDone)
            {
                var next = f.rooms[RoomIndex];
                pts.Add(new Vector3(next.entranceOutside.x, from.exitOutside.y, from.exitOutside.z));
                pts.Add(next.entranceOutside);
                pts.Add(next.entranceInside);
                return pts.ToArray();
            }
            if (f.stairsPath != null && f.stairsPath.Length > 0)
            {
                pts.Add(new Vector3(f.stairsPath[0].x, from.exitOutside.y, from.exitOutside.z));
                pts.AddRange(f.stairsPath);
            }
            if (CurrentIndex + 1 < floors.Length)
            {
                var below = floors[CurrentIndex + 1];
                if (below.rooms.Length > 0)
                {
                    pts.Add(below.rooms[0].entranceOutside);
                    pts.Add(below.rooms[0].entranceInside);
                }
            }
            return pts.ToArray();
        }

        // ------------------------------------------------------------ restart / jump (pause menu, end card)

        /// Restart the room the player is in (or heading to); from the end card, the whole building from the top.
        public void Restart()
        {
            var ui = GameUI.Instance;
            if (ui != null && ui.EndCardOpen)
            {
                GameState.Instance.results.Clear();
                StartFloor = 0;
                StartRoom = 0;
            }
            else
            {
                StartFloor = CurrentIndex;
                StartRoom = floorDone ? Mathf.Max(0, Current.rooms.Length - 1) : RoomIndex;
            }
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }

        /// Start again on another floor (index 0 = the top floor), in its Room 1 (or another room).
        public void JumpTo(int index, int room = 0)
        {
            StartFloor = Mathf.Clamp(index, 0, floors.Length - 1);
            StartRoom = Mathf.Max(0, room);
            SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        }
    }
}
