using UnityEngine;
using UnityEngine.InputSystem;

namespace SortOrStay
{
    /// WASD + mouse-look first-person controller on the new Input System.
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        public Transform cam;
        public float eyeHeight = 1.62f;
        public float walkSpeed = 2.8f;
        public float lookSensitivity = 0.09f;      // degrees per pixel of mouse movement
        public float keyLookSpeed = 95f;           // degrees per second with the arrow keys (trackpad-friendly)

        public bool canMove = true;
        public bool canLook = true;

        /// Frame on which a click only grabbed the cursor, so it should not also click a key.
        public static int LockedThisFrame = -1;

        CharacterController cc;
        float pitch, vy;

        void Awake() { cc = GetComponent<CharacterController>(); }

        void Start() { LockCursor(); }

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

        public void ResetLook(float newPitch = 0f) { pitch = newPitch; }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame) FreeCursor();
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                LockCursor();
                LockedThisFrame = Time.frameCount;
            }

            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (canLook && cam != null)
            {
                Vector2 d = locked && mouse != null ? mouse.delta.ReadValue() * lookSensitivity : Vector2.zero;
                if (kb != null)
                {
                    // arrow keys look around too, so the room is playable on a trackpad
                    float kx = (kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.leftArrowKey.isPressed ? 1f : 0f);
                    float ky = (kb.upArrowKey.isPressed ? 1f : 0f) - (kb.downArrowKey.isPressed ? 1f : 0f);
                    d += new Vector2(kx, ky) * keyLookSpeed * Time.deltaTime;
                }
                transform.Rotate(0f, d.x, 0f, Space.World);
                pitch = Mathf.Clamp(pitch - d.y, -80f, 80f);
                cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            Vector3 move = Vector3.zero;
            if (canMove && kb != null)
            {
                float x = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
                float z = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
                move = transform.right * x + transform.forward * z;
                if (move.sqrMagnitude > 1f) move.Normalize();
                move *= walkSpeed * (kb.leftShiftKey.isPressed ? 1.5f : 1f);
            }

            vy = cc.isGrounded ? -2f : vy - 9.81f * Time.deltaTime;
            cc.Move((move + Vector3.up * vy) * Time.deltaTime);
        }
    }
}
