using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// Picks what the player means and uses it.
    ///
    /// Point-and-click: whatever is under the mouse cursor. Mouse-look: whatever the crosshair points at.
    /// Picking is forgiving: if the ray lands on something that can't be used (a shelf, a scale plate),
    /// the nearest usable thing within a few centimetres is chosen instead. The chosen thing gets a bouncing
    /// marker above it. Second-person view extends the reach for puzzle controls.
    /// Puzzles also call Use() for number-key shortcuts.
    public class PlayerInteractor : MonoBehaviour
    {
        public static PlayerInteractor Instance { get; private set; }

        public Camera cam;
        public PlayerController controller;
        public Transform marker;                 // bouncing chevron for hovered objects other than doors
        public float useDistance = 1.7f;         // flat distance at which a click acts at once
        public float approachDistance = 1.05f;   // otherwise walk until this close
        public float pickRadius = 0.2f;          // "near miss" radius
        const float SecondPersonPuzzleReach = 3f;

        public IInteractable Current { get; private set; }

        Collider currentCol;
        Component pendingTarget;                 // walking over to use this
        Collider pendingCol;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            var ui = GameUI.Instance;
            bool pointer = PlayerController.PointerMode;
            if (ui != null) { ui.prompt = ""; ui.promptAtCursor = pointer; }
            Current = null;
            currentCol = null;
            if (cam == null || controller == null) return;
            if (!controller.canLook || controller.cinematic || controller.Paused) return;
            if (ui != null && (ui.PopupOpen || ui.EndCardOpen)) return;

            var mouse = Mouse.current;
            Ray ray;
            if (pointer)
            {
                if (mouse == null) return;
                ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            }
            else ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

            Pick(ray, out IInteractable it, out Collider col, out RaycastHit _);
            if (it != null)
            {
                Current = it;
                currentCol = col;
                if (ui != null)
                {
                    string p = it.Prompt;
                    float d = Flat(transform.position, ClosestPoint(col, transform.position));
                    float use = InteractionDistance(it);
                    if (!string.IsNullOrEmpty(p) && d > use && !IsDoor(it)) p += "   (too far:  walk up to it)";
                    ui.prompt = p;
                }
            }

            bool click = mouse != null && mouse.leftButton.wasPressedThisFrame;
            bool keyUse = Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame;
            if (!click && !keyUse) return;
            if (PlayerController.LockedThisFrame == Time.frameCount) return;   // that click only grabbed the cursor
            if (!pointer && Cursor.lockState != CursorLockMode.Locked) return;
            if (ui != null && (ui.PopupOpen || ui.LastConsumeFrame == Time.frameCount)) return;
            if (pointer && click && ui != null && ui.IsOverHud(mouse.position.ReadValue())) return;

            if (it != null) { Use(it, col); return; }
            // (clicking the floor or the stairs does nothing: no walking by click)
        }

        /// Doors are only tried from where the player stands: a click on a door never walks the avatar to it.
        static bool IsDoor(IInteractable it) => it is Door || it is RoomGate;

        float InteractionDistance(IInteractable it)
        {
            float reach = it is IHandReach hand && hand.ReachDistance > 0f ? hand.ReachDistance : useDistance;
            if (!WalkOver && controller != null && controller.view == PlayerController.View.Second &&
                (it is SortItem || it is InsertSlot || it is PushButton || it is Terminal))
                return Mathf.Max(reach, SecondPersonPuzzleReach);
            return reach;
        }

        /// A click never walks the avatar anywhere: the player walks (W A S D) and uses what is within reach.
        /// (Only the editor's hands-on play-test walks over to things by itself.)
        static bool WalkOver
        {
            get
            {
#if UNITY_EDITOR
                return DevAutoPlay.On && DevAutoPlay.Hands;
#else
                return false;
#endif
            }
        }

        void LateUpdate()
        {
            if (marker == null) return;
            Collider show = currentCol;
            if (show == null && pendingTarget != null && controller != null && controller.IsAutoWalking) show = pendingCol;
            if (IsDoor(Current) || show == null || !show.enabled || !show.gameObject.activeInHierarchy)
            {
                if (marker.gameObject.activeSelf) marker.gameObject.SetActive(false);
                return;
            }
            if (!marker.gameObject.activeSelf) marker.gameObject.SetActive(true);
            Bounds b = show.bounds;
            float bob = 0.04f * Mathf.Sin(Time.time * 6f);
            marker.position = new Vector3(b.center.x, b.max.y + 0.16f + bob, b.center.z);
            if (cam != null) marker.rotation = Quaternion.Euler(0f, cam.transform.eulerAngles.y, 0f);
        }

        /// Ray first; if what it hits can't be used, take the closest usable thing near the hit point.
        bool Pick(Ray ray, out IInteractable it, out Collider col, out RaycastHit hit)
        {
            it = null;
            col = null;
            if (!Physics.Raycast(ray, out hit, 80f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;

            var direct = hit.collider.GetComponentInParent<IInteractable>();
            if (direct != null && direct.CanInteract)
            {
                it = direct;
                col = hit.collider;
                return true;
            }
            if (hit.normal.y > 0.6f) return true;       // the floor, a landing or a stair: nothing to use there

            var near = Physics.OverlapSphere(hit.point, pickRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var c in near)
            {
                var cand = c.GetComponentInParent<IInteractable>();
                if (cand == null || !cand.CanInteract) continue;
                float d = (ClosestPoint(c, hit.point) - hit.point).sqrMagnitude;
                if (d < best) { best = d; it = cand; col = c; }
            }
            return true;
        }

        /// Use a thing: at once if it is close, otherwise walk over first.
        public void Use(IInteractable it, Collider col)
        {
            if (it == null || col == null || !it.CanInteract) return;
            float use = InteractionDistance(it), approach = approachDistance;
            if (it is IHandReach hr && hr.ReachDistance > 0f) approach = Mathf.Min(approach, hr.ReachDistance * 0.75f);
            Vector3 p = ClosestPoint(col, transform.position);
            if (IsDoor(it))
            {
                pendingTarget = null;
                it.Interact();              // rattles / says why it is locked, from here
                return;
            }
            if (Flat(transform.position, p) <= use || controller == null || !controller.canMove)
            {
                pendingTarget = null;
                if (controller != null) { controller.StopWalking(); controller.FaceTowards(p); }
                it.Interact();
                return;
            }
            if (!WalkOver)
            {
                // too far: the avatar does not walk there by itself
                pendingTarget = null;
                if (controller != null) controller.FaceTowards(p);
                if (GameUI.Instance != null) GameUI.Instance.Flash("Too far away.  Walk up to it first (W A S D), then click it.");
                return;
            }

            var comp = it as Component;
            pendingTarget = comp;
            pendingCol = col;
            controller.WalkTo(p, approach, () =>
            {
                if (pendingTarget != comp) return;
                pendingTarget = null;
                if (comp == null || !it.CanInteract) return;
                Vector3 q = ClosestPoint(col, transform.position);
                controller.FaceTowards(q);
                it.Interact();
            });
        }

        /// Keyboard shortcut: use this object (walking over if needed).
        public void Use(Component target)
        {
            if (target == null || !target.gameObject.activeInHierarchy) return;
            var it = target.GetComponent<IInteractable>() ?? target.GetComponentInParent<IInteractable>();
            var col = target.GetComponent<Collider>() ?? target.GetComponentInChildren<Collider>();
            if (it == null || col == null) return;
            if (!it.CanInteract) return;
            Use(it, col);
        }

        static Vector3 ClosestPoint(Collider c, Vector3 from)
        {
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider || (c is MeshCollider mc && mc.convex))
                return c.ClosestPoint(from);
            return c.bounds.ClosestPoint(from);
        }

        static float Flat(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
