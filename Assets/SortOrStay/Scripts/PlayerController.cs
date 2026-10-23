using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SortOrStay
{
    /// Third-person player built around a realistic human avatar (Microsoft Rocketbox, MIT).
    ///
    /// Two control styles (Tab switches, the choice is remembered):
    ///   Point and click (default) - the cursor stays visible. Click a thing to walk over and use it,
    ///                               click the floor to walk there. Right-drag or arrow keys turn the camera,
    ///                               the scroll wheel zooms. WASD still walks.
    ///   Mouse look                - the old crosshair style: the mouse turns the camera, click uses what the
    ///                               crosshair points at (the avatar still walks over to it by itself).
    /// V cycles first / second / third person, C switches character, Esc pauses.
    /// The root object never rotates: the avatar turns to face where it walks, the camera pivot turns with the mouse.
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        public static PlayerController Instance { get; private set; }

        public const string GestureCheer = "Cheer";
        public const string GestureScratch = "Scratch";
        public const string GestureTryDoor = "TryDoor";
        public const string GestureLookAround = "LookAround";

        /// First = through the eyes, Second = close over the shoulder, Third = full body from further back.
        public enum View { First, Second, Third }

        [Header("Rig")]
        public Transform cameraPivot;          // child of this object; yaw + pitch
        public Camera cam;                     // child of the pivot
        public Transform[] avatars;            // one wrapper per character; only one is active
        public Animator[] animators;
        public Transform[] holdPoints;         // where a carried object sits, per avatar
        public Transform[] rootBones;          // Bip01 per avatar (kept in place while walking)
        public string[] avatarNames = { "Alex", "Maya" };
        public float startYaw;

        [Header("Movement")]
        public float walkSpeed = 1.45f;
        public float runSpeed = 3.3f;
        public float autoWalkSpeed = 2.2f;     // when walking to something you clicked
        public float acceleration = 9f;
        public float turnSpeed = 620f;
        public float lookSensitivity = 0.09f;
        public float dragSensitivity = 0.16f;  // right-drag in point-and-click mode
        public float keyLookSpeed = 95f;

        [Header("Jump")]
        public bool jumpEnabled = true;        // Space jumps
        public float jumpHeight = 0.5f;        // standing hop
        public float runJumpHeight = 0.7f;     // running leap
        public float runJumpBoost = 1.3f;      // extra forward m/s when leaping from a run
        public float jumpWindup = 0.1f;        // the knees bend before a standing take-off (a running leap is near instant)
        public float airControl = 2.5f;        // m/s² of steering in the air: momentum mostly carries you
        public float jumpCooldown = 0.3f;

        [Header("Camera")]
        public float pivotHeight = 1.55f;
        public float camDistance = 1.9f;
        public float minDistance = 1.1f, maxDistance = 4.2f;
        public Vector2 shoulder = new Vector2(0.6f, 0.12f);
        public float collisionRadius = 0.16f;
        public float minPitch = -45f, maxPitch = 60f;
        public float secondDistance = 1.0f;    // second person: close behind the head and shoulders
        public float minSecondDistance = 0.75f, maxSecondDistance = 1.4f;
        public Vector2 secondShoulder = new Vector2(0.35f, 0.12f);
        public View view = View.Third;
        public float firstPersonFov = 70f;     // a little wider than the follow cameras, so close-up things fit

        [Header("State")]
        public bool canMove = true;
        public bool canLook = true;
        public bool cinematic;                 // an intro is driving the camera

        /// Frame on which a click only grabbed the cursor, so it should not also click something in the world.
        public static int LockedThisFrame = -1;
        public static int PreferredAvatar = -1;

        /// Point-and-click (true) or mouse-look with a crosshair (false). Remembered between sessions.
        public static bool PointerMode
        {
            get
            {
                if (pointerMode < 0) pointerMode = PlayerPrefs.GetInt("SortOrStay.PointerMode", 1);
                return pointerMode == 1;
            }
            set
            {
                pointerMode = value ? 1 : 0;
                PlayerPrefs.SetInt("SortOrStay.PointerMode", pointerMode);
            }
        }
        static int pointerMode = -1;

        public int AvatarIndex { get; private set; }
        public float Yaw => yaw;
        public float EyeHeight => eyeHeights != null && AvatarIndex < eyeHeights.Length ? eyeHeights[AvatarIndex] : 1.62f;
        public Vector3 Chest => transform.position + Vector3.up * 1.3f;
        public Transform Body => avatars != null && AvatarIndex < avatars.Length ? avatars[AvatarIndex] : transform;
        public Animator Anim => animators != null && AvatarIndex < animators.Length ? animators[AvatarIndex] : null;
        public string AvatarName => avatarNames != null && AvatarIndex < avatarNames.Length ? avatarNames[AvatarIndex] : "Player";
        public Transform HoldPoint
        {
            get
            {
                if (holdPoints != null && AvatarIndex < holdPoints.Length && holdPoints[AvatarIndex] != null) return holdPoints[AvatarIndex];
                return transform;
            }
        }
        public float Speed { get; private set; }
        public bool IsAutoWalking => autoWalk;
        public bool Paused => GameUI.Instance != null && GameUI.Instance.PauseOpen;
        public bool firstPerson => view == View.First;
        public HandReach Hand { get; private set; }
        /// 0..1: knees bent before take-off and after landing (LegPose drops the hips, the camera dips with them).
        public float JumpCrouch { get; private set; }
        /// 0..1: feet drawn up in the air.
        public float JumpTuck { get; private set; }
        public bool Airborne => airborne;
        /// 0..1: legs and arms split front/back, for a leap taken at a run.
        public float JumpStride { get; private set; }
        /// 0..1: arms thrown up and out while in the air.
        public float JumpArms { get; private set; }

        CharacterController cc;
        float yaw, pitch = 14f, vy, bodyYaw, faceYaw, faceUntil, curSpeed;
        Vector3 moveDir = Vector3.forward;
        float camT = 1f;                       // 0..1 of the full third-person distance (collision)
        float viewBlend = 1f;                  // 0 = first person, 1 = third person
        float viewBlendSpeed = 3.5f;
        float closeK;                          // 0 = third-person distance, 1 = second-person distance
        float baseFov = -1f;

        // first person: the eye keeps a minimum distance from a wall display (Floor 1's key rack) so all of it stays in view
        bool standOffOn;
        Vector3 standOffPoint, standOffNormal, standOffAlong;
        float standOffHalf, standOffMin;

        // jumping
        const float Gravity = 9.81f;
        bool airborne;
        float takeoffAt = -1f;                 // crouching: leave the ground at this time
        float lastGrounded, leftGround, nextJump;
        float landCrouch;                      // knee bend from the last landing, fading out
        bool runningLeap;
        Vector3 airVel;                        // horizontal velocity carried through the air
        float[] eyeHeights;
        Vector3[] rootRest;
        Renderer[][] bodyRenderers;
        bool forceHidden;
        int hideState = -1;                    // 0 visible, 1 shadows only, 2 hidden

        // walking to a clicked point / thing
        bool autoWalk;
        Vector3[] corners = new Vector3[0];
        int corner;
        Vector3 walkGoal;                      // the thing's closest point (or the clicked floor point)
        float arriveDistance;
        Action onArrive;
        float stuckTimer;
        Vector3 stuckPos;
        NavMeshPath path;
        NavMeshDataInstance navInstance;

        static readonly int SpeedHash = Animator.StringToHash("Speed");

        // ------------------------------------------------------------ lifecycle

        void Awake()
        {
            Instance = this;
            cc = GetComponent<CharacterController>();
            yaw = bodyYaw = faceYaw = startYaw;
            moveDir = Quaternion.Euler(0f, startYaw, 0f) * Vector3.forward;
            path = new NavMeshPath();

            int n = avatars != null ? avatars.Length : 0;
            eyeHeights = new float[n];
            rootRest = new Vector3[n];
            bodyRenderers = new Renderer[n][];
            for (int i = 0; i < n; i++)
            {
                if (avatars[i] == null) continue;
                bodyRenderers[i] = avatars[i].GetComponentsInChildren<Renderer>(true);
                eyeHeights[i] = 1.62f;
                var head = FindDeep(avatars[i], "Bip01 Head");
                if (head != null) eyeHeights[i] = Mathf.Clamp(head.position.y - transform.position.y + 0.07f, 1.35f, 1.85f);
                if (rootBones != null && i < rootBones.Length && rootBones[i] != null)
                    rootRest[i] = avatars[i].InverseTransformPoint(rootBones[i].position);
            }

            int pref = PreferredAvatar >= 0 ? PreferredAvatar : PlayerPrefs.GetInt("SortOrStay.Avatar", 0);
            SetAvatar(n > 0 ? Mathf.Clamp(pref, 0, n - 1) : 0, false);
            ApplyBodyRotation();

            Hand = GetComponent<HandReach>();
            if (Hand == null) Hand = gameObject.AddComponent<HandReach>();
            if (GetComponent<LegPose>() == null) gameObject.AddComponent<LegPose>();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (navInstance.valid) navInstance.Remove();
        }

        void Start()
        {
            ApplyCursorForMode();
            RebuildNavMesh();
            SnapCamera();
        }

        // ------------------------------------------------------------ public API

        public static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public static void FreeCursor()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// Free cursor in point-and-click mode, locked cursor in mouse-look mode.
        public static void ApplyCursorForMode()
        {
            if (PointerMode) FreeCursor(); else LockCursor();
        }

        public void TogglePointerMode()
        {
            PointerMode = !PointerMode;
            ApplyCursorForMode();
            LockedThisFrame = Time.frameCount;
            if (GameUI.Instance != null)
                GameUI.Instance.Info(PointerMode ? "Point and click:  click things to use them, right-drag to turn" : "Mouse look:  aim with the crosshair, click to use");
        }

        public void SetLook(float newYaw, float newPitch)
        {
            yaw = newYaw;
            pitch = Mathf.Clamp(newPitch, minPitch, maxPitch);
        }

        /// Turn the body toward a point for a moment (used when the player clicks something).
        public void FaceTowards(Vector3 worldPoint)
        {
            Vector3 d = worldPoint - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude < 0.0004f) return;
            faceYaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
            faceUntil = Time.time + 0.9f;
        }

        public void Gesture(string trigger)
        {
            var a = Anim;
            if (a == null || a.runtimeAnimatorController == null || !a.isActiveAndEnabled) return;
            a.ResetTrigger(GestureCheer);
            a.ResetTrigger(GestureScratch);
            a.ResetTrigger(GestureTryDoor);
            a.ResetTrigger(GestureLookAround);
            a.SetTrigger(trigger);
        }

        public void SetAvatarHidden(bool hidden)
        {
            forceHidden = hidden;
            hideState = -1;
            UpdateVisibility();
        }

        /// The camera starts at the eyes and glides back over the shoulder.
        public void PullBackFromEye(float seconds)
        {
            view = View.Third;
            closeK = 0f;
            viewBlend = 0f;
            viewBlendSpeed = 1f / Mathf.Max(0.05f, seconds);
        }

        /// V: first -> second -> third person -> first ...
        public void ToggleView()
        {
            view = view == View.First ? View.Second : view == View.Second ? View.Third : View.First;
            viewBlendSpeed = 3.5f;
            if (GameUI.Instance != null) GameUI.Instance.Info(ViewName + " view");
        }

        public string ViewName => view == View.First ? "First-person" : view == View.Second ? "Second-person" : "Third-person";

        public void CycleAvatar()
        {
            if (avatars == null || avatars.Length < 2) return;
            SetAvatar((AvatarIndex + 1) % avatars.Length, true);
            if (GameUI.Instance != null) GameUI.Instance.Info("Now playing as " + AvatarName);
        }

        public void SetAvatar(int index, bool remember)
        {
            if (avatars == null || avatars.Length == 0) return;
            AvatarIndex = Mathf.Clamp(index, 0, avatars.Length - 1);
            for (int i = 0; i < avatars.Length; i++)
                if (avatars[i] != null) avatars[i].gameObject.SetActive(i == AvatarIndex);
            hideState = -1;
            ApplyBodyRotation();
            if (remember)
            {
                PreferredAvatar = AvatarIndex;
                PlayerPrefs.SetInt("SortOrStay.Avatar", AvatarIndex);
            }
        }

        /// Where the third-person camera wants to be right now (with wall collision), ignoring smoothing.
        public void ComputeCameraPose(out Vector3 pos, out Quaternion rot)
        {
            Vector3 pivot = transform.position + Vector3.up * pivotHeight;
            rot = Quaternion.Euler(pitch, yaw, 0f);
            float len;
            Vector3 dir = ThirdPersonDir(rot, out len);
            float allowed = CastDistance(pivot, dir, len);
            pos = pivot + dir * allowed;
        }

        /// Jump the camera straight to its resting pose (after a cutscene).
        public void SnapCamera()
        {
            if (cam == null) return;
            viewBlend = firstPerson ? 0f : 1f;
            closeK = view == View.Second ? 1f : 0f;
            Vector3 pivot = transform.position + Vector3.up * pivotHeight;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            float len;
            Vector3 dir = ThirdPersonDir(rot, out len);
            camT = len > 0.001f ? CastDistance(pivot, dir, len) / len : 1f;
            if (!cinematic) PlaceCamera(Time.deltaTime, true);
        }

        /// First person only: never let the eye get closer than 'minDistance' to a flat display (a line of
        /// 'halfLength' each side of 'center' along 'along', facing 'normal'), so the whole display stays on screen.
        public void SetViewStandOff(Vector3 center, Vector3 normal, Vector3 along, float halfLength, float minDistance)
        {
            normal.y = 0f; along.y = 0f;
            standOffPoint = center;
            standOffNormal = normal.normalized;
            standOffAlong = along.normalized;
            standOffHalf = halfLength;
            standOffMin = minDistance;
            standOffOn = true;
        }

        Vector3 StandOff(Vector3 eye)
        {
            if (!standOffOn) return eye;
            Vector3 v = eye - standOffPoint;
            float d = Vector3.Dot(v, standOffNormal);
            float lateral = Mathf.Abs(Vector3.Dot(v, standOffAlong));
            if (d >= standOffMin || d < -0.3f) return eye;
            float edge = Mathf.Clamp01((standOffHalf + 0.9f - lateral) / 0.5f);    // fades out past the ends of the display
            return eye + standOffNormal * ((standOffMin - d) * edge);
        }

        // ------------------------------------------------------------ walking to a target

        /// Walk (around obstacles) until the avatar is within 'arriveDist' metres of 'goal' (measured flat
        /// on the floor), then call 'arrive'. Typing WASD cancels it.
        public void WalkTo(Vector3 goal, float arriveDist, Action arrive)
        {
            walkGoal = goal;
            arriveDistance = arriveDist;
            onArrive = arrive;

            Vector3 start = transform.position;
            Vector3 end = new Vector3(goal.x, start.y, goal.z);
            corners = null;
            if (NavMesh.SamplePosition(start, out NavMeshHit hs, 1.2f, NavMesh.AllAreas)
                && NavMesh.SamplePosition(end, out NavMeshHit he, 2.5f, NavMesh.AllAreas)
                && NavMesh.CalculatePath(hs.position, he.position, NavMesh.AllAreas, path)
                && path.status != NavMeshPathStatus.PathInvalid && path.corners.Length > 0)
            {
                corners = path.corners;
            }
            if (corners == null) corners = new[] { start, end };     // no navmesh: straight line

            corner = corners.Length > 1 ? 1 : 0;
            autoWalk = true;
            stuckTimer = 0f;
            stuckPos = start;
        }

        public void StopWalking()
        {
            autoWalk = false;
            onArrive = null;
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }

        void Arrive()
        {
            var cb = onArrive;
            autoWalk = false;
            onArrive = null;
            cb?.Invoke();
        }

        void GiveUp()
        {
            bool hadTarget = onArrive != null;
            StopWalking();
            if (hadTarget && GameUI.Instance != null) GameUI.Instance.Info("Can't get any closer to that.");
        }

        /// One frame of path following. Returns the direction to walk in (or zero).
        Vector3 AutoWalkStep()
        {
            Vector3 pos = transform.position;
            if (onArrive != null && Flat(pos, walkGoal) <= arriveDistance) { Arrive(); return Vector3.zero; }

            while (corner < corners.Length && Flat(pos, corners[corner]) < 0.18f) corner++;
            if (corner >= corners.Length)
            {
                if (onArrive == null) { StopWalking(); return Vector3.zero; }
                if (Flat(pos, walkGoal) <= arriveDistance + 0.9f) Arrive(); else GiveUp();
                return Vector3.zero;
            }

            stuckTimer += Time.deltaTime;
            if (stuckTimer > 0.6f)
            {
                if ((pos - stuckPos).sqrMagnitude < 0.06f * 0.06f)
                {
                    if (onArrive != null && Flat(pos, walkGoal) <= arriveDistance + 1.0f) Arrive();
                    else GiveUp();
                    return Vector3.zero;
                }
                stuckTimer = 0f;
                stuckPos = pos;
            }

            Vector3 dir = corners[corner] - pos;
            dir.y = 0f;
            return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
        }

        /// Builds a walkable-floor map of the room from its colliders, so clicks can walk around tables and crates.
        public void RebuildNavMesh()
        {
            try
            {
                if (navInstance.valid) navInstance.Remove();
                var bounds = new Bounds(transform.position, new Vector3(60f, 24f, 60f));
                var sources = new List<NavMeshBuildSource>();
                NavMeshBuilder.CollectSources(bounds, Physics.DefaultRaycastLayers, NavMeshCollectGeometry.PhysicsColliders, 0,
                                              new List<NavMeshBuildMarkup>(), sources);
                sources.RemoveAll(s => s.component is Collider c && (c.isTrigger || !c.enabled));
                var settings = NavMesh.GetSettingsByID(0);
                settings.agentRadius = 0.3f;
                settings.agentHeight = 1.8f;
                settings.agentClimb = 0.32f;
                settings.agentSlope = 46f;
                var data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                if (data != null) navInstance = NavMesh.AddNavMeshData(data);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Sort or Stay: could not build the walk map, clicks will walk in straight lines. " + e.Message);
            }
        }

        // ------------------------------------------------------------ update

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var ui = GameUI.Instance;
            bool pointer = PointerMode;

            // Esc pauses / resumes
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                if (ui != null && ui.PauseMenuEnabled) ui.SetPaused(!ui.Paused);
                else if (!pointer) { if (Cursor.lockState == CursorLockMode.Locked) FreeCursor(); else LockCursor(); }
            }
            bool paused = Paused;
            bool popup = ui != null && (ui.PopupOpen || ui.EndCardOpen);

            // mouse-look mode: a click on the game grabs the cursor again
            if (!pointer && !paused && mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked
                && !(ui != null && ui.EndCardOpen))
            {
                LockCursor();
                LockedThisFrame = Time.frameCount;
            }

            if (kb != null && !paused && !cinematic && !popup)
            {
                if (kb.vKey.wasPressedThisFrame) ToggleView();
                if (kb.cKey.wasPressedThisFrame) CycleAvatar();
                if (kb.tabKey.wasPressedThisFrame) TogglePointerMode();
            }

            // ---- look
            if (canLook && !cinematic && !paused)
            {
                Vector2 d = Vector2.zero;
                if (mouse != null)
                {
                    if (!pointer && Cursor.lockState == CursorLockMode.Locked) d = mouse.delta.ReadValue() * lookSensitivity;
                    else if (pointer && mouse.rightButton.isPressed) d = mouse.delta.ReadValue() * dragSensitivity;

                    float scroll = mouse.scroll.ReadValue().y;
                    if (Mathf.Abs(scroll) > 0.01f && !popup)
                    {
                        if (view == View.Second) secondDistance = Mathf.Clamp(secondDistance - scroll * 0.0025f, minSecondDistance, maxSecondDistance);
                        else camDistance = Mathf.Clamp(camDistance - scroll * 0.0025f, minDistance, maxDistance);
                    }
                }
                if (kb != null)
                {
                    float kx = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                    float ky = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                    d += new Vector2(kx, ky) * keyLookSpeed * Time.deltaTime;
                }
                yaw += d.x;
                pitch = Mathf.Clamp(pitch - d.y, minPitch, maxPitch);
            }

            // ---- move (camera-relative), or follow the path to something that was clicked
            Vector3 wish = Vector3.zero;
            bool run = false;
            bool driving = false;
            if (canMove && !cinematic && !paused && kb != null)
            {
                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                wish = q * new Vector3(x, 0f, z);
                if (wish.sqrMagnitude > 1f) wish.Normalize();
                run = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            if (wish.sqrMagnitude > 0.0001f) { if (autoWalk) StopWalking(); }
            else if (autoWalk && canMove && !cinematic && !paused) { wish = AutoWalkStep(); driving = true; }
            else if (autoWalk && (cinematic || !canMove)) StopWalking();

            float target = wish.magnitude * (driving ? autoWalkSpeed : run ? runSpeed : walkSpeed);
            curSpeed = Mathf.MoveTowards(curSpeed, target, acceleration * Time.deltaTime);
            if (wish.sqrMagnitude > 0.0001f) moveDir = wish.normalized;

            // ---- jump: bend the knees, push off, carry momentum through the air, absorb the landing
            float dt = Time.deltaTime;
            bool grounded = cc.isGrounded;
            if (grounded && !airborne) lastGrounded = Time.time;
            bool jumpPressed = jumpEnabled && canMove && !cinematic && !paused && !popup && kb != null && kb.spaceKey.wasPressedThisFrame
                               && !(ui != null && ui.LastConsumeFrame == Time.frameCount);
            if (jumpPressed && !airborne && takeoffAt < 0f && Time.time >= nextJump && Time.time - lastGrounded < 0.12f)
            {
                runningLeap = curSpeed > walkSpeed + 0.4f;
                takeoffAt = Time.time + (runningLeap ? jumpWindup * 0.4f : jumpWindup);
                if (autoWalk) StopWalking();
            }

            if (grounded && !airborne) vy = -2f;
            else vy -= Gravity * dt;

            float windup = 0f;
            if (takeoffAt >= 0f)
            {
                windup = Mathf.Clamp01(1f - (takeoffAt - Time.time) / jumpWindup) * (runningLeap ? 0.5f : 1f);
                if (Time.time >= takeoffAt)
                {
                    takeoffAt = -1f;
                    windup = 0f;
                    vy = Mathf.Sqrt(2f * Gravity * (runningLeap ? runJumpHeight : jumpHeight));
                    airVel = moveDir * (curSpeed + (runningLeap ? runJumpBoost : 0f));   // a running leap carries you forward
                    airborne = true;
                    leftGround = Time.time;
                    ProceduralSfx.Play(ProceduralSfx.Thud, transform.position, 0.12f);
                }
            }

            Vector3 horiz;
            if (airborne)
            {
                Vector3 wishVel = wish * (run ? runSpeed : walkSpeed);
                airVel = Vector3.MoveTowards(airVel, wishVel, airControl * dt);
                horiz = airVel;
                curSpeed = airVel.magnitude;
                if (curSpeed > 0.01f) moveDir = airVel / curSpeed;
            }
            else horiz = moveDir * curSpeed * (1f - 0.55f * landCrouch);     // a hard landing slows you for a moment

            float fallSpeed = -vy;
            cc.Move((horiz + Vector3.up * vy) * dt);

            if (airborne && cc.isGrounded && vy < 0f && Time.time - leftGround > 0.05f)
            {
                airborne = false;
                float impact = Mathf.Clamp01((fallSpeed - 1f) / 4f);
                landCrouch = Mathf.Max(landCrouch, 0.35f + 0.65f * impact);
                nextJump = Time.time + jumpCooldown;
                vy = -2f;
                ProceduralSfx.Play(ProceduralSfx.Thud, transform.position, 0.25f + 0.5f * impact);
            }
            landCrouch = Mathf.MoveTowards(landCrouch, 0f, dt / 0.32f);
            JumpCrouch = Mathf.Max(Mathf.SmoothStep(0f, 1f, windup), landCrouch);
            // feet draw up on the way up and reach down again before touching the ground
            JumpTuck = Mathf.MoveTowards(JumpTuck, airborne ? (vy > -1.2f ? 1f : 0.25f) : 0f, dt * 7f);
            JumpStride = Mathf.MoveTowards(JumpStride, airborne && runningLeap ? (vy > -2f ? 1f : 0.4f) : 0f, dt * 6f);
            JumpArms = Mathf.MoveTowards(JumpArms, airborne ? 1f : 0f, dt * (airborne ? 9f : 4f));

            Vector3 hv = cc.velocity; hv.y = 0f;
            Speed = Mathf.Min(hv.magnitude, curSpeed + 0.05f);

            // ---- body facing
            float targetYaw = bodyYaw;
            if (firstPerson && !cinematic) targetYaw = yaw;
            else if (wish.sqrMagnitude > 0.0001f) targetYaw = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
            else if (Time.time < faceUntil) targetYaw = faceYaw;
            bodyYaw = Mathf.MoveTowardsAngle(bodyYaw, targetYaw, turnSpeed * Time.deltaTime);
            ApplyBodyRotation();

            var a = Anim;
            if (a != null && a.isActiveAndEnabled) a.SetFloat(SpeedHash, airborne ? 0f : Speed, 0.08f, Time.deltaTime);   // legs stop striding in the air
        }

        void LateUpdate()
        {
            // Keep the hips over the feet: the walk and run clips carry forward motion we do not want.
            if (rootBones != null && AvatarIndex < rootBones.Length && rootBones[AvatarIndex] != null && Speed > 0.05f)
            {
                Transform rb = rootBones[AvatarIndex];
                Transform body = Body;
                Vector3 local = body.InverseTransformPoint(rb.position);
                Vector3 rest = rootRest[AvatarIndex];
                rb.position = body.TransformPoint(new Vector3(rest.x, local.y, rest.z));
            }

            if (!cinematic) PlaceCamera(Time.deltaTime, false);
            UpdateVisibility();
        }

        // ------------------------------------------------------------ camera

        Vector3 ThirdPersonDir(Quaternion rot, out float len)
        {
            float k = Mathf.SmoothStep(0f, 1f, closeK);
            Vector2 sh = Vector2.Lerp(shoulder, secondShoulder, k);
            Vector3 offset = rot * new Vector3(sh.x, sh.y, -Mathf.Lerp(camDistance, secondDistance, k));
            len = offset.magnitude;
            return len > 0.0001f ? offset / len : Vector3.back;
        }

        float CastDistance(Vector3 origin, Vector3 dir, float len)
        {
            if (Physics.SphereCast(origin, collisionRadius, dir, out RaycastHit hit, len, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                return Mathf.Max(0.12f, hit.distance - 0.03f);
            return len;
        }

        void PlaceCamera(float dt, bool snap)
        {
            if (cam == null) return;
            closeK = Mathf.MoveTowards(closeK, view == View.Second ? 1f : 0f, dt * 3.5f);
            Vector3 dip = Vector3.down * (JumpCrouch * 0.09f);           // the view sinks with the knees
            Vector3 pivot = transform.position + Vector3.up * pivotHeight + dip;
            Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
            if (cameraPivot != null) { cameraPivot.position = pivot; cameraPivot.rotation = rot; }

            float len;
            Vector3 dir = ThirdPersonDir(rot, out len);
            float allowedT = len > 0.001f ? CastDistance(pivot, dir, len) / len : 1f;
            if (snap || allowedT < camT) camT = allowedT;                   // pull in at once
            else camT = Mathf.MoveTowards(camT, allowedT, dt * 1.6f);       // ease back out

            viewBlend = Mathf.MoveTowards(viewBlend, firstPerson ? 0f : 1f, dt * viewBlendSpeed);
            if (Mathf.Approximately(viewBlend, firstPerson ? 0f : 1f)) viewBlendSpeed = 3.5f;
            float k = Mathf.SmoothStep(0f, 1f, viewBlend);

            Vector3 eye = StandOff(transform.position + Vector3.up * EyeHeight + dip + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, 0.12f));
            Vector3 third = pivot + dir * (len * camT);
            cam.transform.position = Vector3.Lerp(eye, third, k);
            if (baseFov < 0f) baseFov = cam.fieldOfView;
            cam.fieldOfView = Mathf.Lerp(firstPersonFov, baseFov, k);
            cam.transform.rotation = rot;
        }

        void UpdateVisibility()
        {
            if (bodyRenderers == null || AvatarIndex >= bodyRenderers.Length || bodyRenderers[AvatarIndex] == null) return;
            int state = 0;
            if (forceHidden) state = 2;
            else if (cam != null)
            {
                Vector3 head = transform.position + Vector3.up * (EyeHeight - 0.05f);
                if (Vector3.Distance(cam.transform.position, head) < 0.42f || (firstPerson && viewBlend < 0.35f)) state = 1;
            }
            if (state == hideState) return;
            hideState = state;
            foreach (var r in bodyRenderers[AvatarIndex])
            {
                if (r == null) continue;
                r.enabled = state != 2;
                r.shadowCastingMode = state == 1 ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }
        }

        void ApplyBodyRotation()
        {
            if (avatars == null) return;
            Quaternion q = Quaternion.Euler(0f, bodyYaw, 0f);
            foreach (var t in avatars) if (t != null) t.rotation = q;
        }

        static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var r = FindDeep(root.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }
    }
}
