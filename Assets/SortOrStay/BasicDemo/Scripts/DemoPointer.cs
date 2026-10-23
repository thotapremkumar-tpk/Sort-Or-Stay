using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay.BasicDemo
{
    /// Mouse picking for the fixed-camera demo: click a key or the door.
    /// H = hint, R = restart.
    [RequireComponent(typeof(Camera))]
    public class DemoPointer : MonoBehaviour
    {
        public DemoBubblePuzzle puzzle;
        public DemoHUD hud;
        Camera cam;

        void Awake() { cam = GetComponent<Camera>(); }

        void Update()
        {
            if (puzzle == null) return;

            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.hKey.wasPressedThisFrame) puzzle.ShowHint();
                if (kb.rKey.wasPressedThisFrame) puzzle.Setup();
            }

            var m = Mouse.current;
            if (m == null || !m.leftButton.wasPressedThisFrame) return;
            Vector2 p = m.position.ReadValue();
            if (hud != null && hud.PointerOverUI(p)) return;
            if (!Physics.Raycast(cam.ScreenPointToRay(p), out RaycastHit hit, 50f)) return;

            var key = hit.collider.GetComponentInParent<DemoKey>();
            if (key != null) { puzzle.OnKeyClicked(key); return; }
            if (hit.collider.GetComponentInParent<DemoDoor>() != null) puzzle.OnDoorClicked();
        }
    }
}
